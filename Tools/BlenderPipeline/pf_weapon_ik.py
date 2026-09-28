# Weapon-driven arm posing for clip authoring: solve the anatomical arm parameters (aX, eX, wX of pf_clips_human
# pose dicts) so that the hand's grip frame (same frame as PlayerHierarchy.TryComputeGrip in Unity: Y = blade / shaft
# towards the index side, Z = edge towards the knuckles) reaches a target position and direction.
# FK of the arm chain is computed from the rest matrices (no depsgraph update per evaluation).
import bpy, math
import numpy as np
from mathutils import Vector, Matrix, Quaternion
import pf_clips_human as CH
import pf_pose_preview as PV

KEYS = ("flex", "abduct", "twist", "horiz", "elbow", "wflex", "wdev", "wtwist")
LIMITS = dict(flex=(-60, 180), abduct=(-45, 100), twist=(-70, 70), horiz=(-60, 110), elbow=(0, 150),
              wflex=(-65, 65), wdev=(-40, 40), wtwist=(-95, 95))
SCALE = dict(flex=30, abduct=30, twist=40, horiz=30, elbow=30, wflex=30, wdev=25, wtwist=40)

def _from_pose(p, sd):
    a = p["a" + sd]; w = p["w" + sd]
    return [a[0], a[1], a[2], a[3], p["e" + sd], w[0], w[1], w[2]]

def _to_pose(p, sd, x):
    q = dict(p)
    q["a" + sd] = (x[0], x[1], x[2], x[3]); q["e" + sd] = x[4]; q["w" + sd] = (x[5], x[6], x[7])
    return q

class ArmFK:
    def __init__(self, rig, sd):
        self.rig = rig; self.sd = sd; arm = rig.obj
        self.R = {b: rig.rest[f"{b}_{sd}"] for b in ("Clavicle", "UpperArm", "LowerArm", "Hand")}
        self.grip_local = self.R["Hand"].inverted() @ PV.grip_matrix(arm, sd)
        bpy.context.view_layer.update()
        self.M_clav = arm.pose.bones[f"Clavicle_{sd}"].matrix.copy()

    def grip(self, x):
        rig, sd = self.rig, self.sd
        rig.arm(sd, x[0], x[1], x[2], x[3]); rig.elbow(sd, x[4]); rig.wrist(sd, x[5], x[6], x[7])
        pb = rig.obj.pose.bones
        M = self.M_clav
        for parent, b in (("Clavicle", "UpperArm"), ("UpperArm", "LowerArm"), ("LowerArm", "Hand")):
            M = M @ self.R[parent].inverted() @ self.R[b] @ pb[f"{b}_{sd}"].rotation_quaternion.to_matrix().to_4x4()
        return M @ self.grip_local

def _ang(a, b):
    return math.degrees(math.acos(max(-1.0, min(1.0, a.normalized().dot(b.normalized())))))

def solve(rig, p, sd, pos=None, blade=None, edge=None, w_pos=1.0, w_blade=1.0, w_edge=0.35, reg=0.02, iters=500, pos_tol=0.02, starts=3):
    """p: pose dict with the body already posed (apply(rig, p) was called). Returns (new pose dict, info)."""
    fk = ArmFK(rig, sd)
    x0 = np.array(_from_pose(p, sd), dtype=float)
    pos = Vector(pos) if pos is not None else None
    blade = Vector(blade).normalized() if blade is not None else None
    edge = Vector(edge).normalized() if edge is not None else None
    lo = np.array([LIMITS[k][0] for k in KEYS]); hi = np.array([LIMITS[k][1] for k in KEYS]); sc = np.array([SCALE[k] for k in KEYS])
    def cost(x):
        G = fk.grip(x); c = 0.0
        if pos is not None: c += w_pos * ((G.translation - pos).length / pos_tol) ** 2
        if blade is not None: c += w_blade * (_ang(G.col[1].xyz, blade) / 5.0) ** 2
        if edge is not None:
            e = G.col[2].xyz; b = G.col[1].xyz.normalized()
            e_t = edge - b * edge.dot(b)
            if e_t.length > 1e-4: c += w_edge * (_ang(e, e_t) / 10.0) ** 2
        c += reg * float(np.sum(((x - x0) / sc) ** 2))
        c += 50.0 * float(np.sum((np.maximum(lo - x, 0) / sc) ** 2 + (np.maximum(x - hi, 0) / sc) ** 2))
        return c
    best = None
    for start in (x0, x0 + sc * np.array([0.5, 0, 0, 0.5, 0, 0, 0, 0.8]), x0 + sc * np.array([-0.5, 0.3, 0, -0.3, 0.5, 0, 0, -0.8]))[:starts]:
        x, f = _nelder_mead(cost, np.array(start, dtype=float), sc * 0.6, iters)
        if best is None or f < best[1]: best = (x, f)
    x = np.clip(best[0], lo, hi)
    G = fk.grip(x)
    info = dict(cost=round(best[1], 3), pos_err=round((G.translation - pos).length, 4) if pos is not None else None,
                blade_err=round(_ang(G.col[1].xyz, blade), 1) if blade is not None else None,
                x=[round(float(v), 1) for v in x])
    return _to_pose(p, sd, [float(v) for v in x]), info

def _nelder_mead(f, x0, step, iters):
    n = len(x0)
    pts = [x0] + [x0 + np.eye(n)[i] * step[i] for i in range(n)]
    vals = [f(p) for p in pts]
    for _ in range(iters):
        order = np.argsort(vals); pts = [pts[i] for i in order]; vals = [vals[i] for i in order]
        c = np.mean(pts[:-1], axis=0)
        xr = c + (c - pts[-1]); fr = f(xr)
        if fr < vals[0]:
            xe = c + 2 * (c - pts[-1]); fe = f(xe)
            if fe < fr: pts[-1], vals[-1] = xe, fe
            else: pts[-1], vals[-1] = xr, fr
        elif fr < vals[-2]: pts[-1], vals[-1] = xr, fr
        else:
            xc = c + 0.5 * (pts[-1] - c); fc = f(xc)
            if fc < vals[-1]: pts[-1], vals[-1] = xc, fc
            else:
                pts = [pts[0]] + [pts[0] + 0.5 * (p - pts[0]) for p in pts[1:]]
                vals = [vals[0]] + [f(p) for p in pts[1:]]
        if abs(vals[-1] - vals[0]) < 1e-6 and _ > 60: break
    i = int(np.argmin(vals)); return pts[i], vals[i]

def posed(rig, p, targets):
    """apply the body pose, then solve each arm target: targets = {sd: dict(pos=, blade=, edge=, ...)}; pos may be a
    callable(rig, pose) evaluated after the previous arm is solved (e.g. the off hand on the spear shaft)"""
    rig.reset(); CH.apply(rig, p); bpy.context.view_layer.update()
    infos = {}
    for sd, t in targets.items():
        t = dict(t)
        for k in ("pos", "blade", "edge"):
            if callable(t.get(k)): t[k] = t[k](rig, p)
        p, infos[sd] = solve(rig, p, sd, **t)
        rig.reset(); CH.apply(rig, p); bpy.context.view_layer.update()
    return p, infos

def grip_world(rig, sd):
    """current posed grip frame (armature space) of a hand"""
    bpy.context.view_layer.update()
    pb = rig.obj.pose.bones[f"Hand_{sd}"]
    return pb.matrix @ rig.rest[f"Hand_{sd}"].inverted() @ PV.grip_matrix(rig.obj, sd)
