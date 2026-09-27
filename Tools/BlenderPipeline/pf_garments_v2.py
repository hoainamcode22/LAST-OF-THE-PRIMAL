"""Prehistoric garments for PLAYER_Survivor v2: hide kilt, rope belt, left-shoulder fur pelt + chest strap,
tooth necklace, wrist wraps. All built around the MakeHuman-based body (PLR2_Body_full)."""
import bpy, bmesh, math, random
from mathutils import Vector, Matrix, noise
from mathutils.geometry import convex_hull_2d
from mathutils.bvhtree import BVHTree
from pf_player_v2 import _obj, body_bvh

TAU = 2 * math.pi

def slice_points(body, z, xlim=0.3):
    me = body.data; pts = []
    for e in me.edges:
        a = me.vertices[e.vertices[0]].co; b = me.vertices[e.vertices[1]].co
        if (a.z - z) * (b.z - z) < 0:
            t = (z - a.z) / (b.z - a.z); p = a.lerp(b, t)
            if abs(p.x) < xlim: pts.append((p.x, p.y))
    return pts

def hull_radius(pts, thetas, c):
    """radius of the convex hull of pts seen from centre c along each angle (theta 0 = front -Y, increasing to +X)"""
    if len(pts) < 3: return [0.0] * len(thetas)
    idx = convex_hull_2d(pts); poly = [Vector(pts[i]) for i in idx]
    out = []
    for th in thetas:
        d = Vector((math.sin(th), -math.cos(th)))
        best = 0.0
        for i in range(len(poly)):
            a = poly[i] - c; b = poly[(i + 1) % len(poly)] - c
            # ray c + d*t intersects segment a-b
            e = b - a; den = d.x * (-e.y) - d.y * (-e.x)
            if abs(den) < 1e-12: continue
            t = (a.x * (-e.y) - a.y * (-e.x)) / den
            s = (d.x * a.y - d.y * a.x) / den
            if t > 0 and -1e-6 <= s <= 1 + 1e-6: best = max(best, t)
        out.append(best)
    return out

def _smooth_cyclic(vals, k=1, it=2):
    for _ in range(it):
        n = len(vals); vals = [(vals[(i - 1) % n] + 2 * vals[i] + vals[(i + 1) % n]) / 4 for i in range(n)]
    return vals

def kilt(body, mat, top=1.03, seed=3, ncol=64, nrow=16, front_top=1.005):
    rng = random.Random(seed)
    c = Vector((0.0, 0.005))
    thetas = [TAU * i / ncol for i in range(ncol)]
    def hem(th, i):
        f = 0.5 + 0.5 * math.cos(2 * th)                 # 1 at front/back, 0 at the sides
        base = 0.83 - 0.17 * f ** 1.5
        tear = 0.022 * noise.noise(Vector((math.cos(th) * 4, math.sin(th) * 4, seed))) + (0.012 if i % 3 == 0 else -0.004) * rng.random()
        return base + tear
    hems = _smooth_cyclic([hem(t, i) for i, t in enumerate(thetas)], it=1)
    zmin = min(hems)
    zs_s = [top - (top - zmin) * k / 24 for k in range(25)]
    rings = []; prev = None
    for z in zs_s:
        r = hull_radius(slice_points(body, max(z, 0.62)), thetas, c)
        r = [ri + 0.011 + 0.006 * max(0.0, (top - 0.03 - z) / 0.3) for ri in r]
        if prev is not None: r = [max(ri, 0.985 * pi) for ri, pi in zip(r, prev)]
        r = _smooth_cyclic(r, it=2 if z > 0.9 else 4)
        rings.append(r); prev = r
    def radius(ti, z):
        k = (top - z) / (top - zmin) * 24; k0 = int(max(0, min(23, math.floor(k)))); t = k - k0
        return rings[k0][ti] * (1 - t) + rings[k0 + 1][ti] * t
    bm = bmesh.new(); uvl = bm.loops.layers.uv.verify()
    grid = []
    for ti, th in enumerate(thetas):
        col = []
        tc = front_top + (top - front_top) * (0.5 - 0.5 * math.cos(th)) ** 2
        for k in range(nrow):
            z = tc - (tc - hems[ti]) * k / (nrow - 1)
            r = radius(ti, z) * (1.0 + 0.04 * max(0.0, 0.9 - z) / 0.2)
            col.append(bm.verts.new(Vector((c.x + math.sin(th) * r, c.y - math.cos(th) * r, z))))
        grid.append(col)
    for ti in range(ncol):
        A = grid[ti]; B = grid[(ti + 1) % ncol]
        for k in range(nrow - 1):
            f = bm.faces.new((A[k], B[k], B[k + 1], A[k + 1]))
            for lp in f.loops:
                tt = ti + (1 if lp.vert in (B[k], B[k + 1]) else 0); kk = k + (1 if lp.vert in (A[k + 1], B[k + 1]) else 0)
                zz = lp.vert.co.z
                lp[uvl].uv = (0.02 + 0.46 * tt / ncol, 0.98 - 0.46 * (top - zz) / (top - zmin))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    o = _obj("PLR2_Kilt", bm, [mat])
    s = o.modifiers.new("Sol", "SOLIDIFY"); s.thickness = 0.004; s.offset = 1.0
    return o

