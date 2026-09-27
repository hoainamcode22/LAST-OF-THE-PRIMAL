"""Paint the survivor skin in texture space from a baked object-position map (numpy, tiled).
usage: python3 skin_paint.py <workdir> <texdir>
inputs : pos_4k.npy (x*.5+.5, y*.5+.5, z*.5), nrm_4k.png (baked from the muscle high poly), ao_2k.png
outputs: T_Player_Skin_D.png (4096 sRGB), T_Player_Skin_N.png (4096), T_Player_Skin_M.png (2048: R metal, G AO, B cavity, A smoothness)"""
import sys, os, math, numpy as np
from PIL import Image
from scipy import ndimage

def hash3(ix, iy, iz, seed):
    h = (ix * 73856093) ^ (iy * 19349663) ^ (iz * 83492791) ^ (seed * 2654435761)
    h = h & 0xffffffff
    h = (h ^ (h >> 13)) * 1274126177 & 0xffffffff
    h = h ^ (h >> 16)
    return (h & 0xffffff).astype(np.float32) / float(0xffffff) * 2 - 1

def vnoise(P, seed=0):
    Pf = np.floor(P); f = (P - Pf).astype(np.float32); I = Pf.astype(np.int64)
    f = f * f * (3 - 2 * f)
    out = 0
    for dx in (0, 1):
        wx = f[:, 0] if dx else 1 - f[:, 0]
        for dy in (0, 1):
            wy = f[:, 1] if dy else 1 - f[:, 1]
            for dz in (0, 1):
                wz = f[:, 2] if dz else 1 - f[:, 2]
                out = out + wx * wy * wz * hash3(I[:, 0] + dx, I[:, 1] + dy, I[:, 2] + dz, seed)
    return out

def fbm(P, freq, oct=4, seed=0, gain=0.5, aniso=(1, 1, 1)):
    s = 0; a = 1; n = 0
    A = np.array(aniso, np.float32)
    for o in range(oct):
        s = s + a * vnoise(P * A * (freq * 2 ** o), seed + o * 17); n += a; a *= gain
    return s / n

def ss(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1); return t * t * (3 - 2 * t)

def mix(c, col, w):
    w = np.clip(w, 0, 1)[:, None]; return c * (1 - w) + np.asarray(col, np.float32) * w

# ---------------------------------------------------------------- landmarks (metres, Blender space)
EYE = np.array([0.0307, -0.1315, 1.7262]); NOSE = np.array([0.0, -0.1755, 1.683])
MOUTH_Z = 1.651; BROW_Z = 1.7425

def seg_dist(p, a, b):
    a = np.asarray(a, np.float32); b = np.asarray(b, np.float32); ab = b - a
    t = np.clip(((p - a) @ ab) / (ab @ ab), 0, 1)
    return np.linalg.norm(p - (a + t[:, None] * ab), axis=1), t

SCARS = [  # claw marks on the left pectoral / shoulder, one across the right forearm, one through the left brow
    ((0.075, -0.155, 1.405), (0.175, -0.110, 1.315), 0.0042),
    ((0.060, -0.160, 1.380), (0.160, -0.118, 1.290), 0.0045),
    ((0.050, -0.158, 1.352), (0.140, -0.122, 1.268), 0.0038),
    ((-0.36, -0.075, 1.225), (-0.41, -0.060, 1.175), 0.0030),
    ((0.040, -0.150, 1.762), (0.047, -0.150, 1.735), 0.0016),
]

