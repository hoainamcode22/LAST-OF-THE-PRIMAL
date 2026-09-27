"""
PRIMAL FRONTIER - procedural creature animation (original). Works on any rig from pf_dino.bone_specs:
Pelvis / Spine / Chest / Neck_xx / Head / Jaw / Tail_xx / leg chains (Thigh, Calf, Foot, Toe) (UpperArm, Forearm, Hand,
Finger) (wings, fins). Every bone gets an armature-space delta rotation; locals are derived through the parent so
chains compose correctly. Legs use analytic 2-bone IK towards world-space foot targets, so planted feet never slide:
a planted foot moves backwards at exactly the clip speed (in-place clips, the AI moves the object).
"""
import bpy, math
from mathutils import Vector, Quaternion, Matrix

FPS = 30
def sstep(t): t = max(0.0, min(1.0, t)); return t * t * (3 - 2 * t)
def qa(axis, deg): return Quaternion(Vector(axis).normalized(), math.radians(deg))
X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))
FWD = Vector((0, -1, 0))

class _Track(dict):
    def __setitem__(self, k, v):
        dict.__setitem__(self, k, v)
        if hasattr(self, "touched"): self.touched.add(k)


def face_extra(r):
    """eyelids and tongue on rigs that have them: blinks in calm loops, squint when attacking / roaring, eyes shut when
    hurt, half closed in death; tongue lifts when roaring and moves while eating"""
    if "Eyelid_Upper_L" not in r.names and "Tongue_01" not in r.names: return
    name, t = getattr(r, "clip", ""), getattr(r, "t", 0.0)
    bump = lambda x, w: max(0.0, 1 - (x / w) ** 2)
    close = 0.0
    if name in ("Idle", "Idle_Variation", "Walk", "Look", "Alert", "Eat", "Drink", "Turn_Left", "Turn_Right"):
        for c in (0.31, 0.77) if name.startswith("Idle") else (0.55,):
            close = max(close, bump(t - c, 0.035 if name.startswith("Idle") else 0.07))
    elif name in ("Roar", "Attack", "Heavy_Attack", "Charge"): close = 0.35 * math.sin(math.pi * t)
    elif name == "Hurt": close = 0.9 * math.sin(math.pi * min(1, t * 1.6))
    elif name == "Death": close = 0.62 * min(1.0, t * 1.3)
    hd = (r.tail0["Head"] - r.head0["Head"]).normalized() if "Head" in r.names else Vector((0, -1, 0))
    for side, s in (("L", 1), ("R", -1)):
        if f"Eyelid_Upper_{side}" in r.names: r.follow(f"Eyelid_Upper_{side}", Quaternion(hd, math.radians(-s * 68 * close)))
        if f"Eyelid_Lower_{side}" in r.names: r.follow(f"Eyelid_Lower_{side}", Quaternion(hd, math.radians(s * 22 * close)))
    if "Tongue_01" in r.names:
        curl = 0.0; yaw = 0.0
        if name == "Roar": curl = 22 * math.sin(math.pi * t)
        elif name == "Eat": curl = 10 * math.sin(2 * math.pi * t * 4); yaw = 8 * math.sin(2 * math.pi * t * 2)
        elif name == "Drink": curl = 14 * max(0.0, math.sin(2 * math.pi * t * 3))
        elif name == "Death": curl = -8 * min(1.0, t * 1.2); yaw = 10 * min(1.0, t * 1.2)
        elif name in ("Attack", "Heavy_Attack"): curl = 12 * math.sin(math.pi * t)
        for i, nm in enumerate(("Tongue_01", "Tongue_02", "Tongue_03")):
            if nm in r.names: r.follow(nm, qa(Z, yaw * (0.3 + 0.35 * i)) @ qa(X, curl * (0.25 + 0.4 * i)))