def rope_ring(body, mat, z=0.992, off=0.018, sides=8, rad=0.0085, ncol=64, name="PLR2_Belt", sag=0.0, knot=True):
    c = Vector((0.0, 0.005)); thetas = [TAU * i / ncol for i in range(ncol)]
    r = _smooth_cyclic(hull_radius(slice_points(body, z), thetas, c), it=3)
    path = [Vector((c.x + math.sin(t) * (ri + off), c.y - math.cos(t) * (ri + off), z - sag * (0.5 + 0.5 * math.cos(t)))) for t, ri in zip(thetas, r)]
    bm = bmesh.new(); uvl = bm.loops.layers.uv.verify()
    tube(bm, uvl, path, rad, sides, closed=True, uv=(0.0, 0.5, 0.25, 0.5))
    if knot:   # knot + two hanging ends at the front left
        k = path[int(ncol * 0.08)]
        for dx, L in ((0.012, 0.14), (-0.01, 0.11)):
            end = [k + Vector((dx + 0.01 * s, -0.012, -L * s)) for s in (0, 0.35, 0.7, 1.0)]
            tube(bm, uvl, end, rad * 0.8, sides, closed=False, uv=(0.0, 0.5, 0.25, 0.5))
        g = bmesh.ops.create_icosphere(bm, subdivisions=1, radius=rad * 1.9)
        bmesh.ops.translate(bm, verts=g["verts"], vec=k + Vector((0, -0.006, 0)))
        for f in bm.faces:
            if all(v in g["verts"] for v in f.verts):
                for lp in f.loops: lp[uvl].uv = (0.1 + lp.vert.co.x * 3 % 0.3, 0.3 + lp.vert.co.z * 3 % 0.15)
    return _obj(name, bm, [mat])

def tube(bm, uvl, path, rad, sides, closed, uv):
    u0, u1, v0, v1 = uv
    n = len(path); rings = []
    for i, p in enumerate(path):
        a = path[(i - 1) % n] if (closed or i) else path[0]; b = path[(i + 1) % n] if (closed or i < n - 1) else path[-1]
        t = (b - a).normalized()
        up = Vector((0, 0, 1)) if abs(t.z) < 0.9 else Vector((1, 0, 0))
        x = t.cross(up).normalized(); y = t.cross(x)
        rings.append([bm.verts.new(p + (x * math.cos(TAU * k / sides) + y * math.sin(TAU * k / sides)) * rad) for k in range(sides)])
    L = 0.0; acc = [0.0]
    for i in range(1, n): L += (path[i] - path[i - 1]).length; acc.append(L)
    segs = n if closed else n - 1
    for i in range(segs):
        A = rings[i]; B = rings[(i + 1) % n]
        for k in range(sides):
            f = bm.faces.new((A[k], A[(k + 1) % sides], B[(k + 1) % sides], B[k]))
            ua = u0 + (u1 - u0) * (acc[i] / 0.25 % 1.0); ub = ua + (u1 - u0) * ((acc[(i + 1) % n] - acc[i]) % 1.0) / 0.25
            for lp, (uu, vv) in zip(f.loops, ((ua, k / sides), (ua, (k + 1) / sides), (ub, (k + 1) / sides), (ub, k / sides))):
                lp[uvl].uv = (uu, v0 + (v1 - v0) * vv)

def surface_patch(body, name, mats, keep_fn, offset, thick, uv_fn, jag=0.0, seed=1, mat_inner=None):
    """duplicate body faces selected by keep_fn(center), offset along normals, jagged border, solidify"""
    bm = bmesh.new(); bm.from_mesh(body.data); bm.normal_update()
    keep = set(f for f in bm.faces if keep_fn(f.calc_center_median()))
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f not in keep], context='FACES')
    bm.normal_update()
    border = set(v for e in bm.edges if e.is_boundary for v in e.verts)
    for v in bm.verts:
        o = offset
        if v in border and jag: o += jag * noise.noise(v.co * 40 + Vector((seed, 0, 0)))
        v.co = v.co + v.normal * o
    for it in range(2):  # relax the border a little
        for v in border:
            nb = [e.other_vert(v).co for e in v.link_edges if e.is_boundary]
            if nb: v.co = v.co * 0.5 + sum(nb, Vector()) / len(nb) * 0.5
    uvl = bm.loops.layers.uv.verify()
    for f in bm.faces:
        for lp in f.loops: lp[uvl].uv = uv_fn(lp.vert.co)
    o = _obj(name, bm, mats)
    if thick:
        s = o.modifiers.new("Sol", "SOLIDIFY"); s.thickness = thick; s.offset = -1.0
        if mat_inner is not None: s.material_offset = mat_inner; s.material_offset_rim = mat_inner
    return o

