# PRIMAL FRONTIER - procedural tileable PBR texture generator (numpy only).
# Outputs per material: T_<Name>_D.png (albedo, sRGB), T_<Name>_N.png (normal, OpenGL/Unity),
# T_<Name>_M.png (mask: R=metallic 0, G=AO, B=height, A=smoothness) -> works as URP Metallic map and Terrain mask map.
import numpy as np, os, math
from pngio import save_png

def vnoise(N, fx, fy, seed):
    rng = np.random.default_rng(seed)
    g = rng.random((fy, fx)).astype(np.float32)
    def idx(f):
        u = np.arange(N, dtype=np.float32) * f / N
        i0 = np.floor(u).astype(np.int64); t = u - i0
        t = t * t * (3 - 2 * t)
        return i0 % f, (i0 + 1) % f, t
    x0, x1, tx = idx(fx); y0, y1, ty = idx(fy)
    a = g[np.ix_(y0, x0)]; b = g[np.ix_(y0, x1)]; c = g[np.ix_(y1, x0)]; d = g[np.ix_(y1, x1)]
    tx = tx[None, :]; ty = ty[:, None]
    return (a * (1 - tx) + b * tx) * (1 - ty) + (c * (1 - tx) + d * tx) * ty


def vnoise_at(U, V, fx, fy, seed):
    rng = np.random.default_rng(seed)
    g = rng.random((fy, fx)).astype(np.float32)
    x = (U % 1.0) * fx; y = (V % 1.0) * fy
    x0 = np.floor(x).astype(np.int64); y0 = np.floor(y).astype(np.int64)
    tx = x - x0; ty = y - y0
    tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty)
    x0 %= fx; y0 %= fy; x1 = (x0 + 1) % fx; y1 = (y0 + 1) % fy
    a = g[y0, x0]; b = g[y0, x1]; c = g[y1, x0]; d = g[y1, x1]
    return (a * (1 - tx) + b * tx) * (1 - ty) + (c * (1 - tx) + d * tx) * ty

def fbm_at(U, V, fx, fy, octaves, seed, gain=0.5):
    s = np.zeros(U.shape, np.float32); amp = 1.0; tot = 0.0
    for i in range(octaves):
        m = 2 ** i
        s += amp * vnoise_at(U, V, fx * m, fy * m, seed + i * 31)
        tot += amp; amp *= gain
    return s / tot

def grid(N):
    u = (np.arange(N, dtype=np.float32) + 0.5) / N
    return np.meshgrid(u, u)

def ridged(x, p=2.0):
    return (1 - np.abs(2 * x - 1)) ** p

def fbm(N, f, octaves, seed, ax=1, ay=1, gain=0.5):
    s = np.zeros((N, N), np.float32); amp = 1.0; tot = 0.0
    for i in range(octaves):
        m = 2 ** i
        s += amp * vnoise(N, max(1, int(f * m * ax)), max(1, int(f * m * ay)), seed + i * 17)
        tot += amp; amp *= gain
    return s / tot

def voronoi(N, f, seed):
    rng = np.random.default_rng(seed)
    pts = rng.random((f, f, 2)).astype(np.float32)
    u = (np.arange(N, dtype=np.float32) + 0.5) / N * f
    X, Y = np.meshgrid(u, u)
    cx = np.floor(X).astype(np.int64); cy = np.floor(Y).astype(np.int64)
    F1 = np.full((N, N), 9.0, np.float32); F2 = np.full((N, N), 9.0, np.float32)
    ID = np.zeros((N, N), np.float32)
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            nx = cx + dx; ny = cy + dy
            p = pts[ny % f, nx % f]
            px = nx + p[..., 0]; py = ny + p[..., 1]
            d = np.sqrt((X - px) ** 2 + (Y - py) ** 2)
            closer = d < F1
            F2 = np.where(closer, F1, np.minimum(F2, d))
            ID = np.where(closer, ((ny % f) * f + (nx % f)) * 0.6180339 % 1.0, ID)
            F1 = np.minimum(F1, d)
    return F1, F2, ID

