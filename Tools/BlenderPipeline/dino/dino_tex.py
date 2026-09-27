"""
PRIMAL FRONTIER - procedural dinosaur skin textures (original): 3D cellular scales, species colour pattern, belly /
dorsal gradient, horn / beak / claw keratin, teeth, eyes, membranes. Input: <sid>_texdata.npz written by pf_dino.py
(positions, normals, per-corner UVs, part ids). Output: T_Dino_<Sid>_D/N/M.png (+ _Eye_ set).
python3 dino_tex.py <sid> <work_dir> <tex_out_dir> [size]
"""
import sys, os, json, math
import numpy as np
from PIL import Image
from scipy import ndimage
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from dino_specs import SPECS

def rasterize(uv, co, nr, tris, part, S):
    P = np.zeros((S, S, 3), np.float32); N = np.zeros((S, S, 3), np.float32); T = np.full((S, S), -1, np.int16); M = np.zeros((S, S), bool)
    U = uv * (S - 1); U[..., 1] = (S - 1) - U[..., 1]          # image rows go down
    for k in range(len(tris)):
        a, b, c = U[k]
        x0 = int(max(0, math.floor(min(a[0], b[0], c[0])))); x1 = int(min(S - 1, math.ceil(max(a[0], b[0], c[0]))))
        y0 = int(max(0, math.floor(min(a[1], b[1], c[1])))); y1 = int(min(S - 1, math.ceil(max(a[1], b[1], c[1]))))
        if x1 < x0 or y1 < y0: continue
        xs, ys = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        d = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
        if abs(d) < 1e-9: continue
        l1 = ((b[1] - c[1]) * (xs - c[0]) + (c[0] - b[0]) * (ys - c[1])) / d
        l2 = ((c[1] - a[1]) * (xs - c[0]) + (a[0] - c[0]) * (ys - c[1])) / d
        l3 = 1 - l1 - l2
        inside = (l1 >= -0.01) & (l2 >= -0.01) & (l3 >= -0.01)
        if not inside.any(): continue
        yy, xx = np.nonzero(inside); yy += y0; xx += x0
        L1, L2, L3 = l1[inside][:, None], l2[inside][:, None], l3[inside][:, None]
        i0, i1, i2 = tris[k]
        P[yy, xx] = co[i0] * L1 + co[i1] * L2 + co[i2] * L3
        N[yy, xx] = nr[i0] * L1 + nr[i1] * L2 + nr[i2] * L3
        T[yy, xx] = part[i0] if part[i0] == part[i1] else max(part[i0], part[i1], part[i2])
        M[yy, xx] = True
    # nearest covered texel for every gutter texel (used to dilate the final images)
    idx = ndimage.distance_transform_edt(~M, return_distances=False, return_indices=True)
    N /= np.linalg.norm(N, axis=-1, keepdims=True) + 1e-9
    return P, N, T, M, idx

def hash3(c):
    # integer cell coords -> pseudo random in [0,1)^3
    c = c.astype(np.int32)
    h = (c[..., 0] * np.int32(73856093)) ^ (c[..., 1] * np.int32(19349663)) ^ (c[..., 2] * np.int32(83492791))
    h = (h ^ (h >> 13)) * np.int32(1274126177); h = h ^ (h >> 16)
    return np.stack([(h & 1023), ((h >> 10) & 1023), ((h >> 20) & 1023)], -1).astype(np.float32) * (1 / 1024.0)

def voronoi(P, scale, jitter=0.9):
    """F1, F2 distances and a cell random value for points P (n,3) in world metres"""
    q = P / scale; base = np.floor(q)
    f1 = np.full(len(P), 9.0, np.float32); f2 = np.full(len(P), 9.0, np.float32); cid = np.zeros(len(P), np.float32)
    for dx in (-1, 0, 1):
        for dy in (-1, 0, 1):
            for dz in (-1, 0, 1):
                c = base + np.array([dx, dy, dz], np.float32)
                r = hash3(c); pt = c + 0.5 + (r - 0.5) * jitter
                d = np.linalg.norm(q - pt, axis=-1)
                closer = d < f1
                f2 = np.where(closer, f1, np.minimum(f2, d)); cid = np.where(closer, r[:, 0], cid); f1 = np.minimum(f1, d)
    return f1, f2, cid