def pelt(body, hair_mat, cloth_mat, J):
    sh = Vector(J["l-shoulder"]); c0 = sh + Vector((-0.045, 0.0, 0.035))
    nsh = Vector((0.55, 0.0, 1.0)).normalized(); t1 = Vector((0, 1, 0)).cross(nsh).normalized(); t2 = nsh.cross(t1).normalized()
    def keep(c):
        if c.x < 0.035 or c.x > 0.29 or c.z < 1.36: return False
        d = c - c0
        return (d.x ** 2 + (d.y / 1.15) ** 2 + (d.z / 0.9) ** 2) ** 0.5 < 0.13
    def uv(p):
        d = p - c0
        return (0.75 + d.dot(t1) / 0.34 * 0.5, 0.25 + d.dot(t2) / 0.34 * 0.5)
    return surface_patch(body, "PLR2_Pelt", [hair_mat, cloth_mat], keep, 0.012, 0.008, uv, jag=0.016, seed=4, mat_inner=1)

def fur_surface_cards(pelt_obj, hair_mat, n=900, seed=31):
    rng = random.Random(seed)
    me = pelt_obj.data
    polys = [p for p in me.polygons if p.material_index == 0]
    bm = bmesh.new(); uvl = bm.loops.layers.uv.verify()
    for poly in rng.choices(polys, weights=[p.area for p in polys], k=n):
        c = poly.center.copy(); nr = poly.normal.copy()
        d = (Vector((0, 0, -1)) - nr * Vector((0, 0, -1)).dot(nr)).normalized() * 0.8 + nr * 0.35
        d = (d + Vector((rng.uniform(-0.4, 0.4), rng.uniform(-0.4, 0.4), 0))).normalized()
        L = rng.uniform(0.02, 0.035); w = rng.uniform(0.006, 0.011)
        side = d.cross(nr).normalized()
        pts = [c - nr * 0.001, c + d * L * 0.5 + nr * 0.004, c + d * L + nr * 0.002]
        rows = []
        for i, p in enumerate(pts):
            ww = w * (1 - 0.5 * i / 2); v = 0.98 - 0.45 * i / 2
            rows.append((bm.verts.new(p - side * ww / 2), bm.verts.new(p + side * ww / 2), v))
        u0 = rng.uniform(0.5, 0.93)
        for i in range(2):
            A, B, va = rows[i]; C, D, vb = rows[i + 1]
            fc = bm.faces.new((A, B, D, C))
            for lp, uvv in zip(fc.loops, ((u0, va), (u0 + 0.06, va), (u0 + 0.06, vb), (u0, vb))): lp[uvl].uv = uvv
    return _obj("PLR2_PeltFurTop", bm, [hair_mat])

def fur_edge_cards(pelt_obj, body, hair_mat, seed=21, per_edge=4):
    """short fur tufts along the pelt border (fur-strand region of the hair atlas)"""
    rng = random.Random(seed); bvh = body_bvh(body)
    me = pelt_obj.data; bm = bmesh.new(); bm.from_mesh(me); bm.normal_update()
    out = bmesh.new(); uvl = out.loops.layers.uv.verify()
    for e in bm.edges:
        if not e.is_boundary: continue
        a, b = e.verts; mid = (a.co + b.co) / 2; n = (a.normal + b.normal).normalized()
        f = e.link_faces[0]; inward = (f.calc_center_median() - mid).normalized()
        outward = -inward
        for k in range(per_edge):
            root = mid + (b.co - a.co) * rng.uniform(-0.4, 0.4) - outward * 0.004
            d = (outward * 0.6 + Vector((0, 0, -1)) * 0.6 + n * 0.15).normalized()
            L = rng.uniform(0.022, 0.045); w = rng.uniform(0.007, 0.013)
            side = d.cross(n).normalized()
            pts = [root, root + d * L * 0.5 + n * 0.003, root + d * L + n * 0.001]
            rows = []
            for i, p in enumerate(pts):
                q, nn, _, dist = bvh.find_nearest(p)
                if q is not None and (p - q).dot(nn) < 0.006: p = q + nn * 0.006
                ww = w * (1 - 0.5 * i / 2); v = 0.98 - 0.45 * i / 2
                rows.append((out.verts.new(p - side * ww / 2), out.verts.new(p + side * ww / 2), v))
            u0 = rng.uniform(0.5, 0.93)
            for i in range(2):
                A, B, va = rows[i]; C, D, vb = rows[i + 1]
                fc = out.faces.new((A, B, D, C))
                for lp, uvv in zip(fc.loops, ((u0, va), (u0 + 0.06, va), (u0 + 0.06, vb), (u0, vb))): lp[uvl].uv = uvv
    bm.free()
    return _obj("PLR2_PeltFur", out, [hair_mat])

