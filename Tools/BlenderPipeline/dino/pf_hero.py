"""
PRIMAL FRONTIER - hero creature assembly in Blender (Rift Tyrant first): body from dino_hero.py, detailed parts
(individual serrated teeth, gums, tongue, eyeballs + eyelids, curved claws), UV sets per material, richer rig.
Also a quick preview renderer used while iterating on the sculpt.
"""
import bpy, bmesh, json, math, os
import numpy as np
from mathutils import Vector, Matrix
import pf_dino

HD = r"E:\Model game khủng long\characters\work\hero"
RD = r"E:\Model game khủng long\renders\hero"


def _mesh(name, v, f, coll=None):
    old = bpy.data.objects.get(name)
    if old: bpy.data.objects.remove(old, do_unlink=True)
    me = bpy.data.meshes.new(name); me.vertices.add(len(v)); me.vertices.foreach_set("co", np.asarray(v, np.float32).ravel())
    f = np.asarray(f, np.int32)
    me.loops.add(len(f) * 3); me.loops.foreach_set("vertex_index", f.ravel()); me.polygons.add(len(f))
    me.polygons.foreach_set("loop_start", np.arange(0, len(f) * 3, 3, dtype=np.int32)); me.polygons.foreach_set("loop_total", np.full(len(f), 3, np.int32))
    me.update(calc_edges=True); me.validate()
    for p in me.polygons: p.use_smooth = True
    o = bpy.data.objects.new(name, me); (coll or bpy.context.scene.collection).objects.link(o)
    return o


def preview(tag, ply, joints, views=("side", "q34", "head34", "headside", "headfront", "foot"), extra=()):
    """render the body (and optional extra objects) with Workbench from fixed cameras -> <RD>/<tag>_sheet.jpg"""
    os.makedirs(RD, exist_ok=True)
    sc = bpy.data.scenes.get("HeroPrev") or bpy.data.scenes.new("HeroPrev")
    v, f = pf_dino.read_ply(ply)
    o = _mesh("HERO_prev", v, f, sc.collection)
    for e in extra:
        if e.name not in sc.collection.objects: sc.collection.objects.link(e)
    cam = bpy.data.objects.get("HeroCam")
    if not cam:
        cam = bpy.data.objects.new("HeroCam", bpy.data.cameras.new("HeroCam")); sc.collection.objects.link(cam)
    sc.camera = cam; sc.render.engine = 'BLENDER_WORKBENCH'
    sh = sc.display.shading; sh.light = 'STUDIO'; sh.color_type = 'OBJECT' if extra else 'SINGLE'; sh.single_color = (0.8, 0.74, 0.64)
    sh.show_cavity = True; sh.cavity_type = 'BOTH'; sh.show_shadows = True
    o.color = (0.8, 0.74, 0.64, 1)
    sc.render.resolution_x = 1000; sc.render.resolution_y = 700
    J = json.load(open(joints))
    hp = Vector(J["head_frame"]["pos"]); fwd = Vector(J["head_frame"]["fwd"]); hc = hp + fwd * 0.75
    V = {"side": ((13, 1.0, 3.3), (0, 0.9, 2.7), 40), "q34": ((6, -9, 5.5), (0, -1, 3), 38), "head34": ((1.9, -5.6, 5.3), tuple(hc), 32),
         "headside": ((2.6, hc.y, hc.z + 0.1), tuple(hc), 40), "headfront": ((0.4, -6.2, 4.9), tuple(hc), 32), "foot": ((2.6, -1.8, 1.0), (0.5, 0.1, 0.4), 40),
         "mouth": ((1.4, -5.3, 4.2), tuple(hc - Vector((0, 0, 0.2))), 30)}
    files = []
    for k in views:
        e, t, fov = V[k]
        cam.location = Vector(e); cam.rotation_euler = (Vector(t) - Vector(e)).to_track_quat('-Z', 'Y').to_euler()
        cam.data.lens = 18.0 / math.tan(math.radians(fov / 2))
        fp = os.path.join(RD, f"{tag}_{k}.png"); sc.render.filepath = fp
        bpy.ops.render.render(write_still=True, scene=sc.name); files.append(fp)
    # contact sheet
    ims = [bpy.data.images.load(fp) for fp in files]
    W, H = 500, 350; cols = 2; rows = (len(ims) + 1) // 2
    sheet = np.zeros((rows * H, cols * W, 4), np.float32)
    for i, im in enumerate(ims):
        px = np.array(im.pixels[:], np.float32).reshape(im.size[1], im.size[0], 4)
        px = px[::2, ::2][:H, :W]
        r = rows - 1 - i // 2; c = i % 2
        sheet[r * H:(r + 1) * H, c * W:(c + 1) * W] = px
        bpy.data.images.remove(im)
    out = bpy.data.images.new(f"{tag}_sheet", cols * W, rows * H); out.pixels = sheet.ravel()
    out.filepath_raw = os.path.join(RD, f"{tag}_sheet.png"); out.file_format = 'PNG'; out.save(); bpy.data.images.remove(out)
    return os.path.join(RD, f"{tag}_sheet.png")


# ------------------------------------------------------------------ part geometry (all world space, creature faces -Y)
def _frame_from(a, fwd_hint):
    a = Vector(a).normalized(); m = Vector(fwd_hint); m = (m - m.project(a))
    if m.length < 1e-6: m = a.orthogonal()
    m.normalize(); l = a.cross(m); return a, m, l


