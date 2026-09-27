# PRIMAL FRONTIER - distant volcano landmark (original design): a broad stratovolcano across the sea from the island,
# concave slopes cut by radial gullies, a breached crater with a lava lake and one glowing lava flow running down the
# side that faces the island. Scenery only (not reachable, the playable world is unchanged).
# Blender model space: x right, y forward(-y faces the camera side in Unity after export), z up. Units: metres.
import bpy, bmesh, math, os, json, random
from mathutils import Vector, noise
import pf_common as C

UA = os.path.join(C.UNITY, "Assets", "_Project")
H, R, RC, DEPTH = 250.0, 330.0, 46.0, 30.0
# lava flow direction in Blender space (towards the island once placed at Unity (-360, 0, 640))
FLOW = math.atan2(0.872, -0.49)

def height(r, th):
    if r < RC:
        k = (r / RC)
        floor = H - DEPTH
        h = floor + (H - floor) * (k ** 3.0) if k > 0.5 else floor + (H - floor) * (0.5 ** 3.0) * (k / 0.5) ** 2
        dc = math.atan2(math.sin(th - (FLOW - 1.25)), math.cos(th - (FLOW - 1.25)))
        rim_scar = 42.0 * math.exp(-(dc / 0.42) ** 2) * math.exp(-((0.0 - 0.12) / 0.22) ** 2)
        return h - rim_scar * k ** 4
    t = (r - RC) / (R - RC)
    base = H * (1.0 - t) ** 1.75
    base *= 1.0 + (0.06 * math.sin(2 * th + 1.3) + 0.04 * math.sin(3 * th - 0.4)) * min(1.0, t * 4.0)
    # radial gullies strongest mid-slope
    g = 0.0
    for n, a, ph in ((17, 1.0, 0.3), (29, 0.6, 1.9), (47, 0.35, 4.1)):
        g += a * (1.0 - abs(math.sin(n * th + ph + 0.004 * r)) ** 0.6)
    base -= g * H * 0.035 * math.sin(math.pi * min(1.0, t * 1.15))
    # rough rock
    p = Vector((r * math.cos(th), r * math.sin(th), 0.0)) * 0.012
    base += noise.fractal(p, 0.7, 2.0, 5) * 7.0 * (0.3 + t)
    # rim breach on the flow side (the lava spills out there)
    d = math.atan2(math.sin(th - FLOW), math.cos(th - FLOW))
    if t < 0.08: base -= 14.0 * math.exp(-(d / 0.18) ** 2) * (1.0 - t / 0.08)
    # flow channel down the side
    if t < 0.55: base -= 5.0 * math.exp(-(d / 0.035) ** 2) * math.sin(math.pi * min(1.0, t / 0.55))
    # sector collapse: a horseshoe scar that lowers one side of the summit (breaks the perfect cone silhouette)
    dc = math.atan2(math.sin(th - (FLOW - 1.25)), math.cos(th - (FLOW - 1.25)))
    scar = math.exp(-(dc / 0.42) ** 2) * math.exp(-((t - 0.12) / 0.22) ** 2)
    base -= 42.0 * scar
    # a long ridge shoulder on the other side
    dr = math.atan2(math.sin(th - (FLOW + 1.6)), math.cos(th - (FLOW + 1.6)))
    base += 38.0 * math.exp(-(dr / 0.5) ** 2) * math.exp(-((t - 0.42) / 0.2) ** 2)
    # a parasitic cone on the shoulder and a broken, uneven rim: not a perfect cone
    px, py = 0.55 * R * math.cos(FLOW - 1.62), 0.55 * R * math.sin(FLOW - 1.62)
    dx, dy = r * math.cos(th) - px, r * math.sin(th) - py
    dd = math.sqrt(dx * dx + dy * dy)
    base += 62.0 * math.exp(-(dd / 50.0) ** 2) - 14.0 * math.exp(-(dd / 11.0) ** 2)
    base += 18.0 * math.exp(-((r - 0.33 * R) / 50.0) ** 2) * (0.5 + 0.5 * math.sin(th * 2 + 0.7))   # lumpy shoulder
    if t < 0.1: base += noise.noise(Vector((math.cos(th) * 3.0, math.sin(th) * 3.0, 0.5))) * 9.0 * (1.0 - t / 0.1)
    # deeper gullies low down (old lava channels), lava-rock lumps
    base -= 0.9 * H * 0.03 * (1.0 - abs(math.sin(11 * th + 0.9 + 0.002 * r))) * t * (1.0 - t) * 4.0
    return base - 10.0 * t ** 6          # sinks into the sea at the foot

