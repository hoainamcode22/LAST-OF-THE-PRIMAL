# PRIMAL FRONTIER - player animation clips (in place). Every clip is baked per frame with leg IK.
import bpy, math
from mathutils import Vector, Quaternion, Matrix
from pf_anim import HumanRig, ease, lerp, FPS

TAU = math.tau
LR = ("L", "R")
HumanRig.THUMB_ADDUCT = -30.0     # relaxed thumb lies along the index instead of pointing forward (see renders/characters/hands_thumb.png)

BASE = dict(
    pel=(0.0, 0.0, 0.0), pelr=(0.0, 0.0, 0.0),
    sp=(2.0, 0.0, 0.0), spu=(1.0, 0.0, 0.0), ch=(-2.0, 0.0, 0.0), nk=(4.0, 0.0, 0.0), hd=(-3.0, 0.0, 0.0),
    aL=(6.0, -35.0, 0.0, 0.0), aR=(6.0, -35.0, 0.0, 0.0), eL=22.0, eR=22.0,
    wL=(4.0, 0.0, 0.0), wR=(4.0, 0.0, 0.0), cL=(0.0, 0.0), cR=(0.0, 0.0), fL=(18.0, 16.0), fR=(18.0, 16.0),
    # feet: (dx, dy, dz, pitch, yaw, toebend) relative to rest, IK
    ftL=(0.0, 0.0, 0.0, 0.0, 0.0, 0.0), ftR=(0.0, 0.0, 0.0, 0.0, 0.0, 0.0),
    # FK legs (used when fk=1): (hip flex, hip abduct, knee flex, ankle)
    fk=0.0, lL=(0.0, 0.0, 0.0, 0.0), lR=(0.0, 0.0, 0.0, 0.0),
)

def P(**kw):
    d = dict(BASE); d.update(kw); return d

def interp(keys, f):
    """keys: [(frame, pose_dict, easing)] -> pose at frame f"""
    if f <= keys[0][0]: return keys[0][1]
    for i in range(len(keys) - 1):
        f0, p0, _ = keys[i]; f1, p1, e = keys[i + 1]
        if f0 <= f <= f1:
            t = ease((f - f0) / max(1e-6, f1 - f0), e)
            return {k: lerp(p0[k], p1[k], t) for k in p0}
    return keys[-1][1]

def add(p, **deltas):
    q = dict(p)
    for k, v in deltas.items():
        if isinstance(q[k], tuple): q[k] = tuple(a + b for a, b in zip(q[k], v))
        else: q[k] = q[k] + v
    return q

def apply(rig, p):
    rig.pelvis(p["pel"], *p["pelr"])
    for b, k in (("Spine", "sp"), ("Spine_Upper", "spu"), ("Chest", "ch"), ("Neck", "nk"), ("Head", "hd")):
        rig.spine_bone(b, *p[k])
    for sd in LR:
        rig.arm(sd, *p["a" + sd]); rig.elbow(sd, p["e" + sd]); rig.wrist(sd, *p["w" + sd])
        rig.clavicle(sd, *p["c" + sd]); rig.fingers(sd, *p["f" + sd])
    fk = p["fk"]
    if fk > 0.001:
        for sd in LR:
            hf, ab, kn, an = p["l" + sd]
            s = 1 if sd == "L" else -1
            from pf_anim import q_axis
            rig.set_rot(f"Thigh_{sd}", q_axis((1, 0, 0), -hf) @ q_axis((0, 1, 0), -ab * s))
            rig.set_rot(f"Calf_{sd}", q_axis((1, 0, 0), kn))
            rig.set_rot(f"Foot_{sd}", q_axis((1, 0, 0), -an))
            rig.set_rot(f"Toe_{sd}", Quaternion())
        if fk >= 0.999:
            return None
        # B6 (2026-09-30): partial FK weight = blend window between the FK legs (with their ground snap) and the IK legs;
        # Baker.bake solves the IK, then slerps the legs back towards these FK rotations by the weight (no one-frame pop)
        rig._fkw = fk
        rig._fkq = {f"{b}_{sd}": rig.obj.pose.bones[f"{b}_{sd}"].rotation_quaternion.copy() for sd in LR for b in ("Thigh", "Calf", "Foot", "Toe")}
        rig._fkdz = ground_delta(rig) if getattr(rig, "_ground", True) else 0.0
    feet = {}
    for sd in LR:
        dx, dy, dz, pitch, yaw, tb = p["ft" + sd]
        feet[sd] = rig.planted(sd, (dx, dy, dz), pitch, yaw, tb)
    return feet

# ------------------------------------------------------------------ baking
GROUND_RADII = {"Pelvis": 0.115, "Spine": 0.11, "Spine_Upper": 0.12, "Chest": 0.12, "Neck": 0.055, "Head": 0.095,
                "Thigh_L": 0.075, "Thigh_R": 0.075, "Calf_L": 0.05, "Calf_R": 0.05, "Foot_L": 0.035, "Foot_R": 0.035, "Toe_L": 0.015, "Toe_R": 0.015,
                "UpperArm_L": 0.045, "UpperArm_R": 0.045, "LowerArm_L": 0.035, "LowerArm_R": 0.035, "Hand_L": 0.025, "Hand_R": 0.025}

def ground_delta(rig):
    """vertical pelvis move that would put the lowest body surface on the ground (z = 0), not applied"""
    bpy.context.view_layer.update()
    ob = rig.obj; M = ob.matrix_world; lo = 1e9
    for b, r in GROUND_RADII.items():
        pb = ob.pose.bones.get(b)
        if not pb: continue
        h = M @ pb.head; t = M @ pb.tail
        for k in range(5):
            lo = min(lo, h.z + (t.z - h.z) * k / 4 - r)
    return -lo

def ground_snap(rig):
    """FK poses (lying / sitting): move the pelvis vertically so the lowest body surface touches the ground (z = 0)"""
    bpy.context.view_layer.update()
    ob = rig.obj; M = ob.matrix_world; lo = 1e9
    for b, r in GROUND_RADII.items():
        pb = ob.pose.bones.get(b)
        if not pb: continue
        h = M @ pb.head; t = M @ pb.tail
        for k in range(5):
            lo = min(lo, h.z + (t.z - h.z) * k / 4 - r)
    pb = ob.pose.bones["Pelvis"]
    R = ob.data.bones["Pelvis"].matrix_local.to_3x3()
    pb.location = pb.location + R.inverted() @ Vector((0, 0, -lo))
    bpy.context.view_layer.update()

class Baker:
    def __init__(self, rig):
        self.rig = rig; self.meta = {}

    def bake(self, name, frames, loop, fn, events=(), speed=0.0, notes=""):
        rig = self.rig
        rig.new_action(name, frames, loop)
        prev = {}
        for f in range(frames + 1):
            rig.reset(); rig._fkw = 0.0; rig._ground = getattr(fn, "ground", True)
            feet = fn(f)
            if feet:
                w = getattr(rig, "_fkw", 0.0)
                if w:
                    pb = rig.obj.pose.bones["Pelvis"]; Rp = rig.obj.data.bones["Pelvis"].matrix_local.to_3x3()
                    pb.location = pb.location + Rp.inverted() @ Vector((0, 0, w * rig._fkdz))
                for sd, tg in feet.items():
                    rig.solve_leg(sd, *tg)
                if w:
                    for bn, q in rig._fkq.items():
                        pbn = rig.obj.pose.bones[bn]; qi = pbn.rotation_quaternion.copy()
                        if qi.dot(q) < 0: q = -q
                        pbn.rotation_quaternion = qi.slerp(q, w)
                    rig._fkw = 0.0
            elif feet is None and getattr(fn, "ground", True):
                ground_snap(rig)
            for pb in rig.obj.pose.bones:
                q = pb.rotation_quaternion.copy()
                if pb.name in prev and prev[pb.name].dot(q) < 0: q.negate(); pb.rotation_quaternion = q
                prev[pb.name] = q
            rig.key_all(f)
        act = rig.obj.animation_data.action
        act.frame_range = (0, frames)
        self.meta[name] = dict(frames=frames, loop=loop, events=list(events), speed=speed, notes=notes, fps=FPS)
        return act

