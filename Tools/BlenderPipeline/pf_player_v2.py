"""PLAYER_Survivor v2: body from the CC0 MakeHuman base (see mh_morph.py), rebuilt parts, bake inputs."""
import bpy, bmesh, math, json
from mathutils import Vector, Matrix, noise
import pf_ops, pf_mh

STEM = "survivor_v3"

def _obj(name, bm_or_me, mats=()):
    old = bpy.data.objects.get(name)
    if old: bpy.data.objects.remove(old, do_unlink=True)
    if isinstance(bm_or_me, bmesh.types.BMesh):
        me = bpy.data.meshes.new(name); bm_or_me.to_mesh(me); bm_or_me.free()
    else:
        me = bm_or_me; me.name = name
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o)
    for m in mats: me.materials.append(m)
    for p in me.polygons: p.use_smooth = True
    return o

def apply_mods(o):
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(o.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
    o.modifiers.clear(); old = o.data; o.data = me; bpy.data.meshes.remove(old)
    return o

HELPERS = ["helper-l-eye", "helper-r-eye", "helper-upper-teeth", "helper-lower-teeth", "helper-tongue"]

def build_bodies(stem=STEM, ratio=0.72):
    J = pf_mh.joints(stem)
    lo = pf_mh.import_body(stem, "PLR2_Body")
    pf_mh.assign_groups(lo, stem); pf_mh.split_off(lo, HELPERS)
    for g in list(lo.vertex_groups): lo.vertex_groups.remove(g)
    full = lo.copy(); full.data = lo.data.copy(); full.name = "PLR2_Body_full"; bpy.context.scene.collection.objects.link(full)
    m = lo.modifiers.new("Dec", "DECIMATE"); m.ratio = ratio; m.use_symmetry = True; m.symmetry_axis = 'X'; m.use_collapse_triangulate = True
    apply_mods(lo)
    hi = pf_mh.import_body(stem + "_hi", "PLR2_Body_hi")
    pf_mh.assign_groups(hi, stem + "_hi"); pf_mh.split_off(hi, HELPERS)
    for g in list(hi.vertex_groups): hi.vertex_groups.remove(g)
    s = hi.modifiers.new("Sub", "SUBSURF"); s.levels = 2; s.render_levels = 2; s.uv_smooth = 'PRESERVE_BOUNDARIES'
    apply_mods(hi)
    hi.hide_render = True; full.hide_render = True
    for o in (hi, full): o.hide_set(True)
    return lo, hi, full, J

def eyes(J, mat, radius=0.0152):
    bm = bmesh.new(); bm.loops.layers.uv.verify()
    for side in ("l", "r"):
        c = Vector(J[f"{side}-eye"])
        g = bmesh.ops.create_uvsphere(bm, u_segments=20, v_segments=14, radius=radius, calc_uvs=True)
        bmesh.ops.rotate(bm, verts=g["verts"], cent=(0, 0, 0), matrix=Matrix.Rotation(math.radians(90), 3, 'X'))
        bmesh.ops.translate(bm, verts=g["verts"], vec=c)
    return _obj("PLR2_Eyes", bm, [mat])

WORKDIR = r"E:\Model game khủng long\characters\work\mh"
TEX = r"E:\Model game khủng long\textures"

def _img(name, size, noncolor=True, float_buffer=True):
    img = bpy.data.images.get(name)
    if img: bpy.data.images.remove(img)
    img = bpy.data.images.new(name, size, size, alpha=False, float_buffer=float_buffer)
    img.colorspace_settings.name = "Non-Color" if noncolor else "sRGB"
    return img

def _bake_mat(name):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True; nt = m.node_tree; nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    return m, nt, out

def setup_hi_material(hi):
    """high poly: plain skin + fine pore / wrinkle bump so the tangent normal bake carries micro detail"""
    m, nt, out = _bake_mat("_PLR2_HiBake")
    bs = nt.nodes.new("ShaderNodeBsdfPrincipled"); nt.links.new(bs.outputs[0], out.inputs[0])
    tc = nt.nodes.new("ShaderNodeTexCoord")
    pores = nt.nodes.new("ShaderNodeTexVoronoi"); pores.inputs["Scale"].default_value = 2200; pores.feature = 'F1'
    fine = nt.nodes.new("ShaderNodeTexNoise"); fine.inputs["Scale"].default_value = 700; fine.inputs["Detail"].default_value = 3
    nt.links.new(tc.outputs["Object"], pores.inputs["Vector"]); nt.links.new(tc.outputs["Object"], fine.inputs["Vector"])
    b1 = nt.nodes.new("ShaderNodeBump"); b1.inputs["Strength"].default_value = 0.25; b1.inputs["Distance"].default_value = 0.00025
    b2 = nt.nodes.new("ShaderNodeBump"); b2.inputs["Strength"].default_value = 0.15; b2.inputs["Distance"].default_value = 0.0004
    nt.links.new(pores.outputs["Distance"], b1.inputs["Height"]); nt.links.new(fine.outputs["Fac"], b2.inputs["Height"])
    nt.links.new(b1.outputs["Normal"], b2.inputs["Normal"]); nt.links.new(b2.outputs["Normal"], bs.inputs["Normal"])
    hi.data.materials.clear(); hi.data.materials.append(m)

def setup_lo_bake(lo, size=4096):
    """low poly material with the bake target node + an emission of the object position (for the position map)"""
    m, nt, out = _bake_mat("_PLR2_LoBake")
    em = nt.nodes.new("ShaderNodeEmission"); nt.links.new(em.outputs[0], out.inputs[0])
    tc = nt.nodes.new("ShaderNodeTexCoord")
    mp = nt.nodes.new("ShaderNodeMapping"); mp.vector_type = 'POINT'
    mp.inputs["Location"].default_value = (0.5, 0.5, 0.0); mp.inputs["Scale"].default_value = (0.5, 0.5, 0.5)
    nt.links.new(tc.outputs["Object"], mp.inputs["Vector"]); nt.links.new(mp.outputs[0], em.inputs["Color"])
    tn = nt.nodes.new("ShaderNodeTexImage"); tn.name = "BAKE_TARGET"; nt.nodes.active = tn
    lo.data.materials.clear(); lo.data.materials.append(m)
    return tn

def _bake(obj, kind, img, tn, samples=4, selected=None, extrusion=0.02, ray=0.05, margin=24):
    sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.samples = samples
    try: sc.cycles.device = 'GPU'
    except Exception: pass
    tn.image = img
    bk = sc.render.bake; bk.margin = margin; bk.use_selected_to_active = selected is not None
    kw = dict(type=kind)
    if selected is not None:
        bk.cage_extrusion = extrusion; bk.max_ray_distance = ray
    if kind == 'NORMAL': bk.normal_space = 'TANGENT'
    objs = [obj] + ([selected] if selected is not None else [])
    for o in objs: o.hide_set(False); o.hide_render = False
    return pf_ops.run(bpy.ops.object.bake, objs, active=obj, **kw)

def save_png16(img, path):
    sc = bpy.context.scene
    s = sc.render.image_settings
    s.file_format = 'PNG'; s.color_depth = '16'; s.color_mode = 'RGB'
    img.save_render(path, scene=sc)

def save_npy(img, path, dtype="float16"):
    import numpy as np
    w, h = img.size
    a = np.empty(w * h * 4, dtype=np.float32); img.pixels.foreach_get(a)
    a = a.reshape(h, w, 4)[::-1, :, :3]          # top row first (image convention)
    np.save(path, a.astype(dtype))

def save_png8(img, path):
    img.filepath_raw = path; img.file_format = 'PNG'; img.save()

# ------------------------------------------------------------------ hair / beard cards
import random
from mathutils.bvhtree import BVHTree

def body_bvh(body):
    dg = bpy.context.evaluated_depsgraph_get()
    return BVHTree.FromObject(body, dg)

def hairline(p):
    """same hair region as the skin paint (skin_paint.py)"""
    ax = abs(p.x)
    hl_front = 1.805 - 0.35 * max(0.0, ax - 0.035) ** 1.2
    wb = min(1.0, max(0.0, (p.y + 0.05) / 0.1)); wb = wb * wb * (3 - 2 * wb)
    hl = hl_front * (1 - wb) + (1.60 + 0.3 * max(0.0, ax - 0.03)) * wb
    if ax > 0.058 and -0.08 < p.y < 0.035:
        hl = max(hl, 1.748 + 0.35 * max(0.0, -p.y - 0.03))
    return hl

def _push_out(bvh, p, off):
    q, n, i, d = bvh.find_nearest(p)
    if q is None: return p, Vector((0, 0, 1))
    v = p - q
    if v.dot(n) < off: p = q + n * off
    return p, n

def _strip(bm, uvl, pts, sides, widths, u0, u1, v0=1.0, v1=0.08):
    rows = []
    L = len(pts)
    for k, (p, s, w) in enumerate(zip(pts, sides, widths)):
        v = v0 + (v1 - v0) * k / (L - 1)
        rows.append((bm.verts.new(p - s * w / 2), bm.verts.new(p + s * w / 2), v))
    for k in range(L - 1):
        a, b, va = rows[k]; c, d, vb = rows[k + 1]
        f = bm.faces.new((a, b, d, c))
        for lp, uv in zip(f.loops, ((u0, va), (u1, va), (u1, vb), (u0, vb))): lp[uvl].uv = uv

def grow(bvh, root, d0, L, off0, off1, segs, gravity=0.35, lift=0.0, curl=0.0, rng=None):
    pts = [root.copy()]; p = root.copy(); d = d0.normalized(); sides = []
    _, n0 = _push_out(bvh, p, off0)
    for k in range(1, segs + 1):
        t = k / segs
        off = off0 + (off1 - off0) * t
        d = (d + Vector((0, 0, -1)) * gravity / segs * 2).normalized()
        if curl and rng: d = (d + Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-0.3, 0.3))) * curl).normalized()
        p = p + d * (L / segs)
        p, n = _push_out(bvh, p, off)
        # keep direction tangent when hugging the surface
        q, nn, _, dist = bvh.find_nearest(p)
        if dist is not None and dist < off * 1.6:
            d = (d - nn * d.dot(nn) + nn * lift).normalized()
        pts.append(p.copy())
    for k in range(len(pts)):
        a = pts[max(0, k - 1)]; b = pts[min(len(pts) - 1, k + 1)]
        tang = (b - a).normalized()
        q, nn, _, _ = bvh.find_nearest(pts[k])
        nn = nn if nn is not None else Vector((0, 0, 1))
        s = tang.cross(nn)
        if s.length < 1e-4: s = tang.cross(Vector((0, 0, 1)))
        sides.append(s.normalized())
    return pts, sides

