# PRIMAL FRONTIER - CHAR phase clips (2026-09-30): human locomotion v2 (walk / run / sprint), idle, stepping turns,
# starts / stops / pivots, bare-hand set, physical survival actions, climbing.
# Original motion from principles only (Documentation/Character/_motion_principles.md): no copied clips, no mocap.
# Armature space: X = character's left, -Y = forward, Z = up; 30 fps; clips in place (the capsule motor moves the body).
import bpy, math
from mathutils import Vector, Matrix, Quaternion
from pf_anim import ease, lerp, FPS, q_axis
import pf_clips_human as CH
P, add, apply, interp, BASE = CH.P, CH.add, CH.apply, CH.interp, CH.BASE

TAU = math.tau
LR = ("L", "R")

def smooth(t): return ease(t, "smooth")
def inout(t): return ease(t, "inout")
def clamp01(t): return max(0.0, min(1.0, t))
def blend_pose(a, b, k):
    return {kk: lerp(a[kk], b[kk], k) for kk in a}
def Rz(deg): return Matrix.Rotation(math.radians(deg), 3, 'Z')
def fwd_of(yaw):
    """armature forward (-Y) turned by yaw (deg, + = turn left)"""
    r = math.radians(yaw); return Vector((math.sin(r), -math.cos(r), 0.0))

# ================================================================== locomotion v2
# B1 arm arc (elbow near 90 in the run, fore-aft hand path, hands never cross the sternum), B2 shoulder counter-rotation
# (no clavicle term cancelling the chest yaw; yaw moved from the pelvis to the chest), B3 run pelvis lowest at mid-stance
# (pelvis height fitted under the leg-reach limit with the bounce shape, no erosion of the shape), B7 asymmetry (right
# foot a fraction of a frame early, different step height / toe-out / arm amplitude), head stabilisation, 1-3 frame overlap.
LOCO2 = {
    "Walk": dict(frames=32, speed=1.35, D=0.60, step_h=0.085, strike=14, toeoff=52, heel_off=0.45, lean=3.0, bob=0.040, sway=0.020, roll=4.5,
                 tilt=1.2, pel_yaw=4.0, chest_yaw=4.5, head_yaw=1.2, center_back=0.10, kick=0.0,
                 arm=dict(flex0=4.0, flexA=14.0, back=0.85, elb0=28.0, elbA=7.0, elb2=0.0, abd=-33.0, horiz=2.5, wflex=6.0, fist=(20.0, 16.0), prot=0.03),
                 asym=0.010, toe_out=(4.0, 7.0), arm_asym=0.9),
    "Run": dict(frames=22, speed=3.8, D=0.34, step_h=0.15, strike=8, toeoff=60, heel_off=0.45, lean=8.0, bob=-0.062, sway=0.012, roll=4.3,
                tilt=3.0, pel_yaw=6.5, chest_yaw=10.0, head_yaw=2.0, center_back=0.20, kick=0.13, contact_reach=0.955,
                arm=dict(flex0=-12.0, flexA=31.0, back=1.0, elb0=84.0, elbA=-6.0, elb2=3.0, abd=-31.0, horiz=4.0, wflex=4.0, fist=(46.0, 30.0)),
                asym=0.018, toe_out=(3.0, 6.0), arm_asym=0.92),
    "Sprint": dict(frames=18, speed=6.2, D=0.26, step_h=0.20, strike=-8, toeoff=78, heel_off=0.30, lean=13.0, bob=-0.065, sway=0.008, roll=4.0,
                   tilt=3.5, pel_yaw=8.0, chest_yaw=12.0, head_yaw=2.5, center_back=0.20, kick=0.24, contact_reach=0.955,
                   arm=dict(flex0=-10.0, flexA=44.0, back=1.0, elb0=94.0, elbA=-20.0, elb2=3.0, abd=-32.0, horiz=3.0, wflex=2.0, fist=(52.0, 34.0)),
                   asym=0.020, toe_out=(2.0, 5.0), arm_asym=0.93),
    "Run_Backward": dict(frames=24, speed=2.4, D=0.42, step_h=0.11, strike=-10, toeoff=-22, heel_off=0.5, lean=-3.0, bob=-0.05, sway=0.01, roll=2.5,
                         tilt=1.0, pel_yaw=3.5, chest_yaw=5.0, head_yaw=1.5, center_back=0.04, kick=0.0, move=(0, 1),
                         arm=dict(flex0=-6.0, flexA=18.0, back=1.0, elb0=82.0, elbA=-4.0, elb2=2.0, abd=-31.0, horiz=3.0, wflex=4.0, fist=(40.0, 28.0)),
                         asym=0.015, toe_out=(3.0, 6.0), arm_asym=0.92),
    # stepping turns on the spot (loops, blended by the controller at 90 deg/s): head leads, chest, pelvis, then the feet
    "Turn_Left": dict(frames=30, speed=0.0, D=0.62, step_h=0.055, strike=0, toeoff=12, heel_off=0.55, lean=1.0, bob=0.012, sway=0.014, roll=2.5,
                      tilt=0.5, pel_yaw=2.0, chest_yaw=2.5, head_yaw=1.0, center_back=0.0, kick=0.0, move=(0, 0), turn=90.0,
                      lead=dict(pel=3.0, chest=9.0, head=20.0, trail=7.0),
                      arm=dict(flex0=4.0, flexA=5.0, back=1.0, elb0=18.0, elbA=4.0, elb2=0.0, abd=-34.0, horiz=1.0, wflex=5.0, fist=(20.0, 16.0)),
                      asym=0.012, toe_out=(4.0, 7.0), arm_asym=0.85),
    "Turn_Right": dict(frames=30, speed=0.0, D=0.62, step_h=0.055, strike=0, toeoff=12, heel_off=0.55, lean=1.0, bob=0.012, sway=0.014, roll=2.5,
                       tilt=0.5, pel_yaw=2.0, chest_yaw=2.5, head_yaw=1.0, center_back=0.0, kick=0.0, move=(0, 0), turn=-90.0,
                       lead=dict(pel=3.0, chest=9.0, head=20.0, trail=7.0),
                       arm=dict(flex0=4.0, flexA=5.0, back=1.0, elb0=18.0, elbA=4.0, elb2=0.0, abd=-34.0, horiz=1.0, wflex=5.0, fist=(20.0, 16.0)),
                       asym=0.012, toe_out=(4.0, 7.0), arm_asym=0.85),
}

def swing_ease(v):
    return ease((v - 0.05) / 0.85, "smooth")

class Gait2:
    """in-place locomotion loop. pose(f) returns the full pose dict (feet as IK targets), fn(f) applies it."""
    def __init__(self, rig, name, g):
        self.rig = rig; self.name = name; g = dict(g); self.g = g
        self.N = N = g["frames"]; self.speed = g["speed"]; self.D = D = g["D"]
        self.T = N / FPS
        self.S = self.speed * D * self.T                       # stance travel = belt speed x stance time (no slide)
        mv = g.get("move", (0, -1))
        self.mv = Vector((mv[0], mv[1], 0.0)).normalized() if (mv[0] or mv[1]) else Vector((0, 0, 0))
        self.turn = g.get("turn", 0.0)
        self.ph0 = {"L": 0.0, "R": 0.5 + g.get("asym", 0.0)}     # the right foot lands a fraction of a frame early (B7)
        self.hs = {"L": 1.0, "R": 0.93}                          # step height differs a little
        self.toe = {"L": g["toe_out"][0], "R": -g["toe_out"][1]}
        self.t_ms = D * 0.5
        L1 = rig.len["Thigh_L"]; L2 = rig.len["Calf_L"]
        self.reach = 0.993 * (L1 + L2)
        self._fit_pelvis()

    # ---- feet (same no-skid construction as pf_clips_human.gait_fn, per-side height / toe-out, true orbit when turning)
    def foot(self, sd, ph):
        g = self.g; D = self.D; S = self.S; T = self.T; mv = self.mv; turn = self.turn
        rig = self.rig
        cb = g["center_back"]; strike = g["strike"]; toeoff = g["toeoff"]; heel_off = g["heel_off"]
        a = rig.foot_rest[sd]["ankle"]
        if ph < D:
            u = ph / D
            s = S * 0.5 - S * u
            yaw_body = -turn * (u - 0.5) * D * T if turn else 0.0
            if u < 0.14: pitch = -strike * (1 - ease(u / 0.14, "out"))
            elif u > heel_off: pitch = toeoff * ease((u - heel_off) / (1 - heel_off), "smooth")
            else: pitch = 0.0
            dz = 0.0; tb = pitch; stance = u
        else:
            v = (ph - D) / (1 - D)
            ew = ease((v - 0.03) / 0.97, "smooth")         # arrives on its world landing spot exactly at contact (no hover ahead)
            s = -S * 0.5 + (S / D) * ew - S * (1 - D) / D * v
            h = self.g["step_h"] * self.hs[sd] * math.sin(math.pi * min(1, v * 1.05)) ** 1.2
            k = g["kick"]
            if k: h += k * math.sin(math.pi * min(1.0, v * 1.6)) ** 2
            yaw_body = (-turn * D * T * 0.5 + turn * T * ew - turn * (1 - D) * T * v) if turn else 0.0
            if v < 0.3: pitch = toeoff * (1 - ease(v / 0.3)) + 5 * ease(v / 0.3)
            elif v < 0.8: pitch = 5 * (1 - ease((v - 0.3) / 0.5))
            else: pitch = -strike * ease((v - 0.8) / 0.2)
            dz = h; tb = max(0.0, pitch) * 0.5; stance = -1.0 - v          # swing: encoded as -1 - v
            if k: s -= k * 0.6 * math.sin(math.pi * min(1.0, v * 1.6)) ** 2
        off = mv * s - mv * cb
        yaw = self.toe[sd] + yaw_body
        if turn:
            # the planted foot is fixed in the world while the body turns: it orbits the body centre (no skid)
            p0 = Vector((a.x, a.y, 0.0))
            p1 = Rz(yaw_body) @ p0
            off = off + (p1 - p0)
        return (off.x, off.y, dz, pitch, yaw, tb), stance

    def feet_at(self, t):
        return {sd: self.foot(sd, (t + self.ph0[sd]) % 1.0) for sd in LR}

    def sway_x(self, t):
        return self.g["sway"] * math.sin(TAU * (t - self.t_ms + 0.25))

    def shape(self, t):
        # walk: bob > 0, highest at mid-stance; run: bob < 0, lowest at mid-stance, highest in flight
        return 0.5 * self.g["bob"] * math.cos(2 * TAU * (t - self.t_ms))

    def _reach_ok(self, sd, tup, pel):
        tg = self.rig.planted(sd, tup[:3], tup[3], tup[4], tup[5])
        hip = self.rig.rest[f"Thigh_{sd}"].translation
        d = Vector((tg[0].x - hip.x - pel[0], tg[0].y - hip.y - pel[1], tg[0].z - hip.z - pel[2]))
        return d.length <= self.reach

    def lift_heel(self, sd, tup, st, pel):
        """late stance: the heel rises (pivot on the ball) as far as the leg needs to stay in reach, as a real toe-off does"""
        if st < 0 or st <= self.g["heel_off"] * 0.6 or self._reach_ok(sd, tup, pel): return tup
        lo, hi = tup[3], 80.0
        t2 = list(tup); t2[3] = hi; t2[5] = hi
        if not self._reach_ok(sd, tuple(t2), pel): return tuple(t2)
        for _ in range(14):
            mid = 0.5 * (lo + hi); t2[3] = mid; t2[5] = mid
            if self._reach_ok(sd, tuple(t2), pel): hi = mid
            else: lo = mid
        t2[3] = hi; t2[5] = hi
        return tuple(t2)

    def zmax(self, t, flat_only=True, pitch_max=None, reach=None):
        """highest pelvis offset that keeps the grounded feet in reach; flat_only skips the late-stance feet (their heel
        can rise), pitch_max evaluates those feet with the heel up that far instead"""
        rig = self.rig; x = self.sway_x(t); zm = 1e9; R = reach or self.reach
        for sd, (tup, st) in self.feet_at(t).items():
            if st < 0: continue                     # in the air: swing_reach keeps it reachable
            elif st > self.g["heel_off"] * 0.6:
                if flat_only: continue
                if pitch_max is not None: tup = (tup[0], tup[1], tup[2], max(tup[3], pitch_max), tup[4], max(tup[5], pitch_max))
            tg = rig.planted(sd, tup[:3], tup[3], tup[4], tup[5])
            hip = rig.rest[f"Thigh_{sd}"].translation
            hor = math.hypot(tg[0].x - hip.x - x, tg[0].y - hip.y)
            if hor < R:
                zm = min(zm, tg[0].z + math.sqrt(R ** 2 - hor * hor) - hip.z)
            else: zm = min(zm, -0.5)
        return zm

    def _fit_pelvis(self):
        N = self.N; sub = 4; M = N * sub
        # the bounce shape is kept exactly and lowered as a whole until the contact / mid-stance legs are in reach
        # (sampled at quarter frames); late-stance legs are handled by the heel rise, and only where even a 75 deg heel
        # rise is not enough is the pelvis lowered locally (eroded + blurred, so no pops)
        # contact with a slightly flexed knee (run ~15 deg): the foot about to land stays in reach
        rc = self.g.get("contact_reach", 0.985) * self.reach / 0.993
        z0 = min(self.zmax(i / M, reach=rc) - self.shape(i / M) for i in range(M))
        cap = -abs(self.g["bob"]) * 0.5 - 0.004
        self.z0 = min(z0 - 0.004, cap)
        zdes = [self.z0 + self.shape(f / N) for f in range(N)]
        zlim = [self.zmax(f / N, flat_only=False, pitch_max=75.0) for f in range(N)]
        z = [min(a, b) for a, b in zip(zdes, zlim)]
        if any(b < a for a, b in zip(zdes, zlim)):
            k = 2
            er = [min(z[(i + j) % N] for j in range(-k, k + 1)) for i in range(N)]
            w = [math.cos(0.5 * math.pi * j / (k + 1)) ** 2 for j in range(-k, k + 1)]
            z = [min(zdes[i], sum(er[(i + j) % N] * w[j + k] for j in range(-k, k + 1)) / sum(w)) for i in range(N)]
        self.local_dips = sum(1 for a, b in zip(zdes, z) if b < a - 0.002)
        self.ztab = z + [z[0]]

    def arm_sig(self, sd, t, lag):
        k = 1.0 if sd == "L" else -1.0
        return k * -math.cos(TAU * (t - lag))              # +1 = this arm fully forward (the opposite leg forward)

    def pose(self, f):
        g = self.g; N = self.N; t = (f % N) / N
        A = g["arm"]; turn = self.turn
        p = dict(BASE)
        c = lambda lag=0.0: math.cos(TAU * (t - lag))
        x = self.sway_x(t); z = self.ztab[f % N]
        ps = -g["roll"] * math.sin(TAU * (t - self.t_ms + 0.25))       # swing side drops (obliquity)
        pw = -g["pel_yaw"] * c()                                          # pelvis turns with the forward leg
        lagc = 1.5 / N; lagh = 3.0 / N
        cw = g["chest_yaw"] * c(lagc)                                      # chest counter-rotates, 1-2 frames behind
        hw = g["head_yaw"] * c(lagh)                                       # head nearly still in the world
        ld = g.get("lead")
        lp = lc = lh = tr = 0.0
        if ld and turn:
            sgn = 1.0 if turn > 0 else -1.0
            lp, lc, lh, tr = sgn * ld["pel"], sgn * ld["chest"], sgn * ld["head"], sgn * ld["trail"]
        lean = g["lean"]
        tilt = g["tilt"] * math.cos(2 * TAU * (t - self.t_ms - 0.08))    # small pelvis tilt wave (twice per cycle)
        rel = (cw + lc) - (pw + lp)
        # spine distributes the counter-rotation with a growing lag (overlap down the chain)
        rel1 = g["chest_yaw"] * c(lagc * 0.4) + g["pel_yaw"] * c()
        rel2 = g["chest_yaw"] * c(lagc * 0.8) + g["pel_yaw"] * c()
        rel3 = g["chest_yaw"] * c(lagc * 1.2) + g["pel_yaw"] * c()
        dl = lc - lp
        p["pel"] = (x, 0.0, z)
        p["pelr"] = (lean * 0.45 + tilt, ps, pw + lp)
        p["sp"] = (2 + lean * 0.30, -ps * 0.45, 0.28 * rel1 + 0.3 * dl)
        p["spu"] = (1 + lean * 0.30, -ps * 0.30, 0.34 * rel2 + 0.35 * dl)
        p["ch"] = (-2 + lean * 0.15, -ps * 0.12, 0.38 * rel3 + 0.35 * dl)
        chest_w = pw + lp + 0.28 * rel1 + 0.34 * rel2 + 0.38 * rel3 + dl
        head_rel = (hw + lh) - chest_w
        bobk = min(1.0, abs(g["bob"]) / 0.03)
        hpitch = -1.2 * math.cos(2 * TAU * (t - self.t_ms)) * bobk * (1 if g["bob"] > 0 else -1)
        p["nk"] = (4 - lean * 0.45, ps * 0.25, 0.6 * head_rel)
        p["hd"] = (-3 - lean * 0.5 + hpitch, ps * 0.15, 0.4 * head_rel)
        # arms (B1): shoulder swing opposite the legs, elbow 2 frames behind, wrist 3 frames behind
        for sd in LR:
            asy = 1.0 if sd == "L" else g["arm_asym"]
            s0 = self.arm_sig(sd, t, 0.0); se = self.arm_sig(sd, t, 2.0 / N); sw = self.arm_sig(sd, t, 3.0 / N)
            fl = A["flex0"] + A["flexA"] * asy * (s0 if s0 > 0 else s0 * A["back"])
            sgn = 1 if sd == "L" else -1
            p["a" + sd] = (fl, A["abd"] + 1.2 * abs(s0), 0.0, A["horiz"] * max(0.0, s0) - tr * sgn * 0.0)
            el = A["elb0"] + A["elbA"] * se + A["elb2"] * (0.5 + 0.5 * math.cos(2 * TAU * (t - 1.0 / N)))
            if sd == "R": el += 2.0
            p["e" + sd] = el
            p["w" + sd] = (A["wflex"] - 5.0 * sw * min(1.0, A["flexA"] / 20.0), 0.0, 0.0)
            # clavicle: small protraction WITH the arm (adds to the shoulder-line counter-rotation), breath-like lift
            p["c" + sd] = (0.6 * math.cos(2 * TAU * (t - self.t_ms)) * bobk + 1.0 * max(0.0, s0) * min(1.0, A["flexA"] / 25.0),
                           -A.get("prot", 0.07) * A["flexA"] * s0)
            fc = A["fist"]
            p["f" + sd] = (fc[0] + (3.0 if sd == "R" else 0.0), fc[1])
        if tr:
            # arms trail the turning chest (overlap): both upper arms swing slightly against the turn
            for sd in LR:
                a = p["a" + sd]; p["a" + sd] = (a[0], a[1], a[2], a[3] + (-tr if sd == "L" else tr) * 0.5)
        for sd, (tup, st) in self.feet_at(t).items():
            p["ft" + sd] = self.swing_reach(sd, tup, st, (x, 0.0, z)) if st < 0 else self.lift_heel(sd, tup, st, (x, 0.0, z))
        return p

    def swing_reach(self, sd, tup, st, pel):
        """a swinging foot beyond reach: early in the swing it rises (the knee folds, heel towards the buttock), late in the
        swing it is drawn in horizontally towards the hip (the leg reaches for the ground); never dragged by the IK clamp"""
        if self._reach_ok(sd, tup, pel): return tup
        v = -st - 1.0
        rig = self.rig
        a = rig.foot_rest[sd]["ankle"]; hip = rig.rest[f"Thigh_{sd}"].translation
        def bis(t2, setter, lo, hi):
            for _ in range(14):
                mid = 0.5 * (lo + hi); setter(t2, mid)
                if self._reach_ok(sd, tuple(t2), pel): hi = mid
                else: lo = mid
            setter(t2, hi); return t2
        def sz(t2, k): t2[2] = tup[2] + k
        hx, hy = hip.x + pel[0] - a.x, hip.y + pel[1] - a.y      # offsets that would put the ankle under the hip
        def sh(t2, k): t2[0] = tup[0] + (hx - tup[0]) * k; t2[1] = tup[1] + (hy - tup[1]) * k
        up = bis(list(tup), sz, 0.0, 0.4)                          # early swing: the foot rises (knee folds), at most 0.4 m
        if not self._reach_ok(sd, tuple(up), pel):                 # still too far: also drawn in
            base = list(up); x0, y0 = base[0], base[1]
            def sh2(t2, k): t2[0] = x0 + (hx - x0) * k; t2[1] = y0 + (hy - y0) * k
            up = bis(base, sh2, 0.0, 1.0)
        drawn = bis(list(tup), sh, 0.0, 1.0)                       # late swing: drawn in towards the hip
        w = smooth(clamp01((v - 0.35) / 0.4))                      # blended, no switch
        t2 = [lerp(a_, b_, w) for a_, b_ in zip(up, drawn)]
        return tuple(t2)

    def fn(self, f):
        return apply(self.rig, self.pose(f))

    def events(self):
        eR = int(round(self.N * (1.0 - self.ph0["R"]))) % self.N
        return [(0, "OnFootstep", "L"), (eR, "OnFootstep", "R")]

    def mid_stance_frames(self):
        return {"L": self.t_ms * self.N, "R": ((self.t_ms - self.ph0["R"]) % 1.0) * self.N}

