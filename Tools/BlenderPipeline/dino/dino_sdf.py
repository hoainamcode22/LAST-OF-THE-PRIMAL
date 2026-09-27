"""
PRIMAL FRONTIER - dinosaur body from parameters: signed distance field (smooth unions of ellipsoids / round cones
along the skeleton) -> marching cubes -> PLY, plus the skeleton joints as JSON for the Blender rig.
Runs on the device VM (numpy + scikit-image).   python3 dino_sdf.py <species> <out_dir> [voxels_on_long_axis]
"""
import sys, os, json, math
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from dino_specs import SPECS
from sdf_lib import rot_from_x, smin, ssub, write_ply

V = lambda *a: np.array(a, np.float32)

class Field:
    """sparse evaluation: every primitive only touches the voxels inside its own bounding box"""
    def __init__(self, lo, hi, h):
        self.lo = np.array(lo, np.float32); self.h = h
        self.n = np.ceil((np.array(hi, np.float32) - self.lo) / h).astype(int) + 1
        self.d = np.full(tuple(self.n), 1.0, np.float32)
    def _block(self, bmin, bmax):
        i0 = np.clip(np.floor((bmin - self.lo) / self.h).astype(int), 0, self.n - 1)
        i1 = np.clip(np.ceil((bmax - self.lo) / self.h).astype(int) + 1, 1, self.n)
        if np.any(i1 <= i0): return None
        axes = [self.lo[k] + np.arange(i0[k], i1[k], dtype=np.float32) * self.h for k in range(3)]
        X, Y, Z = np.meshgrid(*axes, indexing="ij")
        return (slice(i0[0], i1[0]), slice(i0[1], i1[1]), slice(i0[2], i1[2])), np.stack([X, Y, Z], -1)
    def apply(self, fn, bmin, bmax, k, sub=False):
        b = self._block(np.asarray(bmin, np.float32), np.asarray(bmax, np.float32))
        if b is None: return
        sl, P = b; v = fn(P).astype(np.float32)
        self.d[sl] = ssub(self.d[sl], v, k) if sub else smin(self.d[sl], v, k)

    # ---- primitives
    def ellipsoid(self, c, r, R=None, k=0.05, sub=False):
        c = np.asarray(c, np.float32); r = np.maximum(np.asarray(r, np.float32), 1e-3); R = np.eye(3, dtype=np.float32) if R is None else np.asarray(R, np.float32)
        m = float(r.max()) + k + self.h * 2
        def f(P):
            q = (P - c) @ R
            k0 = np.linalg.norm(q / r, axis=-1); k1 = np.linalg.norm(q / (r * r), axis=-1)
            return k0 * (k0 - 1.0) / np.maximum(k1, 1e-9)
        self.apply(f, c - m, c + m, k, sub)
    def cone(self, a, b, r1, r2, k=0.03, sub=False):
        a = np.asarray(a, np.float32); b = np.asarray(b, np.float32)
        m = max(r1, r2) + k + self.h * 2
        def f(P):
            ba = b - a; l2 = float(ba @ ba) + 1e-9; rr = r1 - r2; a2 = l2 - rr * rr; il2 = 1.0 / l2
            pa = P - a; y = pa @ ba; z = y - l2
            x2v = pa * l2 - y[..., None] * ba; x2 = np.einsum('...i,...i->...', x2v, x2v)
            y2 = y * y * l2; z2 = z * z * l2; kk = np.sign(rr) * rr * rr * x2
            return np.where(np.sign(z) * a2 * z2 > kk, np.sqrt(x2 + z2) * il2 - r2,
                   np.where(np.sign(y) * a2 * y2 < kk, np.sqrt(x2 + y2) * il2 - r1, (np.sqrt(np.maximum(x2 * a2 * il2, 0)) + y * rr) * il2 - r1))
        self.apply(f, np.minimum(a, b) - m, np.maximum(a, b) + m, k, sub)
    def box(self, c, half, rad, R=None, k=0.02, sub=False):
        c = np.asarray(c, np.float32); half = np.asarray(half, np.float32); R = np.eye(3, dtype=np.float32) if R is None else np.asarray(R, np.float32)
        m = float(np.linalg.norm(half)) + k + self.h * 2
        def f(P):
            q = np.abs((P - c) @ R) - half + rad
            return np.linalg.norm(np.maximum(q, 0), axis=-1) + np.minimum(np.max(q, axis=-1), 0) - rad
        self.apply(f, c - m, c + m, k, sub)