def scalp_roots(body, n, seed, zmin=None):
    rng = random.Random(seed)
    cand = [p for p in body.data.polygons if p.center.z > hairline(p.center) + 0.004]
    wts = [p.area for p in cand]
    return [(c.center.copy(), c.normal.copy()) for c in rng.choices(cand, weights=wts, k=n)], rng

def flow_dir(p, nrm, rng):
    """messy medium-long hair: loose centre part, swept back from the forehead, falls to the sides / back"""
    ax = abs(p.x); sx = 1.0 if p.x >= 0 else -1.0
    back = min(1.0, max(0.0, (p.y + 0.06) / 0.1))
    want = Vector((sx * (0.5 + 0.6 * (1 - back)) * min(1.0, ax / 0.02 + 0.3), 0.9 + 0.6 * back, -0.25 - 0.9 * back))
    want += Vector((rng.uniform(-0.35, 0.35), rng.uniform(-0.2, 0.2), rng.uniform(-0.2, 0.2)))
    return (want - nrm * want.dot(nrm)).normalized()

def hair_cards(body, mat, seed=7):
    bvh = body_bvh(body)
    bm = bmesh.new(); uvl = bm.loops.layers.uv.verify()
    layers = [  # count, length range, offset root/tip, width, gravity, curl, lift
        (700, (0.06, 0.10), (0.003, 0.011), (0.010, 0.016), 0.12, 0.12, 0.10),
        (420, (0.11, 0.19), (0.009, 0.018), (0.010, 0.018), 0.55, 0.16, 0.05),
        (60, (0.07, 0.13), (0.014, 0.022), (0.005, 0.009), 0.40, 0.28, 0.0),
    ]
    for li, (cnt, lr, (o0, o1), wr, grav, curl, lift) in enumerate(layers):
        roots, rng = scalp_roots(body, cnt, seed + li * 101)
        for root, nrm in roots:
            back = min(1.0, max(0.0, (root.y + 0.06) / 0.1))
            d = flow_dir(root, nrm, rng)
            L = rng.uniform(*lr) * (0.75 + 0.45 * back)
            pts, sides = grow(bvh, root + nrm * o0, d, L, o0, o1, 6, gravity=grav, lift=lift, curl=curl, rng=rng)
            w0 = rng.uniform(*wr)
            widths = [w0 * (1 - 0.55 * (k / 6) ** 1.3) for k in range(7)]
            u0 = rng.uniform(0.0, 0.42); _strip(bm, uvl, pts, sides, widths, u0, u0 + 0.07, 1.0, 0.1)
    return _obj("PLR2_Hair", bm, [mat])