# ================================================================== idle v2
IDLE_N = 180
def idle2_pose(f, N=IDLE_N):
    """breathing (2 breaths per 6 s loop = 20 / min), weight about 60/40 shifting slowly leg to leg with the pelvis tilted
    to the unloaded side, soft knees, asymmetric feet (right toe out more, a little back), head drift under 3 deg that
    lags the chest, arms relaxed and not mirrored"""
    t = (f % N) / N
    br = math.sin(TAU * 2 * t)                       # breath
    br2 = math.sin(TAU * 2 * t - 0.6)               # the shoulders follow the chest a little later
    ws = 0.35 + 0.65 * math.sin(TAU * t)            # weight: mostly on the left, then over to the right and back
    w2 = math.sin(TAU * 3 * t + 0.9)
    hy = 2.2 * math.sin(TAU * t - 0.8) + 0.8 * math.sin(TAU * 3 * t + 0.3)
    hp = 1.2 * math.sin(TAU * 2 * t + 1.9)
    p = add(BASE,
            pel=(0.020 * ws, 0.004, -0.014 - 0.006 * ws * ws),
            pelr=(0.3 * br, -3.0 * ws, 1.4 * w2 - 1.0),
            sp=(0.5 * br, 1.3 * ws, -0.5 * w2 + 0.5), spu=(1.2 * br, 0.8 * ws, -0.3 * w2), ch=(1.6 * br, 0.4 * ws, -0.2 * w2 + 0.4),
            nk=(0.6 * hp, -1.2 * ws, 0.55 * hy), hd=(-0.9 * br + 0.8 * hp, -0.9 * ws, 0.45 * hy),
            cL=(1.3 * br2, 0.3), cR=(1.1 * br2, -0.2),
            aL=(-2.0 + 1.2 * w2, 1.5 + 0.8 * br, 0, 0), aR=(-3.5 - 0.8 * w2, 0.5 + 0.8 * br, 0, 0),
            eL=-7.0 + 2.0 * w2, eR=-10.0 - 1.5 * w2,
            wL=(1.5 * math.sin(TAU * 2 * t + 2.2), 0, 0), wR=(3.0 - 1.5 * math.sin(TAU * 2 * t + 1.0), 0, 0),
            fL=(2.0 * math.sin(TAU * t + 1.3), 1.0), fR=(6.0 - 2.0 * math.sin(TAU * t + 0.4), -1.0))
    p["ftL"] = (0.004, -0.006, 0.0, 0.0, 4.0, 0.0)
    p["ftR"] = (-0.012, 0.03, 0.0, 0.0, -10.0, 0.0)
    return p

# ================================================================== world-planned transitions (starts, stops, pivots, turn 180)
class Plant:
    """a foot on the ground from f0 to f1: world ankle (x, y) at yaw0; yaw1 = pivot on the ball to that yaw (heel up)"""
    def __init__(self, f0, f1, ank, yaw0, yaw1=None, strike=0.0, strike_len=4, toeoff=0.0, heel_len=5, heel0=0.0, heel0_len=0, pivot_heel=10.0):
        self.f0, self.f1 = f0, f1; self.ank = Vector((ank[0], ank[1], 0.0)); self.yaw0 = yaw0
        self.yaw1 = yaw0 if yaw1 is None else yaw1
        self.strike, self.strike_len, self.toeoff, self.heel_len = strike, strike_len, toeoff, heel_len
        self.heel0, self.heel0_len, self.pivot_heel = heel0, heel0_len, pivot_heel

class WPlan:
    def __init__(self, rig, N):
        self.rig = rig; self.N = N
        self.cap = [(Vector((0.0, 0.0, 0.0)), 0.0)] * (N + 1)
        self.plants = {"L": [], "R": []}
        self.swing_h = {"L": 0.08, "R": 0.08}
        self.body_swing = {}
        self.ball_off = {}
        for sd in LR:
            fr = rig.foot_rest[sd]
            self.ball_off[sd] = Vector((fr["ball"].x - fr["ankle"].x, fr["ball"].y - fr["ankle"].y, 0.0))

    def set_capsule(self, vfun, yawfun):
        """vfun(f) m/s along the heading, yawfun(f) deg; integrated at half frames"""
        pos = Vector((0.0, 0.0, 0.0)); out = []
        for f in range(self.N + 1):
            out.append((pos.copy(), yawfun(f)))
            fm = f + 0.5
            pos = pos + fwd_of(yawfun(fm)) * (vfun(fm) / FPS)
        self.cap = out; self.vfun = vfun; self.yawfun = yawfun

    def cap_at(self, f):
        """capsule at any frame: extrapolated straight along the heading outside 0..N"""
        if 0 <= f <= self.N: return self.cap[int(f)]
        e = 0 if f < 0 else self.N
        c, ps = self.cap[e]
        v = self.vfun(e)
        return c + fwd_of(ps) * (v * (f - e) / FPS), ps

    def to_world(self, f, rel_xy, yaw_rel=0.0):
        c, ps = self.cap[f]
        return c + Rz(ps) @ Vector((rel_xy[0], rel_xy[1], 0.0)), ps + yaw_rel

    def rest_ankle(self, sd):
        a = self.rig.foot_rest[sd]["ankle"]; return Vector((a.x, a.y, 0.0))

    def tuple_to_world(self, f, sd, tup):
        """a capsule-frame foot target tuple (dx, dy, dz, pitch, yaw, tb) -> world ankle xy and yaw"""
        a = self.rest_ankle(sd)
        w, yw = self.to_world(f, (a.x + tup[0], a.y + tup[1]), tup[4])
        return w, yw

    def ankle_at(self, pl, sd, f):
        """ankle world position and yaw of a plant at frame f (ball pivot keeps the ball fixed)"""
        k = inout(clamp01((f - pl.f0) / max(1e-6, pl.f1 - pl.f0))) if pl.yaw1 != pl.yaw0 else 0.0
        yaw = pl.yaw0 + (pl.yaw1 - pl.yaw0) * k
        if pl.yaw1 == pl.yaw0: return pl.ank.copy(), yaw
        ball = pl.ank + Rz(pl.yaw0) @ self.ball_off[sd]
        return ball - Rz(yaw) @ self.ball_off[sd], yaw

    def foot_world(self, sd, f):
        pls = self.plants[sd]
        for i, pl in enumerate(pls):
            if pl.f0 <= f <= pl.f1:
                ank, yaw = self.ankle_at(pl, sd, f)
                pitch = 0.0
                if pl.strike and 0 <= f - pl.f0 < pl.strike_len: pitch = -pl.strike * (1 - ease((f - pl.f0) / pl.strike_len, "out"))
                if pl.heel0:
                    pitch = pl.heel0 if not pl.heel0_len else pl.heel0 * (1 - smooth((f - pl.f0) / pl.heel0_len))
                if pl.toeoff and pl.f1 - f < pl.heel_len: pitch = max(pitch, pl.toeoff * smooth(1 - (pl.f1 - f) / pl.heel_len))
                if pl.yaw1 != pl.yaw0:
                    k = clamp01((f - pl.f0) / max(1e-6, pl.f1 - pl.f0))
                    pitch = max(pitch, pl.pivot_heel * math.sin(math.pi * k))
                return ank, yaw, 0.0, pitch, max(0.0, pitch) * 0.5 if pitch > 0 else pitch, True, None
            if i + 1 < len(pls) and pl.f1 < f < pls[i + 1].f0:
                nb = pls[i + 1]
                u = (f - pl.f1) / (nb.f0 - pl.f1)
                a0, y0 = self.ankle_at(pl, sd, pl.f1); a1, y1 = self.ankle_at(nb, sd, nb.f0)
                ew = swing_ease(u)
                if self.body_swing.get(sd):
                    # turning: the swing is interpolated relative to the body (the leg swings round with the hips instead
                    # of cutting straight across the turning body)
                    c0, p0 = self.cap_at(pl.f1); c1, p1 = self.cap_at(nb.f0); c, pc = self.cap[f]
                    r0 = Rz(-p0) @ (a0 - c0); r1 = Rz(-p1) @ (a1 - c1)
                    ank = c + Rz(pc) @ r0.lerp(r1, ew)
                    yaw = pc + (y0 - p0) + ((y1 - p1) - (y0 - p0)) * smooth(u)
                else:
                    ank = a0.lerp(a1, ew); yaw = y0 + (y1 - y0) * smooth(u)
                d = (a1 - a0).length
                h = self.swing_h[sd] * min(1.0, 0.35 + d / 0.5) * math.sin(math.pi * min(1.0, u * 1.05)) ** 1.2
                t0 = pl.toeoff; st = nb.strike
                if u < 0.3: pitch = t0 * (1 - ease(u / 0.3)) + 5 * ease(u / 0.3)
                elif u < 0.8: pitch = 5 * (1 - ease((u - 0.3) / 0.5))
                else: pitch = -st * ease((u - 0.8) / 0.2)
                return ank, yaw, h, pitch, max(0.0, pitch) * 0.5, False, u
        # before the first / after the last plant: hold
        pl = pls[0] if f < pls[0].f0 else pls[-1]
        ank, yaw = self.ankle_at(pl, sd, pl.f0 if f < pl.f0 else pl.f1)
        return ank, yaw, 0.0, 0.0, 0.0, True, None

    def foot_rel(self, sd, f):
        ank, yaw, dz, pitch, tb, grounded, u = self.foot_world(sd, f)
        c, ps = self.cap[f]
        rel = Rz(-ps) @ (ank - c)
        a = self.rest_ankle(sd)
        self._u = u
        return (rel.x - a.x, rel.y - a.y, dz, pitch, yaw - ps, tb), grounded

    def zmax(self, f, pel_xy, feet):
        rig = self.rig; zm = 1e9; reach = 0.99 * (rig.len["Thigh_L"] + rig.len["Calf_L"])
        for sd in LR:
            tup, gr = feet[sd]
            if not gr and tup[2] > 0.03: continue
            tg = rig.planted(sd, tup[:3], tup[3], tup[4], tup[5])
            hip = rig.rest[f"Thigh_{sd}"].translation
            hor = math.hypot(tg[0].x - hip.x - pel_xy[0], tg[0].y - hip.y - pel_xy[1])
            if hor < reach: zm = min(zm, tg[0].z + math.sqrt(reach * reach - hor * hor) - hip.z)
        return zm