def paint_tile(p, cav, ao):
    """p (N,3) positions, cav (N,) cavity (-1 concave .. +1 convex), ao (N,) -> albedo (N,3), smooth (N,), height (N,)"""
    x, y, z = p[:, 0], p[:, 1], p[:, 2]; ax = np.abs(x)
    n_lo = fbm(p, 3.0, 3, 1); n_mid = fbm(p, 25.0, 3, 2); n_hi = vnoise(p * 900.0, 3); n_pore = vnoise(p * 2600.0, 4)
    c = np.tile(np.array([[0.63, 0.44, 0.33]], np.float32), (len(p), 1))
    height = np.zeros(len(p), np.float32)
    # hue / value variation (redder vs yellower patches) and fine mottling
    c = c * (1 + 0.06 * n_lo[:, None]) + np.array([0.025, -0.004, -0.018], np.float32) * n_mid[:, None]
    c = c * (1 + 0.025 * n_hi[:, None]) * (1 - 0.035 * np.clip(n_pore, 0, 1)[:, None])
    # sun tan: shoulders, upper back, face, forearms darker/warmer; armpits / inner arms lighter
    up = ss(1.25, 1.50, z) * ss(-0.3, 0.2, -y * 0 + 1)
    fore = ss(0.33, 0.45, ax) * ss(0.95, 1.2, z)
    tan = np.clip(0.55 * up + 0.5 * fore + 0.25 * ss(1.55, 1.7, z), 0, 1)
    c = c * (1 - 0.14 * tan[:, None]) + np.array([0.035, 0.008, -0.012], np.float32) * tan[:, None]
    head = z > 1.55
    # redness: nose, cheeks, ears, knuckles, elbows, knees
    red = np.zeros(len(p), np.float32)
    for q, r, k in (((0, -0.17, 1.683), 0.022, 0.8), ((0.05, -0.135, 1.69), 0.028, 0.45), ((-0.05, -0.135, 1.69), 0.028, 0.45),
                    ((0.083, -0.02, 1.72), 0.035, 0.6), ((-0.083, -0.02, 1.72), 0.035, 0.6),
                    ((0.155, -0.07, 0.55), 0.06, 0.4), ((-0.155, -0.07, 0.55), 0.06, 0.4),
                    ((0.41, 0.03, 1.27), 0.05, 0.3), ((-0.41, 0.03, 1.27), 0.05, 0.3)):
        red = np.maximum(red, k * np.clip(1 - np.linalg.norm(p - np.array(q), axis=1) / r, 0, 1))
    red = np.maximum(red, 0.35 * ss(0.5, 0.56, ax) * ss(1.2, 1.05, z))  # hands
    c = mix(c, (0.62, 0.30, 0.25), 0.35 * red)
    # eye sockets: slightly darker, lids warmer
    for sx in (EYE[0], -EYE[0]):
        d = np.linalg.norm(p - np.array([sx, EYE[1] + 0.004, EYE[2]]), axis=1)
        c = c * (1 - 0.16 * ss(0.03, 0.012, d))[:, None]
        c = mix(c, (0.55, 0.32, 0.27), 0.25 * ss(0.022, 0.016, d) * (z > EYE[2] + 0.004))
    # eye area: lash line, caruncle, lid crease, under-eye, crow's feet, brow / forehead / nasolabial lines
    R_EYE = 0.0152
    for sx in (1.0, -1.0):
        ec = np.array([sx * EYE[0], EYE[1], EYE[2]])
        de = np.linalg.norm(p - ec, axis=1)
        front = ss(ec[1] - 0.002, ec[1] - 0.008, y)
        lashline = ss(0.0022, 0.0006, np.abs(de - R_EYE - 0.0006)) * front
        c = mix(c, (0.10, 0.06, 0.045), 0.55 * lashline)
        car = ss(0.0032, 0.001, np.linalg.norm(p - np.array([sx * 0.0172, -0.1385, 1.7245]), axis=1))
        c = mix(c, (0.66, 0.34, 0.31), 0.6 * car)
        lx = np.abs(x - ec[0])
        crease = ss(0.0035, 0.0, np.abs(z - (ec[2] + 0.0115 + 0.004 * (lx / 0.015) ** 2))) * ss(0.017, 0.012, lx) * front
        c = c * (1 - 0.12 * crease)[:, None]; height -= crease * 0.35
        under = ss(0.004, 0.0, np.abs(z - (ec[2] - 0.0125 + 0.003 * (lx / 0.015) ** 2))) * ss(0.018, 0.01, lx) * front
        c = mix(c, (0.40, 0.26, 0.24), 0.18 * under); height -= under * 0.25
        oc = np.array([sx * 0.049, -0.118, 1.727])
        for a in (-24.0, -4.0, 16.0):
            dirv = np.array([sx * math.cos(math.radians(a)), 0.35, math.sin(math.radians(a))]); dirv /= np.linalg.norm(dirv)
            dd, tt = seg_dist(p, oc, oc + dirv * 0.011)
            crow = ss(0.0009, 0.0, dd) * np.clip(np.sin(np.pi * tt), 0, 1)
            height -= crow * 0.6; c = c * (1 - 0.08 * crow)[:, None]
        nl, tn = seg_dist(p, np.array([sx * 0.021, -0.157, 1.674]), np.array([sx * 0.034, -0.148, 1.638]))
        naso = ss(0.0016, 0.0, nl) * np.clip(np.sin(np.pi * tn), 0, 1) ** 0.5
        height -= naso * 0.7; c = c * (1 - 0.1 * naso)[:, None]
        gl, tg = seg_dist(p, np.array([sx * 0.0055, -0.158, 1.744]), np.array([sx * 0.007, -0.157, 1.758]))
        glab = ss(0.0008, 0.0, gl) * np.clip(np.sin(np.pi * tg), 0, 1)
        height -= glab * 0.5
    for zf, amp in ((1.771, 0.28), (1.784, 0.24), (1.796, 0.18)):
        wave = zf + 0.0018 * np.sin(x * 140) - 0.004 * (x / 0.045) ** 2
        fl = ss(0.0009, 0.0, np.abs(z - wave)) * ss(0.048, 0.03, ax) * (y < -0.12)
        height -= fl * amp; c = c * (1 - 0.05 * fl)[:, None]
    # lips
    lipd = np.sqrt((x / 0.0245) ** 2 + ((z - MOUTH_Z) / np.where(z > MOUTH_Z, 0.0115, 0.0135)) ** 2)
    lip = ss(1.0, 0.8, lipd) * (y < -0.14)
    c = mix(c, (0.55, 0.33, 0.28), 0.42 * lip)
    c = c * (1 - 0.45 * (lip * ss(0.0022, 0.0, np.abs(z - MOUTH_Z)) * (ax < 0.024)))[:, None]
    smooth_extra = 0.12 * lip
    height -= lip * np.clip(np.sin(x * 2600) * 2 - 1.2, 0, 1) * 0.25
    # scalp hair base (under the hair cards) and hairline
    hl_front = 1.805 - 0.35 * np.maximum(0, ax - 0.035) ** 1.2
    w_back = ss(-0.05, 0.05, y)
    hl = hl_front * (1 - w_back) + (1.60 + 0.3 * np.maximum(0, ax - 0.03)) * w_back
    hl = np.where((ax > 0.058) & (y > -0.08) & (y < 0.035), np.maximum(hl, 1.748 + 0.35 * np.maximum(0, -y - 0.03)), hl)
    pr = np.stack([p[:, 0] * 0.8 + p[:, 1] * 0.6, p[:, 1] * 0.8 - p[:, 0] * 0.6, p[:, 2]], 1)
    strand = 0.6 * vnoise(pr * np.array([1400, 1400, 120], np.float32), 21) + 0.4 * vnoise(pr * np.array([2600, 2600, 200], np.float32) + 7.1, 22)
    hairm = ss(-0.009, 0.004, z - hl + 0.005 * strand) * head
    c = mix(c, (0.09, 0.065, 0.045), hairm * (0.8 + 0.2 * strand))
    # eyebrows (thick, slightly bushy): soft density + angled strokes
    u = ax
    bz = BROW_Z + 0.004 * (1 - ((u - 0.032) / 0.026) ** 2) - 0.005 * np.maximum(0, (u - 0.046) / 0.016)
    th = 0.0085 - 0.0045 * np.clip((u - 0.014) / 0.05, 0, 1)
    ang = 0.9 - 0.75 * np.clip((u - 0.012) / 0.035, 0, 1)            # inner hairs point up, outer ones sideways
    su = u * np.cos(ang) + (z - bz) * np.sin(ang); sv = -u * np.sin(ang) + (z - bz) * np.cos(ang)
    strokes = vnoise(np.stack([su * 2600, sv * 380, u * 0 + 3.7], 1).astype(np.float32), 31)
    strokes2 = vnoise(np.stack([su * 1500 + 11, sv * 200, u * 0 + 1.3], 1).astype(np.float32), 32)
    edge = np.abs(z - bz) / th + 0.25 * vnoise(p * 600.0, 33)
    brow = ss(1.05, 0.45, edge) * ss(0.008, 0.013, u) * ss(0.064, 0.055, u) * (y < -0.1)
    dens = np.clip(0.45 + 0.45 * strokes + 0.25 * strokes2, 0, 1)
    c = mix(c, (0.16, 0.11, 0.075), 0.55 * brow)
    c = mix(c, (0.06, 0.042, 0.03), 0.75 * brow * dens)
    # beard: short, full, trimmed; stubble dots + shadow, soft edges on cheeks / neck
    top = np.interp(ax, [0, 0.022, 0.03, 0.055, 0.07, 0.085, 0.1], [1.668, 1.668, 1.672, 1.688, 1.72, 1.745, 1.75])
    bw = ss(-0.003, 0.016, top - z - 0.004 * n_mid) * head
    bw = bw * ss(-0.052, -0.068, y + 0.004 * n_mid)                      # stops at the jaw angle / sideburns, clear of the ears
    zb = np.where(y < -0.115, 1.563, 1.563 + (y + 0.115) * 0.85)         # neck line: under the chin, rising to the jaw angle
    bw = bw * ss(zb - 0.004, zb + 0.006, z + 0.003 * n_mid)
    bw = bw * (1 - lip) * (1 - ss(0.015, 0.0, np.linalg.norm((p - NOSE) * np.array([1, 1, 1.4]), axis=1) - 0.012))
    dots = np.clip(vnoise(p * 3200.0, 41) * 1.6 + 0.2, 0, 1)
    bstrand = vnoise(p * np.array([900, 900, 160], np.float32), 43)
    edge_sparse = ss(0.0, 0.6, bw)
    beard = bw * np.clip(0.42 + 0.4 * dots + 0.25 * bstrand, 0, 1) * (0.5 + 0.5 * edge_sparse)
    c = mix(c, (0.085, 0.062, 0.044), 0.84 * beard)
    # scars (lighter, pinkish, slightly shiny), height used for the normal map
    for a, b, w in SCARS:
        d, t = seg_dist(p, a, b)
        taper = np.clip(np.sin(np.pi * t), 0, 1) ** 0.6
        s = ss(w * taper + 1e-4, 0.0, d)
        c = mix(c, (0.72, 0.50, 0.44), 0.7 * s)
        height -= s * 0.8
        smooth_extra = smooth_extra + 0.15 * s
    # dirt: feet / lower legs, knees, hands, mud splotches, face smudges; grime in cavities
    dirt = 0.6 * ss(0.55, 0.08, z) + 0.4 * ss(0.03, 0.0, z) + 0.35 * ss(0.5, 0.56, ax) * ss(1.2, 1.0, z)
    dirt += 0.5 * np.clip(fbm(p, 5.0, 3, 51) * 2.2 - 0.35, 0, 1)
    dirt += 0.25 * np.clip(fbm(p, 12.0, 2, 52) * 2 - 0.6, 0, 1) * head
    dirt = np.clip(dirt * (0.65 + 0.35 * (vnoise(p * 60.0, 53) * 0.5 + 0.5)), 0, 1)
    c = mix(c, (0.20, 0.145, 0.10), 0.55 * dirt)
    # cavity / AO darkening
    c = c * (1 - 0.22 * np.clip(-cav, 0, 1))[:, None] * (0.72 + 0.28 * ao)[:, None]
    c = c * (1 + 0.05 * np.clip(cav, 0, 1))[:, None]
    smooth = 0.36 + 0.1 * ss(1.72, 1.8, z) * (y < -0.1) + 0.12 * ss(0.03, 0.0, np.linalg.norm(p - NOSE, axis=1))
    smooth = smooth + smooth_extra - 0.2 * dirt - 0.12 * beard - 0.2 * hairm - 0.05 * np.clip(-cav, 0, 1)
    return np.clip(c, 0, 1), np.clip(smooth, 0.05, 0.9), height

