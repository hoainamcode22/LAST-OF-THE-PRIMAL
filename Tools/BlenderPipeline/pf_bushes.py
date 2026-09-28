# PRIMAL FRONTIER - interactive bush variants ENV_Bush_02 (tall leafy shrub with visible branches) and ENV_Bush_03
# (low wide fern shrub). Same foliage atlas / materials as the vegetation kit (pf_veg); LOD0 + LOD1 under one root.
import bpy, math, random, os
from mathutils import Vector
import pf_veg as V, pf_common as C, pf_export as E

OUT = os.path.join(C.UNITY, "Assets", "_Project", "Art", "Models", "Vegetation")

def bush_tall(name, seed=31, lod=0):
    rng = random.Random(seed); B = V.Builder()
    c = Vector((0, 0, 0.75))
    tips = []
    for k in range(5 if lod == 0 else 3):
        a = k / 5 * 6.283 + rng.uniform(-0.3, 0.3)
        d = Vector((math.cos(a) * 0.5, math.sin(a) * 0.5, 1)).normalized()
        pts = V._curve_pts(Vector((rng.uniform(-0.05, 0.05), rng.uniform(-0.05, 0.05), -0.05)), d, rng.uniform(0.8, 1.1), 4 if lod == 0 else 2, wobble=0.25, seed=seed + k)
        B.tube(pts, [0.028 * (1 - i / len(pts)) + 0.008 for i in range(len(pts))], segs=6 if lod == 0 else 4, v_tile=1.0)
        tips += pts[2:]
    n = 30 if lod == 0 else 13
    for j in range(n):
        u = rng.uniform(-0.2, 1.0); th = rng.uniform(0, 6.283)
        d = Vector((math.sqrt(max(0.0, 1 - u * u)) * math.cos(th), math.sqrt(max(0.0, 1 - u * u)) * math.sin(th), u * 0.8 + 0.35)).normalized()
        base = tips[rng.randrange(len(tips))] * 0.6 + Vector((0, 0, 0.15))
        side = d.cross(Vector((0.3, -0.2, 1))).normalized()
        B.card(base, d, side, rng.uniform(0.75, 1.05), 0.95, V.Q["broadleaf"], bend=0.3, segs=3 if lod == 0 else 2, center=c)
    return B

def bush_low(name, seed=37, lod=0):
    rng = random.Random(seed); B = V.Builder()
    c = Vector((0, 0, 0.2))
    n = 14 if lod == 0 else 7
    for j in range(n):
        a = j / n * 6.283 + rng.uniform(-0.2, 0.2)
        d = Vector((math.cos(a), math.sin(a), rng.uniform(0.35, 0.9))).normalized()
        B.card(Vector((rng.uniform(-0.1, 0.1), rng.uniform(-0.1, 0.1), -0.05)), d, Vector((-math.sin(a), math.cos(a), 0)),
               rng.uniform(0.9, 1.35), 0.75, V.Q["fern"], bend=0.55, segs=3 if lod == 0 else 2, center=c)
    for j in range(7 if lod == 0 else 3):
        a = rng.uniform(0, 6.283)
        d = Vector((math.cos(a) * 0.6, math.sin(a) * 0.6, 1)).normalized()
        B.card(Vector((0, 0, -0.02)), d, d.cross(Vector((0, 0, 1))).normalized(), rng.uniform(0.6, 0.85), 0.8, V.Q["broadleaf"], bend=0.35, segs=2, center=c)
    return B

def build_export():
    m = V.mats(); out = {}
    for name, fn, seed in (("ENV_Bush_02", bush_tall, 31), ("ENV_Bush_03", bush_low, 37)):
        objs = []
        for lod in (0, 1):
            o = fn(name, seed, lod).finish(f"{name}_LOD{lod}", "02_Forest", (0, 0, 0), m)
            objs.append(o); out[o.name] = C.tri_count(o)
        out[name] = E.export_asset(name, objs, OUT)
    return out
