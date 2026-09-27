"""
PRIMAL FRONTIER - dinosaur assembly in Blender: SDF body (PLY from dino_sdf.py) -> decimated game mesh, eyes, teeth,
claws, wing membrane -> UVs -> texture data for dino_tex.py -> armature from the same joints -> weights (auto +
head/jaw split + rigid parts) -> LOD1/LOD2. Creatures face -Y in Blender (exported rotated to face +Z in Unity).
"""
import bpy, bmesh, json, math, os
import numpy as np
from mathutils import Vector, Matrix, Quaternion
import pf_ops

WORK = r"E:\Model game khủng long\characters\work\dino"
TRI = dict(triceratops=26000, parasaurolophus=24000, ankylosaurus=26000, velociraptor=16000, carnotaurus=24000, spinosaurus=26000,
           apex=28000, pteranodon=14000, mosasaurus=22000)

def V(p): return Vector(p)

def load_joints(sid): return json.load(open(os.path.join(WORK, f"{sid}_joints.json")))

def read_ply(path):
    b = open(path, 'rb').read(); i = b.index(b"end_header\n") + len(b"end_header\n")
    hdr = b[:i].decode(); nv = int(hdr.split("element vertex ")[1].split()[0])
    v = np.frombuffer(b[i:i + nv * 12], '<f4').reshape(-1, 3)
    f = np.frombuffer(b[i + nv * 12:], dtype=[('n', 'u1'), ('i', '<i4', 3)])['i']
    return v.astype(np.float32), f.astype(np.int32)

def mesh_from_np(name, v, f, coll=None):
    old = bpy.data.objects.get(name)
    if old: bpy.data.objects.remove(old, do_unlink=True)
    me = bpy.data.meshes.new(name)
    me.vertices.add(len(v)); me.vertices.foreach_set("co", v.ravel())
    me.loops.add(len(f) * 3); me.loops.foreach_set("vertex_index", f.ravel())
    me.polygons.add(len(f)); me.polygons.foreach_set("loop_start", np.arange(0, len(f) * 3, 3, dtype=np.int32)); me.polygons.foreach_set("loop_total", np.full(len(f), 3, np.int32))
    me.update(calc_edges=True); me.validate()
    o = bpy.data.objects.new(name, me); (coll or bpy.context.scene.collection).objects.link(o)
    return o

def coll(name):
    c = bpy.data.collections.get(name)
    if not c: c = bpy.data.collections.new(name); bpy.context.scene.collection.children.link(c)
    return c

def mat(name, color, rough=0.6):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    if bsdf: bsdf.inputs["Base Color"].default_value = (*color, 1); bsdf.inputs["Roughness"].default_value = rough
    return m

def decimate(o, tris):
    cur = len(o.data.polygons)
    if cur <= tris: return
    md = o.modifiers.new("Dec", 'DECIMATE'); md.ratio = tris / cur; md.use_collapse_triangulate = True
    pf_ops.run(bpy.ops.object.modifier_apply, [o], active=o, modifier="Dec")

def smooth(o):
    for p in o.data.polygons: p.use_smooth = True

# ------------------------------------------------------------------ parts
def add_part(bm_target, verts, faces, part_id, mat_index, part_layer, mat_layer=None):
    base = len(bm_target.verts)
    vs = [bm_target.verts.new(v) for v in verts]
    for f in faces:
        try:
            face = bm_target.faces.new([vs[i] for i in f]); face.material_index = mat_index; face.smooth = True
        except ValueError: pass
    for v in vs: v[part_layer] = part_id

def cone_geo(a, b, r, seg=6, tip_r=0.0):
    a = V(a); b = V(b); d = (b - a); L = d.length; d.normalize()
    x = d.orthogonal().normalized(); y = d.cross(x)
    verts = [a + (x * math.cos(t) + y * math.sin(t)) * r for t in [i * 2 * math.pi / seg for i in range(seg)]]
    faces = []
    if tip_r > 0:
        verts += [b + (x * math.cos(t) + y * math.sin(t)) * tip_r for t in [i * 2 * math.pi / seg for i in range(seg)]]
        for i in range(seg): faces.append((i, (i + 1) % seg, seg + (i + 1) % seg, seg + i))
    else:
        verts.append(b)
        for i in range(seg): faces.append((i, (i + 1) % seg, seg))
    verts.append(a - d * r * 0.3); c = len(verts) - 1
    for i in range(seg): faces.append(((i + 1) % seg, i, c))
    return verts, faces