def seg_dist(p, a, b):
    ab = b - a; t = max(0.0, min(1.0, (p - a).dot(ab) / ab.dot(ab))); return (p - (a + ab * t)).length, t

def chest_strap(body, cloth_mat, J):
    """twisted hide cord holding the pelt: from the left chest across the torso, under the right arm, around the back"""
    bvh = body_bvh(body)
    ctrl = [Vector((0.10, -0.15, 1.40)), Vector((0.0, -0.16, 1.33)), Vector((-0.12, -0.13, 1.25)), Vector((-0.2, -0.04, 1.22)),
            Vector((-0.18, 0.08, 1.26)), Vector((-0.02, 0.13, 1.34)), Vector((0.12, 0.11, 1.42))]
    path = []
    for a, b in zip(ctrl[:-1], ctrl[1:]):
        for k in range(10):
            q, n, _, _ = bvh.find_nearest(a.lerp(b, k / 10)); path.append(q + n * 0.007)
    q, n, _, _ = bvh.find_nearest(ctrl[-1]); path.append(q + n * 0.007)
    for it in range(3):
        path = [path[0]] + [(path[i - 1] + path[i] * 2 + path[i + 1]) / 4 for i in range(1, len(path) - 1)] + [path[-1]]
    bm = bmesh.new(); uvl = bm.loops.layers.uv.verify()
    tube(bm, uvl, path, 0.0045, 6, closed=False, uv=(0.0, 0.5, 0.25, 0.5))
    return _obj("PLR2_Strap", bm, [cloth_mat])

def wrist_wraps(body, cloth_mat, J):
    parts = []
    for s in ("l", "r"):
        w = Vector(J[f"{s}-hand"]); e = Vector(J[f"{s}-elbow"])
        axis = (w - e).normalized()
        def keep(c, w=w, axis=axis):
            t = (c - w).dot(axis)
            return -0.085 < t < -0.005 and (c - w - axis * t).length < 0.06
        uv = lambda p, w=w, axis=axis: (0.55 + ((p - w).dot(axis) * 4) % 0.4, 0.6 + (math.atan2(p.y - w.y, p.z - w.z) / TAU % 1) * 0.35)
        parts.append(surface_patch(body, f"PLR2_Wrap_{s}", [cloth_mat], keep, 0.0045, 0.0025, uv, jag=0.003, seed=7 if s == "l" else 9))
    return parts

def necklace(body, cloth_mat, J, n_teeth=5):
    bvh = body_bvh(body); nk = Vector(J["neck"])
    bm = bmesh.new(); uvl = bm.loops.layers.uv.verify()
    path = []
    for i in range(40):
        th = TAU * i / 40
        z = 1.535 - 0.07 * max(0.0, math.cos(th)) ** 2 + 0.01 * (1 - math.cos(th))
        d = Vector((math.sin(th), -math.cos(th), 0))
        p = Vector((0, 0.0, z)) + d * 0.2
        hit = bvh.ray_cast(p, -d)
        if hit[0] is not None: p = hit[0] + hit[1] * 0.006
        path.append(p)
    tube(bm, uvl, path, 0.0022, 6, closed=True, uv=(0.0, 0.5, 0.25, 0.5))
    # teeth / claws hanging at the front
    front = sorted(range(40), key=lambda i: abs(((i / 40 + 0.5) % 1) - 0.5))[:n_teeth]
    for k, i in enumerate(sorted(front, key=lambda i: ((i / 40 + 0.5) % 1))):
        p = path[i]; L = 0.03 if k == n_teeth // 2 else 0.022
        cone = bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=True, segments=6, radius1=0.0055, radius2=0.0006, depth=L)
        vs = cone["verts"]
        for v in vs:
            t = (v.co.z + L / 2) / L                     # 0 root .. 1 tip
            v.co = Vector((v.co.x, v.co.y - 0.006 * t * t, -t * L))    # hang down, curve forward
        q, nn, _, _ = bvh.find_nearest(p + Vector((0, 0, -0.02)))
        bmesh.ops.translate(bm, verts=vs, vec=p + (nn if nn is not None else Vector((0, -1, 0))) * 0.004)
        for f in bm.faces:
            if all(v in vs for v in f.verts):
                for lp in f.loops: lp[uvl].uv = (0.55 + (lp.vert.co.x * 20 % 0.4), 0.27 + (lp.vert.co.z * 8 % 0.2))
    return _obj("PLR2_Necklace", bm, [cloth_mat])