def _prep(work):
    """normal map cleanup + cavity (cached)"""
    cp = os.path.join(work, '_nrm_cav.npz')
    if os.path.exists(cp):
        z = np.load(cp); return z['n'], z['cav']
    N = np.asarray(Image.open(os.path.join(work, 'nrm_4k.png')).convert('RGB'), np.float32) / 255.0
    n = N * 2 - 1; del N
    bad = n[..., 2] < 0.45                                     # ray misses between fingers / toes -> flat
    bad = ndimage.binary_dilation(bad, iterations=3)
    n[bad] = np.array([0, 0, 1], np.float32)
    for ch in range(3):
        n[..., ch] = np.where(bad, ndimage.gaussian_filter(n[..., ch], 1.2), n[..., ch])
    cav = -(np.gradient(n[..., 0], axis=1) - np.gradient(n[..., 1], axis=0))
    cav = ndimage.gaussian_filter(cav, 2.0)
    cav = np.clip(cav / (np.percentile(np.abs(cav), 99.5) + 1e-6), -1, 1)
    np.savez(cp, n=n.astype(np.float16), cav=cav.astype(np.float16))
    return n, cav

def tiles(work, r0s, r1s, head_only=False):
    P = np.load(os.path.join(work, 'pos_4k.npy'), mmap_mode='r')
    H, W, _ = P.shape
    n, cav = _prep(work)
    AO = np.asarray(Image.open(os.path.join(work, 'ao_2k.png')).convert('L').resize((W, H), Image.BILINEAR), np.float32) / 255.0
    T = 256
    for r0 in range(int(r0s), int(r1s), T):
        out = os.path.join(work, f'_tile_{r0:04d}.npz')
        if os.path.exists(out) and not head_only:
            try:
                np.load(out)['a']; continue
            except Exception: pass
        if head_only and os.path.exists(out + '.' + str(head_only)): continue
        pt = np.asarray(P[r0:r0 + T]).reshape(-1, 3).astype(np.float32)
        pos = np.stack([pt[:, 0] * 2 - 1, pt[:, 1] * 2 - 1, pt[:, 2] * 2], 1)
        if head_only:
            z0 = np.load(out); sel = pos[:, 2] > 1.5
            a = z0['a'].reshape(-1, 3).astype(np.float32); sm = z0['s'].ravel().astype(np.float32)
            h = z0['h'].ravel().astype(np.float32); pores = z0['p'].ravel().astype(np.float32)
            if sel.any():
                a2, s2, h2 = paint_tile(pos[sel], cav[r0:r0 + T].ravel()[sel].astype(np.float32), AO[r0:r0 + T].ravel()[sel])
                a[sel] = a2; sm[sel] = s2; h[sel] = h2
        else:
            a, sm, h = paint_tile(pos, cav[r0:r0 + T].ravel().astype(np.float32), AO[r0:r0 + T].ravel())
            pores = vnoise(pos * 2600.0, 4) * 0.5 + vnoise(pos * 5200.0, 5) * 0.25
        tmp = out[:-4] + '_tmp.npz'
        np.savez(tmp, a=a.reshape(-1, W, 3).astype(np.float16), s=sm.reshape(-1, W).astype(np.float16),
                 h=h.reshape(-1, W).astype(np.float16), p=pores.reshape(-1, W).astype(np.float16))
        os.replace(tmp, out)
        if head_only: open(out + '.' + str(head_only), 'w').write('1')
        print('tile', r0, flush=True)

