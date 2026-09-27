# PRIMAL FRONTIER - vegetation kit: prehistoric trees (araucaria conifer, tree fern, broadleaf, cycad palm),
# ground fern, bush, grass tuft, fallen log. Trunks = bark tubes, foliage = alpha cards from T_FoliageAtlas.
import bpy, bmesh, math, random
from mathutils import Vector, Matrix, noise
import pf_common as C

LINEUP_Y = -160.0
LINEUP_X = 420.0
Q = {"conifer": (0.0, 0.5, 0.5, 1.0), "broadleaf": (0.5, 1.0, 0.5, 1.0), "fern": (0.0, 0.5, 0.0, 0.5), "grass": (0.5, 1.0, 0.0, 0.5)}

class Builder:
    def __init__(self):
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.verify()
        self.vn = {}        # vert -> custom normal
        self.fol_center = []

    def _face(self, verts, uvs, mat):
        f = self.bm.faces.new(verts)
        f.material_index = mat
        for loop, uv in zip(f.loops, uvs):
            loop[self.uv].uv = uv
        f.smooth = True
        return f

    def tube(self, pts, radii, segs=8, u_rep=1.0, v_tile=2.0, jitter=0.0, seed=0, cap=False):
        """pts: list of Vector, radii: list of float. Parallel-transport frames. Bark material (0)."""
        rings = []
        t0 = (pts[1] - pts[0]).normalized()
        ref = Vector((0, 0, 1)) if abs(t0.z) < 0.9 else Vector((1, 0, 0))
        n = t0.cross(ref).normalized(); b = t0.cross(n)
        length = 0.0
        for i, p in enumerate(pts):
            if i > 0:
                length += (p - pts[i - 1]).length
            t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
            # transport frame
            n = (n - t * n.dot(t)).normalized(); b = t.cross(n)
            ring = []
            for j in range(segs + 1):
                a = j / segs * 2 * math.pi
                r = radii[i] * (1 + jitter * noise.noise(Vector((math.cos(a) * 2, math.sin(a) * 2, i * 0.7 + seed))))
                v = self.bm.verts.new(p + (n * math.cos(a) + b * math.sin(a)) * r)
                ring.append((v, (j / segs * u_rep, length / v_tile)))
            rings.append(ring)
        for i in range(len(rings) - 1):
            for j in range(segs):
                a, b2, c, d = rings[i][j], rings[i][j + 1], rings[i + 1][j + 1], rings[i + 1][j]
                self._face([a[0], b2[0], c[0], d[0]], [a[1], b2[1], c[1], d[1]], 0)
        if cap:
            last = rings[-1][:-1]
            self._face([v for v, _ in last], [(0.5 + 0.4 * math.cos(k / segs * 6.283), 0.5 + 0.4 * math.sin(k / segs * 6.283)) for k in range(segs)], 0)
        return rings

    def card(self, base, direction, side, L, W, quad, bend=0.25, segs=3, center=None, droop_axis=None):
        """Bent foliage card. base = attach point, direction = growth dir, side = width axis. Foliage material (1)."""
        d = direction.normalized(); s = side.normalized()
        up = d.cross(s).normalized()
        u0, u1, v0, v1 = quad
        rows = []
        p = base.copy(); dd = d.copy()
        for k in range(segs + 1):
            t = k / segs
            if k > 0:
                seg = L / segs
                # bend: rotate direction towards gravity (or -up) progressively
                g = droop_axis if droop_axis is not None else Vector((0, 0, -1))
                dd = (dd + g * bend * (1.0 / segs) * 2.2).normalized()
                p = p + dd * seg
            wv = W * (0.55 + 0.45 * math.sin(math.pi * min(1, t * 0.9 + 0.1)))
            a = self.bm.verts.new(p - s * wv * 0.5); b = self.bm.verts.new(p + s * wv * 0.5)
            ua = u0 + (u1 - u0) * (0.5 - wv / W * 0.5); ub = u0 + (u1 - u0) * (0.5 + wv / W * 0.5)
            vv = v0 + (v1 - v0) * (0.02 + 0.96 * t)
            rows.append(((a, (ua, vv)), (b, (ub, vv))))
            if center is not None:
                for v in (a, b):
                    rad = (v.co - center)
                    self.vn[v] = (rad.normalized() * 0.75 + up * 0.25).normalized() if rad.length > 1e-4 else up
        for k in range(segs):
            (a, ua), (b, ub) = rows[k]; (c, uc), (dv, ud) = rows[k + 1]
            self._face([a, b, dv, c], [ua, ub, ud, uc], 1)

    def finish(self, name, cname, loc, mats):
        bm = self.bm
        bmesh.ops.remove_doubles(bm, verts=[v for v in bm.verts if v not in self.vn], dist=0.0001)
        vn = {v.index: n for v, n in self.vn.items() if v.is_valid}
        bm.verts.index_update()
        vn = {v.index: self.vn[v] for v in bm.verts if v in self.vn}
        o = C.mesh_obj(name, bm, cname, mats)
        me = o.data
        if vn:
            normals = []
            for v in me.vertices:
                normals.append(vn.get(v.index, v.normal.copy()))
            try:
                me.normals_split_custom_set_from_vertices(normals)
            except Exception as e:
                print("custom normals failed", e)
        o.location = loc
        return o

