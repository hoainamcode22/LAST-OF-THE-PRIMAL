#!/usr/bin/env python3
"""
PRIMAL FRONTIER - procedural water textures (original work, numpy + PIL only, nothing downloaded).
Every texture tiles seamlessly: spectra use whole-number frequencies per tile (FFT), cell noises use wrapped
distances, caustics are splatted with wrap-around.

Output (default /home/claude/pf/src_assets/Water, copy to Assets/_Project/Art/Water/):
  T_Water_Normal_Waves.png    512  normal map (import: Normal map)   wind chop, one tile ~ 12 m of sea
  T_Water_Normal_Ripples.png  512  normal map (import: Normal map)   fine capillary ripples, one tile ~ 3 m
  T_Water_Foam.png            512  linear data RGBA (sRGB off)      R bubbly lace foam, G fine specks,
                                                                     B soft macro noise, A caustics
  T_Water_RainRipple.png      256  linear data RGBA (sRGB off, uncompressed, no mips)
                                                                     RG ring direction, B drop time offset, A ring mask
  T_Water_FlowStreaks.png     512  linear data RGBA (sRGB off)      flowing water, stretched along +x (the flow):
                                                                     RG surface slope (x along, y across), B thin foam
                                                                     streaks, A fine bubbles
Usage: python3 water_textures.py [out_dir] [--preview]
"""
import os, sys
import numpy as np
from PIL import Image

RNG = np.random.default_rng(20260928)


# ------------------------------------------------------------------ helpers
def freq_grid(n):
    k = np.fft.fftfreq(n, d=1.0 / n)             # whole cycles per tile
    kx, ky = np.meshgrid(k, k)                    # [y, x]
    return kx, ky


def spectral_height(n, spectrum_fn, seed):
    """real, periodic height field from an amplitude spectrum (random phases) and its exact periodic gradient"""
    rng = np.random.default_rng(seed)
    kx, ky = freq_grid(n)
    amp = np.sqrt(np.maximum(spectrum_fn(kx, ky), 0.0))
    amp[0, 0] = 0.0
    c = (rng.normal(size=(n, n)) + 1j * rng.normal(size=(n, n))) * amp
    h = np.real(np.fft.ifft2(c))
    gx = np.real(np.fft.ifft2(c * (2j * np.pi * kx / n)))   # d h / d pixel (x)
    gy = np.real(np.fft.ifft2(c * (2j * np.pi * ky / n)))
    return h, gx, gy


def normal_png(gx, gy, max_slope):
    """tangent-space normal (x = u = world x, y = v = world z) scaled so the 99.5th percentile slope = max_slope"""
    s = np.percentile(np.sqrt(gx * gx + gy * gy), 99.5)
    gx = gx / s * max_slope
    gy = gy / s * max_slope
    nz = 1.0 / np.sqrt(1.0 + gx * gx + gy * gy)
    nx = -gx * nz
    ny = -gy * nz
    rgb = np.stack([nx * 0.5 + 0.5, ny * 0.5 + 0.5, nz * 0.5 + 0.5], -1)
    return to8(rgb)


def to8(a):
    return (np.clip(a, 0.0, 1.0) * 255.0 + 0.5).astype(np.uint8)


def norm01(a, lo_pct=0.5, hi_pct=99.5):
    lo, hi = np.percentile(a, lo_pct), np.percentile(a, hi_pct)
    return np.clip((a - lo) / max(1e-9, hi - lo), 0.0, 1.0)


def wrap_blur(a, radius):
    """periodic gaussian blur (FFT)"""
    n0, n1 = a.shape
    ky = np.fft.fftfreq(n0)[:, None]
    kx = np.fft.fftfreq(n1)[None, :]
    g = np.exp(-2.0 * (np.pi * radius) ** 2 * (kx * kx + ky * ky))
    return np.real(np.fft.ifft2(np.fft.fft2(a) * g))


def fbm(n, seed, lo=2, hi=64, slope=1.6):
    def spec(kx, ky):
        k = np.sqrt(kx * kx + ky * ky)
        s = np.where((k >= lo) & (k <= hi), 1.0 / np.maximum(k, 1.0) ** (2.0 * slope), 0.0)
        return s
    h, _, _ = spectral_height(n, spec, seed)
    return norm01(h)