def fit_z(zdes, zmx, k=2):
    """pelvis height under the reach limit: min, erode, blur (never above the limit), ends kept"""
    n = len(zdes)
    z = [min(a, b) for a, b in zip(zdes, zmx)]
    er = [min(z[max(0, min(n - 1, i + j))] for j in range(-k, k + 1)) for i in range(n)]
    w = [math.cos(0.5 * math.pi * j / (k + 1)) ** 2 for j in range(-k, k + 1)]
    out = []
    for i in range(n):
        s = sum(er[max(0, min(n - 1, i + j))] * w[j + k] for j in range(-k, k + 1)) / sum(w)
        out.append(min(s, zmx[i]))
    return out

def v_profile(v0, v1, N, frac):
    """speed from v0 to v1 over N frames whose distance is frac of (max(v0,v1) * T); mixes x^3, smoothstep, 1-(1-x)^3"""
    def g(x, a, b):   # a: weight of the slow curve, b: weight of the fast curve, rest smoothstep
        x = clamp01(x)
        return a * x ** 3 + b * (1 - (1 - x) ** 3) + (1 - a - b) * (x * x * (3 - 2 * x))
    if frac <= 0.5: a = clamp01((0.5 - frac) / 0.25); b = 0.0
    else: a = 0.0; b = clamp01((frac - 0.5) / 0.25)
    if v1 >= v0:
        return lambda f: v0 + (v1 - v0) * g(f / N, a, b)
    return lambda f: v1 + (v0 - v1) * (1 - g(f / N, b, a))

def crossfade_feet(tup_a, tup_b, k):
    return tuple(lerp(x, y, k) for x, y in zip(tup_a, tup_b))

def reach_ok(rig, sd, tup, pel, reach):
    tg = rig.planted(sd, tup[:3], tup[3], tup[4], tup[5])
    hip = rig.rest[f"Thigh_{sd}"].translation
    return (tg[0] - hip - Vector(pel)).length <= reach

def fix_reach(rig, sd, tup, grounded, u, pel, reach):
    """keep a foot target reachable without the IK dragging it: a planted foot behind the hip lifts its heel (pivot on the
    ball), a swinging foot rises early in the swing or is drawn towards the hip late in the swing"""
    if reach_ok(rig, sd, tup, pel, reach): return tup
    t2 = list(tup)
    hip = rig.rest[f"Thigh_{sd}"].translation; a = rig.foot_rest[sd]["ankle"]
    def bis(setter, lo, hi):
        for _ in range(14):
            mid = 0.5 * (lo + hi); setter(mid)
            if reach_ok(rig, sd, tuple(t2), pel, reach): hi = mid
            else: lo = mid
        setter(hi)
    if grounded:
        if a.y + tup[1] > hip.y + pel[1] + 0.02:           # behind the hip: heel up
            def sp(v): t2[3] = v; t2[5] = v
            bis(sp, tup[3], 80.0)
        return tuple(t2)
    def sz(v): t2[2] = v
    bis(sz, tup[2], tup[2] + 0.5); up = list(t2); t2[:] = list(tup)
    x0, y0 = tup[0], tup[1]; hx, hy = hip.x + pel[0] - a.x, hip.y + pel[1] - a.y
    def sh(k): t2[0] = x0 + (hx - x0) * k; t2[1] = y0 + (hy - y0) * k
    bis(sh, 0.0, 1.0); drawn = list(t2)
    w = smooth(clamp01(((u if u is not None else 1.0) - 0.35) / 0.4))
    return tuple(lerp(p_, q_, w) for p_, q_ in zip(up, drawn))

class Transition:
    """shared machinery: capsule-frame feet from a WPlan, pelvis fit, upper body blend, head-in / tail-out crossfades"""
    def __init__(self, rig, plan, ub, pel, head_pose=None, head_k=0, tail_pose=None, tail_k=0, zdes=None):
        self.rig = rig; self.plan = plan; self.N = plan.N
        self.ub = ub; self.pel = pel
        # head / tail poses: a fixed dict (idle) or a function of the clip frame (a loop sampled at the matching loop time,
        # so planted feet keep moving with the belt through the crossfade)
        wrap = lambda x: (x if callable(x) or x is None else (lambda f, x=x: x))
        self.head_pose, self.head_k, self.tail_pose, self.tail_k = wrap(head_pose), head_k, wrap(tail_pose), tail_k
        N = self.N
        self.raw = []
        for f in range(N + 1):
            ft = {}
            w = self._blend_w(f)
            for sd in LR:
                tup, gr = plan.foot_rel(sd, f); u = plan._u
                if w[0] > 0 and head_pose is not None: tup = crossfade_feet(tup, self.head_pose(f)["ft" + sd], w[0])
                if w[1] > 0 and tail_pose is not None: tup = crossfade_feet(tup, self.tail_pose(f)["ft" + sd], w[1])
                ft[sd] = (tup, gr, u)
            self.raw.append(ft)
        reach = 0.993 * (rig.len["Thigh_L"] + rig.len["Calf_L"])
        zd = zdes if zdes else [pel(f)[2] for f in range(N + 1)]
        # heels / swinging feet adapt to the desired pelvis first; only what is still out of reach lowers the pelvis
        def adapt(zs):
            out = []
            for f in range(N + 1):
                x, y, _ = pel(f)
                out.append({sd: (fix_reach(rig, sd, tup, gr, u, (x, y, zs[f]), reach), gr) for sd, (tup, gr, u) in self.raw[f].items()})
            return out
        feet = adapt(zd)
        zm = [plan.zmax(f, pel(f)[:2], feet[f]) for f in range(N + 1)]
        self.z = fit_z(zd, zm)
        self.feet = adapt(self.z)

    def _blend_w(self, f):
        hw = (1 - smooth(f / self.head_k)) if self.head_k and f < self.head_k else 0.0
        tw = smooth(1 - (self.N - f) / self.tail_k) if self.tail_k and f > self.N - self.tail_k else 0.0
        return hw, tw

    def pose(self, f):
        p = self.ub(f)
        x, y, _ = self.pel(f)
        p["pel"] = (x, y, self.z[f])
        for sd in LR: p["ft" + sd] = self.feet[f][sd][0]
        hw, tw = self._blend_w(f)
        if hw > 0 and self.head_pose: p = blend_pose(p, self.head_pose(f), hw)
        if tw > 0 and self.tail_pose: p = blend_pose(p, self.tail_pose(f), tw)
        if (hw > 0 and self.head_pose) or (tw > 0 and self.tail_pose):
            # the crossfade moved the pelvis: keep the feet reachable for the pose actually applied
            reach = 0.993 * (self.rig.len["Thigh_L"] + self.rig.len["Calf_L"])
            for sd in LR:
                _, gr, u = self.raw[f][sd]
                p["ft" + sd] = fix_reach(self.rig, sd, p["ft" + sd], gr, u, p["pel"], reach)
        return p

    def fn(self, f):
        return apply(self.rig, self.pose(f))

def damped(f, period, tau):
    return math.cos(TAU * f / period) * math.exp(-f / tau)