def mats():
    return [C.pbr("Bark", "Bark"), C.pbr("Foliage", "FoliageAtlas", alpha=True, rough_override=0.75)]

def _curve_pts(base, direction, length, n, lean=Vector((0, 0, 0)), wobble=0.0, seed=0, upturn=0.0):
    pts = []; p = base.copy(); d = direction.normalized()
    for i in range(n + 1):
        t = i / n
        if i:
            w = Vector((noise.noise(Vector((seed, t * 3, 0))), noise.noise(Vector((0, seed, t * 3))), 0)) * wobble
            d = (d + lean * (1.0 / n) + w * (1.0 / n) + Vector((0, 0, upturn / n))).normalized()
            p = p + d * (length / n)
        pts.append(p.copy())
    return pts

# ------------------------------------------------------------------ trees
def tree_araucaria(name, seed=1, lod=0):
    rng = random.Random(seed); B = Builder()
    H = rng.uniform(18, 23)
    trunk = _curve_pts(Vector((0, 0, -0.3)), Vector((0, 0, 1)), H, 16 if lod == 0 else 8, lean=Vector((rng.uniform(-0.08, 0.08), rng.uniform(-0.08, 0.08), 0)), wobble=0.12, seed=seed)
    radii = [0.48 * (1 - (i / (len(trunk) - 1)) * 0.85) * (1 + 0.7 * math.exp(-trunk[i].z / 0.9)) for i in range(len(trunk))]
    B.tube(trunk, radii, segs=10 if lod == 0 else 6, u_rep=2, v_tile=2.5, jitter=0.08, seed=seed)
    top = trunk[-1]
    whorl_h = 0.55 * H
    k = 0
    while whorl_h < H - 0.6:
        t = max(0.0, (whorl_h / H - 0.55) / 0.45)
        idx = min(len(trunk) - 1, int(whorl_h / H * (len(trunk) - 1)))
        base = trunk[idx]
        nb = 5 if lod == 0 else 3
        for j in range(nb):
            a = j / nb * 2 * math.pi + k * 0.63 + rng.uniform(-0.2, 0.2)
            dirv = Vector((math.cos(a), math.sin(a), rng.uniform(-0.25, -0.05)))
            L = (4.2 * (1 - t) ** 0.8 + 0.9) * rng.uniform(0.85, 1.1)
            pts = _curve_pts(base, dirv, L, 4 if lod == 0 else 2, upturn=0.8, seed=seed + j + k * 10)
            B.tube(pts, [0.12 * (1 - t * 0.6), 0.08, 0.05, 0.03, 0.02][:len(pts)] if lod == 0 else [0.1, 0.05, 0.02], segs=5 if lod == 0 else 4, v_tile=1.5)
            ncards = 5 if lod == 0 else 2
            for c in range(ncards):
                s_ = 0.35 + 0.65 * (c + 1) / ncards
                pi = min(len(pts) - 1, int(s_ * (len(pts) - 1)))
                pd = (pts[min(pi + 1, len(pts) - 1)] - pts[max(pi - 1, 0)]).normalized()
                side = pd.cross(Vector((0, 0, 1))).normalized()
                tilt = Matrix.Rotation(rng.uniform(-0.5, 0.5), 3, pd)
                B.card(pts[pi] - pd * 0.5, (pd + Vector((0, 0, 0.25))).normalized(), tilt @ side, 3.3, 3.0, Q["conifer"], bend=0.45, center=top - Vector((0, 0, 3)))
                # a second card rotated 90 deg around the branch for volume
                if lod == 0:
                    B.card(pts[pi] - pd * 0.4, (pd + Vector((0, 0, 0.1))).normalized(), (tilt @ side).cross(pd), 2.8, 2.4, Q["conifer"], bend=0.5, center=top - Vector((0, 0, 3)))
        whorl_h += rng.uniform(0.75, 1.05); k += 1
    for j in range(4 if lod == 0 else 2):
        a = j * 1.57 + 0.3
        B.card(top - Vector((0, 0, 1.2)), Vector((math.cos(a) * 0.5, math.sin(a) * 0.5, 1)), Vector((-math.sin(a), math.cos(a), 0)), 3.0, 2.6, Q["conifer"], bend=0.15, center=top - Vector((0, 0, 3)))
    return B

