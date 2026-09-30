# ICON_Charcoal.png (original, PIL): three faceted charcoal lumps with a soft shadow, 256x256 RGBA like the other item icons
import random, math, os
from PIL import Image, ImageDraw, ImageFilter
S = 4; N = 256 * S
random.seed(7)
img = Image.new("RGBA", (N, N), (0, 0, 0, 0))
# soft ground shadow
sh = Image.new("L", (N, N), 0); d = ImageDraw.Draw(sh)
d.ellipse((N*0.16, N*0.62, N*0.86, N*0.8), fill=120)
sh = sh.filter(ImageFilter.GaussianBlur(28 * S / 4))
img.paste(Image.new("RGBA", (N, N), (0, 0, 0, 255)), (0, 0), sh)
light = (-0.55, -0.75)  # from the upper left
def lump(cx, cy, r, rot, seed):
    rnd = random.Random(seed)
    k = 7
    pts = []
    for i in range(k):
        a = rot + i * 2 * math.pi / k + rnd.uniform(-0.25, 0.25)
        rr = r * rnd.uniform(0.78, 1.08)
        pts.append((cx + math.cos(a) * rr * 1.15, cy + math.sin(a) * rr * 0.82))
    top = (cx + rnd.uniform(-0.15, 0.1) * r, cy - r * rnd.uniform(0.2, 0.35))
    d = ImageDraw.Draw(img)
    for i in range(k):
        p1, p2 = pts[i], pts[(i + 1) % k]
        mx, my = (p1[0] + p2[0]) / 2 - cx, (p1[1] + p2[1]) / 2 - cy
        ln = math.hypot(mx, my) or 1
        lit = max(0.0, (mx * light[0] + my * light[1]) / ln)
        base = 18 + int(lit * 46) + rnd.randint(-4, 4)
        col = (base, base, base + 3, 255)
        d.polygon([top, p1, p2], fill=col)
        # crack lines across the facet
        if rnd.random() < 0.6:
            t = rnd.uniform(0.3, 0.7)
            q1 = (top[0] + (p1[0] - top[0]) * t, top[1] + (p1[1] - top[1]) * t)
            q2 = (top[0] + (p2[0] - top[0]) * (t + 0.1), top[1] + (p2[1] - top[1]) * (t + 0.1))
            d.line([q1, q2], fill=(8, 8, 9, 255), width=S * 2)
        # silvery sheen on lit edges
        if lit > 0.55:
            d.line([top, p1], fill=(118, 120, 128, 255), width=S * 2)
    d.polygon(pts, outline=(6, 6, 7, 255))
lump(N*0.40, N*0.56, N*0.17, 0.3, 1)
lump(N*0.62, N*0.60, N*0.13, 1.1, 2)
lump(N*0.52, N*0.42, N*0.12, 2.0, 3)
# a few ash specks
d = ImageDraw.Draw(img)
for i in range(40):
    x = random.uniform(N*0.25, N*0.78); y = random.uniform(N*0.62, N*0.74); r = random.uniform(1.5, 4) * S
    g = random.randint(95, 150); d.ellipse((x - r, y - r, x + r, y + r), fill=(g, g, g - 4, 200))
out = img.resize((256, 256), Image.LANCZOS)
dst = os.path.expanduser("~/mnt/LAST OF THE PRIMAL/Assets/_Project/Art/Icons/ICON_Charcoal.png")
out.save(dst); print("wrote", dst, out.size)