class Rig:
    def __init__(self, arm, spec):
        self.arm = arm; self.sp = spec; self.bones = arm.data.bones
        self.rest = {b.name: b.matrix_local.copy() for b in self.bones}
        self.head0 = {b.name: b.head_local.copy() for b in self.bones}
        self.tail0 = {b.name: b.tail_local.copy() for b in self.bones}
        self.parent = {b.name: (b.parent.name if b.parent else None) for b in self.bones}
        self.order = [b.name for b in self.bones]                          # parents before children
        self.names = set(self.order)
        self.spine = [n for n in self.order if n.startswith("Spine_")] + (["Chest"] if "Chest" in self.names else [])
        self.neck = sorted([n for n in self.order if n.startswith("Neck_")])
        self.tail = sorted([n for n in self.order if n.startswith("Tail_")])
        self.legs = []
        for side in ("L", "R"):
            if f"Thigh_{side}" in self.names: self.legs.append(("hind", side, [f"Thigh_{side}", f"Calf_{side}", f"Foot_{side}", f"Toe_{side}"]))
            if f"Forearm_{side}" in self.names and f"WingFinger_01_{side}" not in self.names:
                self.legs.append(("front", side, [f"UpperArm_{side}", f"Forearm_{side}", f"Hand_{side}", f"Finger_{side}"]))
        self.quad = spec["kind"] == "quad"
        self.L = spec["length"]
        self.reset()

    def reset(self):
        self.D = _Track({n: Quaternion() for n in self.order}); self.D.touched = set(); self.off = Vector()   # pelvis translation (armature space)

    # ---- forward kinematics of rest-relative deltas
    def posed_head(self, name, cache):
        if name in cache: return cache[name]
        p = self.parent[name]
        if p is None: h = self.head0[name].copy()
        else:
            ph = self.posed_head(p, cache)
            h = ph + self.D[p] @ (self.head0[name] - self.head0[p])
        if name == "Pelvis": h = h + self.off
        cache[name] = h; return h

    def world_delta(self, name):
        return self.D[name]

    def set_chain_rel(self, names, q_each):
        """rotate each bone of a chain by q (armature space) on top of its parent: accumulates like a spine curl"""
        for n in names:
            p = self.parent[n]; self.D[n] = q_each @ self.D[p] if p else q_each

    def write(self, frame):
        pb = self.arm.pose.bones
        if getattr(self, "extra", None): self.extra(self)
        for n in self.order:                                   # untouched bones keep their rest pose relative to the parent
            if n not in self.D.touched:
                p = self.parent[n]; dict.__setitem__(self.D, n, self.D[p] if p else Quaternion())
        for n in self.order:
            p = self.parent[n]
            Dp = self.D[p] if p else Quaternion()
            R = self.rest[n].to_quaternion()
            ql = R.inverted() @ Dp.inverted() @ self.D[n] @ R
            b = pb[n]; b.rotation_mode = 'QUATERNION'; b.rotation_quaternion = ql
            b.keyframe_insert("rotation_quaternion", frame=frame)
            if n == "Pelvis":
                Rp = self.rest[n].to_3x3()
                parent_rot = (self.D[p] if p else Quaternion()).to_matrix()
                b.location = Rp.inverted() @ parent_rot.inverted() @ self.off
                b.keyframe_insert("location", frame=frame)

    # ---- helpers
    def follow(self, name, q):
        """bone keeps its parent's delta, then rotates by q around its own head (armature space)"""
        p = self.parent[name]; self.D[name] = q @ (self.D[p] if p else Quaternion())

    def aim_bone(self, name, target_dir, pole=None):
        """absolute delta so the bone points along target_dir (armature space), keeping the rest pole plane"""
        d0 = (self.tail0[name] - self.head0[name]).normalized(); d1 = target_dir.normalized()
        q = d0.rotation_difference(d1)
        if pole is not None:
            # align rest side axis (bone X) with the pole plane
            x0 = q @ self.rest[name].to_3x3().col[0].normalized()
            px = (pole - pole.project(d1)).normalized()
            if px.length > 1e-6:
                x0p = (x0 - x0.project(d1)).normalized()
                ang = x0p.angle(px) if x0p.length > 1e-6 else 0.0
                s = 1 if d1.dot(x0p.cross(px)) > 0 else -1
                q = Quaternion(d1, s * ang) @ q
        self.D[name] = q

    def leg_ik(self, chain, foot_target, lift_toe=0.0, meta_pitch=0.0, cache=None):
        """chain = [upper, lower, meta, toe]; foot_target = world position for the 'foot' joint (meta tail)"""
        up, lo, meta, toe = chain
        if cache is None: cache = {}
        hip = self.posed_head(up, cache)
        # metatarsus keeps its rest direction (tilted by meta_pitch around X), ankle derived from the foot target
        mdir0 = (self.tail0[meta] - self.head0[meta]); ml = mdir0.length
        mdir = qa(X, meta_pitch) @ mdir0.normalized()
        ankle = foot_target - mdir * ml
        l1 = (self.tail0[up] - self.head0[up]).length; l2 = (self.tail0[lo] - self.head0[lo]).length
        dv = ankle - hip; dist = min(dv.length, (l1 + l2) * 0.999); dist = max(dist, abs(l1 - l2) + 1e-3)
        dn = dv.normalized()
        # knee bend plane from the rest pose
        k0 = self.head0[lo]; h0 = self.head0[up]; a0 = self.head0[meta]
        bend0 = (k0 - h0) - (k0 - h0).project((a0 - h0).normalized())
        if bend0.length < 1e-4: bend0 = FWD.copy()
        bend = bend0.normalized(); bend = (bend - bend.project(dn)).normalized()
        cosA = max(-1, min(1, (l1 * l1 + dist * dist - l2 * l2) / (2 * l1 * dist))); A = math.acos(cosA)
        knee = hip + (dn * math.cos(A) + bend * math.sin(A)) * l1
        side = dn.cross(bend)
        self.aim_bone(up, knee - hip, None); self.aim_bone(lo, ankle - knee, None)
        self.aim_bone(meta, mdir, None)
        # toe: keep the rest world direction, curl up when lifted
        tdir0 = (self.tail0[toe] - self.head0[toe]).normalized()
        self.D[toe] = qa(X, -lift_toe) @ Quaternion()  # world orientation = rest (rotated by lift)
        cache.clear()

    # ---- body layers
    def spine_curve(self, pitch=0.0, yaw=0.0, roll=0.0):
        n = max(1, len(self.spine))
        for s in self.spine: self.follow(s, qa(Z, yaw / n) @ qa(X, pitch / n) @ qa(Y, roll / n))

    def neck_head(self, pitch=0.0, yaw=0.0, roll=0.0, head_pitch=0.0, head_yaw=0.0):
        n = max(1, len(self.neck))
        for s in self.neck: self.follow(s, qa(Z, yaw / n) @ qa(X, pitch / n))
        if "Head" in self.names: self.follow("Head", qa(Z, head_yaw) @ qa(X, head_pitch) @ qa(Y, roll))

    def jaw(self, open_deg):
        # long snouts swing much further at the tip for the same angle and stretch the mouth corners: scaled per species
        if "Jaw" in self.names: self.follow("Jaw", qa(X, open_deg * self.sp["head"].get("jaw_k", 1.0)))

    def tail_wave(self, yaw_amp, phase, lag=0.12, pitch=0.0, pitch_amp=0.0):
        n = max(1, len(self.tail))
        for i, t in enumerate(self.tail):
            k = (i + 1) / n
            yv = yaw_amp * k * math.sin(2 * math.pi * (phase - i * lag))
            pv = pitch / n + pitch_amp * k * math.sin(2 * math.pi * (phase - i * lag) * 2)
            self.follow(t, qa(Z, yv) @ qa(X, pv))

    def pelvis(self, off=Vector(), pitch=0.0, yaw=0.0, roll=0.0):
        self.off = off
        self.D["Pelvis"] = qa(Z, yaw) @ qa(X, pitch) @ qa(Y, roll)

    def rest_legs(self, cache=None, sink=0.0):
        """feet stay at their rest ground positions (standing)"""
        for grp, side, ch in self.legs:
            ft = self.tail0[ch[2]].copy(); ft.z = max(0.0, ft.z - sink) if self.head0[ch[2]].z < self.L * 0.2 else ft.z
            self.leg_ik(ch, ft, 0, 0, {})

    def arms_follow(self):
        """small biped arms: hang relaxed with the chest"""
        for grp, side, ch in self.legs:
            pass