def build(name, na, nr, rock, ash):
    bm = bmesh.new(); uv = bm.loops.layers.uv.new("UVMap")
    rings = []
    for i in range(nr + 1):
        r = RC * 0.02 + (R - RC * 0.02) * (i / nr) ** 1.35
        ring = []
        for j in range(na):
            th = 2 * math.pi * j / na
            ring.append(bm.verts.new((r * math.cos(th), r * math.sin(th), height(r, th))))
        rings.append(ring)
    for i in range(nr):
        for j in range(na):
            a, b = rings[i][j], rings[i][(j + 1) % na]
            c, d = rings[i + 1][(j + 1) % na], rings[i + 1][j]
            f = bm.faces.new((a, d, c, b))
            zc = sum(v.co.z for v in f.verts) / 4.0
            cx = sum(v.co.x for v in f.verts) / 4.0; cy = sum(v.co.y for v in f.verts) / 4.0
            edge = H * (0.62 + 0.08 * noise.noise(Vector((cx * 0.01, cy * 0.01, 3.0))))
            f.material_index = 1 if zc > edge else 0
            for l in f.loops: l[uv].uv = (l.vert.co.x / 22.0, l.vert.co.y / 22.0)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    o = C.mesh_obj(name, bm, "21_Volcano", (rock, ash))
    for p in o.data.polygons: p.use_smooth = True
    return o

def lava(name, mat):
    """lava lake in the crater + a glowing ribbon down the channel"""
    bm = bmesh.new(); uv = bm.loops.layers.uv.new("UVMap")
    # lake: disc just above the crater floor
    n = 32; zl = H - DEPTH + 1.2
    centre = bm.verts.new((0, 0, zl)); ring = [bm.verts.new((RC * 0.5 * math.cos(2 * math.pi * k / n), RC * 0.5 * math.sin(2 * math.pi * k / n), zl)) for k in range(n)]
    for k in range(n): bm.faces.new((centre, ring[k], ring[(k + 1) % n]))
    # ribbon along the flow channel, 1 m above the surface, tapering
    prev = None
    steps = 26
    for s in range(steps + 1):
        t = s / steps; r = RC * 0.7 + (R - RC) * 0.5 * t
        w = 16.0 * (1.0 - t) ** 0.8 + 3.0
        side = Vector((-math.sin(FLOW), math.cos(FLOW), 0))
        c = Vector((r * math.cos(FLOW), r * math.sin(FLOW), 0)); c.z = height(r, FLOW) + 1.0
        a = bm.verts.new(c - side * w); b = bm.verts.new(c + side * w)
        a.co.z = height((c - side * w).length, math.atan2((c - side * w).y, (c - side * w).x)) + 1.0
        b.co.z = height((c + side * w).length, math.atan2((c + side * w).y, (c + side * w).x)) + 1.0
        if prev: bm.faces.new((prev[0], a, b, prev[1]))
        prev = (a, b)
    for f in bm.faces:
        for l in f.loops: l[uv].uv = (l.vert.co.x / 30.0, l.vert.co.y / 30.0)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    for f in bm.faces:
        if f.normal.z < 0: f.normal_flip()
    return C.mesh_obj(name, bm, "21_Volcano", (mat,))

def build_all():
    C.coll("21_Volcano")
    random.seed(7)
    rock = C.simple("VolcanoRock", (0.10, 0.09, 0.085), rough=0.9)
    ash = C.simple("VolcanoAsh", (0.2, 0.185, 0.17), rough=0.95)
    lv = C.simple("VolcanoLava", (1.0, 0.35, 0.05), rough=0.5, emission=(1.0, 0.32, 0.04))
    l0 = build("ENV_Volcano_LOD0", 128, 48, rock, ash)
    l1 = build("ENV_Volcano_LOD1", 64, 22, rock, ash)
    lav = lava("ENV_Volcano_Lava", lv)
    root = bpy.data.objects.get("ENV_Volcano") or bpy.data.objects.new("ENV_Volcano", None)
    if root.name not in bpy.data.collections["21_Volcano"].objects: bpy.data.collections["21_Volcano"].objects.link(root)
    for o in (l0, l1, lav): o.parent = root
    bpy.ops.object.select_all(action='DESELECT')
    for o in (root, l0, l1, lav): o.select_set(True)
    bpy.context.view_layer.objects.active = l0
    import pf_export
    path = os.path.join(UA, "Art", "Models", "Environment", "ENV_Volcano.fbx")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=path, **pf_export.FBX_KW)
    meta = dict(height=H, radius=R, crater_radius=RC, crater_floor=[0, 0, H - DEPTH + 1.2], rim=[0, 0, H],
                flow_top=[RC * 0.9 * math.cos(FLOW), RC * 0.9 * math.sin(FLOW), height(RC * 0.9, FLOW)],
                flow_mid=[(RC + (R - RC) * 0.25) * math.cos(FLOW), (RC + (R - RC) * 0.25) * math.sin(FLOW), height(RC + (R - RC) * 0.25, FLOW)])
    os.makedirs(os.path.join(UA, "Data", "World"), exist_ok=True)
    json.dump(meta, open(os.path.join(UA, "Data", "World", "volcano.json"), "w"), indent=1)
    return dict(path=path, tris=[C.tri_count(l0), C.tri_count(l1), C.tri_count(lav)], meta=meta)