def in_beard(p):
    """same region as the painted beard in skin_paint.py"""
    import numpy as np
    ax = abs(p.x)
    top = float(np.interp(ax, [0, 0.022, 0.03, 0.055, 0.07, 0.085, 0.1], [1.668, 1.668, 1.672, 1.688, 1.72, 1.745, 1.75]))
    if p.z > top - 0.004: return False
    if p.y > -0.062: return False
    zb = 1.563 if p.y < -0.115 else 1.563 + (p.y + 0.115) * 0.85
    if p.z < zb + 0.004: return False
    lipd = ((p.x / 0.0265) ** 2 + ((p.z - 1.651) / (0.0125 if p.z > 1.651 else 0.0145)) ** 2) ** 0.5
    if lipd < 1.05 and p.y < -0.13: return False
    if (Vector((p.x, p.y, (p.z - 1.683) * 1.4)) - Vector((0, -0.1755, 0))).length < 0.026: return False
    return True

def beard_cards(body, mat, n=520, seed=11):
    bvh = body_bvh(body); rng = random.Random(seed)
    # only along the lower edge of the beard (jaw line / under the chin), where the silhouette shows
    def lower_edge(c):
        if not in_beard(c): return False
        zb = 1.563 if c.y < -0.115 else 1.563 + (c.y + 0.115) * 0.85
        return c.z < zb + 0.03 or (c.y > -0.1 and c.z < 1.62)
    cand = [p for p in body.data.polygons if lower_edge(p.center)]
    bm = bmesh.new(); uvl = bm.loops.layers.uv.verify()
    for poly in rng.choices(cand, weights=[p.area for p in cand], k=n):
        c = poly.center; nrm = poly.normal
        chin = c.z < 1.625 and abs(c.x) < 0.045
        moust = c.z > 1.655
        down = Vector((0, -0.3 if chin else 0.0, -1.0))
        d = (down + nrm * (0.25 if not moust else 0.1) + Vector((rng.uniform(-0.35, 0.35), 0, 0))).normalized()
        L = rng.uniform(0.006, 0.011) * (1.6 if chin else 1.0)
        pts, sides = grow(bvh, c + nrm * 0.0008, d, L, 0.0008, 0.002 + L * 0.2, 2, gravity=0.1)
        w0 = rng.uniform(0.002, 0.004)
        u0 = rng.uniform(0.0, 0.42)
        _strip(bm, uvl, pts, sides, [w0, w0 * 0.8, w0 * 0.5], u0, u0 + 0.02, 0.9, 0.35)
    return _obj("PLR2_Beard", bm, [mat])

