"""
PRIMAL FRONTIER - PC phase dinosaur clips (DINO agent, 2026-09-30). Original procedural motion, built on the rig layer of
pf_dino_anim (rest-relative bone deltas, 2-bone leg IK with planted feet, in-place clips; the AI moves the object).

New / reworked clips per land species (directive 43-45):
  all      Breathe, Stop, Turn_Left, Turn_Right (reworked: stepping in place, feet counter-rotate at the authored turn
           rate, body bends into the turn, tail counterbalances), Eat (reworked: mouth reaches the ground; graze for
           plant eaters, bite-pull-shake for hunters), Drink (reworked: mouth at the water line, laps, swallow),
           Rest_Down, Rest_Loop, Rest_Shift, Rest_Up (lie down, sleep loop, fidget while lying, get up), Call
  hunters  Chase (low, stretched run at run speed), Bite (fast snap), Recover (after an attack)
  grazers  Flee (head-up panic run at run speed), Defend (species specific threat loop), Charge (reworked: head low)
Kept exactly as pf_dino_anim makes them: Idle, Idle_Variation, Walk, Run, Look, Alert, Roar, Attack, Heavy_Attack, Hurt, Death.

Weight per species (0 small .. 1 giant, same numbers as the AI's WildlifePlan) drives durations, lurch / settle, sway,
tail counterbalance, step timing and how long lying down / getting up takes.
"""
import bpy, math
from mathutils import Vector, Quaternion
import pf_dino_anim as A
from pf_dino_anim import pose_clip, sstep, qa, X, Y, Z, gait_offsets, new_action, FPS

WEIGHT = dict(triceratops=0.85, parasaurolophus=0.62, ankylosaurus=0.9, velociraptor=0.08, carnotaurus=0.55, spinosaurus=0.82, apex=1.0)
# in-place turn rate the Turn clips are authored for (deg/s) = DinosaurDefinition turn x AI pivot factor
TURN_RATE = dict(triceratops=27, parasaurolophus=49, ankylosaurus=22, velociraptor=240, carnotaurus=52, spinosaurus=27, apex=22)
HERBIVORES = ("triceratops", "parasaurolophus", "ankylosaurus")


def lerp(a, b, t): return a + (b - a) * t
def win(t, a, b): return sstep((t - a) / (b - a)) if b > a else (1.0 if t >= a else 0.0)
def bell(t, a, b):
    if t <= a or t >= b: return 0.0
    return math.sin(math.pi * (t - a) / (b - a)) ** 2
def pulse(t, a, b):
    """smooth 0 -> 1 -> 0 over [a, b] with a flat top"""
    return win(t, a, a + (b - a) * 0.3) * (1 - win(t, b - (b - a) * 0.3, b))


# ------------------------------------------------------------------ forward kinematics helpers
def complete(r):
    """untouched bones follow their parent (what Rig.write does) so FK can be measured mid-pose"""
    for n in r.order:
        if n not in r.D.touched:
            p = r.parent[n]; dict.__setitem__(r.D, n, r.D[p] if p else Quaternion())

def tip(r, n):
    complete(r); c = {}
    return r.posed_head(n, c) + r.D[n] @ (r.tail0[n] - r.head0[n])

def head_at(r, n):
    complete(r); return r.posed_head(n, {})

def mouth_z(r):
    z = tip(r, "Head").z
    if "Jaw" in r.names: z = min(z, tip(r, "Jaw").z)
    return z

def grounded(r, ch): return r.tail0[ch[2]].z < r.L * 0.15

def leg_len(r, ch): return sum((r.tail0[b] - r.head0[b]).length for b in ch[:3])

def hip_h(r):
    th = [ch[0] for g, s, ch in r.legs if g == "hind"]
    return r.head0[th[0]].z if th else r.head0["Pelvis"].z


def plant(r, tweak=None, arm_flex=(4.0, 10.0)):
    """grounded feet by IK (tweak(grp, side, ch, base) -> (target, toe_lift, meta_pitch)); small arms follow the chest"""
    for grp, side, ch in r.legs:
        if not grounded(r, ch):
            r.follow(ch[0], qa(X, arm_flex[0])); r.follow(ch[1], qa(X, arm_flex[1])); r.follow(ch[2], qa(X, 6)); r.follow(ch[3], qa(X, 8))
            continue
        base = r.tail0[ch[2]].copy(); base.z = max(0.0, base.z)
        lift, mp = 0.0, 0.0
        if tweak: base, lift, mp = tweak(grp, side, ch, base)
        r.leg_ik(ch, base, lift, mp, {})


def tail_curve(r, yaw_each=None, pitch_each=None, wave=0.0, phase=0.0, lag=0.12):
    n = max(1, len(r.tail))
    for i, tb in enumerate(r.tail):
        k = (i + 1) / n
        y = (yaw_each[i] if yaw_each else 0.0) + wave * k * math.sin(2 * math.pi * (phase - i * lag))
        p = pitch_each[i] if pitch_each else 0.0
        r.follow(tb, qa(Z, y) @ qa(X, p))


def solve_scalar(r, apply, target, hi=1.6, it=18):
    """largest-effect scalar s in [0, hi] so mouth_z == target (mouth_z falls as s grows)"""
    def z(s): r.reset(); apply(r, s); return mouth_z(r)
    if z(hi) > target: return hi, z(hi)
    lo = 0.0
    for _ in range(it):
        mid = (lo + hi) / 2
        if z(mid) > target: lo = mid
        else: hi = mid
    s = (lo + hi) / 2
    return s, z(s)