def sphere_geo(c, r, seg=10, ring=7):
    c = V(c); verts = []; faces = []
    for j in range(1, ring):
        th = math.pi * j / ring
        for i in range(seg):
            ph = 2 * math.pi * i / seg
            verts.append(c + V((math.sin(th) * math.cos(ph), math.sin(th) * math.sin(ph), math.cos(th))) * r)
    top = len(verts); verts.append(c + V((0, 0, r))); bot = len(verts); verts.append(c - V((0, 0, r)))
    for j in range(ring - 2):
        for i in range(seg):
            a = j * seg + i; b = j * seg + (i + 1) % seg; faces.append((a, b, b + seg, a + seg))
    for i in range(seg):
        faces.append((top, (i + 1) % seg, i)); last = (ring - 2) * seg; faces.append((bot, last + i, last + (i + 1) % seg))
    return verts, faces

def build_mesh(sid, sp, J):
    """body + parts in one mesh with a 'part' int layer: 0 skin, 1 keratin (claws/beak), 2 teeth, 3 eye, 4 membrane"""
    v, f = read_ply(os.path.join(WORK, f"{sid}_body.ply"))
    c = coll("DINO_" + sid)
    body = mesh_from_np(f"DINO_{sid}_Work", v, f, c)
    # relax the voxel steps, then reduce, then clean slivers / normals
    sm = body.modifiers.new("Relax", 'SMOOTH'); sm.factor = 0.6; sm.iterations = 3
    pf_ops.run(bpy.ops.object.modifier_apply, [body], active=body, modifier="Relax")
    decimate(body, TRI.get(sid, 22000))
    bmc = bmesh.new(); bmc.from_mesh(body.data)
    bmesh.ops.remove_doubles(bmc, verts=bmc.verts, dist=1e-4)
    bmesh.ops.dissolve_degenerate(bmc, edges=bmc.edges, dist=1e-4)
    bmesh.ops.recalc_face_normals(bmc, faces=bmc.faces)
    bmc.to_mesh(body.data); bmc.free()
    bm = bmesh.new(); bm.from_mesh(body.data)
    part = bm.verts.layers.int.new("part")
    hf = J["head_frame"]; hp, hx, hd, hu = V(hf["pos"]), V(hf["x"]), V(hf["fwd"]), V(hf["up"]); hl, hw, hh = hf["len"], hf["w"], hf["h"]
    H = lambda a: hp + hx * a[0] + hd * a[1] + hu * a[2]
    # eyes
    for e in J["eyes"]:
        ev, ef = sphere_geo(e, J["eye_r"] * 0.95, 10, 7); add_part(bm, ev, ef, 3, 1, part)
    # teeth
    head = sp["head"]; nt = head.get("teeth", 0)
    if nt:
        per = nt // 4; tl = head.get("tooth", 0.03)
        for side in (1, -1):
            for i in range(per):
                t = (i + 0.5) / per
                fw = hl * (0.4 + 0.52 * t)
                wid = hw * (0.62 - 0.35 * t) * (0.8 if head.get("croc") else 1)
                a = H((side * wid, fw, -hh * 0.27)); tv, tf = cone_geo(a, a - hu * tl * (0.7 + 0.5 * math.sin(t * math.pi)) + hd * tl * 0.15, tl * 0.28, 5)
                add_part(bm, tv, tf, 2, 0, part)
                a = H((side * wid * 0.92, fw - hl * 0.02, -hh * 0.33)); tv, tf = cone_geo(a, a + hu * tl * (0.55 + 0.4 * math.sin(t * math.pi)) + hd * tl * 0.1, tl * 0.24, 5)
                add_part(bm, tv, tf, 2, 0, part)
    # claws
    cl = sp.get("claws")
    if cl:
        for key, lg in J["legs"].items():
            if "toe" not in lg: continue
            tips = lg.get("toe_tips") or [lg["toe"]]
            for tip in tips:
                tip = V(tip); size = cl["size"] * (0.6 if key.startswith("arm") else 1.0)
                fwd = V((0, -1, 0)); dn = V((0, 0, -1))
                if cl.get("hoof"): tv, tf = cone_geo(tip + V((0, size * 0.2, size * 0.25)), tip + fwd * size * 0.9 + dn * size * 0.1, size * 0.75, 6, size * 0.3)
                else: tv, tf = cone_geo(tip + V((0, size * 0.3, size * 0.25)), tip + fwd * size * 1.1 + dn * size * 0.55, size * 0.35, 6)
                add_part(bm, tv, tf, 1, 0, part)
            if cl.get("sickle") and key.startswith("hind"):
                ft = V(lg["foot"]); tv, tf = cone_geo(ft + V((0, -0.01, 0.03)), ft + V((0, -0.05, 0.075)), 0.012, 6); add_part(bm, tv, tf, 1, 0, part)
    # pteranodon wing membranes: fan between the arm / finger bones and the body side + ankle
    if sp["kind"] == "flyer":
        for side in ("L", "R"):
            w = J["legs"][f"wing_{side}"]; ank = V(J["legs"][f"hind_{side}"]["ankle"]); sx = 1 if side == "L" else -1
            edge = [V(w["shoulder"]), V(w["elbow"]), V(w["wrist"]), V(w["knuckle"])]
            tipp = V(w["tip"]); fing = [V(w["knuckle"]).lerp(tipp, t) for t in (0.33, 0.66, 1.0)]
            front = edge + fing                                                     # leading edge (arm + wing finger)
            back = [V((sx * 0.11, -0.2, 0.7)), V((sx * 0.1, 0.02, 0.68)), ank + V((0, 0, 0.02))]
            # trailing edge from the wing tip curving back to the ankle
            trail = [tipp.lerp(ank, t) + V((0, 0.35 * math.sin(t * math.pi), 0)) for t in (0.2, 0.45, 0.7)]
            rimB = back[::-1] + trail[::-1]                                         # ankle ... near tip
            n = len(front); m = len(rimB)
            verts = front + rimB; faces = []
            # stitch front[i] with rim (resampled) -> quad strip
            rim = [rimB[min(m - 1, int(round(i * (m - 1) / (n - 1))))] for i in range(n)]
            verts = front + rim
            for i in range(n - 1): faces.append((i, i + 1, n + i + 1, n + i) if side == "L" else (i, n + i, n + i + 1, i + 1))
            add_part(bm, verts, faces, 4, 2, part)
    bm.to_mesh(body.data); bm.free()
    body.data.materials.clear()
    body.data.materials.append(mat(f"M_Dino_{sid.capitalize()}", tuple(sp["colors"]["base"])))
    body.data.materials.append(mat(f"M_Dino_{sid.capitalize()}_Eye", (0.35, 0.25, 0.08), 0.1))
    if sp["kind"] == "flyer": body.data.materials.append(mat(f"M_Dino_{sid.capitalize()}_Membrane", tuple(sp["colors"]["base"]), 0.7))
    smooth(body)
    return body

