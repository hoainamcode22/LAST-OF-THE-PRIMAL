# PRIMAL FRONTIER - weapon clips authored from weapon targets (grip position + blade / shaft direction per key),
# solved into anatomical arm parameters by pf_weapon_ik. Armature space: X = character's left, -Y = forward, Z = up.
# Grip frame (matches Unity PlayerHierarchy.TryComputeGrip): blade / shaft leaves the fist on the thumb side, edge
# faces the knuckles. Sword: blade along the fist, edge forward. Spear: underhand rear hand (thumb towards the tip),
# overhand front hand on the shaft. Bow: in the LEFT hand, limbs along the fist, right hand draws to the cheek.
import bpy, math
from mathutils import Vector
from pf_anim import ease, lerp
import pf_clips_human as CH
import pf_weapon_ik as WI
P, add = CH.P, CH.add

F, LFT, UP = Vector((0, -1, 0)), Vector((1, 0, 0)), Vector((0, 0, 1))
def V(x, y, z): return Vector((x, y, z))
def N(x, y, z): return Vector((x, y, z)).normalized()

FIST_R, FIST_L, RELAX_L = (80, 55), (78, 55), (30, 18)
# ------------------------------------------------------------------ body poses
SW_FEET = dict(ftL=(0.02, -0.12, 0, 0, 15, 0), ftR=(-0.02, 0.12, 0, 0, -25, 0))
def sw_body(**kw):
    d = dict(SW_FEET, pel=(0, 0.02, -0.05), pelr=(3, 0, -12), sp=(5, 0, -4), spu=(2, 0, -3), ch=(0, 0, -5), nk=(0, 0, 8), hd=(-2, 0, 6),
             fR=FIST_R, aL=(22, -28, 0, 8), eL=40, wL=(6, 0, 0), fL=RELAX_L)
    d.update(kw); return P(**d)
SP_FEET = dict(ftL=(0.02, -0.14, 0, 0, 20, 0), ftR=(-0.02, 0.14, 0, 0, -30, 0))
def sp_body(**kw):
    d = dict(SP_FEET, pel=(0, 0.02, -0.06), pelr=(4, 0, -25), sp=(4, 0, -10), ch=(0, 0, -10), nk=(0, 0, 20), hd=(-2, 0, 15),
             fR=(75, 55), fL=(72, 52))
    d.update(kw); return P(**d)
BOW_FEET = dict(ftL=(0.03, -0.12, 0, 0, 25, 0), ftR=(-0.03, 0.12, 0, 0, -40, 0))
def bow_body(**kw):
    d = dict(BOW_FEET, pelr=(0, 0, -30), sp=(0, 0, -10), ch=(0, 0, -18), nk=(0, 0, 30), hd=(0, 0, 25), fL=FIST_L, fR=(45, 32))
    d.update(kw); return P(**d)

# ------------------------------------------------------------------ weapon targets
SW_READY = dict(pos=V(-0.24, -0.32, 1.02), blade=N(0.15, -0.7, 0.7), edge=F)
def spear_pair(g, d, front=0.42):
    d = d.normalized()
    return {"R": dict(pos=g, blade=d), "L": dict(pos=g + d * front, blade=-d, edge=N(0.4, 0, 0.9), w_edge=0.1)}

def K(frame, body, targets, e="smooth"):
    return (frame, body, targets, e)