def build_transitions(rig, walk, run):
    """returns {name: (N, fn, events, speed, notes, extra)} for the start / stop / pivot / turn clips (left foot variants;
    the controller mirrors them for the right foot)"""
    out = {}
    idle = idle2_pose
    # ---------------- Walk_Start: one step with the left foot into walk frame 0 (left heel strike)
    w0 = walk.pose(0)
    plan = WPlan(rig, 1)
    a_idle = {sd: Vector(idle(0)["ft" + sd][:2]) for sd in LR}
    yR0 = w0["ftR"][1]; yI = idle(0)["ftR"][1]
    d = yR0 - yI                                    # distance the capsule travels while the right foot stays planted
    N = 18
    frac = d / (walk.speed * N / FPS)
    plan = WPlan(rig, N); plan.swing_h["L"] = 0.075
    plan.set_capsule(v_profile(0.0, walk.speed, N, frac), lambda f: 0.0)
    i0 = idle(0)
    aL0, yL0 = plan.tuple_to_world(0, "L", i0["ftL"]); aR0, yR0w = plan.tuple_to_world(0, "R", i0["ftR"])
    aLN, yLN = plan.tuple_to_world(N, "L", w0["ftL"])
    plan.plants["L"] = [Plant(0, 2, aL0, yL0, toeoff=22, heel_len=3), Plant(N, N + 4, aLN, yLN, strike=walk.g["strike"])]
    plan.plants["R"] = [Plant(0, N, aR0, yR0w, toeoff=w0["ftR"][3], heel_len=8)]
    def ub_ws(f, N=N):
        k = inout(clamp01(f / N))
        b = blend_pose(idle(0), w0, k)
        lean = 7.0 * math.sin(math.pi * clamp01((f + 1.5) / (N * 0.9)))        # head leads the lean by 1-2 frames
        return add(b, sp=(lean * 0.35, 0, 0), spu=(lean * 0.3, 0, 0), ch=(lean * 0.1, 0, 0), nk=(-lean * 0.2, 0, 0), hd=(-lean * 0.25, 0, 0),
                   pelr=(lean * 0.3, 0, 0))
    def pel_ws(f, N=N):
        k = inout(clamp01(f / N))
        x0 = idle(0)["pel"][0]; xN = w0["pel"][0]
        # weight moves over the right (support) foot before the left lifts, COM a little ahead of the support foot
        x = x0 + (xN - x0) * k - 0.022 * math.sin(math.pi * clamp01(f / (N * 0.75)))
        y = -0.03 * math.sin(math.pi * clamp01(f / N))
        z = lerp(idle(0)["pel"][2], w0["pel"][2], k) - 0.012 * math.sin(math.pi * clamp01(f / N))
        return (x, y, z)
    tr = Transition(rig, plan, ub_ws, pel_ws, head_pose=idle(0), head_k=2, tail_pose=lambda f, N=N: walk.pose((f - N) % walk.N), tail_k=4)
    out["Walk_Start"] = (N, tr.fn, [(N - 2, "OnFootstep", "L")], walk.speed, "idle -> walk: weight to the right foot, lean, one step with the left foot, ends on walk frame 0 (left heel strike); mirror for the right foot",
                         dict(root_speed=[round(plan.vfun(f), 3) for f in range(N + 1)], root_distance=round(plan.cap[N][0].length, 3), ends_in="Walk@0"))
    # ---------------- Walk_Stop: from walk frame 0, the right foot comes alongside, counter-lean, settle into idle frame 0
    N = 22; Nd = 15
    i0 = idle(0)
    yL = w0["ftL"][1]; d = i0["ftL"][1] - yL        # the planted left foot must end at its idle place
    frac = d / (walk.speed * Nd / FPS)
    plan = WPlan(rig, N); plan.swing_h["R"] = 0.07
    vdec = v_profile(walk.speed, 0.0, Nd, frac)
    plan.set_capsule(lambda f: vdec(f) if f < Nd else 0.0, lambda f: 0.0)
    aL0, yL0 = plan.tuple_to_world(0, "L", w0["ftL"]); aR0, yR0w = plan.tuple_to_world(0, "R", w0["ftR"])
    aRN, yRN = plan.tuple_to_world(N, "R", i0["ftR"])
    plan.plants["L"] = [Plant(0, N + 4, aL0, yL0, strike=walk.g["strike"], strike_len=3)]
    plan.plants["R"] = [Plant(-4, 3, aR0, yR0w, heel0=w0["ftR"][3], heel0_len=0, toeoff=walk.g["toeoff"], heel_len=4), Plant(14, N + 4, aRN, yRN, strike=6)]
    def ub_wst(f, N=N):
        k = inout(clamp01(f / 16.0))
        b = blend_pose(w0, idle(0), k)
        # arms keep swinging past the body line and settle (pendulum), counter-lean back at the brake, then settle
        osc = damped(f, 18.0, 6.0) - (1 - k) * 0.0
        for sd, sg in (("L", -1.0), ("R", 1.0)):
            a = b["a" + sd]; ai = idle(0)["a" + sd]; aw = w0["a" + sd]
            b["a" + sd] = (ai[0] + (aw[0] - ai[0]) * osc if f < 16 else lerp(ai[0] + (aw[0] - ai[0]) * osc, ai[0], smooth((f - 16) / 6.0)), a[1], a[2], a[3])
        back = -9.0 * math.sin(math.pi * clamp01((f - 5) / 12.0)) + 3.0 * math.sin(math.pi * clamp01((f - 14) / 8.0))
        return add(b, pelr=(back * 0.35, 0, 0), sp=(back * 0.3, 0, 0), spu=(back * 0.25, 0, 0), ch=(back * 0.1, 0, 0),
                   nk=(-back * 0.25, 0, 0), hd=(-back * 0.3, 0, 0))
    def pel_wst(f, N=N):
        k = inout(clamp01(f / 16.0))
        x = lerp(w0["pel"][0], i0["pel"][0], k) + 0.018 * math.sin(math.pi * clamp01((f - 2) / 12.0))
        y = -0.035 * math.sin(math.pi * clamp01((f - 6) / 14.0))       # COM overshoots forward, settles back
        z = lerp(w0["pel"][2], i0["pel"][2], k) - 0.015 * math.sin(math.pi * clamp01((f - 8) / 12.0))
        return (x, y, z)
    tr = Transition(rig, plan, ub_wst, pel_wst, head_pose=lambda f: walk.pose(f % walk.N), head_k=3, tail_pose=idle(0), tail_k=5)
    out["Walk_Stop"] = (N, tr.fn, [(14, "OnFootstep", "R")], 0.0, "walk frame 0 -> idle frame 0: brake on the left foot, right foot alongside, counter-lean, arms swing past and settle; mirror for the right foot",
                        dict(root_speed=[round(plan.vfun(f) if f < Nd else 0.0, 3) for f in range(N + 1)], root_distance=round(plan.cap[N][0].length, 3), starts_in="Walk@0", ends_in="Idle@0"))
    # ---------------- Run_Start: lean, right leg drives, left foot lands on run frame 0
    r0 = run.pose(0)
    N = 14
    rto = run.foot("R", run.D)[0]                     # right foot position at toe-off in the run
    plan = WPlan(rig, N); plan.swing_h["L"] = 0.12; plan.swing_h["R"] = 0.18
    vfun = v_profile(0.0, run.speed, N, 0.47)
    plan.set_capsule(vfun, lambda f: 0.0)
    i0 = idle(0)
    aL0, yL0 = plan.tuple_to_world(0, "L", i0["ftL"]); aR0, yR0w = plan.tuple_to_world(0, "R", i0["ftR"])
    # right toe-off when the capsule has moved past the right foot as far as the run's toe-off
    need = rto[1] - i0["ftR"][1]; fto = N - 3
    for f in range(N + 1):
        if plan.cap[f][0].length >= need * 0.8: fto = min(f, N - 3); break
    aLN, yLN = plan.tuple_to_world(N, "L", r0["ftL"]); aRN, yRN = plan.tuple_to_world(N, "R", r0["ftR"])
    plan.plants["L"] = [Plant(0, 1, aL0, yL0, toeoff=25, heel_len=2), Plant(N, N + 4, aLN, yLN, strike=run.g["strike"])]
    plan.plants["R"] = [Plant(0, fto, aR0, yR0w, toeoff=run.g["toeoff"], heel_len=max(3, fto - 2)), Plant(N + 6, N + 8, aRN + fwd_of(0) * 0.5, yRN)]
    def ub_rs(f, N=N):
        k = inout(clamp01(f / N))
        b = blend_pose(idle(0), r0, k)
        lean = 13.0 * math.sin(math.pi * clamp01((f + 1.5) / (N * 1.25)))
        return add(b, pelr=(lean * 0.35, 0, 0), sp=(lean * 0.35, 0, 0), spu=(lean * 0.25, 0, 0), ch=(lean * 0.1, 0, 0), nk=(-lean * 0.3, 0, 0), hd=(-lean * 0.35, 0, 0))
    def pel_rs(f, N=N):
        k = inout(clamp01(f / N))
        x = lerp(i0["pel"][0], r0["pel"][0], k) - 0.02 * math.sin(math.pi * clamp01(f / (N * 0.7)))
        y = -0.05 * math.sin(math.pi * clamp01(f / N))
        z = lerp(i0["pel"][2], r0["pel"][2], k) - 0.03 * math.sin(math.pi * clamp01(f / (N * 0.8)))
        return (x, y, z)
    tr = Transition(rig, plan, ub_rs, pel_rs, head_pose=idle(0), head_k=2, tail_pose=lambda f, N=N: run.pose((f - N) % run.N), tail_k=5)
    out["Run_Start"] = (N, tr.fn, [(N - 2, "OnFootstep", "L")], run.speed, "idle -> run: forward lean, the right leg drives off, left foot lands on run frame 0; mirror for the right foot",
                        dict(root_speed=[round(plan.vfun(f), 3) for f in range(N + 1)], root_distance=round(plan.cap[N][0].length, 3), ends_in="Run@0"))
    # ---------------- Run_Stop: brake step with the right foot, counter-lean, left comes alongside, settle
    N = 26; Nd = 16
    plan = WPlan(rig, N); plan.swing_h["L"] = 0.07; plan.swing_h["R"] = 0.12
    vdec = v_profile(run.speed, 0.0, Nd, 0.5)
    plan.set_capsule(lambda f: vdec(f) if f < Nd else 0.0, lambda f: 0.0)
    i0 = idle(0)
    aL0, yL0 = plan.tuple_to_world(0, "L", r0["ftL"])
    aRN, yRN = plan.tuple_to_world(N, "R", i0["ftR"]); aLN, yLN = plan.tuple_to_world(N, "L", i0["ftL"])
    fR = 8; fLoff = 10; fLon = 19
    r0R = plan.tuple_to_world(0, "R", run.foot("R", (0.0 + run.ph0["R"]) % 1.0)[0])
    # the right foot is in the air at frame 0 (swinging): a virtual plant behind at its lift-off keeps the swing continuous
    ph_R = (run.ph0["R"]) % 1.0; v_sw = (ph_R - run.D) / (1 - run.D)
    back_f = -int(round(v_sw * (1 - run.D) * run.N))
    toR = plan.tuple_to_world(0, "R", run.foot("R", run.D)[0])
    toR = (toR[0] + Vector((0.0, run.speed * (-back_f) / FPS, 0.0)), toR[1])     # the capsule was behind at that frame
    plan.plants["R"] = [Plant(back_f - 3, back_f, toR[0], toR[1], toeoff=run.g["toeoff"], heel_len=3),
                        Plant(fR, N + 4, aRN, yRN, strike=8, strike_len=3)]
    plan.plants["L"] = [Plant(0, fLoff, aL0, yL0, strike=run.g["strike"], strike_len=3, toeoff=45, heel_len=4), Plant(fLon, N + 4, aLN, yLN, strike=4)]
    def ub_rst(f, N=N):
        k = inout(clamp01(f / 20.0))
        b = blend_pose(r0, idle(0), k)
        osc = damped(f, 16.0, 6.0)
        for sd in LR:
            a = b["a" + sd]; ai = idle(0)["a" + sd]; aw = r0["a" + sd]
            fl = ai[0] + (aw[0] - ai[0]) * osc
            if f > 18: fl = lerp(fl, ai[0], smooth((f - 18) / 6.0))
            b["a" + sd] = (fl, a[1], a[2], a[3])
            ei = idle(0)["e" + sd]; ew = r0["e" + sd]
            b["e" + sd] = lerp(ew, ei, smooth(clamp01((f - 6) / 16.0)))
        back = -13.0 * math.sin(math.pi * clamp01((f - 3) / 13.0)) + 4.0 * math.sin(math.pi * clamp01((f - 15) / 9.0))
        return add(b, pelr=(back * 0.35, 0, 0), sp=(back * 0.3, 0, 0), spu=(back * 0.25, 0, 0), ch=(back * 0.1, 0, 0), nk=(-back * 0.25, 0, 0), hd=(-back * 0.3, 0, 0))
    def pel_rst(f, N=N):
        k = inout(clamp01(f / 20.0))
        x = lerp(r0["pel"][0], i0["pel"][0], k) + 0.02 * math.sin(math.pi * clamp01((f - 6) / 12.0))
        y = -0.045 * math.sin(math.pi * clamp01((f - 8) / 16.0))
        z = lerp(r0["pel"][2], i0["pel"][2], k) - 0.05 * math.sin(math.pi * clamp01((f - 4) / 16.0))
        return (x, y, z)
    tr = Transition(rig, plan, ub_rst, pel_rst, head_pose=lambda f: run.pose(f % run.N), head_k=3, tail_pose=idle(0), tail_k=5)
    out["Run_Stop"] = (N, tr.fn, [(fR, "OnFootstep", "R"), (fLon, "OnFootstep", "L")], 0.0,
                       "run frame 0 -> idle frame 0: brake step with the right foot, counter-lean back, left foot alongside, arms swing past and settle; mirror for the right foot",
                       dict(root_speed=[round(plan.vfun(f) if f < Nd else 0.0, 3) for f in range(N + 1)], root_distance=round(plan.cap[N][0].length, 3), starts_in="Run@0", ends_in="Idle@0"))
    # ---------------- Run_Pivot_180 (turn left): capsule yaw 0 -> 180 over 18 frames (300 deg/s, the motor's run cap),
    # speed dips to 55 % and back; plant, turn over the outside (right) foot on its ball, step out in the new direction
    N = 20; Nt = 18
    vmin = run.speed * 0.55
    yawf = lambda f: 180.0 * inout(clamp01(f / Nt))
    vf = lambda f: run.speed - (run.speed - vmin) * math.sin(math.pi * clamp01(f / N)) ** 1.5
    plan = WPlan(rig, N); plan.swing_h["L"] = 0.1; plan.swing_h["R"] = 0.1; plan.body_swing = {"L": True, "R": True}
    plan.set_capsule(vf, yawf)
    aL0, yL0 = plan.tuple_to_world(0, "L", r0["ftL"])
    # right foot: plants across at frame 7 turned about 40 deg into the turn (relative to the capsule), pivots on its ball
    # while the body comes round, then drives out; the pelvis turns early with it (no knee twisted against the foot)
    fRon, fRoff = 7, 14
    cap7, ps7 = plan.cap[fRon]; ps14 = plan.cap[fRoff][1]
    aR7 = cap7 + Rz(ps7) @ Vector((-0.19, -0.12, 0.0))
    aLN, yLN = plan.tuple_to_world(N, "L", r0["ftL"])
    aRN, yRN = plan.tuple_to_world(N, "R", run.foot("R", run.ph0["R"])[0])
    plan.plants["L"] = [Plant(0, 5, aL0, yL0, strike=run.g["strike"], strike_len=2, toeoff=35, heel_len=3), Plant(N, N + 4, aLN, yLN, strike=4)]
    plan.plants["R"] = [Plant(back_f - 3, back_f, toR[0], toR[1], toeoff=run.g["toeoff"], heel_len=3),
                        Plant(fRon, fRoff, aR7, ps7 + 35.0, yaw1=ps14 + 8.0, strike=0, toeoff=50, heel_len=4, pivot_heel=16.0),
                        Plant(N + 8, N + 10, aRN, yRN)]
    def ub_piv(f, N=N):
        # head leads the capsule's turn, chest and pelvis follow into it; lean into the turn; arms counterbalance
        k = clamp01(f / Nt); bump = math.sin(math.pi * k)
        head = 30.0 * math.sin(math.pi * clamp01((f + 2) / (Nt + 1)))
        chest = 18.0 * math.sin(math.pi * clamp01((f + 1) / (Nt + 2)))
        pelv = 9.0 * math.sin(math.pi * clamp01((f - 1) / (Nt + 1)))
        roll = 8.0 * bump
        back = -8.0 * math.sin(math.pi * clamp01(f / 10.0)) + 6.0 * math.sin(math.pi * clamp01((f - 10) / 10.0))
        b = add(P(), pelr=(back * 0.3 + run.g["lean"] * 0.45, roll * 0.6, pelv), sp=(back * 0.3 + 4.4, roll * 0.3, (chest - pelv) * 0.3),
                spu=(back * 0.25 + 3.4, roll * 0.2, (chest - pelv) * 0.35), ch=(-0.8, 0, (chest - pelv) * 0.35),
                nk=(-3.0, -roll * 0.4, (head - chest) * 0.6), hd=(-7.0, -roll * 0.3, (head - chest) * 0.4),
                aL=(-15 + 28 * bump, -30 + 10 * bump, 0, 4), eL=85, aR=(12 - 26 * bump, -31 + 4 * bump, 0, 2), eR=90, fL=(46, 30), fR=(49, 30))
        wr = smooth(clamp01((f - (N - 6)) / 6.0))
        return blend_pose(b, r0, wr) if f > N - 6 else (blend_pose(r0, b, smooth(clamp01(f / 3.0))) if f < 3 else b)
    def pel_piv(f, N=N):
        bump = math.sin(math.pi * clamp01(f / Nt))
        return (0.04 * bump, -0.02 * bump, r0["pel"][2] - 0.04 * bump)
    tr = Transition(rig, plan, ub_piv, pel_piv, head_pose=lambda f: run.pose(f % run.N), head_k=3, tail_pose=lambda f, N=N: run.pose((f - N) % run.N), tail_k=5)
    out["Run_Pivot_180"] = (N, tr.fn, [(fRon, "OnFootstep", "R"), (N - 1, "OnFootstep", "L")], run.speed,
                            "180 deg reversal at run speed, turning left (mirror: right): plant, turn over the right foot on its ball, head leads, lean into the turn, out on run frame 0. Authored in the capsule frame: capsule yaw 0 -> 180 over frames 0-18 (inout), speed dips to 55 %",
                            dict(root_speed=[round(vf(f), 3) for f in range(N + 1)], root_yaw=[round(yawf(f), 2) for f in range(N + 1)], root_distance=round(plan.cap[N][0].length, 3), starts_in="Run@0", ends_in="Run@0"))
    # ---------------- Turn_180 (standing, left): head, chest, pelvis lead, left foot steps out on its ball, right steps round,
    # left pivots to 180; capsule yaw follows the body 0 -> 180 over frames 2-28
    N = 34
    yawf = lambda f: 180.0 * inout(clamp01((f - 2) / 26.0))
    plan = WPlan(rig, N); plan.swing_h["L"] = 0.06; plan.swing_h["R"] = 0.07; plan.body_swing = {"L": True, "R": True}
    plan.set_capsule(lambda f: 0.0, yawf)
    i0 = idle(0)
    aL0, yL0 = plan.tuple_to_world(0, "L", i0["ftL"]); aR0, yR0w = plan.tuple_to_world(0, "R", i0["ftR"])
    aLN, yLN = plan.tuple_to_world(N, "L", i0["ftL"]); aRN, yRN = plan.tuple_to_world(N, "R", i0["ftR"])
    # left: steps out and opens to ~90 deg (lands on frame 12), the right pivots on its ball then steps round (lands on
    # frame 24), the left closes the stance with a small step (frames 25-31); every step lands beside the body, no crossing
    aL1 = Rz(90.0) @ aL0
    plan.plants["L"] = [Plant(0, 4, aL0, yL0, toeoff=15, heel_len=3), Plant(12, 25, aL1, yL0 + 90.0, strike=3, toeoff=10, heel_len=3),
                        Plant(31, N + 4, aLN, yLN, strike=2)]
    plan.plants["R"] = [Plant(0, 14, aR0, yR0w, yaw1=yR0w + 55.0, toeoff=20, heel_len=4, pivot_heel=8.0), Plant(24, N + 4, aRN, yRN, strike=3)]
    def ub_t180(f, N=N):
        head = 45.0 * math.sin(math.pi * clamp01(f / 24.0)) ** 1.2
        chest = 22.0 * math.sin(math.pi * clamp01((f - 1) / 25.0))
        pelv = 6.0 * math.sin(math.pi * clamp01((f - 3) / 25.0)) - 5.0 * math.sin(math.pi * clamp01((f - 18) / 14.0))
        trail = 12.0 * math.sin(math.pi * clamp01((f - 3) / 26.0))
        b = idle(f)
        b = add(b, pelr=(0, 0, pelv), sp=(2.0 * math.sin(math.pi * clamp01(f / N)), 0, (chest - pelv) * 0.3), spu=(0, 0, (chest - pelv) * 0.35), ch=(0, 0, (chest - pelv) * 0.35),
                nk=(0, 0, (head - chest) * 0.6), hd=(0, 0, (head - chest) * 0.4),
                aL=(0, 3 * math.sin(math.pi * clamp01(f / N)), 0, -trail * 0.6), aR=(0, 3 * math.sin(math.pi * clamp01(f / N)), 0, trail * 0.6),
                eL=6 * math.sin(math.pi * clamp01(f / N)), eR=6 * math.sin(math.pi * clamp01(f / N)))
        return b
    def pel_t180(f, N=N):
        i = idle(0)["pel"]
        # weight over the right foot while the left steps, over the left while the right steps round
        x = i[0] - 0.03 * math.sin(math.pi * clamp01(f / 12.0)) + 0.035 * math.sin(math.pi * clamp01((f - 12) / 14.0))
        return (x, i[1], i[2] - 0.02 * math.sin(math.pi * clamp01(f / N)))
    tr = Transition(rig, plan, ub_t180, pel_t180, head_pose=idle(0), head_k=2, tail_pose=idle(0), tail_k=4)
    out["Turn_180"] = (N, tr.fn, [(12, "OnFootstep", "L"), (24, "OnFootstep", "R"), (31, "OnFootstep", "L")], 0.0,
                       "standing 180 deg turn to the left (mirror: right) in 34 frames: head, chest, pelvis, then the feet (3 steps, ball pivots). Authored in the capsule frame: capsule yaw 0 -> 180 over frames 2-28 (inout)",
                       dict(root_yaw=[round(yawf(f), 2) for f in range(N + 1)], root_speed=0.0, root_distance=0.0, starts_in="Idle@0", ends_in="Idle@0"))
    return out

# ================================================================== bare hands (batch 2)
# Keys give the body (stance, hips, trunk, head) and the fist targets (grip point, knuckle direction = edge, thumb side =
# blade), solved into arm angles with pf_weapon_ik (same machinery as the weapon clips). In-betweens follow the fist in
# fist space, so a straight punch leaves and returns along one line. Kinetic chain: legs / hips turn first, the chest 1-2
# frames later, the elbow extends last; the guard hand stays at the chin; wrists aligned (knuckles along the strike).
def _V(x, y, z): return Vector((x, y, z))
def _N(x, y, z): return Vector((x, y, z)).normalized()
FIST = (82.0, 58.0)

def ball_pivot(rig, sd, tup, yaw_new, pitch=None):
    """rotate a planted foot to yaw_new about its ball (the heel swings, the ball stays): returns the new foot tuple"""
    fr = rig.foot_rest[sd]
    bo = Vector((fr["ball"].x - fr["ankle"].x, fr["ball"].y - fr["ankle"].y, 0.0))
    d = Rz(tup[4]) @ bo - Rz(yaw_new) @ bo
    p = tup[3] if pitch is None else pitch
    return (tup[0] + d.x, tup[1] + d.y, tup[2], p, yaw_new, max(0.0, p) * 0.5)

