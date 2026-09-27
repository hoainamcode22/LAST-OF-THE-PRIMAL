"""
PRIMAL FRONTIER - hero creature textures (original design): skin atlas with normals baked from the high-resolution
SDF (skull ridges, muscles, folds, scutes) + procedural scales per body region (pebbly flank scales, dorsal scutes,
transverse belly scutes, labial scales along the lips, scutate toes / metatarsus, rugose snout), "rift" stripe
pattern, scars, mud on the feet, wet mouth interior, SDF ambient occlusion. Plus a mouth atlas (teeth, gums, tongue)
and an eye texture.   python3 dino_tex_hero.py <sid> <work_dir> <out_dir> [skin_px] [mouth_px] [eye_px]
"""
import sys, os, json, math, time
import numpy as np
from PIL import Image
from scipy import ndimage
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from dino_specs import SPECS
from dino_tex import hash3, voronoi, fbm, vnoise, smoothstep
from sdfview import Grid

CH = 700_000


def raster(uv, tris_sel, S):
    """rasterise the selected triangles: triangle id and barycentrics per texel (+ nearest covered texel for dilation)"""
    TRI = np.full((S, S), -1, np.int32); BA = np.zeros((S, S, 3), np.float32)
    U = uv * (S - 1); U[..., 1] = (S - 1) - U[..., 1]
    for k in tris_sel:
        a, b, c = U[k]
        x0 = int(max(0, math.floor(min(a[0], b[0], c[0])))); x1 = int(min(S - 1, math.ceil(max(a[0], b[0], c[0]))))
        y0 = int(max(0, math.floor(min(a[1], b[1], c[1])))); y1 = int(min(S - 1, math.ceil(max(a[1], b[1], c[1]))))
        if x1 < x0 or y1 < y0: continue
        d = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
        if abs(d) < 1e-12: continue
        xs, ys = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        l1 = ((b[1] - c[1]) * (xs - c[0]) + (c[0] - b[0]) * (ys - c[1])) / d
        l2 = ((c[1] - a[1]) * (xs - c[0]) + (a[0] - c[0]) * (ys - c[1])) / d
        l3 = 1 - l1 - l2
        inside = (l1 >= -0.02) & (l2 >= -0.02) & (l3 >= -0.02)
        if not inside.any(): continue
        yy, xx = np.nonzero(inside); yy += y0; xx += x0
        TRI[yy, xx] = k; BA[yy, xx, 0] = l1[inside]; BA[yy, xx, 1] = l2[inside]; BA[yy, xx, 2] = l3[inside]
    M = TRI >= 0
    idx = ndimage.distance_transform_edt(~M, return_distances=False, return_indices=True)
    return TRI, BA, M, idx


def tangents(co, tris, uv):
    p0, p1, p2 = co[tris[:, 0]], co[tris[:, 1]], co[tris[:, 2]]
    t0, t1, t2 = uv[:, 0], uv[:, 1], uv[:, 2]
    e1 = p1 - p0; e2 = p2 - p0; d1 = t1 - t0; d2 = t2 - t0
    r = d1[:, 0] * d2[:, 1] - d2[:, 0] * d1[:, 1]; r = np.where(np.abs(r) < 1e-12, 1e-12, r)
    T = (e1 * d2[:, 1:2] - e2 * d1[:, 1:2]) / r[:, None]
    B = (e2 * d1[:, 0:1] - e1 * d2[:, 0:1]) / r[:, None]
    fn = np.cross(e1, e2); fn /= np.linalg.norm(fn, axis=1, keepdims=True) + 1e-12
    T /= np.linalg.norm(T, axis=1, keepdims=True) + 1e-12; B /= np.linalg.norm(B, axis=1, keepdims=True) + 1e-12
    hand = np.sign(np.einsum('ij,ij->i', np.cross(fn, T), B)); hand[hand == 0] = 1
    return T, B, hand


def grad(G, P, e):
    g = np.stack([G(P + [e, 0, 0]) - G(P - [e, 0, 0]), G(P + [0, e, 0]) - G(P - [0, e, 0]), G(P + [0, 0, e]) - G(P - [0, 0, e])], 1)
    return g / (np.linalg.norm(g, axis=1, keepdims=True) + 1e-9)


