"""Player v2 textures: hair/fur atlas (T_Player_Hair_D/N) and the hide/rope/bone atlas (T_Player_Cloth_D/N/M).
usage: python3 tex_player_v2.py <texdir>"""
import sys, os, math, numpy as np
from PIL import Image
from scipy import ndimage
sys.path.insert(0, os.path.dirname(__file__))
from tex_gen import fbm, grid, ramp, normal_from_height

_CACHE = os.path.join(os.path.dirname(__file__), '..', 'characters', 'work', 'mh')

def strands_cached(key, *a, **k):
    f = os.path.join(_CACHE, f'_strands_{key}.npz')
    if os.path.exists(f):
        z = np.load(f); return z['rgb'], z['a']
    rgb, al = strands(*a, **k); np.savez(f, rgb=rgb.astype(np.float16), a=al.astype(np.float16))
    return rgb, al

def strands(H, W, n, rng, len_rng, col_root, col_tip, width_rng=(0.6, 1.4), wave=(0, 10), drift=(-15, 15), y0=0, clump=None, alpha_root=0.95):
    rgb = np.zeros((H, W, 3), np.float32); acc = np.zeros((H, W), np.float32); a = np.zeros((H, W), np.float32)
    for i in range(n):
        if clump is not None and rng.random() < 0.7:
            cx = clump[rng.integers(len(clump))]; x0 = cx + rng.normal(0, W * 0.012)
        else:
            x0 = rng.random() * W
        L = int(H * rng.uniform(*len_rng)); ys = np.arange(L)
        ph = rng.random() * 6.28; A = rng.uniform(*wave); dr = rng.uniform(*drift)
        xs = x0 + np.sin(ys / H * rng.uniform(4, 9) + ph) * A + ys / H * dr
        t = ys / max(1, L - 1)
        w = rng.uniform(*width_rng) * (1 - 0.5 * t)
        shade = rng.uniform(0.6, 1.35)
        col = (np.asarray(col_root) * (1 - t[:, None] * 0.7) + np.asarray(col_tip) * (t[:, None] * 0.7)) * shade
        alpha = alpha_root * (1 - t ** 2.5)
        for dx in (-1, 0, 1, 2):
            xi = np.floor(xs).astype(int) + dx
            cov = np.clip(w - np.abs(xs - (xi + 0.5)) + 0.5, 0, 1) * alpha
            ok = (xi >= 0) & (xi < W)
            yy = ys[ok] + y0; xx = xi[ok]; cc = cov[ok]
            np.add.at(rgb, (yy, xx), col[ok] * cc[:, None]); np.add.at(acc, (yy, xx), cc)
            np.maximum.at(a, (yy, xx), cc)
    rgb = rgb / np.maximum(acc, 1e-4)[..., None]
    return rgb, np.clip(a, 0, 1)

