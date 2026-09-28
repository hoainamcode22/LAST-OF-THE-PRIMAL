# PRIMAL FRONTIER - survival milestone 1 props (original designs): hide A-frame tent, rain collector (hide funnel on a
# lashed tripod over a wooden basin), leaf cup, burnt meat. Built on the XY ground plane, Z up, entrance of the tent
# towards -Y (= Unity +Z after export). Shared M_Tools atlas (pf_tools) where possible.
import bpy, bmesh, math, random, os
from mathutils import Vector, noise
import pf_tools as T
import pf_common as C

UA = os.path.join(C.UNITY, "Assets", "_Project")
OUT = os.path.join(UA, "Art", "Models", "Props")
ICONS = os.path.join(UA, "Art", "Icons")

def _panel(b, corners, nu, nv, reg, sag=0.03, thick=0.012, seed=0, uvs=1.0):
    """double-sided hide panel between 4 corners (p00, p10, p11, p01), gently sagging"""
    p00, p10, p11, p01 = [Vector(c) for c in corners]
    n = (p10 - p00).cross(p01 - p00).normalized()
    grid = {}
    for side in (0, 1):
        for i in range(nu + 1):
            for j in range(nv + 1):
                u, v = i / nu, j / nv
                p = p00.lerp(p10, u).lerp(p01.lerp(p11, u), v)
                s = math.sin(math.pi * u) * math.sin(math.pi * v) * sag + 0.006 * noise.noise(Vector((u * 5, v * 5, seed)))
                p = p - n * s + (n * -thick if side else Vector())
                grid[(side, i, j)] = b.bm.verts.new(p)
    fs = []
    for side in (0, 1):
        for i in range(nu):
            for j in range(nv):
                q = [grid[(side, i, j)], grid[(side, i + 1, j)], grid[(side, i + 1, j + 1)], grid[(side, i, j + 1)]]
                fs.append(b.bm.faces.new(q if side == 0 else list(reversed(q))))
    b._map(fs, reg, lambda co: ((co - p00).dot((p10 - p00).normalized()) * uvs, (co - p00).dot((p01 - p00).normalized()) * uvs))
    return fs

def tent(col=None):
    random.seed(51); b = T.Builder(51)
    H, W, Lh = 1.55, 1.05, 1.1            # ridge height, half width at the ground, half length
    for y in (-Lh + 0.05, Lh - 0.05):      # inverted V pole pairs, crossing above the ridge
        for s in (-1, 1):
            b.cyl("wood", (s * (W + 0.05), y, -0.05), (-s * 0.12, y, H + 0.14), 0.035, 0.028, segs=6, rings=3, jitter=0.08)
        b.wrap("cord", (0, y, H), (0, 1, 0), 0.05, 0.05, turns=3, thick=0.006)
    b.cyl("wood", (0, -Lh - 0.2, H + 0.02), (0, Lh + 0.2, H + 0.02), 0.035, 0.03, segs=6, rings=5, jitter=0.06)   # ridge pole
    for s in (-1, 1):                     # hide roof / walls
        _panel(b, [(s * 0.03, -Lh, H), (s * 0.03, Lh, H), (s * (W + 0.02), Lh, 0.03), (s * (W + 0.02), -Lh, 0.03)][::1] if s > 0 else
                  [(s * 0.03, Lh, H), (s * 0.03, -Lh, H), (s * (W + 0.02), -Lh, 0.03), (s * (W + 0.02), Lh, 0.03)], 8, 5, "leather", sag=0.05, seed=s)
        for y in (-Lh + 0.1, 0.0, Lh - 0.1):   # pegs + cords at the foot
            b.cyl("wood", (s * (W + 0.18), y, -0.08), (s * (W + 0.2), y, 0.14), 0.018, 0.014, segs=5, rings=1)
            b.cyl("cord", (s * (W + 0.02), y, 0.05), (s * (W + 0.19), y, 0.12), 0.005, 0.005, segs=4, rings=1, cap=False)
    # closed back (+Y): triangle of hide
    tri_v = [b.bm.verts.new(p) for p in ((-W, Lh - 0.02, 0.03), (W, Lh - 0.02, 0.03), (0, Lh - 0.02, H - 0.03))]
    tri_b = [b.bm.verts.new(p) for p in ((-W, Lh + 0.0, 0.03), (0, Lh + 0.0, H - 0.03), (W, Lh + 0.0, 0.03))]
    f1 = b.bm.faces.new(tri_v); f2 = b.bm.faces.new(tri_b)
    b._map([f1, f2], "leather", lambda co: (co.x * 0.8, co.z * 0.8))
    # front flap rolled up on the right side of the entrance, floor fur and a rolled bed at the back
    b.cyl("leather", (W * 0.55, -Lh - 0.02, 0.75), (W * 0.1, -Lh - 0.02, H - 0.1), 0.06, 0.05, segs=8, rings=3)
    _panel(b, [(-0.72, -0.85, 0.035), (0.72, -0.85, 0.035), (0.72, 0.95, 0.035), (-0.72, 0.95, 0.035)], 4, 5, "leather", sag=-0.01, thick=0.02, seed=7, uvs=0.7)
    b.cyl("leather", (-0.5, 0.8, 0.1), (0.5, 0.8, 0.1), 0.08, 0.08, segs=8, rings=2)
    return b.build("PROP_Tent", col)