def seg_dist(P, a, b):
    a = np.asarray(a, np.float32); b = np.asarray(b, np.float32); ab = b - a
    t = np.clip(((P - a) @ ab) / max(float(ab @ ab), 1e-9), 0, 1)
    return np.linalg.norm(P - (a + t[:, None] * ab), axis=-1), t


def cellular(P, scale, jitter=0.85):
    f1, f2, cid = voronoi(P, scale, jitter)
    return smoothstep(0.0, 0.32, f2 - f1), cid                                   # 0 in crevices, 1 on scale centres


def skin_chunk(P, N, part, J, sp, hs, rng_scars):
    """colour, height (for micro normals), smoothness, extra AO for a chunk of skin texels"""
    col = hs["colors"]; hf = J["head_frame"]
    hp = np.array(hf["pos"], np.float32); hx = np.array(hf["x"], np.float32); hd = np.array(hf["fwd"], np.float32); hu = np.array(hf["up"], np.float32)
    hl, hw, hh, zl = hf["len"], hf["w"], hf["h"], hf["lip"]
    loc = P - hp; lx = loc @ hx; ly = loc @ hd; lz = loc @ hu
    n = len(P)
    head = smoothstep(-0.25, 0.05, ly) * (np.abs(lx) < hw * 1.6) * (lz > -hh * 1.3) * (lz < hh * 1.2)
    dors = np.clip(N[:, 2], -1, 1); up = smoothstep(0.2, 0.8, dors); under = smoothstep(-0.1, -0.7, dors)
    x, y, z = P[:, 0], P[:, 1], P[:, 2]
    # --- scales
    hs_small, c_small = cellular(P, 0.021)
    hs_big, c_big = cellular(P * np.array([1, 0.8, 1], np.float32), 0.055)
    hs_face, c_face = cellular(P, 0.012)
    w_big = up * (1 - head) * smoothstep(0.5, 1.5, z)
    # transverse belly scutes: bands across the underside of neck / chest / belly / tail
    belly_band = 1 - np.abs(np.sin(y * math.pi / 0.075 + 0.3 * np.sin(x * 9)))
    belly_band = smoothstep(0.05, 0.35, belly_band) * (1 - smoothstep(0.0, 0.03, 0.03 - np.abs(x)) * 0.6)
    w_belly = under * (1 - head) * smoothstep(1.0, 2.2, z)
    # labial scales along the lip line (rectangular cells)
    lipd = np.abs(lz - zl)
    w_lip = head * smoothstep(0.08, 0.02, lipd) * (ly > hl * 0.2)
    lab = smoothstep(0.1, 0.4, 1 - np.abs(np.sin(ly * math.pi / 0.036))) * smoothstep(0.0, 0.012, lipd)
    # scutate metatarsus / toes: transverse bands on the front of the lower legs and on top of the toes
    w_leg = smoothstep(0.75, 0.45, z) * (1 - head)
    scute_leg = smoothstep(0.1, 0.45, 1 - np.abs(np.sin((z * 1.0 - y * 0.5) * math.pi / 0.042)))
    h = hs_small * (1 - w_big) + hs_big * w_big
    h = h * (1 - w_belly) + belly_band * w_belly
    h = h * (1 - head * 0.6) + hs_face * head * 0.6
    h = h * (1 - w_lip * 0.6) + lab * w_lip * 0.6
    h = h * (1 - w_leg * 0.6) + scute_leg * w_leg * 0.6
    cell = c_small * (1 - w_big) + c_big * w_big
    # rugose snout / brow
    rug = head * smoothstep(0.35, 0.9, (lz - zl) / (hh * 1.1)) * smoothstep(hl * 0.3, hl * 0.5, ly)
    h = h * (1 - rug * 0.5) + fbm(P, 0.03, 3, 7) * rug * 0.5
    # --- colour
    C = lambda k: np.array(col[k], np.float32)
    big = fbm(P, 1.2, 4, 1); mid = fbm(P, 0.3, 3, 5); fine = fbm(P, 0.06, 2, 9)
    c = C("base")[None] * (0.82 + 0.36 * big[:, None])
    c = c * (1 - under[:, None]) + C("belly")[None] * under[:, None] * (0.92 + 0.16 * mid[:, None])
    top = smoothstep(0.45, 0.95, dors)
    c = c * (1 - top[:, None] * 0.6) + C("dorsal")[None] * top[:, None] * 0.6
    # "rift" stripes: fractured diagonal bands over back and flanks
    warp = fbm(P, 0.5, 4, 17) * 4.0
    s = np.sin(y * 3.4 + z * 1.6 + warp)
    edge_n = fbm(P, 0.08, 3, 21)
    band = smoothstep(0.55, 0.75, s + (edge_n - 0.5) * 0.5) * smoothstep(-0.3, 0.35, dors) * (1 - head * 0.9) * smoothstep(0.8, 2.0, z)
    c = c * (1 - band[:, None] * 0.62) + C("dorsal")[None] * band[:, None] * 0.62 * 0.8
    rim = smoothstep(0.45, 0.55, s + (edge_n - 0.5) * 0.5) * (1 - band) * smoothstep(-0.3, 0.35, dors) * smoothstep(0.8, 2.0, z) * (1 - head * 0.9)
    c = c * (1 - rim[:, None] * 0.25) + C("accent")[None] * rim[:, None] * 0.25
    # throat and lower jaw pale, face darker, brow crest accent
    throat = head * smoothstep(zl - 0.02, zl - hh * 0.5, lz) * under
    neckthroat = smoothstep(-0.2, -0.75, dors) * smoothstep(3.2, 4.2, z) * (y < -1.2)
    thr = np.maximum(throat, neckthroat * 0.8)
    c = c * (1 - thr[:, None] * 0.7) + C("throat")[None] * thr[:, None] * 0.7
    face = head * smoothstep(zl, zl + hh * 0.5, lz)
    c = c * (1 - face[:, None] * 0.25)
    crest = np.zeros(n, np.float32)
    for row in J.get("crest_rows", []):
        for a_, b_ in zip(row[:-1], row[1:]):
            d_, _ = seg_dist(P, a_, b_); crest = np.maximum(crest, smoothstep(0.1, 0.03, d_))
    crest *= smoothstep(0.0, 0.3, dors)
    c = c * (1 - crest[:, None] * 0.7) + C("accent")[None] * crest[:, None] * 0.7
    # scars on face and neck (pale thin healed lines)
    scar = np.zeros(n, np.float32)
    for a_, b_, w_ in rng_scars:
        d_, t_ = seg_dist(P, a_, b_); scar = np.maximum(scar, smoothstep(w_, w_ * 0.3, d_) * smoothstep(0.0, 0.15, t_) * smoothstep(1.0, 0.85, t_))
    c = c * (1 - scar[:, None] * 0.55) + np.array([0.62, 0.48, 0.42], np.float32)[None] * scar[:, None] * 0.55
    # mud on the feet
    mud = smoothstep(0.55, 0.12, z + (fbm(P, 0.15, 3, 31) - 0.5) * 0.25)
    c = c * (1 - mud[:, None] * 0.55) + np.array([0.24, 0.19, 0.14], np.float32)[None] * mud[:, None] * 0.55
    # mouth interior (walls of the cavity carved in the skull)
    mouth = np.zeros(n, np.float32)
    for cen, rx, ry, rz in J.get("mouth_cavity", []):
        q = P - np.array(cen, np.float32); qx = q @ hx; qy = q @ hd; qz = q @ hu
        e = np.sqrt((qx / (rx * 1.35)) ** 2 + (qy / (ry * 1.6)) ** 2 + (qz / (rz * 1.5 + 0.012)) ** 2)
        mouth = np.maximum(mouth, smoothstep(1.05, 0.8, e))
    inner = mouth * (np.abs(lx) < np.interp(ly, [0, hl], [hw * 0.9, hw * 0.3]))
    mc = C("mouth")[None] * (0.8 + 0.3 * mid[:, None])
    gumc = C("gum")[None] * (0.85 + 0.2 * fine[:, None])
    near_lip = smoothstep(0.035, 0.0, lipd)[:, None]
    mcol = mc * (1 - near_lip) + gumc * near_lip
    c = c * (1 - inner[:, None]) + mcol * inner[:, None]
    # per-scale variation and crevice darkening
    c *= (0.9 + 0.2 * cell[:, None]) * (0.7 + 0.3 * h[:, None] * (1 - inner[:, None]) + 0.3 * inner[:, None])
    # claws (part 1): dark keratin, lighter worn tips
    ker = (part == 1)
    if ker.any():
        kc = C("claw")[None] * (0.8 + 0.5 * fbm(P[ker], 0.02, 2, 41)[:, None])
        c[ker] = kc; h[ker] = 0.5 + 0.1 * fbm(P[ker], 0.01, 2, 43)
    c = np.clip(c, 0, 1)
    smooth = 0.22 + 0.16 * h
    smooth = smooth * (1 - inner) + 0.72 * inner
    smooth = smooth * (1 - mud) + 0.1 * mud
    smooth[ker] = 0.5
    hgt = h * (1 - inner * 0.8) * (1 - scar * 0.6)
    return c, hgt, smooth, inner