G_FT_L = (0.03, -0.13, 0.0, 0.0, 14.0, 0.0)
G_FT_R = (-0.035, 0.14, 0.0, 5.0, -32.0, 2.5)
def guard_body(**kw):
    d = dict(pel=(0.0, 0.01, -0.06), pelr=(4.0, 0.0, -16.0), sp=(6.0, 0.0, -4.0), spu=(3.0, 0.0, -3.0), ch=(2.0, 0.0, -4.0),
             nk=(-1.0, 0.0, 14.0), hd=(-3.0, 0.0, 12.0), ftL=G_FT_L, ftR=G_FT_R, fL=FIST, fR=FIST,
             aL=(55.0, -18.0, 0.0, 25.0), eL=100.0, aR=(45.0, -22.0, 0.0, 30.0), eR=120.0, cL=(2.0, -3.0), cR=(2.0, 0.0))
    d.update(kw); return P(**d)
# fist targets in the guard: lead (left) fist forward at cheek height, rear (right) fist at the chin; fists near vertical
G_L = dict(pos=_V(0.11, -0.33, 1.46), blade=_N(-0.35, 0.25, 0.9), edge=_N(0.05, -0.85, 0.5), w_edge=0.4)
G_R = dict(pos=_V(-0.075, -0.19, 1.49), blade=_N(0.3, 0.3, 0.9), edge=_N(0.0, -0.75, 0.65), w_edge=0.4)
def Kf(frame, body, targets, e="smooth"): return (frame, body, targets, e)

class Reach:
    """fist targets measured from the posed body: shoulder / head positions after the body pose, arm length from the rest"""
    def __init__(self, rig):
        import pf_pose_preview as PV
        self.rig = rig; arm = rig.obj; self.L = {}
        for sd in LR:
            sh = rig.rest[f"UpperArm_{sd}"].translation; el = rig.rest[f"LowerArm_{sd}"].translation
            g = PV.grip_matrix(arm, sd).translation
            self.L[sd] = (el - sh).length + (g - el).length
    def _pose(self, body):
        self.rig.reset(); CH.apply(self.rig, body); bpy.context.view_layer.update()
    def sh(self, body, sd):
        self._pose(body); return self.rig.obj.pose.bones[f"UpperArm_{sd}"].head.copy()
    def head(self, body):
        self._pose(body); return self.rig.obj.pose.bones["Head"].head.copy()
    def straight(self, body, sd, frac, lat=0.12, z=1.45):
        """a straight punch: from the shoulder towards a point in front of the body centre at chin height"""
        s0 = self.sh(body, sd); sg = 1.0 if sd == "L" else -1.0
        d = Vector((-sg * lat, -1.0, (z - s0.z) / 0.6)).normalized()
        return s0 + d * frac * self.L[sd], d
    def rel(self, body, sd, off):
        return self.sh(body, sd) + Vector(off)
    def chin(self, body, off=(0.0, -0.13, -0.17)):
        return self.head(body) + Vector(off)

def bare_defs(rig):
    C = {}; R = Reach(rig)
    g = guard_body()
    # guard: lead fist about 30 cm in front of the lead shoulder at cheek height, rear fist at the chin; thumbs up / back,
    # the wrist is left near straight (no knuckle target in the guard)
    def GL(body, dy=0.0, dz=0.0):
        return dict(pos=R.rel(body, "L", (-0.075, -0.30 + dy, 0.03 + dz)), blade=_N(-0.35, 0.25, 0.9), w_blade=0.5)
    def GR(body, dy=0.0, dz=0.0):
        return dict(pos=R.chin(body, (-0.075, -0.14 + dy, -0.17 + dz)), blade=_N(0.3, 0.3, 0.9), w_blade=0.5)
    def ST(body, sd, frac, lat=0.12):
        p, d = R.straight(body, sd, frac, lat)
        sg = 1.0 if sd == "L" else -1.0
        return dict(pos=p, blade=_N(-sg, 0, 0.15), edge=d, w_edge=0.8)          # palm down, knuckles along the strike line
    guard = Kf(0, g, {"L": GL(g), "R": GR(g)})
    # ---- guard idle (upper-body layer, loop): breathing, small weight bounce, fists alive
    g20 = guard_body(pel=(0.004, 0.006, -0.068), spu=(3.8, 0, -3), ch=(3.0, 0, -4))
    g40 = guard_body(pel=(-0.004, 0.012, -0.062), spu=(2.6, 0, -3), ch=(1.4, 0, -4))
    C["BareHand_Idle"] = dict(frames=60, loop=True, keys=[
        guard, Kf(20, g20, {"L": GL(g20, -0.006, -0.006), "R": GR(g20, 0.004, -0.006)}), Kf(40, g40, {"L": GL(g40, 0.006, 0.006), "R": GR(g40, -0.003, 0.005)}),
        Kf(60, g, {"L": GL(g), "R": GR(g)})], events=[], no_refine=True, notes="relaxed fighting guard: lead fist forward at cheek height, rear fist at the chin, elbows in front of the ribs, chin down, breathing")
    # ---- jab (left, lead): contact f5 of 14
    j1 = guard_body(pel=(0.0, 0.012, -0.068))
    j3 = guard_body(pel=(0.0, -0.015, -0.066), pelr=(4, 0, -18), sp=(6, 0, -5), spu=(3, 0, -4), ch=(2, 0, -6), nk=(-1, 0, 16), hd=(-3, 0, 14))
    j5 = guard_body(pel=(0.0, -0.03, -0.065), pelr=(4, 0, -20), sp=(6, 0, -6), spu=(3, 0, -5), ch=(2, 0, -7), nk=(-1, 0, 19), hd=(-3, 0, 16))
    j9 = guard_body(pel=(0.0, -0.01, -0.063))
    jab_hit = ST(j5, "L", 0.95)
    mid = dict(jab_hit); mid["pos"] = GL(j3)["pos"].lerp(ST(j3, "L", 0.95)["pos"], 0.4); mid["w_edge"] = 0.4
    C["BareHand_Punch_1"] = dict(frames=14, loop=False, keys=[
        guard,
        Kf(1, j1, {"L": GL(j1, 0.01, -0.005), "R": GR(j1)}, "out"),
        Kf(3, j3, {"L": mid, "R": GR(j3)}, "in"),
        Kf(5, j5, {"L": jab_hit, "R": GR(j5)}, "in"),
        Kf(6, j5, {"L": dict(jab_hit, pos=jab_hit["pos"] + jab_hit["edge"] * 0.012), "R": GR(j5)}, "out"),
        Kf(9, j9, {"L": GL(j9, -0.07), "R": GR(j9)}, "smooth"),
        Kf(14, g, {"L": GL(g), "R": GR(g)}, "inout")],
        events=[(1, "OnAttackStart", "jab"), (4, "OnAttackActive", "jab"), (5, "OnAttackHit", "jab"), (7, "OnAttackEnd", "jab")],
        contact=5, notes="lead jab: tiny dip, shoulder turns in, fist out palm-down on a straight line (arm ~95 % extended, never locked), rear fist at the chin, back along the same line")
    # ---- cross (right, rear): hips then chest, rear heel lifts and pivots on the ball, contact f6 of 16
    c2 = guard_body(pel=(0.005, -0.01, -0.07), pelr=(5, 0, -8), sp=(6, 0, -3), spu=(3, 0, -3), ch=(2, 0, -4), nk=(-1, 0, 5), hd=(-3, 0, 4),
                    ftR=ball_pivot(rig, "R", G_FT_R, -22.0, pitch=12.0))
    c4 = guard_body(pel=(0.015, -0.035, -0.073), pelr=(6, 0, 4), sp=(7, 0, 3), spu=(4, 0, 2), ch=(3, 0, 1), nk=(-1, 0, -4), hd=(-3, 0, -3),
                    ftR=ball_pivot(rig, "R", G_FT_R, -14.0, pitch=20.0))
    c6 = guard_body(pel=(0.02, -0.05, -0.075), pelr=(6, 0, 12), sp=(7, 0, 7), spu=(4, 0, 6), ch=(3, 0, 7), nk=(-1, 0, -14), hd=(-3, 0, -11),
                    ftR=ball_pivot(rig, "R", G_FT_R, -8.0, pitch=26.0), aL=(55, -18, 0, 35), eL=115)
    c11 = guard_body(pel=(0.01, -0.02, -0.066), pelr=(5, 0, -6), sp=(6, 0, -1), ch=(2, 0, -1), nk=(-1, 0, 6), hd=(-3, 0, 5),
                     ftR=ball_pivot(rig, "R", G_FT_R, -24.0, pitch=10.0))
    cr_hit = ST(c6, "R", 0.95)
    cmid = dict(cr_hit); cmid["pos"] = GR(c4)["pos"].lerp(ST(c4, "R", 0.95)["pos"], 0.45); cmid["w_edge"] = 0.4
    lchin = lambda b: dict(pos=R.chin(b, (0.07, -0.13, -0.16)), blade=_N(-0.3, 0.3, 0.9), w_blade=0.5)
    C["BareHand_Punch_2"] = dict(frames=16, loop=False, keys=[
        guard,
        Kf(2, c2, {"L": GL(c2, 0.06, 0.01), "R": GR(c2, -0.03)}, "smooth"),
        Kf(4, c4, {"L": lchin(c4), "R": cmid}, "in"),
        Kf(6, c6, {"L": lchin(c6), "R": cr_hit}, "in"),
        Kf(7, c6, {"L": lchin(c6), "R": dict(cr_hit, pos=cr_hit["pos"] + cr_hit["edge"] * 0.012)}, "out"),
        Kf(11, c11, {"L": GL(c11, 0.04), "R": GR(c11, -0.08)}, "smooth"),
        Kf(16, g, {"L": GL(g), "R": GR(g)}, "inout")],
        events=[(2, "OnAttackStart", "cross"), (5, "OnAttackActive", "cross"), (6, "OnAttackHit", "cross"), (8, "OnAttackEnd", "cross")],
        contact=6, notes="rear cross: rear heel lifts and pivots on the ball, hips turn, chest 1-2 frames later, fist palm-down straight out, lead fist back to the chin")
    # ---- lead hook (finisher): load on the lead leg, hips / chest whip round, elbow up at shoulder height, lead foot pivots;
    # ends in the follow-through (Combo_End settles it), contact f7 of 20
    hk_ftL = ball_pivot(rig, "L", G_FT_L, -12.0, pitch=16.0)
    h3 = guard_body(pel=(0.02, -0.01, -0.085), pelr=(5, -3, -4), sp=(6, -2, 1), spu=(3, 0, 1), ch=(2, 0, 2), nk=(-1, 0, 2), hd=(-3, 0, 2), aL=(60, 10, 0, 20))
    h5 = guard_body(pel=(0.01, -0.025, -0.09), pelr=(6, 1, -22), sp=(7, 1, -5), spu=(4, 0, -5), ch=(3, 0, -6), nk=(-2, 0, 18), hd=(-4, 0, 15), aL=(78, 35, 0, 40), eL=95,
                    ftL=ball_pivot(rig, "L", G_FT_L, 2.0, pitch=8.0))
    h7 = guard_body(pel=(0.0, -0.035, -0.09), pelr=(6, 3, -38), sp=(7, 2, -9), spu=(4, 0, -9), ch=(3, 0, -11), nk=(-2, 0, 34), hd=(-4, 0, 28),
                    ftL=hk_ftL, ftR=ball_pivot(rig, "R", G_FT_R, -38.0, pitch=4.0), aL=(80, 40, 0, 50), eL=95)
    h14 = guard_body(pel=(0.0, -0.035, -0.085), pelr=(6, 2, -32), sp=(7, 1, -8), spu=(4, 0, -7), ch=(3, 0, -8), nk=(-2, 0, 30), hd=(-4, 0, 24),
                     ftL=hk_ftL, ftR=ball_pivot(rig, "R", G_FT_R, -36.0, pitch=4.0))
    h20 = guard_body(pel=(0.0, -0.03, -0.08), pelr=(5, 1, -26), sp=(6, 0, -6), spu=(3, 0, -5), ch=(2, 0, -6), nk=(-1, 0, 24), hd=(-3, 0, 20),
                     ftL=ball_pivot(rig, "L", G_FT_L, -6.0, pitch=4.0), ftR=ball_pivot(rig, "R", G_FT_R, -34.0, pitch=4.0))
    def HK(body, across, fwd, up=0.06, edge=(-1.0, -0.2, 0.0)):
        return dict(pos=R.rel(body, "L", (-across, -fwd, up)), blade=_N(0, 0.1, 1), edge=_N(*edge), w_edge=0.5)
    C["BareHand_Punch_3"] = dict(frames=20, loop=False, keys=[
        guard,
        Kf(3, h3, {"L": dict(pos=R.rel(h3, "L", (0.02, -0.26, -0.01)), blade=_N(0, 0.2, 1), w_blade=0.5), "R": GR(h3)}, "inout"),
        Kf(5, h5, {"L": HK(h5, 0.02, 0.36, edge=(-0.75, -0.65, 0.0)), "R": GR(h5)}, "in"),
        Kf(7, h7, {"L": HK(h7, 0.22, 0.36), "R": GR(h7)}, "in"),
        Kf(9, h7, {"L": HK(h7, 0.32, 0.28, edge=(-0.9, 0.3, 0.0)), "R": GR(h7)}, "out"),
        Kf(14, h14, {"L": GL(h14, 0.02, 0.0), "R": GR(h14)}, "inout"),
        Kf(20, h20, {"L": GL(h20, 0.01), "R": GR(h20)}, "smooth")],
        events=[(3, "OnAttackStart", "hook"), (6, "OnAttackActive", "hook"), (7, "OnAttackHit", "hook"), (9, "OnAttackEnd", "hook")],
        contact=7, notes="lead hook finisher: load on the lead leg, hips and chest whip round, elbow up at shoulder height, fist on a flat arc, lead foot pivots; ends in the follow-through (BareHand_Combo_End settles it)")
    # ---- heavy rear straight: wind-up (weight back, coil), kinetic chain, rear heel pivots out, contact f9 of 28
    v5 = guard_body(pel=(-0.02, 0.045, -0.1), pelr=(3, -2, -30), sp=(4, -1, -9), spu=(2, 0, -8), ch=(1, 0, -9), nk=(0, 0, 32), hd=(-2, 0, 26),
                    ftR=(G_FT_R[0], G_FT_R[1], 0.0, 0.0, G_FT_R[4], 0.0))
    v7 = guard_body(pel=(0.0, 0.0, -0.1), pelr=(5, 0, -8), sp=(6, 0, -6), spu=(3, 0, -7), ch=(2, 0, -8), nk=(-1, 0, 16), hd=(-3, 0, 13),
                    ftR=ball_pivot(rig, "R", G_FT_R, -18.0, pitch=18.0))
    v8 = guard_body(pel=(0.02, -0.05, -0.095), pelr=(7, 1, 10), sp=(8, 0, 4), spu=(4, 0, 3), ch=(3, 0, 2), nk=(-2, 0, -6), hd=(-3, 0, -5),
                    ftR=ball_pivot(rig, "R", G_FT_R, -4.0, pitch=30.0))
    v9 = guard_body(pel=(0.035, -0.09, -0.085), pelr=(8, 2, 24), sp=(9, 1, 11), spu=(5, 0, 10), ch=(4, 0, 11), nk=(-2, 0, -24), hd=(-4, 0, -19),
                    ftR=ball_pivot(rig, "R", G_FT_R, 8.0, pitch=38.0), aL=(50, -18, 0, 35), eL=118)
    v12 = add(v9, pelr=(0, 0, 4), sp=(1, 0, 3), ch=(0, 0, 3), nk=(0, 0, -6), hd=(0, 0, -4))
    v19 = guard_body(pel=(0.015, -0.04, -0.08), pelr=(6, 1, -2), sp=(7, 0, 2), spu=(4, 0, 1), ch=(3, 0, 1), nk=(-1, 0, 2), hd=(-3, 0, 2),
                     ftR=ball_pivot(rig, "R", G_FT_R, -20.0, pitch=14.0))
    hv_hit = ST(v9, "R", 0.97)
    hmid = dict(hv_hit); hmid["pos"] = GR(v8)["pos"].lerp(ST(v8, "R", 0.97)["pos"], 0.4); hmid["w_edge"] = 0.4
    C["BareHand_Heavy"] = dict(frames=28, loop=False, keys=[
        guard,
        Kf(5, v5, {"L": GL(v5, -0.06, -0.01), "R": dict(pos=R.chin(v5, (-0.12, 0.02, -0.14)), blade=_N(0.3, 0.5, 0.8), w_blade=0.5)}, "inout"),
        Kf(7, v7, {"L": GL(v7, 0.03), "R": dict(pos=R.chin(v7, (-0.09, -0.08, -0.16)), blade=_N(0.6, 0.3, 0.7), w_blade=0.5)}, "in"),
        Kf(9, v8, {"L": lchin(v8), "R": hmid}, "in"),
        Kf(10, v9, {"L": lchin(v9), "R": hv_hit}, "in"),
        Kf(13, v12, {"L": lchin(v12), "R": dict(hv_hit, pos=hv_hit["pos"] + hv_hit["edge"] * 0.02)}, "out"),
        Kf(20, v19, {"L": GL(v19, 0.03), "R": GR(v19, -0.12)}, "inout"),
        Kf(28, g, {"L": GL(g), "R": GR(g)}, "inout")],
        events=[(5, "OnAttackStart", "heavy"), (9, "OnAttackActive", "heavy"), (10, "OnAttackHit", "heavy"), (13, "OnAttackEnd", "heavy")],
        contact=10, notes="heavy rear straight: weight back and shoulders coiled (anticipation), legs / hips drive, chest follows, elbow extends last, rear heel pivots out, weight onto the lead leg, follow-through, slower recovery to the guard")
    # ---- hit reaction (additive layer: starts and ends exactly on the guard pose)
    hit = guard_body(pel=(0.0, 0.04, -0.075), pelr=(1, 1, -12), sp=(2, 1, -2), spu=(-1, 1, -1), ch=(-3, 1, -2), nk=(-6, 2, 20), hd=(-12, 5, 20))
    hit8 = guard_body(pel=(0.0, 0.02, -0.07), pelr=(3, 0, -15), ch=(0, 0, -3), nk=(-3, 1, 16), hd=(-6, 2, 14))
    C["BareHand_HitReaction"] = dict(frames=18, loop=False, keys=[
        guard,
        Kf(3, hit, {"L": GL(hit, 0.08, 0.05), "R": GR(hit, 0.03, 0.04)}, "out"),
        Kf(8, hit8, {"L": GL(hit8, 0.03, 0.02), "R": GR(hit8, 0.01, 0.01)}, "smooth"),
        Kf(18, g, {"L": GL(g), "R": GR(g)}, "inout")],
        events=[(0, "OnHurt", "bare")], notes="flinch in the guard (head snaps back, fists come in to cover), recovers to the guard; additive layer (frame 0 = reference)")
    # ---- combo end: from the hook follow-through back to the relaxed stance (idle frame 0), feet step back to parallel
    # (key 0 is replaced by the solved last pose of Punch_3 in build_bare, joint-space from there: no IK branch pops)
    i0 = idle2_pose(0)
    ce8 = guard_body(pel=(0.0, -0.01, -0.05), pelr=(3, 0, -12), sp=(4, 0, -3), ch=(0, 0, -3), nk=(-1, 0, 10), hd=(-2, 0, 8), fL=(55, 38), fR=(55, 38),
                     aL=(35, -22, 0, 18), eL=80, aR=(28, -26, 0, 20), eR=85, wL=(8, 0, 0), wR=(8, 0, 0))
    C["BareHand_Combo_End"] = dict(frames=24, loop=False, keys=[Kf(0, None, {}), Kf(9, ce8, {}, "inout"), Kf(24, i0, {}, "inout")],
        events=[], step_feet=True, after="BareHand_Punch_3", notes="finisher settles: fists drop, a breath out, feet step back to a relaxed stance (ends on Idle frame 0)")
    # ---- block (loop): high guard, forearms up covering the head, chin down, knees soft
    blk = guard_body(pel=(0.0, 0.015, -0.085), pelr=(6, 0, -10), sp=(10, 0, -2), spu=(5, 0, -2), ch=(3, 0, -2), nk=(2, 0, 8), hd=(4, 0, 6),
                     aL=(70, -5, 0, 30), eL=125, aR=(70, -5, 0, 30), eR=125)
    blk2 = add(blk, pel=(0, 0.004, -0.004), spu=(0.8, 0, 0), ch=(0.8, 0, 0))
    def BK(body, sd):
        sg = 1.0 if sd == "L" else -1.0
        return dict(pos=R.chin(body, (0.07 * sg, -0.12, 0.02)), blade=_N(-0.2 * sg, 0.25, 0.95), edge=_N(0.0, -0.25, 0.97), w_edge=0.3)
    C["BareHand_Block"] = dict(frames=40, loop=True, keys=[
        Kf(0, blk, {"L": BK(blk, "L"), "R": BK(blk, "R")}), Kf(20, blk2, {"L": BK(blk2, "L"), "R": BK(blk2, "R")}), Kf(40, blk, {"L": BK(blk, "L"), "R": BK(blk, "R")})], events=[], no_refine=True,
        notes="high guard block: forearms vertical in front of the face, elbows in front of the ribs, wrists straight, chin down, knees soft, breathing")
    rig.reset()
    return C