def unwrap(o, margin=0.004):
    pf_ops.run(bpy.ops.object.mode_set, [o], active=o, mode='EDIT')
    pf_ops.run(bpy.ops.mesh.select_all, [o], active=o, action='SELECT')
    pf_ops.run(bpy.ops.uv.smart_project, [o], active=o, angle_limit=math.radians(60), island_margin=margin, area_weight=0.0, correct_aspect=True, scale_to_bounds=False)
    try:   # tighter packing = more texels on the skin
        pf_ops.run(bpy.ops.uv.select_all, [o], active=o, action='SELECT')
        pf_ops.run(bpy.ops.uv.pack_islands, [o], active=o, rotate=True, margin=0.0025)
    except Exception as e: print("pack_islands failed", e)
    pf_ops.run(bpy.ops.object.mode_set, [o], active=o, mode='OBJECT')

def export_tex_data(o, sid):
    """vertex positions / normals / parts and per-corner UVs for the texture generator (dino_tex.py on the VM)"""
    me = o.data; me.calc_loop_triangles()
    n = len(me.vertices); co = np.empty(n * 3, np.float32); me.vertices.foreach_get("co", co)
    nr = np.empty(n * 3, np.float32); me.vertices.foreach_get("normal", nr)
    tris = np.empty(len(me.loop_triangles) * 3, np.int32); me.loop_triangles.foreach_get("vertices", tris)
    loops = np.empty(len(me.loop_triangles) * 3, np.int32); me.loop_triangles.foreach_get("loops", loops)
    uv = np.empty(len(me.loops) * 2, np.float32); me.uv_layers.active.data.foreach_get("uv", uv)
    part = np.zeros(n, np.int32)
    if "part" in me.attributes: me.attributes["part"].data.foreach_get("value", part)
    np.savez_compressed(os.path.join(WORK, f"{sid}_texdata.npz"), co=co.reshape(-1, 3), nr=nr.reshape(-1, 3), tris=tris.reshape(-1, 3),
                        uv=uv.reshape(-1, 2)[loops].reshape(-1, 3, 2), part=part)