def finish(work, texdir):
    P = np.load(os.path.join(work, 'pos_4k.npy'), mmap_mode='r'); H, W, _ = P.shape
    valid = (np.asarray(P[..., 0]) > 0.001) | (np.asarray(P[..., 1]) > 0.001)
    n, cav = _prep(work); n = n.astype(np.float32)
    parts = [np.load(os.path.join(work, f'_tile_{r0:04d}.npz')) for r0 in range(0, H, 256)]
    alb = np.concatenate([z['a'] for z in parts]).astype(np.float32)
    sm = np.concatenate([z['s'] for z in parts]).astype(np.float32)
    hgt = ndimage.gaussian_filter(np.concatenate([z['h'] for z in parts]).astype(np.float32), 1.0)
    pores = np.concatenate([z['p'] for z in parts]).astype(np.float32)
    micro = hgt * 3.0 + pores * 0.35
    dx = np.gradient(micro, axis=1); dy = np.gradient(micro, axis=0)
    n[..., 0] -= dx * 1.2; n[..., 1] += dy * 1.2
    n /= np.linalg.norm(n, axis=-1, keepdims=True) + 1e-8
    alb[~valid] = np.array([0.5, 0.36, 0.28], np.float32)
    Image.fromarray((np.clip(alb, 0, 1) * 255 + 0.5).astype(np.uint8)).save(os.path.join(texdir, 'T_Player_Skin_D.png'))
    Image.fromarray(((n * 0.5 + 0.5) * 255 + 0.5).astype(np.uint8)).save(os.path.join(texdir, 'T_Player_Skin_N.png'))
    AO = np.asarray(Image.open(os.path.join(work, 'ao_2k.png')).convert('L'), np.float32)
    cav2 = np.asarray(Image.fromarray(((cav.astype(np.float32) * 0.5 + 0.5) * 255).astype(np.uint8)).resize((2048, 2048), Image.LANCZOS))
    sm2 = np.asarray(Image.fromarray((np.clip(sm, 0, 1) * 255).astype(np.uint8)).resize((2048, 2048), Image.LANCZOS))
    M = np.zeros((2048, 2048, 4), np.uint8); M[..., 1] = AO.astype(np.uint8); M[..., 2] = cav2; M[..., 3] = sm2
    Image.fromarray(M, 'RGBA').save(os.path.join(texdir, 'T_Player_Skin_M.png'))
    Image.fromarray((np.clip(alb, 0, 1) * 255).astype(np.uint8)).resize((1024, 1024), Image.LANCZOS).save(os.path.join(work, 'skin_D_preview.png'))
    print('done', flush=True)

if __name__ == '__main__':
    if sys.argv[1] == 'tiles': tiles(*sys.argv[2:5])
    elif sys.argv[1] == 'head': tiles(*sys.argv[2:5], head_only=sys.argv[5])
    elif sys.argv[1] == 'finish': finish(*sys.argv[2:4])