def hair_atlas(out, S=2048, seed=401):
    rng = np.random.default_rng(seed)
    H = S; half = S // 2
    # left half: long head hair strands, root at the top row
    clumps = list(rng.random(22) * half)
    rgbL, aL = strands_cached('hairL', H, half, 4200, rng, (0.55, 1.0), (0.06, 0.042, 0.03), (0.2, 0.14, 0.09), wave=(2, 14), drift=(-30, 30), clump=clumps)
    bg = np.array([0.05, 0.035, 0.025], np.float32)
    rgbL = np.where(aL[..., None] > 0, rgbL, bg)
    # right half, top: fur strands for edge cards (short, tan/grey with dark tips), bottom: opaque fur surface
    q = S // 2
    rgbF, aF = strands_cached('furF', q, half, 5200, rng, (0.35, 0.95), (0.42, 0.34, 0.25), (0.16, 0.12, 0.09), width_rng=(0.7, 1.6), wave=(1, 5), drift=(-12, 12), clump=list(rng.random(40) * half))
    rgbF = np.where(aF[..., None] > 0, rgbF, np.array([0.3, 0.24, 0.18], np.float32))
    U, V = np.meshgrid(np.linspace(0, 1, half, endpoint=False), np.linspace(0, 1, q, endpoint=False))
    base = ramp(fbm(half, 6, 4, 411)[:q, :],
                [(0, (0.22, 0.17, 0.12)), (0.5, (0.40, 0.32, 0.23)), (1, (0.55, 0.46, 0.34))])
    tuft = np.zeros((q, half), np.float32)
    for k in range(3):   # layered short strokes, tiling in both directions
        r2, a2 = strands_cached(f'tuft{k}', q, half, 3500, rng, (0.08, 0.2), (0.5, 0.41, 0.3), (0.12, 0.09, 0.07), width_rng=(0.6, 1.2), wave=(0, 2), drift=(-6, 6))
        r2 = np.roll(r2, int(q * k / 3), axis=0); a2 = np.roll(a2, int(q * k / 3), axis=0)
        base = base * (1 - a2[..., None] * 0.8) + r2 * a2[..., None] * 0.8; tuft = np.maximum(tuft, a2)
    # right-bottom: [0.5,0.75] matted dreadlock tile (u around the lock, v along it), [0.75,1] opaque fur surface
    dw = half // 2
    U2, V2 = np.meshgrid(np.linspace(0, 1, dw, endpoint=False), np.linspace(0, 1, q, endpoint=False))
    twist = np.sin(2 * math.pi * (U2 * 3 + V2 * 26)) * 0.5 + 0.5
    twist2 = np.sin(2 * math.pi * (U2 * 5 - V2 * 41) + 1.3) * 0.5 + 0.5
    mat_ = fbm(q, 18, 4, 421)[:, :dw]; fine = fbm(q, 60, 3, 422, ax=1, ay=4)[:, :dw]
    dread_h = twist * 0.45 + twist2 * 0.2 + mat_ * 0.25 + fine * 0.1
    dread = ramp(dread_h + 0.15 * fbm(q, 5, 3, 423)[:, :dw], [(0, (0.03, 0.022, 0.016)), (0.5, (0.075, 0.055, 0.038)), (0.85, (0.14, 0.10, 0.07)), (1, (0.24, 0.17, 0.11))])
    bleach = np.clip((fbm(q, 3, 3, 424)[:, :dw] - 0.6) * 3, 0, 1)[..., None]
    dread = dread * (1 - bleach * 0.4) + np.array([0.22, 0.15, 0.09]) * bleach * 0.4
    D = np.zeros((S, S, 4), np.float32)
    fade = np.clip(np.arange(H) / (0.06 * H), 0, 1); fade = (fade * fade * (3 - 2 * fade))[:, None]
    aL = aL * fade; aF = aF * np.clip(np.arange(q) / (0.08 * q), 0, 1)[:, None]
    D[:, :half, :3] = rgbL; D[:, :half, 3] = aL
    D[:q, half:, :3] = rgbF; D[:q, half:, 3] = aF
    D[q:, half:half + dw, :3] = dread; D[q:, half:half + dw, 3] = 1.0
    D[q:, half + dw:, :3] = base[:, :dw]; D[q:, half + dw:, 3] = 1.0
    Image.fromarray((np.clip(D, 0, 1) * 255).astype(np.uint8), 'RGBA').save(os.path.join(out, 'T_Player_Hair_D.png'))
    hgt = np.zeros((S, S), np.float32); hgt[:, :half] = aL * 0.5; hgt[q:, half + dw:] = tuft[:, :dw]; hgt[q:, half:half + dw] = dread_h * 1.5
    n = normal_from_height(ndimage.gaussian_filter(hgt, 0.8), 2.0)
    Image.fromarray((np.clip(n, 0, 1) * 255).astype(np.uint8)).save(os.path.join(out, 'T_Player_Hair_N.png'))