def tooth_geo(root, d, L, fwd, seg=8, rings=10, serr=0.05):
    """recurved blade tooth: lens cross-section with sharp front / back edges (carinae) and serrations on them"""
    a, m, l = _frame_from(d, fwd)
    root = Vector(root); verts = []; faces = []
    s_vals = [-0.28 + (1.28) * (i / rings) ** 0.9 for i in range(rings)]            # root part sits in the gum
    for ri, s in enumerate(s_vals):
        ss = max(s, 0.0)
        c = root + a * (L * s) - m * (L * 0.16 * ss * ss)                          # bends backwards towards the tip
        taper = (1 - ss) ** 0.75 if s > 0 else 1.0
        wm = L * 0.19 * taper + L * 0.01; wl = L * 0.105 * taper + L * 0.006
        for k in range(seg):
            th = 2 * math.pi * k / seg; cm = math.cos(th); sl = math.sin(th)
            edge = abs(cm) > 0.95
            r_m = wm * (1.14 if edge else 1.0) * (1 + (serr if (edge and ri % 2 == 0 and s > 0.05) else 0))
            verts.append(c + m * (cm * r_m) + l * (sl * wl * (1 - 0.25 * cm * cm)))
    tip = root + a * L - m * (L * 0.16) ; verts.append(tip); ti = len(verts) - 1
    base = root - a * (L * 0.3); verts.append(base); bi = len(verts) - 1
    for ri in range(rings - 1):
        for k in range(seg):
            i0 = ri * seg + k; i1 = ri * seg + (k + 1) % seg
            faces.append((i0, i1, i1 + seg, i0 + seg))
    last = (rings - 1) * seg
    for k in range(seg): faces.append((last + k, last + (k + 1) % seg, ti)); faces.append(((k + 1) % seg, k, bi))
    return verts, faces


