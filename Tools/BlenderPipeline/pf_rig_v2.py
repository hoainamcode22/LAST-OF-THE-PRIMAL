"""PLAYER_Survivor v2 rig: re-pose the MakeHuman-based character to a clean A-pose (legs under the hips, elbows almost
straight), then build the game humanoid rig (Unity Humanoid names, 3 bones per finger, toes, weapon sockets) and skin
the body + all attached parts."""
import bpy, bmesh, math, json
from mathutils import Vector, Matrix, Quaternion
from mathutils.bvhtree import BVHTree
import pf_ops, pf_mh

STEM = "survivor_v4"
FACE_PARTS = ("PLR2_Eyes", "PLR2_Mouth", "PLR2_Lashes", "PLR2_Brows", "PLR2_Beard")
HEAD_PARTS = ("PLR2_Hair",)
BODY_PARTS = ("PLR2_Kilt", "PLR2_Belt", "PLR2_Pelt", "PLR2_PeltFur", "PLR2_PeltFurTop", "PLR2_Strap", "PLR2_Wrap_l", "PLR2_Wrap_r",
              "PLR2_Necklace", "PLR2_HairTie")

def V(p): return Vector(p)

# ------------------------------------------------------------------ helpers
def apply_mods_keep_shapes(o):
    """apply the modifier stack to the basis and every shape key (armature deformation is the same for all keys)"""
    import numpy as np
    me = o.data; n = len(me.vertices)
    keys = me.shape_keys.key_blocks if me.shape_keys else None
    if keys:
        vals = [k.value for k in keys]
        for k in keys[1:]: k.value = 0.0
    dg = bpy.context.evaluated_depsgraph_get()
    ev = o.evaluated_get(dg); em = ev.to_mesh()
    if len(em.vertices) != n:
        ev.to_mesh_clear(); raise RuntimeError(f"{o.name}: modifier changed the vertex count")
    new = np.empty(n * 3, np.float32); em.vertices.foreach_get("co", new); ev.to_mesh_clear()
    old = np.empty(n * 3, np.float32); me.vertices.foreach_get("co", old)
    D = new - old
    o.modifiers.clear()
    if keys:
        for k in keys:
            kc = np.empty(n * 3, np.float32); k.data.foreach_get("co", kc); k.data.foreach_set("co", kc + D)
        for k, v in zip(keys, vals): k.value = v
    me.vertices.foreach_set("co", new); me.update()