def cloth_atlas(out, S=2048, seed=501):
    """quadrants (UV, v up): hide leather [0,.5]x[.5,1], worn leather strap [.5,1]x[.5,1], rope [0,.5]x[.25,.5], bone [.5,1]x[.25,.5],
    fur underside / dark hide [0,1]x[0,.25]"""
    rng = np.random.default_rng(seed); h2 = S // 2; q = S // 4
    D = np.zeros((S, S, 3), np.float32); Hh = np.zeros((S, S), np.float32); Sm = np.zeros((S, S), np.float32)
    # rows are image rows (top = v 1)
    # hide leather (top-left): mottled rawhide, wrinkles, stains, stitch-free
    f1 = fbm(h2, 5, 5, 511); f2 = fbm(h2, 22, 4, 512); wr = fbm(h2, 18, 3, 513)
    alb = ramp(f1 * 0.7 + f2 * 0.3, [(0, (0.22, 0.14, 0.08)), (0.45, (0.38, 0.26, 0.16)), (0.8, (0.50, 0.37, 0.24)), (1, (0.56, 0.44, 0.30))])
    crease = np.clip(1 - np.abs(wr - 0.5) / 0.025, 0, 1) * np.clip((fbm(h2, 4, 3, 515) - 0.45) * 4, 0, 1)
    alb = alb * (1 - 0.18 * crease[..., None]) * (0.88 + 0.12 * f2[..., None])
    stain = np.clip((fbm(h2, 3, 4, 514) - 0.58) * 4, 0, 1)
    alb = alb * (1 - 0.35 * stain[..., None])
    D[:h2, :h2] = alb; Hh[:h2, :h2] = f2 * 0.6 - crease * 0.4; Sm[:h2, :h2] = 0.28 + 0.12 * stain
    # strap leather (top-right): darker, oiled, worn edges
    f3 = fbm(h2, 8, 4, 521)
    D[:h2, h2:] = ramp(f3, [(0, (0.15, 0.09, 0.05)), (0.6, (0.28, 0.18, 0.1)), (1, (0.40, 0.28, 0.17))])
    Hh[:h2, h2:] = f3 * 0.5; Sm[:h2, h2:] = 0.38
    # rope (rows h2..h2+q, left): twisted plant fibre, tiles horizontally, strands along u
    U, V = np.meshgrid(np.linspace(0, 1, h2, endpoint=False), np.linspace(0, 1, q, endpoint=False))
    twist = 0.5 + 0.5 * np.sin(2 * math.pi * (U * 24 + V * 3))
    fib = fbm(h2, 40, 3, 531, ax=6, ay=1)[:q]
    D[h2:h2 + q, :h2] = ramp(twist * 0.6 + fib * 0.4, [(0, (0.20, 0.16, 0.10)), (0.6, (0.42, 0.35, 0.23)), (1, (0.58, 0.50, 0.35))])
    Hh[h2:h2 + q, :h2] = twist * 0.8 + fib * 0.2; Sm[h2:h2 + q, :h2] = 0.12
    # bone / tooth (rows h2..h2+q, right): ivory with grime in cracks
    f4 = fbm(h2, 10, 4, 541)[:q]; cr = fbm(h2, 30, 3, 542)[:q]
    bone = ramp(f4, [(0, (0.62, 0.56, 0.44)), (0.6, (0.80, 0.74, 0.60)), (1, (0.88, 0.84, 0.72))])
    crack = np.clip((np.abs(cr - 0.5) < 0.02).astype(np.float32) * 1.0, 0, 1)
    D[h2:h2 + q, h2:] = bone * (1 - 0.5 * crack[..., None]); Hh[h2:h2 + q, h2:] = f4 * 0.3 - crack * 0.5; Sm[h2:h2 + q, h2:] = 0.45
    # bottom band: dark hide / fur underside
    f5 = fbm(S, 12, 4, 551)[:q]
    D[S - q:, :] = ramp(f5, [(0, (0.12, 0.08, 0.05)), (1, (0.30, 0.21, 0.13))]); Hh[S - q:, :] = f5 * 0.4; Sm[S - q:, :] = 0.25
    # global dirt
    dirt = np.clip((fbm(S, 7, 4, 561) - 0.55) * 3, 0, 1)
    D = D * (1 - 0.35 * dirt[..., None]) + np.array([0.14, 0.1, 0.07]) * 0.35 * dirt[..., None]
    Image.fromarray((np.clip(D, 0, 1) * 255).astype(np.uint8)).save(os.path.join(out, 'T_Player_Cloth_D.png'))
    n = normal_from_height(ndimage.gaussian_filter(Hh, 1.0), 3.0)
    Image.fromarray((np.clip(n, 0, 1) * 255).astype(np.uint8)).save(os.path.join(out, 'T_Player_Cloth_N.png'))
    M = np.zeros((S, S, 4), np.float32); M[..., 1] = 1.0; M[..., 3] = Sm - 0.1 * dirt
    Image.fromarray((np.clip(M, 0, 1) * 255).astype(np.uint8), 'RGBA').save(os.path.join(out, 'T_Player_Cloth_M.png'))