def worley(n, cells, seed, jitter=1.0, warp=None):
    """wrapped F1 / F2 distances (in cell units) for a jittered grid of `cells` x `cells` points;
    warp = (wx, wy) periodic offsets in tile units (organic, still seamless)"""
    rng = np.random.default_rng(seed)
    pts = (np.stack(np.meshgrid(np.arange(cells), np.arange(cells)), -1).reshape(-1, 2) + 0.5
           + (rng.random((cells * cells, 2)) - 0.5) * jitter)            # (x, y) in cell units
    ys, xs = np.mgrid[0:n, 0:n]
    px = (xs + 0.5) / n * cells
    py = (ys + 0.5) / n * cells
    if warp is not None:
        px = np.mod(px + warp[0] * cells, cells)
        py = np.mod(py + warp[1] * cells, cells)
    f1 = np.full((n, n), 1e9)
    f2 = np.full((n, n), 1e9)
    idx = np.zeros((n, n), dtype=np.int32)
    for i, (x, y) in enumerate(pts):
        dx = np.abs(px - x); dx = np.minimum(dx, cells - dx)
        dy = np.abs(py - y); dy = np.minimum(dy, cells - dy)
        d = np.sqrt(dx * dx + dy * dy)
        closer = d < f1
        f2 = np.where(closer, f1, np.minimum(f2, d))
        idx = np.where(closer, i, idx)
        f1 = np.where(closer, d, f1)
    return f1, f2, idx


# ------------------------------------------------------------------ 1. wind waves (chop)
def waves_normal(n=512):
    wind = np.array([0.8, 0.6]); wind /= np.linalg.norm(wind)

    def spec(kx, ky):
        k = np.sqrt(kx * kx + ky * ky)
        kk = np.maximum(k, 1e-6)
        cosang = (kx * wind[0] + ky * wind[1]) / kk
        directional = 0.2 + 0.8 * np.abs(cosang) ** 3                # mostly along the wind, never rows
        peak = 4.5                                                   # dominant waves: ~4.5 per tile (2.7 m at 12 m / tile)
        phillips = np.exp(-(peak / kk) ** 2) / kk ** 4
        return np.where((k >= 1.5) & (k <= 120), phillips * directional * np.exp(-(k / 55.0) ** 2), 0.0)

    h, gx, gy = spectral_height(n, spec, 11)
    # a second, broader swell layer for large-scale variation
    def spec2(kx, ky):
        k = np.sqrt(kx * kx + ky * ky)
        return np.where((k >= 1) & (k <= 4), 1.0 / np.maximum(k, 1) ** 3, 0.0)
    h2, gx2, gy2 = spectral_height(n, spec2, 12)
    s1 = np.std(np.sqrt(gx * gx + gy * gy)); s2 = np.std(np.sqrt(gx2 * gx2 + gy2 * gy2))
    gx = gx / s1 + gx2 / s2 * 0.35
    gy = gy / s1 + gy2 / s2 * 0.35
    return normal_png(gx, gy, 0.85), h


# ------------------------------------------------------------------ 2. capillary ripples
def ripples_normal(n=512):
    def spec(kx, ky):
        k = np.sqrt(kx * kx + ky * ky)
        kk = np.maximum(k, 1e-6)
        band = np.exp(-((np.log(kk) - np.log(22.0)) ** 2) / (2 * 0.55 ** 2))   # log-normal band around 22 cycles / tile
        return np.where(k >= 3, band / kk ** 2, 0.0)
    _, gx, gy = spectral_height(n, spec, 21)
    return normal_png(gx, gy, 0.7)


# ------------------------------------------------------------------ 3. foam (R lace, G specks, B macro, A caustics)
def foam_lace(n=512):
    """white lace with dark round holes of many sizes, domain-warped so nothing looks like a grid"""
    lace = np.ones((n, n))
    rng = np.random.default_rng(31)
    wx = (fbm(n, 36, lo=2, hi=12, slope=1.5) - 0.5) * 0.05
    wy = (fbm(n, 37, lo=2, hi=12, slope=1.5) - 0.5) * 0.05
    for cells, rmin, rmax, depth in [(9, 0.18, 0.36, 0.95), (18, 0.16, 0.36, 0.85), (36, 0.14, 0.34, 0.7), (72, 0.12, 0.32, 0.5)]:
        f1, f2, idx = worley(n, cells, int(rng.integers(1e6)), warp=(wx, wy))
        pick = np.random.default_rng(cells + 5).random(cells * cells)
        radius = rmin + (rmax - rmin) * pick[idx]
        present = (np.random.default_rng(cells + 9).random(cells * cells) < 0.8)[idx]   # some cells keep solid foam
        hole = 1.0 - np.clip((f1 - radius * 0.6) / (radius * 0.4), 0.0, 1.0)       # 1 inside the bubble
        lace *= 1.0 - hole * depth * present
    density = fbm(n, 35, lo=2, hi=20, slope=1.3)
    v = lace * (0.55 + 0.45 * density) + density * 0.25
    v = wrap_blur(v, 0.6)
    return norm01(v, 1, 99.7)