def mouth_chunk(P, N, part, J, hs):
    col = hs["colors"]; hf = J["head_frame"]
    hp = np.array(hf["pos"], np.float32); hu = np.array(hf["up"], np.float32); zl = hf["lip"]
    lz = (P - hp) @ hu; d = np.abs(lz - zl)
    n = len(P); c = np.zeros((n, 3), np.float32); h = np.zeros(n, np.float32); sm = np.zeros(n, np.float32)
    C = lambda k: np.array(col[k], np.float32)
    t = (part == 2)
    if t.any():
        tip = smoothstep(0.01, 0.1, d[t])
        streak = fbm(P[t] * np.array([4, 4, 1], np.float32), 0.02, 2, 51)
        c[t] = C("tooth_root")[None] * (1 - tip[:, None]) + C("tooth")[None] * tip[:, None]
        c[t] *= (0.9 + 0.12 * streak[:, None]); h[t] = 0.5 + 0.2 * streak; sm[t] = 0.62
    g = (part == 6)
    if g.any():
        f = fbm(P[g], 0.012, 3, 53); c[g] = C("gum")[None] * (0.8 + 0.35 * f[:, None]); h[g] = f; sm[g] = 0.75
    tg = (part == 5)
    if tg.any():
        pap, cid = cellular(P[tg], 0.006, 0.9)
        back = smoothstep(0.0, 0.25, fbm(P[tg], 0.1, 2, 55))
        c[tg] = C("tongue")[None] * (0.8 + 0.25 * pap[:, None]) * (0.85 + 0.2 * back[:, None]); h[tg] = pap; sm[tg] = 0.7
    return np.clip(c, 0, 1), h, sm