def normal_from_height(h, strength):
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5
    dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 0.5
    n = np.stack([-dx * strength, dy * strength, np.ones_like(h)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return n * 0.5 + 0.5

def ramp(t, cols):
    """cols: list of (pos, (r,g,b)) sorted by pos"""
    t = np.clip(t, 0, 1)[..., None]
    out = np.zeros(t.shape[:-1] + (3,), np.float32)
    ps = [c[0] for c in cols]; cs = [np.array(c[1], np.float32) for c in cols]
    out[:] = cs[0]
    for i in range(len(cols) - 1):
        a, b = ps[i], ps[i + 1]
        w = np.clip((t - a) / max(b - a, 1e-6), 0, 1)
        m = (t >= a)
        out = np.where(m, cs[i] * (1 - w) + cs[i + 1] * w, out)
    return out

def norm01(x):
    return (x - x.min()) / (x.max() - x.min() + 1e-8)

def ao_from_height(h, k=1.0):
    blur = h.copy()
    for s in (2, 4, 8, 16):
        blur = (blur + np.roll(blur, s, 0) + np.roll(blur, -s, 0) + np.roll(blur, s, 1) + np.roll(blur, -s, 1)) / 5
    return np.clip(1 - np.clip(blur - h, 0, 1) * 4 * k, 0.35, 1)

def save_set(out, name, alb, h, nstrength, smooth, ao=None):
    if ao is None: ao = ao_from_height(norm01(h))
    save_png(os.path.join(out, f"T_{name}_D.png"), alb * ao[..., None] ** 0.6)
    save_png(os.path.join(out, f"T_{name}_N.png"), normal_from_height(h, nstrength))
    N = h.shape[0]
    m = np.stack([np.zeros((N, N)), ao, norm01(h), np.broadcast_to(np.clip(smooth, 0, 1), (N, N))], -1)
    save_png(os.path.join(out, f"T_{name}_M.png"), m)

# ---------------------------------------------------------------- materials
def sand(out, N=1024):
    U, V = grid(N)
    grains = fbm(N, 96, 3, 11); big = fbm(N, 3, 5, 12)
    warp = fbm_at(U, V, 3, 3, 3, 13)
    ripple = 0.5 + 0.5 * np.sin(2 * math.pi * (V * 14 + warp * 2.5 + U * 1))
    h = ripple * 0.12 + grains * 0.5 + big * 0.4
    t = big * 0.75 + grains * 0.25 + ripple * 0.04
    alb = ramp(t, [(0, (0.66, 0.56, 0.41)), (0.5, (0.77, 0.67, 0.50)), (1, (0.85, 0.77, 0.61))])
    speck = (fbm(N, 160, 1, 14) > 0.8).astype(np.float32)[..., None]
    alb = alb * (1 - speck * 0.18)
    one = np.ones((N, N), np.float32)
    save_set(out, "Sand", alb, h, 2.5, 0.12 + grains * 0.05, ao=one)
    wet = alb * np.array([0.60, 0.58, 0.55], np.float32)
    save_set(out, "SandWet", wet, h * 0.5, 1.5, 0.55 + big * 0.2, ao=one)

def grass(out, N=1024):
    b = fbm(N, 6, 5, 21); d = fbm(N, 3, 4, 22); fine = fbm(N, 128, 2, 23)
    blades = fbm(N, 180, 1, 24, ax=1, ay=3)
    h = fine * 0.4 + blades * 0.4 + b * 0.3
    t = b * 0.7 + fine * 0.3
    alb = ramp(t, [(0, (0.18, 0.24, 0.10)), (0.5, (0.29, 0.35, 0.16)), (1, (0.44, 0.42, 0.24))])
    dirt = np.clip((d - 0.58) * 6, 0, 1)[..., None]
    alb = alb * (1 - dirt) + ramp(fine, [(0, (0.26, 0.20, 0.13)), (1, (0.40, 0.32, 0.22))]) * dirt
    save_set(out, "Grass", alb, h, 4.0, 0.18 + fine * 0.08)

def forest_floor(out, N=1024):
    rng = np.random.default_rng(31)
    b = fbm(N, 5, 5, 32); fine = fbm(N, 96, 2, 33)
    rgb = ramp(b * 0.6 + fine * 0.4, [(0, (0.09, 0.07, 0.05)), (1, (0.22, 0.16, 0.10))]).astype(np.float32)
    hh = np.zeros((N, N), np.float32)
    pal = [(0.36, 0.24, 0.13), (0.45, 0.31, 0.16), (0.27, 0.18, 0.10), (0.52, 0.40, 0.22), (0.24, 0.28, 0.12)]
    for i in range(2600):
        cx, cy = rng.random() * N, rng.random() * N
        L = 14 + rng.random() * 30; W = L * (0.35 + rng.random() * 0.25); ang = rng.random() * 6.283
        col = pal[rng.integers(len(pal))]
        for ox in (-N, 0, N):
            for oy in (-N, 0, N):
                if -60 < cx + ox < N + 60 and -60 < cy + oy < N + 60:
                    a = np.zeros((N, N), np.float32) if False else None
                    _leaf_h(rgb, hh, cx + ox, cy + oy, L, W, ang, col, rng, i / 2600.0)
    moss = np.clip((fbm(N, 4, 4, 34) - 0.6) * 5, 0, 1)[..., None]
    rgb = rgb * (1 - moss * 0.6) + np.array([0.20, 0.26, 0.11], np.float32) * (0.8 + fine[..., None] * 0.4) * moss * 0.6
    h = hh * 0.6 + b * 0.25 + fine * 0.15
    save_set(out, "ForestFloor", rgb, h, 5.0, 0.12 + hh * 0.1)

def rock(out, N=1024):
    U, V = grid(N)
    wx = fbm_at(U, V, 3, 3, 4, 45) - 0.5; wy = fbm_at(U, V, 3, 3, 4, 46) - 0.5
    r1 = ridged(fbm_at(U + wx * 0.25, V + wy * 0.25, 4, 4, 6, 47), 2.5)
    r2 = ridged(fbm_at(U + wx * 0.1, V + wy * 0.1, 12, 12, 4, 48), 1.5)
    fine = fbm(N, 96, 3, 43)
    F1, F2, ID = voronoi(N, 5, 41)
    crack = np.clip(1 - (F2 - F1) * 25, 0, 1) ** 2
    strata = 0.5 + 0.5 * np.sin(2 * math.pi * (V * 7 + wx * 1.2))
    blocks = fbm_at(U + wx * 0.3, V + wy * 0.3, 3, 3, 3, 49)
    h = (1 - r1) * 0.5 + (1 - r2) * 0.08 + fine * 0.15 + strata * 0.07 + blocks * 0.35
    low = fbm(N, 2, 4, 42)
    t = norm01(h) * 0.6 + low * 0.4
    alb = ramp(t, [(0, (0.24, 0.23, 0.21)), (0.35, (0.38, 0.36, 0.33)), (0.7, (0.50, 0.47, 0.42)), (1, (0.60, 0.56, 0.50))])
    lichen = np.clip((fbm(N, 8, 4, 44) - 0.64) * 6, 0, 1)[..., None] * np.clip(norm01(h) * 1.5, 0, 1)[..., None]
    alb = alb * (1 - lichen * 0.5) + np.array([0.43, 0.44, 0.29], np.float32) * lichen * 0.5
    save_set(out, "Rock", alb, h, 12.0, 0.18 + fine * 0.1)

def mud(out, N=1024):
    U, V = grid(N)
    b = fbm(N, 4, 5, 51); fine = fbm(N, 64, 3, 52)
    wx = fbm_at(U, V, 4, 4, 3, 54) - 0.5
    ruts = ridged(fbm_at(U + wx * 0.2, V, 6, 6, 4, 55), 3)
    h = b * 0.5 + fine * 0.25 + ruts * 0.25
    alb = ramp(b * 0.6 + fine * 0.4, [(0, (0.13, 0.10, 0.07)), (0.6, (0.22, 0.17, 0.11)), (1, (0.32, 0.25, 0.17))])
    puddle = np.clip((0.38 - b) * 8, 0, 1)
    save_set(out, "Mud", alb * (1 - puddle[..., None] * 0.35), h * (1 - puddle * 0.7), 3.0, 0.35 + puddle * 0.5)

def bark(out, N=1024):
    U, V = grid(N)
    wx = fbm_at(U, V, 4, 2, 3, 66) - 0.5
    ridges = ridged(fbm_at(U + wx * 0.08, V, 10, 2, 4, 61), 1.6)
    plates = fbm_at(U + wx * 0.05, V, 20, 6, 3, 62)
    fine = fbm(N, 64, 3, 65)
    h = ridges * 0.55 + plates * 0.3 + fine * 0.15
    alb = ramp(norm01(h), [(0, (0.09, 0.07, 0.05)), (0.35, (0.20, 0.16, 0.12)), (0.75, (0.33, 0.27, 0.20)), (1, (0.44, 0.38, 0.30))])
    moss = np.clip((fbm(N, 3, 4, 67) - 0.62) * 5, 0, 1)[..., None]
    alb = alb * (1 - moss * 0.5) + np.array([0.20, 0.26, 0.11], np.float32) * moss * 0.5
    save_set(out, "Bark", alb, h, 8.0, 0.1 + fine * 0.05)

def planks(out, N=1024, n=8):
    Y = np.arange(N)[:, None] / N; X = np.arange(N)[None, :] / N
    row = np.floor(Y * n)
    fr = Y * n - row
    gap = np.clip(np.minimum(fr, 1 - fr) * 60, 0, 1)           # 0 at seams
    rng = np.random.default_rng(71)
    off = rng.random(n)[row.astype(int)[:, 0]][:, None]
    seg = ((X + off) * 2) % 1.0
    butt = np.clip(np.minimum(seg, 1 - seg) * 120, 0, 1)
    grain = fbm(N, 4, 5, 72, ax=1, ay=24)
    rings = 0.5 + 0.5 * np.sin(2 * math.pi * (grain * 6 + fr * 1.5))
    tone = rng.random(n)[row.astype(int)[:, 0]][:, None]
    weather = fbm(N, 6, 4, 73)
    t = rings * 0.4 + tone * 0.35 + grain * 0.25
    alb = ramp(t, [(0, (0.22, 0.16, 0.10)), (0.5, (0.36, 0.27, 0.18)), (1, (0.48, 0.38, 0.26))])
    bleach = np.clip((weather - 0.45) * 2.5, 0, 1)[..., None]
    alb = alb * (1 - bleach * 0.6) + np.array([0.52, 0.49, 0.43], np.float32) * bleach * 0.6
    nails = ((np.abs(seg - 0.04) < 0.004) & (np.abs(fr - 0.5) < 0.12 * 2 / n * n / 2 * 0.2)).astype(np.float32)
    seam = gap * butt
    alb *= (0.25 + 0.75 * seam)[..., None]
    alb = alb * (1 - nails[..., None]) + np.array([0.18, 0.14, 0.12]) * nails[..., None]
    h = seam * 0.6 + rings * 0.15 + grain * 0.25
    save_set(out, "WoodPlanks", alb, h, 6.0, 0.18 + (1 - bleach[..., 0]) * 0.12)

def wood_beam(out, N=1024):
    grain = fbm(N, 3, 6, 81, ax=1, ay=20)
    rings = 0.5 + 0.5 * np.sin(2 * math.pi * grain * 9)
    cracks = np.clip(1 - np.abs(vnoise(N, 14, 1, 82) - 0.5) * 30, 0, 1)
    h = rings * 0.3 + grain * 0.5 - cracks * 0.4
    alb = ramp(rings * 0.5 + grain * 0.5, [(0, (0.24, 0.17, 0.11)), (1, (0.46, 0.35, 0.24))]) * (1 - cracks[..., None] * 0.6)
    save_set(out, "WoodBeam", alb, h, 5.0, 0.2)

def cloth(out, N=1024):
    Y = np.arange(N)[:, None] / N; X = np.arange(N)[None, :] / N
    weave = (0.5 + 0.25 * np.sin(2 * math.pi * X * 256) + 0.25 * np.sin(2 * math.pi * Y * 256))
    stain = fbm(N, 5, 5, 91); fine = fbm(N, 64, 2, 92)
    alb = ramp(stain, [(0, (0.46, 0.39, 0.28)), (0.45, (0.66, 0.60, 0.48)), (1, (0.78, 0.73, 0.62))])
    alb *= (0.85 + 0.15 * weave)[..., None]
    h = weave * 0.3 + fine * 0.3 + stain * 0.2
    save_set(out, "SailCloth", alb, h, 2.0, 0.08 + (1 - stain) * 0.1)

def rope(out, N=512):
    Y = np.arange(N)[:, None] / N; X = np.arange(N)[None, :] / N
    tw = 0.5 + 0.5 * np.sin(2 * math.pi * (X * 8 + Y * 16))
    fib = fbm(N, 8, 3, 101, ax=1, ay=8)
    h = tw * 0.7 + fib * 0.3
    alb = ramp(h, [(0, (0.30, 0.24, 0.15)), (1, (0.66, 0.56, 0.39))])
    save_set(out, "Rope", alb, h, 5.0, 0.1)

# ---------------------------------------------------------------- foliage atlas (2x2)
def _leaf(rgb, a, cx, cy, L, W, ang, col, rng, shape=0.8, vein=True):
    H, Wd = a.shape
    ca, sa = math.cos(ang), math.sin(ang)
    ex = abs(ca) * L + abs(sa) * W; ey = abs(sa) * L + abs(ca) * W
    x0 = int(max(0, cx - ex)); x1 = int(min(Wd, cx + ex + 1)); y0 = int(max(0, cy - ey)); y1 = int(min(H, cy + ey + 1))
    if x1 <= x0 or y1 <= y0: return
    X, Y = np.meshgrid(np.arange(x0, x1) - cx, np.arange(y0, y1) - cy)
    u = X * ca + Y * sa; v = -X * sa + Y * ca
    s = np.clip(u / L, 0, 1)
    half = W * 0.5 * np.sin(np.pi * s) ** shape
    m = (u >= 0) & (u <= L) & (np.abs(v) <= half)
    if not m.any(): return
    shade = 0.75 + 0.35 * s - 0.25 * (np.abs(v) / (half + 1e-3))
    c = np.array(col, np.float32) * (0.9 + 0.2 * rng.random())
    pix = c[None, None, :] * shade[..., None]
    if vein:
        vm = np.abs(v) < max(1.0, W * 0.04)
        pix = np.where(vm[..., None], pix * 1.25, pix)
    sub = rgb[y0:y1, x0:x1]; sub[m] = pix[m]
    a[y0:y1, x0:x1][m] = 1.0

def _stem(rgb, a, pts, width, col):
    for i in range(len(pts) - 1):
        (xa, ya), (xb, yb) = pts[i], pts[i + 1]
        L = math.hypot(xb - xa, yb - ya)
        steps = max(2, int(L))
        for k in range(steps):
            t = k / steps
            x = xa + (xb - xa) * t; y = ya + (yb - ya) * t
            r = width
            X0, X1, Y0, Y1 = int(x - r), int(x + r + 1), int(y - r), int(y + r + 1)
            rgb[max(0,Y0):Y1, max(0,X0):X1] = col
            a[max(0,Y0):Y1, max(0,X0):X1] = 1


def _leaf_h(rgb, hh, cx, cy, L, W, ang, col, rng, layer):
    H, Wd = hh.shape
    ca, sa = math.cos(ang), math.sin(ang)
    ex = abs(ca) * L + abs(sa) * W; ey = abs(sa) * L + abs(ca) * W
    x0 = int(max(0, cx - ex)); x1 = int(min(Wd, cx + ex + 1)); y0 = int(max(0, cy - ey)); y1 = int(min(H, cy + ey + 1))
    if x1 <= x0 or y1 <= y0: return
    X, Y = np.meshgrid(np.arange(x0, x1) - cx, np.arange(y0, y1) - cy)
    u = X * ca + Y * sa; v = -X * sa + Y * ca
    s_ = np.clip(u / L, 0, 1)
    half = W * 0.5 * np.sin(np.pi * s_) ** 0.8
    m = (u >= 0) & (u <= L) & (np.abs(v) <= half)
    if not m.any(): return
    c = np.array(col, np.float32) * (0.75 + 0.4 * rng.random())
    shade = 0.8 + 0.25 * s_
    vein = np.abs(v) < max(0.8, W * 0.05)
    pix = c[None, None, :] * shade[..., None] * np.where(vein, 0.8, 1.0)[..., None]
    sub = rgb[y0:y1, x0:x1]; sub[m] = pix[m]
    hs = hh[y0:y1, x0:x1]; hs[m] = 0.4 + 0.6 * layer

def foliage_atlas(out, S=2048):
    Q = S // 2
    rgb = np.zeros((S, S, 3), np.float32); a = np.zeros((S, S), np.float32)
    rng = np.random.default_rng(111)
    # base color bleed so mip edges are not black
    rgb[:] = (0.16, 0.22, 0.09)
    # Q0 top-left: conifer spray (araucaria-like overlapping scales)
    for b in range(5):
        sx, sy = Q * 0.5 + (b - 2) * 70, Q * 0.95
        ang = -math.pi / 2 + (b - 2) * 0.28
        L = Q * (0.78 - abs(b - 2) * 0.08)
        pts = [(sx + math.cos(ang) * L * t + math.sin(t * 3) * 10, sy + math.sin(ang) * L * t) for t in np.linspace(0, 1, 12)]
        _stem(rgb, a, pts, 5, (0.22, 0.17, 0.10))
        for t in np.linspace(0.05, 0.98, 90):
            px = sx + math.cos(ang) * L * t; py = sy + math.sin(ang) * L * t
            for side in (-1, 1):
                la = ang + side * (0.9 + rng.random() * 0.3)
                size = 38 * (1.1 - t * 0.6)
                _leaf(rgb, a, px, py, size, size * 0.35, la, (0.17, 0.27, 0.10), rng, 0.6, vein=False)
    # Q1 top-right: broadleaf cluster
    ox = Q
    for b in range(7):
        ang = -math.pi / 2 + (b - 3) * 0.35
        L = Q * 0.62
        sx, sy = ox + Q * 0.5, Q * 0.98
        pts = [(sx + math.cos(ang) * L * t, sy + math.sin(ang) * L * t) for t in np.linspace(0, 1, 8)]
        _stem(rgb, a, pts, 4, (0.24, 0.18, 0.11))
        for t in np.linspace(0.25, 1.0, 9):
            px = sx + math.cos(ang) * L * t; py = sy + math.sin(ang) * L * t
            for side in (-1, 1):
                la = ang + side * (0.6 + rng.random() * 0.5)
                _leaf(rgb, a, px, py, 120 * (1.1 - t * 0.3), 55, la, (0.20, 0.31, 0.11) if rng.random() > .2 else (0.33, 0.35, 0.14), rng, 0.9)
    # Q2 bottom-left: fern frond (pointing up)
    oy = Q
    sx, sy = Q * 0.5, oy + Q * 0.98
    L = Q * 0.92
    pts = [(sx + math.sin(t * 2.2) * 30 * t, sy - L * t) for t in np.linspace(0, 1, 30)]
    _stem(rgb, a, pts, 4, (0.25, 0.22, 0.10))
    for i, t in enumerate(np.linspace(0.06, 0.97, 44)):
        px = sx + math.sin(t * 2.2) * 30 * t; py = sy - L * t
        plen = Q * 0.24 * math.sin(math.pi * t) ** 0.75 + 10
        for side in (-1, 1):
            la = -math.pi / 2 + side * (1.05 - t * 0.35)
            # pinna made of small lobes
            for k in np.linspace(0, 1, 9):
                qx = px + math.cos(la) * plen * k; qy = py + math.sin(la) * plen * k
                lob = plen * 0.22 * (1.0 - k * 0.6) + 6
                _leaf(rgb, a, qx, qy, lob, lob * 0.55, la + side * 0.9, (0.19, 0.30, 0.10), rng, 0.7, vein=False)
                _leaf(rgb, a, qx, qy, lob, lob * 0.55, la - side * 0.9, (0.19, 0.30, 0.10), rng, 0.7, vein=False)
            _stem(rgb, a, [(px, py), (px + math.cos(la) * plen, py + math.sin(la) * plen)], 1.5, (0.22, 0.26, 0.10))
    # Q3 bottom-right: grass tuft
    for i in range(140):
        bx = Q + Q * 0.5 + rng.normal(0, Q * 0.14); by = S - 2
        h = Q * (0.45 + rng.random() * 0.5)
        ang = -math.pi / 2 + rng.normal(0, 0.28)
        col = (0.30, 0.37, 0.15) if rng.random() > 0.3 else (0.47, 0.44, 0.25)
        _leaf(rgb, a, bx, by, h, 14 + rng.random() * 8, ang, col, rng, 0.35, vein=False)
    save_png(os.path.join(out, "T_FoliageAtlas_D.png"), np.concatenate([rgb, a[..., None]], -1))
    nrm = np.zeros((S, S, 3), np.float32); nrm[:] = (0.5, 0.5, 1.0)
    save_png(os.path.join(out, "T_FoliageAtlas_N.png"), nrm)

def run_all(out, only=None):
    os.makedirs(out, exist_ok=True)
    fns = (sand, grass, forest_floor, rock, mud, bark, planks, wood_beam, cloth, rope, foliage_atlas)
    for fn in fns:
        if only and fn.__name__ not in only: continue
        fn(out)
    return sorted(os.listdir(out))

# ---------------------------------------------------------------- character textures
def player_hair(out, W=512, H=1024, seed=301):
    """strand clumps with alpha: dark brown, sun-bleached tips; row 0 = top (root)."""
    rng = np.random.default_rng(seed)
    rgb = np.zeros((H, W, 3), np.float32); a = np.zeros((H, W), np.float32)
    rgb[:] = (0.09, 0.06, 0.04)
    Y = np.arange(H)[:, None] / H
    for i in range(1400):
        x0 = rng.random() * W; wdt = 1.2 + rng.random() * 2.2
        ln = 0.55 + rng.random() * 0.45
        wave = rng.random() * 18; ph = rng.random() * 6.28; drift = (rng.random() - 0.5) * 40
        ys = np.arange(int(H * ln))
        xs = x0 + np.sin(ys / H * 6 + ph) * wave + ys / H * drift
        col = np.array([0.10, 0.07, 0.045]) * (0.6 + rng.random() * 0.9)
        tip = np.array([0.30, 0.22, 0.14])
        for y, x in zip(ys[::1], xs[::1]):
            t = y / (H * ln)
            xi0 = int(max(0, x - wdt)); xi1 = int(min(W, x + wdt + 1))
            if xi1 <= xi0: continue
            c = col * (1 - t * 0.6) + tip * (t * 0.6)
            rgb[y, xi0:xi1] = c
            a[y, xi0:xi1] = np.maximum(a[y, xi0:xi1], (1 - t ** 3) * (0.9 + 0.1 * rng.random()))
    save_png(os.path.join(out, "T_Player_Hair_D.png"), np.concatenate([rgb, a[..., None]], -1))
    n = np.zeros((H, W, 3), np.float32); n[:] = (0.5, 0.5, 1.0)
    save_png(os.path.join(out, "T_Player_Hair_N.png"), n)

def player_cloth(out, N=1024):
    """coarse hand-woven plant-fibre wrap, dirty, stained, frayed."""
    U, V = grid(N)
    weave = 0.5 + 0.25 * np.sin(2 * math.pi * U * 96) * np.sign(np.sin(2 * math.pi * V * 48)) + 0.25 * np.sin(2 * math.pi * V * 96)
    fib = fbm(N, 24, 3, 311, ax=1, ay=6)
    stain = fbm(N, 4, 5, 312); dirt = fbm(N, 9, 4, 313)
    alb = ramp(stain * 0.7 + fib * 0.3, [(0, (0.22, 0.18, 0.13)), (0.5, (0.40, 0.34, 0.25)), (1, (0.55, 0.48, 0.36))])
    alb *= (0.82 + 0.18 * weave)[..., None]
    grime = np.clip((dirt - 0.5) * 3, 0, 1)[..., None]
    alb = alb * (1 - grime * 0.45) + np.array([0.16, 0.12, 0.08], np.float32) * grime * 0.45
    h = weave * 0.5 + fib * 0.3 + stain * 0.2
    save_set(out, "Player_Cloth", alb, h, 3.0, 0.06 + (1 - stain) * 0.06)

def eye_texture(out, N=256):
    """UV-sphere eye: pupil at v=1 (+Y pole points forward after placement)."""
    U, V = grid(N)
    r = V                            # image row 0 = UV v=1 = front pole
    iris = np.clip((0.23 - r) * 80, 0, 1); pupil = np.clip((0.09 - r) * 120, 0, 1)
    ang = U * 2 * math.pi
    fibres = 0.5 + 0.5 * np.sin(ang * 60 + fbm(N, 8, 2, 321) * 6)
    sclera = np.array([0.82, 0.78, 0.72], np.float32)
    ic = ramp(r / 0.23 + fibres * 0.15, [(0, (0.12, 0.07, 0.03)), (0.7, (0.32, 0.2, 0.09)), (1, (0.18, 0.12, 0.06))])
    col = sclera * (1 - iris[..., None]) + ic * iris[..., None]
    col = col * (1 - pupil[..., None]) + np.array([0.01, 0.01, 0.01]) * pupil[..., None]
    veins = np.clip((fbm(N, 20, 3, 322) - 0.72) * 4, 0, 1) * np.clip((r - 0.3) * 3, 0, 1)
    col = col * (1 - veins[..., None] * 0.3) + np.array([0.6, 0.2, 0.2]) * veins[..., None] * 0.3
    save_png(os.path.join(out, "T_Player_Eye_D.png"), col)