# ------------------------------------------------------------------ species context
class Ctx:
    def __init__(self, r, sp, sid):
        self.r, self.sp, self.sid = r, sp, sid
        self.m = WEIGHT.get(sid, 0.5)
        self.pred = sp["head"].get("teeth", 0) > 0
        self.quad = r.quad
        self.H = hip_h(r)
        self.g = sp["gait"]; self.speeds = sp["speeds"]
        self.belly = sp.get("_belly", self.H * 0.55)
        self.hind = [(g, s, ch) for g, s, ch in r.legs if g == "hind"]
        self.front = [(g, s, ch) for g, s, ch in r.legs if g == "front" and grounded(r, ch)]
        self.log = []

    def dur(self, small, big): return lerp(small, big, self.m)
    def frames(self, small_s, big_s): return max(8, int(round(self.dur(small_s, big_s) * FPS)))


# ------------------------------------------------------------------ reaching the ground (eat / drink / lie)
REACH = {   # max pelvis pitch, spine pitch, neck pitch, head pitch, pelvis drop (x hip height) for the ground reach search
    "triceratops": (9, 8, 34, 30, 0.10), "ankylosaurus": (7, 6, 30, 26, 0.08), "parasaurolophus": (24, 10, 46, 22, 0.16),
    "velociraptor": (26, 10, 52, 34, 0.26), "carnotaurus": (30, 12, 40, 26, 0.18), "spinosaurus": (28, 12, 38, 24, 0.16),
    "apex": (30, 12, 36, 24, 0.16),
}

def head_dir(r):
    return (tip(r, "Head") - head_at(r, "Head")).normalized()

def mouth_yz(r):
    a = tip(r, "Head"); z = a.z; y = a.y
    if "Jaw" in r.names:
        b = tip(r, "Jaw"); z = min(z, b.z)
    return y, z

def reach_limit_y(c):
    """the mouth must stay ahead of the front feet (quadrupeds) / well ahead of the hind feet (bipeds)"""
    r = c.r
    if c.front: return min(r.tail0[ch[2]].y for g, s, ch in c.front) - 0.12 * c.H
    return min(r.tail0[ch[2]].y for g, s, ch in c.hind) - 0.55 * c.H

def neck_boom(r, pitch, yaw=0.0, head_pitch=0.0, head_yaw=0.0, roll=0.0, base=0.55):
    """neck bent mostly at its base (a boom lowering the head in front of the chest), head pitched on top"""
    n = len(r.neck); w = [base ** i for i in range(n)]; sw = sum(w) or 1.0
    for i, nb in enumerate(r.neck): r.follow(nb, qa(Z, yaw / max(1, n)) @ qa(X, pitch * w[i] / sw))
    if "Head" in r.names: r.follow("Head", qa(Z, head_yaw) @ qa(X, head_pitch) @ qa(Y, roll))


def reach_solve(c, target_z, yaw=0.0, head_yaw=0.0, tag=""):
    """3-parameter search (body b, neck n, head h) that puts the mouth at target_z ahead of the feet, snout pointing down
    and forward, with the least effort; returns f(r, s) applying the solution scaled by s (s = 1 reaches)"""
    r = c.r
    pp, spn, nk, hd, dr = REACH.get(c.sid, (20, 10, 30, 20, 0.08))
    ylim = reach_limit_y(c)
    wb = 0.04 if (c.quad and c.sid != "parasaurolophus") else (0.12 if c.quad else 0.3)
    def app(r_, b, n, h, s=1.0, yaw_=0.0, hyaw=0.0, off=None, roll=0.0, spine_yaw=0.0, neck=0.0, head=0.0, head_roll=0.0):
        r_.pelvis(Vector((0, 0, -dr * c.H * b * min(1.0, s))) + (off or Vector()), pitch=pp * b * s, roll=roll)
        r_.spine_curve(pitch=spn * b * s, yaw=spine_yaw)
        neck_boom(r_, nk * n * s + neck, yaw_, hd * h * s + head, hyaw, head_roll)
    best = None
    for bi in range(0, 11):
        b = bi / 10
        for ni in range(0, 15):
            n = ni / 10
            for hi in range(-8, 5):
                h = hi / 4
                r.reset(); app(r, b, n, h); y, z = mouth_yz(r); hdv = head_dir(r)
                e = (100 * (z - target_z) ** 2 + 30 * max(0.0, y - ylim) ** 2 + wb * b * b + 0.03 * n * n + 0.02 * h * h
                     + 4 * (hdv.z + 0.7) ** 2 + 20 * max(0.0, hdv.y + 0.2) ** 2)
                if best is None or e < best[0]: best = (e, b, n, h, y, z, hdv.z)
    e, b, n, h, y, z, hz = best
    c.log.append(f"reach{tag}: body {b:.2f} neck {n:.2f} head {h:.2f} mouth y {y:.2f} (limit {ylim:.2f}) z {z:.3f} (target {target_z:.2f}) snout dz {hz:.2f}")
    def f(r_, s, yaw_=yaw, hyaw=head_yaw, **kw):
        app(r_, b, n, h, s, yaw_, hyaw, **kw)
    return f


def lie_body(c, r, sh, sf, lean=0.0):
    """pelvis drop / pitch for lying: sh = hind fold 0..1, sf = front fold 0..1 (quadrupeds); belly on the ground at 1"""
    drop = max(0.0, c.belly - 0.03)
    if c.quad and c.front:
        dy = max(0.5, r.head0["Pelvis"].y - r.head0[c.front[0][2][0]].y)
        dh, df = drop * sh, drop * sf
        pitch = math.degrees(math.atan2(df - dh, dy))
        # nose up (hind folded first): the rump behind the hips would sink, so the hips stay higher
        lift = 0.55 * dy * math.sin(math.radians(-pitch)) if pitch < 0 else 0.0
        r.pelvis(Vector((0, 0, -dh + lift)), pitch=pitch)
        r.spine_curve(pitch=0.0)
    else:
        s = sh
        r.pelvis(Vector((0, -0.04 * c.H * s, -drop * s)), pitch=6 * s + lean)
        r.spine_curve(pitch=2 * s)