def normal_from_height(Hh, strength, S):
    gx = ndimage.sobel(Hh, axis=1) / 8.0; gy = ndimage.sobel(Hh, axis=0) / 8.0
    nx = -gx * strength * (S / 1024); ny = gy * strength * (S / 1024); nz = np.ones_like(nx)
    nn = np.stack([nx, ny, nz], -1); return nn / np.linalg.norm(nn, axis=-1, keepdims=True)


def blend_normals(a, b):
    """whiteout blend of two tangent-space normals (unit vectors, z up)"""
    r = np.stack([a[..., 0] + b[..., 0], a[..., 1] + b[..., 1], a[..., 2] * b[..., 2]], -1)
    return r / (np.linalg.norm(r, axis=-1, keepdims=True) + 1e-9)


def save(out, name, D, Nimg, Mimg):
    Image.fromarray((np.clip(D, 0, 1) * 255).astype(np.uint8), "RGB").save(os.path.join(out, f"T_{name}_D.png"))
    Image.fromarray(((Nimg * 0.5 + 0.5) * 255).astype(np.uint8), "RGB").save(os.path.join(out, f"T_{name}_N.png"))
    Image.fromarray((np.clip(Mimg, 0, 1) * 255).astype(np.uint8), "RGBA").save(os.path.join(out, f"T_{name}_M.png"))


