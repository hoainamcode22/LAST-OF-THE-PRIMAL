"""
PRIMAL FRONTIER - hero-quality creature body (original design), first used for the Rift Tyrant.
Same signed-distance approach as dino_sdf.py but with real anatomy: skull with brow crests, nasal ridge, nostrils,
antorbital hollows, cheek flares and jaw muscles; separate upper / lower jaws with a mouth cavity and throat; neck
folds and dewlap; thigh / tail muscle (caudofemoralis); knees, tendons, ankle bones; three segmented toes with pads
and a dewclaw; two-fingered hands; dorsal scutes and scattered feature scales.
Also returns everything Blender needs to add the separate parts: tooth list, gum lines, tongue, eyes / eyelids, claws.
python3 dino_hero.py <species> <out_dir>      (writes <sid>_body.ply (game-res), <sid>_joints.json, <sid>_sdf.npz)
"""
import sys, os, json, math
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from dino_specs import SPECS
from dino_sdf import Field, frame, catmull, V, tolist
from sdf_lib import write_ply


def resample(pts, n):
    """n+1 points evenly spaced by arc length along a smooth curve through pts"""
    dense = np.array(catmull([np.asarray(p, np.float32) for p in pts], 16))
    seg = np.linalg.norm(np.diff(dense, axis=0), axis=1); s = np.concatenate([[0], np.cumsum(seg)])
    out = []
    for t in np.linspace(0, s[-1], n + 1):
        i = min(np.searchsorted(s, t, side="right") - 1, len(dense) - 2)
        u = (t - s[i]) / max(s[i + 1] - s[i], 1e-9); out.append(dense[i] * (1 - u) + dense[i + 1] * u)
    return out


def sample(F, P):
    """trilinear SDF lookup at world points P (n,3)"""
    q = (np.asarray(P, np.float32) - F.lo) / F.h
    i = np.clip(np.floor(q).astype(int), 0, np.array(F.n) - 2); f = q - i
    d = 0
    for dx in (0, 1):
        for dy in (0, 1):
            for dz in (0, 1):
                w = (f[:, 0] if dx else 1 - f[:, 0]) * (f[:, 1] if dy else 1 - f[:, 1]) * (f[:, 2] if dz else 1 - f[:, 2])
                d = d + w * F.d[i[:, 0] + dx, i[:, 1] + dy, i[:, 2] + dz]
    return d


def surface_along(F, a, b, steps=400):
    """first point from a (inside) towards b where the field crosses zero"""
    ts = np.linspace(0, 1, steps); P = a[None] + (b - a)[None] * ts[:, None]
    d = sample(F, P); k = np.where((d[:-1] < 0) & (d[1:] >= 0))[0]
    if len(k) == 0: return None
    k = k[0]; u = d[k] / (d[k] - d[k + 1] + 1e-9)
    return P[k] + (P[k + 1] - P[k]) * u