def lie_legs(c, sh, sf):
    """feet folded under the body; digitigrade metatarsi flat on the ground"""
    r = c.r
    def tw(grp, side, ch, base):
        sx = 1 if side == "L" else -1
        L = leg_len(r, ch)
        if grp == "hind":
            s = sh
            tgt = Vector((base.x + sx * 0.12 * L * s, base.y - 0.34 * L * s, 0.0))
            meta = -72 * s if not c.quad else -30 * s
            return tgt, 18 * s, meta
        s = sf
        tgt = Vector((base.x + sx * 0.06 * L * s, base.y + 0.3 * L * s, 0.0 + 0.02 * s))
        return tgt, 0.0, 70 * s
    plant(c.r, tw, arm_flex=(lerp(4, 25, sh), lerp(10, 40, sh)))


LIE_HEAD_K = 0.3

def prepare_lie(c):
    """solve (once) the neck for the full lying pose (chin on the ground, snout forward) and the tail on the ground"""
    r = c.r
    pp, spn, nk, hd, dr = REACH.get(c.sid, (20, 10, 30, 20, 0.08))
    target = 0.05 + 0.02 * c.H
    def pose(r_, n, h):
        lie_body(c, r_, 1.0, 1.0)
        neck_boom(r_, nk * n, 10, hd * h, 8)
    best = None
    for i in range(-8, 15):
        n = i / 10
        for j in range(-8, 5):
            h = j / 4
            r.reset(); pose(r, n, h); y, z = mouth_yz(r); hdv = head_dir(r)
            e = 100 * (z - target) ** 2 + 20 * max(0.0, hdv.y + 0.3) ** 2 + 6 * (hdv.z + 0.3) ** 2 + 0.02 * n * n + 0.02 * h * h
            if best is None or e < best[0]: best = (e, n, h, z)
    _, n, hh, z = best
    r.reset(); pose(r, n, hh)
    ntail = len(r.tail); pitches = []; yaws = []
    h = max(0.04, 0.035 * c.H)
    for i, tb in enumerate(r.tail):
        yaw = (8 + 10 * (i / max(1, ntail - 1)))
        bst = (1e9, 0.0)
        for p in range(-32, 33, 2):
            r.follow(tb, qa(Z, yaw) @ qa(X, p)); tp_ = tip(r, tb); zt = tp_.z
            dv = (tp_ - head_at(r, tb)).normalized()
            e = abs(zt - h) + (0.5 if zt < 0 else 0.0) + 2.0 * max(0.0, 0.3 - dv.y) * (r.tail0[tb] - r.head0[tb]).length
            if e < bst[0]: bst = (e, p)
        r.follow(tb, qa(Z, yaw) @ qa(X, bst[1])); pitches.append(bst[1]); yaws.append(yaw)
    c.lie = dict(neck_s=n, head_s=hh, mouth=z, tail_p=pitches, tail_y=yaws)
    c.log.append(f"lie: neck {n:.2f} head {hh:.2f} mouth z {z:.3f} tail pitches {pitches}")
    return c.lie


def lie_pose(c, r, sh, sf, sn, st, t_breath=0.0, breath_k=1.0, head_lift=0.0, look=0.0, lean=0.0):
    L = c.lie
    b = math.sin(2 * math.pi * t_breath)
    lie_body(c, r, sh, sf, lean)
    # breathing: belly / chest rise (pelvis stays on the ground, the chest heaves)
    r.off = r.off + Vector((0, 0, 0.004 * c.H * breath_k * (1 + b) * 0.5 * sh))
    r.spine_curve(pitch=-0.8 * b * breath_k)
    pp, spn, nk, hd, dr = REACH.get(c.sid, (20, 10, 30, 20, 0.08))
    k = sn * (1 - head_lift)
    neck_boom(r, nk * L["neck_s"] * k - 10 * head_lift, 10 * sn + look * 30, hd * L["head_s"] * k - 4 * head_lift, 8 * sn + look * 14)
    r.jaw(0)
    tail_curve(r, [y * st for y in L["tail_y"]], [p * st for p in L["tail_p"]], wave=1.2 * (1 - st * 0.7), phase=t_breath)
    lie_legs(c, sh, sf)