# ------------------------------------------------------------------ gait
def gait_offsets(rig, run):
    offs = {}
    for grp, side, ch in rig.legs:
        s = 0.0 if side == "L" else 0.5
        if rig.quad:
            if grp == "front": s += (0.25 if not run else 0.1)
        offs[(grp, side)] = s % 1.0
    return offs

def foot_target(rig, ch, phase, duty, stride, lift):
    base = rig.tail0[ch[2]].copy()
    grounded = base.z < rig.L * 0.15
    if not grounded:                                         # small arms: no stepping
        return base, 0.0
    reach = stride * duty                                    # ground covered while the foot is planted (speed * stance time)
    if phase < duty:                                         # stance: moves backwards at exactly the body speed
        s = phase / duty
        y = base.y - reach / 2 + reach * s
        return Vector((base.x, y, base.z)), 0.0
    u = (phase - duty) / (1 - duty)                          # swing: back to front, lifted
    e = sstep(u)
    y = base.y + reach / 2 - reach * e
    z = base.z + lift * math.sin(math.pi * u) ** 0.9
    return Vector((base.x, y, z)), 25.0 * math.sin(math.pi * u)

def new_action(arm, name):
    act = bpy.data.actions.get(name)
    if act: bpy.data.actions.remove(act)
    act = bpy.data.actions.new(name); act.use_fake_user = True
    arm.animation_data_create(); arm.animation_data.action = act
    return act

