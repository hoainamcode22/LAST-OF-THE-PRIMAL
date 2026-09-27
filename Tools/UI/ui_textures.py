"""
PRIMAL FRONTIER - procedural UI textures (original, generated here; no external art).
Writes Assets/_Project/Resources/UI/*.png : panels (dark, leather, wood, stone, aged paper), slots, buttons, bars,
vignette, HUD icons (health, hunger, thirst, stamina, temperature, crosshair, journal, unknown) and journal sketches.
Run: python3 ui_textures.py <project_root>
"""
import sys, os, math
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from scipy.ndimage import gaussian_filter

ROOT = sys.argv[1] if len(sys.argv) > 1 else "."
OUT = os.path.join(ROOT, "Assets/_Project/Resources/UI")
SK = os.path.join(OUT, "Sketches")
os.makedirs(SK, exist_ok=True)
rng = np.random.default_rng(7)

def noise(h, w, scale, octaves=4, seed=0):
    r = np.random.default_rng(seed); acc = np.zeros((h, w)); amp = 1.0; tot = 0
    for o in range(octaves):
        s = max(1, scale / (2 ** o))
        n = gaussian_filter(r.standard_normal((h, w)), s, mode="wrap")
        n /= (n.std() + 1e-9); acc += n * amp; tot += amp; amp *= 0.5
    acc /= tot; return (acc - acc.min()) / (acc.max() - acc.min() + 1e-9)

def save(arr, name, folder=OUT):
    a = np.clip(arr, 0, 255).astype(np.uint8)
    Image.fromarray(a, "RGBA" if a.shape[2] == 4 else "RGB").save(os.path.join(folder, name + ".png"))

def rounded_mask(h, w, r, soft=1.5):
    y, x = np.mgrid[0:h, 0:w].astype(float)
    dx = np.maximum(np.maximum(r - x, x - (w - 1 - r)), 0); dy = np.maximum(np.maximum(r - y, y - (h - 1 - r)), 0)
    d = np.sqrt(dx * dx + dy * dy)
    return np.clip((r - d) / soft + 0.5, 0, 1)

def edge_dist(h, w):
    y, x = np.mgrid[0:h, 0:w].astype(float)
    return np.minimum(np.minimum(x, w - 1 - x), np.minimum(y, h - 1 - y))

def rgba(rgb, a): return np.dstack([rgb, a * 255])

# ------------------------------------------------------------------ panels
def panel_dark(n=128):
    h = w = n; nz = noise(h, w, 10, seed=1)
    base = np.array([18, 15, 12]) + (nz[..., None] - 0.5) * 10
    d = edge_dist(h, w); rim = np.exp(-d / 3.0)
    col = base + rim[..., None] * np.array([60, 48, 34])
    worn = noise(h, w, 3, seed=2) > 0.62
    a = rounded_mask(h, w, 10) * (0.82 + 0.1 * nz); a = np.where((d < 2.2) & worn, a * 0.55, a)
    save(rgba(col, a), "ui_panel")

def leather(n=256):
    h = w = n; big = noise(h, w, 24, seed=3); grain = noise(h, w, 1.2, 2, seed=4); pores = noise(h, w, 0.7, 1, seed=5)
    base = np.array([74, 46, 27]) * (0.78 + 0.35 * big)[..., None] * (0.9 + 0.2 * grain)[..., None] - (pores > 0.8)[..., None] * 10
    d = edge_dist(h, w)
    base *= (0.55 + 0.45 * np.clip(d / 26, 0, 1))[..., None]            # darker burnished edge
    # stitch line
    st = (np.abs(d - 13) < 1.3) & ((np.mgrid[0:h, 0:w][1] + np.mgrid[0:h, 0:w][0]) % 12 < 7)
    base = np.where(st[..., None], np.array([196, 170, 128]) * (0.8 + 0.2 * grain)[..., None], base)
    a = rounded_mask(h, w, 18)
    save(rgba(base, a), "ui_leather")