# ------------------------------------------------------------------ armature
def bone_specs(sp, J):
    S = []
    pel = V(J["pelvis"]); tail = [V(p) for p in J["tail"]]; spine = [V(p) for p in J["spine"]]; neck = [V(p) for p in J["neck"]]
    up = V((0, 0, 1))
    S.append(("Root", None, V((0, pel.y, 0)), V((0, pel.y - 0.5 * sp["length"] / 8, 0)), up, False))
    S.append(("Pelvis", "Root", pel, pel.lerp(tail[0], 0.6), up, True))
    prev = pel; par = "Pelvis"
    names = [f"Spine_{i + 1:02d}" for i in range(len(spine) - 1)] + ["Chest"]
    for nm, p in zip(names, spine): S.append((nm, par, prev, p, up, True)); prev = p; par = nm
    chest_end = prev
    np_ = neck + [V(J["head"])]
    for i, p in enumerate(np_):
        nm = f"Neck_{i + 1:02d}"; S.append((nm, par, prev, p, up, True)); prev = p; par = nm
    hf = J["head_frame"]; hu = V(hf["up"])
    S.append(("Head", par, V(J["head"]), V(J["snout"]), hu, True))
    S.append(("Jaw", "Head", V(J["jaw"]), V(J["chin"]), hu, True))
    prev = tail[0]; par = "Pelvis"
    S[1] = ("Pelvis", "Root", pel, tail[0], up, True)
    for i in range(len(tail) - 1):
        nm = f"Tail_{i + 1:02d}"; S.append((nm, par, tail[i], tail[i + 1], up, True)); par = nm
    for key, lg in J["legs"].items():
        grp, side = key.rsplit("_", 1)
        if grp == "hind":
            chain = [("Thigh", "hip", "knee"), ("Calf", "knee", "ankle"), ("Foot", "ankle", "foot"), ("Toe", "foot", "toe")]; root = "Pelvis"
        elif grp in ("front", "arm"):
            chain = [("UpperArm", "hip", "knee"), ("Forearm", "knee", "ankle"), ("Hand", "ankle", "foot"), ("Finger", "foot", "toe")]; root = "Chest"
        elif grp == "wing":
            k = lg; t = [V(k["knuckle"]).lerp(V(k["tip"]), s) for s in (0.0, 0.33, 0.66, 1.0)]
            S.append((f"UpperArm_{side}", "Chest", V(k["shoulder"]), V(k["elbow"]), up, True))
            S.append((f"Forearm_{side}", f"UpperArm_{side}", V(k["elbow"]), V(k["wrist"]), up, True))
            S.append((f"Hand_{side}", f"Forearm_{side}", V(k["wrist"]), V(k["knuckle"]), up, True))
            for i in range(3): S.append((f"WingFinger_{i + 1:02d}_{side}", f"Hand_{side}" if i == 0 else f"WingFinger_{i:02d}_{side}", t[i], t[i + 1], up, True))
            continue
        else:  # fins
            base = "Fin_Front" if "front" in grp else "Fin_Hind"; root = "Chest" if "front" in grp else "Pelvis"
            S.append((f"{base}_{side}", root, V(lg["root"]), V(lg["mid"]), up, True)); S.append((f"{base}_02_{side}", f"{base}_{side}", V(lg["mid"]), V(lg["tip"]), up, True))
            continue
        par = root
        for nm, a, b in chain:
            h = V(lg[a]); t = V(lg[b])
            if (t - h).length < 0.01: t = h + V((0, -0.02, 0))
            fwd = V((0, -1, 0))
            S.append((f"{nm}_{side}", par, h, t, fwd, True)); par = f"{nm}_{side}"
    return S