# ------------------------------------------------------------------ v4: expressions (shape keys), mouth, eyes, lashes
MOUTH = ["helper-upper-teeth", "helper-lower-teeth", "helper-tongue"]

def _add_shapes(ob, stem):
    import numpy as np
    z = np.load(f"{WORKDIR}\\{stem}_shapes.npz")
    if not ob.data.shape_keys: ob.shape_key_add(name="Basis", from_mix=False)
    base = np.empty(len(ob.data.vertices) * 3, np.float32); ob.data.vertices.foreach_get("co", base); base = base.reshape(-1, 3)
    for k in z.files:
        sk = ob.shape_key_add(name=k, from_mix=False)
        sk.data.foreach_set("co", (base + z[k]).ravel()); sk.value = 0.0
    ob.active_shape_key_index = 0
    return list(z.files)

def _keep_groups(ob, groups, name):
    """copy ob keeping only the verts in the given vertex groups (shape keys survive through bmesh)"""
    o = ob.copy(); o.data = ob.data.copy(); o.name = name; o.data.name = name
    bpy.context.scene.collection.objects.link(o)
    idx = {o.vertex_groups[g].index for g in groups if g in o.vertex_groups}
    bm = bmesh.new(); bm.from_mesh(o.data); dl = bm.verts.layers.deform.verify()
    kill = [v for v in bm.verts if not any(gi in v[dl] for gi in idx)]
    bmesh.ops.delete(bm, geom=kill, context='VERTS'); bm.to_mesh(o.data); bm.free()
    for g in list(o.vertex_groups): o.vertex_groups.remove(g)
    return o

def transfer_shapes(src, dst):
    """shape keys src -> dst by barycentric sampling of the src basis surface (dst = decimated copy)"""
    import numpy as np
    from mathutils.geometry import barycentric_transform
    me = src.data; keys = me.shape_keys.key_blocks
    tris = [(p.index, tuple(p.vertices[i] for i in (0, k, k + 1))) for p in me.polygons for k in range(1, len(p.vertices) - 1)]
    verts = [v.co.copy() for v in me.vertices]
    bvh = BVHTree.FromPolygons(verts, [t for _, t in tris])
    if not dst.data.shape_keys: dst.shape_key_add(name="Basis", from_mix=False)
    samples = []
    for v in dst.data.vertices:
        q, n, ti, d = bvh.find_nearest(v.co)
        a, b, c = tris[ti][1]
        A, B, C = verts[a], verts[b], verts[c]
        # barycentric weights of q in ABC
        v0 = B - A; v1 = C - A; v2 = q - A
        d00 = v0.dot(v0); d01 = v0.dot(v1); d11 = v1.dot(v1); d20 = v2.dot(v0); d21 = v2.dot(v1)
        den = d00 * d11 - d01 * d01 or 1e-12
        wb = (d11 * d20 - d01 * d21) / den; wc = (d00 * d21 - d01 * d20) / den; wa = 1 - wb - wc
        samples.append((a, b, c, wa, wb, wc))
    S = np.array(samples)
    ia, ib, ic = S[:, 0].astype(int), S[:, 1].astype(int), S[:, 2].astype(int); w = S[:, 3:6]
    basis = np.array([v.co for v in me.vertices])
    dco = np.empty(len(dst.data.vertices) * 3, np.float32); dst.data.vertices.foreach_get("co", dco); dco = dco.reshape(-1, 3)
    for kb in keys[1:]:
        kco = np.empty(len(me.vertices) * 3, np.float32); kb.data.foreach_get("co", kco); kco = kco.reshape(-1, 3)
        dl = kco - basis
        off = dl[ia] * w[:, :1] + dl[ib] * w[:, 1:2] + dl[ic] * w[:, 2:3]
        sk = dst.shape_key_add(name=kb.name, from_mix=False)
        sk.data.foreach_set("co", (dco + off).astype(np.float32).ravel()); sk.value = 0.0
    dst.active_shape_key_index = 0

def build_bodies_v4(stem="survivor_v4", ratio=None):
    J = pf_mh.joints(stem)
    for n in ("PLR2_Body", "PLR2_Body_full", "PLR2_Mouth", "_mh_all"):
        if n in bpy.data.objects: bpy.data.objects.remove(bpy.data.objects[n], do_unlink=True)
    allo = pf_mh.import_body(stem, "_mh_all")
    pf_mh.assign_groups(allo, stem)
    shapes = _add_shapes(allo, stem)
    full = _keep_groups(allo, ["body"], "PLR2_Body_full")
    mouth = _keep_groups(allo, MOUTH, "PLR2_Mouth")
    bpy.data.objects.remove(allo, do_unlink=True)
    lo = full.copy(); lo.data = full.data.copy(); lo.name = "PLR2_Body"; lo.data.name = "PLR2_Body"
    bpy.context.scene.collection.objects.link(lo)
    if ratio:   # decimated LOD (expressions sampled from the full mesh; lips can web when the mouth opens)
        lo.shape_key_clear()
        m = lo.modifiers.new("Dec", "DECIMATE"); m.ratio = ratio; m.use_symmetry = True; m.symmetry_axis = 'X'; m.use_collapse_triangulate = True
        apply_mods(lo)
        transfer_shapes(full, lo)
    full.hide_render = True; full.hide_set(True)
    return lo, full, mouth, J, shapes