# ------------------------------------------------------------------ gait
def swing_g(v, w=0.25):
    """velocity-matching term for the swing: g(0)=g(1)=0, g'(0)=g'(1)=1, zero in the middle of the swing"""
    g = 0.0
    if v < w: g += v * (1 - v / w) ** 2
    if v > 1 - w: g -= (1 - v) * (1 - (1 - v) / w) ** 2
    return g

def gait_fn(rig, frames, D, S, step_h, strike, toeoff, lean, arm_amp, elbow0, elbow_amp, bob, sway, twist, roll,
            move=(0.0, -1.0), crouch=0.0, center_back=0.08, kick=0.0, turn=0.0, heel_off=0.68, heel_ease="in", leg_yaw=0.0, track=0.0):
    """returns fn(frame) for an in-place locomotion cycle. move = unit direction of body travel in armature XY.
    turn (deg/s) replaces translation with rotation of the stance feet around the body (turn on the spot)."""
    mv = Vector((move[0], move[1], 0.0)).normalized() if (move[0] or move[1]) else Vector((0, 0, 0))
    L1 = rig.len["Thigh_L"]; L2 = rig.len["Calf_L"]
    T = frames / FPS
    # strafing: legs and pelvis turn by leg_yaw towards the travel direction (upper body counter-rotates) so the feet
    # pass each other side by side instead of crossing; the foot homes rotate about the body centre
    Ry = Matrix.Rotation(math.radians(leg_yaw), 3, 'Z')
    home = {}
    for sd in LR:
        a = rig.foot_rest[sd]["ankle"]; a2 = Vector((a.x, a.y, 0.0))
        home[sd] = Ry @ a2 - a2
        if track and mv.length > 0:   # extra separation of the two foot tracks, perpendicular to travel
            perp = Vector((-mv.y, mv.x, 0.0))
            home[sd] += perp * (track if (Ry @ a2).dot(perp) > 0 else -track)
    def foot(sd, ph):
        if ph < D:
            u = ph / D
            s = S * 0.5 - S * u
            off = (mv * s - mv * center_back if turn == 0 else Vector((0, 0, 0))) + home[sd]
            yaw = leg_yaw
            if turn:
                yaw = -turn * (u - 0.5) * D * T
            if u < 0.14: pitch = -strike * (1 - ease(u / 0.14, "out"))
            elif u > heel_off: pitch = toeoff * ease((u - heel_off) / (1 - heel_off), heel_ease)
            else: pitch = 0.0
            return (off.x, off.y, 0.0, pitch, yaw, pitch), True
        v = (ph - D) / (1 - D)
        e = ease(v, "inout")
        # built in world space: the swinging foot travels one stride (S/D) with zero world velocity at lift-off and
        # touchdown, minus the body travel -> in-place the foot leaves and lands with the stance (belt) velocity (no skid)
        # the foot lifts (first 5%) and is set down (last 10%) almost vertically while still in world space
        ew = ease((v - 0.05) / 0.85, "smooth")
        s = -S * 0.5 + (S / D) * ew - S * (1 - D) / D * v
        off = (mv * s - mv * center_back if turn == 0 else Vector((0, 0, 0))) + home[sd]
        h = step_h * math.sin(math.pi * min(1, v * 1.05)) ** 1.2 + kick * math.sin(math.pi * min(1.0, v * 1.6)) ** 2
        if kick: off = off - mv * kick * 0.6 * math.sin(math.pi * min(1.0, v * 1.6)) ** 2
        # same yaw range as the stance phase (continuous at lift-off/touchdown) with matching angular velocity
        yaw = (-turn * D * T * 0.5 + turn * T * ew - turn * (1 - D) * T * v) if turn else leg_yaw
        if v < 0.3: pitch = toeoff * (1 - ease(v / 0.3)) + 5 * ease(v / 0.3)
        elif v < 0.8: pitch = 5 * (1 - ease((v - 0.3) / 0.5))
        else: pitch = -strike * ease((v - 0.8) / 0.2)
        return (off.x, off.y, h, pitch, yaw, max(0.0, pitch) * 0.5), False
    # pelvis height: bob, lowered wherever a grounded foot would be out of reach (no IK clamping -> no sliding).
    # the clamped curve is eroded then blurred over the cycle so it stays below every limit but has no pops
    reach = 0.99 * (L1 + L2)
    def sway_x(t): return sway * math.sin(TAU * (t - 0.05))
    def z_clamped(f):
        t = f / frames
        z = 0.5 * bob * math.cos(2 * TAU * (t - D * 0.5)) - crouch
        x = sway_x(t); zmax = 1e9
        for sd, ph0 in (("L", 0.0), ("R", 0.5)):
            (dx, dy, dz, pitch, yaw, tb), stance = foot(sd, (t + ph0) % 1.0)
            if not stance and dz > 0.02: continue   # a foot in the air may be pulled in by the IK
            tg = rig.planted(sd, (dx, dy, dz), pitch, yaw, tb)
            hip = Ry @ rig.rest[f"Thigh_{sd}"].translation
            hor = math.hypot(tg[0].x - hip.x - x, tg[0].y - hip.y)
            if hor < reach:
                zmax = min(zmax, tg[0].z + math.sqrt(reach * reach - hor * hor) - hip.z)
        return min(z, zmax)
    zr = [z_clamped(f) for f in range(frames)]
    k = max(2, frames // 8)
    er = [min(zr[(i + j) % frames] for j in range(-k, k + 1)) for i in range(frames)]
    wts = [math.cos(0.5 * math.pi * j / (k + 1)) ** 2 for j in range(-k, k + 1)]
    ztab = [sum(er[(i + j) % frames] * wts[j + k] for j in range(-k, k + 1)) / sum(wts) for i in range(frames)]
    def fn(f):
        t = f / frames
        c = math.cos(TAU * t)
        p = dict(BASE)
        x = sway_x(t); z = ztab[f % frames]
        pel_tw = -twist * c; pel_side = -roll * math.sin(TAU * (t - 0.05))
        feet_t = {}
        for sd, ph0 in (("L", 0.0), ("R", 0.5)):
            feet_t[sd] = foot(sd, (t + ph0) % 1.0)
        # pelvis turns with the forward leg, the chest counter-rotates (shoulders swing against the hips), the neck
        # cancels the chest so the head stays pointed where the body travels
        tl = turn * 0.08 if turn else 0.0            # turning on the spot: chest / head lead the feet
        p["pel"] = (x, 0.0, z)
        p["pelr"] = (lean * 0.4, pel_side, pel_tw + leg_yaw)
        p["sp"] = (2 + lean * 0.3, -pel_side * 0.4, twist * 0.3 * c - leg_yaw * 0.3 + tl * 0.3)
        p["spu"] = (1 + lean * 0.3, -pel_side * 0.3, twist * 0.5 * c - leg_yaw * 0.3 + tl * 0.4)
        p["ch"] = (-2 + lean * 0.2, pel_side * 0.15, twist * 0.7 * c - leg_yaw * 0.25 + tl * 0.5)
        p["nk"] = (4 - lean * 0.4, -pel_side * 0.2, -twist * 0.6 * c - leg_yaw * 0.15 + tl * 0.8)
        p["hd"] = (-3 - lean * 0.5 + 1.5 * math.cos(2 * TAU * (t - D * 0.5)) * min(1.0, bob / 0.03), 0.0, -twist * 0.35 * c + tl * 0.6)
        # arms: opposite to the legs, swing further forward than back; forearm and hand trail the upper arm
        def fwd(ph): return -math.cos(TAU * ph)                 # +1 = left arm fully forward (right leg forward)
        def asym(v): return v if v > 0 else v * 0.72
        sL, sLe, sLw = fwd(t), fwd(t - 0.06), fwd(t - 0.12)
        for sd, k in (("L", 1.0), ("R", -1.0)):
            s0, se, sw = k * sL, k * sLe, k * sLw
            p["a" + sd] = (6 + arm_amp * asym(s0), -35 + arm_amp * 0.08 + 1.5 * abs(s0), 0.0, arm_amp * 0.12 * s0)
            p["e" + sd] = elbow0 + elbow_amp * (0.5 + 0.5 * se)
            p["w" + sd] = (4 - 6.0 * math.sin(TAU * (t - 0.12)) * k * min(1.0, arm_amp / 20.0), 0.0, 0.0)
            p["c" + sd] = (0.8 * math.cos(2 * TAU * (t - D * 0.5)) * min(1.0, bob / 0.03), 0.12 * arm_amp * s0)
        if elbow0 > 50:
            p["fL"] = (55.0, 35.0); p["fR"] = (55.0, 35.0)
        for sd in LR:
            (dx, dy, dz, pitch, yaw, tb), stance = feet_t[sd]
            p["ft" + sd] = (dx, dy, dz, pitch, yaw, tb)
        return apply(rig, p)
    fn.zr = zr; fn.ztab = ztab; fn.foot = foot
    return fn

def foot_events(frames, D):
    return [(0, "OnFootstep", "L"), (int(round(frames * 0.5)), "OnFootstep", "R")]

# ------------------------------------------------------------------ clip library
def build_all(rig, which=None):
    B = Baker(rig)
    import pf_clips_weapons as W
    import pf_clips_c as C
    # weapon clips come from pf_clips_weapons, the CHAR-phase clips (locomotion v2, idle v2, turns, new sets) from pf_clips_c
    def want(n): return (which is None or n in which) and n not in W.OVERRIDES and n not in C.OVERRIDES
    # ---- idle
    if want("Idle"):
        def idle(f, N=180):
            t = f / N
            br = math.sin(TAU * 3 * t)                     # three breaths per loop (2 s each)
            ws = math.sin(TAU * t)                         # weight over the left leg, then the right
            w2 = math.sin(TAU * 2 * t + 0.7)
            lk = math.sin(TAU * t + 1.3); lk2 = math.sin(TAU * 2 * t + 2.1)
            p = add(BASE, pel=(0.022 * ws, 0.0, -0.012 - 0.008 * ws * ws), pelr=(0.0, -2.2 * ws, 1.5 * w2),
                    sp=(0.4 * br, 0.9 * ws, -0.6 * w2), spu=(1.5 * br, 0.7 * ws, -0.4 * w2), ch=(1.8 * br, 0.3 * ws, -0.3 * w2),
                    nk=(0.8 * lk2, -0.6 * ws, 2.5 * lk), hd=(-1.2 * br + 1.0 * lk2, -0.8 * ws, 2.0 * lk),
                    cL=(1.6 * br, 0.4 * ws), cR=(1.6 * br, -0.4 * ws),
                    aL=(1.5 * w2, 1.0 * br, 0, 0), aR=(-1.5 * w2, 1.0 * br, 0, 0), eL=3.0 * w2, eR=-3.0 * w2,
                    wL=(2.0 * lk2, 0, 0), wR=(-2.0 * lk2, 0, 0), fL=(3.0 * lk, 2.0 * lk), fR=(-3.0 * lk, -2.0 * lk))
            return apply(rig, p)
        B.bake("Idle", 180, True, idle, notes="breathing, weight shift leg to leg, soft knees, head and hand life")
    if want("Idle_Variation"):
        N = 150
        K = [(0, P(), "smooth"), (30, P(nk=(2, 0, 25), hd=(-6, 0, 20), ch=(-2, 0, 8)), "inout"), (60, P(nk=(2, 0, 25), hd=(-6, 0, 20), ch=(-2, 0, 8)), "smooth"),
             (85, P(nk=(2, 0, -22), hd=(-2, 0, -20), ch=(-2, 0, -8)), "inout"), (105, P(nk=(8, 0, -10), hd=(6, 0, -8), aL=(10, -30, 0, 0), eL=40, wL=(20, 0, 0), cL=(4, 0)), "inout"),
             (125, P(sp=(4, 0, 0), aR=(8, -35, 0, 0)), "smooth"), (150, P(), "smooth")]
        def idle_var(f):
            t = f / N; br = math.sin(TAU * 3 * t); ws = math.sin(TAU * t)
            return apply(rig, add(interp(K, f), pel=(0.018 * ws, 0.0, -0.012 - 0.006 * ws * ws), pelr=(0.0, -1.8 * ws, 0.0),
                                  sp=(0.4 * br, 0.7 * ws, 0.0), spu=(1.4 * br, 0.5 * ws, 0.0), ch=(1.6 * br, 0.0, 0.0), cL=(1.4 * br, 0.0), cR=(1.4 * br, 0.0)))
        B.bake("Idle_Variation", N, True, idle_var, notes="looks left/right, rubs arm, shifts weight")
    # ---- locomotion (speed in m/s for gameplay sync)
    loco = {
        "Walk": dict(frames=30, D=0.56, speed=1.35, step_h=0.09, strike=14, toeoff=55, heel_off=0.4, heel_ease="smooth", lean=3, arm_amp=24, elbow0=18, elbow_amp=24, bob=0.035, sway=0.014, twist=6, roll=3, move=(0, -1), center_back=0.1),
        "Walk_Backward": dict(frames=30, D=0.58, speed=1.05, step_h=0.07, strike=-14, toeoff=-25, heel_off=0.5, heel_ease="smooth", lean=-2, arm_amp=14, elbow0=18, elbow_amp=12, bob=0.025, sway=0.012, twist=3, roll=2, move=(0, 1), center_back=0.04),
        "Walk_Left": dict(frames=24, D=0.56, speed=1.1, step_h=0.07, strike=0, toeoff=30, heel_off=0.5, heel_ease="smooth", lean=0, arm_amp=6, elbow0=14, elbow_amp=6, bob=0.02, sway=0.004, twist=2, roll=2, move=(1, 0), center_back=0.0, leg_yaw=50.0, track=0.06),
        "Walk_Right": dict(frames=24, D=0.56, speed=1.1, step_h=0.07, strike=0, toeoff=30, heel_off=0.5, heel_ease="smooth", lean=0, arm_amp=6, elbow0=14, elbow_amp=6, bob=0.02, sway=0.004, twist=2, roll=2, move=(-1, 0), center_back=0.0, leg_yaw=-50.0, track=0.06),
        "Run": dict(frames=20, D=0.32, speed=3.8, step_h=0.16, strike=4, toeoff=60, heel_off=0.4, heel_ease="smooth", lean=9, arm_amp=42, elbow0=80, elbow_amp=20, bob=-0.045, sway=0.01, twist=11, roll=4, move=(0, -1), kick=0.12, center_back=0.1),
        "Sprint": dict(frames=14, D=0.24, speed=6.2, step_h=0.2, strike=0, toeoff=70, heel_off=0.3, heel_ease="smooth", lean=15, arm_amp=60, elbow0=86, elbow_amp=16, bob=-0.05, sway=0.008, twist=13, roll=4, move=(0, -1), kick=0.22, center_back=0.1),
        "Run_Backward": dict(frames=24, D=0.42, speed=2.4, step_h=0.11, strike=-10, toeoff=-22, heel_off=0.5, heel_ease="smooth", lean=-3, arm_amp=22, elbow0=62, elbow_amp=14, bob=-0.03, sway=0.01, twist=5, roll=2, move=(0, 1), center_back=0.04),
        "Strafe_Run_L": dict(frames=18, D=0.36, speed=3.0, step_h=0.12, strike=0, toeoff=40, heel_off=0.45, heel_ease="smooth", lean=4, arm_amp=16, elbow0=62, elbow_amp=12, bob=-0.03, sway=0.004, twist=3, roll=2, move=(1, 0), center_back=0.0, leg_yaw=60.0, track=0.09, kick=0.04),
        "Strafe_Run_R": dict(frames=18, D=0.36, speed=3.0, step_h=0.12, strike=0, toeoff=40, heel_off=0.45, heel_ease="smooth", lean=4, arm_amp=16, elbow0=62, elbow_amp=12, bob=-0.03, sway=0.004, twist=3, roll=2, move=(-1, 0), center_back=0.0, leg_yaw=-60.0, track=0.09, kick=0.04),
        "Crouch_Walk": dict(frames=40, D=0.68, speed=0.95, step_h=0.08, strike=8, toeoff=20, lean=22, arm_amp=14, elbow0=38, elbow_amp=14, bob=0.02, sway=0.018, twist=4, roll=2, move=(0, -1), crouch=0.3),
        "Turn_Left": dict(frames=30, D=0.6, speed=0.0, step_h=0.06, strike=0, toeoff=10, lean=1, arm_amp=6, elbow0=14, elbow_amp=6, bob=0.015, sway=0.012, twist=3, roll=2, move=(0, 0), turn=90.0),
        "Turn_Right": dict(frames=30, D=0.6, speed=0.0, step_h=0.06, strike=0, toeoff=10, lean=1, arm_amp=6, elbow0=14, elbow_amp=6, bob=0.015, sway=0.012, twist=3, roll=2, move=(0, 0), turn=-90.0),
    }
    for name, g in loco.items():
        if not want(name): continue
        g = dict(g); frames = g.pop("frames"); speed = g.pop("speed")
        D = g["D"]; T = frames / FPS
        S = speed * D * T
        fn = gait_fn(rig, frames, S=S, **g)
        if name == "Crouch_Walk":
            base_fn = fn
            def fn(f, base_fn=base_fn):
                return base_fn(f)
        B.bake(name, frames, True, fn, events=foot_events(frames, D), speed=speed,
               notes=f"in place; stance travel {S:.3f} m; turn {g.get('turn', 0)} deg/s")
    # ---- crouch idle
    if want("Crouch"):
        def crouch(f, N=60):
            br = math.sin(TAU * f / N)
            p = P(pel=(0, 0.02, -0.33 + 0.004 * br), pelr=(10, 0, 0), sp=(10, 0, 0), spu=(6 + br, 0, 0), ch=(2, 0, 0), nk=(-10, 0, 0), hd=(-12, 0, 0),
                  aL=(25, -38, 0, 0), aR=(25, -38, 0, 0), eL=35, eR=35, ftL=(0.02, -0.02, 0, 0, 8, 0), ftR=(-0.02, 0.05, 0, 0, -8, 0))
            return apply(rig, p)
        B.bake("Crouch", 60, True, crouch)
    # ---- jump / fall / land
    if want("Jump"):
        K = [(0, P(), "smooth"),
             (7, P(pel=(0, 0.02, -0.17), pelr=(12, 0, 0), sp=(12, 0, 0), aL=(-35, -30, 0, 0), aR=(-35, -30, 0, 0), eL=25, eR=25), "out"),
             (11, P(pel=(0, 0, 0.02), pelr=(-2, 0, 0), sp=(-2, 0, 0), aL=(60, -25, 0, 0), aR=(60, -25, 0, 0), eL=30, eR=30,
                    ftL=(0, 0, 0.0, 40, 0, 40), ftR=(0, 0, 0.0, 40, 0, 40)), "in"),
             (18, P(pel=(0, 0, 0.0), aL=(45, -20, 0, 0), aR=(45, -20, 0, 0), eL=40, eR=40, ftL=(0, 0.05, 0.22, 20, 0, 5), ftR=(0, 0.0, 0.18, 15, 0, 5)), "out"),
             (24, P(pel=(0, 0, 0.0), aL=(30, -15, 0, 0), aR=(30, -15, 0, 0), eL=40, eR=40, ftL=(0, 0.02, 0.15, 10, 0, 5), ftR=(0, 0.0, 0.14, 10, 0, 5)), "smooth")]
        B.bake("Jump", 24, False, lambda f: apply(rig, interp(K, f)), events=[(11, "OnJumpTakeoff", "")], notes="anticipation -> takeoff -> tuck; physics moves the body")
    if want("Fall"):
        def fall(f, N=30):
            t = f / N; s = math.sin(TAU * t)
            p = P(pel=(0, 0, 0.0), sp=(-4, 0, 0), aL=(25 + 8 * s, -5, 0, 0), aR=(25 - 8 * s, -5, 0, 0), eL=30, eR=30, hd=(-8, 0, 0),
                  ftL=(0, 0.04 + 0.03 * s, 0.14, 12, 0, 5), ftR=(0, 0.04 - 0.03 * s, 0.18, 12, 0, 5), fL=(25, 20), fR=(25, 20))
            return apply(rig, p)
        B.bake("Fall", 30, True, fall)
    if want("Land"):
        K = [(0, P(ftL=(0, 0.02, 0.1, -10, 0, 0), ftR=(0, 0.0, 0.1, -10, 0, 0), aL=(25, -5, 0, 0), aR=(25, -5, 0, 0), eL=30, eR=30), "smooth"),
             (3, P(pel=(0, 0.02, -0.2), pelr=(14, 0, 0), sp=(14, 0, 0), aL=(20, -25, 0, 0), aR=(20, -25, 0, 0), eL=35, eR=35), "out"),
             (8, P(pel=(0, 0.02, -0.12), pelr=(8, 0, 0), sp=(8, 0, 0), aL=(15, -32, 0, 0), aR=(15, -32, 0, 0), eL=25, eR=25), "smooth"),
             (20, P(), "inout")]
        B.bake("Land", 20, False, lambda f: apply(rig, interp(K, f)), events=[(3, "OnLand", "")])
    # ---- gather / interact
    kneel_R = dict(pel=(0, 0.08, -0.44), pelr=(8, 0, 0), ftL=(0.0, -0.2, 0, 0, 5, 0), ftR=(0.0, 0.32, 0.0, 65, -5, 65))
    if want("Pickup"):
        K = [(0, P(), "smooth"),
             (12, P(pel=(0, 0.1, -0.36), pelr=(22, 0, 0), sp=(22, 0, 0), spu=(14, 0, 0), ch=(6, 0, 0), nk=(4, 0, 0), hd=(8, 0, 0),
                    aR=(60, -20, 0, 0), eR=20, wR=(10, 0, 0), fR=(10, 5), aL=(20, -35, 0, 0), eL=30, ftL=(0, -0.08, 0, 0, 5, 0), ftR=(0, 0.1, 0, 20, -5, 20)), "inout"),
             (16, P(pel=(0, 0.1, -0.36), pelr=(22, 0, 0), sp=(22, 0, 0), spu=(14, 0, 0), ch=(6, 0, 0), nk=(4, 0, 0), hd=(8, 0, 0),
                    aR=(62, -20, 0, 0), eR=30, wR=(20, 0, 0), fR=(70, 50), aL=(20, -35, 0, 0), eL=30, ftL=(0, -0.08, 0, 0, 5, 0), ftR=(0, 0.1, 0, 20, -5, 20)), "smooth"),
             (30, P(aR=(30, -35, 0, 0), eR=70, fR=(70, 50)), "inout"), (40, P(), "smooth")]
        B.bake("Pickup", 40, False, lambda f: apply(rig, interp(K, f)), events=[(16, "OnPickup", "")])
    def chop(name, N, lift_up, strike_down, low, hit_frame):
        ready = P(pel=(0, 0.02, -0.05), pelr=(4, 0, -10), sp=(6, 0, -5), ch=(0, 0, -10), ftL=(0.03, -0.12, 0, 0, 15, 0), ftR=(-0.03, 0.1, 0, 0, -20, 0),
                  aR=(40, -25, 0, 25), eR=80, aL=(35, -30, 0, 30), eL=85, fR=(70, 50), fL=(70, 50), wR=(-20, 0, 0))
        up = add(ready, pelr=(-4, 0, 8), sp=(-6, 0, 6), ch=(-6, 0, 10), aR=(lift_up - 40, 20, 0, -20), eR=40, aL=(lift_up - 45, 15, 0, -15), eL=50, wR=(-30, 0, 0))
        hit = add(ready, pel=(0, 0, -0.08), pelr=(10, 0, -4), sp=(strike_down, 0, -4), ch=(8, 0, -6), aR=(low - 40, 5, 0, 5), eR=-40, aL=(low - 35, 5, 0, 5), eL=-45, wR=(25, 0, 0))
        K = [(0, ready, "smooth"), (int(N * 0.42), up, "inout"), (hit_frame, hit, "in"), (hit_frame + 3, add(hit, sp=(2, 0, 0)), "out"), (N, ready, "inout")]
        B.bake(name, N, True, lambda f: apply(rig, interp(K, f)), events=[(hit_frame, "OnGatherHit", name)])
    if want("Gather_Wood"): chop("Gather_Wood", 36, 130, 18, 55, 21)
    if want("Gather_Stone"): chop("Gather_Stone", 34, 145, 30, 40, 20)
    if want("Interact"):
        K = [(0, P(), "smooth"), (10, P(sp=(8, 0, 0), aR=(70, -25, 0, 5), eR=20, wR=(-10, 0, 0), fR=(5, 5), hd=(6, 0, 0)), "inout"),
             (16, P(sp=(10, 0, 0), aR=(76, -25, 0, 5), eR=15, wR=(-20, 0, 0), fR=(5, 5), hd=(8, 0, 0)), "out"), (30, P(), "inout")]
        B.bake("Interact", 30, False, lambda f: apply(rig, interp(K, f)), events=[(14, "OnInteract", "")])
    if want("Craft"):
        def craft(f, N=60):
            t = f / N; s = math.sin(TAU * t * 2)
            p = P(**kneel_R, sp=(20, 0, 0), spu=(12, 0, 0), ch=(6, 0, 0), nk=(8, 0, 0), hd=(14, 0, 0),
                  aL=(55 + 4 * s, -20, 0, 20), eL=75 + 6 * s, aR=(50 - 6 * s, -20, 0, 25), eR=70 - 8 * s, wR=(10 * s, 0, 0), fL=(60, 40), fR=(50, 40))
            return apply(rig, p)
        B.bake("Craft", 60, True, craft, events=[(15, "OnCraftTick", ""), (45, "OnCraftTick", "")])
    if want("Drink"):
        low = P(**kneel_R, sp=(30, 0, 0), spu=(18, 0, 0), ch=(10, 0, 0), nk=(4, 0, 0), hd=(10, 0, 0), aL=(70, -10, 0, 30), eL=20, aR=(70, -10, 0, 30), eR=20, wL=(-20, 0, 0), wR=(-20, 0, 0), fL=(30, 20), fR=(30, 20))
        cup = add(low, sp=(-18, 0, 0), spu=(-10, 0, 0), ch=(-6, 0, 0), nk=(-4, 0, 0), hd=(-18, 0, 0), aL=(15, 0, 0, 5), eL=95, aR=(15, 0, 0, 5), eR=95, wL=(30, 0, 0), wR=(30, 0, 0))
        K = [(0, P(), "smooth"), (18, low, "inout"), (26, low, "smooth"), (40, cup, "inout"), (52, add(cup, hd=(-6, 0, 0)), "smooth"), (70, P(), "inout")]
        B.bake("Drink", 70, False, lambda f: apply(rig, interp(K, f)), events=[(44, "OnDrink", "")])
    if want("Eat"):
        bite = P(aR=(35, -10, 0, 30), eR=120, wR=(10, 0, 0), fR=(60, 40), hd=(4, 0, 0), nk=(6, 0, 0))
        K = [(0, P(), "smooth"), (14, bite, "inout"), (20, add(bite, hd=(4, 0, 0)), "smooth"), (26, add(bite, eR=-10), "smooth"), (32, bite, "smooth"), (48, P(), "inout")]
        B.bake("Eat", 48, False, lambda f: apply(rig, interp(K, f)), events=[(18, "OnEat", ""), (30, "OnEat", "")])
    # ---- combat
    spear_ready = P(pel=(0, 0.02, -0.06), pelr=(4, 0, -25), sp=(4, 0, -10), ch=(0, 0, -10), nk=(0, 0, 20), hd=(-2, 0, 15),
                    ftL=(0.02, -0.14, 0, 0, 20, 0), ftR=(-0.02, 0.14, 0, 0, -30, 0),
                    aR=(30, -30, 0, 0), eR=95, wR=(-10, 0, 0), aL=(55, -10, 0, 25), eL=40, fR=(70, 50), fL=(70, 50))
    if want("Attack_Spear"):
        wind = add(spear_ready, pel=(0, 0.04, 0), pelr=(0, 0, -12), ch=(-4, 0, -12), aR=(-10, 0, 0, 0), eR=20, aL=(-5, 0, 0, 0))
        thrust = add(spear_ready, pel=(0, -0.1, -0.04), pelr=(6, 0, 25), sp=(6, 0, 10), ch=(4, 0, 18), aR=(45, 5, 0, 0), eR=-80, aL=(15, 5, 0, 0), eL=-25, ftL=(0, -0.06, 0, 0, 0, 0), ftR=(0, 0.0, 0, 25, 0, 25))
        K = [(0, spear_ready, "smooth"), (8, wind, "inout"), (12, thrust, "in"), (16, add(thrust, aR=(3, 0, 0, 0)), "out"), (30, spear_ready, "inout")]
        B.bake("Attack_Spear", 30, False, lambda f: apply(rig, interp(K, f)), events=[(12, "OnAttackHit", "spear_thrust")])
    if want("Spear_Attack_2"):
        up = add(spear_ready, pelr=(0, 0, 10), ch=(-6, 0, 12), aR=(95, 20, 0, 0), eR=40, aL=(75, 5, 0, 0), eL=20)
        down = add(spear_ready, pel=(0, -0.08, -0.1), pelr=(14, 0, 0), sp=(14, 0, 0), ch=(8, 0, 0), aR=(40, 0, 0, 0), eR=-60, aL=(30, 0, 0, 0), eL=-20, ftR=(0, 0.0, 0, 20, 0, 20))
        K = [(0, spear_ready, "smooth"), (10, up, "inout"), (14, down, "in"), (18, add(down, sp=(2, 0, 0)), "out"), (34, spear_ready, "inout")]
        B.bake("Spear_Attack_2", 34, False, lambda f: apply(rig, interp(K, f)), events=[(14, "OnAttackHit", "spear_downstab")],
               notes="combo follow-up: overhead wind-up, downward stab, recovery")
    if want("Throw_Spear"):
        aim = add(spear_ready, pelr=(-2, 0, -20), ch=(-8, 0, -20), aR=(60, 60, 0, -40), eR=90, wR=(-20, 0, 0), aL=(70, -5, 0, 20), eL=10, fL=(10, 5))
        release = add(spear_ready, pel=(0, -0.12, -0.04), pelr=(10, 0, 25), sp=(10, 0, 12), ch=(8, 0, 20), aR=(110, 20, 0, 0), eR=-40, wR=(30, 0, 0), aL=(20, -30, 0, 0), eL=30, fR=(5, 5),
                      ftR=(0, 0.0, 0, 30, 0, 30))
        K = [(0, spear_ready, "smooth"), (14, aim, "inout"), (20, aim, "smooth"), (24, release, "in"), (28, add(release, sp=(4, 0, 0)), "out"), (46, P(), "inout")]
        B.bake("Throw_Spear", 46, False, lambda f: apply(rig, interp(K, f)), events=[(24, "OnThrowRelease", "spear")])
    bow_idle = P(pelr=(0, 0, -30), sp=(0, 0, -10), ch=(0, 0, -20), nk=(0, 0, 30), hd=(0, 0, 25), ftL=(0.03, -0.12, 0, 0, 25, 0), ftR=(-0.03, 0.12, 0, 0, -40, 0),
                 aL=(45, -25, 0, 30), eL=15, fL=(65, 45), aR=(30, -30, 0, 10), eR=70, fR=(40, 30))
    bow_drawn = add(bow_idle, aL=(40, 25, 0, 5), eL=-15, wL=(-5, 0, 0), aR=(50, 65, 0, -25), eR=125, wR=(-20, 0, 0), fR=(50, 35), ch=(-2, 0, -4))
    if want("Bow_Aim"):
        def bi(f, N=60):
            br = math.sin(TAU * f / N)
            return apply(rig, add(bow_idle, spu=(br, 0, 0), aL=(br, 0, 0, 0)))
        B.bake("Bow_Aim", 60, True, bi)
    if want("Bow_Draw"):
        def bd(f, N=26):
            k = ease(min(1, f / 22), "inout")
            p = {kk: lerp(bow_idle[kk], bow_drawn[kk], k) for kk in bow_idle}
            if f > 22: p = add(p, aR=(0.2 * math.sin(f), 0, 0, 0))
            return apply(rig, p)
        B.bake("Bow_Draw", 26, False, bd, events=[(4, "OnBowDrawStart", "")])
    if want("Bow_Release"):
        rel = add(bow_drawn, aR=(-10, 20, 0, 0), eR=100, fR=(5, 5), wR=(10, 0, 0))
        K = [(0, bow_drawn, "smooth"), (3, rel, "out"), (10, add(rel, aR=(-5, 0, 0, 0)), "smooth"), (28, bow_idle, "inout")]
        B.bake("Bow_Release", 28, False, lambda f: apply(rig, interp(K, f)), events=[(1, "OnBowRelease", "")])
    # ---- reactions
    if want("Hurt"):
        hit = P(pel=(0, 0.05, -0.03), pelr=(-6, 0, 6), sp=(-8, 2, 0), ch=(-6, 0, 4), nk=(-6, 0, 0), hd=(-10, 4, 8), aL=(-10, -25, 0, 0), aR=(-8, -25, 0, 0), eL=30, eR=30, ftR=(0, 0.04, 0, 10, 0, 10))
        K = [(0, P(), "smooth"), (4, hit, "out"), (10, add(hit, sp=(3, 0, 0)), "smooth"), (24, P(), "inout")]
        B.bake("Hurt", 24, False, lambda f: apply(rig, interp(K, f)), events=[(0, "OnHurt", "light")])
    if want("Hurt_Heavy"):
        hit = P(pel=(0, 0.14, -0.12), pelr=(-4, 0, 10), sp=(20, 4, 6), spu=(14, 0, 0), ch=(10, 0, 0), nk=(-10, 0, 0), hd=(-12, 5, 10),
                aL=(40, -20, 0, 20), eL=90, aR=(35, -20, 0, 20), eR=95, ftL=(0, 0.12, 0, 0, 0, 0), ftR=(0, 0.18, 0, 30, 0, 30))
        K = [(0, P(), "smooth"), (5, hit, "out"), (16, add(hit, pel=(0, 0.02, 0.02)), "smooth"), (40, P(), "inout")]
        B.bake("Hurt_Heavy", 40, False, lambda f: apply(rig, interp(K, f)), events=[(0, "OnHurt", "heavy")])
    lying = P(fk=1.0, pel=(0, 0.55, -0.78), pelr=(-88, 0, 0), sp=(-2, 0, 0), spu=(0, 0, 0), ch=(0, 0, 0), nk=(-6, 0, 0), hd=(-10, 6, 20),
              aL=(-10, 10, 0, 0), aR=(-10, 20, 0, 0), eL=20, eR=35, fL=(35, 20), fR=(30, 20), lL=(8, 4, 10, 30), lR=(4, 8, 18, 30))
    if want("Death"):
        knees = P(**kneel_R, sp=(12, 0, 0), aL=(10, -35, 0, 0), aR=(10, -35, 0, 0), eL=25, eR=25, hd=(10, 0, 0))
        K = [(0, P(), "smooth"), (10, add(P(), pel=(0, 0.08, -0.1), sp=(10, 0, 0), hd=(12, 0, 0)), "out"), (24, knees, "inout")]
        def death(f):
            if f <= 24: return apply(rig, interp(K, f))
            # collapse backwards onto the ground with FK legs (feet leave the ground)
            k = ease((f - 24) / 26.0, "in")
            p = {kk: lerp(knees[kk], lying[kk], k) for kk in knees}
            p["fk"] = ease(min(1.0, (f - 24) / 10.0), "smooth")         # B6: IK -> FK over 10 frames
            p["ftL"] = knees["ftL"]; p["ftR"] = knees["ftR"]             # the IK side of the blend keeps the kneel feet
            # FK legs start from the kneel's own angles (left knee up, right knee down): the original (20, 30) / (-60, 110)
            # start stood the body up on a straight left leg once the ground snap applied
            p["lL"] = lerp((70, 0, 95, 10), lying["lL"], k); p["lR"] = lerp((-10, 0, 110, 35), lying["lR"], k)
            return apply(rig, p)
        B.bake("Death", 60, False, death, events=[(50, "OnBodyFall", "")], notes="ends lying on the back")
    if want("Get_Up"):
        sit = P(fk=1.0, pel=(0, 0.25, -0.62), pelr=(-15, 0, 0), sp=(20, 0, 0), spu=(10, 0, 0), aL=(-30, -30, 0, 0), aR=(-30, -30, 0, 0), eL=10, eR=10,
                lL=(80, 5, 60, 10), lR=(85, 8, 80, 10))
        def revive(f):
            if f <= 20:
                k = ease(f / 20.0); return apply(rig, {kk: lerp(lying[kk], sit[kk], k) for kk in lying})
            if f <= 34:
                k = ease((f - 20) / 14.0)
                kneel = P(**kneel_R, sp=(18, 0, 0), aL=(20, -30, 0, 0), aR=(40, -20, 0, 0), eR=30)
                p = {kk: lerp(sit[kk], kneel[kk], k) for kk in sit}
                p["fk"] = 1.0 - ease(min(1.0, max(0.0, (k - 0.25) / 0.5)), "smooth")   # B6: FK -> IK over ~6 frames
                p["lL"] = sit["lL"]; p["lR"] = sit["lR"]
                return apply(rig, p)
            k = ease((f - 34) / 26.0, "inout")
            kneel = P(**kneel_R, sp=(18, 0, 0), aL=(20, -30, 0, 0), aR=(40, -20, 0, 0), eR=30)
            return apply(rig, {kk: lerp(kneel[kk], BASE[kk], k) for kk in BASE})
        B.bake("Get_Up", 60, False, revive, notes="lying -> sit -> kneel -> stand (GET_UP)")
    if want("Sleep"):
        def sleep(f, N=120):
            br = math.sin(TAU * f / N)
            side = dict(lying); side["pelr"] = (-88, 0, 70); side["hd"] = (-2, 20, 30); side["aL"] = (60, -20, 0, 10); side["aR"] = (40, 10, 0, 30)
            side["eL"] = 90; side["eR"] = 80; side["lL"] = (40, 0, 60, 20); side["lR"] = (30, 0, 50, 20); side["pel"] = (0, 0.5, -0.83)
            return apply(rig, add(side, spu=(1.5 * br, 0, 0), ch=(1.0 * br, 0, 0)))
        B.bake("Sleep", 120, True, sleep, notes="lying on the side, breathing")
    if want("Build"):
        def build(f, N=40):
            t = f / N
            up = t < 0.55
            k = ease(t / 0.55, "inout") if up else ease((t - 0.55) / 0.12, "in") if t < 0.67 else 1 - ease((t - 0.67) / 0.33, "inout") * 0
            ph = math.sin(math.pi * min(1, t / 0.55)) if up else max(0.0, 1 - (t - 0.55) / 0.12)
            p = P(**kneel_R, sp=(14, 0, 0), spu=(8, 0, 0), hd=(12, 0, 0), aL=(50, -15, 0, 20), eL=40, fL=(40, 30),
                  aR=(40 + 60 * ph, -15, 0, 10), eR=70 + 40 * ph, wR=(-30 * ph + 20 * (1 - ph), 0, 0), fR=(70, 50))
            return apply(rig, p)
        B.bake("Build", 40, True, build, events=[(24, "OnBuildHit", "")])
    if want("Carry_Item"):
        def carry(f, N=60):
            br = math.sin(TAU * f / N)
            p = P(sp=(-2, 0, 0), spu=(-2 + br, 0, 0), aL=(35, -20, 0, 25), eL=95, aR=(35, -20, 0, 25), eR=95, wL=(-10, 0, 0), wR=(-10, 0, 0),
                  fL=(50, 30), fR=(50, 30), cL=(3, 4), cR=(3, 4), hd=(2, 0, 0))
            return apply(rig, p)
        B.bake("Carry_Item", 60, True, carry, notes="upper-body carry pose (use on an upper-body Animator layer while walking)")
    # ---- v2 additions (vertical slice brief)
    if want("Gather_Plant"):
        ready = P(pel=(0, 0.1, -0.40), pelr=(18, 0, 0), sp=(22, 0, 0), spu=(12, 0, 0), ch=(6, 0, 0), nk=(4, 0, 0), hd=(12, 0, 0),
                  aL=(50, -20, 0, 15), eL=40, aR=(50, -20, 0, 15), eR=40, fL=(30, 20), fR=(30, 20),
                  ftL=(0.0, -0.18, 0, 0, 5, 0), ftR=(0.0, 0.2, 0, 40, -5, 40))
        reach = add(ready, pelr=(6, 0, 0), sp=(8, 0, 0), hd=(4, 0, 0), aL=(28, 5, 0, -5), eL=-30, aR=(30, 5, 0, -5), eR=-32, fL=(-15, -10), fR=(-15, -10))
        grab = add(reach, fL=(55, 40), fR=(55, 40))
        pull = add(ready, pel=(0, 0.04, 0.02), pelr=(-6, 0, 4), sp=(-12, 0, 4), ch=(-4, 0, 6), aL=(-10, 0, 0, 10), eL=35, aR=(-15, 0, 0, 12), eR=40, fL=(55, 40), fR=(55, 40))
        toss = add(pull, pelr=(0, 0, -10), ch=(0, 0, -12), aR=(10, 35, 0, -20), eR=10, fR=(-10, -5), hd=(0, 0, -15))
        K = [(0, ready, "smooth"), (12, reach, "inout"), (17, grab, "smooth"), (26, pull, "out"), (34, toss, "inout"), (48, ready, "inout")]
        B.bake("Gather_Plant", 48, True, lambda f: apply(rig, interp(K, f)), events=[(26, "OnGatherHit", "Gather_Plant")])
    if want("Attack_Spear_Heavy"):
        wind = add(spear_ready, pel=(0, 0.08, -0.02), pelr=(-4, 0, -25), sp=(-4, 0, -10), ch=(-8, 0, -18), nk=(0, 0, 10),
                   aR=(-25, 10, 0, 0), eR=40, aL=(20, -10, 0, 10), eL=60)
        step = add(wind, pel=(0, -0.08, 0.0), ftL=(0, -0.14, 0.07, -10, 0, 0))
        lunge = add(spear_ready, pel=(0, -0.24, -0.13), pelr=(12, 0, 30), sp=(10, 0, 14), ch=(6, 0, 22), aR=(55, 5, 0, 0), eR=-90, aL=(20, 5, 0, 0), eL=-30,
                    ftL=(0, -0.28, 0, 0, 0, 0), ftR=(0, 0.05, 0, 35, 0, 35))
        hold = add(lunge, sp=(3, 0, 0), aR=(3, 0, 0, 0))
        back = add(spear_ready, ftL=(0, -0.14, 0.05, 0, 0, 0))
        K = [(0, spear_ready, "smooth"), (14, wind, "inout"), (18, wind, "smooth"), (21, step, "in"), (24, lunge, "out"), (29, hold, "smooth"),
             (38, back, "inout"), (46, spear_ready, "inout")]
        B.bake("Attack_Spear_Heavy", 46, False, lambda f: apply(rig, interp(K, f)), events=[(21, "OnFootstep", "L"), (24, "OnAttackHit", "spear_heavy")],
               notes="wind-up, lunge step, full-body thrust")
    if want("Use_Item"):
        reach = P(aR=(-15, -30, 0, -20), eR=70, wR=(10, 0, 0), fR=(10, 5), sp=(4, -4, 0), nk=(4, 0, -8), hd=(12, -6, -18))
        take = add(reach, fR=(60, 40))
        front = P(sp=(6, 0, 0), aR=(45, -15, 0, 30), eR=100, fR=(50, 35), aL=(40, -15, 0, 25), eL=100, fL=(30, 20), nk=(6, 0, 0), hd=(16, 0, 0))
        use = add(front, aL=(4, 0, 0, 4), fL=(45, 30), aR=(-4, 0, 0, 0))
        K = [(0, P(), "smooth"), (10, reach, "inout"), (14, take, "smooth"), (24, front, "inout"), (30, use, "smooth"), (40, P(), "inout")]
        B.bake("Use_Item", 40, False, lambda f: apply(rig, interp(K, f)), events=[(28, "OnUseItem", "")])
    if want("Wake_Up"):
        prop = P(fk=1.0, pel=(0, 0.47, -0.74), pelr=(-62, 0, 12), sp=(10, 0, 4), spu=(8, 0, 0), nk=(12, 0, 0), hd=(8, 0, 12),
                 aL=(-45, -10, 0, 0), eL=95, aR=(25, -30, 0, 10), eR=35, fL=(30, 20), fR=(20, 10), lL=(30, 0, 40, 20), lR=(45, 5, 70, 20))
        sit = P(fk=1.0, pel=(0, 0.25, -0.62), pelr=(-15, 0, 0), sp=(22, 0, 0), spu=(10, 0, 0), aL=(-30, -30, 0, 0), aR=(-30, -30, 0, 0), eL=10, eR=10,
                lL=(80, 5, 60, 10), lR=(85, 8, 80, 10))
        dazed = add(sit, sp=(8, 0, 0), aR=(125, 10, 0, 30), eR=125, wR=(20, 0, 0), fR=(30, 20), nk=(10, 0, 0), hd=(12, 0, -12))
        kneel = P(**kneel_R, sp=(18, 0, 0), aL=(20, -30, 0, 0), aR=(40, -20, 0, 0), eR=30, hd=(4, 0, 0))
        look_l = P(nk=(0, 0, 25), hd=(-4, 0, 30), ch=(0, 0, 10), sp=(0, 0, 4))
        look_r = P(nk=(0, 0, -25), hd=(-4, 0, -30), ch=(0, 0, -10), sp=(0, 0, -4))
        FK = [(0, lying, "smooth"), (22, lying, "smooth"), (48, prop, "inout"), (72, sit, "inout"), (84, dazed, "inout"), (98, dazed, "smooth"), (108, sit, "inout")]
        IK = [(120, kneel, "smooth"), (126, kneel, "smooth"), (146, P(), "inout"), (156, look_l, "inout"), (168, look_r, "inout"), (180, P(), "inout")]
        def wake(f):
            if f < 22: return apply(rig, add(lying, spu=(1.2 * math.sin(TAU * f / 22), 0, 0)))
            if f <= 108: return apply(rig, interp(FK, f))
            if f <= 120:     # sit -> kneel (switch to IK once the weight is over the knees)
                k = ease((f - 108) / 12.0)
                p = {kk: lerp(sit[kk], kneel[kk], k) for kk in sit}
                p["fk"] = 1.0 - ease(min(1.0, max(0.0, (k - 0.25) / 0.5)), "smooth")   # B6: FK -> IK over ~6 frames
                p["lL"] = sit["lL"]; p["lR"] = sit["lR"]
                return apply(rig, p)
            return apply(rig, interp(IK, f))
        B.bake("Wake_Up", 180, False, wake, events=[(60, "OnWakeUp", "sit"), (146, "OnWakeUp", "stand"), (180, "OnWakeUp", "done")],
               notes="opening: lying on the back -> prop on elbow -> sit, hand to head -> kneel -> stand -> look around")
    # ================================================================== phase 2: knife, dodge, climbing, fruit
    if want("Knife_Attack"):
        ready = P(pel=(0, 0.02, -0.06), pelr=(4, 0, -15), sp=(6, 0, -5), ch=(0, 0, -8), aR=(40, -10, 0, 20), eR=90, wR=(-10, 0, 0), fR=(70, 50),
                  aL=(35, -25, 0, 20), eL=70, fL=(40, 30), ftL=(0.02, -0.12, 0, 0, 15, 0), ftR=(-0.02, 0.12, 0, 0, -25, 0))
        wind = add(ready, pelr=(0, 0, -15), ch=(-4, 0, -15), aR=(-20, 50, 0, -40), eR=25, wR=(-25, 0, 0), aL=(10, 0, 0, 10))
        slash = add(ready, pel=(0, -0.06, -0.03), pelr=(6, 0, 25), sp=(6, 0, 10), ch=(4, 0, 22), aR=(35, -5, 0, 70), eR=-60, wR=(25, 0, 0), aL=(-10, 0, 0, -10), ftR=(0, 0.0, 0, 20, 0, 20))
        follow = add(slash, aR=(-4, 0, 0, 12), ch=(0, 0, 4))
        K = [(0, ready, "smooth"), (7, wind, "inout"), (10, slash, "in"), (13, follow, "out"), (26, ready, "inout")]
        B.bake("Knife_Attack", 26, False, lambda f: apply(rig, interp(K, f)), events=[(10, "OnAttackHit", "knife_slash")],
               notes="anticipation (cock back), fast slash across, follow-through, recovery")
    if want("Dodge"):
        crouch = P(pel=(0, 0.02, -0.12), pelr=(10, 0, 0), sp=(8, 0, 0), aL=(20, -30, 0, 0), aR=(20, -30, 0, 0), eL=40, eR=40)
        air = P(pel=(0, 0.06, 0.02), pelr=(-8, 0, 0), sp=(-6, 0, 0), ch=(-4, 0, 0), aL=(35, -10, 0, 0), aR=(35, -10, 0, 0), eL=50, eR=50,
                ftL=(0, -0.1, 0.12, -10, 0, 0), ftR=(0, 0.05, 0.08, -5, 0, 0))
        land = P(pel=(0, 0.04, -0.16), pelr=(12, 0, 0), sp=(10, 0, 0), aL=(25, -25, 0, 0), aR=(25, -25, 0, 0), eL=40, eR=40,
                 ftL=(0, -0.08, 0, 0, 0, 0), ftR=(0, 0.1, 0, 15, 0, 15))
        K = [(0, P(), "smooth"), (4, crouch, "out"), (8, air, "inout"), (13, land, "in"), (22, P(), "inout")]
        B.bake("Dodge", 22, False, lambda f: apply(rig, interp(K, f)), events=[(4, "OnDodge", "start"), (13, "OnFootstep", "L")],
               notes="short hop back: crouch, push, air, land; the motor moves the body (burst)")
    # ---- climbing (facing the trunk, the body is moved along the trunk by code; legs in FK, no ground snap)
    def grip_pose(t=0.0, reachR=0.0, pullL=0.0, legL=0.0):
        """t: breathing phase; reachR / pullL / legL in -1..1 move one arm up and the other down, one leg up"""
        return P(fk=1.0, pel=(0, 0.02, 0.0), pelr=(6, 0, 0), sp=(8, 0, 0), spu=(4 + 1.0 * math.sin(TAU * t), 0, 0), ch=(2, 0, 0), nk=(-6, 0, 0), hd=(-14, 0, 0),
                 aR=(116 + 20 * reachR, 4, 0, 52 - 10 * reachR), eR=88 - 34 * reachR, wR=(24, 0, 0), fR=(80, 60),
                 aL=(116 + 20 * pullL, 4, 0, 52 - 10 * pullL), eL=88 - 34 * pullL, wL=(24, 0, 0), fL=(80, 60),
                 cR=(5 + 5 * reachR, 6), cL=(5 + 5 * pullL, 6),
                 lL=(70 + 20 * legL, 26, 100 + 16 * legL, 22), lR=(70 - 20 * legL, 26, 100 - 16 * legL, 22))
    def no_ground(fn): fn.ground = False; return fn
    if want("Climb_Idle"):
        B.bake("Climb_Idle", 60, True, no_ground(lambda f: apply(rig, grip_pose(f / 60.0))), notes="hanging on the trunk, breathing")
    def climb_cycle(f, N, direction):
        t = (f / N) if direction > 0 else 1.0 - f / N
        s = math.sin(TAU * t)
        p = grip_pose(t, reachR=s, pullL=-s, legL=-s)
        p["pel"] = (0.0, 0.02, 0.03 * math.cos(2 * TAU * t)); p["pelr"] = (6, 2.5 * s, 3 * s); p["hd"] = (-14, 0, -4 * s)
        return apply(rig, p)
    if want("Climb_Up"):
        B.bake("Climb_Up", 36, True, no_ground(lambda f: climb_cycle(f, 36, 1)), speed=0.6,
               events=[(9, "OnClimbStep", "R"), (27, "OnClimbStep", "L")], notes="in place; the body moves up at speed m/s")
    if want("Climb_Down"):
        B.bake("Climb_Down", 36, True, no_ground(lambda f: climb_cycle(f, 36, -1)), speed=-0.5,
               events=[(9, "OnClimbStep", "L"), (27, "OnClimbStep", "R")], notes="in place; the body moves down at speed m/s")
    if want("Climb_Start"):
        reach = P(pel=(0, -0.02, -0.14), pelr=(8, 0, 0), sp=(6, 0, 0), hd=(-18, 0, 0), aR=(150, 10, 0, 15), eR=35, aL=(140, 10, 0, 15), eL=45, fR=(40, 30), fL=(40, 30))
        def start(f):
            if f <= 12: return apply(rig, interp([(0, P(), "smooth"), (12, reach, "inout")], f))
            k = ease((f - 12) / 12.0, "inout"); g = grip_pose(0.0)
            p = {kk: lerp(reach[kk], g[kk], k) for kk in g}; p["fk"] = ease(min(1.0, max(0.0, (k - 0.1) / 0.45)), "smooth")
            p["lL"] = g["lL"]; p["lR"] = g["lR"]                                   # B6: IK -> FK blended
            return apply(rig, p)
        start.ground = False
        B.bake("Climb_Start", 24, False, start, events=[(12, "OnClimbGrab", "")], notes="reach up, jump onto the trunk (code lifts the body)")
    if want("Climb_End"):
        crouch = P(pel=(0, 0.04, -0.2), pelr=(14, 0, 0), sp=(12, 0, 0), aL=(25, -25, 0, 0), aR=(25, -25, 0, 0), eL=45, eR=45, ftL=(0, -0.05, 0, 0, 0, 0), ftR=(0, 0.08, 0, 10, 0, 10))
        def end(f):
            if f <= 10:
                k = ease(f / 10.0); g = grip_pose(0.0)
                drop = dict(g); drop["aR"] = (100, 0, 0, 10); drop["aL"] = (100, 0, 0, 10); drop["eR"] = 40; drop["eL"] = 40; drop["lL"] = (30, 10, 40, 10); drop["lR"] = (30, 10, 40, 10)
                return apply(rig, {kk: lerp(g[kk], drop[kk], k) for kk in g})
            c10 = dict(crouch); c10["aL"] = (100, 0, 0, 10); c10["aR"] = (100, 0, 0, 10); c10["eL"] = 40; c10["eR"] = 40
            p = dict(interp([(10, c10, "smooth"), (15, crouch, "smooth"), (18, crouch, "smooth"), (28, P(), "inout")], f))
            if f < 15:                                                           # B6: FK -> IK over 5 frames
                g = grip_pose(0.0)
                p["fk"] = 1.0 - ease((f - 10) / 5.0, "smooth"); p["lL"] = (30, 10, 40, 10); p["lR"] = (30, 10, 40, 10)
            return apply(rig, p)
        end.ground = False
        B.bake("Climb_End", 28, False, end, events=[(10, "OnLand", "")], notes="let go, drop and land (code lowers the body to the ground)")
    if want("Harvest_Fruit"):
        def harvest(f, N=44):
            g = grip_pose(f / N)
            reach = dict(g); reach["aR"] = (125, 35, 0, 5); reach["eR"] = 18; reach["wR"] = (-10, 0, 0); reach["fR"] = (10, 10); reach["hd"] = (-24, 0, -20); reach["nk"] = (-8, 0, -15)
            grab = dict(reach); grab["fR"] = (70, 50)
            back = dict(g); back["aR"] = (70, -10, 0, 30); back["eR"] = 115; back["fR"] = (70, 50); back["hd"] = (-6, 0, 0)
            K = [(0, g, "smooth"), (14, reach, "inout"), (20, grab, "smooth"), (32, back, "inout"), (44, g, "inout")]
            return apply(rig, interp(K, f))
        harvest.ground = False
        B.bake("Harvest_Fruit", 44, False, harvest, events=[(22, "OnHarvest", "fruit")], notes="one hand holds the trunk, the other reaches out, picks, brings it in")
    # ================================================================== upgrade: sword set, two-handed spear, bow in the left hand
    B.weapon_report = W.bake(rig, B, which)
    # ================================================================== CHAR phase (2026-09-30)
    B.char_report = C.build(rig, B, which)
    return B