def eye_atlas(out, S=1024, seed=601):
    """UV-sphere eye (front pole at v=1 = image row 0). iris half-angle ~24 deg, pupil ~8 deg.
    rows below v=0.2 (back of the eyeball, never visible) hold the mouth colours: left = teeth, right = tongue/gums"""
    rng = np.random.default_rng(seed)
    U, V = np.meshgrid(np.linspace(0, 1, S, endpoint=False), np.linspace(0, 1, S, endpoint=False))
    th = V * 180.0                      # degrees from the front pole
    ang = U * 2 * math.pi
    fib = fbm(S, 6, 3, 611, ax=40, ay=1)          # radial streaks (u = around the pole)
    fib2 = fbm(S, 12, 3, 612, ax=90, ay=2)
    crypt = np.clip((fbm(S, 20, 3, 613) - 0.62) * 5, 0, 1)
    IR, PU = 24.0, 7.5
    t = np.clip((th - PU) / (IR - PU), 0, 1)       # 0 pupil edge .. 1 limbus
    iris = ramp(t + 0.18 * (fib - 0.5) + 0.08 * (fib2 - 0.5), [(0.0, (0.30, 0.20, 0.08)), (0.22, (0.55, 0.38, 0.14)), (0.35, (0.42, 0.27, 0.10)),
                                                                   (0.7, (0.30, 0.18, 0.07)), (0.9, (0.18, 0.11, 0.05)), (1.0, (0.08, 0.05, 0.03))])
    iris = iris * (1 - 0.35 * crypt[..., None])
    collarette = np.exp(-((t - 0.3) / 0.05) ** 2)[..., None]
    iris = iris * (1 + 0.25 * collarette)
    sclera = ramp(np.clip((th - IR) / 90, 0, 1), [(0, (0.86, 0.82, 0.76)), (0.5, (0.84, 0.78, 0.72)), (1, (0.70, 0.60, 0.56))])
    vein = np.clip(1 - np.abs(fbm(S, 10, 4, 614, ax=6, ay=1) - 0.5) / 0.03, 0, 1) * np.clip((th - 45) / 40, 0, 1)
    sclera = sclera * (1 - 0.35 * vein[..., None]) + np.array([0.7, 0.25, 0.22]) * 0.35 * vein[..., None]
    limbus = np.clip((th - IR) / 2.5, 0, 1)[..., None]           # soft iris edge
    col = iris * (1 - limbus) + sclera * limbus
    ring = np.exp(-((th - IR) / 1.8) ** 2)[..., None]              # dark limbal ring
    col = col * (1 - 0.45 * ring)
    pupil = np.clip((PU - th) / 0.8 + 0.5, 0, 1)[..., None]
    col = col * (1 - pupil) + np.array([0.012, 0.01, 0.01]) * pupil
    # mouth colours on the hidden back of the eyeball
    back = V > 0.82
    teeth = ramp(fbm(S, 8, 3, 615), [(0, (0.72, 0.66, 0.52)), (1, (0.86, 0.82, 0.70))])
    tongue = ramp(fbm(S, 10, 3, 616), [(0, (0.45, 0.16, 0.15)), (1, (0.62, 0.28, 0.26))])
    col = np.where((back & (U < 0.5))[..., None], teeth, col)
    col = np.where((back & (U >= 0.5))[..., None], tongue, col)
    Image.fromarray((np.clip(col, 0, 1) * 255).astype(np.uint8)).save(os.path.join(out, 'T_Player_Eye_D.png'))

if __name__ == '__main__':
    which = sys.argv[2] if len(sys.argv) > 2 else 'all'
    if which in ('all', 'hair'): hair_atlas(sys.argv[1])
    if which in ('all', 'cloth'): cloth_atlas(sys.argv[1])
    if which in ('all', 'eye'): eye_atlas(sys.argv[1])
    print('ok')