def clip_defs():
    C = {}
    # ---------------- sword
    ready = K(0, sw_body(), {"R": SW_READY})
    C["Sword_Idle"] = dict(frames=60, loop=True, upper=True, keys=[
        ready,
        K(30, sw_body(spu=(3, 0, -3), ch=(1.5, 0, -5)), {"R": dict(SW_READY, pos=V(-0.24, -0.325, 1.03), blade=N(0.13, -0.68, 0.72))}),
        K(60, sw_body(), {"R": SW_READY})], events=[], notes="sword held low-forward, edge towards the enemy, breathing")
    C["Sword_Attack_1"] = dict(frames=30, loop=False, keys=[
        ready,
        K(8, sw_body(pel=(0, 0.05, -0.04), pelr=(0, 0, -24), sp=(2, 0, -10), spu=(0, 0, -6), ch=(-4, 0, -12), nk=(0, 0, 24), hd=(-4, 0, 16),
                     aL=(35, -20, 0, 30), eL=70), {"R": dict(pos=V(-0.3, 0.02, 1.62), blade=N(-0.35, 0.55, 0.75), edge=N(0.2, -0.6, -0.2))}, "inout"),
        K(11, sw_body(pel=(0, -0.02, -0.07), pelr=(4, 0, -4), sp=(6, 0, 0), ch=(2, 0, 2), nk=(0, 0, 4), hd=(0, 0, 2), aL=(20, -25, 0, 10), eL=50),
          {"R": dict(pos=V(-0.2, -0.36, 1.45), blade=N(0.15, -0.55, 0.8), edge=N(0.45, -0.35, -0.8))}, "in"),
        K(14, sw_body(pel=(0, -0.06, -0.1), pelr=(8, 0, 20), sp=(10, 0, 10), spu=(4, 0, 6), ch=(6, 0, 14), nk=(-4, 0, -20), hd=(4, 0, -14),
                      ftL=(0.02, -0.2, 0, 0, 10, 0), ftR=(-0.02, 0.12, 0, 20, -25, 20), aL=(0, -30, 0, -10), eL=30),
          {"R": dict(pos=V(0.04, -0.44, 0.96), blade=N(0.55, -0.55, -0.62), edge=N(0.5, 0.2, -0.8))}, "out"),
        K(17, sw_body(pel=(0, -0.06, -0.1), pelr=(8, 0, 24), sp=(12, 0, 12), spu=(4, 0, 7), ch=(6, 0, 16), nk=(-4, 0, -22), hd=(4, 0, -16),
                      ftL=(0.02, -0.2, 0, 0, 10, 0), ftR=(-0.02, 0.12, 0, 20, -25, 20), aL=(-5, -30, 0, -10), eL=30),
          {"R": dict(pos=V(0.2, -0.28, 0.86), blade=N(0.6, -0.1, -0.78), edge=N(0.3, 0.5, -0.4))}, "smooth"),
        K(30, sw_body(), {"R": SW_READY}, "inout")],
        events=[(8, "OnAttackStart", "sword_1"), (10, "OnAttackActive", "sword_1"), (13, "OnAttackHit", "sword_1"), (16, "OnAttackEnd", "sword_1")],
        notes="diagonal cut from over the right shoulder down to the left hip, weight onto the front foot, follow-through")
    C["Sword_Attack_2"] = dict(frames=28, loop=False, keys=[
        ready,
        K(7, sw_body(pelr=(0, 0, 18), sp=(0, 0, 8), ch=(0, 0, 18), nk=(0, 0, -20), hd=(0, 0, -14), aL=(30, -25, 0, 0), eL=60),
          {"R": dict(pos=V(0.18, -0.22, 1.26), blade=N(0.8, 0.5, 0.12), edge=N(-0.4, -0.9, 0))}, "inout"),
        K(10, sw_body(pel=(0, -0.03, -0.06), pelr=(4, 0, 0), sp=(4, 0, 0), ch=(2, 0, -4), nk=(0, 0, 2), hd=(0, 0, 2), aL=(25, -30, 0, 10), eL=60),
          {"R": dict(pos=V(-0.12, -0.52, 1.26), blade=N(0.1, -1, 0.05), edge=N(-1, 0, 0))}, "in"),
        K(13, sw_body(pel=(0, -0.04, -0.07), pelr=(4, 0, -22), sp=(4, 0, -10), ch=(2, 0, -24), nk=(0, 0, 26), hd=(0, 0, 18), aL=(20, -30, 0, 30), eL=70),
          {"R": dict(pos=V(-0.4, -0.32, 1.24), blade=N(-0.8, -0.45, 0.05), edge=N(-0.4, 0.9, 0))}, "out"),
        K(28, sw_body(), {"R": SW_READY}, "inout")],
        events=[(7, "OnAttackStart", "sword_2"), (9, "OnAttackActive", "sword_2"), (11, "OnAttackHit", "sword_2"), (14, "OnAttackEnd", "sword_2")],
        notes="horizontal back-hand cut from the left side across to the right")
    C["Sword_Attack_3"] = dict(frames=34, loop=False, keys=[
        ready,
        K(11, sw_body(pel=(0, 0.04, -0.02), sp=(-6, 0, -4), ch=(-6, 0, 0), nk=(4, 0, 0), hd=(-6, 0, 0), aL=(60, -10, 0, 20), eL=50),
          {"R": dict(pos=V(-0.14, 0.06, 1.86), blade=N(0.0, 0.75, 0.66), edge=N(0, -0.6, 0.8))}, "inout"),
        K(14, sw_body(pel=(0, -0.08, -0.1), pelr=(8, 0, 2), sp=(10, 0, 0), spu=(4, 0, 0), aL=(30, -20, 0, 10), eL=40, ftL=(0, -0.22, 0, 0, 8, 0)),
          {"R": dict(pos=V(-0.1, -0.42, 1.5), blade=N(0.0, -0.6, 0.8), edge=N(0, -0.8, -0.6))}, "in"),
        K(16, sw_body(pel=(0, -0.12, -0.16), pelr=(12, 0, 4), sp=(18, 0, 0), spu=(8, 0, 0), ch=(4, 0, 0), nk=(-8, 0, 0), hd=(-6, 0, 0),
                      ftL=(0, -0.3, 0, 0, 5, 0), ftR=(0, 0.12, 0, 25, -15, 25), aL=(10, -30, 0, -10), eL=20),
          {"R": dict(pos=V(-0.08, -0.5, 1.02), blade=N(0.0, -0.72, -0.7), edge=N(0, 0.6, -0.8))}, "out"),
        K(19, sw_body(pel=(0, -0.12, -0.17), pelr=(12, 0, 4), sp=(20, 0, 0), spu=(8, 0, 0), ch=(4, 0, 0), nk=(-8, 0, 0), hd=(-6, 0, 0),
                      ftL=(0, -0.3, 0, 0, 5, 0), ftR=(0, 0.12, 0, 25, -15, 25), aL=(5, -30, 0, -10), eL=20),
          {"R": dict(pos=V(-0.06, -0.36, 0.86), blade=N(0.0, -0.4, -0.92), edge=N(0, 0.9, -0.4))}, "smooth"),
        K(34, sw_body(), {"R": SW_READY}, "inout")],
        events=[(11, "OnAttackStart", "sword_3"), (13, "OnAttackActive", "sword_3"), (15, "OnAttackHit", "sword_3"), (18, "OnAttackEnd", "sword_3"),
                (14, "OnFootstep", "L")],
        notes="overhead finisher with a step: sword raised behind the head, straight down cut")
    def two_hand(t):
        g = t["pos"]; b = t["blade"]
        return {"R": t, "L": dict(pos=g - b.normalized() * 0.1, blade=b, edge=t.get("edge"), w_edge=0.2)}
    C["Sword_Heavy"] = dict(frames=46, loop=False, keys=[
        ready,
        K(16, sw_body(pel=(0, 0.06, -0.04), pelr=(-2, 0, -20), sp=(-6, 0, -8), ch=(-8, 0, -10), nk=(4, 0, 16), hd=(-4, 0, 10), fL=FIST_L),
          two_hand(dict(pos=V(-0.22, 0.14, 1.88), blade=N(-0.1, 0.8, 0.55), edge=N(0, -0.5, 0.85))), "inout"),
        K(20, sw_body(pel=(0, 0.06, -0.04), pelr=(-2, 0, -21), sp=(-7, 0, -8), ch=(-9, 0, -10), nk=(4, 0, 16), hd=(-4, 0, 10), fL=FIST_L),
          two_hand(dict(pos=V(-0.23, 0.15, 1.9), blade=N(-0.1, 0.82, 0.52), edge=N(0, -0.5, 0.85))), "smooth"),
        K(24, sw_body(pel=(0, -0.1, -0.1), pelr=(8, 0, 4), sp=(10, 0, 2), spu=(4, 0, 0), ftL=(0, -0.26, 0, 0, 8, 0), fL=FIST_L),
          two_hand(dict(pos=V(-0.1, -0.45, 1.52), blade=N(0.0, -0.6, 0.8), edge=N(0, -0.8, -0.6))), "in"),
        K(27, sw_body(pel=(0, -0.16, -0.2), pelr=(14, 0, 4), sp=(20, 0, 0), spu=(8, 0, 0), ch=(4, 0, 0), nk=(-8, 0, 0), hd=(-6, 0, 0),
                      ftL=(0, -0.34, 0, 0, 5, 0), ftR=(0, 0.12, 0, 28, -15, 28), fL=FIST_L),
          two_hand(dict(pos=V(-0.06, -0.55, 0.96), blade=N(0.0, -0.7, -0.72), edge=N(0, 0.6, -0.8))), "out"),
        K(32, sw_body(pel=(0, -0.16, -0.21), pelr=(14, 0, 4), sp=(22, 0, 0), spu=(8, 0, 0), ch=(4, 0, 0), nk=(-8, 0, 0), hd=(-6, 0, 0),
                      ftL=(0, -0.34, 0, 0, 5, 0), ftR=(0, 0.12, 0, 28, -15, 28), fL=FIST_L),
          two_hand(dict(pos=V(-0.05, -0.42, 0.84), blade=N(0.0, -0.45, -0.9), edge=N(0, 0.9, -0.45))), "smooth"),
        K(46, sw_body(), {"R": SW_READY}, "inout")],
        events=[(16, "OnAttackStart", "sword_heavy"), (23, "OnAttackActive", "sword_heavy"), (26, "OnAttackHit", "sword_heavy"), (30, "OnAttackEnd", "sword_heavy"),
                (24, "OnFootstep", "L")],
        notes="charged two-handed overhead blow: long wind-up, lunge step, heavy follow-through")
    blk = dict(pos=V(-0.18, -0.32, 1.5), blade=N(1, 0.05, 0.14), edge=N(0, -1, 0.3))
    C["Sword_Block"] = dict(frames=40, loop=True, upper=True, keys=[
        K(0, sw_body(pel=(0, 0.02, -0.08), pelr=(4, 0, -8), sp=(4, 0, 0), nk=(-4, 0, 6), hd=(2, 0, 4), fL=(40, 30)), {"R": blk, "L": dict(pos=V(0.16, -0.34, 1.46), blade=UP, edge=F, w_blade=0.3, w_edge=0.1)}),
        K(20, sw_body(pel=(0, 0.02, -0.085), pelr=(4, 0, -8), sp=(4.5, 0, 0), nk=(-4, 0, 6), hd=(2, 0, 4), fL=(40, 30)), {"R": dict(blk, pos=V(-0.18, -0.325, 1.49)), "L": dict(pos=V(0.16, -0.345, 1.45), blade=UP, edge=F, w_blade=0.3, w_edge=0.1)}),
        K(40, sw_body(pel=(0, 0.02, -0.08), pelr=(4, 0, -8), sp=(4, 0, 0), nk=(-4, 0, 6), hd=(2, 0, 4), fL=(40, 30)), {"R": blk, "L": dict(pos=V(0.16, -0.34, 1.46), blade=UP, edge=F, w_blade=0.3, w_edge=0.1)})],
        events=[], notes="blade held across the body in front of the face, off hand guarding")
    back = dict(pos=V(-0.18, 0.05, 1.68), blade=N(0.3, 0.35, -0.88), edge=N(0, 1, 0))
    rest = K(0, P(), {})
    C["Sword_Equip"] = dict(frames=22, loop=False, upper=True, keys=[
        rest,
        K(8, P(ch=(0, 0, -10), hd=(0, 0, -12), fR=(10, 5)), {"R": dict(back, w_edge=0.0)}, "inout"),
        K(10, P(ch=(0, 0, -10), hd=(0, 0, -12)), {"R": dict(back, w_edge=0.0)}, "smooth"),
        K(15, sw_body(ch=(0, 0, -6)), {"R": dict(pos=V(-0.32, -0.12, 1.66), blade=N(-0.1, 0.3, 0.95), edge=F)}, "inout"),
        K(22, sw_body(), {"R": SW_READY}, "inout")],
        events=[(9, "OnEquip", "draw")], notes="reach over the right shoulder, draw the sword from the back, bring it to the ready pose")
    C["Sword_Unequip"] = dict(frames=22, loop=False, upper=True, keys=[
        K(0, sw_body(), {"R": SW_READY}),
        K(7, sw_body(ch=(0, 0, -6)), {"R": dict(pos=V(-0.32, -0.12, 1.66), blade=N(-0.1, 0.3, 0.95), edge=F)}, "inout"),
        K(13, P(ch=(0, 0, -10), hd=(0, 0, -12)), {"R": dict(back, w_edge=0.0)}, "inout"),
        K(22, P(), {}, "inout")],
        events=[(13, "OnEquip", "sheathe")], notes="sword back over the right shoulder onto the back, hand returns")
    # ---------------- spear (rear hand underhand, front hand overhand on the shaft)
    sd0 = N(0.04, -1, 0.2)
    sp_ready = K(0, sp_body(), spear_pair(V(-0.2, -0.12, 1.0), sd0))
    C["Attack_Spear"] = dict(frames=30, loop=False, keys=[
        sp_ready,
        K(8, sp_body(pel=(0, 0.05, -0.05), pelr=(2, 0, -34), ch=(-4, 0, -18), nk=(0, 0, 28), hd=(0, 0, 20)), spear_pair(V(-0.22, 0.06, 1.04), N(0.04, -1, 0.16)), "inout"),
        K(12, sp_body(pel=(0, -0.1, -0.06), pelr=(6, 0, -4), sp=(6, 0, 4), ch=(4, 0, 6), nk=(0, 0, 2), hd=(0, 0, 2), ftL=(0, -0.2, 0, 0, 10, 0), ftR=(0, 0.12, 0, 25, -20, 25)),
          spear_pair(V(-0.14, -0.38, 1.1), N(0.02, -1, 0.08), front=0.3), "in"),
        K(16, sp_body(pel=(0, -0.1, -0.06), pelr=(6, 0, -2), sp=(6, 0, 5), ch=(4, 0, 7), nk=(0, 0, 0), hd=(0, 0, 0), ftL=(0, -0.2, 0, 0, 10, 0), ftR=(0, 0.12, 0, 25, -20, 25)),
          spear_pair(V(-0.13, -0.4, 1.1), N(0.02, -1, 0.07), front=0.3), "out"),
        K(30, sp_body(), spear_pair(V(-0.2, -0.12, 1.0), sd0), "inout")],
        events=[(8, "OnAttackStart", "spear_thrust"), (11, "OnAttackActive", "spear_thrust"), (12, "OnAttackHit", "spear_thrust"), (17, "OnAttackEnd", "spear_thrust")],
        notes="two-handed thrust: pull back, drive forward from the hips, recover")
    C["Spear_Attack_2"] = dict(frames=34, loop=False, keys=[
        sp_ready,
        K(10, sp_body(pelr=(0, 0, -16), ch=(-8, 0, -6), nk=(4, 0, 16), hd=(-6, 0, 10)), spear_pair(V(-0.22, 0.02, 1.62), N(0.02, -1, -0.3), front=0.38), "inout"),
        K(14, sp_body(pel=(0, -0.08, -0.12), pelr=(14, 0, 0), sp=(14, 0, 0), ch=(8, 0, 0), nk=(-6, 0, 0), hd=(-6, 0, 0), ftR=(0, 0.1, 0, 20, -20, 20)),
          spear_pair(V(-0.12, -0.42, 1.2), N(0.02, -0.75, -0.66), front=0.36), "in"),
        K(18, sp_body(pel=(0, -0.08, -0.13), pelr=(15, 0, 0), sp=(16, 0, 0), ch=(8, 0, 0), nk=(-6, 0, 0), hd=(-6, 0, 0), ftR=(0, 0.1, 0, 20, -20, 20)),
          spear_pair(V(-0.11, -0.44, 1.14), N(0.02, -0.7, -0.71), front=0.36), "out"),
        K(34, sp_body(), spear_pair(V(-0.2, -0.12, 1.0), sd0), "inout")],
        events=[(10, "OnAttackStart", "spear_downstab"), (13, "OnAttackActive", "spear_downstab"), (14, "OnAttackHit", "spear_downstab"), (19, "OnAttackEnd", "spear_downstab")],
        notes="combo follow-up: spear raised to the shoulder, downward stab, recovery")
    C["Attack_Spear_Heavy"] = dict(frames=46, loop=False, keys=[
        sp_ready,
        K(14, sp_body(pel=(0, 0.08, -0.03), pelr=(-4, 0, -36), sp=(-4, 0, -12), ch=(-8, 0, -18), nk=(0, 0, 30), hd=(0, 0, 22)), spear_pair(V(-0.24, 0.16, 1.04), N(0.04, -1, 0.14)), "inout"),
        K(18, sp_body(pel=(0, 0.08, -0.03), pelr=(-4, 0, -37), sp=(-4, 0, -12), ch=(-8, 0, -19), nk=(0, 0, 30), hd=(0, 0, 22)), spear_pair(V(-0.25, 0.17, 1.04), N(0.04, -1, 0.14)), "smooth"),
        K(21, sp_body(pel=(0, -0.04, -0.04), pelr=(2, 0, -24), ftL=(0, -0.16, 0.07, -10, 10, 0)), spear_pair(V(-0.2, -0.1, 1.06), N(0.03, -1, 0.12)), "in"),
        K(24, sp_body(pel=(0, -0.24, -0.13), pelr=(12, 0, 4), sp=(10, 0, 6), ch=(6, 0, 8), nk=(0, 0, -2), hd=(0, 0, -2), ftL=(0, -0.3, 0, 0, 5, 0), ftR=(0, 0.06, 0, 35, -15, 35)),
          spear_pair(V(-0.12, -0.6, 1.05), N(0.02, -1, 0.05), front=0.28), "out"),
        K(29, sp_body(pel=(0, -0.24, -0.13), pelr=(12, 0, 5), sp=(12, 0, 6), ch=(6, 0, 8), nk=(0, 0, -2), hd=(0, 0, -2), ftL=(0, -0.3, 0, 0, 5, 0), ftR=(0, 0.06, 0, 35, -15, 35)),
          spear_pair(V(-0.12, -0.62, 1.04), N(0.02, -1, 0.04), front=0.28), "smooth"),
        K(38, sp_body(ftL=(0, -0.16, 0.05, 0, 10, 0)), spear_pair(V(-0.2, -0.14, 1.0), sd0), "inout"),
        K(46, sp_body(), spear_pair(V(-0.2, -0.12, 1.0), sd0), "inout")],
        events=[(14, "OnAttackStart", "spear_heavy"), (21, "OnFootstep", "L"), (22, "OnAttackActive", "spear_heavy"), (24, "OnAttackHit", "spear_heavy"), (30, "OnAttackEnd", "spear_heavy")],
        notes="wind-up, lunge step, full-body two-handed thrust")
    C["Throw_Spear"] = dict(frames=46, loop=False, keys=[
        sp_ready,
        K(14, sp_body(pelr=(-2, 0, -34), ch=(-8, 0, -22), nk=(0, 0, 32), hd=(0, 0, 24), aL=(70, -5, 0, 20), eL=10, fL=(10, 5)),
          {"R": dict(pos=V(-0.26, 0.22, 1.66), blade=N(0.02, -1, 0.25), edge=N(0, 0, 1), w_edge=0.1)}, "inout"),
        K(20, sp_body(pelr=(-2, 0, -35), ch=(-8, 0, -23), nk=(0, 0, 33), hd=(0, 0, 25), aL=(72, -5, 0, 20), eL=8, fL=(10, 5)),
          {"R": dict(pos=V(-0.27, 0.24, 1.67), blade=N(0.02, -1, 0.26), edge=N(0, 0, 1), w_edge=0.1)}, "smooth"),
        K(24, sp_body(pel=(0, -0.12, -0.04), pelr=(10, 0, 20), sp=(10, 0, 12), ch=(8, 0, 18), nk=(0, 0, -16), hd=(0, 0, -12), ftR=(0, 0.0, 0, 30, -15, 30),
                      aL=(20, -30, 0, -10), eL=30, fR=(5, 5)),
          {"R": dict(pos=V(-0.14, -0.5, 1.5), blade=N(0.0, -1, 0.02), w_edge=0.0)}, "in"),
        K(28, sp_body(pel=(0, -0.12, -0.05), pelr=(10, 0, 22), sp=(14, 0, 12), ch=(8, 0, 20), nk=(0, 0, -18), hd=(0, 0, -14), ftR=(0, 0.0, 0, 30, -15, 30),
                      aL=(15, -30, 0, -10), eL=30, fR=(10, 5)),
          {"R": dict(pos=V(-0.08, -0.46, 1.2), blade=N(0.3, -0.7, -0.6), w_edge=0.0)}, "out"),
        K(46, P(), {}, "inout")],
        events=[(24, "OnThrowRelease", "spear")], notes="overhand throw: spear drawn back over the shoulder, off arm points, whip release")
    # ---------------- bow (left hand holds the bow)
    bow_low = dict(pos=V(0.16, -0.46, 1.16), blade=N(0.25, -0.4, 0.88), edge=N(0, -1, -0.2))
    nock_low = dict(pos=V(0.06, -0.36, 1.2), blade=N(0.2, -0.2, 0.95), edge=N(0.3, -1, 0))
    bow_up = dict(pos=V(0.12, -0.66, 1.46), blade=N(0.18, 0, 1), edge=F)
    anchor = dict(pos=V(-0.05, -0.1, 1.58), blade=N(0.1, 0.1, 1), edge=N(0.2, -1, 0.1), w_edge=0.3)
    C["Bow_Aim"] = dict(frames=60, loop=True, upper=True, keys=[
        K(0, bow_body(), {"L": bow_low, "R": nock_low}),
        K(30, bow_body(spu=(1, 0, 0)), {"L": dict(bow_low, pos=V(0.16, -0.465, 1.17)), "R": dict(nock_low, pos=V(0.06, -0.365, 1.21))}),
        K(60, bow_body(), {"L": bow_low, "R": nock_low})], events=[], notes="bow held low in the left hand, arrow nocked, ready to raise")
    drawn_body = bow_body(ch=(-2, 0, -22), nk=(0, 0, 34), hd=(-2, 0, 28))
    C["Bow_Draw"] = dict(frames=26, loop=False, upper=True, keys=[
        K(0, bow_body(), {"L": bow_low, "R": nock_low}),
        K(8, bow_body(ch=(-1, 0, -20)), {"L": dict(bow_up, pos=V(0.13, -0.6, 1.4)), "R": dict(pos=V(0.02, -0.42, 1.42), blade=UP, edge=F)}, "inout"),
        K(22, drawn_body, {"L": bow_up, "R": anchor}, "inout"),
        K(26, drawn_body, {"L": dict(bow_up, pos=V(0.12, -0.661, 1.461)), "R": dict(anchor, pos=V(-0.051, -0.1, 1.58))})],
        events=[(4, "OnBowDrawStart", "")], notes="raise the bow, push the bow arm out, draw the string to the cheek")
    rel = dict(pos=V(-0.11, 0.05, 1.61), blade=UP, edge=N(0, -1, 0), w_edge=0.2)
    C["Bow_Release"] = dict(frames=28, loop=False, upper=True, keys=[
        K(0, drawn_body, {"L": bow_up, "R": anchor}),
        K(3, add(drawn_body, fR=(-40, -27)), {"L": dict(bow_up, pos=V(0.13, -0.68, 1.47)), "R": rel}, "out"),
        K(10, add(drawn_body, fR=(-40, -27)), {"L": dict(bow_up, pos=V(0.13, -0.675, 1.465)), "R": dict(rel, pos=V(-0.12, 0.06, 1.6))}, "smooth"),
        K(28, bow_body(), {"L": bow_low, "R": nock_low}, "inout")],
        events=[(1, "OnBowRelease", "")], notes="string released: draw hand flies back past the ear, bow arm holds, settle")
    return C