def eyes_v4(J, mat, radius=0.0152, cornea=0.0013):
    """UV-sphere eyes with a corneal bulge (front pole at v=1 faces -Y)"""
    bm = bmesh.new(); bm.loops.layers.uv.verify()
    for side in ("l", "r"):
        c = Vector(J[f"{side}-eye"])
        g = bmesh.ops.create_uvsphere(bm, u_segments=32, v_segments=24, radius=radius, calc_uvs=True)
        for v in g["verts"]:
            th = math.degrees(math.acos(max(-1.0, min(1.0, v.co.z / radius))))
            if th < 34: v.co = v.co * (1 + (cornea / radius) * math.cos(th / 34 * math.pi / 2) ** 2)
        bmesh.ops.rotate(bm, verts=g["verts"], cent=(0, 0, 0), matrix=Matrix.Rotation(math.radians(90), 3, 'X'))
        bmesh.ops.translate(bm, verts=g["verts"], vec=c)
    return _obj("PLR2_Eyes", bm, [mat])

def mouth_uv(mouth):
    """teeth -> left of the hidden back rows of the eye texture, tongue/gums -> right"""
    me = mouth.data; uvl = me.uv_layers.active or me.uv_layers.new()
    for p in me.polygons:
        tongue = p.center.z < 1.652 and abs(p.center.x) < 0.02 and p.center.y > -0.15 and len(p.vertices) >= 3
        for li in p.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            uu = 0.05 + 0.4 * ((co.x + 0.03) / 0.06 % 1.0)
            vv = 0.02 + 0.12 * ((co.z - 1.63) / 0.05 % 1.0)
            uvl.data[li].uv = (uu + (0.5 if tongue else 0.0), vv)

def _aperture(body_bvh_, c, r, step=0.0004):
    """frontal scan: visible eyeball columns -> upper / lower lid margin points (3D, on the lids)"""
    import numpy as np
    xs = np.arange(c.x - 0.022, c.x + 0.022, step); zs = np.arange(c.z - 0.016, c.z + 0.016, step)
    top = {}; bot = {}
    for x in xs:
        vis = []
        for z in zs:
            o = Vector((x, c.y - 0.1, z)); d = Vector((0, 1, 0))
            # analytic sphere hit
            oc = o - c; b = oc.dot(d); cc = oc.dot(oc) - r * r; disc = b * b - cc
            if disc < 0: continue
            ts = -b - math.sqrt(disc)
            hit = body_bvh_.ray_cast(o, d, 0.2)
            if hit[0] is None or hit[3] > ts + 1e-5: vis.append(z)
        if len(vis) >= 2:
            top[x] = max(vis); bot[x] = min(vis)
    return top, bot

def lashes(body, J, mat, seed=5):
    bvh = body_bvh(body); rng = random.Random(seed)
    bm = bmesh.new(); uvl = bm.loops.layers.uv.verify()
    for side in ("l", "r"):
        c = Vector(J[f"{side}-eye"]); sx = 1 if side == "l" else -1
        top, bot = _aperture(bvh, c, 0.0152)
        for lid, pts_d, L0, up in (("upper", top, 0.0085, 1), ("lower", bot, 0.0042, -1)):
            xs = sorted(pts_d)
            if len(xs) < 4: continue
            xs = xs[1:-1]
            N = 16 if lid == "upper" else 12
            sel = [xs[int(round(i * (len(xs) - 1) / (N - 1)))] for i in range(N)]
            rows = []
            for i, x in enumerate(sel):
                z = pts_d[x] + up * 0.0005
                hit = bvh.ray_cast(Vector((x, c.y - 0.1, z)), Vector((0, 1, 0)), 0.2)
                if hit[0] is None: continue
                p, n = hit[0], hit[1]
                t = i / (N - 1); prof = math.sin(math.pi * (0.15 + 0.85 * t if sx > 0 else 1 - 0.85 * t)) ** 0.7
                L = L0 * (0.55 + 0.45 * prof)
                outx = (x - c.x) / 0.02
                d1 = Vector((outx * 0.25, -0.9, up * 0.45)).normalized()
                d2 = Vector((outx * 0.3, -0.4, up * 1.0)).normalized()
                root = p + n * 0.0003
                rows.append([root, root + d1 * L * 0.55, root + d1 * L * 0.55 + d2 * L * 0.5])
            for i in range(len(rows) - 1):
                u0 = 0.05 + 0.35 * i / (len(rows) - 1); u1 = 0.05 + 0.35 * (i + 1) / (len(rows) - 1)
                for k in range(2):
                    a = bm.verts.new(rows[i][k]); b = bm.verts.new(rows[i + 1][k]); cc = bm.verts.new(rows[i + 1][k + 1]); d = bm.verts.new(rows[i][k + 1])
                    f = bm.faces.new((a, b, cc, d))
                    v0 = 0.93 - 0.34 * k; v1 = 0.93 - 0.34 * (k + 1)
                    for lp, uv in zip(f.loops, ((u0, v0), (u1, v0), (u1, v1), (u0, v1))): lp[uvl].uv = uv
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-6)
    return _obj("PLR2_Lashes", bm, [mat])

