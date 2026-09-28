# PRIMAL FRONTIER - player rig upgrade (Phase: character foundation)
# 1. forearm twist bones (LowerArmTwist_L/R): spread wrist twist along the forearm instead of pinching at the wrist
# 2. shoulder weight smoothing (clavicle / upper arm / chest / spine) and pelt / strap follow-through
# Operates on PLAYER_Survivor_v2_work.blend (rig PLAYER_Survivor_Rig, meshes PLAYER_Survivor_LOD0/1/2). Idempotent.
import bpy, bmesh, math
from mathutils import Vector

RIG = "PLAYER_Survivor_Rig"
LODS = ("PLAYER_Survivor_LOD0", "PLAYER_Survivor_LOD1", "PLAYER_Survivor_LOD2")
TWIST_SHARE = 0.6        # fraction of the hand's twist the forearm twist bone takes (Blender constraint + Unity driver)

def _mode(obj, mode):
    if bpy.context.object and bpy.context.object.mode != 'OBJECT': bpy.ops.object.mode_set(mode='OBJECT')
    for o in bpy.context.selected_objects: o.select_set(False)
    bpy.context.view_layer.objects.active = obj; obj.select_set(True)
    if mode != 'OBJECT': bpy.ops.object.mode_set(mode=mode)

def add_twist_bones():
    arm = bpy.data.objects[RIG]
    _mode(arm, 'EDIT')
    eb = arm.data.edit_bones
    made = []
    for sd in ("L", "R"):
        name = f"LowerArmTwist_{sd}"
        la = eb[f"LowerArm_{sd}"]
        tw = eb.get(name) or eb.new(name)
        tw.head = la.head.lerp(la.tail, 0.5); tw.tail = la.head.lerp(la.tail, 0.97)
        tw.roll = la.roll; tw.parent = la; tw.use_connect = False; tw.use_deform = True
        made.append(name)
    bpy.ops.object.mode_set(mode='POSE')
    for sd in ("L", "R"):
        pb = arm.pose.bones[f"LowerArmTwist_{sd}"]
        pb.rotation_mode = 'QUATERNION'
        c = pb.constraints.get("TwistFromHand") or pb.constraints.new('COPY_ROTATION')
        c.name = "TwistFromHand"; c.target = arm; c.subtarget = f"Hand_{sd}"
        c.use_x = False; c.use_y = True; c.use_z = False
        c.owner_space = 'LOCAL'; c.target_space = 'LOCAL'; c.mix_mode = 'REPLACE'; c.influence = TWIST_SHARE
    bpy.ops.object.mode_set(mode='OBJECT')
    return made

def smoothstep(a, b, x):
    t = max(0.0, min(1.0, (x - a) / (b - a))); return t * t * (3 - 2 * t)

def split_forearm_weights(mesh_obj):
    arm = bpy.data.objects[RIG]
    stats = {}
    for sd in ("L", "R"):
        bl = arm.data.bones[f"LowerArm_{sd}"]
        A = arm.matrix_world @ bl.head_local; B = arm.matrix_world @ bl.tail_local
        d = (B - A); L = d.length; d.normalize()
        g_la = mesh_obj.vertex_groups.get(f"LowerArm_{sd}")
        g_tw = mesh_obj.vertex_groups.get(f"LowerArmTwist_{sd}") or mesh_obj.vertex_groups.new(name=f"LowerArmTwist_{sd}")
        if not g_la: continue
        mw = mesh_obj.matrix_world; moved = 0
        for v in mesh_obj.data.vertices:
            w = 0.0
            for g in v.groups:
                if g.group == g_la.index: w = g.weight; break
            if w <= 0.0: continue
            t = (mw @ v.co - A).dot(d) / L
            f = smoothstep(0.22, 0.88, t) * 0.92            # never all of it: the wrist ring stays partly on the forearm
            if f <= 0.001: continue
            g_la.add([v.index], w * (1 - f), 'REPLACE')
            g_tw.add([v.index], w * f, 'REPLACE'); moved += 1
        stats[sd] = moved
    return stats

def smooth_region(mesh_obj, centers, radius, groups, repeat=6, factor=0.5, mat_filter=None):
    """smooth the listed vertex groups over vertices near the given armature-space points"""
    arm = bpy.data.objects[RIG]
    pts = [arm.matrix_world @ c for c in centers]
    me = mesh_obj.data; mw = mesh_obj.matrix_world
    face_ok = None
    if mat_filter is not None:
        face_ok = set()
        for p in me.polygons:
            if p.material_index in mat_filter: face_ok.update(p.vertices)
    sel = [v.index for v in me.vertices if any((mw @ v.co - p).length < radius for p in pts) and (face_ok is None or v.index in face_ok)]
    if not sel: return 0
    _mode(mesh_obj, 'OBJECT')
    for v in me.vertices: v.select = False
    for e in me.edges: e.select = False
    for p in me.polygons: p.select = False
    for i in sel: me.vertices[i].select = True
    bpy.ops.object.mode_set(mode='WEIGHT_PAINT')
    mesh_obj.data.use_paint_mask_vertex = True
    for gname in groups:
        g = mesh_obj.vertex_groups.get(gname)
        if not g: continue
        mesh_obj.vertex_groups.active_index = g.index
        bpy.ops.object.vertex_group_smooth(group_select_mode='ACTIVE', factor=factor, repeat=repeat, expand=0.0)
    mesh_obj.data.use_paint_mask_vertex = False
    bpy.ops.object.mode_set(mode='OBJECT')
    return len(sel)