def build(sid, work, out, S_skin=4096, S_mouth=2048, S_eye=1024):
    sp = SPECS[sid]; hs = sp["hero"]; J = json.load(open(os.path.join(work, f"{sid}_joints.json")))
    d = np.load(os.path.join(work, f"{sid}_texdata.npz"))
    co, nr, tris, uv, part, mat = d["co"], d["nr"], d["tris"], d["uv"], d["part"], d["mat"]
    nr = nr / (np.linalg.norm(nr, axis=1, keepdims=True) + 1e-9)
    G = Grid(os.path.join(work, f"{sid}_sdf.npz"))
    T, B, hand = tangents(co, tris, uv)
    tpart = np.where(part[tris[:, 0]] == part[tris[:, 1]], part[tris[:, 0]], np.max(part[tris], axis=1))
    name = "Dino_" + sid.capitalize(); os.makedirs(out, exist_ok=True)
    rng = np.random.default_rng(11)
    hf = J["head_frame"]; hp = np.array(hf["pos"]); hx = np.array(hf["x"]); hd = np.array(hf["fwd"]); hu = np.array(hf["up"])
    scars = []
    for i in range(7):
        s = rng.choice([-1, 1]); a = hp + hd * rng.uniform(0.2, 1.3) + hu * rng.uniform(-0.1, 0.4) + hx * s * 0.45
        b = a + (hd * rng.uniform(-1, 1) + hu * rng.uniform(-0.6, 0.6)) * rng.uniform(0.15, 0.35)
        scars.append((a, b, rng.uniform(0.006, 0.012)))
    for i in range(5):
        s = rng.choice([-1, 1]); a = np.array([s * 0.55, rng.uniform(-2.6, -1.4), rng.uniform(3.6, 4.4)])
        b = a + np.array([0, rng.uniform(-0.4, 0.4), rng.uniform(-0.35, 0.35)]); scars.append((a, b, rng.uniform(0.008, 0.015)))
    t0 = time.time()
    # ================================================================ skin atlas
    S = S_skin; TRI, BA, M, idx = raster(uv, np.where(mat == 0)[0], S); print(f"skin raster {time.time() - t0:.0f}s", flush=True)
    ys, xs = np.nonzero(M); nt = len(ys)
    colI = np.zeros((S, S, 3), np.float32); hI = np.zeros((S, S), np.float32); smI = np.zeros((S, S), np.float32); aoI = np.ones((S, S), np.float32)
    bakeI = np.zeros((S, S, 3), np.float32); bakeI[..., 2] = 1
    for c0 in range(0, nt, CH):
        yy = ys[c0:c0 + CH]; xx = xs[c0:c0 + CH]; k = TRI[yy, xx]; ba = BA[yy, xx]
        tv = tris[k]
        P = co[tv[:, 0]] * ba[:, 0:1] + co[tv[:, 1]] * ba[:, 1:2] + co[tv[:, 2]] * ba[:, 2:3]
        Nl = nr[tv[:, 0]] * ba[:, 0:1] + nr[tv[:, 1]] * ba[:, 1:2] + nr[tv[:, 2]] * ba[:, 2:3]
        Nl /= np.linalg.norm(Nl, axis=1, keepdims=True) + 1e-9
        pt = tpart[k]
        # --- bake: SDF normal at the projected surface point
        dd = G(P); Pp = P - Nl * dd[:, None] * 0.9
        Ns = grad(G, Pp, G.h * 0.9)
        use = (pt == 0) & (np.abs(dd) < 0.06) & ((Ns * Nl).sum(1) > 0.2)
        Ns = np.where(use[:, None], Ns, Nl)
        Tt = T[k] - Nl * (T[k] * Nl).sum(1, keepdims=True); Tt /= np.linalg.norm(Tt, axis=1, keepdims=True) + 1e-9
        Bt = np.cross(Nl, Tt) * hand[k][:, None]
        nts = np.stack([(Ns * Tt).sum(1), (Ns * Bt).sum(1), (Ns * Nl).sum(1)], 1); nts /= np.linalg.norm(nts, axis=1, keepdims=True) + 1e-9
        bakeI[yy, xx] = nts
        # --- AO from the SDF
        ao = np.ones(len(P), np.float32)
        for s_, w_ in ((0.03, 0.35), (0.08, 0.3), (0.18, 0.2), (0.35, 0.15)):
            ao -= w_ * np.clip((s_ - G(Pp + Ns * s_)) / s_, 0, 1)
        ao = np.where(use, np.clip(ao, 0.25, 1), 0.85)
        c, hgt, sm, inner = skin_chunk(P, Ns, pt, J, sp, hs, scars)
        ao = ao * (1 - inner * 0.4)
        colI[yy, xx] = c * (0.55 + 0.45 * ao[:, None]); hI[yy, xx] = hgt; smI[yy, xx] = sm; aoI[yy, xx] = ao
        print(f"  skin {min(c0 + CH, nt)}/{nt} {time.time() - t0:.0f}s", flush=True)
    dil = lambda a: a[idx[0], idx[1]]
    colI, hI, smI, aoI, bakeI = dil(colI), dil(hI), dil(smI), dil(aoI), dil(bakeI)
    micro = normal_from_height(hI, 1.6, S)
    Nimg = blend_normals(bakeI, micro)
    Mimg = np.stack([np.zeros_like(hI), aoI, hI, smI], -1)
    save(out, name, colI, Nimg, Mimg); print(f"skin done {time.time() - t0:.0f}s", flush=True)
    del colI, hI, smI, aoI, bakeI, micro, Nimg, Mimg, TRI, BA
    # ================================================================ mouth atlas
    S = S_mouth; TRI, BA, M, idx = raster(uv, np.where(mat == 2)[0], S)
    ys, xs = np.nonzero(M)
    k = TRI[ys, xs]; ba = BA[ys, xs]; tv = tris[k]
    P = co[tv[:, 0]] * ba[:, 0:1] + co[tv[:, 1]] * ba[:, 1:2] + co[tv[:, 2]] * ba[:, 2:3]
    Nl = nr[tv[:, 0]] * ba[:, 0:1] + nr[tv[:, 1]] * ba[:, 1:2] + nr[tv[:, 2]] * ba[:, 2:3]
    c, hgt, sm = mouth_chunk(P, Nl, tpart[k], J, hs)
    colI = np.zeros((S, S, 3), np.float32); hI = np.zeros((S, S), np.float32); smI = np.zeros((S, S), np.float32)
    colI[ys, xs] = c; hI[ys, xs] = hgt; smI[ys, xs] = sm
    colI, hI, smI = colI[idx[0], idx[1]], hI[idx[0], idx[1]], smI[idx[0], idx[1]]
    save(out, name + "_Mouth", colI, normal_from_height(hI, 1.0, S), np.stack([np.zeros_like(hI), np.ones_like(hI), hI, smI], -1))
    print(f"mouth done {time.time() - t0:.0f}s", flush=True)
    # ================================================================ eye (UV = projection along the eye axis)
    S = S_eye; yv, xv = np.mgrid[0:S, 0:S]; u = (xv + 0.5) / S; v = 1 - (yv + 0.5) / S
    du = (u - 0.5) * 2; dv = (v - 0.5) * 2; r = np.sqrt(du ** 2 + dv ** 2); ang = np.arctan2(dv, du)
    iris_c = np.array(hs["colors"]["iris"], np.float32)
    fib = 0.75 + 0.35 * np.abs(np.sin(ang * 23 + 3 * np.sin(ang * 5))) * smoothstep(0.1, 0.6, r)
    iris = iris_c[None, None] * fib[..., None] * (1.15 - 0.55 * smoothstep(0.1, 0.62, r))[..., None]
    ring = smoothstep(0.52, 0.64, r); iris = iris * (1 - ring[..., None] * 0.85)
    sclera = np.array([0.16, 0.12, 0.08], np.float32)[None, None] * np.ones((S, S, 1), np.float32)
    E = np.where((r < 0.64)[..., None], iris, sclera)
    pupil = (np.abs(du) < 0.085 * np.sqrt(np.clip(1 - (dv / 0.5) ** 2, 0, 1))) & (np.abs(dv) < 0.5)
    E[pupil] = 0.015
    Nimg = np.zeros((S, S, 3), np.float32); Nimg[..., 2] = 1
    Mimg = np.stack([np.zeros((S, S)), np.ones((S, S)), np.zeros((S, S)), np.full((S, S), 0.93)], -1)
    save(out, name + "_Eye", E, Nimg, Mimg)
    print(f"all done {time.time() - t0:.0f}s", flush=True)


if __name__ == "__main__":
    sid, work, out = sys.argv[1], sys.argv[2], sys.argv[3]
    a = [int(x) for x in sys.argv[4:7]] + [4096, 2048, 1024][len(sys.argv[4:7]):]
    build(sid, work, out, *a)