def brow_cards(body, mat, n=650, seed=13):
    """short hair cards lying on the painted brows for 3D volume (same brow curve as skin_paint.py)"""
    bvh = body_bvh(body); rng = random.Random(seed)
    bm = bmesh.new(); uvl = bm.loops.layers.uv.verify()
    BROW_Z = 1.7425
    for i in range(n):
        u = rng.uniform(0.012, 0.06); sx = rng.choice((1, -1))
        bz = BROW_Z + 0.004 * (1 - ((u - 0.032) / 0.026) ** 2) - 0.005 * max(0, (u - 0.046) / 0.016)
        th = 0.0085 - 0.0045 * min(1, max(0, (u - 0.014) / 0.05))
        z = bz + rng.uniform(-0.8, 0.8) * th
        hit = bvh.ray_cast(Vector((sx * u, -0.3, z)), Vector((0, 1, 0)), 0.5)
        if hit[0] is None: continue
        p, nrm = hit[0], hit[1]
        ang = 0.9 - 0.75 * min(1, max(0, (u - 0.012) / 0.035))       # inner hairs point up, outer sideways
        d = Vector((sx * math.cos(ang), 0, math.sin(ang)))
        d = (d - nrm * d.dot(nrm)).normalized()
        L = rng.uniform(0.005, 0.009); w = rng.uniform(0.0012, 0.002)
        d = (d + Vector((0, 0, rng.uniform(-0.25, 0.25)))).normalized()
        side = d.cross(nrm).normalized()
        pts = [p + nrm * 0.0003, p + d * L * 0.5 + nrm * 0.0009, p + d * L + nrm * 0.0005]
        rows = []
        for k, q in enumerate(pts):
            ww = w * (1 - 0.4 * k / 2); v = 0.9 - 0.25 * k / 2
            rows.append((bm.verts.new(q - side * ww / 2), bm.verts.new(q + side * ww / 2), v))
        u0 = rng.uniform(0.0, 0.45)
        for k in range(2):
            A, B, va = rows[k]; C, D, vb = rows[k + 1]
            f = bm.faces.new((A, B, D, C))
            for lp, uv in zip(f.loops, ((u0, va), (u0 + 0.012, va), (u0 + 0.012, vb), (u0, vb))): lp[uvl].uv = uv
    return _obj("PLR2_Brows", bm, [mat])


# ------------------------------------------------------------------ tied-back hair (leather tie at the nape + tail)
TIE = Vector((0.0, 0.082, 1.655))

def grow_to(bvh, root, target, off0, off1, segs, rng, wobble=0.004):
    """strand from root that hugs the scalp and converges on target (the tie)"""
    pts = [root.copy()]; p = root.copy()
    for k in range(1, segs + 1):
        t = k / segs
        q = root.lerp(target, t)
        q += Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1))) * wobble * math.sin(math.pi * t)
        q, n = _push_out(bvh, q, off0 + (off1 - off0) * t)
        pts.append(q)
    sides = []
    for k in range(len(pts)):
        a = pts[max(0, k - 1)]; b = pts[min(len(pts) - 1, k + 1)]
        tang = (b - a).normalized()
        _, nn, _, _ = bvh.find_nearest(pts[k])
        nn = nn if nn is not None else Vector((0, 0, 1))
        s_ = tang.cross(nn); sides.append(s_.normalized() if s_.length > 1e-5 else Vector((1, 0, 0)))
    return pts, sides