def bake_modifiers(o):
    """apply solidify etc. so later armature baking keeps the vertex count"""
    keep = [m for m in o.modifiers if m.type not in ('ARMATURE',)]
    if not keep: return
    for m in list(o.modifiers):
        if m.type == 'ARMATURE': o.modifiers.remove(m)
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(o.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
    old = o.data; o.modifiers.clear(); o.data = me; bpy.data.meshes.remove(old)

def transfer_weights(target, source, arm_obj):
    """nearest-face interpolated vertex weights from the skinned body"""
    for m in list(target.modifiers):
        if m.type in ('ARMATURE', 'DATA_TRANSFER'): target.modifiers.remove(m)
    target.vertex_groups.clear()
    for g in source.vertex_groups: target.vertex_groups.new(name=g.name)
    dt = target.modifiers.new("WT", 'DATA_TRANSFER'); dt.object = source
    dt.use_vert_data = True; dt.data_types_verts = {'VGROUP_WEIGHTS'}; dt.vert_mapping = 'POLYINTERP_NEAREST'
    dt.layers_vgroup_select_src = 'ALL'; dt.layers_vgroup_select_dst = 'NAME'
    pf_ops.run(bpy.ops.object.datalayer_transfer if False else bpy.ops.object.modifier_apply, [target], active=target, modifier="WT")
    md = target.modifiers.new("Armature", 'ARMATURE'); md.object = arm_obj
    target.parent = arm_obj; target.matrix_parent_inverse = arm_obj.matrix_world.inverted()

def rigid(target, arm_obj, bone):
    for m in list(target.modifiers):
        if m.type == 'ARMATURE': target.modifiers.remove(m)
    target.vertex_groups.clear()
    vg = target.vertex_groups.new(name=bone); vg.add(list(range(len(target.data.vertices))), 1.0, 'REPLACE')
    md = target.modifiers.new("Armature", 'ARMATURE'); md.object = arm_obj
    target.parent = arm_obj; target.matrix_parent_inverse = arm_obj.matrix_world.inverted()

def _armature(name, specs):
    old = bpy.data.objects.get(name)
    if old: bpy.data.objects.remove(old, do_unlink=True)
    arm = bpy.data.armatures.new(name); obj = bpy.data.objects.new(name, arm)
    bpy.context.scene.collection.objects.link(obj); arm.display_type = 'STICK'
    pf_ops.mode_set(obj, 'EDIT')
    eb = {}
    for nm, par, h, t, align, deform in specs:
        b = arm.edit_bones.new(nm); b.head = h; b.tail = t; b.align_roll(align); b.use_deform = deform
        if par:
            b.parent = eb[par]; b.use_connect = (eb[par].tail - b.head).length < 1e-5
        eb[nm] = b
    pf_ops.mode_set(obj, 'OBJECT')
    for pb in obj.pose.bones: pb.rotation_mode = 'QUATERNION'
    return obj

# ------------------------------------------------------------------ 1. re-pose
def repose(J, straighten_to=8.0):
    up = V((0, 0, 1)); fwd = V((0, -1, 0))
    specs = [("T_Body", None, V((0, 0.0, 0.93)), V((0, 0.0, 1.58)), fwd, True)]
    for s, sd in (("l", "L"), ("r", "R")):
        mid = V(J[f"{s}-finger-3-1"])
        specs += [
            (f"T_Thigh_{sd}", "T_Body", V(J[f"{s}-upper-leg"]), V(J[f"{s}-knee"]), fwd, True),
            (f"T_Calf_{sd}", f"T_Thigh_{sd}", V(J[f"{s}-knee"]), V(J[f"{s}-ankle"]), fwd, True),
            (f"T_Foot_{sd}", f"T_Calf_{sd}", V(J[f"{s}-ankle"]), V(J[f"{s}-foot-2"]), up, True),
            (f"T_UpperArm_{sd}", "T_Body", V(J[f"{s}-shoulder"]), V(J[f"{s}-elbow"]), fwd, True),
            (f"T_LowerArm_{sd}", f"T_UpperArm_{sd}", V(J[f"{s}-elbow"]), V(J[f"{s}-hand"]), fwd, True),
            (f"T_Hand_{sd}", f"T_LowerArm_{sd}", V(J[f"{s}-hand"]), mid, up, True),
        ]
    T = _armature("_T_Repose", specs)
    body = bpy.data.objects["PLR2_Body"]
    for m in list(body.modifiers):
        if m.type == 'ARMATURE': body.modifiers.remove(m)
    body.vertex_groups.clear()
    pf_ops.run(bpy.ops.object.parent_set, [body, T], active=T, type='ARMATURE_AUTO')
    parts = [bpy.data.objects[n] for n in BODY_PARTS if n in bpy.data.objects]
    for p in parts:
        bake_modifiers(p)
        transfer_weights(p, body, T)
    # pose: legs under the hips (feet kept flat), elbows almost straight
    pb = T.pose.bones
    def rot_world(bone, R, pivot):
        m = pb[bone].matrix
        pb[bone].matrix = Matrix.Translation(pivot) @ R.to_4x4() @ Matrix.Translation(-pivot) @ m
        bpy.context.view_layer.update()
    for s, sd, sgn in (("l", "L", 1), ("r", "R", -1)):
        hip = V(J[f"{s}-upper-leg"]); ank = V(J[f"{s}-ankle"])
        v = ank - hip; target_x = sgn * 0.0015
        th = math.atan2(v.x, -v.z) - math.atan2(target_x, -v.z)
        foot_m = pb[f"T_Foot_{sd}"].matrix.copy()
        rot_world(f"T_Thigh_{sd}", Matrix.Rotation(th, 3, 'Y'), hip)
        # keep the foot's world orientation (sole flat), only follow the new ankle position
        newank = pb[f"T_Foot_{sd}"].head.copy()
        fm = foot_m.copy(); fm.translation = newank; pb[f"T_Foot_{sd}"].matrix = fm; bpy.context.view_layer.update()
        sh = V(J[f"{s}-shoulder"]); el = V(J[f"{s}-elbow"]); wr = V(J[f"{s}-hand"])
        ua = (el - sh).normalized(); fa = (wr - el).normalized()
        ang = ua.angle(fa); axis = fa.cross(ua).normalized()
        rot_world(f"T_LowerArm_{sd}", Matrix.Rotation(ang - math.radians(straighten_to), 3, axis), el)
    # transform every joint by the bone that carries it
    carry = {}
    for s, sd in (("l", "L"), ("r", "R")):
        for k in J:
            if not isinstance(J[k], list): continue
            if k.startswith(f"{s}-finger") or k in (f"{s}-hand-2", f"{s}-hand-3"): carry[k] = f"T_Hand_{sd}"
            elif k == f"{s}-hand": carry[k] = f"T_LowerArm_{sd}"
            elif k == f"{s}-knee": carry[k] = f"T_Thigh_{sd}"
            elif k == f"{s}-ankle": carry[k] = f"T_Calf_{sd}"
            elif k.startswith(f"{s}-toe") or k.startswith(f"{s}-foot"): carry[k] = f"T_Foot_{sd}"
    J2 = {}
    for k, p in J.items():
        if not isinstance(p, list): continue
        b = carry.get(k)
        if b:
            M = pb[b].matrix @ T.data.bones[b].matrix_local.inverted()
            J2[k] = list(M @ V(p))
        else:
            J2[k] = list(p)
    # bake the pose into the meshes
    apply_mods_keep_shapes(body); body.parent = None; body.vertex_groups.clear()
    for p in parts:
        apply_mods_keep_shapes(p); p.parent = None; p.vertex_groups.clear()
    full = bpy.data.objects.get("PLR2_Body_full")
    bpy.data.objects.remove(T, do_unlink=True)
    return J2

# ------------------------------------------------------------------ 2. game rig
def rig_specs(J):
    up = V((0, 0, 1)); fwd = V((0, -1, 0))
    P = lambda k: V(J[k])
    pel = V((0, (P("pelvis").y + P("spine-4").y) / 2, (P("l-upper-leg").z + P("pelvis").z) / 2 - 0.005))
    s4, s2, s1, nk, hd, h2 = P("spine-4"), P("spine-2"), P("spine-1"), P("neck"), P("head"), P("head-2")
    chest_top = V((0, (s1.y + nk.y) / 2 - 0.01, 1.515))
    B = [("Root", None, V((0, 0, 0)), V((0, -0.25, 0)), up, False),
         ("Pelvis", "Root", pel, s4, fwd, True),
         ("Spine", "Pelvis", s4, s2, fwd, True),
         ("Spine_Upper", "Spine", s2, s1, fwd, True),
         ("Chest", "Spine_Upper", s1, chest_top, fwd, True),
         ("Neck", "Chest", chest_top, hd, fwd, True),
         ("Head", "Neck", hd, V((0, hd.y, h2.z)), fwd, True)]
    for s, sd in (("l", "L"), ("r", "R")):
        sh, el, wr = P(f"{s}-shoulder"), P(f"{s}-elbow"), P(f"{s}-hand")
        mid = P(f"{s}-finger-3-1"); idx = P(f"{s}-finger-2-1"); pky = P(f"{s}-finger-5-1")
        d = (mid - wr).normalized(); across = (idx - pky).normalized()
        n = d.cross(across).normalized()
        if n.z > 0: n = -n                                     # palm normal points down / into the palm
        B += [(f"Clavicle_{sd}", "Chest", P(f"{s}-clavicle"), sh, fwd, True),
              (f"UpperArm_{sd}", f"Clavicle_{sd}", sh, el, fwd, True),
              (f"LowerArm_{sd}", f"UpperArm_{sd}", el, wr, fwd, True),
              (f"Hand_{sd}", f"LowerArm_{sd}", wr, mid, n, True)]
        for fi, nm in ((1, "Thumb"), (2, "Index"), (3, "Middle"), (4, "Ring"), (5, "Pinky")):
            pts = [P(f"{s}-finger-{fi}-{k}") for k in (1, 2, 3, 4)]
            B += [(f"{nm}_01_{sd}", f"Hand_{sd}", pts[0], pts[1], n, True),
                  (f"{nm}_02_{sd}", f"{nm}_01_{sd}", pts[1], pts[2], n, True),
                  (f"{nm}_03_{sd}", f"{nm}_02_{sd}", pts[2], pts[3], n, True)]
        palm = wr.lerp(mid, 0.55)
        B.append((f"Weapon_{sd}", f"Hand_{sd}", palm + n * 0.03, palm + n * 0.03 + across * 0.1, n, False))
        hip, kn, an = P(f"{s}-upper-leg"), P(f"{s}-knee"), P(f"{s}-ankle")
        ball = (P(f"{s}-toe-2-1") + P(f"{s}-toe-3-1")) / 2; ball.z = max(0.022, ball.z - 0.004)
        toe = P(f"{s}-foot-2"); toe.z = max(toe.z, 0.012)
        B += [(f"Thigh_{sd}", "Pelvis", hip, kn, fwd, True),
              (f"Calf_{sd}", f"Thigh_{sd}", kn, an, fwd, True),
              (f"Foot_{sd}", f"Calf_{sd}", an, ball, up, True),
              (f"Toe_{sd}", f"Foot_{sd}", ball, toe, up, True)]
    return B

def build_rig(J, name="PLAYER_Survivor_Rig"):
    return _armature(name, rig_specs(J))

def skin(arm):
    body = bpy.data.objects["PLR2_Body"]
    for m in list(body.modifiers):
        if m.type == 'ARMATURE': body.modifiers.remove(m)
    body.vertex_groups.clear()
    r = pf_ops.run(bpy.ops.object.parent_set, [body, arm], active=arm, type='ARMATURE_AUTO')
    unweighted = sum(1 for v in body.data.vertices if not v.groups)
    for n in BODY_PARTS:
        if n in bpy.data.objects: transfer_weights(bpy.data.objects[n], body, arm)
    for n in FACE_PARTS + HEAD_PARTS:
        if n in bpy.data.objects: rigid(bpy.data.objects[n], arm, "Head")
    return r, unweighted

def hair_tail_weights(hair, arm, z_top=1.64, z_bot=1.48):
    """tied tail follows head at the tie and the chest further down the back"""
    hair.vertex_groups.clear()
    gh = hair.vertex_groups.new(name="Head"); gn = hair.vertex_groups.new(name="Neck"); gc = hair.vertex_groups.new(name="Chest")
    for v in hair.data.vertices:
        z = v.co.z
        t = max(0.0, min(1.0, (z - z_bot) / (z_top - z_bot))); t = t * t * (3 - 2 * t)
        if v.co.y < 0.03: t = 1.0
        gh.add([v.index], t, 'REPLACE')
        if t < 1.0:
            gn.add([v.index], (1 - t) * 0.4, 'REPLACE'); gc.add([v.index], (1 - t) * 0.6, 'REPLACE')

def face_shapes(body):
    import pf_player_v2 as P
    out = {}
    for n in ("PLR2_Lashes", "PLR2_Brows", "PLR2_Beard"):
        o = bpy.data.objects.get(n)
        if not o: continue
        if o.data.shape_keys: o.shape_key_clear()
        P.transfer_shapes(body, o)
        out[n] = len(o.data.shape_keys.key_blocks)
    return out

def pose_test(arm, pose):
    """pose: {bone: (axis, degrees)} in armature space"""
    for pb in arm.pose.bones: pb.rotation_quaternion = Quaternion()
    for b, (axis, deg) in pose.items():
        pb = arm.pose.bones[b]
        R = arm.data.bones[b].matrix_local.to_3x3()
        q = Quaternion(Vector(axis).normalized(), math.radians(deg))
        pb.rotation_quaternion = (R.inverted() @ q.to_matrix() @ R).to_quaternion()
    bpy.context.view_layer.update()

def skirt_weights(obj, thigh_keep=0.4, cross=0.3, smooth_iter=6, top_z=None, front_keep=None, front_cross=None):
    """hanging garment: pelvis carries most of it, thighs pull the lower hem partially (both flaps feel both legs)"""
    me = obj.data
    names = [g.name for g in obj.vertex_groups]
    gi = {g.name: g.index for g in obj.vertex_groups}
    for n in ("Pelvis", "Thigh_L", "Thigh_R"):
        if n not in gi: gi[n] = obj.vertex_groups.new(name=n).index
    zs = [v.co.z for v in me.vertices]; ztop = top_z or max(zs); zbot = min(zs)
    W = []
    for v in me.vertices:
        w = {obj.vertex_groups[g.group].name: g.weight for g in v.groups}
        tl = w.get("Thigh_L", 0.0) + w.get("Calf_L", 0.0); tr = w.get("Thigh_R", 0.0) + w.get("Calf_R", 0.0)
        down = max(0.0, min(1.0, (ztop - v.co.z) / max(1e-4, ztop - zbot)))       # 0 at the belt .. 1 at the hem
        fr = max(0.0, min(1.0, (-v.co.y - 0.0) / 0.08)) if front_keep is not None else 0.0     # 1 on the front flap
        keep = thigh_keep * (1 - fr) + (front_keep or thigh_keep) * fr
        cr = cross * (1 - fr) + (front_cross if front_cross is not None else cross) * fr
        if fr > 0.5:   # the front flap is pulled by whichever thigh is lifted: use the stronger side, not only the local one
            tmax = max(tl, tr); tl, tr = tl * 0.5 + tmax * 0.5, tr * 0.5 + tmax * 0.5
        k = keep * down ** 1.2
        L = (tl * (1 - cr) + tr * cr) * k; R = (tr * (1 - cr) + tl * cr) * k
        W.append([L, R])
    # smooth over mesh edges
    import numpy as np
    W = np.array(W); nb = [[] for _ in me.vertices]
    for e in me.edges:
        a, b = e.vertices; nb[a].append(b); nb[b].append(a)
    for _ in range(smooth_iter):
        W = np.array([(W[i] + W[n].mean(0)) * 0.5 if n else W[i] for i, n in enumerate(nb)])
    for g in list(obj.vertex_groups): obj.vertex_groups.remove(g)
    gp = obj.vertex_groups.new(name="Pelvis"); gl = obj.vertex_groups.new(name="Thigh_L"); gr = obj.vertex_groups.new(name="Thigh_R")
    for i, (l, r) in enumerate(W):
        p = max(0.0, 1.0 - l - r)
        gp.add([i], p, 'REPLACE')
        if l > 1e-4: gl.add([i], float(l), 'REPLACE')
        if r > 1e-4: gr.add([i], float(r), 'REPLACE')