def locomotion(rig, name, speed, cycle, duty, lift_k, run=False):
    arm = rig.arm; act = new_action(arm, name)
    frames = max(8, int(round(cycle * FPS)))
    stride = speed * frames / FPS                            # the clip's real length, so the planted foot matches the speed exactly
    lift = lift_k * (1.3 if run else 1.0)
    offs = gait_offsets(rig, run)
    L = rig.L; bob = (0.012 if not run else 0.03) * L * (0.6 if rig.quad else 1.0)
    rig.clip = name
    for f in range(frames + 1):
        ph = f / frames; rig.t = ph
        rig.reset()
        steps = 2 if not rig.quad else 2
        rig.pelvis(Vector((0, 0, -bob * 0.5 * (1 + math.cos(4 * math.pi * ph)) * (0.7 if rig.quad else 1.0))),
                   pitch=(-3 if run else -1) * (1 if not rig.quad else 0.4), yaw=2.5 * math.sin(2 * math.pi * ph), roll=(2.2 if not rig.quad else 1.0) * math.sin(2 * math.pi * ph))
        rig.spine_curve(pitch=1.0 * math.sin(4 * math.pi * ph), yaw=-3.0 * math.sin(2 * math.pi * ph))
        rig.neck_head(pitch=(4 if run else 1.5) * math.sin(4 * math.pi * ph + 0.6) - (8 if run and not rig.quad else 0), yaw=2.0 * math.sin(2 * math.pi * ph + 1.0),
                      head_pitch=-1.5 * math.sin(4 * math.pi * ph + 0.9) + (6 if run and not rig.quad else 0))
        rig.jaw(3 if run else 0)
        rig.tail_wave((7 if run else 5), ph + 0.1, 0.1, pitch=(4 if run else 0), pitch_amp=1.5)
        for grp, side, ch in rig.legs:
            p = (ph + offs[(grp, side)]) % 1.0
            tgt, toe = foot_target(rig, ch, p, duty, stride, lift)
            if (rig.tail0[ch[2]].z >= rig.L * 0.15):                # arms: gentle swing
                rig.follow(ch[0], qa(X, 6 * math.sin(2 * math.pi * p))); rig.follow(ch[1], qa(X, 8 + 4 * math.sin(2 * math.pi * p))); rig.follow(ch[2], Quaternion()); rig.follow(ch[3], qa(X, 10))
                continue
            rig.leg_ik(ch, tgt, toe, -10 * math.sin(math.pi * max(0, (p - duty) / (1 - duty))) if p > duty else 4 * (p / duty - 0.5), {})
        rig.write(f + 1)
    act.frame_range = (1, frames + 1)
    return dict(name=name, frames=frames, loop=True, speed=round(speed, 3), move="forward")

def pose_clip(rig, name, frames, fn, loop):
    arm = rig.arm; act = new_action(arm, name)
    rig.clip = name
    for f in range(frames + 1):
        t = f / frames; rig.t = t; rig.reset(); fn(rig, t); rig.write(f + 1)
    act.frame_range = (1, frames + 1)
    return dict(name=name, frames=frames, loop=loop, speed=0.0)

def breathe(rig, t, k=1.0):
    b = math.sin(2 * math.pi * t)
    rig.pelvis(Vector((0, 0, -0.004 * rig.L * (1 + b) * 0.5 * k)))
    rig.spine_curve(pitch=0.8 * b * k)

def wings(r, spread, flap=0.0, fold=0.0, sweep=0.0):
    """spread 1 = open, fold 1 = folded against the body; flap = up/down degrees"""
    for side, s in (("L", 1), ("R", -1)):
        if f"UpperArm_{side}" not in r.names: continue
        r.follow(f"UpperArm_{side}", qa(Y, s * (flap - 70 * fold)) @ qa(Z, s * (35 * fold + sweep)))
        r.follow(f"Forearm_{side}", qa(Z, -s * 120 * fold) @ qa(Y, s * 0.35 * flap))
        r.follow(f"Hand_{side}", qa(Z, s * 60 * fold))
        for i in range(1, 4):
            nm = f"WingFinger_{i:02d}_{side}"
            if nm in r.names: r.follow(nm, qa(Z, s * (150 * fold if i == 1 else 8 * fold)) @ qa(Y, s * 0.25 * flap * (i / 3)))