def tree_fern(name, seed=2, lod=0):
    rng = random.Random(seed); B = Builder()
    H = rng.uniform(4.2, 6.0)
    trunk = _curve_pts(Vector((0, 0, -0.2)), Vector((rng.uniform(-0.1, 0.1), rng.uniform(-0.1, 0.1), 1)), H, 8 if lod == 0 else 4, wobble=0.25, seed=seed)
    radii = [0.24 * (1 + 0.5 * math.exp(-p.z / 0.5)) * (1.1 if i == len(trunk) - 1 else 1.0) for i, p in enumerate(trunk)]
    B.tube(trunk, radii, segs=8 if lod == 0 else 5, u_rep=1, v_tile=1.2, jitter=0.15, seed=seed)
    top = trunk[-1]
    n = 18 if lod == 0 else 9
    for j in range(n):
        a = j / n * 2 * math.pi + rng.uniform(-0.15, 0.15)
        pitch = rng.uniform(0.25, 0.9)
        d = Vector((math.cos(a), math.sin(a), pitch)).normalized()
        side = Vector((-math.sin(a), math.cos(a), 0))
        B.card(top, d, side, rng.uniform(3.0, 3.8), 2.1, Q["fern"], bend=0.55, segs=4 if lod == 0 else 2, center=top)
    for j in range(3 if lod == 0 else 0):
        a = rng.uniform(0, 6.28)
        B.card(top, Vector((math.cos(a) * 0.25, math.sin(a) * 0.25, 1)), Vector((-math.sin(a), math.cos(a), 0)), 1.4, 0.7, Q["fern"], bend=0.1, center=top)
    return B

def tree_broadleaf(name, seed=3, lod=0):
    rng = random.Random(seed); B = Builder()
    Ht = rng.uniform(3.2, 4.2)
    trunk = _curve_pts(Vector((0, 0, -0.3)), Vector((0, 0, 1)), Ht, 5 if lod == 0 else 3, wobble=0.3, seed=seed)
    B.tube(trunk, [0.42 * (1 + 0.6 * math.exp(-p.z / 0.6)) * (1 - i * 0.06) for i, p in enumerate(trunk)], segs=10 if lod == 0 else 6, u_rep=2, v_tile=2.0, jitter=0.1, seed=seed)
    top = trunk[-1]
    crown_c = top + Vector((0, 0, 4.0))
    limbs = 4 if lod == 0 else 3
    for j in range(limbs):
        a = j / limbs * 6.283 + rng.uniform(-0.3, 0.3)
        d = Vector((math.cos(a) * 1.1, math.sin(a) * 1.1, 1))
        L = rng.uniform(5.0, 6.5)
        pts = _curve_pts(top, d, L, 4 if lod == 0 else 2, wobble=0.4, seed=seed + j * 7, upturn=0.2)
        B.tube(pts, [0.24, 0.18, 0.13, 0.09, 0.06][:len(pts)] if lod == 0 else [0.22, 0.12, 0.06], segs=6 if lod == 0 else 4, v_tile=1.8)
        ends = [pts[-1]]
        if lod == 0:
            for s_ in range(3):
                b0 = pts[1 + s_ % 3]; bd = (d + Vector((rng.uniform(-0.8, 0.8), rng.uniform(-0.8, 0.8), 0.3))).normalized()
                bp = _curve_pts(b0, bd, rng.uniform(2.0, 3.2), 2, seed=seed + j * 7 + s_)
                B.tube(bp, [0.08, 0.05, 0.03], segs=4, v_tile=1.2); ends.append(bp[-1])
        for e in ends:
            nc = 12 if lod == 0 else 5
            for c in range(nc):
                u = rng.uniform(-1, 1); th = rng.uniform(0, 6.283)
                dirv = Vector((math.sqrt(1 - u * u) * math.cos(th), math.sqrt(1 - u * u) * math.sin(th), abs(u) * 0.8 + 0.2)).normalized()
                side = dirv.cross(Vector((0.3, 0.2, 1))).normalized()
                B.card(e - dirv * 1.2, dirv, side, 3.4, 3.2, Q["broadleaf"], bend=0.35, center=crown_c)
    return B