def loft(F, origin, R, prof, k, sub=False):
    """Swept rounded cross-section along local +y. prof: dict of arrays y, top, bot, w, n (squareness), taper
    (>0 narrower at the top, <0 narrower at the bottom). Approximate SDF, good near the surface where the profile
    changes slowly; the ends are closed by shrinking the profile to zero there."""
    y = np.asarray(prof["y"], np.float32)
    top = np.asarray(prof["top"], np.float32); bot = np.asarray(prof["bot"], np.float32); w = np.asarray(prof["w"], np.float32)
    n = np.asarray(prof.get("n", [2.4] * len(y)), np.float32); tp = np.asarray(prof.get("taper", [0.0] * len(y)), np.float32)
    corners = []
    for yy, t_, b_, w_ in zip(y, top, bot, w):
        for sx in (-w_, w_):
            for zz in (t_, b_): corners.append(origin + R @ np.array([sx, yy, zz], np.float32))
    corners = np.array(corners); m = k + F.h * 3
    def f(P):
        q = (P - origin) @ R; x, yy, z = q[..., 0], q[..., 1], q[..., 2]
        yc = np.clip(yy, y[0], y[-1])
        T = np.interp(yc, y, top); B = np.interp(yc, y, bot); W = np.interp(yc, y, w); N = np.interp(yc, y, n); TP = np.interp(yc, y, tp)
        flat = prof.get("flat")
        u = np.clip((z - B) / np.maximum(T - B, 1e-3), 0, 1)
        We = np.maximum(W * (1 - TP * (u - 0.5) * 2 * 0.5), 1e-3)
        if flat == "bottom": zc = B; a = np.maximum(T - B, 1e-3)          # widest at the bottom edge (lip line)
        elif flat == "top": zc = T; a = np.maximum(T - B, 1e-3)
        else: zc = (T + B) * 0.5; a = np.maximum((T - B) * 0.5, 1e-3)
        r = (np.abs(x) / We) ** N + (np.abs(z - zc) / a) ** N
        r = r ** (1 / N)
        d2 = (r - 1) * np.minimum(We, a)
        if flat == "bottom": cut = B - z
        elif flat == "top": cut = z - T
        else: cut = None
        if cut is not None:                                                 # smooth max with the flat plane (crisp lip edge)
            kk = 0.015; hq = np.clip(0.5 - 0.5 * (cut - d2) / kk, 0, 1)
            d2 = d2 * hq + cut * (1 - hq) + kk * hq * (1 - hq)
        dy = np.maximum(y[0] - yy, yy - y[-1])
        return np.where(dy > 0, np.sqrt(np.maximum(d2, 0) ** 2 + dy ** 2) + np.minimum(np.maximum(d2, dy), 0), d2)
    F.apply(f, corners.min(0) - m, corners.max(0) + m, k, sub)