def build_armature(sid, sp, J):
    import pf_rig_v2
    arm = pf_rig_v2._armature(f"DINO_{sid}_Rig", bone_specs(sp, J))
    c = coll("DINO_" + sid)
    for cc in arm.users_collection: cc.objects.unlink(arm)
    c.objects.link(arm)
    return arm

# ------------------------------------------------------------------ weights
def _auto_weights(body, arm):
    """Bone-heat weights solved on the body shell only. Teeth / eyes / claws are many tiny separate islands and make the heat
    solver fail for most bones (the carnivores ended up ~85% unweighted), so they are left out here and skinned rigidly later."""
    import bmesh
    me = body.data; n = len(me.vertices)
    part = np.zeros(n, np.int32)
    if "part" in me.attributes: me.attributes["part"].data.foreach_get("value", part)
    drop = np.where((part == 1) | (part == 2) | (part == 3))[0]
    if len(drop) == 0:
        pf_ops.run(bpy.ops.object.parent_set, [body, arm], active=arm, type='ARMATURE_AUTO'); return
    tme = me.copy(); tmp = bpy.data.objects.new(body.name + "_wtmp", tme)
    for c in body.users_collection: c.objects.link(tmp)
    tmp.matrix_world = body.matrix_world.copy()
    bm = bmesh.new(); bm.from_mesh(tme); bm.verts.ensure_lookup_table()
    oid = bm.verts.layers.int.new("oid")
    for v in bm.verts: v[oid] = v.index
    bmesh.ops.delete(bm, geom=[bm.verts[int(i)] for i in drop], context='VERTS')
    bm.to_mesh(tme); bm.free()
    pf_ops.run(bpy.ops.object.parent_set, [tmp, arm], active=arm, type='ARMATURE_AUTO')
    ids = np.zeros(len(tme.vertices), np.int32); tme.attributes["oid"].data.foreach_get("value", ids)
    pf_ops.run(bpy.ops.object.parent_set, [body, arm], active=arm, type='ARMATURE_NAME')
    gname = {g.index: g.name for g in tmp.vertex_groups}
    groups = {g.name: g for g in body.vertex_groups}
    for v in tme.vertices:
        oi = int(ids[v.index])
        for g in v.groups:
            if g.weight > 1e-4:
                nm = gname[g.group]
                grp = groups.get(nm) or body.vertex_groups.new(name=nm); groups[nm] = grp
                grp.add([oi], g.weight, 'REPLACE')
    bpy.data.objects.remove(tmp); bpy.data.meshes.remove(tme)