def build_flyer(rig, spec):
    metas = []
    def stand(r, t, k=1.0):
        breathe(r, t, k); wings(r, 0, fold=1.0); r.rest_legs()
    metas.append(pose_clip(rig, "Idle", 90, lambda r, t: (stand(r, t), r.neck_head(yaw=10 * math.sin(2 * math.pi * t)), r.rest_legs()), True))
    metas.append(pose_clip(rig, "Idle_Variation", 120, lambda r, t: (stand(r, t), r.neck_head(pitch=-10 * math.sin(math.pi * t) ** 2, yaw=30 * math.sin(2 * math.pi * t)), wings(r, 0, fold=1.0 - 0.6 * math.sin(math.pi * t) ** 2), r.rest_legs()), True))
    m = locomotion(rig, "Walk", spec["speeds"]["walk"], spec["gait"]["walk"], spec["gait"]["duty_walk"], spec["gait"]["lift"]); metas.append(m)
    def fly(r, t):
        ph = 2 * math.pi * t
        r.pelvis(Vector((0, 0, 0.05 * math.sin(ph + 1.2))), pitch=-4)
        wings(r, 1, flap=38 * math.sin(ph)); r.neck_head(pitch=-6, head_pitch=4 * math.sin(ph))
        for grp, side, ch in r.legs: r.follow(ch[0], qa(X, -60)); r.follow(ch[1], qa(X, 20)); r.follow(ch[2], qa(X, 10)); r.follow(ch[3], Quaternion())
    metas.append(dict(pose_clip(rig, "Fly", 27, fly, True), speed=0.0))
    def glide(r, t):
        ph = 2 * math.pi * t
        r.pelvis(Vector((0, 0, 0.02 * math.sin(ph))), roll=4 * math.sin(ph), pitch=-3)
        wings(r, 1, flap=4 * math.sin(ph)); r.neck_head(yaw=5 * math.sin(ph))
        for grp, side, ch in r.legs: r.follow(ch[0], qa(X, -60)); r.follow(ch[1], qa(X, 20)); r.follow(ch[2], qa(X, 10)); r.follow(ch[3], Quaternion())
    metas.append(pose_clip(rig, "Glide", 90, glide, True))
    def takeoff(r, t):
        a = sstep(t / 0.3); up = sstep((t - 0.3) / 0.7)
        r.pelvis(Vector((0, 0, 0.6 * up)), pitch=-10 * a)
        wings(r, 1, flap=45 * math.sin(2 * math.pi * t * 3) * up, fold=1 - a)
        if up < 0.05: r.rest_legs()
    metas.append(pose_clip(rig, "Takeoff", 40, takeoff, False))
    def land(r, t):
        a = sstep(t / 0.6)
        r.pelvis(Vector((0, 0, 0.5 * (1 - a))), pitch=10 * (1 - a))
        wings(r, 1, flap=30 * math.sin(2 * math.pi * t * 2) * (1 - a), fold=sstep((t - 0.6) / 0.4))
        if a > 0.95: r.rest_legs()
    metas.append(pose_clip(rig, "Land", 40, land, False))
    def peck(r, t):
        s = math.sin(math.pi * min(1, t * 1.5)); stand(r, t, 0.3); r.neck_head(pitch=30 * s, head_pitch=15 * s); r.jaw(20 * s)
    m = pose_clip(rig, "Attack", 30, peck, False); m["events"] = [dict(frame=12, function="OnBite", param="")]; metas.append(m)
    metas.append(pose_clip(rig, "Roar", 60, lambda r, t: (stand(r, t, 0.3), r.neck_head(pitch=-20 * math.sin(math.pi * t), head_pitch=-10 * math.sin(math.pi * t)), r.jaw(30 * math.sin(math.pi * t)), wings(r, 0, fold=1 - 0.5 * math.sin(math.pi * t))), False))
    metas.append(pose_clip(rig, "Hurt", 24, lambda r, t: (stand(r, t, 0.2), r.neck_head(pitch=-20 * math.sin(math.pi * t)), wings(r, 0, fold=1 - 0.4 * math.sin(math.pi * t))), False))
    def die(r, t):
        a = sstep(t / 0.7); r.pelvis(Vector((0, 0, -0.45 * a)), roll=-70 * a); wings(r, 1, fold=1 - 0.7 * a); r.neck_head(pitch=30 * a, yaw=30 * a); r.jaw(15 * a)
    m = pose_clip(rig, "Death", 60, die, False); m["events"] = [dict(frame=45, function="OnBodyFall", param="")]; metas.append(m)
    return metas