def wood(n=256):
    h = w = n; y, x = np.mgrid[0:h, 0:w].astype(float)
    warp = noise(h, w, 20, seed=6) * 18
    rings = (np.sin((y + warp) * 0.35 + noise(h, w, 4, seed=7) * 3) + 1) * 0.5
    fine = noise(h, w, 0.8, 2, seed=8)
    base = np.array([96, 64, 38]) * (0.7 + 0.25 * rings + 0.15 * fine)[..., None]
    for yy in (h // 3, 2 * h // 3):                                   # plank seams
        base[yy - 1:yy + 1] *= 0.45
    d = edge_dist(h, w); base *= (0.6 + 0.4 * np.clip(d / 10, 0, 1))[..., None]
    save(rgba(base, rounded_mask(h, w, 6)), "ui_wood")

def stone(n=256):
    h = w = n; a1 = noise(h, w, 14, seed=9); a2 = noise(h, w, 2, 3, seed=10)
    base = np.array([92, 90, 84]) * (0.65 + 0.3 * a1 + 0.2 * a2)[..., None]
    cr = np.abs(noise(h, w, 9, 2, seed=11) - 0.5) < 0.012
    base[cr] *= 0.5
    d = edge_dist(h, w); base *= (0.55 + 0.45 * np.clip(d / 12, 0, 1))[..., None]
    save(rgba(base, rounded_mask(h, w, 8)), "ui_stone")

def paper(n=512):
    h = w = n; big = noise(h, w, 50, seed=12); fib = noise(h, w, 0.6, 1, seed=13); stains = noise(h, w, 30, 3, seed=14)
    base = np.array([226, 208, 170]) * (0.9 + 0.08 * big + 0.04 * fib)[..., None]
    base -= np.clip(stains - 0.72, 0, 1)[..., None] * np.array([150, 170, 190])   # tea stains
    d = edge_dist(h, w); ed = noise(h, w, 6, seed=15) * 18
    burn = np.clip(1 - (d - ed * 0.5) / 34, 0, 1)
    base = base * (1 - burn[..., None] * 0.55) + burn[..., None] * np.array([70, 40, 18]) * 0.55
    a = np.clip((d - ed * 0.25) / 2.0, 0, 1)
    save(rgba(base, a), "ui_paper")

def slot(n=96, active=False):
    h = w = n; nz = noise(h, w, 6, seed=16)
    d = edge_dist(h, w)
    base = np.array([22, 19, 16]) + (nz[..., None] - 0.5) * 12
    inner = np.exp(-np.maximum(d - 4, 0) / 6.0)
    base = base * (1 - 0.5 * inner[..., None])
    border = (d < 3.2)
    bc = np.array([214, 160, 80]) if active else np.array([92, 78, 60])
    base = np.where(border[..., None], bc * (0.75 + 0.35 * nz)[..., None], base)
    if active:
        glow = np.exp(-np.maximum(d - 3, 0) / 5.0) * 0.6
        base = base + glow[..., None] * np.array([120, 80, 30])
    a = rounded_mask(h, w, 8) * np.where(border, 1.0, 0.86)
    save(rgba(base, a), "ui_slot_active" if active else "ui_slot")

def button(w=192, h=64):
    nz = noise(h, w, 5, seed=17); y = np.mgrid[0:h, 0:w][0].astype(float)
    grad = 1.08 - 0.3 * (y / h)
    base = np.array([88, 60, 36]) * (grad * (0.85 + 0.25 * nz))[..., None]
    d = edge_dist(h, w)
    base = np.where((d < 2.5)[..., None], np.array([170, 128, 72]) * (0.8 + 0.3 * nz)[..., None], base)
    save(rgba(base, rounded_mask(h, w, 10)), "ui_button")

def bars():
    h, w = 24, 128
    d = edge_dist(h, w)
    back = np.dstack([np.full((h, w), 12), np.full((h, w), 10), np.full((h, w), 8)]).astype(float)
    back = np.where((d < 1.6)[..., None], np.array([90, 76, 58]), back)
    save(rgba(back, rounded_mask(h, w, 6) * 0.9), "ui_bar_back")
    h2, w2 = 12, 64; y = np.mgrid[0:h2, 0:w2][0].astype(float)
    nz = noise(h2, w2, 1.5, 2, seed=18)
    v = (0.78 + 0.35 * (1 - np.abs(y - h2 * 0.35) / h2) + 0.08 * nz) * 255
    fill = np.dstack([v, v, v])
    save(rgba(fill, rounded_mask(h2, w2, 3)), "ui_bar_fill")

def vignette(n=512):
    y, x = np.mgrid[0:n, 0:n].astype(float) / (n - 1) * 2 - 1
    r = np.sqrt(x * x + y * y)
    a = np.clip((r - 0.35) / 0.9, 0, 1) ** 1.6
    save(rgba(np.zeros((n, n, 3)), 0.25 + 0.75 * a), "ui_vignette")

# ------------------------------------------------------------------ icons (white, tinted in UI)
def icon(name, draw_fn, n=128):
    im = Image.new("L", (n * 4, n * 4), 0); d = ImageDraw.Draw(im); draw_fn(d, n * 4)
    im = im.resize((n, n), Image.LANCZOS)
    a = np.array(im).astype(float) / 255
    save(rgba(np.full((n, n, 3), 255.0), a), "icon_" + name)

def heart(d, s):
    k = s / 100
    d.ellipse([18 * k, 20 * k, 52 * k, 54 * k], fill=255); d.ellipse([48 * k, 20 * k, 82 * k, 54 * k], fill=255)
    d.polygon([(19 * k, 44 * k), (81 * k, 44 * k), (50 * k, 84 * k)], fill=255)
def meat(d, s):
    k = s / 100
    d.ellipse([20 * k, 14 * k, 70 * k, 64 * k], fill=255)
    d.line([(58 * k, 56 * k), (80 * k, 80 * k)], fill=255, width=int(11 * k))
    d.ellipse([74 * k, 70 * k, 90 * k, 86 * k], fill=255); d.ellipse([68 * k, 78 * k, 84 * k, 94 * k], fill=255)
def drop(d, s):
    k = s / 100
    d.polygon([(50 * k, 10 * k), (24 * k, 58 * k), (76 * k, 58 * k)], fill=255); d.ellipse([24 * k, 38 * k, 76 * k, 90 * k], fill=255)
def bolt(d, s):
    k = s / 100
    d.polygon([(58 * k, 6 * k), (22 * k, 56 * k), (46 * k, 56 * k), (38 * k, 94 * k), (78 * k, 40 * k), (54 * k, 40 * k)], fill=255)
def thermo(d, s):
    k = s / 100
    d.rounded_rectangle([40 * k, 8 * k, 60 * k, 70 * k], radius=10 * k, fill=255); d.ellipse([30 * k, 58 * k, 70 * k, 96 * k], fill=255)
    d.rounded_rectangle([46 * k, 16 * k, 54 * k, 64 * k], radius=4 * k, fill=0)
    d.ellipse([38 * k, 66 * k, 62 * k, 90 * k], fill=0); d.ellipse([42 * k, 70 * k, 58 * k, 86 * k], fill=255)
    d.rectangle([48 * k, 36 * k, 52 * k, 72 * k], fill=255)
def cross(d, s):
    k = s / 100
    for a, b in (((50, 18), (50, 38)), ((50, 62), (50, 82)), ((18, 50), (38, 50)), ((62, 50), (82, 50))):
        d.line([(a[0] * k, a[1] * k), (b[0] * k, b[1] * k)], fill=255, width=int(6 * k))
    d.ellipse([46 * k, 46 * k, 54 * k, 54 * k], fill=255)
def book(d, s):
    k = s / 100
    d.polygon([(12 * k, 20 * k), (48 * k, 28 * k), (48 * k, 88 * k), (12 * k, 80 * k)], fill=255)
    d.polygon([(88 * k, 20 * k), (52 * k, 28 * k), (52 * k, 88 * k), (88 * k, 80 * k)], fill=255)
def unknown(d, s):
    k = s / 100
    d.arc([28 * k, 12 * k, 72 * k, 56 * k], 190, 400, fill=255, width=int(11 * k))
    d.line([(50 * k, 54 * k), (50 * k, 68 * k)], fill=255, width=int(11 * k)); d.ellipse([43 * k, 76 * k, 57 * k, 90 * k], fill=255)

# ------------------------------------------------------------------ journal sketches (sepia pencil on transparent)
def sketch(name, fn, w=512, h=420):
    im = Image.new("L", (w * 2, h * 2), 0); d = ImageDraw.Draw(im); fn(d, w * 2, h * 2)
    im = im.filter(ImageFilter.GaussianBlur(1.2)).resize((w, h), Image.LANCZOS)
    a = np.array(im).astype(float) / 255
    jitter = noise(h, w, 0.8, 1, seed=hash(name) % 1000)
    a = np.clip(a * (0.75 + 0.35 * jitter), 0, 1)
    col = np.dstack([np.full((h, w), 70), np.full((h, w), 44), np.full((h, w), 24)]).astype(float)
    save(rgba(col, a * 0.92), "sketch_" + name, SK)

def pen(d): return dict(fill=255, width=5)
def hatch(d, box, step=18, ang=1.0):
    x0, y0, x1, y1 = box
    for t in range(int(x0 - (y1 - y0)), int(x1), step):
        d.line([(t, y1), (t + (y1 - y0) * ang, y0)], fill=150, width=2)

def sk_wreck(d, w, h):
    d.line([(80, h * 0.72), (w - 80, h * 0.72)], **pen(d))
    d.arc([200, h * 0.35, w - 200, h * 1.05], 180, 360, fill=255, width=7)
    for i in range(8):
        x = 260 + i * (w - 520) / 7; d.line([(x, h * 0.72), (x + 30, h * 0.36 + abs(i - 3.5) * 20)], fill=255, width=6)
    d.line([(w * 0.55, h * 0.72), (w * 0.62, h * 0.12)], fill=255, width=9)
    for i in range(6): d.arc([60 + i * 150, h * 0.76, 220 + i * 150, h * 0.86], 200, 340, fill=200, width=4)
def sk_water(d, w, h):
    for r in range(6):
        y = h * 0.3 + r * 60
        for i in range(7): d.arc([60 + i * 140, y, 200 + i * 140, y + 50], 200, 340, fill=255 - r * 20, width=5)
    d.ellipse([w * 0.4, h * 0.05, w * 0.6, h * 0.25], outline=255, width=5)
def sk_fire(d, w, h):
    cx = w / 2
    for i in range(5): d.line([(cx - 220 + i * 40, h * 0.85), (cx + 220 - i * 40, h * 0.7)], fill=255, width=12)
    for s, o in ((1.0, 0), (0.7, -60), (0.6, 70)):
        pts = [(cx + o - 90 * s, h * 0.72), (cx + o - 40 * s, h * 0.45), (cx + o, h * 0.72 - 380 * s), (cx + o + 40 * s, h * 0.45), (cx + o + 90 * s, h * 0.72)]
        d.line(pts, fill=255, width=6, joint="curve")
def sk_night(d, w, h):
    d.ellipse([w * 0.6, h * 0.1, w * 0.8, h * 0.34], outline=255, width=6); d.ellipse([w * 0.64, h * 0.08, w * 0.84, h * 0.3], fill=0)
    for i in range(22):
        x, y = rng.uniform(60, w - 60), rng.uniform(40, h * 0.5); d.line([(x - 8, y), (x + 8, y)], fill=230, width=3); d.line([(x, y - 8), (x, y + 8)], fill=230, width=3)
    for i in range(9):
        x = 80 + i * (w - 160) / 8; hh = rng.uniform(0.2, 0.35) * h
        d.polygon([(x - 60, h * 0.95), (x, h * 0.95 - hh), (x + 60, h * 0.95)], outline=255)
def sk_shelter(d, w, h):
    d.line([(w * 0.2, h * 0.85), (w * 0.8, h * 0.85)], **pen(d))
    d.line([(w * 0.25, h * 0.85), (w * 0.55, h * 0.2)], fill=255, width=9); d.line([(w * 0.75, h * 0.85), (w * 0.55, h * 0.2)], fill=255, width=9)
    for i in range(10):
        y = h * 0.28 + i * 50; x = w * 0.55 - (y - h * 0.2) * 0.47
        d.line([(x, y), (w * 0.55 + (y - h * 0.2) * 0.31, y)], fill=190, width=4)
def sk_item(name, icon_path):
    if not os.path.exists(icon_path): return False
    im = Image.open(icon_path).convert("RGBA").resize((380, 380), Image.LANCZOS)
    g = np.array(im.convert("L")).astype(float); a = np.array(im)[..., 3].astype(float) / 255
    from skimage import filters
    e = filters.sobel(g / 255); e = np.clip(e * 4, 0, 1)
    tone = (1 - g / 255) * 0.55 + e * 0.9
    out = np.zeros((420, 512)); out[20:400, 66:446] = np.clip(tone * a, 0, 1)
    col = np.dstack([np.full(out.shape, 70), np.full(out.shape, 44), np.full(out.shape, 24)]).astype(float)
    save(rgba(col, out * 0.92), "sketch_" + name, SK); return True
def sk_beach(d, w, h):
    d.line([(40, h * 0.6), (w - 40, h * 0.55)], **pen(d))
    for i in range(5): d.arc([40 + i * 190, h * 0.62, 240 + i * 190, h * 0.72], 200, 340, fill=220, width=4)
    for x in (w * 0.2, w * 0.3): d.line([(x, h * 0.58), (x + 40, h * 0.1)], fill=255, width=7); d.arc([x - 80, h * 0.05, x + 160, h * 0.25], 180, 300, fill=255, width=5)
def sk_meadow(d, w, h):
    d.line([(40, h * 0.5), (w * 0.4, h * 0.3), (w * 0.7, h * 0.45), (w - 40, h * 0.35)], fill=255, width=5)
    for i in range(60):
        x = rng.uniform(40, w - 40); y = rng.uniform(h * 0.55, h * 0.95); d.line([(x, y), (x + rng.uniform(-12, 12), y - rng.uniform(20, 50))], fill=200, width=3)
    d.line([(w * 0.3, h * 0.9), (w * 0.5, h * 0.55), (w * 0.9, h * 0.52)], fill=140, width=30)
def sk_cave(d, w, h):
    d.arc([w * 0.2, h * 0.2, w * 0.8, h * 1.3], 180, 360, fill=255, width=9)
    d.chord([w * 0.32, h * 0.42, w * 0.68, h * 1.2], 180, 360, fill=110)
    d.line([(40, h * 0.3), (w * 0.2, h * 0.35)], **pen(d)); d.line([(w * 0.8, h * 0.35), (w - 40, h * 0.28)], **pen(d))
def sk_rocks(d, w, h):
    for i in range(6):
        cx = 100 + i * (w - 200) / 5; s = rng.uniform(80, 160)
        pts = [(cx - s, h * 0.9), (cx - s * 0.6, h * 0.9 - s * 1.1), (cx + s * 0.2, h * 0.9 - s * 1.4), (cx + s, h * 0.9 - s * 0.5), (cx + s * 1.1, h * 0.9)]
        d.polygon(pts, outline=255); d.line(pts + [pts[0]], fill=255, width=5)
def sk_footprint(d, w, h):
    cx, cy = w / 2, h * 0.62
    d.ellipse([cx - 110, cy - 70, cx + 110, cy + 110], outline=255, width=7)
    for ang, L in ((-28, 330), (0, 380), (28, 330)):
        a = math.radians(ang - 90); ex, ey = cx + math.cos(a) * L, cy + math.sin(a) * L
        d.line([(cx + math.cos(a) * 60, cy + math.sin(a) * 60), (ex, ey)], fill=255, width=46)
        d.polygon([(ex - 22, ey + 10), (ex + math.cos(a) * 60, ey + math.sin(a) * 60), (ex + 22, ey + 10)], fill=255)
    d.line([(cx + 260, cy + 90), (cx + 380, cy + 90)], fill=200, width=4); d.text((cx + 270, cy + 100), "x 1", fill=200)
def sk_log(d, w, h):
    d.polygon([(w * 0.2, h * 0.2), (w * 0.5, h * 0.25), (w * 0.5, h * 0.85), (w * 0.2, h * 0.8)], outline=255); d.line([(w * 0.2, h * 0.2), (w * 0.5, h * 0.25), (w * 0.5, h * 0.85), (w * 0.2, h * 0.8), (w * 0.2, h * 0.2)], fill=255, width=6)
    d.line([(w * 0.8, h * 0.2), (w * 0.5, h * 0.25), (w * 0.5, h * 0.85), (w * 0.8, h * 0.8), (w * 0.8, h * 0.2)], fill=255, width=6)
    for i in range(8):
        y = h * 0.32 + i * 50; d.line([(w * 0.24, y), (w * 0.46, y + 6)], fill=170, width=3); d.line([(w * 0.54, y + 6), (w * 0.76, y)], fill=170, width=3)
def sk_trike(d, w, h):
    # generic three-horned grazer silhouette (original drawing)
    body = [(w * 0.25, h * 0.55), (w * 0.35, h * 0.35), (w * 0.6, h * 0.32), (w * 0.72, h * 0.42), (w * 0.76, h * 0.6), (w * 0.7, h * 0.66), (w * 0.3, h * 0.66)]
    d.line(body + [body[0]], fill=255, width=7)
    d.line([(w * 0.25, h * 0.55), (w * 0.08, h * 0.5)], fill=255, width=12)                     # tail
    d.ellipse([w * 0.7, h * 0.3, w * 0.9, h * 0.6], outline=255, width=7)                          # frill
    d.polygon([(w * 0.76, h * 0.45), (w * 0.93, h * 0.52), (w * 0.9, h * 0.62), (w * 0.76, h * 0.6)], outline=255)
    d.line([(w * 0.84, h * 0.42), (w * 0.99, h * 0.3)], fill=255, width=6); d.line([(w * 0.8, h * 0.4), (w * 0.95, h * 0.27)], fill=255, width=6)
    d.line([(w * 0.92, h * 0.52), (w * 0.99, h * 0.46)], fill=255, width=6)
    for x in (0.33, 0.43, 0.6, 0.68): d.line([(w * x, h * 0.64), (w * x, h * 0.86)], fill=255, width=16)
def sk_claw(d, w, h):
    for i in range(3):
        o = (i - 1) * 90; d.line([(w * 0.35 + o, h * 0.15), (w * 0.62 + o, h * 0.85)], fill=255, width=14)
    hatch(d, (w * 0.25, h * 0.1, w * 0.8, h * 0.9), 26)

if __name__ == "__main__":
    panel_dark(); leather(); wood(); stone(); paper(); slot(); slot(active=True); button(); bars(); vignette()
    for n, f in (("health", heart), ("hunger", meat), ("thirst", drop), ("stamina", bolt), ("temperature", thermo), ("crosshair", cross), ("journal", book), ("unknown", unknown)):
        icon(n, f)
    for n, f in (("wreck", sk_wreck), ("water", sk_water), ("fire", sk_fire), ("night", sk_night), ("shelter", sk_shelter), ("beach", sk_beach),
                 ("meadow", sk_meadow), ("cave", sk_cave), ("rocks", sk_rocks), ("footprint", sk_footprint), ("log", sk_log), ("trike", sk_trike), ("claw", sk_claw)):
        sketch(n, f)
    icons = os.path.join(ROOT, "Assets/_Project/Art/Icons")
    for n, ic in (("axe", "ICON_StoneAxe"), ("rope", "ICON_Rope"), ("spear", "ICON_StoneSpear"), ("bow", "ICON_Bow"), ("meat", "ICON_CookedMeat")):
        sk_item(n, os.path.join(icons, ic + ".png"))
    print("ok", len(os.listdir(OUT)), len(os.listdir(SK)))