OVERRIDES = ("Attack_Spear", "Spear_Attack_2", "Attack_Spear_Heavy", "Throw_Spear", "Bow_Aim", "Bow_Draw", "Bow_Release")

_cache = {}
def _sig(body, targets):
    def r(v):
        if isinstance(v, Vector): return tuple(round(x, 5) for x in v)
        if isinstance(v, (tuple, list)): return tuple(round(x, 5) if isinstance(x, float) else x for x in v)
        return round(v, 5) if isinstance(v, float) else v
    return repr((sorted((k, r(v)) for k, v in body.items()), sorted((sd, sorted((k, r(v)) for k, v in t.items())) for sd, t in targets.items())))

def solve_keys(rig, keys):
    """keys are solved in order; each key starts from the previous key's arm solution (same IK branch, no flips), and
    a key identical to an earlier one reuses its solution exactly (seamless loops)"""
    out = []; infos = []; memo = {}; prev = None
    for frame, body, targets, e in keys:
        if not targets:
            out.append((frame, body, e)); infos.append((frame, {})); prev = body; continue
        sig = _sig(body, targets)
        if sig in memo:
            pose, info = memo[sig]
        else:
            start = dict(body)
            if prev is not None:
                for sd in targets:
                    for k in ("a", "e", "w"): start[k + sd] = prev[k + sd]
            pose, info = WI.posed(rig, start, targets)
            memo[sig] = (pose, info)
        out.append((frame, pose, e)); infos.append((frame, info)); prev = pose
    return out, infos