def build_swimmer(rig, spec):
    metas = []
    def swim(r, t, amp, speedk=1.0):
        ph = t
        r.pelvis(Vector((0.02 * r.L * math.sin(2 * math.pi * ph), 0, 0.01 * r.L * math.sin(4 * math.pi * ph))), yaw=-3 * amp / 10 * math.sin(2 * math.pi * ph))
        r.spine_curve(yaw=4 * amp / 10 * math.sin(2 * math.pi * (ph - 0.1)))
        r.neck_head(yaw=-3 * amp / 10 * math.sin(2 * math.pi * (ph + 0.1)))
        r.tail_wave(amp, ph, 0.14, pitch=0)
        for side, s in (("L", 1), ("R", -1)):
            for base in ("Fin_Front", "Fin_Hind"):
                if f"{base}_{side}" in r.names:
                    r.follow(f"{base}_{side}", qa(Y, s * 15 * math.sin(2 * math.pi * ph + (0 if base == "Fin_Front" else 1.5))) @ qa(X, 10 * math.sin(2 * math.pi * ph)))
                    r.follow(f"{base}_02_{side}", qa(Y, s * 10 * math.sin(2 * math.pi * ph - 0.6)))
    metas.append(pose_clip(rig, "Idle", 90, lambda r, t: (swim(r, t, 6), r.jaw(2)), True))
    m = pose_clip(rig, "Walk", 60, lambda r, t: swim(r, t, 14), True); m["speed"] = 0.0; metas.append(m)
    m = pose_clip(rig, "Run", 34, lambda r, t: (swim(r, t, 22), r.jaw(4)), True); m["speed"] = 0.0; metas.append(m)
    def bite(r, t):
        a = sstep(t / 0.35); s = sstep((t - 0.35) / 0.15); rc = sstep((t - 0.55) / 0.45)
        wind = a * (1 - s); hit = s * (1 - rc)
        swim(r, t, 8); r.neck_head(pitch=-8 * wind + 8 * hit, head_pitch=-6 * wind); r.jaw(42 * wind + 4)
        r.pelvis(Vector((0, 0.04 * r.L * wind - 0.08 * r.L * hit, 0)))
    m = pose_clip(rig, "Attack", 40, bite, False); m["events"] = [dict(frame=17, function="OnBite", param="")]; metas.append(m)
    metas.append(pose_clip(rig, "Roar", 60, lambda r, t: (swim(r, t, 6), r.neck_head(pitch=-15 * math.sin(math.pi * t)), r.jaw(35 * math.sin(math.pi * t))), False))
    metas.append(pose_clip(rig, "Hurt", 24, lambda r, t: (swim(r, t, 12), r.neck_head(yaw=-20 * math.sin(math.pi * t)), r.jaw(15 * math.sin(math.pi * t))), False))
    def die(r, t):
        a = sstep(t / 0.8); r.pelvis(Vector((0, 0, 0.3 * a)), roll=-160 * a); r.tail_wave(0, 0, 0); r.jaw(20 * a)
    metas.append(pose_clip(rig, "Death", 75, die, False))
    return metas