BARE = ("BareHand_Idle", "BareHand_Punch_1", "BareHand_Punch_2", "BareHand_Punch_3", "BareHand_Heavy", "BareHand_HitReaction",
        "BareHand_Combo_End", "BareHand_Block", "Unarmed_Block")

def spline_interp(keys, f, loop, N):
    """Catmull-Rom through the key poses (no velocity steps at dense keys); keys: [(frame, pose, easing)]"""
    fr = [k[0] for k in keys]
    if f <= fr[0]: return keys[0][1]
    if f >= fr[-1]: return keys[-1][1]
    i = max(j for j in range(len(fr) - 1) if fr[j] <= f)
    f0, f1 = fr[i], fr[i + 1]; t = (f - f0) / (f1 - f0)
    def at(j):
        if 0 <= j < len(keys): return keys[j][0], keys[j][1]
        if loop:
            # the last key equals the first: wrap past the seam
            if j < 0: return keys[j - 1][0] - N, keys[j - 1][1]
            return keys[j - len(keys) + 1][0] + N, keys[j - len(keys) + 1][1]
        jj = min(max(j, 0), len(keys) - 1); return keys[jj][0], keys[jj][1]
    (fa, pa), (fb, pb), (fc, pc), (fd, pd) = at(i - 1), at(i), at(i + 1), at(i + 2)
    h = fc - fb
    def ch(a, b, c, d_):
        if isinstance(b, (tuple, list)): return tuple(ch(x, y, z, w) for x, y, z, w in zip(a, b, c, d_))
        m1 = (c - a) / max(1e-6, fc - fa) * h; m2 = (d_ - b) / max(1e-6, fd - fb) * h
        t2 = t * t; t3 = t2 * t
        return (2 * t3 - 3 * t2 + 1) * b + (t3 - 2 * t2 + t) * m1 + (-2 * t3 + 3 * t2) * c + (t3 - t2) * m2
    return {k: ch(pa[k], pb[k], pc[k], pd[k]) for k in pb}

def bake_keyed(rig, B, name, d, alias=None, solved=None):
    import pf_clips_weapons as W
    import pf_weapon_ik as WI
    # keys are solved in order, each seeded only from the previous key's arm (one start: the same IK branch, no flips)
    raw = [(f, body, {sd: dict({"starts": 1, "reg": 0.05}, **t) for sd, t in tg.items()}, e) for (f, body, tg, e) in d["keys"]]
    keys, infos = W.solve_keys(rig, raw)
    if solved is not None: solved[name] = keys
    kf = sorted(k[0] for k in raw); keyframes = set(kf); cache = {}; last = {}
    step = d.get("step_feet")
    def gap(f):
        for a, b in zip(kf, kf[1:]):
            if a < f < b: return b - a
        return 0
    def mid(f):
        for a, b in zip(kf, kf[1:]):
            if a < f < b: return math.sin(math.pi * (f - a) / (b - a))
        return 0.0
    def fn(f):
        p = spline_interp(keys, f, d["loop"], d["frames"]) if d.get("spline") else CH.interp(keys, f)
        if f not in keyframes:
            if f not in cache:
                tg, _ = W._targets_at(raw, f)
                if tg and gap(f) > 4 and not d.get("no_refine"):
                    # long gaps: refine towards the target path in hand space, seeded between the joint blend and the
                    # previous frame; the refinement fades out (never switches) when it would move the arm far
                    rig.reset(); CH.apply(rig, p); bpy.context.view_layer.update()
                    for sd, t in tg.items():
                        start = dict(p)
                        if sd in last:
                            for k in ("a", "e", "w"): start[k + sd] = lerp(p[k + sd], last[sd][k], 0.5)
                        t = {k: v for k, v in t.items() if k not in ("starts", "reg")}
                        q, info = WI.solve(rig, start, sd, reg=0.3, iters=240, starts=1, **t)
                        jump = max(abs(q["w" + sd][2] - p["w" + sd][2]), abs(q["a" + sd][2] - p["a" + sd][2]), abs(q["e" + sd] - p["e" + sd]) * 0.5)
                        w = clamp01((45.0 - jump) / 25.0) * mid(f)      # fades out near the keys: continuous
                        for k in ("a", "e", "w"): p[k + sd] = lerp(p[k + sd], q[k + sd], w)
                        rig.reset(); CH.apply(rig, p); bpy.context.view_layer.update()
                cache[f] = p
            p = dict(cache[f]); rig.reset()
        for sd in LR: last[sd] = {k: p[k + sd] for k in ("a", "e", "w")}
        if d.get("post"): p = d["post"](f, p)
        if step:
            # feet that move between the two surrounding keys are lifted (a small step), one after the other
            for i in range(len(keys) - 1):
                f0, p0, _ = keys[i]; f1, p1, _ = keys[i + 1]
                if f0 <= f <= f1:
                    for sd, (a, b) in (("L", (0.0, 0.6)), ("R", (0.4, 1.0))):
                        t0 = p0["ft" + sd]; t1 = p1["ft" + sd]
                        dist = math.hypot(t1[0] - t0[0], t1[1] - t0[1])
                        if dist > 0.02:
                            u = clamp01(((f - f0) / (f1 - f0) - a) / (b - a))
                            k = inout(u)
                            t = list(lerp(t0, t1, k)); t[2] += 0.04 * math.sin(math.pi * u)
                            p["ft" + sd] = tuple(t)
                    break
        return CH.apply(rig, p)
    out = alias or name
    B.bake(out, d["frames"], d["loop"], fn, events=d.get("events", []), notes=d.get("notes", ""))
    ex = {}
    if "contact" in d: ex["contact"] = d["contact"]
    B.meta[out]["extra"] = ex
    return [(f, {sd: (i.get("pos_err"), i.get("blade_err")) for sd, i in info.items()}) for f, info in infos]

def build_bare(rig, B, which=None):
    import pf_clips_weapons as W
    defs = bare_defs(rig); rep = {}; solved = {}
    for name, d in defs.items():
        if d.get("after"):
            # starts on the exact solved last pose of the clip it follows
            src = solved.get(d["after"]) or W.solve_keys(rig, defs[d["after"]]["keys"])[0]
            d = dict(d); d["keys"] = [(0, src[-1][1], {}, "smooth")] + d["keys"][1:]
        if which is None or name in which or (name == "BareHand_Punch_3" and "BareHand_Combo_End" in (which or [])):
            if which is None or name in which: rep[name] = bake_keyed(rig, B, name, d, solved=solved)
            else: solved[name] = W.solve_keys(rig, d["keys"])[0]
        if name == "BareHand_Block" and (which is None or "Unarmed_Block" in which):
            rep["Unarmed_Block"] = bake_keyed(rig, B, name, d, alias="Unarmed_Block")
    return rep

# ================================================================== survival actions (batch 3)
# Physical drinking / eating / gathering / bandage: hands placed with the same IK (mouth, water, ground, forearm targets
# measured from the posed body), feet step (lifted) into kneels and squats, OnDrink / OnEat / OnGatherHit / OnUseItem at
# the contact frames U's receivers expect.
KNEEL = dict(pel=(0.0, 0.08, -0.44), pelr=(8.0, 0.0, 0.0), ftL=(0.0, -0.2, 0.0, 0.0, 5.0, 0.0), ftR=(0.0, 0.32, 0.0, 65.0, -5.0, 65.0))
def kneel_body(**kw):
    d = dict(KNEEL); d.update(kw); return P(**d)
GATHER_READY = dict(pel=(0, 0.1, -0.40), pelr=(18, 0, 0), sp=(22, 0, 0), spu=(12, 0, 0), ch=(6, 0, 0), nk=(4, 0, 0), hd=(12, 0, 0),
                    aL=(50, -20, 0, 15), eL=40, aR=(50, -20, 0, 15), eR=40, fL=(30, 20), fR=(30, 20),
                    ftL=(0.0, -0.18, 0, 0, 5, 0), ftR=(0.0, 0.2, 0, 40, -5, 40))
def ready_body(**kw):
    d = dict(GATHER_READY); d.update(kw); return P(**d)

def reach_to(R, body, sd, point, frac=0.93):
    """the point itself when the arm can reach it from the posed shoulder, else the point on the way at frac of the arm
    length (U's hand IK finishes the reach onto the real water / ground surface in the game)"""
    sh = R.sh(body, sd); v = point - sh
    return point.copy() if v.length <= frac * R.L[sd] else sh + v.normalized() * frac * R.L[sd]

def mouth(R, body, off=(0.0, 0.0, 0.0)):
    """mouth point of the posed head (rest offset carried by the head bone), plus an armature-space offset"""
    rig = R.rig; R._pose(body)
    hb = rig.obj.pose.bones["Head"]; rest = rig.rest["Head"]
    m_rest = rest.translation + Vector((0.0, -0.105, -0.05))
    return hb.matrix @ (rest.inverted() @ m_rest) + Vector(off)