def _targets_at(keys_raw, f):
    """weapon targets interpolated in weapon space (grip position lerp, directions nlerp) with the same easing as
    the pose interpolation; a side is only targeted when both surrounding keys target it"""
    if f <= keys_raw[0][0] or f >= keys_raw[-1][0]: return None, 0.0
    for i in range(len(keys_raw) - 1):
        f0, _, t0, _ = keys_raw[i]; f1, _, t1, e = keys_raw[i + 1]
        if f0 <= f <= f1:
            t = ease((f - f0) / max(1e-6, f1 - f0), e)
            out = {}
            for sd in ("R", "L"):
                a, b = t0.get(sd), t1.get(sd)
                if not a or not b: continue
                d = {}
                for k in ("pos", "blade", "edge"):
                    va, vb = a.get(k), b.get(k)
                    if va is None or vb is None: continue
                    v = va.lerp(vb, t)
                    d[k] = v if k == "pos" else (v.normalized() if v.length > 1e-4 else vb)
                for k in ("w_edge", "w_blade"):
                    if k in a or k in b: d[k] = (a.get(k, 1.0 if k == "w_blade" else 0.35) + b.get(k, 1.0 if k == "w_blade" else 0.35)) * 0.5
                out[sd] = d
            return out, t
    return None, 0.0