def tied_hair(body, hair_mat, cloth_mat, seed=17, scale_n=1.0, scale_w=1.0, name="PLR2_Hair", with_tie=True, tail_n=240):
    bvh = body_bvh(body); rng = random.Random(seed)
    bm = bmesh.new(); uvl = bm.loops.layers.uv.verify()
    # scalp layer: every strand runs back to the tie
    for li, (cnt, (o0, o1), wr) in enumerate(((760, (0.0025, 0.012), (0.010, 0.015)), (420, (0.006, 0.016), (0.011, 0.017)))):
        roots, _ = scalp_roots(body, max(20, int(cnt * scale_n)), seed + li * 7)
        for root, nrm in roots:
            if (root - TIE).length < 0.03: continue
            if abs(root.x) > 0.058 and root.z < 1.76 and root.y < 0.03: continue     # above the ears: avoid sharp bends
            if abs(root.x) > 0.045 and root.z < hairline(root) + 0.014 and root.y < 0.0: continue   # temple hairline: painted only
            tgt = TIE + Vector((rng.uniform(-0.012, 0.012), rng.uniform(-0.004, 0.004), rng.uniform(-0.01, 0.01)))
            pts, sides = grow_to(bvh, root + nrm * o0, tgt, o0, o1, 5, rng)
            w0 = rng.uniform(*wr) * scale_w
            widths = [w0 * (1 - 0.35 * (k / 5)) for k in range(6)]
            u0 = rng.uniform(0.0, 0.42); _strip(bm, uvl, pts, sides, widths, u0, u0 + 0.07, 0.97, 0.45)
    # tail: bundle of cards hanging from the tie down the upper back
    axis0 = TIE + Vector((0, 0.012, -0.005))
    for i in range(max(12, int(tail_n * scale_n))):
        a = rng.uniform(0, 2 * math.pi); rr = rng.uniform(0.0, 0.014)
        root = axis0 + Vector((math.cos(a) * rr, 0.006 + math.sin(a) * rr * 0.6, rng.uniform(-0.004, 0.004)))
        L = rng.uniform(0.16, 0.27)
        d = Vector((math.cos(a) * 0.12, 0.35, -1.0)).normalized()
        pts = [root]; p = root.copy(); dd = d.copy()
        for k in range(1, 9):
            dd = (dd + Vector((0, 0, -1)) * 0.12 + Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), 0)) * 0.05).normalized()
            p = p + dd * (L / 8)
            p, _ = _push_out(bvh, p, 0.014 + 0.02 * k / 8)     # rests on the back / pelt
            pts.append(p.copy())
        sides = []
        for k in range(len(pts)):
            tang = (pts[min(len(pts) - 1, k + 1)] - pts[max(0, k - 1)]).normalized()
            radial = (pts[k] - Vector((axis0.x, pts[k].y, pts[k].z))).normalized() if abs(pts[k].x - axis0.x) > 1e-4 else Vector((1, 0, 0))
            s_ = tang.cross(Vector((0, 1, 0))) if rng.random() < 0.5 else tang.cross(radial)
            sides.append(s_.normalized() if s_.length > 1e-5 else Vector((1, 0, 0)))
        w0 = rng.uniform(0.010, 0.018) * scale_w
        widths = [w0 * (1 - 0.6 * (k / 8) ** 1.2) for k in range(9)]
        u0 = rng.uniform(0.0, 0.42); _strip(bm, uvl, pts, sides, widths, u0, u0 + 0.07, 0.95, 0.08)
    hair = _obj(name, bm, [hair_mat])
    if not with_tie: return hair, None
    # leather tie wrapped around the gathered hair
    tb = bmesh.new(); tuv = tb.loops.layers.uv.verify()
    import pf_garments_v2 as G
    for k in range(3):
        c = TIE + Vector((0, 0.014 + 0.004 * k, -0.012 - 0.009 * k))
        ring = [c + Vector((math.cos(t) * 0.017, math.sin(t) * 0.012 * 0.8, 0.003 * math.sin(3 * t))) for t in [2 * math.pi * i / 14 for i in range(14)]]
        G.tube(tb, tuv, ring, 0.0035, 6, closed=True, uv=(0.52, 0.98, 0.52, 0.98))
    tie = _obj("PLR2_HairTie", tb, [cloth_mat])
    return hair, tie

def mouth_parts(stem, eye_mat):
    """teeth (ivory, smoothed) and tongue as separate UV regions of the eye texture's hidden back rows; keeps shape keys"""
    for n in ("PLR2_Mouth", "PLR2_Teeth", "_mh_all2"):
        if n in bpy.data.objects: bpy.data.objects.remove(bpy.data.objects[n], do_unlink=True)
    allo = pf_mh.import_body(stem, "_mh_all2"); pf_mh.assign_groups(allo, stem); _add_shapes(allo, stem)
    teeth = _keep_groups(allo, ["helper-upper-teeth", "helper-lower-teeth"], "PLR2_Teeth")
    tongue = _keep_groups(allo, ["helper-tongue"], "PLR2_Tongue")
    bpy.data.objects.remove(allo, do_unlink=True)
    for o, u0 in ((teeth, 0.05), (tongue, 0.55)):
        me = o.data; uvl = me.uv_layers.active or me.uv_layers.new()
        for p in me.polygons:
            for li in p.loop_indices:
                co = me.vertices[me.loops[li].vertex_index].co
                uvl.data[li].uv = (u0 + 0.4 * ((co.x + 0.03) / 0.06 % 1.0), 0.02 + 0.12 * ((co.z - 1.62) / 0.06 % 1.0))
        me.materials.clear(); me.materials.append(eye_mat)
        for k in me.shape_keys.key_blocks[1:]: k.value = 0.0
    # join into one mouth object (shape keys match by name)
    for o in (teeth, tongue): o.select_set(False)
    import pf_ops
    pf_ops.run(bpy.ops.object.join, [teeth, tongue], active=teeth)
    teeth.name = "PLR2_Mouth"; teeth.data.name = "PLR2_Mouth"
    return teeth