def survival_defs(rig):
    C = {}; R = Reach(rig)
    i0 = idle2_pose(0)
    step = add(i0, pel=(0.0, 0.0, -0.05), sp=(8, 0, 0), ftL=(0.0, -0.2, 0.0, 0.0, 1.0, 0.0), aL=(10, -30, 0, 5), aR=(-4, -33, 0, 0))
    # ---------------- Drink_Kneel: step, kneel on the right knee, both hands scoop water, bring it to the mouth, drink
    k15 = kneel_body(sp=(16, 0, 0), spu=(8, 0, 0), nk=(6, 0, 0), hd=(10, 0, 0), aL=(35, -22, 0, 10), eL=45, aR=(35, -22, 0, 10), eR=45)
    k22 = kneel_body(pelr=(20, 0, 0), sp=(36, 0, 0), spu=(20, 0, 0), ch=(10, 0, 0), nk=(-4, 0, 0), hd=(6, 0, 0), aL=(70, -12, 0, 25), eL=25, aR=(70, -12, 0, 25), eR=25, fL=(25, 12), fR=(25, 12))
    k28 = add(k22, sp=(-4, 0, 0), fL=(18, 8), fR=(18, 8))
    k38 = kneel_body(pelr=(10, 0, 0), sp=(18, 0, 0), spu=(8, 0, 0), ch=(2, 0, 0), nk=(10, 0, 0), hd=(18, 0, 0), aL=(40, -10, 0, 30), eL=110, aR=(40, -10, 0, 30), eR=110, fL=(40, 22), fR=(40, 22))
    k46 = add(k38, nk=(-6, 0, 0), hd=(-10, 0, 0), sp=(-3, 0, 0))
    k52 = kneel_body(sp=(14, 0, 0), spu=(6, 0, 0), nk=(4, 0, 0), hd=(6, 0, 0), aL=(30, -24, 0, 10), eL=60, aR=(30, -24, 0, 10), eR=60, wL=(-25, 0, 0), wR=(-25, 0, 0))
    def water(body, sd, z=0.1, fwd=0.5):
        sg = 1.0 if sd == "L" else -1.0
        return dict(pos=reach_to(R, body, sd, Vector((0.06 * sg, -fwd, z))), blade=_N(sg, 0.0, 0.0), edge=_N(0.0, -1.0, -0.35), w_edge=0.4)
    def at_mouth(body, sd, lift=0.0):
        sg = 1.0 if sd == "L" else -1.0
        return dict(pos=mouth(R, body, (0.035 * sg, -0.05, -0.035 + lift)), blade=_N(sg, 0.0, 0.2), edge=_N(0.0, -1.0, 0.5), w_edge=0.3)
    C["Drink_Kneel"] = dict(frames=72, loop=False, step_feet=True, keys=[
        Kf(0, i0, {}), Kf(6, step, {}, "inout"), Kf(15, k15, {}, "inout"),
        Kf(22, k22, {"L": water(k22, "L"), "R": water(k22, "R")}, "inout"),
        Kf(28, k28, {"L": water(k28, "L", 0.16, 0.46), "R": water(k28, "R", 0.16, 0.46)}, "smooth"),
        Kf(38, k38, {"L": at_mouth(k38, "L"), "R": at_mouth(k38, "R")}, "inout"),
        Kf(46, k46, {"L": at_mouth(k46, "L", 0.02), "R": at_mouth(k46, "R", 0.02)}, "smooth"),
        Kf(52, k52, {}, "inout"), Kf(60, k15, {}, "smooth"), Kf(66, step, {}, "inout"), Kf(72, i0, {}, "inout")],
        events=[(27, "OnScoop", "kneel"), (40, "OnDrink", "kneel")],
        notes="kneel at the water on the right knee, lean in, both hands cup and scoop, bring the water to the mouth, drink, shake the drips off, stand up")
    # ---------------- Collect_Water: kneel, the container hand dips in and holds while it fills, lift it to the chest
    c22 = kneel_body(pelr=(20, 0, 0), sp=(34, 0, 0), spu=(18, 0, 0), ch=(8, 0, 0), nk=(-2, 0, 0), hd=(8, 0, 0), aL=(40, -15, 0, 20), eL=70, aR=(70, -12, 0, 20), eR=25, fR=(62, 45), fL=(25, 18))
    c30 = add(c22, sp=(1, 0, 0), hd=(2, 0, 0))
    c44 = kneel_body(sp=(12, 0, 0), spu=(6, 0, 0), nk=(4, 0, 0), hd=(10, 0, 0), aL=(35, -20, 0, 15), eL=60, aR=(40, -20, 0, 30), eR=100, fR=(62, 45))
    def cont(body, z, fwd, tilt=0.0):
        return dict(pos=reach_to(R, body, "R", Vector((-0.08, -fwd, z))), blade=_N(0.0, -0.3 + tilt, 1.0), edge=_N(0.0, -1.0, -0.2), w_edge=0.3)
    C["Collect_Water"] = dict(frames=64, loop=False, step_feet=True, keys=[
        Kf(0, i0, {}), Kf(6, step, {}, "inout"), Kf(15, k15, {}, "inout"),
        Kf(22, c22, {"R": cont(c22, 0.06, 0.5)}, "inout"),
        Kf(30, c30, {"R": cont(c30, 0.05, 0.52, 0.3)}, "smooth"),
        Kf(35, c30, {"R": cont(c30, 0.07, 0.5, 0.0)}, "smooth"),
        Kf(44, c44, {"R": dict(pos=R.rel(c44, "R", (0.1, -0.24, -0.22)), blade=_N(0.0, 0.0, 1.0), w_blade=0.5)}, "inout"),
        Kf(52, k15, {}, "inout"), Kf(58, step, {}, "inout"), Kf(64, i0, {}, "inout")],
        events=[(22, "OnScoop", "fill"), (34, "OnDrink", "fill")],
        notes="kneel at the water, the container hand dips in and holds while it fills (OnDrink fill), lift it to the chest, stand up")
    # ---------------- Drink_Container (standing): bring the container to the lips, tilt the head back, drink, lower
    d12 = add(i0, sp=(-1, 0, 0), aR=(40, -20, 0, 25), eR=115, fR=(60, 45))
    d24 = add(i0, sp=(-4, 0, 0), spu=(-3, 0, 0), nk=(-8, 0, 0), hd=(-14, 0, 0), aR=(45, -15, 0, 25), eR=125, fR=(60, 45))
    def cup(body, tilt):
        return dict(pos=mouth(R, body, (-0.01, -0.07, -0.03)), blade=_N(0.2, -0.25 - tilt, 1.0), edge=_N(0.0, -1.0, 0.2 + tilt), w_edge=0.3)
    C["Drink_Container"] = dict(frames=44, loop=False, keys=[
        Kf(0, i0, {}), Kf(12, d12, {"R": cup(d12, 0.0)}, "inout"), Kf(18, d24, {"R": cup(d24, 0.5)}, "inout"),
        Kf(28, d24, {"R": cup(d24, 0.6)}, "smooth"), Kf(34, d12, {"R": cup(d12, 0.1)}, "inout"), Kf(44, i0, {}, "inout")],
        events=[(20, "OnDrink", "")], notes="standing: the container comes up to the lips, head tilts back, drink, lower")
    # ---------------- Eat: hand to mouth, bite, chew (small jaw-driven nods), second bite, lower
    e14 = add(i0, nk=(4, 0, 0), hd=(6, 0, 0), aR=(35, -12, 0, 30), eR=120, fR=(60, 42))
    e22 = add(i0, nk=(2, 0, 0), hd=(2, 0, 0), aR=(30, -15, 0, 25), eR=100, fR=(60, 42))
    def food(body, dz=0.0):
        return dict(pos=mouth(R, body, (-0.015, -0.05, -0.02 + dz)), blade=_N(0.3, -0.3, 1.0), w_blade=0.4)
    def eat_fn_mod(f, p):
        # chewing: small head nods at ~2.3 Hz between and after the bites
        ch = 0.0
        if 17 <= f <= 31 or 35 <= f <= 46: ch = 1.6 * math.sin(TAU * (f - 17) / 13.0)
        return add(p, hd=(ch, 0, 0))
    C["Eat"] = dict(frames=48, loop=False, keys=[
        Kf(0, i0, {}), Kf(14, e14, {"R": food(e14)}, "inout"), Kf(22, e22, {"R": food(e22, -0.12)}, "inout"),
        Kf(32, e14, {"R": food(e14)}, "inout"), Kf(38, e22, {"R": food(e22, -0.12)}, "inout"), Kf(48, i0, {}, "inout")],
        events=[(16, "OnEat", ""), (34, "OnEat", "")], post=eat_fn_mod,
        notes="food to the mouth, bite, chew (head nods, jaw from PlayerFacial), second bite, lower")
    # ---------------- Gather_Plant (B4): no hold, pull eased in-out, low toss (abduction +10 instead of +35), fist roll small
    ready = ready_body()
    reach = add(ready, pelr=(6, 0, 0), sp=(8, 0, 0), hd=(4, 0, 0), aL=(28, 5, 0, -5), eL=-30, aR=(30, 5, 0, -5), eR=-32, fL=(-15, -10), fR=(-15, -10))
    grab = add(reach, sp=(-2, 0, 0), aL=(-4, 0, 0, 0), aR=(-4, 0, 0, 0), eL=4, eR=4, fL=(70, 50), fR=(70, 50))
    pull = add(ready, pel=(0, 0.04, 0.02), pelr=(-6, 0, 4), sp=(-12, 0, 4), ch=(-4, 0, 6), aL=(-10, 0, 0, 10), eL=35, aR=(-15, 0, 0, 12), eR=40, fL=(55, 40), fR=(55, 40))
    toss = add(pull, pelr=(0, 0, -8), ch=(0, 0, -9), aR=(8, 10, 0, -8), eR=14, fR=(-10, -5), hd=(0, 0, -12))
    C["Gather_Plant"] = dict(frames=48, loop=True, keys=[
        Kf(0, ready, {}), Kf(12, reach, {}, "inout"), Kf(16, grab, {}, "smooth"), Kf(25, pull, {}, "inout"), Kf(34, toss, {}, "inout"), Kf(48, ready, {}, "inout")],
        events=[(24, "OnGatherHit", "Gather_Plant")],
        notes="v2 (B4): reach, grip without a hold, pull eased in-out, low toss to the side, back to the ready squat")
    # ---------------- Gather_Enter / Gather_Exit: stand <-> ready squat (feet step, no crossfaded 0.38 m drop)
    en6 = add(i0, pel=(0.0, 0.03, -0.12), pelr=(8, 0, 0), sp=(10, 0, 0), aL=(15, -30, 0, 5), aR=(15, -30, 0, 5), eL=30, eR=30, ftL=(0.0, -0.12, 0.0, 0.0, 4.0, 0.0))
    C["Gather_Enter"] = dict(frames=12, loop=False, step_feet=True, keys=[Kf(0, i0, {}), Kf(6, en6, {}, "inout"), Kf(12, ready, {}, "inout")],
        events=[], notes="stand -> gather ready squat in 12 frames (0.4 s): the left foot steps forward, the right heel comes up, hips lower with the trunk leaning in; ends on Gather_Plant frame 0")
    C["Gather_Exit"] = dict(frames=14, loop=False, step_feet=True, keys=[Kf(0, ready, {}), Kf(7, en6, {}, "inout"), Kf(14, i0, {}, "inout")],
        events=[], notes="gather ready squat -> stand in 14 frames (0.47 s): push up through the legs, feet step back under the hips; ends on Idle frame 0")
    # ---------------- Gather_Stone_Hand (loop from the ready squat): reach, grip, wrench the stone free, pouch it at the hip
    s9 = add(ready, pelr=(10, 0, -4), sp=(18, 0, -4), spu=(8, 0, 0), hd=(-2, 0, -6), aL=(-10, 0, 0, 0), eL=40, fR=(10, 5))
    s13 = add(s9, fR=(72, 55), sp=(2, 0, 0))
    s17 = add(ready, pel=(0, 0.02, 0.02), pelr=(-2, 0, 4), sp=(-4, 0, 6), ch=(-2, 0, 6), hd=(0, 0, 4), fR=(75, 58))
    s26 = add(ready, pelr=(0, 0, 8), sp=(-2, 0, 8), ch=(0, 0, 10), hd=(4, 0, 10), fR=(75, 58))
    s31 = add(s26, fR=(15, 8))
    def ground(body, sd, x, fwd, z=0.07):
        sg = 1.0 if sd == "L" else -1.0
        return dict(pos=reach_to(R, body, sd, Vector((x, -fwd, z))), blade=_N(sg * -1.0, -0.2, 0.0), edge=_N(0.0, -0.6, -0.8), w_edge=0.3)
    C["Gather_Stone_Hand"] = dict(frames=40, loop=True, keys=[
        Kf(0, ready, {}), Kf(9, s9, {"R": ground(s9, "R", -0.1, 0.42)}, "inout"), Kf(13, s13, {"R": ground(s13, "R", -0.1, 0.41, 0.075)}, "smooth"),
        Kf(17, s17, {"R": dict(pos=Vector((-0.1, -0.38, 0.3)), blade=_N(0.6, 0.0, 0.8), w_blade=0.4)}, "out"),
        Kf(26, s26, {"R": dict(pos=R.rel(s26, "R", (0.06, -0.12, -0.42)), blade=_N(0.3, 0.2, 0.9), w_blade=0.4)}, "inout"),
        Kf(31, s31, {"R": dict(pos=R.rel(s31, "R", (0.06, -0.12, -0.42)), blade=_N(0.3, 0.2, 0.9), w_blade=0.4)}, "smooth"), Kf(40, ready, {}, "inout")],
        events=[(17, "OnGatherHit", "Gather_Stone_Hand")],
        notes="bare-hand stone: reach down, grip, wrench the stone free (OnGatherHit), bring it to the hip pouch, release, back to the ready squat")
    # ---------------- Gather_Branch (loop from the ready squat): both hands grip a branch, snap it, set the pieces aside
    b8 = add(ready, pelr=(10, 0, 0), sp=(18, 0, 0), spu=(8, 0, 0), hd=(-2, 0, 0), fL=(10, 5), fR=(10, 5))
    b12 = add(b8, fL=(75, 55), fR=(75, 55))
    b16 = add(ready, pel=(0, 0.03, 0.03), pelr=(0, 0, 0), sp=(2, 0, 0), ch=(0, 0, 0), fL=(78, 58), fR=(78, 58))
    b24 = add(ready, pelr=(4, 0, -12), sp=(6, 0, -8), ch=(2, 0, -10), hd=(6, 0, -14), fL=(78, 58), fR=(78, 58))
    b32 = add(b24, fL=(15, 8), fR=(15, 8))
    C["Gather_Branch"] = dict(frames=40, loop=True, keys=[
        Kf(0, ready, {}), Kf(8, b8, {"L": ground(b8, "L", 0.13, 0.43), "R": ground(b8, "R", -0.13, 0.43)}, "inout"),
        Kf(12, b12, {"L": ground(b12, "L", 0.12, 0.42, 0.08), "R": ground(b12, "R", -0.12, 0.42, 0.08)}, "smooth"),
        Kf(16, b16, {"L": dict(pos=Vector((0.2, -0.36, 0.34)), blade=_N(-0.5, 0.0, 0.85), w_blade=0.3),
                     "R": dict(pos=Vector((-0.2, -0.38, 0.24)), blade=_N(0.5, 0.0, 0.85), w_blade=0.3)}, "out"),
        Kf(24, b24, {"L": dict(pos=Vector((-0.08, -0.36, 0.34)), blade=_N(-0.3, 0.0, 0.95), w_blade=0.3),
                     "R": dict(pos=Vector((-0.34, -0.26, 0.26)), blade=_N(0.3, 0.0, 0.95), w_blade=0.3)}, "inout"),
        Kf(32, b32, {"L": dict(pos=Vector((-0.08, -0.36, 0.3)), blade=_N(-0.3, 0.0, 0.95), w_blade=0.3),
                     "R": dict(pos=Vector((-0.36, -0.24, 0.2)), blade=_N(0.3, 0.0, 0.95), w_blade=0.3)}, "smooth"),
        Kf(40, ready, {}, "inout")],
        events=[(16, "OnGatherHit", "Gather_Branch")],
        notes="small branch: both hands grip it on the ground, snap it over the grip with a sharp twist (OnGatherHit), set the pieces aside, back to the ready squat")
    # ---------------- Bandage_Use (standing): left forearm held across the front, the right hand wraps twice, pulls the knot
    bb = add(i0, sp=(6, 0, 2), spu=(3, 0, 0), nk=(12, 0, 8), hd=(16, 0, 8), aL=(45, -12, 0, 45), eL=95, fL=(30, 20), aR=(35, -15, 0, 30), eR=90, fR=(35, 25))
    Lh = dict(pos=R.rel(bb, "L", (-0.22, -0.32, -0.2)), blade=_N(0.0, 0.0, 1.0), w_blade=0.3)
    # forearm axis (elbow -> wrist) of the held arm, approximated from the shoulder-relative hand place
    ctr = R.rel(bb, "L", (-0.12, -0.24, -0.2)); ax = Vector((-0.6, -0.8, 0.05)).normalized()
    u = ax.cross(Vector((0, 0, 1))).normalized(); v = ax.cross(u).normalized()
    keys = [Kf(0, i0, {}), Kf(10, bb, {"L": Lh, "R": dict(pos=ctr + u * 0.09, blade=_N(0.0, 0.0, 1.0), w_blade=0.2)}, "inout")]
    n = 0
    for f in range(13, 41, 3):
        a = TAU * (f - 10) / 15.0
        keys.append(Kf(f, bb, {"L": Lh, "R": dict(pos=ctr + (u * math.cos(a) + v * math.sin(a)) * 0.085 - ax * 0.012 * n, blade=_N(0.0, 0.0, 1.0), w_blade=0.15)}, "smooth"))
        n += 1
    tie = add(bb, ch=(0, 0, -4), fR=(70, 50))
    keys.append(Kf(44, tie, {"L": Lh, "R": dict(pos=ctr + u * 0.1 + Vector((-0.12, 0.02, -0.02)), blade=_N(0.0, 0.0, 1.0), w_blade=0.2)}, "out"))
    keys.append(Kf(48, tie, {"L": Lh, "R": dict(pos=ctr + u * 0.1 + Vector((-0.14, 0.02, -0.03)), blade=_N(0.0, 0.0, 1.0), w_blade=0.2)}, "smooth"))
    keys.append(Kf(58, i0, {}, "inout"))
    C["Bandage_Use"] = dict(frames=58, loop=False, keys=keys, events=[(45, "OnUseItem", "bandage")],
        notes="left forearm held across the front, head looks at it, the right hand wraps the bandage round twice, pulls the knot tight (OnUseItem), arms lower")
    rig.reset()
    return C