def foam_specks(n=512):
    """fine bubbles in clusters (white water detail for the stream and the swash edge)"""
    wx = (fbm(n, 44, lo=3, hi=16, slope=1.5) - 0.5) * 0.04
    wy = (fbm(n, 45, lo=3, hi=16, slope=1.5) - 0.5) * 0.04
    f1, f2, idx = worley(n, 48, 41, warp=(wx, wy))
    rnd = np.random.default_rng(42).random(48 * 48)[idx]
    speck = np.clip(1.0 - f1 / (0.2 + 0.35 * rnd), 0.0, 1.0) ** 1.2
    cluster = fbm(n, 43, lo=3, hi=24, slope=1.2)
    v = speck * (0.25 + cluster) + cluster * 0.35
    return norm01(wrap_blur(v, 0.5), 1, 99.8)


def caustics(n=512, height=None):
    """photon splat through a periodic height field: light bends at the surface and bunches up into bright lines"""
    if height is None:
        def spec(kx, ky):
            k = np.sqrt(kx * kx + ky * ky)
            return np.where((k >= 2) & (k <= 14), 1.0 / np.maximum(k, 1) ** 3.2, 0.0)
        height, _, _ = spectral_height(n, spec, 51)
    h = norm01(height) - 0.5
    gy, gx = np.gradient(np.pad(h, 1, mode='wrap'))
    gx = gx[1:-1, 1:-1]; gy = gy[1:-1, 1:-1]
    acc = np.zeros(n * n)
    ss = 3                                                   # 3x3 photons per texel
    for oy in range(ss):
        for ox in range(ss):
            ys, xs = np.mgrid[0:n, 0:n].astype(np.float64)
            xs += (ox + 0.5) / ss; ys += (oy + 0.5) / ss
            k = n * 0.9                                      # refraction strength (texels per unit slope)
            tx = np.mod(xs - gx * k, n); ty = np.mod(ys - gy * k, n)
            ix = tx.astype(np.int64) % n; iy = ty.astype(np.int64) % n
            acc += np.bincount((iy * n + ix).ravel(), minlength=n * n)
    c = acc.reshape(n, n)
    c = wrap_blur(c, 0.9)
    c = c / np.mean(c)
    return np.clip((c - 0.6) / 2.6, 0.0, 1.0) ** 0.8


def foam_texture(n=512):
    r = foam_lace(n)
    g = foam_specks(n)
    b = fbm(n, 61, lo=1, hi=8, slope=1.4)
    def spec(kx, ky):
        k = np.sqrt(kx * kx + ky * ky)
        return np.where((k >= 3) & (k <= 16), 1.0 / np.maximum(k, 1) ** 3.0, 0.0)
    hc, _, _ = spectral_height(n, spec, 52)
    a = caustics(n, hc)
    return to8(np.stack([r, g, b, a], -1))


# ------------------------------------------------------------------ 4. rain ripple data
def rain_ripple(n=256, drops=110):
    rng = np.random.default_rng(71)
    pos = rng.random((drops, 2)) * n
    rad = rng.uniform(10.0, 22.0, drops)
    tof = rng.random(drops)
    ys, xs = np.mgrid[0:n, 0:n].astype(np.float64) + 0.5
    best = np.full((n, n), 9.0)           # d / R of the chosen drop
    out = np.zeros((n, n, 4))
    for (x, y), r, t in zip(pos, rad, tof):
        dx = xs - x; dx = (dx + n / 2) % n - n / 2
        dy = ys - y; dy = (dy + n / 2) % n - n / 2
        d = np.sqrt(dx * dx + dy * dy)
        rel = d / r
        take = (rel < 1.0) & (rel < best)
        if not take.any():
            continue
        inv = 1.0 / np.maximum(d, 1e-6)
        out[..., 0] = np.where(take, dx * inv * 0.5 + 0.5, out[..., 0])
        out[..., 1] = np.where(take, dy * inv * 0.5 + 0.5, out[..., 1])
        out[..., 2] = np.where(take, t, out[..., 2])
        out[..., 3] = np.where(take, 1.0 - rel, out[..., 3])
        best = np.where(take, rel, best)
    out[..., 0] = np.where(best > 1.0, 0.5, out[..., 0])
    out[..., 1] = np.where(best > 1.0, 0.5, out[..., 1])
    return to8(out)