def tube_geo(pts, r, seg=8, squash=1.0, up=(0, 0, 1), taper_ends=0.25):
    pts = [Vector(p) for p in pts]; n = len(pts); verts = []; faces = []
    for i, p in enumerate(pts):
        t = (pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized()
        u = Vector(up); u = (u - u.project(t)).normalized(); x = t.cross(u)
        k = i / (n - 1); rr = r * (min(1.0, min(k, 1 - k) / taper_ends) ** 0.5 if taper_ends > 0 else 1.0) + r * 0.08
        for j in range(seg):
            th = 2 * math.pi * j / seg; verts.append(p + x * (math.cos(th) * rr) + u * (math.sin(th) * rr * squash))
    for i in range(n - 1):
        for j in range(seg):
            a = i * seg + j; b = i * seg + (j + 1) % seg; faces.append((a, b, b + seg, a + seg))
    verts.append(pts[0]); c0 = len(verts) - 1; verts.append(pts[-1]); c1 = len(verts) - 1
    for j in range(seg):
        faces.append(((j + 1) % seg, j, c0)); last = (n - 1) * seg; faces.append((last + j, last + (j + 1) % seg, c1))
    return verts, faces


def tongue_geo(pts, w, t, up, seg=12):
    """flattened tapering tongue with a shallow midline groove and a rounded tip"""
    pts = [Vector(p) for p in pts]; n = len(pts); verts = []; faces = []
    for i, p in enumerate(pts):
        s = i / (n - 1)
        tg = (pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized()
        u = Vector(up); u = (u - u.project(tg)).normalized(); x = tg.cross(u)
        ww = w * (1 - 0.55 * s) * (math.sqrt(max(0.0, 1 - ((s - 0.9) / 0.1) ** 2)) if s > 0.9 else 1.0) + 0.004
        tt = t * (1 - 0.5 * s) + 0.003
        for j in range(seg):
            th = 2 * math.pi * j / seg; cx = math.cos(th); sz = math.sin(th)
            groove = -0.35 * tt * math.exp(-(cx / 0.25) ** 2) if sz > 0 else 0.0
            verts.append(p + x * (cx * ww) + u * (sz * tt + groove * sz))
    for i in range(n - 1):
        for j in range(seg):
            a = i * seg + j; b = i * seg + (j + 1) % seg; faces.append((a, b, b + seg, a + seg))
    verts.append(pts[0]); c0 = len(verts) - 1; verts.append(pts[-1] + (pts[-1] - pts[-2]).normalized() * 0.01); c1 = len(verts) - 1
    for j in range(seg):
        faces.append(((j + 1) % seg, j, c0)); last = (n - 1) * seg; faces.append((last + j, last + (j + 1) % seg, c1))
    return verts, faces


def eyeball_geo(c, r, out_axis, seg=18, ring=12):
    """UV sphere whose pole points outwards (iris centred on the pole)"""
    o = Vector(out_axis).normalized(); t1 = o.orthogonal().normalized(); t2 = o.cross(t1); c = Vector(c)
    verts = []; faces = []
    for j in range(1, ring):
        th = math.pi * j / ring
        for i in range(seg):
            ph = 2 * math.pi * i / seg
            verts.append(c + (o * math.cos(th) + t1 * (math.sin(th) * math.cos(ph)) + t2 * (math.sin(th) * math.sin(ph))) * r)
    top = len(verts); verts.append(c + o * r); bot = len(verts); verts.append(c - o * r)
    for j in range(ring - 2):
        for i in range(seg):
            a = j * seg + i; b = j * seg + (i + 1) % seg; faces.append((a, a + seg, b + seg, b))
    for i in range(seg):
        faces.append((top, i, (i + 1) % seg)); last = (ring - 2) * seg; faces.append((bot, last + (i + 1) % seg, last + i))
    return verts, faces


def lid_geo(c, r, out_axis, up, fwd, phi0, phi1, psi=72, n_phi=7, n_psi=12, r_in=1.02, r_out=1.1):
    """thick spherical cap around the eye: phi measured from the outward axis towards up (degrees), psi = fore-aft extent"""
    o = Vector(out_axis).normalized(); u = Vector(up); u = (u - u.project(o)).normalized(); f = o.cross(u)
    if f.dot(Vector(fwd)) < 0: f = -f
    c = Vector(c); verts = []; faces = []
    def P(ph, ps, rr):
        ph = math.radians(ph); ps = math.radians(ps)
        return c + (o * (math.cos(ph) * math.cos(ps)) + u * (math.sin(ph) * math.cos(ps)) + f * math.sin(ps)) * (r * rr)
    for layer, rr in enumerate((r_out, r_in)):
        for i in range(n_phi):
            ph = phi0 + (phi1 - phi0) * i / (n_phi - 1)
            for j in range(n_psi):
                ps = -psi + 2 * psi * j / (n_psi - 1); verts.append(P(ph, ps * (1 - 0.35 * abs(ph - phi0) / max(1, abs(phi1 - phi0))), rr))
    N = n_phi * n_psi
    for layer in (0, 1):
        for i in range(n_phi - 1):
            for j in range(n_psi - 1):
                a = layer * N + i * n_psi + j; q = (a, a + 1, a + n_psi + 1, a + n_psi)
                faces.append(q if layer == 0 else q[::-1])
    # rim along the lid margin (i = 0) and the sides
    for j in range(n_psi - 1):
        a, b = j, j + 1; faces.append((b, a, N + a, N + b))
        a, b = (n_phi - 1) * n_psi + j, (n_phi - 1) * n_psi + j + 1; faces.append((a, b, N + b, N + a))
    for i in range(n_phi - 1):
        a, b = i * n_psi, (i + 1) * n_psi; faces.append((a, b, N + b, N + a))
        a, b = i * n_psi + n_psi - 1, (i + 1) * n_psi + n_psi - 1; faces.append((b, a, N + a, N + b))
    if phi1 < phi0: faces = [q[::-1] for q in faces]
    return verts, faces


def claw_geo(base, d, L, down=(0, 0, -1), seg=8, rings=7, thick=0.32):
    """curved, laterally compressed hook claw"""
    a = Vector(d).normalized(); dn = Vector(down); dn = (dn - dn.project(a)).normalized(); side = a.cross(dn)
    base = Vector(base); verts = []; faces = []
    for ri in range(rings):
        s = ri / (rings - 1) * 0.94
        ang = s * 1.1                                                     # curls down along its length
        c = base + (a * math.sin(ang) + dn * (1 - math.cos(ang))) * (L / 1.1)
        tg = (a * math.cos(ang) + dn * math.sin(ang)).normalized(); nn = tg.cross(side).normalized()
        h = L * 0.26 * (1 - s) ** 0.9 + L * 0.02; w = h * thick * 1.6
        for k in range(seg):
            th = 2 * math.pi * k / seg
            verts.append(c + nn * (math.cos(th) * h * (0.8 if math.cos(th) > 0 else 1.0)) + side * (math.sin(th) * w))
    endang = 1.1; tip = base + (a * math.sin(endang) + dn * (1 - math.cos(endang))) * (L / 1.1); verts.append(tip); ti = len(verts) - 1
    verts.append(base - a * L * 0.15); bi = len(verts) - 1
    for ri in range(rings - 1):
        for k in range(seg):
            i0 = ri * seg + k; i1 = ri * seg + (k + 1) % seg; faces.append((i0, i0 + seg, i1 + seg, i1))
    last = (rings - 1) * seg
    for k in range(seg): faces.append((last + k, ti, last + (k + 1) % seg)); faces.append(((k + 1) % seg, bi, k))
    return verts, faces


def ground_claw(verts, floor=0.004):
    """tilt a foot claw up around its base so the hooked tip just touches the ground instead of sinking into it"""
    vs = [Vector(v) for v in verts]
    low = min(vs, key=lambda v: v.z)
    if low.z >= floor: return vs
    base = max(vs, key=lambda v: (v - low).length)
    arm = low - base; hor = Vector((arm.x, arm.y, 0))
    if hor.length < 1e-4: return [v + Vector((0, 0, floor - low.z)) for v in vs]
    axis = Vector((0, 0, 1)).cross(hor).normalized()
    ang = math.asin(min(0.95, (floor - low.z) / max(arm.length, 1e-4)))
    for _ in range(3):                                                   # rotate up until the tip clears the floor
        R = Matrix.Rotation(-ang, 3, axis)
        out = [base + R @ (v - base) for v in vs]
        mz = min(v.z for v in out)
        if mz >= floor * 0.5: return out
        R = Matrix.Rotation(ang, 3, axis); out = [base + R @ (v - base) for v in vs]
        if min(v.z for v in out) >= floor * 0.5: return out
        ang *= 1.6
    return [v + Vector((0, 0, floor - min(v.z for v in out))) for v in out]


PART = dict(skin=0, claw=1, tooth=2, eye=3, membrane=4, tongue=5, gum=6, lid=7)
GRP = dict(none=0, head=1, jaw=2, tongue=3, lid_up_L=4, lid_lo_L=5, lid_up_R=6, lid_lo_R=7, nearest=8)


def build_parts(J, sp):
    """list of (verts, faces, part, mat, grp) for every detailed part"""
    hs = sp["hero"]; hf = J["head_frame"]; hx = Vector(hf["x"]); hd = Vector(hf["fwd"]); hu = Vector(hf["up"])
    parts = []
    for t in J["teeth"]:
        v, f = tooth_geo(t["root"], t["dir"], t["len"], t["fwd"]); parts.append((v, f, PART["tooth"], 2, GRP["head"] if t["upper"] else GRP["jaw"]))
    for g in J["gums"]:
        v, f = tube_geo(g["pts"], g["r"], 8, squash=0.7, up=tuple(hu), taper_ends=0.12); parts.append((v, f, PART["gum"], 2, GRP["head"] if g["upper"] else GRP["jaw"]))
    tg = J["tongue"]; v, f = tongue_geo(tg["pts"], tg["w"] * 0.5, tg["t"] * 0.5, hu); parts.append((v, f, PART["tongue"], 2, GRP["tongue"]))
    er = J["eye_r"]
    for i, e in enumerate(J["eyes"]):
        s = 1 if e[0] > 0 else -1; out = (hx * s + hd * 0.18).normalized()
        v, f = eyeball_geo(e, er, out); parts.append((v, f, PART["eye"], 1, GRP["head"]))
        side = "L" if s > 0 else "R"
        v, f = lid_geo(e, er, out, hu, hd, 22, 125); parts.append((v, f, PART["lid"], 0, GRP[f"lid_up_{side}"]))
        v, f = lid_geo(e, er, out, hu, hd, -32, -112, psi=62, n_phi=5); parts.append((v, f, PART["lid"], 0, GRP[f"lid_lo_{side}"]))
    for key, toes in J["toes"].items():
        for tp in toes:
            d = (Vector(tp[-1]) - Vector(tp[-2])).normalized(); d.z = min(d.z, -0.1); d.normalize()
            v, f = claw_geo(Vector(tp[-1]) - d * 0.03, d, 0.17, seg=8); parts.append((ground_claw(v), f, PART["claw"], 0, GRP["nearest"]))
    for key, dc in J.get("dewclaw", {}).items():
        d = (Vector(dc[1]) - Vector(dc[0])).normalized(); v, f = claw_geo(Vector(dc[1]) - d * 0.02, d, 0.07); parts.append((ground_claw(v), f, PART["claw"], 0, GRP["nearest"]))
    for key, fingers in J.get("fingers", {}).items():
        for fg in fingers:
            d = (Vector(fg[2]) - Vector(fg[1])).normalized(); v, f = claw_geo(Vector(fg[2]) - d * 0.01, d, 0.075); parts.append((v, f, PART["claw"], 0, GRP["nearest"]))
    return parts


def parts_object(J, sp, name="HERO_parts", coll=None):
    parts = build_parts(J, sp)
    V_ = []; F_ = []; P_ = []; M_ = []; G_ = []
    for v, f, pt, mi, g in parts:
        b = len(V_); V_ += [tuple(x) for x in v]; F_ += [tuple(i + b for i in q) for q in f]; P_ += [pt] * len(v); G_ += [g] * len(v); M_ += [mi] * len(f)
    old = bpy.data.objects.get(name)
    if old: bpy.data.objects.remove(old, do_unlink=True)
    me = bpy.data.meshes.new(name); me.from_pydata(V_, [], F_); me.update()
    me.polygons.foreach_set("material_index", np.array(M_, np.int32))
    a = me.attributes.new("mat_i", 'INT', 'FACE'); a.data.foreach_set("value", np.array(M_, np.int32))
    for p in me.polygons: p.use_smooth = True
    a = me.attributes.new("part", 'INT', 'POINT'); a.data.foreach_set("value", np.array(P_, np.int32))
    a = me.attributes.new("grp", 'INT', 'POINT'); a.data.foreach_set("value", np.array(G_, np.int32))
    o = bpy.data.objects.new(name, me); (coll or bpy.context.scene.collection).objects.link(o)
    return o


# ------------------------------------------------------------------ assembly
def _mat(name, color, rough=0.5):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    if b: b.inputs["Base Color"].default_value = (*color, 1); b.inputs["Roughness"].default_value = rough
    return m


def build_mesh_hero(sid, sp, J, ply):
    """body shell + all detailed parts in one mesh (materials: 0 skin, 1 eye, 2 mouth) with 'part' / 'grp' attributes"""
    import pf_ops
    c = pf_dino.coll("DINO_" + sid)
    v, f = pf_dino.read_ply(ply)
    body = _mesh(f"DINO_{sid}_Work", v, f, c)
    bm = bmesh.new(); bm.from_mesh(body.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(body.data); bm.free()
    # light relax of the marching-cubes facets (normals come from the SDF bake anyway)
    sm = body.modifiers.new("Relax", 'SMOOTH'); sm.factor = 0.35; sm.iterations = 1
    pf_ops.run(bpy.ops.object.modifier_apply, [body], active=body, modifier="Relax")
    me = body.data
    for name in ("part", "grp"):
        if name not in me.attributes: me.attributes.new(name, 'INT', 'POINT')
    po = parts_object(J, sp, name=f"DINO_{sid}_Parts", coll=c)
    Name = sid.capitalize()
    mats = [_mat(f"M_Dino_{Name}", tuple(sp["hero"]["colors"]["base"])), _mat(f"M_Dino_{Name}_Eye", (0.5, 0.35, 0.1), 0.1),
            _mat(f"M_Dino_{Name}_Mouth", (0.8, 0.7, 0.6), 0.3)]
    for o in (body, po):
        o.data.materials.clear()
        for m in mats: o.data.materials.append(m)
    for p in body.data.polygons: p.material_index = 0; p.use_smooth = True
    if "mat_i" not in body.data.attributes: body.data.attributes.new("mat_i", 'INT', 'FACE')
    pf_ops.run(bpy.ops.object.join, [body, po], active=body)
    body.name = f"DINO_{sid}_Work"; body.data.name = body.name
    mi = np.zeros(len(body.data.polygons), np.int32); body.data.attributes["mat_i"].data.foreach_get("value", mi)
    body.data.polygons.foreach_set("material_index", mi); body.data.update()
    return body


def unwrap_hero(o, margin=0.0025):
    """separate UV packs per material: skin atlas, mouth atlas, eyes by planar projection"""
    import pf_ops
    me = o.data
    if not me.uv_layers: me.uv_layers.new(name="UVMap")
    ts = bpy.context.scene.tool_settings; sync0 = ts.use_uv_select_sync; ts.use_uv_select_sync = False   # pack only the selected material
    for mi in (0, 2):
        pf_ops.run(bpy.ops.object.mode_set, [o], active=o, mode='EDIT')
        bm = bmesh.from_edit_mesh(me)
        for fc in bm.faces: fc.select_set(fc.material_index == mi)
        bmesh.update_edit_mesh(me)
        pf_ops.run(bpy.ops.uv.smart_project, [o], active=o, angle_limit=math.radians(62 if mi == 0 else 70), island_margin=margin,
                   area_weight=0.0, correct_aspect=True, scale_to_bounds=False)
        try:
            pf_ops.run(bpy.ops.uv.select_all, [o], active=o, action='SELECT')
            pf_ops.run(bpy.ops.uv.pack_islands, [o], active=o, rotate=True, margin=margin)
        except Exception as e: print("pack failed", e)
        pf_ops.run(bpy.ops.object.mode_set, [o], active=o, mode='OBJECT')
    ts.use_uv_select_sync = sync0


def eye_uvs(o, J):
    me = o.data; hf = J["head_frame"]; hx = Vector(hf["x"]); hd = Vector(hf["fwd"]); hu = Vector(hf["up"]); er = J["eye_r"]
    uvl = me.uv_layers.active.data
    for p in me.polygons:
        if p.material_index != 1: continue
        cen = Vector((0, 0, 0))
        for li in p.loop_indices: cen += me.vertices[me.loops[li].vertex_index].co
        cen /= len(p.loop_indices)
        e = Vector(J["eyes"][0] if cen.x > 0 else J["eyes"][1]); s = 1 if cen.x > 0 else -1
        out = (hx * s + hd * 0.18).normalized(); t1 = (hu - hu.project(out)).normalized(); t2 = out.cross(t1)
        for li in p.loop_indices:
            q = me.vertices[me.loops[li].vertex_index].co - e
            u = 0.5 + s * q.dot(t2) / (2.05 * er); v = 0.5 + q.dot(t1) / (2.05 * er)
            if q.dot(out) < 0: u, v = 0.02, 0.02                                  # back of the eyeball: dark corner
            uvl[li].uv = (u, v)


def export_tex_data_hero(o, sid, out_dir):
    me = o.data; me.calc_loop_triangles()
    n = len(me.vertices); co = np.empty(n * 3, np.float32); me.vertices.foreach_get("co", co)
    nr = np.empty(n * 3, np.float32); me.vertices.foreach_get("normal", nr)
    tris = np.empty(len(me.loop_triangles) * 3, np.int32); me.loop_triangles.foreach_get("vertices", tris)
    loops = np.empty(len(me.loop_triangles) * 3, np.int32); me.loop_triangles.foreach_get("loops", loops)
    mat = np.empty(len(me.loop_triangles), np.int32); me.loop_triangles.foreach_get("material_index", mat)
    uv = np.empty(len(me.loops) * 2, np.float32); me.uv_layers.active.data.foreach_get("uv", uv)
    part = np.zeros(n, np.int32); me.attributes["part"].data.foreach_get("value", part)
    path = os.path.join(out_dir, f"{sid}_texdata.npz")
    np.savez_compressed(path, co=co.reshape(-1, 3), nr=nr.reshape(-1, 3), tris=tris.reshape(-1, 3), uv=uv.reshape(-1, 2)[loops].reshape(-1, 3, 2),
                        part=part, mat=mat)
    return path


# ------------------------------------------------------------------ textured preview (Eevee)
def hook_textures(sid, texdir):
    Name = sid.capitalize()
    for suffix in ("", "_Eye", "_Mouth"):
        m = bpy.data.materials.get(f"M_Dino_{Name}{suffix}")
        if not m: continue
        m.use_nodes = True; nt = m.node_tree; nt.nodes.clear()
        out = nt.nodes.new("ShaderNodeOutputMaterial"); b = nt.nodes.new("ShaderNodeBsdfPrincipled"); nt.links.new(b.outputs[0], out.inputs[0])
        def img(k, cs):
            p = os.path.join(texdir, f"T_Dino_{Name}{suffix}_{k}.png")
            n = nt.nodes.new("ShaderNodeTexImage"); im = bpy.data.images.load(p, check_existing=True); im.reload()
            im.colorspace_settings.name = cs; n.image = im; return n
        d = img("D", "sRGB"); nm = img("N", "Non-Color"); mm = img("M", "Non-Color")
        mix = nt.nodes.new("ShaderNodeMix"); mix.data_type = 'RGBA'; mix.blend_type = 'MULTIPLY'; mix.inputs["Factor"].default_value = 0.6
        sep = nt.nodes.new("ShaderNodeSeparateColor")
        nt.links.new(mm.outputs["Color"], sep.inputs[0])
        nt.links.new(d.outputs["Color"], mix.inputs["A"]); nt.links.new(sep.outputs["Green"], mix.inputs["B"])
        nt.links.new(mix.outputs["Result"], b.inputs["Base Color"])
        inv = nt.nodes.new("ShaderNodeMath"); inv.operation = 'SUBTRACT'; inv.inputs[0].default_value = 1.0
        nt.links.new(mm.outputs["Alpha"], inv.inputs[1]); nt.links.new(inv.outputs[0], b.inputs["Roughness"])
        nmap = nt.nodes.new("ShaderNodeNormalMap"); nt.links.new(nm.outputs["Color"], nmap.inputs["Color"]); nt.links.new(nmap.outputs["Normal"], b.inputs["Normal"])
        mm.image.alpha_mode = 'CHANNEL_PACKED'


def preview_textured(tag, obj, joints, views=("q34", "head34", "headside", "mouth", "side", "foot"), engine='BLENDER_EEVEE_NEXT'):
    os.makedirs(RD, exist_ok=True)
    sc = bpy.data.scenes.get("HeroPrevTex") or bpy.data.scenes.new("HeroPrevTex")
    for o in list(sc.collection.objects): sc.collection.objects.unlink(o)
    sc.collection.objects.link(obj)
    for extra in [c for c in obj.children]: sc.collection.objects.link(extra)
    cam = bpy.data.objects.get("HeroCamT")
    if not cam: cam = bpy.data.objects.new("HeroCamT", bpy.data.cameras.new("HeroCamT"))
    sc.collection.objects.link(cam); sc.camera = cam
    sun = bpy.data.objects.get("HeroSun")
    if not sun: sun = bpy.data.objects.new("HeroSun", bpy.data.lights.new("HeroSun", 'SUN'))
    sun.data.energy = 3.5; sun.rotation_euler = (math.radians(50), 0, math.radians(35)); sc.collection.objects.link(sun)
    if not sc.world: sc.world = bpy.data.worlds.new("HeroWorld")
    sc.world.use_nodes = True; bg = sc.world.node_tree.nodes.get("Background")
    if bg: bg.inputs[0].default_value = (0.45, 0.5, 0.58, 1); bg.inputs[1].default_value = 0.8
    try: sc.render.engine = engine
    except Exception: sc.render.engine = 'BLENDER_EEVEE'
    sc.render.resolution_x = 1000; sc.render.resolution_y = 700
    J = json.load(open(joints))
    hp = Vector(J["head_frame"]["pos"]); fwd = Vector(J["head_frame"]["fwd"]); hc = hp + fwd * 0.75
    V = {"side": ((13, 1.0, 3.3), (0, 0.9, 2.7), 40), "q34": ((6, -9, 5.5), (0, -1, 3), 38), "head34": ((1.9, -5.6, 5.3), tuple(hc), 32),
         "headside": ((2.6, hc.y, hc.z + 0.1), tuple(hc), 40), "headfront": ((0.4, -6.2, 4.9), tuple(hc), 32), "foot": ((2.6, -1.8, 1.0), (0.5, 0.1, 0.4), 40),
         "mouth": ((1.4, -5.3, 4.2), tuple(hc - Vector((0, 0, 0.2))), 30)}
    files = []
    for k in views:
        e, t, fov = V[k]
        cam.location = Vector(e); cam.rotation_euler = (Vector(t) - Vector(e)).to_track_quat('-Z', 'Y').to_euler(); cam.data.lens = 18.0 / math.tan(math.radians(fov / 2))
        fp = os.path.join(RD, f"{tag}_{k}.png"); sc.render.filepath = fp
        bpy.ops.render.render(write_still=True, scene=sc.name); files.append(fp)
    ims = [bpy.data.images.load(fp) for fp in files]
    W, H = 500, 350; cols = 2; rows = (len(ims) + 1) // 2
    sheet = np.zeros((rows * H, cols * W, 4), np.float32)
    for i, im in enumerate(ims):
        px = np.array(im.pixels[:], np.float32).reshape(im.size[1], im.size[0], 4)[::2, ::2][:H, :W]
        r = rows - 1 - i // 2; c = i % 2; sheet[r * H:(r + 1) * H, c * W:(c + 1) * W] = px; bpy.data.images.remove(im)
    out = bpy.data.images.new(f"{tag}_sheet", cols * W, rows * H); out.pixels = sheet.ravel()
    out.filepath_raw = os.path.join(RD, f"{tag}_sheet.png"); out.file_format = 'PNG'; out.save(); bpy.data.images.remove(out)
    for o in list(sc.collection.objects): sc.collection.objects.unlink(o)
    return os.path.join(RD, f"{tag}_sheet.png")


# ------------------------------------------------------------------ rig / skin / export
def hero_bone_specs(sp, J):
    S = pf_dino.bone_specs(sp, J)
    hf = J["head_frame"]; hu = Vector(hf["up"]); hd = Vector(hf["fwd"])
    tp = [Vector(p) for p in J["tongue"]["pts"]]
    marks = [0, 3, 5, len(tp) - 1]
    for i in range(3):
        S.append((f"Tongue_{i + 1:02d}", "Jaw" if i == 0 else f"Tongue_{i:02d}", tp[marks[i]], tp[marks[i + 1]], hu, True))
    for e in J["eyes"]:
        side = "L" if e[0] > 0 else "R"; c = Vector(e)
        S.append((f"Eyelid_Upper_{side}", "Head", c, c + hu * 0.07, hd, True))
        S.append((f"Eyelid_Lower_{side}", "Head", c, c - hu * 0.06, hd, True))
    for key, fingers in J.get("fingers", {}).items():
        side = key.rsplit("_", 1)[1]
        if len(fingers) > 1:
            S.append((f"Finger_02_{side}", f"Hand_{side}", Vector(fingers[1][0]), Vector(fingers[1][2]), Vector((0, -1, 0)), True))
    return S


def build_armature_hero(sid, sp, J):
    import pf_rig_v2
    old = bpy.data.objects.get(f"DINO_{sid}_Rig")
    if old: bpy.data.objects.remove(old, do_unlink=True)
    arm = pf_rig_v2._armature(f"DINO_{sid}_Rig", hero_bone_specs(sp, J))
    c = pf_dino.coll("DINO_" + sid)
    for cc in arm.users_collection: cc.objects.unlink(arm)
    c.objects.link(arm)
    return arm


def _seg_d(p, a, b):
    ab = b - a; u = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-9))); return (a + ab * u - p).length, u