def rain_collector(col=None):
    random.seed(61); b = T.Builder(61)
    apex = Vector((0, 0, 1.4))
    for k in range(3):
        a = 2 * math.pi * k / 3 + 0.3
        foot = Vector((math.cos(a) * 0.62, math.sin(a) * 0.62, -0.04))
        b.cyl("wood", foot, apex + (apex - foot).normalized() * 0.15, 0.03, 0.024, segs=6, rings=3, jitter=0.08)
    b.wrap("cord", apex, (0, 0, 1), 0.05, 0.07, turns=4, thick=0.006)
    # hide funnel: open cone from a ring tied to the legs down to a spout over the basin
    top_r, bot_r, z0, z1 = 0.46, 0.05, 1.02, 0.62
    nseg, nr = 12, 4
    rings = []
    for i in range(nr + 1):
        t = i / nr; r = top_r + (bot_r - top_r) * (t ** 0.8); z = z0 + (z1 - z0) * t
        rings.append([b.bm.verts.new((math.cos(2 * math.pi * k / nseg) * r * (1 + 0.04 * math.sin(k * 2.3)), math.sin(2 * math.pi * k / nseg) * r, z - (0.03 * math.sin(k * 1.7) if i == 0 else 0))) for k in range(nseg)])
    fs = []
    for i in range(nr):
        for k in range(nseg):
            q = [rings[i][k], rings[i][(k + 1) % nseg], rings[i + 1][(k + 1) % nseg], rings[i + 1][k]]
            fs.append(b.bm.faces.new(q)); fs.append(b.bm.faces.new(list(reversed([b.bm.verts.new(v.co + Vector((0, 0, -0.008))) for v in q]))))
    b._map(fs, "leather", lambda co: (math.atan2(co.y, co.x) * 0.5, co.z * 1.5))
    for k in range(3):                     # ties from the funnel rim to the legs
        a = 2 * math.pi * k / 3 + 0.3
        rim = Vector((math.cos(a) * top_r, math.sin(a) * top_r, z0))
        leg = Vector((math.cos(a) * 0.62, math.sin(a) * 0.62, -0.04)).lerp(apex, 0.74)
        b.cyl("cord", rim, leg, 0.005, 0.005, segs=4, rings=1, cap=False)
    b.cyl("wood", (0, 0, z1 + 0.02), (0, 0, z1 - 0.1), 0.035, 0.02, segs=6, rings=1)   # spout
    # wooden basin (hollow)
    R, Hb = 0.3, 0.24
    b.cyl("wood", (0, 0, 0.0), (0, 0, Hb), R, R * 1.05, segs=14, rings=2, jitter=0.02, cap=False)
    b.cyl("wood", (0, 0, Hb), (0, 0, 0.03), R * 0.9, R * 0.86, segs=14, rings=1, cap=False)
    top = bmesh.ops.create_circle(b.bm, cap_ends=True, segments=14, radius=R * 0.9)
    for v in top["verts"]: v.co.z = 0.03
    b._map([f for f in b.bm.faces if all(v in top["verts"] for v in f.verts)], "wood", lambda co: (co.x * 2, co.y * 2))
    rim_o = [b.bm.verts.new((math.cos(2 * math.pi * k / 14) * R * 1.05, math.sin(2 * math.pi * k / 14) * R * 1.05, Hb)) for k in range(14)]
    rim_i = [b.bm.verts.new((math.cos(2 * math.pi * k / 14) * R * 0.9, math.sin(2 * math.pi * k / 14) * R * 0.9, Hb)) for k in range(14)]
    rf = [b.bm.faces.new((rim_o[k], rim_o[(k + 1) % 14], rim_i[(k + 1) % 14], rim_i[k])) for k in range(14)]
    b._map(rf, "wood", lambda co: (co.x * 3, co.y * 3))
    body = b.build("PROP_RainCollector_Body", col)
    # water surface (own object, moved up by RainCollector with the amount)
    me = bpy.data.meshes.new("Water"); bm = bmesh.new()
    bmesh.ops.create_circle(bm, cap_ends=True, segments=16, radius=R * 0.88)
    for v in bm.verts: v.co.z = 0.05
    bm.to_mesh(me); bm.free()
    w = bpy.data.objects.new("Water", me); (col or bpy.context.scene.collection).objects.link(w)
    m = bpy.data.materials.get("M_FreshWater") or bpy.data.materials.new("M_FreshWater")
    m.use_nodes = True; bs = m.node_tree.nodes.get("Principled BSDF")
    if bs: bs.inputs["Base Color"].default_value = (0.12, 0.25, 0.3, 1); bs.inputs["Roughness"].default_value = 0.05
    me.materials.append(m)
    return body, w