# ------------------------------------------------------------------ 5. flow streaks (stream surface, x = downstream)
def flow_streaks(n=512):
    def spec(kx, ky):
        ka = np.sqrt((kx * 4.0) ** 2 + ky ** 2)                        # features ~4x longer along x than across
        k = np.maximum(ka, 1e-6)
        band = np.exp(-((np.log(k) - np.log(34.0)) ** 2) / (2 * 0.6 ** 2))
        return np.where(ka >= 4, band / k ** 2, 0.0)
    h, gx, gy = spectral_height(n, spec, 81)
    def spec2(kx, ky):                                                  # a few broad swirls so it is not all fine grain
        ka = np.sqrt((kx * 2.5) ** 2 + ky ** 2)
        return np.where((ka >= 3) & (ka <= 14), 1.0 / np.maximum(ka, 1) ** 3, 0.0)
    h2, gx2, gy2 = spectral_height(n, spec2, 82)
    s1 = np.percentile(np.sqrt(gx * gx + gy * gy), 99.5); s2 = np.percentile(np.sqrt(gx2 * gx2 + gy2 * gy2), 99.5)
    gxx = gx / s1 * 0.75 + gx2 / s2 * 0.45
    gyy = gy / s1 * 0.75 + gy2 / s2 * 0.45
    rg = np.stack([np.clip(-gxx * 0.5 + 0.5, 0, 1), np.clip(-gyy * 0.5 + 0.5, 0, 1)], -1)
    # thin foam streaks: ridges of an even more stretched field, broken up by a slow mask
    def spec3(kx, ky):
        ka = np.sqrt((kx * 7.0) ** 2 + ky ** 2)
        return np.where((ka >= 6) & (ka <= 60), 1.0 / np.maximum(ka, 1) ** 2.2, 0.0)
    h3, _, _ = spectral_height(n, spec3, 83)
    ridge = 1.0 - np.abs(norm01(h3) * 2 - 1)                           # 1 on the zero crossings: thin lines
    ridge = np.clip((ridge - 0.72) / 0.28, 0, 1) ** 1.5
    mask = fbm(n, 84, lo=2, hi=10, slope=1.4)
    streak = norm01(ridge * (0.25 + mask) + fbm(n, 85, lo=6, hi=40, slope=1.2) * 0.15)
    wx = (fbm(n, 86, lo=2, hi=10, slope=1.5) - 0.5) * 0.03
    f1, _, idx = worley(n, 40, 87, warp=(wx, wx * 0.3))
    rnd = np.random.default_rng(88).random(40 * 40)[idx]
    bubbles = norm01(np.clip(1 - f1 / (0.18 + 0.3 * rnd), 0, 1) * (0.3 + fbm(n, 89, lo=3, hi=20, slope=1.2)))
    return to8(np.concatenate([rg, streak[..., None], bubbles[..., None]], -1))


# ------------------------------------------------------------------ checks + preview
def seam_error(img):
    a = img.astype(np.float64)
    inner_x = np.abs(a[:, 1] - a[:, 0]).mean() + np.abs(a[:, -1] - a[:, -2]).mean()
    wrap_x = np.abs(a[:, 0] - a[:, -1]).mean()
    inner_y = np.abs(a[1] - a[0]).mean() + np.abs(a[-1] - a[-2]).mean()
    wrap_y = np.abs(a[0] - a[-1]).mean()
    return wrap_x / max(1e-6, inner_x / 2), wrap_y / max(1e-6, inner_y / 2)


def tile2x2(img):
    return np.concatenate([np.concatenate([img, img], 1), np.concatenate([img, img], 1)], 0)


def main():
    out = sys.argv[1] if len(sys.argv) > 1 and not sys.argv[1].startswith('--') else '/home/claude/pf/src_assets/Water'
    preview = '--preview' in sys.argv
    os.makedirs(out, exist_ok=True)
    results = {}
    wn, _ = waves_normal(512)
    results['T_Water_Normal_Waves.png'] = wn
    results['T_Water_Normal_Ripples.png'] = ripples_normal(512)
    results['T_Water_Foam.png'] = foam_texture(512)
    results['T_Water_RainRipple.png'] = rain_ripple(256)
    results['T_Water_FlowStreaks.png'] = flow_streaks(512)
    for name, img in results.items():
        mode = 'RGBA' if img.shape[-1] == 4 else 'RGB'
        Image.fromarray(img, mode).save(os.path.join(out, name))
        ex, ey = seam_error(img)
        print(f'{name}: {img.shape[1]}x{img.shape[0]} {mode}, seam ratio x {ex:.2f} y {ey:.2f} (about 1 = seamless)')
        if preview:
            pv = os.path.join(out, '_preview'); os.makedirs(pv, exist_ok=True)
            if mode == 'RGBA':
                for ci, cn in enumerate('RGBA'):
                    Image.fromarray(tile2x2(img[..., ci]), 'L').save(os.path.join(pv, name.replace('.png', f'_{cn}_2x2.png')))
            else:
                Image.fromarray(tile2x2(img), mode).save(os.path.join(pv, name.replace('.png', '_2x2.png')))


if __name__ == '__main__':
    main()