def vnoise(P, scale, seed=0):
    """smooth value noise (trilinear) in [0,1]"""
    q = P / scale + seed * 17.3; b = np.floor(q); f = q - b; f = f * f * (3 - 2 * f)
    acc = 0
    for dx in (0, 1):
        for dy in (0, 1):
            for dz in (0, 1):
                w = (f[:, 0] if dx else 1 - f[:, 0]) * (f[:, 1] if dy else 1 - f[:, 1]) * (f[:, 2] if dz else 1 - f[:, 2])
                acc = acc + w * hash3(b + np.array([dx, dy, dz], np.float32))[:, 0]
    return acc

def fbm(P, scale, oct=4, seed=0):
    a = 0; amp = 0.5; tot = 0
    for o in range(oct):
        a = a + vnoise(P, scale / (2 ** o), seed + o) * amp; tot += amp; amp *= 0.5
    return a / tot

def seg_dist(P, a, b):
    a = np.array(a, np.float32); b = np.array(b, np.float32); ab = b - a
    t = np.clip(((P - a) @ ab) / max(ab @ ab, 1e-9), 0, 1)
    return np.linalg.norm(P - (a + t[:, None] * ab), axis=-1), t

def smoothstep(e0, e1, x): t = np.clip((x - e0) / (e1 - e0 + 1e-9), 0, 1); return t * t * (3 - 2 * t)