def build_clips(arm, spec):
    rig = Rig(arm, spec); rig.extra = face_extra; kind = spec["kind"]; g = spec["gait"]; sp = spec["speeds"]
    if kind in ("flyer", "swimmer"):
        metas = build_flyer(rig, spec) if kind == "flyer" else build_swimmer(rig, spec)
        arm.animation_data.action = None
        for pb in arm.pose.bones: pb.rotation_quaternion = Quaternion(); pb.location = Vector()
        return metas
    pred = spec["head"].get("teeth", 0) > 0
    metas = []
    def standing(r): r.rest_legs()
    # --- idle / variations
    def idle(r, t):
        breathe(r, t); r.neck_head(pitch=1.5 * math.sin(2 * math.pi * t), yaw=4 * math.sin(2 * math.pi * t * 1), head_pitch=1 * math.sin(4 * math.pi * t))
        r.tail_wave(3, t, 0.12); r.jaw(0); standing(r)
    metas.append(pose_clip(rig, "Idle", 120, idle, True))
    def idle_var(r, t):
        breathe(r, t); look = math.sin(math.pi * t) ** 2
        r.neck_head(pitch=-6 * look, yaw=28 * math.sin(2 * math.pi * t), head_yaw=10 * math.sin(2 * math.pi * t), head_pitch=4 * look)
        r.tail_wave(6, t * 2, 0.12); r.jaw(0); standing(r)
    metas.append(pose_clip(rig, "Idle_Variation", 150, idle_var, True))
    # --- locomotion
    metas.append(locomotion(rig, "Walk", sp["walk"], g["walk"], g["duty_walk"], g["lift"] * spec["length"] / (8 if kind != "biped" else 6)))
    metas.append(locomotion(rig, "Run", sp["run"], g["run"], g["duty_run"], g["lift"] * spec["length"] / (6 if kind != "biped" else 5), run=True))
    # turning in place: small stepping with the body twisting (the AI rotates the object)
    for nm, s in (("Turn_Left", 1), ("Turn_Right", -1)):
        m = locomotion(rig, nm, sp["walk"] * 0.35, g["walk"], g["duty_walk"], g["lift"] * spec["length"] / 9)
        m["speed"] = 0.0; m["move"] = "turn"; metas.append(m)
        # add the twist on top
    # --- feeding
    def eat(r, t):
        breathe(r, t, 0.5); down = sstep(min(1, t * 4)) * sstep(min(1, (1 - t) * 4)) if False else 1.0
        chew = math.sin(2 * math.pi * t * 4)
        low = 1.0
        # bipeds tip the whole body forward from the hips; quadrupeds lower the neck
        r.pelvis(Vector((0, 0, -0.01 * r.L)), pitch=2 if r.quad else 20)
        r.spine_curve(pitch=3 if r.quad else 8)
        r.neck_head(pitch=(38 if r.quad else 14) * low, yaw=3 * math.sin(2 * math.pi * t), head_pitch=(12 if r.quad else 16) + 3 * chew)
        r.jaw(6 + 6 * max(0, chew)); r.tail_wave(3, t, 0.12, pitch=-3); standing(r)
    metas.append(pose_clip(rig, "Eat", 120, eat, True))
    def drink(r, t):
        breathe(r, t, 0.4); lap = math.sin(2 * math.pi * t * 3)
        r.pelvis(Vector((0, 0, -0.015 * r.L)), pitch=3 if r.quad else 24)
        r.spine_curve(pitch=4 if r.quad else 10)
        r.neck_head(pitch=(44 if r.quad else 16), head_pitch=(16 if r.quad else 20) + 2 * lap)
        r.jaw(4 + 4 * max(0, lap)); r.tail_wave(2, t, 0.12, pitch=-4); standing(r)
    metas.append(pose_clip(rig, "Drink", 120, drink, True))
    # --- awareness
    def look(r, t):
        a = sstep(min(1, t * 3)) * sstep(min(1, (1 - t) * 3))
        breathe(r, t, 0.6)
        r.neck_head(pitch=-14 * a, yaw=35 * math.sin(2 * math.pi * t) * a, head_pitch=-6 * a, head_yaw=15 * math.sin(2 * math.pi * t) * a)
        r.tail_wave(2, t, 0.1); r.jaw(0); standing(r)
    metas.append(pose_clip(rig, "Look", 90, look, False))
    def alert(r, t):
        breathe(r, t * 2, 0.6)
        r.pelvis(Vector((0, 0, 0.005 * r.L)), pitch=-3)
        r.neck_head(pitch=-20, yaw=6 * math.sin(2 * math.pi * t), head_pitch=-5)
        r.tail_wave(1.5, t, 0.1, pitch=6); r.jaw(0); standing(r)
    metas.append(pose_clip(rig, "Alert", 60, alert, True))
    def roar(r, t):
        a = sstep(min(1, t * 3.5)); b = sstep(min(1, (1 - t) * 3))
        k = a * b; shake = math.sin(2 * math.pi * t * 9) * k
        r.pelvis(Vector((0, 0.01 * r.L * k, -0.01 * r.L * k)), pitch=-4 * k)
        r.spine_curve(pitch=-4 * k)
        r.neck_head(pitch=(-10 if pred else -22) * k, yaw=2 * shake, head_pitch=(-12 if pred else -18) * k, roll=2 * shake)
        r.jaw((38 if pred else 26) * k); r.tail_wave(4, t * 2, 0.1, pitch=8 * k); standing(r)
    metas.append(pose_clip(rig, "Roar", 90, roar, False))
    # --- combat
    def attack(r, t):
        # anticipation (0-0.35), strike (0.35-0.5), recover
        a = sstep(t / 0.35); s = sstep((t - 0.35) / 0.15); rc = sstep((t - 0.55) / 0.45)
        wind = a * (1 - s); hit = s * (1 - rc)
        if pred:
            r.pelvis(Vector((0, 0.03 * r.L * wind - 0.06 * r.L * hit, -0.01 * r.L * wind)), pitch=5 * wind - 6 * hit)
            r.spine_curve(pitch=6 * wind - 8 * hit)
            r.neck_head(pitch=-12 * wind + 14 * hit, head_pitch=-10 * wind + 6 * hit)
            r.jaw(40 * wind * (1 - s) + 45 * max(0, 1 - abs(t - 0.4) / 0.08) * 0 + 36 * wind)
            if t > 0.42: r.jaw(4)
        elif spec["armor"] and spec["armor"].get("club"):
            r.pelvis(Vector(), yaw=-10 * wind + 16 * hit)
            r.tail_wave(0, 0, 0); 
            for i, tb in enumerate(r.tail): r.follow(tb, qa(Z, (-14 * wind + 30 * hit) * (i + 1) / len(r.tail)))
            r.neck_head(pitch=4 * wind); r.jaw(0)
        else:   # horn / head butt
            r.pelvis(Vector((0, 0.02 * r.L * wind - 0.05 * r.L * hit, -0.02 * r.L * wind)), pitch=6 * wind - 3 * hit)
            r.neck_head(pitch=18 * wind - 6 * hit, head_pitch=14 * wind - 22 * hit, yaw=6 * hit)
            r.jaw(0)
        if not (spec["armor"] and spec["armor"].get("club")): r.tail_wave(4, t, 0.1, pitch=6 * wind)
        standing(r)
    m = pose_clip(rig, "Attack", 42, attack, False); m["events"] = [dict(frame=int(42 * 0.42), function="OnBite" if pred else ("OnTailHit" if spec["armor"] and spec["armor"].get("club") else "OnHornHit"), param="")]; metas.append(m)
    def heavy(r, t):
        a = sstep(t / 0.4); s = sstep((t - 0.4) / 0.2); rc = sstep((t - 0.65) / 0.35)
        wind = a * (1 - s); hit = s * (1 - rc); shake = math.sin(2 * math.pi * t * 6) * hit
        r.pelvis(Vector((0, 0.04 * r.L * wind - 0.08 * r.L * hit, -0.02 * r.L * (wind + hit))), pitch=7 * wind - 8 * hit, yaw=8 * shake)
        r.spine_curve(pitch=6 * wind - 8 * hit, yaw=6 * shake)
        r.neck_head(pitch=-8 * wind + 16 * hit, yaw=18 * shake, head_pitch=-8 * wind + 10 * hit, roll=10 * shake)
        r.jaw((42 if pred else 10) * (wind + hit * 0.3)); r.tail_wave(8, t * 2, 0.1); standing(r)
    m = pose_clip(rig, "Heavy_Attack", 60, heavy, False); m["events"] = [dict(frame=int(60 * 0.52), function="OnBite" if pred else "OnHornHit", param="heavy")]; metas.append(m)
    def hurt(r, t):
        a = math.sin(math.pi * min(1, t * 1.6)) * (1 - t)
        r.pelvis(Vector((0, 0.02 * r.L * a, -0.01 * r.L * a)), roll=6 * a, yaw=-6 * a)
        r.spine_curve(pitch=-5 * a, yaw=8 * a)
        r.neck_head(pitch=-16 * a, yaw=-20 * a, head_pitch=-10 * a); r.jaw(18 * a); r.tail_wave(10 * a, t * 2, 0.1, pitch=10 * a); standing(r)
    metas.append(pose_clip(rig, "Hurt", 30, hurt, False))
    def death(r, t):
        a = sstep(t / 0.75)
        wid = spec["body"][1]["w"] if len(spec["body"]) > 1 else spec["body"][0]["w"]
        hz = r.head0["Pelvis"].z
        # roll onto the left side and drop to the ground
        r.pelvis(Vector((0.35 * wid * a, 0, -(hz - wid * 0.95) * a)), roll=-78 * a, pitch=-3 * a)
        r.spine_curve(pitch=-4 * a, yaw=6 * a)
        r.neck_head(pitch=(10 if r.quad else -6) * a, yaw=20 * a, head_pitch=10 * a); r.jaw(14 * a)
        r.tail_wave(0, 0, 0)
        for i, tb in enumerate(r.tail): r.follow(tb, qa(Z, 14 * a * (i + 1) / len(r.tail)))
        cache = {}
        for grp, side, ch in r.legs:
            r.follow(ch[0], qa(X, (20 if side == "L" else -10) * a)); r.follow(ch[1], qa(X, 25 * a)); r.follow(ch[2], qa(X, -15 * a)); r.follow(ch[3], Quaternion())
    m = pose_clip(rig, "Death", 75, death, False); m["events"] = [dict(frame=60, function="OnBodyFall", param="")]; metas.append(m)
    # herbivore charge = low-headed run; predators chase = run (the controller reuses Run)
    if not pred:
        cm = locomotion(rig, "Charge", sp["run"] * 0.9, g["run"], g["duty_run"], g["lift"] * spec["length"] / 6, run=True); metas.append(cm)
    # footstep events on stance starts
    for m in metas:
        if m["name"] in ("Walk", "Run", "Charge"):
            m["events"] = m.get("events", []) + [dict(frame=1, function="OnFootstep", param="L"), dict(frame=m["frames"] // 2, function="OnFootstep", param="R")]
    arm.animation_data.action = None
    for pb in arm.pose.bones: pb.rotation_quaternion = Quaternion(); pb.location = Vector()
    return metas