def _fill_unweighted(body, arm):
    """Any vertex still without weights: inverse-distance to the two nearest deform bones on its own side."""
    me = body.data
    un = [v.index for v in me.vertices if not v.groups]
    if not un: return 0
    bones = [b for b in arm.data.bones if b.use_deform]
    A = np.array([b.head_local for b in bones], np.float32); B = np.array([b.tail_local for b in bones], np.float32)
    side = np.array([1 if b.name.endswith("_L") else (-1 if b.name.endswith("_R") else 0) for b in bones])
    groups = {g.name: g for g in body.vertex_groups}
    for i in un:
        p = np.array(me.vertices[i].co, np.float32)
        ab = B - A; t = np.clip(((p - A) * ab).sum(1) / np.maximum((ab * ab).sum(1), 1e-8), 0, 1)
        d = np.linalg.norm(A + ab * t[:, None] - p, axis=1)
        s = 1 if p[0] > 0.02 else (-1 if p[0] < -0.02 else 0)
        if s: d = np.where(side == -s, 1e9, d)
        top = np.argsort(d)[:2]; w = 1.0 / (d[top] + 0.02) ** 4; w /= w.sum()
        for k, wk in zip(top, w):
            nm = bones[int(k)].name
            grp = groups.get(nm) or body.vertex_groups.new(name=nm); groups[nm] = grp
            grp.add([i], float(wk), 'REPLACE')
    return len(un)


def skin(body, arm, sp, J):
    for m in list(body.modifiers):
        if m.type == 'ARMATURE': body.modifiers.remove(m)
    body.vertex_groups.clear()
    _auto_weights(body, arm)
    me = body.data; n = len(me.vertices)
    part = np.zeros(n, np.int32)
    if "part" in me.attributes: me.attributes["part"].data.foreach_get("value", part)
    co = np.empty(n * 3, np.float32); me.vertices.foreach_get("co", co); co = co.reshape(-1, 3)
    groups = {g.name: g for g in body.vertex_groups}
    def G(name): return groups.get(name) or body.vertex_groups.new(name=name)
    hf = J["head_frame"]; hp, hx, hd, hu = [np.array(hf[k], np.float32) for k in ("pos", "x", "fwd", "up")]; hl, hh = hf["len"], hf["h"]
    loc = co - hp; lf = loc @ hd; lu = loc @ hu; lx = loc @ hx
    head_zone = (lf > -0.1 * hl) & (lf < 1.25 * hl) & (np.abs(lx) < hf["w"] * 2.2 + 0.5) & (lu > -hh * 1.2) & (lu < hh * 2.5)
    # lower jaw: below the lip line and in front of the hinge; blend 3 cm around the corner
    lip = -hh * 0.3
    jaw_w = np.clip((lip - lu) / (hh * 0.06 + 1e-4), 0, 1) * np.clip((lf - hl * 0.2) / (hl * 0.15), 0, 1)
    ids = np.where(head_zone | (part == 2) | (part == 3))[0]
    headG, jawG = G("Head"), G("Jaw")
    for i in ids:
        i = int(i)
        if part[i] == 3: w_j = 0.0
        elif part[i] == 2: w_j = 1.0 if lu[i] < lip - hh * 0.03 else 0.0
        else: w_j = float(jaw_w[i])
        for g in list(me.vertices[i].groups):
            body.vertex_groups[g.group].remove([i])
        if w_j > 0: jawG.add([i], w_j, 'REPLACE')
        if w_j < 1: headG.add([i], 1 - w_j, 'REPLACE')
    # claws follow the nearest toe / finger bone rigidly
    tipbones = [b.name for b in arm.data.bones if b.name.startswith(("Toe_", "Finger_", "Foot_", "Hand_"))]
    for i in np.where(part == 1)[0]:
        i = int(i)
        if head_zone[i]: continue
        p = Vector(co[i]); best = min(tipbones, key=lambda nm: (arm.data.bones[nm].tail_local - p).length)
        for g in list(me.vertices[i].groups): body.vertex_groups[g.group].remove([i])
        G(best).add([i], 1.0, 'REPLACE')
    # membranes: distance-weighted between wing bones and the body
    if sp["kind"] == "flyer":
        cand = [b for b in arm.data.bones if b.name.startswith(("UpperArm", "Forearm", "Hand_", "WingFinger", "Chest", "Spine", "Pelvis", "Thigh", "Calf"))]
        for i in np.where(part == 4)[0]:
            i = int(i); p = Vector(co[i]); side = "L" if co[i][0] > 0 else "R"
            ds = []
            for b in cand:
                if b.name.endswith(("_L", "_R")) and not b.name.endswith(side): continue
                a, t = b.head_local, b.tail_local; ab = t - a; u = max(0, min(1, (p - a).dot(ab) / max(ab.length_squared, 1e-8)))
                ds.append(((a + ab * u - p).length, b.name))
            ds.sort(); top = ds[:2]; ws = [1 / (d + 0.02) ** 2 for d, _ in top]; s = sum(ws)
            for g in list(me.vertices[i].groups): body.vertex_groups[g.group].remove([i])
            for (d, nm), w in zip(top, ws): G(nm).add([i], w / s, 'REPLACE')
    filled = _fill_unweighted(body, arm)
    pf_ops.run(bpy.ops.object.vertex_group_limit_total, [body], active=body, limit=4)
    pf_ops.run(bpy.ops.object.vertex_group_normalize_all, [body], active=body, lock_active=False)
    left = int(sum(1 for v in me.vertices if not v.groups))
    if left: raise RuntimeError(f"{body.name}: {left} vertices without weights")
    return filled