def build_hero(sid, sp):
    hs = sp["hero"]; hd = sp["head"]; L = sp["length"]
    rng = np.random.default_rng(hs["teeth"]["seed"])
    tail, body, neck = hs["tail"], hs["body"], hs["neck"]
    J = {}
    axis = [V(0, p["y"], p["z"]) for p in tail + body + neck]
    radii = [V(p["w"], p["h"], p["drop"]) for p in tail + body + neck]
    # ---------------------------------------------------------------- head frame
    hp = V(*hd["pos"]); pitch = math.radians(hd.get("pitch", 0))
    hdir = V(0, -math.cos(pitch), math.sin(pitch))
    hup = np.cross(np.cross(hdir, V(0, 0, 1)), hdir); hup /= np.linalg.norm(hup)
    if hup[2] < 0: hup = -hup
    hx = np.cross(hdir, hup); hx = -hx if hx[0] < 0 else hx
    Rh = np.stack([hx, hdir, hup], 1)
    hl, hw, hh = hd["len"], hd["w"], hd["h"]
    def H(x, y, z): return hp + hx * x + hdir * y + hup * z
    zl = hs["lip"] * hh                                                   # lip line height (head local)
    # ---------------------------------------------------------------- joints
    J["pelvis"] = V(0, body[0]["y"], body[0]["z"])
    J["tail"] = resample([V(0, p["y"], p["z"]) for p in reversed(tail)], hs["tail_bones"])          # base -> tip
    J["spine"] = [V(0, p["y"], p["z"]) for p in body[1:]]
    chest_end = J["spine"][-1]
    ncurve = [chest_end] + [V(0, p["y"], p["z"]) for p in neck] + [hp]
    J["neck"] = resample(ncurve, hs["neck_bones"])[1:-1]
    J["head"] = hp; J["snout"] = H(0, hl, 0)
    J["jaw"] = H(0, hl * 0.17, zl - 0.02); J["chin"] = H(0, hl * 0.95, zl - hh * 0.2)

    # ---------------------------------------------------------------- field
    legs = hs.get("legs", sp["legs"])
    pts = axis + [J["snout"], H(0, hl, -hh), H(0, 0, hh)]
    for lg in legs.values():
        for k in ("hip", "knee", "ankle", "foot"):
            pts.append(V(lg[k][0], lg[k][1], lg[k][2])); pts.append(V(-lg[k][0], lg[k][1], lg[k][2]))
    pts = np.array(pts); pad = 0.45
    lo = pts.min(0) - pad; hi = pts.max(0) + pad; lo[2] = -0.05
    xr = max(abs(lg["knee"][0]) + lg["r"][0] for lg in legs.values()) + 0.2
    lo[0] = min(lo[0], -xr); hi[0] = max(hi[0], xr)
    F = Field(lo, hi, hs["voxel"]); h = F.h
    k0 = hs["blend"]
    print("grid", tuple(F.n), f"{np.prod(F.n) / 1e6:.0f}M voxels", flush=True)

    # ---------------------------------------------------------------- trunk
    samples = catmull([np.concatenate([a, r]) for a, r in zip(axis, radii)], 10)
    for i, s in enumerate(samples):
        c = s[:3].copy(); w, hh2, drop = s[3], s[4], s[5]
        nxt = samples[min(i + 1, len(samples) - 1)][:3]; prv = samples[max(i - 1, 0)][:3]
        d = nxt - prv; d = d / (np.linalg.norm(d) + 1e-9)
        if d[1] > 0: d = -d
        R = frame(d); c[2] -= drop
        seg = max(np.linalg.norm(nxt - prv) * 0.9, min(w, hh2) * 0.7)
        F.ellipsoid(c, (w, seg, hh2 + drop * 0.5), R, k=k0)
        if i % 2 == 0 and w > 0.08:                                         # vertebral ridge along the back
            F.ellipsoid(c + R[:, 2] * (hh2 * 0.9 + drop * 0.3), (w * 0.2, seg * 1.1, hh2 * 0.16), R, k=k0 * 0.6)
    # chest / pectoral mass and belly
    ch = J["spine"][-1]
    F.ellipsoid(ch + V(0, -0.1, -0.5), (0.5, 0.5, 0.45), None, k=k0 * 2.5)

    # ---------------------------------------------------------------- neck: dewlap + folds
    for i, t in enumerate(np.linspace(0.15, 0.85, 3)):
        c = ncurve[1] * (1 - t) + hp * t
        F.ellipsoid(c + V(0, 0.05, -0.36 + 0.08 * t), (0.28, 0.22, 0.2), None, k=k0 * 1.5)
    for i in range(6):                                                     # ventral / lateral skin folds
        t = 0.1 + i * 0.15; c = ncurve[1] * (1 - t) + hp * t
        dn = hp - ncurve[1]; dn /= np.linalg.norm(dn); Rn = frame(-dn if dn[1] > 0 else dn)
        for sx in (1, -1):
            F.ellipsoid(c + V(sx * 0.3, 0, -0.26), (0.07, 0.03, 0.15), Rn, k=0.04)
        F.ellipsoid(c + V(0, 0, -0.46 + 0.06 * i), (0.22, 0.028, 0.04), Rn, k=0.03)

    # ---------------------------------------------------------------- head: lofted skull and mandible
    def E(c, r, k=0.04, sub=False): F.ellipsoid(H(*c), r, Rh, k=k, sub=sub)
    def top_at(x, y, zmax):
        p = surface_along(F, H(x, y, -0.05), H(x, y, zmax), 300)
        return float((p - hp) @ hup) if p is not None else None
    def side_at(y, z, s):
        p = surface_along(F, H(0.02 * s, y, z), H(s * hw * 1.8, y, z), 300)
        return float((p - hp) @ hx) if p is not None else None
    S = hs["skull"]; M = hs["mandible"]
    ysk = np.array(S["y"]) * hl
    loft(F, hp, Rh, dict(y=ysk, top=np.array(S["top"]) * hh, bot=np.array(S["bot"]) * hh,
                         w=np.array(S["w"]) * hw, n=S["n"], taper=S["taper"], flat="bottom"), k=0.1)
    ym = np.array(M["y"]) * hl
    loft(F, hp, Rh, dict(y=ym, top=np.array(M["top"]) * hh, bot=np.array(M["bot"]) * hh,
                         w=np.array(M["w"]) * hw, n=M["n"], taper=M["taper"], flat="top"), k=0.08)
    def ulip(y): return float(np.interp(y, ysk, np.array(S["w"]) * hw)) * (1 + np.interp(y, ysk, S["taper"]) * 0.5)
    def llip(y): return float(np.interp(y, ym, np.array(M["w"]) * hw)) * (1 - np.interp(y, ym, M["taper"]) * 0.5)
    # jaw muscles behind the eye and at the hinge, throat into the neck
    for s in (1, -1):
        E((s * hw * 0.62, hl * 0.12, hh * 0.18), (hw * 0.3, hl * 0.13, hh * 0.3), k=0.1)
        E((s * hw * 0.5, hl * 0.16, zl - hh * 0.36), (hw * 0.24, hl * 0.13, hh * 0.24), k=0.1)
    E((0, hl * 0.04, zl - hh * 0.42), (hw * 0.45, hl * 0.24, hh * 0.26), k=0.14)
    # nasal rugosities along the snout ridge
    for i, t in enumerate(np.linspace(0.36, 0.86, 12)):
        y = hl * t
        for sx in ((0,) if i % 3 else (-1, 1)):
            x = sx * 0.035 + (rng.random() - 0.5) * 0.016; zt = top_at(x, y, hh * 1.2)
            if zt is None: continue
            r = 0.02 + 0.01 * rng.random(); E((x, y, zt - 0.004), (r, r * 1.5, r * 0.6), k=0.018)
    # nostrils, antorbital hollows, cheek ridge
    for s in (1, -1):
        y = hl * 0.9; z = hh * 0.02; xs = side_at(y, z, s)
        if xs is not None: E((xs - s * 0.008, y, z), (0.028, 0.05, 0.022), k=0.012, sub=True)
        y = hl * 0.55; z = hh * 0.02; xs = side_at(y, z, s)
        if xs is not None: E((xs + s * 0.03, y, z), (0.045, hl * 0.11, hh * 0.09), k=0.035, sub=True)
        for t in np.linspace(0.2, 0.45, 4):
            y = hl * t; z = -hh * 0.12; xs = side_at(y, z, s)
            if xs is not None: E((xs - s * 0.012, y, z), (0.03, 0.06, 0.028), k=0.03)
    # brow crests ("rift" crests): ridge on the skull edge from above the eye backwards, with small spikes
    cr = hs["crest"]; pts_c = []
    for s in (1, -1):
        row = []
        for t in np.linspace(0, 1, 9):
            y = hl * (0.4 - 0.3 * t); zt0 = float(np.interp(y, ysk, np.array(S["top"]) * hh))
            x = s * float(np.interp(y, ysk, np.array(S["w"]) * hw)) * 0.62
            zt = top_at(x, y, hh * 1.4)
            if zt is None: continue
            row.append((x, y, zt)); E((x, y, zt + 0.012), (0.045 - 0.012 * t, 0.06, 0.04), k=0.03)
        for i in range(cr["spikes"]):
            if not row: break
            x, y, zt = row[min(len(row) - 1, int((i + 0.5) / cr["spikes"] * len(row)))]
            base = H(x, y, zt + 0.02); t = i / max(1, cr["spikes"] - 1)
            tip = base + (hup * 0.85 + hx * s * 0.35 - hdir * 0.4) * cr["h"] * (1.0 - 0.45 * abs(t - 0.25))
            F.cone(base, tip, 0.026, 0.004, k=0.018)
        if row:
            x, y, zt = row[0]; E((x * 1.05, y + hl * 0.02, zt - 0.02), (0.05, 0.06, 0.035), k=0.03)   # lacrimal horn above the eye
        pts_c.append(row)
    J["crest_rows"] = [[H(*p).tolist() for p in r] for r in pts_c]
    # ---------------------------------------------------------------- mouth: lip slit, cavity, throat
    slit_t = max(h * 1.05, 0.016)                        # at least two voxels: no skin bridges between the jaws
    F.box(H(0, hl * 0.62, zl), (hw * 1.5, hl * 0.42, slit_t), h * 0.4, Rh, k=h * 0.4, sub=True)
    cav = []
    for t in np.linspace(0, 1, 16):
        y = hl * (0.18 + 0.76 * t)
        rx = 0.72 * min(ulip(y), llip(y)); rz = 0.075 * (1 - t) + 0.03 * t
        c = (0, y, zl); cav.append([list(H(*c)), rx, hl * 0.06, rz])
        E(c, (rx, hl * 0.06, rz), k=0.02, sub=True)
    for t in np.linspace(0, 1, 5):                                          # throat
        c = (0, hl * (0.18 - 0.2 * t), zl - 0.02 * t); r = 0.07 - 0.03 * t
        cav.append([list(H(*c)), r, hl * 0.05, r * 0.9]); E(c, (r, hl * 0.05, r * 0.9), k=0.02, sub=True)
    J["mouth_cavity"] = cav

    # ---------------------------------------------------------------- legs
    J["legs"] = {}; J["toes"] = {}; J["dewclaw"] = {}; J["fingers"] = {}
    for name, lg in legs.items():
        for side, sx in (("L", 1), ("R", -1)):
            key = f"{name}_{side}"
            hip, knee, ank, foot = [V(lg[k][0] * sx, lg[k][1], lg[k][2]) for k in ("hip", "knee", "ankle", "foot")]
            r = lg["r"]; st = lg["stance"]
            if st == "hand":
                F.ellipsoid(hip * 0.55 + knee * 0.45, (r[0] * 0.9, r[0] * 0.9, np.linalg.norm(knee - hip) * 0.6), None, k=0.05)   # biceps
                F.cone(hip, knee, r[0] * 0.85, r[1], k=0.05); F.cone(knee, ank, r[1], r[2], k=0.03)
                F.ellipsoid(knee * 0.6 + ank * 0.4, (r[1] * 1.1, r[1] * 1.1, np.linalg.norm(ank - knee) * 0.4), None, k=0.03)
                F.cone(ank, foot, r[2], r[3], k=0.02)
                dirh = foot - ank; dirh /= np.linalg.norm(dirh)
                fingers = []
                for fi in range(hs["fingers"]):
                    spread = (fi - (hs["fingers"] - 1) / 2) * 0.5
                    d = dirh * math.cos(spread) + V(sx, 0, 0) * math.sin(spread) * 0.6; d /= np.linalg.norm(d)
                    ln = lg["toe_len"] * (1.25 if fi == 0 else 1.0) * 1.6
                    p0 = foot + V(sx * (fi - 0.5) * 0.025, 0, 0); p1 = p0 + d * ln * 0.5; p2 = p1 + (d + V(0, 0, -0.5)) / np.linalg.norm(d + V(0, 0, -0.5)) * ln * 0.5
                    F.cone(p0, p1, r[3] * 0.55, r[3] * 0.42, k=0.012); F.cone(p1, p2, r[3] * 0.42, r[3] * 0.3, k=0.008)
                    F.ellipsoid(p1, (r[3] * 0.5,) * 3, None, k=0.008)
                    fingers.append([p0.tolist(), p1.tolist(), p2.tolist()])
                J["fingers"][key] = fingers
                J["legs"][key] = dict(hip=hip.tolist(), knee=knee.tolist(), ankle=ank.tolist(), foot=foot.tolist(), toe=fingers[0][2], stance=st)
                continue
            # thigh: one long muscle mass along the femur that melts into the flank, knee kept slim
            tl = np.linalg.norm(knee - hip); df = (knee - hip) / tl; Rf = frame(df, V(0, -1, 0))
            F.ellipsoid(hip + df * tl * 0.42 + V(-sx * 0.05, 0.03, 0), (r[0] * 0.74, tl * 0.64, r[0] * 1.0), Rf, k=k0 * 2.6)
            F.ellipsoid(hip + df * tl * 0.2 + V(-sx * 0.12, 0.28, 0.08), (r[0] * 0.6, tl * 0.45, r[0] * 0.8), Rf, k=k0 * 2.6)   # hamstring into the tail
            F.cone(hip, knee, r[0] * 0.62, r[1] * 0.95, k=k0 * 1.8)
            F.ellipsoid(knee + V(0, -0.06, 0.06), (r[1] * 0.7, r[1] * 0.75, r[1] * 0.75), None, k=k0 * 2.0)     # knee
            # shin: tibia with calf (gastrocnemius) behind
            F.cone(knee, ank, r[1] * 0.85, r[2], k=k0 * 1.2)
            sl = np.linalg.norm(ank - knee); dshin = (ank - knee) / sl
            F.ellipsoid(knee * 0.7 + ank * 0.3 + V(0, 0.07, 0), (r[1] * 0.78, sl * 0.36, r[1] * 0.9), frame(dshin, V(0, -1, 0)), k=k0 * 1.5)
            F.cone(knee + V(0, -0.1, -0.05), ank + V(0, -0.05, 0.06), 0.045, 0.03, k=0.04)                 # tibial crest
            # ankle + metatarsus with tendons
            for s2 in (1, -1): F.ellipsoid(ank + V(s2 * r[2] * 0.7, 0, 0), (0.06, 0.07, 0.07), None, k=0.03)
            F.cone(ank, foot + V(0, 0, r[3]), r[2], r[3], k=0.03)
            dm = (foot - ank); dm /= np.linalg.norm(dm); fwdm = np.cross(V(sx, 0, 0), dm) * sx
            for s2 in (-0.4, 0.4):
                F.cone(ank + V(s2 * r[2] * 0.6, 0, 0) - fwdm * r[2] * 0.75, foot + V(s2 * r[3] * 0.6, 0, r[3] * 1.2) - fwdm * r[3] * 0.7, 0.028, 0.022, k=0.02)
            # toes: three segmented toes with knuckles and pads
            toes = []; toe_tips = []
            base = foot + V(0, 0, r[3] * 0.8)
            F.ellipsoid(base + V(0, -0.06, -r[3] * 0.25), (r[3] * 1.5, r[3] * 1.3, r[3] * 0.75), None, k=0.04)   # metatarsal pad
            for i in range(hs["toes"]):
                a = (i - (hs["toes"] - 1) / 2) * 0.36
                d = V(math.sin(a) * sx, -math.cos(a), 0)
                ln = lg["toe_len"] * (0.8 if i == 1 else 0.66)
                p = [base + V(sx * (i - 1) * r[3] * 0.55, -0.02, -r[3] * 0.1)]
                for kseg, frac in enumerate((0.4, 0.33, 0.27)):
                    nxtp = p[-1] + d * ln * frac - V(0, 0, r[3] * (0.35 if kseg == 0 else 0.15))
                    nxtp[2] = max(nxtp[2], r[3] * 0.4 * (1 - 0.25 * kseg)); p.append(nxtp)
                rr = r[3] * 0.82
                for kseg in range(3):
                    F.cone(p[kseg], p[kseg + 1], rr * (1 - 0.2 * kseg), rr * (1 - 0.2 * (kseg + 1)), k=0.02)
                    F.ellipsoid(p[kseg + 1], (rr * (0.92 - 0.18 * kseg),) * 3, None, k=0.012)                    # knuckle
                    pad = (p[kseg] + p[kseg + 1]) * 0.5; pad = pad.copy(); pad[2] = rr * 0.5 * (1 - 0.2 * kseg)
                    F.ellipsoid(pad, (rr * 0.95, np.linalg.norm(p[kseg + 1] - p[kseg]) * 0.5, rr * 0.5), None, k=0.015)
                toes.append([q.tolist() for q in p]); toe_tips.append(p[-1].tolist())
            J["toes"][key] = toes
            if hs.get("dewclaw"):
                mt = ank * 0.25 + (foot + V(0, 0, r[3])) * 0.75                       # on the back / inner side of the metatarsus
                dc0 = mt + V(-sx * r[2] * 0.55, r[2] * 0.6, 0); dc1 = dc0 + V(-sx * 0.02, -0.02, -0.11)
                F.cone(dc0 + V(sx * 0.03, -0.03, 0.02), dc1, r[3] * 0.45, r[3] * 0.28, k=0.03); J["dewclaw"][key] = [dc0.tolist(), dc1.tolist()]
            ctr = np.mean(np.array(toe_tips), 0)
            J["legs"][key] = dict(hip=hip.tolist(), knee=knee.tolist(), ankle=ank.tolist(), foot=foot.tolist(), toe=ctr.tolist(), toe_tips=toe_tips, stance=st)
            # hip blend and the tail-pulling muscle (caudofemoralis) from the thigh into the tail base
            F.ellipsoid(hip - V(sx * r[0] * 0.45, -0.05, -r[0] * 0.25), (r[0] * 0.75, r[0] * 1.2, r[0] * 0.9), None, k=k0 * 2.5)
            tb = min(samples, key=lambda q: abs(q[1] - 2.3))
            F.cone(hip * 0.7 + knee * 0.3 + V(0, 0.2, 0), V(sx * tb[3] * 0.45, 2.3, tb[2] - tb[4] * 0.25), r[0] * 0.55, tb[3] * 0.4, k=k0)
            F.ellipsoid(hip + V(-sx * 0.05, -0.1, 0.35), (0.25, 0.55, 0.14), None, k=0.08)       # ilium ridge

    # ---------------------------------------------------------------- dorsal scutes and feature scales
    sc = hs["scutes"]
    ys = np.arange(hp[1] + 0.35, 4.6, sc["spacing"])
    for j, y in enumerate(ys):
        s = min(samples, key=lambda q: abs(q[1] - y)); w, hh2 = s[3], s[4]
        if w < 0.1: continue
        size = sc["size"] * (1.25 if -1.8 < y < 1.0 else 1.0) * (0.75 if y > 2.5 else 1.0)
        for side in (1, -1):
            ang = side * 0.28
            p = V(math.sin(ang) * w * 0.92, y + (0.05 if side > 0 else -0.05), s[2] - s[5] + math.cos(ang) * hh2 * 0.97)
            n = V(math.sin(ang), 0, math.cos(ang))
            F.cone(p - n * size * 0.4, p + n * size * (0.9 if y < 2.5 else 1.4), size, size * 0.3, k=size * 0.5)
    fs = []
    tries = 0
    while len(fs) < hs["feature_scales"] and tries < hs["feature_scales"] * 20:
        tries += 1
        s = samples[rng.integers(5, len(samples) - 4)]
        w, hh2 = s[3], s[4]
        if w < 0.15: continue
        ang = rng.uniform(-2.1, 2.1)                                          # upper and side surface only
        p = V(math.sin(ang) * w, s[1] + rng.uniform(-0.1, 0.1), s[2] - s[5] + math.cos(ang) * hh2)
        n = V(math.sin(ang) * hh2, 0, math.cos(ang) * w); n /= np.linalg.norm(n)
        r = rng.uniform(0.028, 0.055) * (1.2 if abs(ang) < 0.8 else 1.0)
        fs.append((p, n, r))
    for p, n, r in fs: F.ellipsoid(p + n * r * 0.1, (r, r, r * 0.55), frame(V(0, -1, 0), n) if abs(n[2]) < 0.99 else None, k=r * 0.6)

    # ---------------------------------------------------------------- eyes: find the skull side, carve the socket
    er = hs["eye_r"]; ey, ez = 0.46, 0.30
    a = H(0.05, ey, ez); b = H(hw * 1.6, ey, ez)
    sp_ = surface_along(F, a, b)
    ex = (float((sp_ - hp) @ hx) if sp_ is not None else 0.3) - er * 0.55
    J["eyes"] = [H(s * ex, ey, ez).tolist() for s in (1, -1)]
    for s in (1, -1): E((s * ex, ey, ez), (er * 1.05, er * 1.12, er * 0.98), k=er * 0.35, sub=True)
    J["eye_r"] = er; J["eyelid"] = dict(r=er * 1.14)

    # ---------------------------------------------------------------- flat soles
    F.apply(lambda P: P[..., 2], np.array([lo[0], lo[1], lo[2]]), np.array([hi[0], hi[1], h * 2]), k=h * 0.5, sub=True)

    # ---------------------------------------------------------------- parts for Blender (head local -> world)
    T = hs["teeth"]; teeth = []
    def add_row(n, y0, y1, upper, lens, prefix):
        for i in range(n):
            t = (i + 0.5) / n; y = hl * (y0 + (y1 - y0) * t)
            lipw = ulip(y) if upper else llip(y)
            ln = lens(t) * (0.9 + 0.2 * rng.random())
            for s in (1, -1):
                x = s * (lipw - ln * 0.12 - 0.014)                                   # root fully inside the jaw wall
                zb = float(np.interp(y, ysk, np.array(S["bot"]) * hh)) if upper else float(np.interp(y, ym, np.array(M["top"]) * hh))
                root = H(x, y, zb + (0.015 + ln * 0.2 if upper else -0.015 - ln * 0.2))
                d = hup * (-1 if upper else 1) + (-hdir) * (0.28 if upper else 0.18) + hx * s * (0.13 if upper else -0.03)
                d /= np.linalg.norm(d)
                teeth.append(dict(root=root.tolist(), dir=d.tolist(), len=ln, side=s, upper=upper, fwd=hdir.tolist(), out=(hx * s).tolist(), row=prefix))
    add_row(T["premax"], 0.9, 0.985, True, lambda t: T["premax_len"], "premax")
    add_row(T["maxilla"], 0.36, 0.88, True, lambda t: T["max_len"] * (0.55 + 0.45 * math.sin(math.pi * min(1, (1 - t) * 1.25))), "maxilla")
    add_row(T["dentary"], 0.3, 0.95, False, lambda t: T["lower_len"] * (0.55 + 0.45 * math.sin(math.pi * min(1, (1 - t) * 1.2))), "dentary")
    J["teeth"] = teeth
    # gum lines (along the tooth roots, inside the lips)
    gums = []
    for upper, y0, y1 in ((True, 0.34, 0.985), (False, 0.28, 0.955)):
        for s in (1, -1):
            line = []
            for t in np.linspace(0, 1, 24):
                y = hl * (y0 + (y1 - y0) * t)
                lipw = ulip(y) if upper else llip(y)
                if t > 0.93: lipw *= (1 - (t - 0.93) / 0.07 * 0.85)
                line.append(H(s * (lipw - 0.05), y, zl + (0.004 if upper else -0.004)).tolist())
            gums.append(dict(upper=upper, side=s, pts=line, r=0.02))
    J["gums"] = gums
    tg = hs["tongue"]
    J["tongue"] = dict(pts=[H(0, hl * (0.16 + 0.6 * t), zl - 0.055 + 0.025 * t).tolist() for t in np.linspace(0, 1, 8)],
                       w=tg["w"], t=tg["t"], len=tg["len"])
    J["head_frame"] = dict(pos=hp.tolist(), x=hx.tolist(), fwd=hdir.tolist(), up=hup.tolist(), len=hl, w=hw, h=hh, lip=zl)
    J["horns"] = []
    return F, J