SURV = ("Drink_Kneel", "Collect_Water", "Drink_Container", "Eat", "Gather_Plant", "Gather_Enter", "Gather_Exit", "Gather_Stone_Hand", "Gather_Branch", "Bandage_Use")

def build_survival(rig, B, which=None):
    defs = survival_defs(rig); rep = {}
    for name, d in defs.items():
        if which is None or name in which:
            rep[name] = bake_keyed(rig, B, name, d)
    return rep

# ================================================================== climbing (batch 4)
# Rock face (U: Climbable kind RockFace, the pivot 0.34 m from the face, W / S climb at 0.6 / 0.55 m/s) and ledge
# pull-over (Ledge: reach 0.35 s, pull 0.35 + 0.3 s per metre, over 0.6 s). Clips are in place: the code moves the body.
# Hands hold the face plane (y = -0.30 from the pivot), toes on holds (ankle 0.14 m off the face), one hand and the
# opposite foot move together while the other two hold; held limbs slide down with the body's climb (no slip).
FACE_Y = -0.30
def climb_body(t=0.0, **kw):
    d = dict(pel=(0.0, -0.05, 0.16), pelr=(4.0, 0.0, 0.0), sp=(6.0, 0.0, 0.0), spu=(3.0, 0.0, 0.0), ch=(0.0, 0.0, 0.0), nk=(-8.0, 0.0, 0.0), hd=(-16.0, 0.0, 0.0),
             aL=(140.0, 10.0, 0.0, 10.0), eL=40.0, aR=(140.0, 10.0, 0.0, 10.0), eR=40.0, fL=(55.0, 40.0), fR=(55.0, 40.0), cL=(8.0, 4.0), cR=(8.0, 4.0))
    d.update(kw); p = P(**d)
    return p

def rock_hold(sd, z, x=None):
    sg = 1.0 if sd == "L" else -1.0
    return dict(pos=Vector((0.2 * sg if x is None else x, FACE_Y, z)), blade=_N(-sg * 0.8, 0.0, 0.6), edge=_N(0.0, -0.4, 0.9), w_edge=0.25)

def rock_foot(sd, z, pitch=12.0):
    """toes on a hold of the face at height z (ankle 0.14 m behind the face plane), heel a little lower"""
    sg = 1.0 if sd == "L" else -1.0
    a = 0.0
    return (0.03 * sg, FACE_Y + 0.155 - (-0.011), z, pitch, 8.0 * sg, pitch)

def climb_defs(rig):
    C = {}
    N = 36; spd = 0.6; T = N / FPS; travel = spd * T * 0.5       # each limb holds for half a cycle while the body rises
    hi_h, lo_h = 1.98, 1.98 - travel                             # hand hold heights (reach high, pulled down to)
    hi_f, lo_f = 0.64, 0.64 - travel                             # foot hold heights
    def limb(ph, hi, lo):
        """height of a limb over the cycle (0..1): holds (slides down with the climb) the first half, moves up the second"""
        if ph < 0.5: return hi - (hi - lo) * (ph / 0.5), 0.0
        u = (ph - 0.5) / 0.5
        return lo + (hi - lo) * ease(u, "inout") + 0.0, math.sin(math.pi * u)
    keys = []
    for f in range(0, N + 1, 3):
        t = f / N
        zRh, sRh = limb((t + 0.5) % 1.0, hi_h, lo_h); zLh, sLh = limb(t, hi_h, lo_h)
        zLf, sLf = limb((t + 0.5) % 1.0, hi_f, lo_f); zRf, sRf = limb(t, hi_f, lo_f)
        sway = math.sin(TAU * t)
        body = climb_body(pel=(0.035 * sway, -0.05 - 0.02 * abs(sway), 0.16 + 0.03 * math.cos(2 * TAU * t)), pelr=(4.0, 3.0 * sway, 4.0 * sway),
                          sp=(6.0, -1.5 * sway, -2.0 * sway), hd=(-16.0 - 4.0 * max(sRh, sLh), 0.0, -6.0 * sway),
                          ftL=rock_foot("L", zLf + 0.04 * sLf, 12.0 - 10.0 * sLf), ftR=rock_foot("R", zRf + 0.04 * sRf, 12.0 - 10.0 * sRf),
                          fL=(55.0 - 40.0 * sLh, 40.0 - 25.0 * sLh), fR=(55.0 - 40.0 * sRh, 40.0 - 25.0 * sRh))
        tg = {"L": rock_hold("L", zLh, 0.2 + 0.03 * sLh), "R": rock_hold("R", zRh, -0.2 - 0.03 * sRh)}
        for sd, sw in (("L", sLh), ("R", sRh)):
            if sw > 0: tg[sd] = dict(tg[sd], pos=tg[sd]["pos"] + Vector((0.0, 0.06 * sw, 0.0)))    # the moving hand comes off the rock
        keys.append(Kf(f, body, tg, "linear"))
    keys[-1] = (N, keys[0][1], keys[0][2], "linear")      # the closing key IS the first key (seamless loop, same IK solve)
    C["Climb_Rock_Up"] = dict(frames=N, loop=True, keys=keys, speed=spd, spline=True,
        events=[(0, "OnClimbStep", "L"), (18, "OnClimbStep", "R")], root_speed=spd,
        notes="rock face, climbing up (in place; the body moves up at 0.6 m/s): right hand + left foot reach while the left hand + right foot hold, then the other pair; hips sway towards the holding side")
    # down: the same cycle played backwards (limbs reach down, feet first)
    kd = [(N - f, b, tg, "linear") for (f, b, tg, e) in reversed(keys)]
    C["Climb_Rock_Down"] = dict(frames=N, loop=True, keys=kd, speed=-0.55, spline=True, reverse_of="Climb_Rock_Up",
        events=[(0, "OnClimbStep", "R"), (18, "OnClimbStep", "L")], root_speed=-0.55,
        notes="rock face, climbing down (in place; the body moves down at 0.55 m/s): the cycle of Climb_Rock_Up reversed, feet feel down first")
    # idle on the face: both hands holding, feet on holds, breathing
    ki = []
    for f in (0, 30, 60):
        br = math.sin(TAU * f / 60.0)
        body = climb_body(spu=(3.0 + 1.2 * br, 0, 0), ftL=rock_foot("L", 0.52), ftR=rock_foot("R", 0.40))
        ki.append(Kf(f, body, {"L": rock_hold("L", 1.86), "R": rock_hold("R", 1.72)}))
    ki[1] = Kf(30, climb_body(spu=(4.2, 0, 0), pel=(0.0, -0.045, 0.165), ftL=rock_foot("L", 0.52), ftR=rock_foot("R", 0.40)), {"L": rock_hold("L", 1.865), "R": rock_hold("R", 1.725)})
    C["Climb_Rock_Idle"] = dict(frames=60, loop=True, keys=ki, events=[], no_refine=True, spline=True, notes="hanging on the rock face: both hands on holds, toes on holds, breathing")
    # ---------------- Ledge_Mantle: reach up, grab the lip, pull, press, knee over, stand (the code lifts / moves the body)
    i0 = idle2_pose(0)
    lip = 1.95
    k10 = climb_body(pel=(0.0, -0.03, 0.02), pelr=(2, 0, 0), sp=(2, 0, 0), hd=(-22, 0, 0), ftL=(0.0, -0.05, 0.0, 30.0, 4.0, 30.0), ftR=(0.0, 0.0, 0.0, 20.0, -8.0, 20.0))
    k20 = climb_body(pel=(0.0, -0.06, 0.0), pelr=(6, 0, 0), sp=(10, 0, 0), spu=(6, 0, 0), hd=(-10, 0, 0), ftL=rock_foot("L", 0.34), ftR=rock_foot("R", 0.18),
                     aL=(120, 5, 0, 20), eL=90, aR=(120, 5, 0, 20), eR=90)
    k30 = climb_body(pel=(0.0, -0.12, -0.1), pelr=(22, 0, 0), sp=(22, 0, 0), spu=(10, 0, 0), ch=(4, 0, 0), nk=(-4, 0, 0), hd=(-6, 0, 0), ftL=rock_foot("L", 0.36), ftR=rock_foot("R", 0.26),
                     aL=(40, -10, 0, 20), eL=20, aR=(40, -10, 0, 20), eR=20)
    k38 = climb_body(pel=(0.0, -0.15, -0.06), pelr=(26, 0, 0), sp=(24, 0, 0), spu=(10, 0, 0), ch=(4, 0, 0), nk=(-2, 0, 0), hd=(0, 0, 0), ftL=rock_foot("L", 0.3),
                     ftR=(-0.03, -0.3, 0.55, 20.0, -8.0, 20.0), aL=(35, -12, 0, 20), eL=18, aR=(35, -12, 0, 20), eR=18)
    k46 = add(i0, pel=(0.0, -0.1, -0.18), pelr=(20, 0, 0), sp=(18, 0, 0), spu=(8, 0, 0), ftL=(0.0, 0.12, 0.08, 30.0, 4.0, 30.0), ftR=(0.0, -0.2, 0.0, 0.0, -8.0, 0.0), aL=(10, -25, 0, 10), aR=(10, -25, 0, 10))
    def lipt(sd, z):
        sg = 1.0 if sd == "L" else -1.0
        return dict(pos=Vector((0.22 * sg, FACE_Y - 0.02, z)), blade=_N(-sg * 0.3, -0.4, 0.8), edge=_N(0.0, -0.95, -0.3), w_edge=0.3)
    def press(sd, z):
        sg = 1.0 if sd == "L" else -1.0
        return dict(pos=Vector((0.2 * sg, FACE_Y + 0.02, z)), blade=_N(-sg * 0.3, -0.5, 0.8), w_blade=0.3)
    C["Ledge_Mantle"] = dict(frames=54, loop=False, keys=[
        Kf(0, i0, {}),
        Kf(10, k10, {"L": lipt("L", lip), "R": lipt("R", lip)}, "inout"),
        Kf(20, k20, {"L": lipt("L", 1.5), "R": lipt("R", 1.5)}, "inout"),
        Kf(30, k30, {"L": press("L", 0.8), "R": press("R", 0.8)}, "inout"),
        Kf(38, k38, {"L": press("L", 0.78), "R": press("R", 0.78)}, "inout"),
        Kf(46, k46, {}, "inout"), Kf(54, i0, {}, "inout")],
        events=[(10, "OnClimbGrab", "lip"), (30, "OnClimbStep", "L"), (38, "OnClimbStep", "R")],
        notes="ledge pull-over: reach up, grab the lip, pull (feet walk the face), press up on straight arms, right knee over the lip, stand up. Timing matches PlayerClimb (reach 0.35 s, pull ~0.8 s, over 0.6 s)")
    rig.reset()
    return C

CLIMB = ("Climb_Rock_Up", "Climb_Rock_Down", "Climb_Rock_Idle", "Ledge_Mantle")

def build_climb(rig, B, which=None):
    import pf_clips_weapons as W
    defs = climb_defs(rig); rep = {}; solved = {}
    for name, d in defs.items():
        if d.get("reverse_of"):
            # the down cycle replays the solved up poses backwards (same holds, no second IK solve that could differ)
            src = solved.get(d["reverse_of"]) or W.solve_keys(rig, [(f, b, {sd: dict({"starts": 1, "reg": 0.05}, **t) for sd, t in tg.items()}, e)
                                                                   for (f, b, tg, e) in defs[d["reverse_of"]]["keys"]])[0]
            d = dict(d); d["keys"] = [(d["frames"] - f, pose, {}, "linear") for (f, pose, e) in reversed(src)]
        if which is None or name in which:
            rep[name] = bake_keyed(rig, B, name, d, solved=solved)
            B.meta[name]["speed"] = d.get("speed", 0.0)
            if "root_speed" in d: B.meta[name]["extra"]["root_speed"] = d["root_speed"]
    return rep

# ================================================================== build
OVERRIDES = ("Walk", "Run", "Sprint", "Run_Backward", "Idle", "Turn_Left", "Turn_Right", "Eat", "Gather_Plant")
NEW_B1 = ("Walk_Start", "Walk_Stop", "Run_Start", "Run_Stop", "Run_Pivot_180", "Turn_180")

def build(rig, B, which=None):
    def want(n): return which is None or n in which
    report = {}
    gaits = {}
    for name, g in LOCO2.items():
        gaits[name] = Gait2(rig, name, g)
    for name, G in gaits.items():
        if not want(name): continue
        B.bake(name, G.N, True, G.fn, events=G.events(), speed=G.speed,
               notes=f"v2 in place; stance travel {G.S:.3f} m; turn {G.turn} deg/s; pelvis base {G.z0:.3f} m")
        B.meta[name]["extra"] = dict(root_speed=G.speed, root_distance_per_cycle=round(G.speed * G.T, 3), turn_deg_s=G.turn,
                                     mid_stance=G.mid_stance_frames())
    if want("Idle"):
        B.bake("Idle", IDLE_N, True, lambda f: apply(rig, idle2_pose(f)),
               notes="v2: 2 breaths per loop (20 / min), weight ~60/40 shifting leg to leg, pelvis tilted to the unloaded side, asymmetric feet, head drift < 3 deg")
    if any(want(n) for n in NEW_B1):
        T = build_transitions(rig, gaits["Walk"], gaits["Run"])
        for name, (N, fn, ev, speed, notes, extra) in T.items():
            if not want(name): continue
            # speed 0 in the meta: the builder's foot-slide test assumes a constant belt speed; the real speed curve of the
            # clip is in extra root_speed (and root_distance / root_yaw)
            B.bake(name, N, False, fn, events=ev, speed=0.0, notes=notes + f" (end speed {speed} m/s, see root_speed)")
            B.meta[name]["extra"] = extra
    if any(want(n) for n in BARE):
        report["bare"] = build_bare(rig, B, which)
    if any(want(n) for n in SURV):
        report["survival"] = build_survival(rig, B, which)
    if any(want(n) for n in CLIMB):
        report["climb"] = build_climb(rig, B, which)
    return report