def make_lods(body, arm, sid):
    lods = []
    body.name = f"DINO_{sid}_LOD0"; body.data.name = body.name; lods.append(body)
    for i, r in ((1, 0.45), (2, 0.16)):
        nm = f"DINO_{sid}_LOD{i}"
        old = bpy.data.objects.get(nm)
        if old: bpy.data.objects.remove(old, do_unlink=True)
        o = body.copy(); o.data = body.data.copy(); o.name = nm; o.data.name = nm
        for c in body.users_collection: c.objects.link(o)
        for m in list(o.modifiers): o.modifiers.remove(m)
        md = o.modifiers.new("Dec", 'DECIMATE'); md.ratio = r; md.use_collapse_triangulate = True
        pf_ops.run(bpy.ops.object.modifier_apply, [o], active=o, modifier="Dec")
        am = o.modifiers.new("Armature", 'ARMATURE'); am.object = arm
        o.parent = arm
        lods.append(o)
    return lods


# ------------------------------------------------------------------ export
UNITY = r"E:\LAST OF THE PRIMAL\Assets\Art\Characters\Dinosaurs"
TEXD = r"E:\Model game khủng long\textures\dino"

def finish(sid, sp, J, metas=None):
    """armature + weights + clips + LODs + FBX/textures/meta into the Unity project"""
    import pf_dino_anim, pf_char_export, shutil
    body = bpy.data.objects.get(f"DINO_{sid}_Work") or bpy.data.objects.get(f"DINO_{sid}_LOD0")
    arm = build_armature(sid, sp, J)
    unweighted = skin(body, arm, sp, J)
    metas = pf_dino_anim.build_clips(arm, sp)
    lods = make_lods(body, arm, sid)
    Name = sid.capitalize()
    dest = os.path.join(UNITY, Name)
    meta = dict(character=Name, rig="Generic", fps=30, clips=[dict(name=m["name"], frames=m["frames"], loop=m["loop"], speed=m.get("speed", 0.0),
                notes="", move=m.get("move", ""), events=m.get("events", [])) for m in metas])
    keep = set(m["name"] for m in metas)
    for a in list(bpy.data.actions):
        if a.name not in keep: bpy.data.actions.remove(a)             # the FBX exporter bakes every action in the file
    res = pf_char_export.export(arm, lods, dest, f"DINO_{Name}", [m["name"] for m in metas], [], meta, f"DINO_{Name}_anim.json",
                                use_mesh_modifiers=False, face_unity_forward=True)
    os.makedirs(os.path.join(dest, "Textures"), exist_ok=True)
    copied = []
    for f in os.listdir(TEXD):
        if f.startswith(f"T_Dino_{Name}_"):
            shutil.copy2(os.path.join(TEXD, f), os.path.join(dest, "Textures", f)); copied.append(f)
    return dict(fbx=res["fbx"], bytes=res["bytes"], takes=res["takes_found"], textures=len(copied), filled_by_distance=unweighted,
                tris=[len(o.data.polygons) for o in lods], clips=len(metas))