if __name__ == "__main__":
    from skimage.measure import marching_cubes
    import fast_simplification, time
    sid, out = sys.argv[1], sys.argv[2]; target = int(sys.argv[3]) if len(sys.argv) > 3 else 52000
    os.makedirs(out, exist_ok=True); t0 = time.time()
    F, J = build_hero(sid, SPECS[sid]); t1 = time.time(); print(f"sdf {t1 - t0:.0f}s", flush=True)
    np.savez(os.path.join(out, f"{sid}_sdf.npz"), d=F.d.astype(np.float16), lo=F.lo, h=np.float32(F.h))
    verts, faces, _, _ = marching_cubes(F.d, level=0.0, spacing=(F.h, F.h, F.h)); verts = (verts + F.lo).astype(np.float32)
    t2 = time.time(); print(f"mc {t2 - t1:.0f}s verts {len(verts)} faces {len(faces)}", flush=True)
    faces = faces[:, ::-1].copy()                                        # outward winding
    vs, fs = fast_simplification.simplify(verts, faces.astype(np.int64), target_reduction=1 - target / len(faces), agg=6)
    print(f"simplified {len(fs)} faces in {time.time() - t2:.0f}s", flush=True)
    write_ply(os.path.join(out, f"{sid}_body.ply"), vs.astype(np.float32), fs[:, ::-1].astype(np.int32))   # write_ply flips back
    json.dump(tolist(J), open(os.path.join(out, f"{sid}_joints.json"), "w"), indent=1)
    print("done", f"{time.time() - t0:.0f}s")