def skin_hero(body, arm, sp, J):
    import pf_ops
    from mathutils.bvhtree import BVHTree
    for m in list(body.modifiers):
        if m.type == 'ARMATURE': body.modifiers.remove(m)
    body.vertex_groups.clear()
    me = body.data; n = len(me.vertices)
    part = np.zeros(n, np.int32); me.attributes["part"].data.foreach_get("value", part)
    grp = np.zeros(n, np.int32); me.attributes["grp"].data.foreach_get("value", grp)
    co = np.empty(n * 3, np.float32); me.vertices.foreach_get("co", co); co = co.reshape(-1, 3)
    # 1. bone heat on the skin shell only (parts are skinned explicitly below)
    drop = np.where(part != 0)[0]
    tme = me.copy(); tmp = bpy.data.objects.new(body.name + "_wtmp", tme)
    for c in body.users_collection: c.objects.link(tmp)
    bm = bmesh.new(); bm.from_mesh(tme); bm.verts.ensure_lookup_table()
    oid = bm.verts.layers.int.new("oid")
    for v in bm.verts: v[oid] = v.index
    bmesh.ops.delete(bm, geom=[bm.verts[int(i)] for i in drop], context='VERTS'); bm.to_mesh(tme); bm.free()
    pf_ops.run(bpy.ops.object.parent_set, [tmp, arm], active=arm, type='ARMATURE_AUTO')
    ids = np.zeros(len(tme.vertices), np.int32); tme.attributes["oid"].data.foreach_get("value", ids)
    pf_ops.run(bpy.ops.object.parent_set, [body, arm], active=arm, type='ARMATURE_NAME')
    G = {g.name: g for g in body.vertex_groups}
    def VG(nm):
        if nm not in G: G[nm] = body.vertex_groups.new(name=nm)
        return G[nm]
    W = [dict() for _ in range(n)]
    gname = {g.index: g.name for g in tmp.vertex_groups}
    for v in tme.vertices:
        oi = int(ids[v.index])
        for g in v.groups:
            if g.weight > 1e-4: W[oi][gname[g.group]] = g.weight
    bpy.data.objects.remove(tmp); bpy.data.meshes.remove(tme)
    # small arms: bone heat lets them pull a wide patch of chest / flank; keep their influence near the arm itself
    armb = [b for b in arm.data.bones if b.name.startswith(("UpperArm", "Forearm", "Hand_", "Finger"))]
    for i in range(n):
        if not W[i]: continue
        p = Vector(co[i]); changed = False
        for b in armb:
            if b.name in W[i]:
                lim = 0.28 if b.name.startswith("UpperArm") else 0.16
                if _seg_d(p, b.head_local, b.tail_local)[0] > lim: W[i].pop(b.name); changed = True
        if changed and not W[i]: W[i] = {"Chest": 1.0}
    # 2. head: rigid skull / jaw split on the lip line, blended into the neck at the back of the skull
    hf = J["head_frame"]; hp = np.array(hf["pos"]); hx = np.array(hf["x"]); hd = np.array(hf["fwd"]); hu = np.array(hf["up"])
    hl, hw, hh, lip = hf["len"], hf["w"], hf["h"], hf["lip"]
    loc = co - hp; lf = loc @ hd; lu = loc @ hu; lx = loc @ hx
    zone = (lf > -0.2 * hl) & (lf < 1.2 * hl) & (np.abs(lx) < hw * 1.9) & (lu > -hh * 1.3) & (lu < hh * 1.5) & (part == 0)
    wH = np.clip((lf + 0.04 * hl) / (0.16 * hl), 0, 1)
    jw = np.clip((lip - lu) / 0.012 + 0.5, 0, 1) * np.clip((lf - 0.1 * hl) / (0.08 * hl), 0, 1)
    for i in np.where(zone)[0]:
        i = int(i); w = float(wH[i]); j = float(jw[i])
        d = {k: v * (1 - w) for k, v in W[i].items()}
        d["Head"] = d.get("Head", 0) + (1 - j) * w; d["Jaw"] = d.get("Jaw", 0) + j * w
        W[i] = d
    # 3. parts
    tp = [Vector(p) for p in J["tongue"]["pts"]]
    tb = [b for b in ("Tongue_01", "Tongue_02", "Tongue_03") if b in arm.data.bones]
    tipbones = [b.name for b in arm.data.bones if b.name.startswith(("Toe_", "Finger_", "Foot_", "Hand_"))]
    lids = {4: "Eyelid_Upper_L", 5: "Eyelid_Lower_L", 6: "Eyelid_Upper_R", 7: "Eyelid_Lower_R"}
    for i in np.where(part != 0)[0]:
        i = int(i); g = int(grp[i]); p = Vector(co[i])
        if g == 1: W[i] = {"Head": 1.0}
        elif g == 2: W[i] = {"Jaw": 1.0}
        elif g in lids: W[i] = {lids[g]: 1.0}
        elif g == 3:
            ds = []
            for b in tb:
                bb = arm.data.bones[b]; d, u = _seg_d(p, bb.head_local, bb.tail_local); ds.append((d, b))
            ds.sort(); top = ds[:2]; ws = [1 / (d + 0.01) ** 3 for d, _ in top]; s_ = sum(ws)
            W[i] = {b: w / s_ for (d, b), w in zip(top, ws)}
        elif g == 8:
            best = min(tipbones, key=lambda nm: _seg_d(p, arm.data.bones[nm].head_local, arm.data.bones[nm].tail_local)[0])
            W[i] = {best: 1.0}
        else:
            W[i] = W[i] or {"Head": 1.0}
    # 4. write, limit to 4, normalise; fill any vertex still empty from its nearest bone
    for i in range(n):
        d = W[i]
        if not d:
            p = Vector(co[i]); best = min(arm.data.bones, key=lambda b: _seg_d(p, b.head_local, b.tail_local)[0]); d = {best.name: 1.0}
        top = sorted(d.items(), key=lambda kv: -kv[1])[:4]; s_ = sum(w for _, w in top) or 1.0
        for nm, w in top:
            if w / s_ > 1e-4: VG(nm).add([i], w / s_, 'REPLACE')
    left = int(sum(1 for v in me.vertices if not v.groups))
    if left: raise RuntimeError(f"{left} vertices without weights")
    return 0