def build(sid, work, out, S):
    sp = SPECS[sid]; col = sp["colors"]; J = json.load(open(os.path.join(work, f"{sid}_joints.json")))
    d = np.load(os.path.join(work, f"{sid}_texdata.npz"))
    P, N, T, M, idx = rasterize(d["uv"], d["co"], d["nr"], d["tris"], d["part"], S)
    sel = M.reshape(-1)
    flat = P.reshape(-1, 3)[sel]; nflat = N.reshape(-1, 3)[sel]; tflat = T.reshape(-1)[sel]
    def full(a, fill=0.0):
        out = np.full((S * S,) + a.shape[1:], fill, np.float32); out[sel] = a
        out = out.reshape((S, S) + a.shape[1:]); return out[idx[0], idx[1]]
    L = sp["length"]; sc = col["scale"]
    # --- scales: big dorsal scales, small ventral / limb scales
    f1a, f2a, ca = voronoi(flat, sc)
    f1b, f2b, cb = voronoi(flat, sc * 0.45)
    zmin, zmax = flat[:, 2].min(), flat[:, 2].max()
    dorsal = np.clip(nflat[:, 2] * 0.7 + 0.3, 0, 1)                        # 1 on the back, 0 underneath
    wbig = smoothstep(0.35, 0.75, dorsal)
    edge_a = f2a - f1a; edge_b = f2b - f1b
    hs = wbig * smoothstep(0.0, 0.35, edge_a) + (1 - wbig) * smoothstep(0.0, 0.35, edge_b)   # 0 in crevices, 1 on scale centres
    cell = wbig * ca + (1 - wbig) * cb
    big = fbm(flat, L * 0.12, 4, 1); mid = fbm(flat, L * 0.03, 3, 5)
    # --- colour
    base = np.array(col["base"], np.float32); belly = np.array(col["belly"], np.float32); dors = np.array(col["dorsal"], np.float32); acc = np.array(col["accent"], np.float32)
    underside = smoothstep(0.55, 0.05, np.clip(nflat[:, 2] * 0.5 + 0.5, 0, 1))  # 1 on the belly
    c = base[None] * (0.85 + 0.3 * big[:, None])
    c = c * (1 - underside[:, None]) + belly[None] * underside[:, None] * (0.9 + 0.2 * mid[:, None])
    topness = smoothstep(0.6, 0.95, dorsal)
    c = c * (1 - topness[:, None] * 0.55) + dors[None] * topness[:, None] * 0.55
    y = flat[:, 1]; pat = sp["colors"]["pattern"]
    if pat in ("bands", "stripes"):
        freq = (2 * math.pi) / (L * (0.09 if pat == "bands" else 0.045))
        s = np.sin(y * freq + fbm(flat, L * 0.05, 3, 9) * 5.0)
        band = smoothstep(0.35, 0.8, s) * smoothstep(0.35, 0.85, dorsal) * (1 - underside)
        c = c * (1 - band[:, None] * 0.45) + dors[None] * band[:, None] * 0.45
    elif pat == "mottled":
        blot = smoothstep(0.55, 0.7, fbm(flat, L * 0.04, 3, 11)) * (1 - underside)
        c = c * (1 - blot[:, None] * 0.35) + dors[None] * blot[:, None] * 0.35
    elif pat == "countershade":
        c = c * (1 - underside[:, None] * 0.4) + belly[None] * underside[:, None] * 0.4
    # accent colour on display structures (frill, crest, sail, head top)
    hf = J["head_frame"]; hp = np.array(hf["pos"], np.float32); hdv = np.array(hf["fwd"], np.float32); hu = np.array(hf["up"], np.float32)
    loc = flat - hp; lf = loc @ hdv; lu = loc @ hu
    headzone = smoothstep(hf["len"] * 1.4, hf["len"] * 0.6, np.linalg.norm(loc, axis=-1))
    acc_w = np.zeros(len(flat), np.float32)
    if sp["head"].get("frill"): acc_w = np.maximum(acc_w, headzone * smoothstep(hf["h"] * 0.5, hf["h"] * 1.3, lu) * smoothstep(0.3 * hf["len"], -0.2, lf))
    if sp["head"].get("crest"): acc_w = np.maximum(acc_w, smoothstep(hf["h"] * 0.35, hf["h"] * 1.0, lu) * headzone)
    if sp.get("sail"):
        acc_w = np.maximum(acc_w, smoothstep(0.3, 0.95, (flat[:, 2] - zmin) / (zmax - zmin)) * 0.9)
    acc_w *= 0.35 + 0.65 * smoothstep(0.4, 0.6, fbm(flat, L * 0.03, 3, 13))
    c = c * (1 - acc_w[:, None] * 0.6) + acc[None] * acc_w[:, None] * 0.6
    # scale cell variation + crevice darkening
    c *= (0.88 + 0.24 * cell[:, None]) * (0.72 + 0.28 * hs[:, None])
    # --- keratin: horns, beak tip, claws
    ker = (tflat == 1).astype(np.float32)
    for a, b, r in J.get("horns", []):
        dd, t = seg_dist(flat, a, b); ker = np.maximum(ker, smoothstep(r * 1.3, r * 0.9, dd) * smoothstep(0.08, 0.3, t))
    if sp["head"].get("beak"): ker = np.maximum(ker, smoothstep(hf["len"] * 0.8, hf["len"] * 0.95, lf) * headzone)
    kc = np.array([0.36, 0.31, 0.24], np.float32) * (0.8 + 0.4 * fbm(flat, 0.05, 2, 21))[:, None]
    c = c * (1 - ker[:, None]) + kc * ker[:, None]
    teeth = (tflat == 2); c[teeth] = np.array([0.78, 0.72, 0.58], np.float32)
    memb = (tflat == 4)
    if memb.any():
        vein = smoothstep(0.02, 0.0, np.abs(fbm(flat[memb], 0.25, 3, 31) - 0.5))
        c[memb] = (base * 0.8 + acc * 0.2)[None] * (0.9 + 0.2 * big[memb])[:, None] * (1 - 0.25 * vein[:, None])
    # eyes: iris with dark pupil (slit for predators)
    eye = (tflat == 3)
    if eye.any():
        pe = flat[eye]; ec = np.array(J["eyes"], np.float32); side = np.where(pe[:, 0] > 0, 0, 1); cen = ec[side]
        dirv = pe - cen; dirv /= np.linalg.norm(dirv, axis=-1, keepdims=True) + 1e-9
        outward = np.stack([np.where(side == 0, 1.0, -1.0), np.zeros(len(pe)), np.zeros(len(pe))], -1)
        ang = np.degrees(np.arccos(np.clip((dirv * outward).sum(-1), -1, 1)))
        pred = sp["head"].get("teeth", 0) > 0
        pupil_w = np.abs(dirv[:, 1]) * (2.5 if pred else 1.0)
        iris = np.array([0.62, 0.45, 0.12] if pred else [0.45, 0.33, 0.16], np.float32)
        ec_col = np.where((ang < 38)[:, None], iris[None] * (0.8 + 0.4 * (1 - ang / 38))[:, None], np.array([0.08, 0.06, 0.05], np.float32)[None])
        pupil = (ang < 16) & ((pupil_w < 0.12) if pred else True)
        ec_col[pupil] = 0.02
        c[eye] = ec_col
    c = np.clip(c, 0, 1)
    # --- height -> normal (UV space finite differences)
    height = (hs * 0.8 + 0.2 * mid) * (1 - ker * 0.7) * (~teeth) * (~eye)
    Hh = full(height.astype(np.float32))
    strength = 2.2 * col.get("bumps", 1.0)
    gx = ndimage.sobel(Hh, axis=1) / 8.0; gy = ndimage.sobel(Hh, axis=0) / 8.0
    nx = -gx * strength * (S / 1024); ny = gy * strength * (S / 1024); nz = np.ones_like(nx)
    nn = np.stack([nx, ny, nz], -1); nn /= np.linalg.norm(nn, axis=-1, keepdims=True)
    Nimg = ((nn * 0.5 + 0.5) * 255).astype(np.uint8)
    # --- mask: R metal, G AO, B height, A smoothness
    ao = np.clip(0.55 + 0.45 * hs, 0, 1) * (0.85 + 0.15 * np.clip(nflat[:, 2] * 0.5 + 0.5, 0, 1))
    smooth = np.full(len(flat), 0.28, np.float32) + 0.1 * (1 - hs)
    smooth[ker > 0.5] = 0.45; smooth[teeth] = 0.55; smooth[eye] = 0.92; smooth[memb] = 0.35
    Mimg = full(np.stack([np.zeros(len(flat)), ao, height, smooth], -1).astype(np.float32))
    name = "Dino_" + sid.capitalize()
    os.makedirs(out, exist_ok=True)
    Image.fromarray((full(c.astype(np.float32)) * 255).astype(np.uint8), "RGB").save(os.path.join(out, f"T_{name}_D.png"))
    Image.fromarray(Nimg, "RGB").save(os.path.join(out, f"T_{name}_N.png"))
    Image.fromarray((np.clip(Mimg, 0, 1) * 255).astype(np.uint8), "RGBA").save(os.path.join(out, f"T_{name}_M.png"))
    # eye + membrane materials share the atlas (same UVs)
    for extra in ("Eye", "Membrane"):
        for k in ("D", "N", "M"):
            src = os.path.join(out, f"T_{name}_{k}.png"); dst = os.path.join(out, f"T_{name}_{extra}_{k}.png")
            Image.open(src).save(dst)
    return float(M.mean())

if __name__ == "__main__":
    sid, work, out = sys.argv[1], sys.argv[2], sys.argv[3]; S = int(sys.argv[4]) if len(sys.argv) > 4 else 1024
    import time; t = time.time(); cov = build(sid, work, out, S); print(f"{sid}: {S}px coverage {cov:.2f} in {time.time() - t:.1f}s")
