"""sphere-traced preview of a saved SDF grid (<sid>_sdf.npz): python3 sdfview.py npz out.png view [...]"""
import sys, numpy as np
from PIL import Image

class Grid:
    def __init__(self, path):
        z = np.load(path); self.d = z["d"].astype(np.float32); self.lo = z["lo"].astype(np.float32); self.h = float(z["h"])
        self.n = np.array(self.d.shape)
    def __call__(self, P):
        q = (P - self.lo) / self.h
        out = np.any((q < 0) | (q > self.n - 1.001), axis=-1)
        q = np.clip(q, 0, self.n - 1.001); i = np.floor(q).astype(np.int32); f = q - i
        d = np.zeros(len(P), np.float32)
        for dx in (0, 1):
            for dy in (0, 1):
                for dz in (0, 1):
                    w = (f[:, 0] if dx else 1 - f[:, 0]) * (f[:, 1] if dy else 1 - f[:, 1]) * (f[:, 2] if dz else 1 - f[:, 2])
                    d += w * self.d[i[:, 0] + dx, i[:, 1] + dy, i[:, 2] + dz]
        d[out] = np.maximum(d[out], self.h * 2)
        return d

def render(G, eye, target, fov=35, W=900, H=600, up=(0, 0, 1)):
    eye = np.array(eye, np.float32); target = np.array(target, np.float32)
    f = target - eye; f /= np.linalg.norm(f); r = np.cross(f, up); r /= np.linalg.norm(r); u = np.cross(r, f)
    t = np.tan(np.radians(fov) / 2)
    xs = (np.arange(W) + 0.5) / W * 2 - 1; ys = 1 - (np.arange(H) + 0.5) / H * 2
    X, Y = np.meshgrid(xs * t * W / H, ys * t)
    D = f[None] + X.reshape(-1, 1) * r[None] + Y.reshape(-1, 1) * u[None]; D /= np.linalg.norm(D, axis=1, keepdims=True)
    # start at the grid box
    lo = G.lo; hi = G.lo + (G.n - 1) * G.h
    inv = 1 / np.where(np.abs(D) < 1e-9, 1e-9, D)
    t0 = np.max(np.minimum((lo - eye) * inv, (hi - eye) * inv), axis=1); t1 = np.min(np.maximum((lo - eye) * inv, (hi - eye) * inv), axis=1)
    T = np.maximum(t0, 0); alive = t1 > T; hit = np.zeros(len(D), bool)
    for it in range(400):
        idx = np.where(alive & ~hit)[0]
        if len(idx) == 0: break
        P = eye + D[idx] * T[idx, None]; d = G(P)
        h = np.abs(d) < G.h * 0.25
        hit[idx[h]] = True
        st = np.where(d > 0, np.maximum(d * 0.55, G.h * 0.2), d * 0.8)     # overshoot inside: step back
        T[idx[~h]] += st[~h]
        alive[idx[T[idx] > t1[idx]]] = False
    img = np.zeros((len(D), 3), np.float32) + np.array([0.16, 0.17, 0.19])
    hi_ = np.where(hit)[0]; P = eye + D[hi_] * T[hi_, None]
    e = G.h
    n = np.stack([G(P + [e, 0, 0]) - G(P - [e, 0, 0]), G(P + [0, e, 0]) - G(P - [0, e, 0]), G(P + [0, 0, e]) - G(P - [0, 0, e])], 1)
    n /= np.linalg.norm(n, axis=1, keepdims=True) + 1e-9
    L1 = np.array([0.5, -0.6, 0.65]); L1 /= np.linalg.norm(L1); L2 = np.array([-0.6, 0.3, 0.3]); L2 /= np.linalg.norm(L2)
    ao = np.ones(len(P), np.float32)
    for k, s in enumerate((0.02, 0.05, 0.1, 0.2)):
        ao -= np.clip(s - G(P + n * s), 0, None) / s * (0.5 ** k) * 0.35
    ao = np.clip(ao, 0.2, 1)
    dif = np.clip(n @ L1, 0, 1) * 0.85 + np.clip(n @ L2, 0, 1) * 0.25 + 0.18
    spec = np.clip((n @ ((L1 - D[hi_]) / np.linalg.norm(L1 - D[hi_], axis=1, keepdims=True)).T).diagonal() if False else 0, 0, 1)
    img[hi_] = (dif * ao)[:, None] * np.array([0.86, 0.8, 0.7])
    return (np.clip(img.reshape(H, W, 3), 0, 1) ** (1 / 1.6) * 255).astype(np.uint8)

if __name__ == "__main__":
    import json
    G = Grid(sys.argv[1]); out = sys.argv[2]; J = json.load(open(sys.argv[3]))
    hp = np.array(J["head_frame"]["pos"]); fwd = np.array(J["head_frame"]["fwd"]); hc = hp + fwd * 0.75
    views = [
        ((10, 1.5, 3.2), (0, 0.8, 2.6), 60, 1200, 600),                       # full side
        ((2.2, -5.4, 5.4), hc, 38, 700, 520),                                 # head 3/4 front
        ((2.6, hc[1], hc[2] + 0.2), hc, 40, 700, 520),                        # head side
        ((3.2, -2.5, 1.2), (0.5, 0.0, 0.6), 45, 700, 520),                     # leg / foot
    ]
    ims = [render(G, e, t, fov, W, H) for e, t, fov, W, H in views]
    canvas = Image.new("RGB", (1400, 600 + 520 * 2), (20, 20, 20))
    canvas.paste(Image.fromarray(ims[0]), (0, 0))
    canvas.paste(Image.fromarray(ims[1]), (0, 600)); canvas.paste(Image.fromarray(ims[2]), (700, 600)); canvas.paste(Image.fromarray(ims[3]), (0, 1120))
    canvas.save(out)