def tree_cycad(name, seed=4, lod=0):
    rng = random.Random(seed); B = Builder()
    H = rng.uniform(2.4, 3.8)
    trunk = _curve_pts(Vector((0, 0, -0.2)), Vector((rng.uniform(-0.3, 0.3), rng.uniform(-0.3, 0.3), 1)), H, 6 if lod == 0 else 3, wobble=0.15, seed=seed)
    B.tube(trunk, [0.36 * (1 + 0.35 * math.exp(-p.z / 0.5)) for p in trunk], segs=10 if lod == 0 else 6, u_rep=2, v_tile=0.8, jitter=0.2, seed=seed)
    top = trunk[-1]
    n = 22 if lod == 0 else 11
    for j in range(n):
        a = j / n * 6.283 + rng.uniform(-0.1, 0.1)
        pitch = rng.uniform(0.3, 1.4)
        d = Vector((math.cos(a), math.sin(a), pitch)).normalized()
        B.card(top, d, Vector((-math.sin(a), math.cos(a), 0)), rng.uniform(2.3, 3.0), 1.6, Q["fern"], bend=0.3, segs=3 if lod == 0 else 2, center=top)
    return B

# ------------------------------------------------------------------ ground plants
def fern(name, seed=5, lod=0):
    rng = random.Random(seed); B = Builder()
    n = 11 if lod == 0 else 6
    c = Vector((0, 0, 0.3))
    for j in range(n):
        a = j / n * 6.283 + rng.uniform(-0.2, 0.2)
        d = Vector((math.cos(a), math.sin(a), rng.uniform(0.8, 1.8))).normalized()
        B.card(Vector((0, 0, -0.05)), d, Vector((-math.sin(a), math.cos(a), 0)), rng.uniform(0.9, 1.3), 0.8, Q["fern"], bend=0.5, segs=3 if lod == 0 else 2, center=c)
    return B

def bush(name, seed=6, lod=0):
    rng = random.Random(seed); B = Builder()
    c = Vector((0, 0, 0.6))
    n = 16 if lod == 0 else 8
    for j in range(n):
        u = rng.uniform(0.0, 1.0); th = rng.uniform(0, 6.283)
        d = Vector((math.sqrt(1 - u * u) * math.cos(th), math.sqrt(1 - u * u) * math.sin(th), u * 0.9 + 0.3)).normalized()
        side = d.cross(Vector((0.2, -0.3, 1))).normalized()
        B.card(Vector((rng.uniform(-0.2, 0.2), rng.uniform(-0.2, 0.2), -0.05)), d, side, rng.uniform(1.1, 1.5), 1.2, Q["broadleaf"], bend=0.35, center=c)
    return B

def grass(name, seed=7, lod=0):
    rng = random.Random(seed); B = Builder()
    n = 3 if lod == 0 else 2
    for j in range(n):
        a = j / n * math.pi + rng.uniform(-0.2, 0.2)
        side = Vector((math.cos(a), math.sin(a), 0))
        B.card(Vector((0, 0, -0.03)), Vector((0, 0, 1)), side, 0.75, 1.0, Q["grass"], bend=0.05, segs=1, center=Vector((0, 0, -0.4)))
    return B

def fallen_log(name, seed=8, lod=0):
    rng = random.Random(seed); B = Builder()
    L = rng.uniform(6.5, 8.5)
    pts = _curve_pts(Vector((-L / 2, 0, 0.35)), Vector((1, 0, 0.02)), L, 8 if lod == 0 else 4, wobble=0.15, seed=seed)
    B.tube(pts, [0.45 * (1 - i * 0.03) for i in range(len(pts))], segs=10 if lod == 0 else 6, u_rep=2, v_tile=2.0, jitter=0.12, seed=seed, cap=True)
    if lod == 0:
        for k in range(3):
            p = pts[2 + k * 2]; d = Vector((rng.uniform(-0.2, 0.2), rng.choice((-1, 1)), rng.uniform(0.2, 0.9))).normalized()
            B.tube(_curve_pts(p, d, rng.uniform(0.6, 1.2), 2, seed=seed + k), [0.1, 0.07, 0.03], segs=5, v_tile=1.0)
    # root-plate end
    return B

KIT = [
    ("ENV_Tree_01", tree_araucaria, 1, "02_Forest"),
    ("ENV_Tree_02", tree_fern, 2, "02_Forest"),
    ("ENV_Tree_03", tree_broadleaf, 3, "02_Forest"),
    ("ENV_Tree_04", tree_cycad, 4, "02_Forest"),
    ("ENV_Fern_01", fern, 5, "02_Forest"),
    ("ENV_Bush_01", bush, 6, "02_Forest"),
    ("ENV_Grass_01", grass, 7, "02_Forest"),
    ("ENV_FallenLog_01", fallen_log, 8, "02_Forest"),
]

def build_kit(names=None):
    m = mats(); out = {}
    x = 0
    for name, fn, seed, cname in KIT:
        if names and name not in names:
            x += 14; continue
        for lod in (0, 1):
            B = fn(name, seed, lod)
            nm = f"{name}_LOD{lod}"
            o = B.finish(nm, cname, (LINEUP_X + x, LINEUP_Y + lod * 25, 0.4), m)
            out[nm] = C.tri_count(o)
        x += 14
    return out