def make_lods_hero(body, arm, sid, ratios=((1, 0.42), (2, 0.15), (3, 0.05))):
    """LOD1 keeps teeth / eyes (decimated), LOD2+ drop the small mouth parts"""
    import pf_ops
    lods = []
    body.name = f"DINO_{sid}_LOD0"; body.data.name = body.name; lods.append(body)
    for i, r in ratios:
        nm = f"DINO_{sid}_LOD{i}"
        old = bpy.data.objects.get(nm)
        if old: bpy.data.objects.remove(old, do_unlink=True)
        o = body.copy(); o.data = body.data.copy(); o.name = nm; o.data.name = nm
        for c in body.users_collection: c.objects.link(o)
        for m in list(o.modifiers): o.modifiers.remove(m)
        if i >= 2:
            bm = bmesh.new(); bm.from_mesh(o.data); lay = bm.verts.layers.int.get("part")
            kill = [v for v in bm.verts if v[lay] in (2, 5, 6, 7)]                  # teeth, tongue, gums, lids
            bmesh.ops.delete(bm, geom=kill, context='VERTS'); bm.to_mesh(o.data); bm.free()
            r = r * len(body.data.polygons) / max(1, len(o.data.polygons))
        md = o.modifiers.new("Dec", 'DECIMATE'); md.ratio = min(1.0, r); md.use_collapse_triangulate = True
        pf_ops.run(bpy.ops.object.modifier_apply, [o], active=o, modifier="Dec")
        am = o.modifiers.new("Armature", 'ARMATURE'); am.object = arm; o.parent = arm
        lods.append(o)
    return lods