# ------------------------------------------------------------------ locomotion with posture + weight
def locomotion2(c, name, speed, cycle, duty, lift_k, run=True, post=None, events=True):
    r = c.r; arm = r.arm; new_action(arm, name)
    post = post or {}
    frames = max(8, int(round(cycle * FPS)))
    stride = speed * frames / FPS
    lift = lift_k * (1.3 if run else 1.0)
    offs = gait_offsets(r, run)
    m = c.m
    bob = (0.03 if run else 0.012) * r.L * (0.6 if r.quad else 1.0) * lerp(0.75, 1.3, m)
    r.clip = name
    for f in range(frames + 1):
        ph = f / frames; r.t = ph; r.reset()
        # impact: heavy bodies sink after each footfall (2 per cycle), small ones stay springy
        dip = bob * 0.5 * (1 + math.cos(4 * math.pi * ph)) * (0.7 if r.quad else 1.0)
        imp = m * 0.012 * r.L * max(0.0, math.sin(4 * math.pi * ph - 0.4)) ** 3 * (0.6 if r.quad else 1.0)
        r.pelvis(Vector((0, 0, -dip - imp + post.get("rise", 0.0) * c.H)),
                 pitch=post.get("pitch", -3) * (1 if not r.quad else 0.4),
                 yaw=post.get("yaw_k", 1.0) * lerp(3.0, 2.0, m) * math.sin(2 * math.pi * ph),
                 roll=post.get("roll_k", 1.0) * (2.2 if not r.quad else 1.0) * lerp(0.7, 1.4, m) * math.sin(2 * math.pi * ph))
        r.spine_curve(pitch=1.0 * math.sin(4 * math.pi * ph) + post.get("spine", 0.0), yaw=-3.0 * math.sin(2 * math.pi * ph))
        # small animals stabilise the head, big ones let it ride with the body
        stab = lerp(0.8, 0.2, m)
        r.neck_head(pitch=post.get("neck", 0.0) + (4 * (1 - stab)) * math.sin(4 * math.pi * ph + 0.6),
                    yaw=2.0 * math.sin(2 * math.pi * ph + 1.0) * (1 - stab * 0.5),
                    head_pitch=post.get("head", 0.0) - 1.5 * math.sin(4 * math.pi * ph + 0.9) + stab * 3 * math.sin(4 * math.pi * ph))
        r.jaw(post.get("jaw", 3.0) + post.get("pant", 0.0) * max(0.0, math.sin(4 * math.pi * ph)))
        r.tail_wave(post.get("tail_amp", 7.0) * lerp(0.8, 1.3, m), ph + 0.1 + 0.08 * m, 0.1 + 0.03 * m, pitch=post.get("tail_pitch", 4.0), pitch_amp=1.5 + 1.5 * m)
        for grp, side, ch in r.legs:
            p = (ph + offs[(grp, side)]) % 1.0
            tgt, toe = A.foot_target(r, ch, p, duty, stride, lift)
            if not grounded(r, ch):
                sw = post.get("arm_swing", 6.0)
                r.follow(ch[0], qa(X, post.get("arm_tuck", 0.0) + sw * math.sin(2 * math.pi * p))); r.follow(ch[1], qa(X, 8 + post.get("arm_tuck", 0.0) + 4 * math.sin(2 * math.pi * p)))
                r.follow(ch[2], Quaternion()); r.follow(ch[3], qa(X, 10)); continue
            r.leg_ik(ch, tgt, toe, -10 * math.sin(math.pi * max(0, (p - duty) / (1 - duty))) if p > duty else 4 * (p / duty - 0.5), {})
        r.write(f + 1)
    r.arm.animation_data.action.frame_range = (1, frames + 1)
    m_ = dict(name=name, frames=frames, loop=True, speed=round(speed, 3), move="forward")
    if events: m_["events"] = [dict(frame=1, function="OnFootstep", param="L"), dict(frame=frames // 2, function="OnFootstep", param="R")]
    return m_


# ------------------------------------------------------------------ clips
def clip_turn(c, name, side):
    """stepping in place; planted feet counter-rotate about the object origin at the authored turn rate, so the feet stay
    put in the world while the AI turns the object"""
    r = c.r; g = c.g; m = c.m
    rate = TURN_RATE.get(c.sid, 45)
    cycle = g["walk"] * lerp(0.8, 1.25, m); frames = max(12, int(round(cycle * FPS))); T = frames / FPS
    duty = g["duty_walk"]; offs = gait_offsets(r, False)
    sweep = math.radians(rate) * T                        # body turn over one cycle
    lift = g["lift"] * c.sp["length"] / (9 if r.quad else 7)
    def fn(r, t):
        bend = side * lerp(10, 7, m)
        r.pelvis(Vector((0, 0, -0.006 * r.L * (1 + math.cos(4 * math.pi * t)) * 0.5)), yaw=side * 3 + 1.5 * math.sin(2 * math.pi * t), roll=lerp(1.5, 2.5, m) * math.sin(2 * math.pi * t))
        r.spine_curve(yaw=bend + 2 * math.sin(2 * math.pi * t), pitch=0.6 * math.sin(4 * math.pi * t))
        r.neck_head(pitch=-3, yaw=side * lerp(16, 12, m) + 2 * math.sin(2 * math.pi * t + 0.8), head_yaw=side * 8, head_pitch=1.5 * math.sin(4 * math.pi * t))
        r.jaw(0)
        n = max(1, len(r.tail))
        tail_curve(r, [-side * lerp(4, 7, m) * (i + 1) / n for i in range(n)], [1.5] * n, wave=4, phase=t + 0.15)
        for grp, sd, ch in r.legs:
            if not grounded(r, ch):
                r.follow(ch[0], qa(X, 4)); r.follow(ch[1], qa(X, 10)); r.follow(ch[2], Quaternion()); r.follow(ch[3], qa(X, 8)); continue
            p = (t + offs[(grp, sd)]) % 1.0
            base = r.tail0[ch[2]].copy(); base.z = max(0.0, base.z)
            if p < duty:
                u = p / duty; ang = side * sweep * duty * (0.5 - u); z = base.z; toe = 0.0
            else:
                u = (p - duty) / (1 - duty); e = sstep(u)
                ang = side * sweep * duty * (-0.5 + e); z = base.z + lift * math.sin(math.pi * u) ** 0.9; toe = 20 * math.sin(math.pi * u)
            ca, sa = math.cos(ang), math.sin(ang)
            tgt = Vector((base.x * ca - base.y * sa, base.x * sa + base.y * ca, z))
            r.leg_ik(ch, tgt, toe, 0.0, {})
    mt = pose_clip(r, name, frames, fn, True)
    mt.update(speed=0.0, move="turn", notes=f"authored for {rate} deg/s in place")
    mt["events"] = [dict(frame=int(frames * ((1 - offs[(g_, s_)]) % 1.0)) + 1, function="OnFootstep", param=s_) for g_, s_, ch in c.hind]
    return mt


def clip_stop(c):
    """walking / running body brought to a stand: forward lurch, one last step, damped settle (long and heavy for giants)"""
    r = c.r; m = c.m; g = c.g
    frames = c.frames(0.55, 1.6); T = frames / FPS
    amp = lerp(0.07, 0.09, m) * c.H
    k = lerp(6.5, 3.2, m); fq = lerp(1.35, 1.1, m)
    def d(t):
        return math.exp(-k * t) * math.sin(2 * math.pi * fq * t) / 0.62 if t > 0 else 0.0
    reach = c.speeds["walk"] * 1.5 * g["walk"] * g["duty_walk"]
    step_t = min(0.45, lerp(0.2, 0.45, m) / T)
    lift = g["lift"] * c.sp["length"] / (8 if r.quad else 6)
    lead = {("hind", "L"): 0.0, ("front", "R"): 0.3 * step_t}
    def fn(r, t):
        x = d(t); xl = d(max(0.0, t - 0.06)); x2 = d(max(0.0, t - 0.12))
        r.pelvis(Vector((0, -amp * x, -0.35 * amp * abs(x))), pitch=lerp(3, 5, m) * x, roll=1.5 * x2)
        r.spine_curve(pitch=lerp(1.5, 3, m) * x)
        r.neck_head(pitch=-lerp(2, 5, m) * xl, head_pitch=-lerp(2, 4, m) * xl)
        r.jaw(0)
        n = max(1, len(r.tail))
        tail_curve(r, None, [lerp(5, 10, m) * x2 * (i + 1) / n for i in range(n)], wave=3 * (1 - t), phase=t)
        def tw(grp, side, ch, base):
            key = (grp, side)
            if key not in lead: return base, 0.0, 0.0
            u = (t - lead[key]) / step_t
            if u <= 0: return Vector((base.x, base.y + reach / 2, base.z)), 0.0, 0.0
            if u >= 1: return base, 0.0, 0.0
            e = sstep(u)
            return Vector((base.x, base.y + reach / 2 * (1 - e), base.z + lift * math.sin(math.pi * u))), 20 * math.sin(math.pi * u), -8 * math.sin(math.pi * u)
        plant(r, tw)
    mt = pose_clip(r, "Stop", frames, fn, False)
    ev = [dict(frame=int(frames * step_t) + 1, function="OnFootstep", param="L")]
    if c.quad: ev.append(dict(frame=int(frames * 1.3 * step_t) + 1, function="OnFootstep", param="R"))
    mt["events"] = ev
    return mt


def clip_breathe(c):
    """heavy breathing after exertion; hunters pant with the mouth open"""
    r = c.r; m = c.m
    frames = c.frames(1.0, 3.0)
    def fn(r, t):
        b = math.sin(2 * math.pi * t * 2)
        r.pelvis(Vector((0, 0, -0.01 * c.H * (1 + b) * 0.5)), pitch=1.5)
        r.spine_curve(pitch=-2.4 * b)
        r.neck_head(pitch=6 + 2 * b, head_pitch=4 - 1.5 * b, yaw=3 * math.sin(2 * math.pi * t))
        r.jaw((10 + 7 * max(0.0, b)) if c.pred else (2 + 3 * max(0.0, b)))
        r.tail_wave(2, t, 0.12, pitch=-2)
        plant(r, arm_flex=(6, 14))
    mt = pose_clip(r, "Breathe", frames, fn, True)
    mt["events"] = [dict(frame=int(frames * f) + 1, function="OnBreath", param="") for f in (0.38, 0.88)]
    return mt


def clip_eat(c):
    r = c.r; m = c.m
    frames = 150
    if c.pred:
        target = 0.22 + 0.02 * c.H                     # carcass height
        f = reach_solve(c, target, tag=" eat/tear"); s0 = 1.0
        def fn(r, t):
            bite = pulse(t, 0.06, 0.22)                 # open, bite down
            yank = pulse(t, 0.2, 0.52)                  # pull back and shake
            gulp = pulse(t, 0.58, 0.95)                 # head up, swallow
            s = s0 * (1 - 0.18 * yank - 0.55 * gulp) + 0.04 * bite
            shake = math.sin(2 * math.pi * t * 9) * yank
            gl = math.sin(2 * math.pi * (t - 0.6) * 3.4) * gulp
            f(r, s, yaw_=10 * shake, off=Vector((0, 0.03 * c.H * yank, 0)), roll=2 * shake, spine_yaw=3 * shake,
              neck=-10 * yank + 3 * gl, head=4 * yank, head_roll=8 * shake)
            r.jaw(30 * win(t, 0.02, 0.1) * (1 - win(t, 0.14, 0.2)) + 4 * yank + 6 * max(0.0, gl))
            r.tail_wave(3 + 4 * yank, t * 2, 0.1, pitch=6 * s)
            plant(r, arm_flex=(10, 20))
        ev = [dict(frame=int(frames * 0.18) + 1, function="OnEat", param="bite"), dict(frame=int(frames * 0.36) + 1, function="OnEat", param="tear"),
              dict(frame=int(frames * 0.75) + 1, function="OnEat", param="swallow")]
    else:
        target = 0.05
        f = reach_solve(c, target, tag=" eat/graze"); s0 = 1.0
        crops = (0.1, 0.3, 0.5)
        def fn(r, t):
            chew = pulse(t, 0.66, 0.96)                 # head half up, chewing, glance around
            s = s0 * (1 - 0.42 * chew)
            for cr in crops: s -= 0.05 * s0 * bell(t, cr, cr + 0.09) - 0.08 * s0 * bell(t, cr + 0.08, cr + 0.16)   # dip, crop, yank up
            sweep = math.sin(2 * math.pi * t) * (1 - chew)
            f(r, s, yaw_=12 * sweep, hyaw=6 * sweep + 10 * math.sin(2 * math.pi * t * 2) * chew)
            jaw = 0.0
            for cr in crops: jaw += 12 * bell(t, cr - 0.04, cr + 0.06)
            jaw += chew * (4 + 5 * max(0.0, math.sin(2 * math.pi * t * 10)))
            r.jaw(jaw)
            r.tail_wave(3, t, 0.12, pitch=-3)
            plant(r)
        ev = [dict(frame=int(frames * (cr + 0.06)) + 1, function="OnEat", param="crop") for cr in crops]
    mt = pose_clip(r, "Eat", frames, fn, True); mt["events"] = ev
    return mt


def clip_drink(c):
    r = c.r
    frames = 120
    target = 0.0
    f = reach_solve(c, target, tag=" drink"); s0 = 1.0
    laps = (0.08, 0.2, 0.32, 0.44)
    def fn(r, t):
        up = pulse(t, 0.56, 0.9)                        # lift to swallow (birds and crocodiles do; so do these)
        s = s0 * (1 - (0.45 if not r.quad else 0.28) * up)
        for lp in laps: s += 0.03 * s0 * bell(t, lp, lp + 0.1)
        f(r, s)
        jaw = sum(8 * bell(t, lp, lp + 0.1) for lp in laps) + 3 * up * max(0.0, math.sin(2 * math.pi * t * 6))
        r.jaw(jaw)
        r.tail_wave(2, t, 0.12, pitch=-3 if r.quad else 5 * s)
        plant(r, arm_flex=(8, 16))
    mt = pose_clip(r, "Drink", frames, fn, True)
    mt["events"] = [dict(frame=int(frames * (lp + 0.06)) + 1, function="OnDrink", param="") for lp in laps]
    return mt


def clip_rest(c):
    r = c.r; m = c.m
    prepare_lie(c)
    out = []
    breath_n = 2
    # Rest_Down: hind first (sits back), then the front, then the head; a settle bounce at contact
    fd = c.frames(1.4, 3.6)
    def down(r, t):
        if c.quad and c.front: sh = win(t, 0.0, 0.62); sf = win(t, 0.28, 0.88)
        else: sh = win(t, 0.0, 0.8); sf = sh
        bounce = lerp(0.02, 0.05, m) * math.exp(-8 * max(0.0, t - 0.86)) * math.sin(2 * math.pi * max(0.0, t - 0.86) * 5) if t > 0.86 else 0.0
        sn = win(t, 0.55, 1.0); st = win(t, 0.3, 1.0)
        lie_pose(c, r, sh, sf, sn, st, 0.0, 1.0)
        r.off = r.off + Vector((0, 0, bounce * c.H))
    mt = pose_clip(r, "Rest_Down", fd, down, False)
    mt["events"] = [dict(frame=int(fd * (0.88 if c.quad else 0.8)) + 1, function="OnBodyFall", param="rest")]
    out.append(mt)
    fl = c.frames(4.0, 7.0)
    def loop(r, t):
        lie_pose(c, r, 1.0, 1.0, 1.0, 1.0, t * breath_n, lerp(1.0, 1.6, m))
    mt = pose_clip(r, "Rest_Loop", fl, loop, True); mt["events"] = []
    out.append(mt)
    fs = c.frames(2.2, 3.6)
    def shift(r, t):
        a = pulse(t, 0.05, 0.95)
        lie_pose(c, r, 1.0, 1.0, 1.0, 1.0, 0.0, 1.0, head_lift=0.75 * a, look=math.sin(2 * math.pi * t) * a)
    mt = pose_clip(r, "Rest_Shift", fs, shift, False); mt["events"] = []
    out.append(mt)
    fu = c.frames(1.1, 3.0)
    def up(r, t):
        # front first (quadrupeds push up the chest), then the hind; bipeds rock forward and rise
        sn = 1 - win(t, 0.0, 0.35)
        if c.quad and c.front: sf = 1 - win(t, 0.08, 0.55); sh = 1 - win(t, 0.4, 0.95)
        else: sh = 1 - win(t, 0.12, 0.9); sf = sh
        st = 1 - win(t, 0.3, 0.9)
        lean = 0.0 if (c.quad and c.front) else 12 * bell(t, 0.05, 0.8)      # bipeds lean forward over the feet
        lie_pose(c, r, sh, sf, sn, st, 0.0, 1.0, lean=lean)
    mt = pose_clip(r, "Rest_Up", fu, up, False)
    mt["events"] = [dict(frame=int(fu * 0.6) + 1, function="OnFootstep", param="L"), dict(frame=int(fu * 0.85) + 1, function="OnFootstep", param="R")]
    out.append(mt)
    return out


def clip_call(c):
    r = c.r; m = c.m; sid = c.sid
    if sid == "parasaurolophus":
        frames = 120
        def fn(r, t):
            a = pulse(t, 0.05, 0.95); p1 = bell(t, 0.22, 0.5); p2 = bell(t, 0.55, 0.88)
            r.pelvis(Vector((0, 0, 0.01 * c.H * a)), pitch=-6 * a)
            r.spine_curve(pitch=-4 * a - 2 * (p1 + p2))
            r.neck_head(pitch=-26 * a - 3 * (p1 + p2), head_pitch=-26 * a, yaw=3 * math.sin(2 * math.pi * t))
            r.jaw(12 * (p1 + p2))
            r.tail_wave(3, t, 0.12, pitch=4 * a)
            plant(r)
        ev = [dict(frame=int(frames * 0.24) + 1, function="OnCall", param="1"), dict(frame=int(frames * 0.57) + 1, function="OnCall", param="2")]
    elif sid in HERBIVORES:
        frames = 105
        def fn(r, t):
            a = pulse(t, 0.05, 0.95); low = bell(t, 0.05, 0.4); b = bell(t, 0.35, 0.9)
            pump = math.sin(2 * math.pi * t * 5) * b
            r.pelvis(Vector((0, 0, -0.01 * c.H * low + 0.004 * c.H * pump)), pitch=2 * low)
            r.spine_curve(pitch=3 * low - 3 * b)
            r.neck_head(pitch=14 * low - 14 * b, head_pitch=8 * low - 16 * b, roll=1.5 * pump)
            r.jaw(22 * b)
            r.tail_wave(4, t, 0.12, pitch=5 * b)
            plant(r)
        ev = [dict(frame=int(frames * 0.42) + 1, function="OnCall", param="")]
    elif c.m < 0.3:
        frames = 45
        barks = (0.15, 0.4, 0.65)
        def fn(r, t):
            a = pulse(t, 0.02, 0.98)
            bb = sum(bell(t, x, x + 0.16) for x in barks)
            r.pelvis(Vector((0, 0, 0)), pitch=-4 * a)
            r.spine_curve(pitch=-2 * a)
            r.neck_head(pitch=-12 * a + 8 * bb, head_pitch=-10 * a - 6 * bb, yaw=6 * math.sin(2 * math.pi * t * 1.5))
            r.jaw(28 * bb)
            r.tail_wave(6, t * 2, 0.1, pitch=8 * a)
            plant(r, arm_flex=(-8 * a, 18))
        ev = [dict(frame=int(frames * (x + 0.08)) + 1, function="OnCall", param=str(i)) for i, x in enumerate(barks)]
    else:
        # big hunters: a closed-mouth rumble, the throat and chest pumping, then a short open-mouth end
        frames = 105
        def fn(r, t):
            a = pulse(t, 0.05, 0.95); rum = pulse(t, 0.12, 0.72); open_ = bell(t, 0.7, 0.95)
            pump = math.sin(2 * math.pi * t * 7) * rum
            r.pelvis(Vector((0, 0, -0.01 * c.H * a + 0.004 * c.H * pump)), pitch=2 * a)
            r.spine_curve(pitch=-2 * a + 1.2 * pump)
            r.neck_head(pitch=8 * a - 12 * open_ + 1.5 * pump, head_pitch=6 * a - 10 * open_, roll=1 * pump)
            r.jaw(3 * rum + 26 * open_)
            r.tail_wave(3, t, 0.12, pitch=3 * a)
            plant(r)
        ev = [dict(frame=int(frames * 0.14) + 1, function="OnCall", param="rumble")]
    mt = pose_clip(r, "Call", frames, fn, False); mt["events"] = ev
    return mt


def clip_defend(c):
    r = c.r; sid = c.sid; m = c.m
    if sid == "triceratops":
        frames = 90
        def fn(r, t):
            hook = math.sin(2 * math.pi * t)
            r.pelvis(Vector((0, 0.02 * c.H, -0.04 * c.H)), pitch=4, yaw=2 * hook)
            r.spine_curve(pitch=3, yaw=-2 * hook)
            r.neck_head(pitch=18 + 3 * math.sin(4 * math.pi * t), yaw=10 * hook, head_pitch=16, head_yaw=6 * hook, roll=6 * hook)
            r.jaw(3 * max(0.0, math.sin(4 * math.pi * t)))
            r.tail_wave(5, t, 0.1, pitch=6)
            def tw(grp, side, ch, base):
                if grp == "front" and side == "L":       # pawing / stamping
                    u = bell(t, 0.1, 0.4)
                    return Vector((base.x, base.y + 0.12 * u * c.H, base.z + 0.18 * c.H * u)), 10 * u, 12 * u
                return base, 0.0, 0.0
            plant(r, tw)
        ev = [dict(frame=int(frames * 0.42) + 1, function="OnFootstep", param="F"), dict(frame=int(frames * 0.7) + 1, function="OnBreath", param="snort")]
    elif sid == "ankylosaurus":
        frames = 120
        def fn(r, t):
            w = math.sin(2 * math.pi * t)
            r.pelvis(Vector((0, 0, -0.05 * c.H)), yaw=-10 + 4 * w, pitch=2)
            r.spine_curve(yaw=-10 + 3 * w)
            r.neck_head(pitch=10, yaw=18 - 4 * w, head_pitch=8)
            r.jaw(0)
            n = max(1, len(r.tail))
            tail_curve(r, [(14 + 22 * w) * (i + 1) / n for i in range(n)], [6 + 4 * (i + 1) / n for i in range(n)], wave=0, phase=t)
            plant(r)
        ev = [dict(frame=int(frames * 0.25) + 1, function="OnBreath", param="snort")]
    else:   # parasaurolophus and any other grazer: rear up, stamp down, head toss
        frames = 90
        def fn(r, t):
            rear = pulse(t, 0.08, 0.55); slam = bell(t, 0.52, 0.66)
            r.pelvis(Vector((0, 0.03 * c.H * rear, 0.02 * c.H * rear - 0.03 * c.H * slam)), pitch=-14 * rear + 3 * slam)
            r.spine_curve(pitch=-6 * rear)
            r.neck_head(pitch=-10 * rear + 10 * slam, head_pitch=-12 * rear + 6 * slam, yaw=8 * math.sin(2 * math.pi * t * 2) * (1 - rear))
            r.jaw(14 * rear)
            r.tail_wave(8, t * 2, 0.1, pitch=-4 * rear)
            def tw(grp, side, ch, base):
                if grp == "front":
                    lift = 0.35 * leg_len(r, ch) * rear
                    return Vector((base.x, base.y + 0.1 * lift, base.z + lift)), 0.0, 30 * rear
                return base, 0.0, 0.0
            plant(r, tw)
        ev = [dict(frame=int(frames * 0.6) + 1, function="OnFootstep", param="F"), dict(frame=int(frames * 0.2) + 1, function="OnCall", param="")]
    mt = pose_clip(r, "Defend", frames, fn, True); mt["events"] = ev
    return mt


def clip_bite(c):
    r = c.r; m = c.m
    frames = c.frames(0.45, 0.85)
    def fn(r, t):
        wind = win(t, 0.0, 0.3) * (1 - win(t, 0.3, 0.42)); snap = win(t, 0.3, 0.42) * (1 - win(t, 0.5, 1.0))
        r.pelvis(Vector((0, 0.02 * c.H * wind - 0.05 * c.H * snap, -0.01 * c.H * (wind + snap))), pitch=2 * wind - 5 * snap)
        r.spine_curve(pitch=2 * wind - 5 * snap)
        r.neck_head(pitch=-10 * wind + 12 * snap, head_pitch=-8 * wind + 6 * snap, yaw=3 * snap)
        r.jaw(36 * win(t, 0.05, 0.3) * (1 - win(t, 0.36, 0.44)) + 3 * snap)
        r.tail_wave(3, t, 0.1, pitch=5 * wind)
        plant(r, arm_flex=(-12 * snap, 20))
    mt = pose_clip(r, "Bite", frames, fn, False)
    mt["events"] = [dict(frame=int(frames * 0.42) + 1, function="OnBite", param="bite")]
    return mt


def clip_recover(c):
    r = c.r; m = c.m
    frames = c.frames(0.8, 1.6)
    def fn(r, t):
        back = bell(t, 0.0, 0.6); sh = math.exp(-4 * t) * math.sin(2 * math.pi * t * 4) * win(t, 0.05, 0.15)
        r.pelvis(Vector((0, 0.04 * c.H * back, -0.015 * c.H * back)), pitch=-3 * back, roll=2 * sh)
        r.spine_curve(pitch=-2 * back, yaw=3 * sh)
        r.neck_head(pitch=-8 * back, yaw=14 * sh, head_pitch=-4 * back, roll=10 * sh)
        r.jaw(8 * back)
        r.tail_wave(6 * (1 - t), t * 2, 0.1, pitch=4 * back)
        def tw(grp, side, ch, base):
            if grp == "hind" and side == "R":              # one foot steps back and plants
                u = win(t, 0.08, 0.4)
                return Vector((base.x, base.y + 0.18 * c.H * u, base.z + 0.12 * c.H * bell(t, 0.08, 0.4))), 15 * bell(t, 0.08, 0.4), 0.0
            return base, 0.0, 0.0
        if t > 0.55:                                        # ...and returns
            def tw(grp, side, ch, base, t=t):
                if grp == "hind" and side == "R":
                    u = 1 - win(t, 0.6, 0.9)
                    return Vector((base.x, base.y + 0.18 * c.H * u, base.z + 0.12 * c.H * bell(t, 0.6, 0.9))), 15 * bell(t, 0.6, 0.9), 0.0
                return base, 0.0, 0.0
        plant(r, tw)
    mt = pose_clip(r, "Recover", frames, fn, False)
    mt["events"] = [dict(frame=int(frames * 0.4) + 1, function="OnFootstep", param="R"), dict(frame=int(frames * 0.9) + 1, function="OnFootstep", param="R")]
    return mt


# ------------------------------------------------------------------ eyelids / tongue (Rift Tyrant) for the new clip names
PROXY = {"Chase": "Charge", "Bite": "Attack", "Call": "Roar", "Recover": "Walk", "Breathe": "Idle", "Stop": "Walk", "Flee": "Walk", "Defend": "Alert"}

def pc_face_extra(r):
    if "Eyelid_Upper_L" not in r.names and "Tongue_01" not in r.names: return
    name, t = getattr(r, "clip", ""), getattr(r, "t", 0.0)
    if name.startswith("Rest_"):
        close = {"Rest_Loop": 0.95, "Rest_Down": 0.95 * win(t, 0.7, 1.0), "Rest_Up": 0.95 * (1 - win(t, 0.0, 0.25)),
                 "Rest_Shift": 0.95 * (1 - pulse(t, 0.05, 0.95))}.get(name, 0.0)
        hd = (r.tail0["Head"] - r.head0["Head"]).normalized()
        for side, s in (("L", 1), ("R", -1)):
            if f"Eyelid_Upper_{side}" in r.names: r.follow(f"Eyelid_Upper_{side}", Quaternion(hd, math.radians(-s * 68 * close)))
            if f"Eyelid_Lower_{side}" in r.names: r.follow(f"Eyelid_Lower_{side}", Quaternion(hd, math.radians(s * 22 * close)))
        return
    if name in PROXY:
        r.clip = PROXY[name]
        try: A.face_extra(r)
        finally: r.clip = name
    else: A.face_extra(r)


# ------------------------------------------------------------------ entry
def build_pc_clips(arm, sp, sid):
    """returns (metas, log); metas replace / extend what pf_dino_anim.build_clips made"""
    r = A.Rig(arm, sp); r.extra = pc_face_extra
    c = Ctx(r, sp, sid)
    g = sp["gait"]; spd = sp["speeds"]
    metas = []
    metas.append(clip_turn(c, "Turn_Left", 1))
    metas.append(clip_turn(c, "Turn_Right", -1))
    metas.append(clip_stop(c))
    metas.append(clip_breathe(c))
    metas.append(clip_eat(c))
    metas.append(clip_drink(c))
    metas += clip_rest(c)
    metas.append(clip_call(c))
    runlift = g["lift"] * sp["length"] / (6 if r.quad else 5)
    if c.pred:
        metas.append(locomotion2(c, "Chase", spd["run"], g["run"] * 0.96, g["duty_run"], runlift,
                                 post=dict(pitch=-1, neck=10, head=-6, jaw=9, pant=5, tail_amp=4, tail_pitch=2, arm_tuck=-10, arm_swing=3, roll_k=0.8)))
        metas.append(clip_bite(c))
        metas.append(clip_recover(c))
    else:
        metas.append(locomotion2(c, "Flee", spd["run"], g["run"] * 0.94, g["duty_run"], runlift * 1.1,
                                 post=dict(pitch=-4, neck=-12, head=-8, jaw=6, pant=4, tail_amp=9, tail_pitch=9, roll_k=1.2)))
        metas.append(locomotion2(c, "Charge", spd["run"] * 0.9, g["run"], g["duty_run"], runlift,
                                 post=dict(pitch=1, neck=16, head=14, jaw=0, tail_amp=5, tail_pitch=3, roll_k=0.8)))
        metas.append(clip_defend(c))
    arm.animation_data.action = None
    for pb in arm.pose.bones: pb.rotation_quaternion = Quaternion(); pb.location = Vector()
    return metas, c.log
