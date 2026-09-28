# PRIMAL FRONTIER - procedural animation authoring for the humanoid rig.
# Poses are written in anatomical terms (degrees, armature axes at rest: X = character's left, -Y = forward, Z = up),
# converted to bone-local quaternions. Legs are solved every frame with analytic 2-bone IK so planted feet never slide.
import bpy, math
from mathutils import Vector, Matrix, Quaternion, Euler

FPS = 30

def ease(t, kind="smooth"):
    t = max(0.0, min(1.0, t))
    if kind == "linear": return t
    if kind == "in": return t * t * t                   # slow start (anticipation into strike)
    if kind == "out": return 1 - (1 - t) ** 3           # fast start, soft landing (impact -> settle)
    if kind == "inout": return t * t * t * (t * (6 * t - 15) + 10)
    return t * t * (3 - 2 * t)

def lerp(a, b, t):
    if isinstance(a, (tuple, list)): return tuple(lerp(x, y, t) for x, y in zip(a, b))
    if isinstance(a, dict): return {k: lerp(a[k], b.get(k, a[k]), t) for k in a}
    return a + (b - a) * t

def q_axis(axis, deg):
    return Quaternion(Vector(axis).normalized(), math.radians(deg))

class HumanRig:
    def __init__(self, arm_obj):
        self.obj = arm_obj
        self.bones = arm_obj.data.bones
        self.rest = {b.name: b.matrix_local.copy() for b in self.bones}
        self.len = {b.name: b.length for b in self.bones}
        self.hand = {}
        for sd in ("L", "R"):
            d = (self.rest[f"LowerArm_{sd}"].col[1].xyz).normalized()
            s = 1 if sd == "L" else -1
            n = Vector((-s * 0.707, 0, -0.707)).normalized()
            self.hand[sd] = (d, n)
        # rest foot contact points (armature space)
        self.foot_rest = {}
        for sd in ("L", "R"):
            ank = self.rest[f"Foot_{sd}"].translation.copy()
            ball = self.rest[f"Toe_{sd}"].translation.copy()
            toe = ball + self.rest[f"Toe_{sd}"].col[1].xyz.normalized() * self.len[f"Toe_{sd}"]
            heel = Vector((ank.x, ank.y + 0.035, 0.0))
            self.foot_rest[sd] = dict(ankle=ank, ball=ball, toe=toe, heel=heel)

    # ---- low level
    def local_from_arm(self, bone, q_arm):
        R = self.rest[bone].to_3x3()
        return (R.inverted() @ q_arm.to_matrix() @ R).to_quaternion()

    def set_rot(self, bone, q_arm, twist=0.0):
        pb = self.obj.pose.bones[bone]
        q = self.local_from_arm(bone, q_arm)
        if twist: q = q @ q_axis((0, 1, 0), twist)
        pb.rotation_quaternion = q

    def reset(self):
        for pb in self.obj.pose.bones:
            pb.rotation_quaternion = Quaternion(); pb.location = (0, 0, 0); pb.scale = (1, 1, 1)

    # ---- anatomical setters (armature-space axes at rest)
    def spine_bone(self, bone, bend=0, side=0, twist=0):
        """bend + = forward, side + = towards character's left, twist + = turn left"""
        q = q_axis((0, 0, 1), twist) @ q_axis((0, 1, 0), side) @ q_axis((1, 0, 0), bend)
        self.set_rot(bone, q)

    def arm(self, sd, flex=0, abduct=0, twist=0, horiz=0):
        """flex + = swing forward, abduct + = raise sideways (0 = A-pose rest, -38 = relaxed down), horiz + = swing across chest"""
        s = 1 if sd == "L" else -1
        q = q_axis((0, 0, 1), -horiz * s) @ q_axis((1, 0, 0), -flex) @ q_axis((0, 1, 0), -abduct * s)
        self.set_rot(f"UpperArm_{sd}", q, twist * s)

    def elbow(self, sd, flex=0, twist=0):
        d, n = self.hand[sd]
        axis = d.cross(Vector((0, -1, 0))).normalized()
        s = 1 if sd == "L" else -1
        self.set_rot(f"LowerArm_{sd}", q_axis(axis, flex), twist * s)

    def wrist(self, sd, flex=0, dev=0, twist=0):
        """flex + = palm-ward bend"""
        d, n = self.hand[sd]
        s = 1 if sd == "L" else -1
        a_flex = d.cross(n).normalized()
        self.set_rot(f"Hand_{sd}", q_axis(a_flex, flex) @ q_axis(n, dev * s), twist * s)

    def clavicle(self, sd, shrug=0, fwd=0):
        s = 1 if sd == "L" else -1
        self.set_rot(f"Clavicle_{sd}", q_axis((0, 0, 1), fwd * s) @ q_axis((0, 1, 0), -shrug * s))

    # relaxed hands: curl grows from the index to the little finger; at low curl the middle joint bends most
    # (proximal and distal less), closing into an even fist as curl rises
    FINGER_GRAD = {"Index": 0.8, "Middle": 0.94, "Ring": 1.08, "Pinky": 1.22}
    FINGER_SPREAD = {"Index": 1.0, "Middle": 0.3, "Ring": -0.35, "Pinky": -1.0}

    THUMB_ADDUCT = 0.0      # degrees: thumb metacarpal swung towards the index (relaxed hand), set by the clip library

    def fingers(self, sd, curl=20, thumb=15, spread=3.0):
        d, n = self.hand[sd]
        s = 1 if sd == "L" else -1
        k = max(0.0, min(1.0, (curl - 20.0) / 45.0))          # 0 relaxed .. 1 fist
        segw = (("01", 0.55 + 0.45 * k), ("02", 1.18), ("03", 0.85 + 0.1 * k))
        spr = spread * (1.0 - k)
        for f in ("Index", "Middle", "Ring", "Pinky"):
            g = 1.0 + (self.FINGER_GRAD[f] - 1.0) * (1.0 - 0.6 * k)
            for seg, w in segw:
                b = f"{f}_{seg}_{sd}"
                if b not in self.rest: continue
                fd = self.rest[b].col[1].xyz.normalized()
                q = q_axis(fd.cross(n), curl * g * w)
                if seg == "01" and spr: q = q_axis(n, spr * self.FINGER_SPREAD[f] * s) @ q
                self.set_rot(b, q)
        for seg, k2 in (("01", 0.6), ("02", 0.9), ("03", 0.7)):
            b = f"Thumb_{seg}_{sd}"
            if b not in self.rest: continue
            fd = self.rest[b].col[1].xyz.normalized()
            q = q_axis(fd.cross(n), thumb * k2)
            if seg == "01" and self.THUMB_ADDUCT: q = q_axis(n, self.THUMB_ADDUCT * s) @ q
            self.set_rot(b, q)

    def pelvis(self, loc=(0, 0, 0), bend=0, side=0, twist=0):
        pb = self.obj.pose.bones["Pelvis"]
        R = self.rest["Pelvis"].to_3x3()
        pb.location = R.inverted() @ Vector(loc)
        self.spine_bone("Pelvis", bend, side, twist)

    # ---- IK
    def _posed(self, bone):
        return self.obj.pose.bones[bone].matrix.copy()

    def solve_leg(self, sd, ankle, ball, toe, pole=Vector((0, -1, 0))):
        bpy.context.view_layer.update()
        P = self._posed("Pelvis")
        Rp = self.rest["Pelvis"]
        Rt, Rc, Rf, Ro = (self.rest[f"{b}_{sd}"] for b in ("Thigh", "Calf", "Foot", "Toe"))
        M0 = P @ Rp.inverted() @ Rt
        H = M0.translation.copy()
        L1, L2 = self.len[f"Thigh_{sd}"], self.len[f"Calf_{sd}"]
        A = Vector(ankle)
        v = A - H; dist = v.length
        dist_c = min(max(dist, abs(L1 - L2) + 1e-4), L1 + L2 - 1e-4)
        dirv = v.normalized()
        A = H + dirv * dist_c
        a = (L1 * L1 - L2 * L2 + dist_c * dist_c) / (2 * dist_c)
        h = math.sqrt(max(L1 * L1 - a * a, 0.0))
        pole = Vector(pole)
        pp = (pole - dirv * pole.dot(dirv))
        pp = pp.normalized() if pp.length > 1e-6 else Vector((0, -1, 0))
        K = H + dirv * a + pp * h
        def frame(y, xref, pos):
            y = y.normalized()
            x = (xref - y * xref.dot(y)).normalized()
            z = x.cross(y)
            m = Matrix((x, y, z)).transposed().to_4x4(); m.translation = pos
            return m
        # hinge axis perpendicular to the leg plane, sign matched to the rest X axis
        nrm = (K - H).cross(A - K)
        x_rest = M0.col[0].xyz
        if nrm.length < 1e-6: nrm = x_rest.copy()
        if nrm.dot(x_rest) < 0: nrm = -nrm
        Mt = frame(K - H, nrm, H)
        self.obj.pose.bones[f"Thigh_{sd}"].rotation_quaternion = (M0.inverted() @ Mt).to_quaternion()
        Mc0 = Mt @ Rt.inverted() @ Rc
        Mc = frame(A - K, nrm, K)
        self.obj.pose.bones[f"Calf_{sd}"].rotation_quaternion = (Mc0.inverted() @ Mc).to_quaternion()
        Mf0 = Mc @ Rc.inverted() @ Rf
        B = Vector(ball)
        Mf = frame(B - A, Mf0.col[0].xyz, A)
        self.obj.pose.bones[f"Foot_{sd}"].rotation_quaternion = (Mf0.inverted() @ Mf).to_quaternion()
        Mo0 = Mf @ Rf.inverted() @ Ro
        Mo = frame(Vector(toe) - B, Mo0.col[0].xyz, B)
        self.obj.pose.bones[f"Toe_{sd}"].rotation_quaternion = (Mo0.inverted() @ Mo).to_quaternion()

    def planted(self, sd, offset=(0, 0, 0), pitch=0.0, yaw=0.0, toe_bend=0.0):
        """foot targets: rest foot moved by offset, rotated by yaw (about Z at ankle) and pitch (+ = heel up, pivot on ball)."""
        fr = self.foot_rest[sd]
        off = Vector(offset)
        Rz = Matrix.Rotation(math.radians(yaw), 3, 'Z')
        ank, ball, toe, heel = (Rz @ (fr[k] - fr["ankle"]) + fr["ankle"] + off for k in ("ankle", "ball", "toe", "heel"))
        lat = Rz @ Vector((1, 0, 0))
        if pitch > 0:     # heel up: rotate ankle (and heel) about the ball; toes stay flat on the ground
            R = Matrix.Rotation(math.radians(pitch), 3, lat)
            ank = ball + R @ (ank - ball)
            if toe_bend < 0:  # optional toe curl up (swing)
                toe = ball + Matrix.Rotation(math.radians(toe_bend), 3, lat) @ (toe - ball)
        elif pitch < 0:   # toe up: rotate the whole foot about the heel
            R = Matrix.Rotation(math.radians(pitch), 3, lat)
            ank = heel + R @ (ank - heel); ball = heel + R @ (ball - heel); toe = heel + R @ (toe - heel)
        pole = Rz @ Vector((0, -1, 0))
        return ank, ball, toe, pole

    # ---- baking
    def new_action(self, name, frames, loop):
        act = bpy.data.actions.get(name)
        if act: bpy.data.actions.remove(act)
        act = bpy.data.actions.new(name)
        act.use_fake_user = True
        act["pf_loop"] = bool(loop)
        act["pf_frames"] = frames
        self.obj.animation_data_create()
        self.obj.animation_data.action = act
        return act

    def key_all(self, frame):
        for pb in self.obj.pose.bones:
            pb.keyframe_insert("rotation_quaternion", frame=frame, group=pb.name)
            if pb.name == "Pelvis":
                pb.keyframe_insert("location", frame=frame, group=pb.name)

    def fix_quat_continuity(self, act):
        """keep consecutive quaternion keys in the same hemisphere (no flips)"""
        by_bone = {}
        for fc in act.fcurves if hasattr(act, "fcurves") else []:
            pass