def bake(rig, B, which=None):
    """keys are solved exactly; in-between frames start from the joint-space blend (smooth) and are refined towards
    the weapon-space blend, so a spear stays level while it is pulled back and a blade follows its arc"""
    defs = clip_defs(); report = {}
    for name, d in defs.items():
        if which is not None and name not in which: continue
        raw = d["keys"]
        keys, infos = solve_keys(rig, raw)
        report[name] = [(f, {sd: (i.get("pos_err"), i.get("blade_err")) for sd, i in info.items()}) for f, info in infos]
        keyframes = {k[0] for k in raw}
        cache = {}; last = {}
        def fn(f, keys=keys, raw=raw, keyframes=keyframes, cache=cache, last=last):
            p = CH.interp(keys, f)
            if f not in keyframes:
                if f not in cache:
                    tg, _ = _targets_at(raw, f)
                    if tg:
                        rig.reset(); CH.apply(rig, p); bpy.context.view_layer.update()
                        for sd, t in tg.items():
                            # B5 (2026-09-30): continuity. Start between the joint blend and the previous frame's arm, and
                            # keep the joint blend when the refined arm would flip (wrist twist / arm roll / elbow jump)
                            start = dict(p)
                            if sd in last:
                                for k in ("a", "e", "w"): start[k + sd] = lerp(p[k + sd], last[sd][k], 0.5)
                            q, _ = WI.solve(rig, start, sd, reg=0.3, iters=240, starts=1, **t)
                            jump = max(abs(q["w" + sd][2] - p["w" + sd][2]), abs(q["a" + sd][2] - p["a" + sd][2]), abs(q["e" + sd] - p["e" + sd]) * 0.5)
                            w = max(0.0, min(1.0, (45.0 - jump) / 25.0))       # fades out, never switches
                            kf_ = sorted(keyframes); seg = [(a, b) for a, b in zip(kf_, kf_[1:]) if a < f < b]
                            if seg: w *= math.sin(math.pi * (f - seg[0][0]) / (seg[0][1] - seg[0][0]))   # and near the keys
                            for k in ("a", "e", "w"): p[k + sd] = lerp(p[k + sd], q[k + sd], w)
                            rig.reset(); CH.apply(rig, p); bpy.context.view_layer.update()
                    cache[f] = p
                p = cache[f]
                rig.reset()
            for sd in ("L", "R"): last[sd] = {k: p[k + sd] for k in ("a", "e", "w")}
            return CH.apply(rig, p)
        B.bake(name, d["frames"], d["loop"], fn, events=d.get("events", []), notes=d.get("notes", ""))
        report[name + "__max_grip_step_cm"] = _max_step(rig, name, d["frames"])
    return report

def _max_step(rig, name, frames):
    """largest frame-to-frame move of either grip (cm): a pop shows up as an outlier"""
    arm = rig.obj; sc = bpy.context.scene; worst = {"R": 0.0, "L": 0.0}; last = {}
    for f in range(frames + 1):
        sc.frame_set(f)
        for sd in ("R", "L"):
            g = WI.grip_world(rig, sd).translation
            if sd in last: worst[sd] = max(worst[sd], (g - last[sd]).length * 100)
            last[sd] = g.copy()
    return {k: round(v, 1) for k, v in worst.items()}

def key_poses(rig, names):
    """solved key poses for preview: [(label, pose)]"""
    defs = clip_defs(); out = []
    for n in names:
        keys, infos = solve_keys(rig, defs[n]["keys"])
        for (f, pose, e), (_, info) in zip(keys, infos):
            out.append((f"{n}_{f:02d}", pose, info))
    return out