def frame(fwd, up_hint=(0, 0, 1)):
    """columns: X lateral(left), Y forward, Z up  for a part pointing along fwd"""
    f = np.asarray(fwd, np.float32); f /= np.linalg.norm(f)
    up = np.asarray(up_hint, np.float32); x = np.cross(f, up); x /= np.linalg.norm(x) + 1e-9
    z = np.cross(x, f)
    return np.stack([x, f, z], 1)

def catmull(pts, n):
    """smooth resample of a polyline (list of np arrays incl. extra values)"""
    P = [pts[0]] + pts + [pts[-1]]; out = []
    for i in range(1, len(P) - 2):
        p0, p1, p2, p3 = P[i - 1], P[i], P[i + 1], P[i + 2]
        for s in range(n):
            t = s / n; t2 = t * t; t3 = t2 * t
            out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3))
    out.append(pts[-1]); return out

def build(sp, voxels=260):
    L = sp["length"]; kind = sp["kind"]
    tail, body, neck, hd = sp["tail"], sp["body"], sp["neck"], sp["head"]
    # --- skeleton joints (world, Blender space)
    J = {}
    axis = [V(0, p["y"], p["z"]) for p in tail] + [V(0, p["y"], p["z"]) for p in body] + [V(0, p["y"], p["z"]) for p in neck]
    radii = [V(p["w"], p["h"], p["drop"]) for p in tail + body + neck]
    J["pelvis"] = V(0, body[0]["y"], body[0]["z"])
    J["tail"] = [V(0, p["y"], p["z"]) for p in reversed(tail)]                       # base -> tip
    J["spine"] = [V(0, p["y"], p["z"]) for p in body[1:]]
    J["neck"] = [V(0, p["y"], p["z"]) for p in neck]
    hp = V(*hd["pos"]); pitch = math.radians(hd.get("pitch", 0))
    hdir = V(0, -math.cos(pitch), math.sin(pitch)); hup = V(0, math.sin(pitch), math.cos(pitch)) * np.sign(math.cos(pitch))
    hup = np.cross(np.cross(hdir, V(0, 0, 1)), hdir); hup /= np.linalg.norm(hup)
    if hup[2] < 0: hup = -hup
    hx = np.cross(hdir, hup); hx = -hx if hx[0] < 0 else hx                       # +X left
    def H(a):  # head-local (x lateral, fwd, up) -> world
        return hp + hx * a[0] + hdir * a[1] + hup * a[2]
    hl, hw, hh = hd["len"], hd["w"], hd["h"]
    J["head"] = hp; J["snout"] = H((0, hl, 0))
    J["jaw"] = H((0, hl * 0.22, -hh * 0.35)); J["chin"] = H((0, hl * 0.9, -hh * 0.55))

    # --- field bounds
    pts = axis + [J["snout"], J["chin"]]
    legs = sp["legs"]
    for lg in legs.values():
        for k in ("hip", "knee", "ankle", "foot", "shoulder", "elbow", "wrist", "knuckle", "tip", "root"):
            if k in lg: pts.append(V(*[(lg[k][0]), lg[k][1], lg[k][2]])); pts.append(V(-lg[k][0], lg[k][1], lg[k][2]))
    pts = np.array(pts); pad = max(0.15, L * 0.12); lo = pts.min(0) - pad; hi = pts.max(0) + pad
    if sp.get("sail"): hi[2] += sp["sail"]["height"]
    lo[2] = max(lo[2], -0.05) if kind != "swimmer" else lo[2]
    h = float((hi - lo).max()) / voxels
    F = Field(lo, hi, h)

    # --- trunk: dense ellipsoid beads along the spline (tail tip -> neck end)
    samples = catmull([np.concatenate([a, r]) for a, r in zip(axis, radii)], 8)
    blend = L * 0.02
    for i, s in enumerate(samples):
        c = s[:3].copy(); w, hh2, drop = s[3], s[4], s[5]
        nxt = samples[min(i + 1, len(samples) - 1)][:3]; prv = samples[max(i - 1, 0)][:3]
        d = nxt - prv; d = d / (np.linalg.norm(d) + 1e-9)
        if d[1] > 0: d = -d                                                             # forward = -Y
        R = frame(d); c[2] -= drop                                                     # belly sag lowers the centre a bit
        seg = max(np.linalg.norm(nxt - prv) * 0.9, min(w, hh2) * 0.8)
        F.ellipsoid(c, (w, seg, hh2 + drop * 0.5), R, k=blend)
    # --- head
    Rh = np.stack([hx, hdir, hup], 1)
    croc = hd.get("croc"); duck = hd.get("duckbill"); pointy = hd.get("pointy")
    F.ellipsoid(H((0, hl * 0.28, 0.02 * hh)), (hw, hl * 0.34, hh * 0.62), Rh, k=blend * 0.7)          # braincase / cheeks
    if pointy:
        F.cone(H((0, hl * 0.15, 0)), H((0, hl * 1.0, -hh * 0.1)), hh * 0.45, hh * 0.04, k=blend * 0.3)
    elif croc:
        F.cone(H((0, hl * 0.3, 0.02)), H((0, hl * 1.0, -hh * 0.05)), hh * 0.55, hh * 0.28, k=blend * 0.5)
        F.ellipsoid(H((0, hl * 0.93, 0)), (hw * 0.75, hl * 0.1, hh * 0.35), Rh, k=blend * 0.3)          # rosette tip
    else:
        F.ellipsoid(H((0, hl * 0.62, -hh * 0.02)), (hw * (0.62 if not duck else 0.8), hl * 0.42, hh * 0.5), Rh, k=blend * 0.6)   # snout
    if hd.get("brow"):
        for s in (1, -1): F.ellipsoid(H((s * hw * 0.55, hl * 0.4, hh * 0.42)), (hw * 0.3, hl * 0.14, hh * 0.13), Rh, k=blend * 0.3)
    if hd.get("beak"):
        F.cone(H((0, hl * 0.75, -hh * 0.05)), H((0, hl * 1.02, -hh * 0.18)), hh * 0.26, hh * 0.05, k=blend * 0.3)
    # lower jaw (separate lip line: carved by the mouth slit below)
    F.ellipsoid(H((0, hl * 0.52, -hh * 0.42)), (hw * 0.78, hl * 0.44, hh * 0.24), Rh, k=blend * 0.5)
    # mouth slit from the tip back to the corner (keeps upper / lower lips apart for the jaw bone)
    md = hd.get("mouth_depth", 0.3)
    F.box(H((0, hl * (1.0 - md * 0.5) + hl * 0.08, -hh * 0.3)), (hw * 1.4, hl * md * 0.5 + hl * 0.1, max(h * 0.9, hh * 0.022)), h * 0.3, Rh, k=h * 0.6, sub=True)
    # eye sockets
    e = hd["eye"]
    for s in (1, -1): F.ellipsoid(H((s * e["at"][0] * 1.15, e["at"][1], e["at"][2])), (e["r"] * 1.1, e["r"] * 1.3, e["r"] * 0.9), Rh, k=e["r"] * 0.5, sub=True)
    # horns (slightly curved round cones)
    J["horns"] = []
    for hn in hd.get("horns", []):
        base = H(hn["at"]); dd = hx * hn["dir"][0] + hdir * hn["dir"][1] + hup * hn["dir"][2]; dd /= np.linalg.norm(dd)
        n = 4; prev = base; cur_dir = dd.copy(); J["horns"].append([base.tolist(), (base + dd * hn["len"]).tolist(), hn["r"]])
        for i in range(n):
            t = (i + 1) / n
            cur_dir = cur_dir + hup * hn.get("curve", 0) * 0.5 / n; cur_dir /= np.linalg.norm(cur_dir)
            nxt = prev + cur_dir * hn["len"] / n
            F.cone(prev, nxt, hn["r"] * (1 - 0.85 * (i / n)), hn["r"] * (1 - 0.85 * t) + h * 0.3, k=hn["r"] * (0.5 if i == 0 else 0.05))
            prev = nxt
    # frill (triceratops): flattened ellipsoid tilted up-back, scalloped edge knobs
    fr = hd.get("frill")
    if fr:
        c = H(fr["at"]); t = math.radians(fr["tilt"])
        fdir = -hdir * math.cos(t) + hup * math.sin(t); fdir /= np.linalg.norm(fdir)
        fup = np.cross(hx, fdir); Rf = np.stack([hx, fdir, fup], 1)
        cc = c + fdir * fr["size"][1] * 0.55
        F.ellipsoid(cc, (fr["size"][0], fr["size"][1] * 0.62, fr["thick"] * 1.6), Rf, k=blend * 0.4)
        F.cone(H((0, hl * 0.2, hh * 0.3)), cc - fdir * fr["size"][1] * 0.1, hh * 0.3, fr["thick"] * 2.5, k=blend * 0.8)   # frill root
        for i in range(fr["spikes"]):
            a = math.pi * (i / (fr["spikes"] - 1)) - math.pi / 2                          # around the rim
            rim = cc + hx * math.sin(a) * fr["size"][0] * 0.96 + fdir * math.cos(a) * fr["size"][1] * 0.6
            out = rim + (rim - cc) / (np.linalg.norm(rim - cc) + 1e-9) * fr["size"][0] * 0.13
            F.cone(rim, out, fr["thick"] * 1.4, h * 0.6, k=fr["thick"])
    # crest (parasaurolophus tube, pteranodon blade)
    cr = hd.get("crest")
    if cr:
        a = H((0, hl * (0.35 if not cr.get("flat") else 0.25), hh * 0.35))
        back = -hdir * cr["len"] + hup * cr["rise"]
        mid = a + back * 0.5 + hup * cr["rise"] * 0.3
        if cr.get("flat"):
            F.ellipsoid(a + back * 0.5, (cr["r"] * 0.6, cr["len"] * 0.55, cr["r"] * 2.2), frame(back), k=blend * 0.3)
        else:
            F.cone(a, mid, cr["r"] * 1.2, cr["r"], k=blend * 0.5); F.cone(mid, a + back, cr["r"], cr["r"] * 0.85, k=cr["r"] * 0.3)

    # --- legs / arms
    J["legs"] = {}
    for name, lg in legs.items():
        for side, sx in (("L", 1), ("R", -1)):
            key = f"{name}_{side}"
            if "fin" in name:
                a = V(lg["root"][0] * sx, lg["root"][1], lg["root"][2]); b = V(lg["tip"][0] * sx, lg["tip"][1], lg["tip"][2])
                d = b - a; Rn = frame(d / np.linalg.norm(d), (0, 0, 1))
                F.ellipsoid((a + b) / 2, (lg["w"] * 0.5, np.linalg.norm(d) * 0.55, lg["w"] * 0.13), Rn, k=blend * 0.6)
                J["legs"][key] = dict(root=a.tolist(), mid=((a + b) / 2).tolist(), tip=b.tolist()); continue
            if name == "wing":
                pts = [V(lg[k][0] * sx, lg[k][1], lg[k][2]) for k in ("shoulder", "elbow", "wrist", "knuckle", "tip")]
                r = lg["r"]
                F.cone(pts[0], pts[1], r[0], r[1], k=blend * 0.5); F.cone(pts[1], pts[2], r[1], r[2], k=h); F.cone(pts[2], pts[3], r[2], r[2] * 0.9, k=h)
                F.cone(pts[3], pts[4], r[3], h * 0.8, k=h * 0.5)
                F.ellipsoid(pts[0] * 0.6 + pts[1] * 0.4 - V(0, 0, 0.02), (r[0] * 1.4, r[0] * 1.8, r[0]), frame(pts[1] - pts[0]), k=blend)   # flight muscle
                J["legs"][key] = dict(zip(("shoulder", "elbow", "wrist", "knuckle", "tip"), [p.tolist() for p in pts])); continue
            hip, knee, ank, foot = [V(lg[k][0] * sx, lg[k][1], lg[k][2]) for k in ("hip", "knee", "ankle", "foot")]
            r = lg["r"]; st = lg["stance"]
            # thigh muscle mass + bones
            F.ellipsoid(hip * 0.55 + knee * 0.45, (r[0] * 0.92, r[0] * 1.25, np.linalg.norm(knee - hip) * 0.62), None, k=blend)
            F.cone(hip, knee, r[0] * 0.8, r[1], k=blend * 0.8)
            F.cone(knee, ank, r[1], r[2], k=blend * 0.4)
            F.ellipsoid(knee * 0.7 + ank * 0.3, (r[1] * 1.05, r[1] * 1.05, np.linalg.norm(ank - knee) * 0.32), None, k=blend * 0.5)   # calf muscle
            if st == "hand":                                                                    # small arm with fingers
                F.cone(ank, foot, r[2], r[3], k=h)
                tip = foot + (foot - ank) / (np.linalg.norm(foot - ank) + 1e-9) * lg["toe_len"]
                J["legs"][key] = dict(hip=hip.tolist(), knee=knee.tolist(), ankle=ank.tolist(), foot=foot.tolist(), toe=tip.tolist(), stance=st); continue
            F.cone(ank, foot + V(0, 0, r[3]), r[2], r[3], k=blend * 0.3)
            # toes / foot pad
            ntoe = 3 if st == "digitigrade" else 4
            spread = 0.5 if st == "digitigrade" else 0.35
            toe_tips = []
            for i in range(ntoe):
                a = (i - (ntoe - 1) / 2) * spread
                d = V(math.sin(a) * sx, -math.cos(a), 0)
                base = foot + V(0, 0, r[3] * 0.8)
                tipp = base + d * lg["toe_len"] * (1.0 if i == (ntoe - 1) // 2 or ntoe == 4 else 0.85) - V(0, 0, r[3] * 0.55)
                F.cone(base, tipp, r[3] * (0.75 if st == "digitigrade" else 0.95), r[3] * 0.45, k=blend * 0.25)
                toe_tips.append(tipp.tolist())
            if st == "plantigrade": F.ellipsoid(foot + V(0, -lg["toe_len"] * 0.25, r[3] * 0.6), (r[3] * 1.6, lg["toe_len"] * 0.7, r[3] * 0.7), None, k=blend * 0.4)
            ctr = np.mean(np.array(toe_tips), 0)
            J["legs"][key] = dict(hip=hip.tolist(), knee=knee.tolist(), ankle=ank.tolist(), foot=foot.tolist(), toe=ctr.tolist(), toe_tips=toe_tips, stance=st)
            # shoulder / hip blend mass into the trunk
            F.ellipsoid(hip - V(sx * r[0] * 0.45, 0, -r[0] * 0.2), (r[0] * 0.7, r[0] * 1.1, r[0] * 0.9), None, k=blend * 1.5)

    # --- armour (ankylosaurus osteoderms, dorsal scutes), club, side spikes
    arm = sp.get("armor")
    if arm:
        ys = np.arange(body[0]["y"] + 0.6, neck[0]["y"], -arm["spacing"])
        for y in ys:
            s = min(samples, key=lambda q: abs(q[1] - y)); w, hh2 = s[3], s[4]
            for j in range(arm["rows"]):
                ang = (j - (arm["rows"] - 1) / 2) / max(1, arm["rows"] - 1) * 2.1          # across the back
                p = V(math.sin(ang) * w * 0.95, y, s[2] + math.cos(ang) * hh2 * 0.95)
                n = V(math.sin(ang), 0, math.cos(ang))
                F.cone(p - n * arm["size"] * 0.3, p + n * arm["size"] * 1.1, arm["size"], arm["size"] * 0.25, k=arm["size"] * 0.6)
            if arm.get("side_spikes"):
                for sx in (1, -1):
                    p = V(sx * w * 1.0, y, s[2] - hh2 * 0.1); F.cone(p, p + V(sx * arm["size"] * 2.2, 0.05, -0.02), arm["size"] * 0.8, h * 0.6, k=arm["size"] * 0.4)
        if arm.get("club"):
            tip = J["tail"][-1]; c = tip + V(0, 0.05, 0); cr = arm["club"]["r"]
            F.ellipsoid(c, cr, None, k=blend * 0.4)
            for sx in (1, -1): F.ellipsoid(c + V(sx * cr[0] * 0.55, 0, 0), (cr[0] * 0.55, cr[1] * 0.5, cr[2] * 1.1), None, k=blend * 0.3)
    sail = sp.get("sail")
    if sail:
        ys = np.linspace(sail["start"], sail["end"], 40)
        for i, y in enumerate(ys):
            s = min(samples, key=lambda q: abs(q[1] - y)); t = i / (len(ys) - 1)
            ht = sail["height"] * math.sin(math.pi * (0.1 + 0.8 * t)) ** 0.8
            base = V(0, y, s[2] + s[4] * 0.7)
            F.box(base + V(0, 0, ht * 0.5), (sail["thick"] + h * 0.5, abs(ys[1] - ys[0]) * 0.8, ht * 0.5), h * 0.4, None, k=blend * 0.5)
    fl = sp.get("fluke")
    if fl:
        tip = J["tail"][-1]
        F.ellipsoid(tip + V(0, 0.1, fl["h"] * 0.25), (0.08, fl["w"] * 0.45, fl["h"] * 0.45), frame(V(0, -0.3, 1)), k=blend * 0.5)
        F.ellipsoid(tip + V(0, 0.2, -fl["h"] * 0.15), (0.08, fl["w"] * 0.4, fl["h"] * 0.3), frame(V(0, -0.6, -1)), k=blend * 0.5)
    # flat soles: nothing below the ground plane (land animals)
    if kind not in ("swimmer",):
        zlo = float(lo[2])
        F.apply(lambda P: P[..., 2], np.array([lo[0], lo[1], zlo]), np.array([hi[0], hi[1], min(0.0, float(hi[2]))]) + np.array([0, 0, h * 2]), k=h * 0.5, sub=True)
    J["eyes"] = [H((s * e["at"][0], e["at"][1], e["at"][2])).tolist() for s in (1, -1)]
    J["eye_r"] = e["r"]
    J["head_frame"] = dict(pos=hp.tolist(), x=hx.tolist(), fwd=hdir.tolist(), up=hup.tolist(), len=hl, w=hw, h=hh)
    return F, J

def tolist(o):
    if isinstance(o, np.ndarray): return o.tolist()
    if isinstance(o, dict): return {k: tolist(v) for k, v in o.items()}
    if isinstance(o, list): return [tolist(v) for v in o]
    return o

if __name__ == "__main__":
    from skimage.measure import marching_cubes
    sid, out = sys.argv[1], sys.argv[2]; vox = int(sys.argv[3]) if len(sys.argv) > 3 else 260
    os.makedirs(out, exist_ok=True)
    import time; t0 = time.time()
    F, J = build(SPECS[sid], vox)
    t1 = time.time()
    verts, faces, _, _ = marching_cubes(F.d, level=0.0, spacing=(F.h, F.h, F.h))
    verts = verts + F.lo
    write_ply(os.path.join(out, f"{sid}_body.ply"), verts, faces)
    json.dump(tolist(J), open(os.path.join(out, f"{sid}_joints.json"), "w"), indent=1)
    print(f"{sid}: grid {tuple(F.n)} h={F.h:.4f} sdf {t1 - t0:.1f}s mc {time.time() - t1:.1f}s verts {len(verts)} faces {len(faces)}")