def leaf_cup(col=None):
    """a broad leaf folded into a cone, pinned with a thorn"""
    me = bpy.data.meshes.new("ITEM_LeafCup"); bm = bmesh.new()
    nseg = 14; R, H = 0.055, 0.1
    tip = bm.verts.new((0, 0, 0.0))
    ring = [bm.verts.new((math.cos(2 * math.pi * k / nseg) * R * (1 + 0.12 * math.cos(k * 1.3)), math.sin(2 * math.pi * k / nseg) * R, H + 0.012 * math.sin(k * 2.1))) for k in range(nseg)]
    lip = [bm.verts.new((v.co.x * 1.18, v.co.y * 1.18, v.co.z + 0.01)) for v in ring]
    for k in range(nseg):
        bm.faces.new((tip, ring[k], ring[(k + 1) % nseg]))
        bm.faces.new((ring[k], lip[k], lip[(k + 1) % nseg], ring[(k + 1) % nseg]))
    inner = [bm.verts.new((v.co.x * 0.92, v.co.y * 0.92, v.co.z - 0.004)) for v in ring]
    tip2 = bm.verts.new((0, 0, 0.008))
    for k in range(nseg): bm.faces.new((tip2, inner[(k + 1) % nseg], inner[k]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.to_mesh(me); bm.free()
    for p in me.polygons: p.use_smooth = True
    o = bpy.data.objects.new("ITEM_LeafCup", me); (col or bpy.context.scene.collection).objects.link(o)
    m = C.simple("LeafCup", (0.16, 0.36, 0.1), 0.55)
    me.materials.append(m)
    return o

def burnt_meat(col=None):
    b = T.Builder(25)
    b.blob("meat_cooked", (0, 0, 0.037), (0.09, 0.061, 0.03), seed=24, rough=0.24, facet=True)
    n_meat = len(b.bm.faces)
    b.cyl("bone", (-0.13, 0, 0.04), (0.14, 0, 0.04), 0.012, 0.012, segs=6, rings=1)
    for i, f in enumerate(b.bm.faces):
        if i < n_meat: f.material_index = 1
    o = b.build("ITEM_BurntMeat", col)
    o.data.materials.append(C.simple("BurntFood", (0.07, 0.045, 0.03), 0.9))
    return o

def export(name, objs):
    import pf_export
    return pf_export.export_asset(name, objs, OUT)

def build_all(render_icons=True):
    import pf_icons
    col = bpy.data.collections.get("_CampProps") or bpy.data.collections.new("_CampProps")
    if col.name not in [c.name for c in bpy.context.scene.collection.children]: bpy.context.scene.collection.children.link(col)
    for n in ("PROP_Tent", "PROP_RainCollector_Body", "Water", "ITEM_LeafCup", "ITEM_BurntMeat"):
        o = bpy.data.objects.get(n)
        if o: bpy.data.objects.remove(o, do_unlink=True)
    out = {}
    t = tent(col); out["tent"] = export("PROP_Tent", [t])
    body, water = rain_collector(col); out["collector"] = export("PROP_RainCollector", [body, water])
    cup = leaf_cup(col); out["cup"] = export("ITEM_LeafCup", [cup])
    bm_ = burnt_meat(col); out["burnt"] = export("ITEM_BurntMeat", [bm_])
    out["tris"] = {o.name: sum(len(p.vertices) - 2 for p in o.data.polygons) for o in (t, body, cup, bm_)}
    if render_icons:
        os.makedirs(ICONS, exist_ok=True)
        pf_icons.render_object(t, os.path.join(ICONS, "ICON_Tent.png"), size=256, yaw=35, pitch=22, margin=1.1)
        water.hide_render = False
        pf_icons.render_object(body, os.path.join(ICONS, "ICON_RainCollector.png"), size=256, yaw=30, pitch=18, margin=1.12)
        pf_icons.render_object(cup, os.path.join(ICONS, "ICON_LeafCup.png"), size=256, yaw=30, pitch=28, margin=1.2)
        pf_icons.render_object(bm_, os.path.join(ICONS, "ICON_BurntMeat.png"), size=256, yaw=25, pitch=40, margin=1.2)
    return out