def normalize_limit(mesh_obj):
    _mode(mesh_obj, 'OBJECT')
    bpy.ops.object.mode_set(mode='WEIGHT_PAINT')
    bpy.ops.object.vertex_group_limit_total(group_select_mode='ALL', limit=4)
    bpy.ops.object.vertex_group_normalize_all(group_select_mode='ALL', lock_active=False)
    bpy.ops.object.mode_set(mode='OBJECT')

def upgrade():
    out = {"twist_bones": add_twist_bones()}
    arm = bpy.data.objects[RIG]
    for n in LODS:
        o = bpy.data.objects[n]
        info = {"forearm": split_forearm_weights(o)}
        sh = [arm.data.bones[f"UpperArm_{sd}"].head_local for sd in ("L", "R")]
        grp = ["Chest", "Spine_Upper", "Clavicle_L", "Clavicle_R", "UpperArm_L", "UpperArm_R"]
        info["shoulder_skin"] = smooth_region(o, sh, 0.11, grp, repeat=8, factor=0.5, mat_filter={0})
        info["shoulder_cloth"] = smooth_region(o, sh, 0.2, grp, repeat=12, factor=0.6, mat_filter={3})
        el = [arm.data.bones[f"LowerArm_{sd}"].head_local for sd in ("L", "R")]
        info["elbow"] = smooth_region(o, el, 0.06, ["UpperArm_L", "UpperArm_R", "LowerArm_L", "LowerArm_R"], repeat=3, factor=0.4, mat_filter={0})
        normalize_limit(o)
        out[n] = info
    return out

# ---------------------------------------------------------------- deformation test renders
import os
from mathutils import Matrix, Quaternion

def _rot_world(arm, pb, axis, deg):
    """rotate a pose bone about a world axis through its head (armature space)"""
    bpy.context.view_layer.update()
    m = pb.matrix.copy(); head = m.to_translation()
    R = Matrix.Rotation(math.radians(deg), 4, Vector(axis))
    pb.matrix = Matrix.Translation(head) @ R @ Matrix.Translation(-head) @ m
    bpy.context.view_layer.update()

POSES = {
    "shoulder_up_90": lambda arm: _rot_world(arm, arm.pose.bones["UpperArm_L"], (0, 1, 0), -90),
    "shoulder_fwd_90": lambda arm: _rot_world(arm, arm.pose.bones["UpperArm_L"], (1, 0, 0), 90),
    "wrist_twist_p80": lambda arm: setattr(arm.pose.bones["Hand_L"], "rotation_quaternion", Quaternion((0, 1, 0), math.radians(80))),
    "wrist_twist_m80": lambda arm: setattr(arm.pose.bones["Hand_L"], "rotation_quaternion", Quaternion((0, 1, 0), math.radians(-80))),
    "elbow_bend_110": lambda arm: _rot_world(arm, arm.pose.bones["LowerArm_L"], (1, 0, 0), 110),
}

def _reset_pose(arm):
    for pb in arm.pose.bones:
        pb.rotation_mode = 'QUATERNION'
        pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0); pb.scale = (1, 1, 1)

def deform_tests(subdir="audit_deform_after", names=None):
    import pf_look
    arm = bpy.data.objects[RIG]
    ad = arm.animation_data; keep = ad.action if ad else None
    if ad: ad.action = None
    pf_look.studio(ground=False)
    hid = {}
    for n in LODS[1:]:
        o = bpy.data.objects.get(n)
        if o: hid[n] = o.hide_render; o.hide_render = True
    out = []
    try:
        for name in (names or POSES):
            _reset_pose(arm); bpy.context.view_layer.update()
            POSES[name](arm); bpy.context.view_layer.update()
            a = arm.matrix_world @ arm.pose.bones["UpperArm_L"].head
            b = arm.matrix_world @ arm.pose.bones["Hand_L"].head
            t = a.lerp(b, 0.45 if name.startswith("shoulder") else 0.7)
            out += pf_look.shoot(t, 1.05, t.z + 0.12, views=("front", "three4"), size=(560, 560), prefix=name, lens=50, look_z=t.z, subdir=subdir)
    finally:
        _reset_pose(arm)
        if ad: ad.action = keep
        for n, v in hid.items(): bpy.data.objects[n].hide_render = v
    return out