def finish_hero(sid, sp, J):
    import pf_dino_anim, pf_char_export, shutil
    body = bpy.data.objects.get(f"DINO_{sid}_Work") or bpy.data.objects.get(f"DINO_{sid}_LOD0")
    for m in list(body.modifiers):
        if m.type == 'ARMATURE': body.modifiers.remove(m)
    body.parent = None
    arm = build_armature_hero(sid, sp, J)
    skin_hero(body, arm, sp, J)
    metas = pf_dino_anim.build_clips(arm, sp)
    lods = make_lods_hero(body, arm, sid)
    Name = sid.capitalize(); dest = os.path.join(pf_dino.UNITY, Name)
    meta = dict(character=Name, rig="Generic", fps=30, clips=[dict(name=m["name"], frames=m["frames"], loop=m["loop"], speed=m.get("speed", 0.0),
                notes="", move=m.get("move", ""), events=m.get("events", [])) for m in metas])
    keep = set(m["name"] for m in metas)
    for a in list(bpy.data.actions):
        if a.name not in keep: bpy.data.actions.remove(a)
    res = pf_char_export.export(arm, lods, dest, f"DINO_{Name}", [m["name"] for m in metas], [], meta, f"DINO_{Name}_anim.json",
                                use_mesh_modifiers=False, face_unity_forward=True)
    os.makedirs(os.path.join(dest, "Textures"), exist_ok=True)
    copied = []
    for f in os.listdir(pf_dino.TEXD):
        if f.startswith(f"T_Dino_{Name}_") and "Membrane" not in f:
            shutil.copy2(os.path.join(pf_dino.TEXD, f), os.path.join(dest, "Textures", f)); copied.append(f)
    return dict(fbx=res["fbx"], bytes=res["bytes"], takes=res["takes_found"], textures=copied, tris=[len(o.data.polygons) for o in lods],
                clips=len(metas), bones=len(arm.data.bones))


def ground_claws_in_mesh(o):
    """apply ground_claw to the claw islands already in a built mesh (keeps topology / UVs)"""
    me = o.data; n = len(me.vertices)
    part = np.zeros(n, np.int32); me.attributes["part"].data.foreach_get("value", part)
    co = np.empty(n * 3, np.float32); me.vertices.foreach_get("co", co); co = co.reshape(-1, 3)
    comp, _ = pf_dino._components(me); fixed = 0
    for c in np.unique(comp[part == 1]):
        ids = np.where(comp == c)[0]
        if co[ids, 2].min() >= 0.004 or co[ids, 2].max() > 0.6: continue
        new = ground_claw([tuple(p) for p in co[ids]])
        co[ids] = np.array([tuple(v) for v in new], np.float32); fixed += 1
    me.vertices.foreach_set("co", co.ravel()); me.update()
    return fixed