def _arch_teeth(bm, uvl, upper, y_front, k, z_edge, u0):
    """one dental arch of ellipsoid teeth + a gum ribbon; returns nothing (adds to bm)"""
    dims = [(4.4, 6.0, 9.0), (3.6, 5.5, 8.0), (4.2, 7.0, 8.5), (3.6, 7.5, 7.0), (3.5, 7.5, 6.5), (5.2, 9.0, 6.0), (5.0, 9.0, 5.5)]
    if not upper: dims = [(3.0, 5.5, 8.0), (3.2, 5.8, 8.0), (3.8, 6.5, 8.5), (3.6, 7.0, 7.0), (3.6, 7.5, 6.5), (5.4, 9.0, 5.8), (5.2, 9.0, 5.5)]
    sgn = 1 if upper else -1
    def arch(x): return Vector((x, y_front + k * x * x, 0))
    def tangent(x): return Vector((1, 2 * k * x, 0)).normalized()
    for side in (1, -1):
        s = 0.0; x = 0.0
        for (w, t, h) in dims:
            w, t, h = w * 0.00105, t * 0.00105, h * 0.00105
            # advance along the arch by half the tooth width
            for _ in range(20):
                x += side * (w / 2) / 20 * abs(tangent(x).x)
            c = arch(x); tg = tangent(x) * side; nrm = Vector((0, 0, 1)).cross(tg).normalized()
            if nrm.y > 0: nrm = -nrm            # labial side points forward
            g = bmesh.ops.create_uvsphere(bm, u_segments=8, v_segments=6, radius=1.0, calc_uvs=False)
            for v in g["verts"]:
                lx, ly, lz = v.co.x * w / 2, v.co.y * t / 2, v.co.z * h / 2
                lz = max(-h * 0.42, min(h * 0.5, lz))         # flat biting edge
                edge_z = z_edge + sgn * (h / 2 + 0.0003)
                v.co = c + tg * lx + nrm * (-ly) + Vector((0, 0, edge_z + sgn * lz))
            for f in bm.faces:
                if all(v in g["verts"] for v in f.verts):
                    for lp in f.loops: lp[uvl].uv = (u0 + 0.35 * ((lp.vert.co.x + 0.03) / 0.06), 0.03 + 0.1 * ((lp.vert.co.z - 1.63) / 0.05))
            for _ in range(20):
                x += side * (w / 2) / 20 * abs(tangent(x).x)
        # gum ribbon from the centre to the last molar, just behind the lips
        pass
    xs = [(-0.026 + 0.052 * i / 16) for i in range(17)]
    rows = []
    for x in xs:
        c = arch(x); nrm = Vector((0, 0, 1)).cross(tangent(x)).normalized()
        if nrm.y > 0: nrm = -nrm
        base = z_edge + sgn * 0.0085
        rows.append([bm.verts.new(c + nrm * 0.0012 + Vector((0, 0, base))), bm.verts.new(c + nrm * 0.0008 + Vector((0, 0, base + sgn * 0.006))),
                     bm.verts.new(c - nrm * 0.004 + Vector((0, 0, base + sgn * 0.008)))])
    for i in range(len(rows) - 1):
        for j in range(2):
            f = bm.faces.new((rows[i][j], rows[i + 1][j], rows[i + 1][j + 1], rows[i][j + 1]))
            for lp in f.loops: lp[uvl].uv = (0.6 + 0.3 * i / 16, 0.05 + 0.04 * j)

def new_teeth(stem, eye_mat):
    for n in ("PLR2_Mouth", "_mh_all3", "_up", "_lo", "_tg"):
        if n in bpy.data.objects: bpy.data.objects.remove(bpy.data.objects[n], do_unlink=True)
    allo = pf_mh.import_body(stem, "_mh_all3"); pf_mh.assign_groups(allo, stem); _add_shapes(allo, stem)
    up = _keep_groups(allo, ["helper-upper-teeth"], "_up"); lo = _keep_groups(allo, ["helper-lower-teeth"], "_lo")
    tg = _keep_groups(allo, ["helper-tongue"], "PLR2_Tongue")
    bpy.data.objects.remove(allo, do_unlink=True)
    parts = []
    for upper, src in ((True, up), (False, lo)):
        bm = bmesh.new(); uvl = bm.loops.layers.uv.verify()
        _arch_teeth(bm, uvl, upper, -0.1505 if upper else -0.1488, 44.0, 1.6515 if upper else 1.6508, 0.05)
        o = _obj("PLR2_TeethU" if upper else "PLR2_TeethL", bm, [eye_mat])
        transfer_shapes(src, o); parts.append(o)
    me = tg.data; uvl = me.uv_layers.active or me.uv_layers.new()
    for p in me.polygons:
        for li in p.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            uvl.data[li].uv = (0.55 + 0.4 * ((co.x + 0.03) / 0.06 % 1.0), 0.02 + 0.12 * ((co.z - 1.62) / 0.06 % 1.0))
    me.materials.clear(); me.materials.append(eye_mat)
    for o in (up, lo): bpy.data.objects.remove(o, do_unlink=True)
    import pf_ops
    pf_ops.run(bpy.ops.object.join, parts + [tg], active=parts[0])
    m = parts[0]; m.name = "PLR2_Mouth"; m.data.name = "PLR2_Mouth"
    for k in m.data.shape_keys.key_blocks[1:]: k.value = 0.0
    return m


def build_hi_v4(stem="survivor_v4_hi"):
    if "PLR2_Body_hi" in bpy.data.objects: bpy.data.objects.remove(bpy.data.objects["PLR2_Body_hi"], do_unlink=True)
    hi = pf_mh.import_body(stem, "PLR2_Body_hi")
    pf_mh.assign_groups(hi, stem); pf_mh.split_off(hi, HELPERS)
    for g in list(hi.vertex_groups): hi.vertex_groups.remove(g)
    s = hi.modifiers.new("Sub", "SUBSURF"); s.levels = 2; s.render_levels = 2; s.uv_smooth = 'PRESERVE_BOUNDARIES'
    apply_mods(hi); hi.hide_render = True; hi.hide_set(True)
    return hi
