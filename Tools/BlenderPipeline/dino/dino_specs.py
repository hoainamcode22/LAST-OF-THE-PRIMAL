"""
PRIMAL FRONTIER - original dinosaur designs as parameters (Blender space: metres, creature faces -Y, +Z up,
+X = creature's left, ground z = 0). Shared by the SDF body builder (VM) and the Blender rig/animation scripts.
Joints are explicit so the skeleton and the body come from the same numbers.
"""
import math

def lerp(a, b, t): return a + (b - a) * t

# ---------------------------------------------------------------------------------------------------------------
# helpers to write specs compactly
def chain(points):
    """points: list of (y, z, half_width, half_height); returns dict list"""
    return [dict(y=p[0], z=p[1], w=p[2], h=p[3], drop=(p[4] if len(p) > 4 else 0.0)) for p in points]

SPECS = {}

# ---------------------------------------------------------------- 1. TRICERATOPS (quadruped, 8 m)
SPECS["triceratops"] = dict(
    name="Triceratops", kind="quad", length=8.0, colors=dict(base=(0.34, 0.30, 0.22), belly=(0.62, 0.56, 0.44), dorsal=(0.20, 0.17, 0.12),
        pattern="bands", accent=(0.45, 0.22, 0.12), scale=0.045, bumps=0.8),
    # tail tip -> pelvis -> spine -> chest -> neck base
    tail=chain([(4.9, 0.85, 0.05, 0.05), (4.2, 1.05, 0.12, 0.13), (3.4, 1.35, 0.22, 0.26), (2.6, 1.65, 0.36, 0.42), (1.8, 1.95, 0.55, 0.62)]),
    body=chain([(1.0, 2.15, 0.76, 0.74, 0.05), (0.2, 2.2, 0.92, 0.84, 0.12), (-0.6, 2.15, 0.9, 0.82, 0.1), (-1.3, 1.98, 0.76, 0.72, 0.05)]),
    neck=chain([(-1.85, 1.85, 0.6, 0.62), (-2.25, 1.8, 0.5, 0.55)]),
    head=dict(pos=(0, -2.5, 1.78), pitch=-18, len=1.9, w=0.5, h=0.7, snout=0.22, beak=True, mouth_depth=0.55,
              horns=[dict(at=(0.24, 0.6, 0.34), dir=(0.22, 0.75, 0.62), len=1.0, r=0.12, curve=-0.25),
                     dict(at=(-0.24, 0.6, 0.34), dir=(-0.22, 0.75, 0.62), len=1.0, r=0.12, curve=-0.25),
                     dict(at=(0.0, 1.5, 0.2), dir=(0, 0.35, 0.94), len=0.3, r=0.09, curve=0.0)],
              frill=dict(at=(0, 0.15, 0.5), size=(1.1, 1.0), tilt=38, thick=0.08, spikes=11),
              eye=dict(at=(0.3, 0.72, 0.28), r=0.055), teeth=0),
    legs=dict(hind=dict(hip=(0.55, 1.0, 1.95), knee=(0.62, 0.55, 1.05), ankle=(0.6, 1.05, 0.38), foot=(0.6, 0.85, 0.0), toe_len=0.32,
                        r=(0.5, 0.3, 0.21, 0.17), stance="plantigrade"),
              front=dict(hip=(0.58, -1.25, 1.75), knee=(0.72, -1.15, 0.95), ankle=(0.68, -1.3, 0.32), foot=(0.68, -1.38, 0.0), toe_len=0.22,
                         r=(0.4, 0.25, 0.19, 0.15), stance="plantigrade")),
    claws=dict(hind=3, front=4, size=0.07, hoof=True),
    armor=None, sail=None, feathers=False,
    speeds=dict(walk=1.6, run=5.5), gait=dict(walk=1.1, run=0.75, duty_walk=0.68, duty_run=0.42, lift=0.18),
)

# ---------------------------------------------------------------- 2. PARASAUROLOPHUS (semi-bipedal, 9 m)
SPECS["parasaurolophus"] = dict(
    name="Parasaurolophus", kind="quad", length=9.0, colors=dict(base=(0.52, 0.40, 0.24), belly=(0.78, 0.70, 0.52), dorsal=(0.30, 0.22, 0.13),
        pattern="stripes", accent=(0.55, 0.20, 0.12), scale=0.035, bumps=0.5),
    tail=chain([(5.4, 1.2, 0.04, 0.05), (4.5, 1.5, 0.1, 0.16), (3.5, 1.9, 0.2, 0.3), (2.6, 2.3, 0.32, 0.45), (1.7, 2.6, 0.45, 0.58)]),
    body=chain([(1.0, 2.75, 0.58, 0.68, 0.05), (0.2, 2.75, 0.68, 0.8, 0.15), (-0.6, 2.6, 0.62, 0.72, 0.1), (-1.2, 2.45, 0.5, 0.55)]),
    neck=chain([(-1.7, 2.6, 0.34, 0.38), (-2.1, 2.9, 0.26, 0.3), (-2.35, 3.2, 0.22, 0.25)]),
    head=dict(pos=(0, -2.55, 3.35), pitch=-30, len=1.05, w=0.2, h=0.34, snout=0.12, beak=True, duckbill=True, mouth_depth=0.3,
              crest=dict(len=1.5, r=0.085, rise=0.25), eye=dict(at=(0.14, 0.35, 0.16), r=0.04), teeth=0),
    legs=dict(hind=dict(hip=(0.42, 0.8, 2.55), knee=(0.48, 0.25, 1.4), ankle=(0.45, 0.85, 0.42), foot=(0.45, 0.6, 0.0), toe_len=0.34,
                        r=(0.36, 0.2, 0.13, 0.1), stance="digitigrade"),
              front=dict(hip=(0.36, -1.0, 2.2), knee=(0.42, -1.05, 1.35), ankle=(0.4, -1.2, 0.5), foot=(0.4, -1.28, 0.0), toe_len=0.14,
                         r=(0.2, 0.13, 0.1, 0.08), stance="plantigrade")),
    claws=dict(hind=3, front=3, size=0.05, hoof=True), armor=None, sail=None, feathers=False,
    speeds=dict(walk=1.7, run=7.0), gait=dict(walk=1.15, run=0.7, duty_walk=0.65, duty_run=0.38, lift=0.2),
)

# ---------------------------------------------------------------- 3. ANKYLOSAURUS (low, wide, 6.5 m)
SPECS["ankylosaurus"] = dict(
    name="Ankylosaurus", kind="quad", length=6.5, colors=dict(base=(0.38, 0.33, 0.24), belly=(0.58, 0.52, 0.40), dorsal=(0.26, 0.22, 0.16),
        pattern="mottled", accent=(0.5, 0.42, 0.3), scale=0.05, bumps=1.0),
    tail=chain([(3.6, 0.7, 0.07, 0.06), (3.0, 0.8, 0.12, 0.1), (2.3, 0.95, 0.24, 0.2), (1.6, 1.1, 0.42, 0.32)]),
    body=chain([(0.9, 1.2, 0.85, 0.5, 0.02), (0.1, 1.28, 1.05, 0.55, 0.08), (-0.7, 1.25, 1.0, 0.52, 0.06), (-1.3, 1.12, 0.78, 0.44)]),
    neck=chain([(-1.75, 1.02, 0.5, 0.34), (-2.05, 0.98, 0.42, 0.3)]),
    head=dict(pos=(0, -2.25, 0.98), pitch=-10, len=0.62, w=0.36, h=0.3, snout=0.2, beak=True, mouth_depth=0.2,
              horns=[dict(at=(0.3, 0.52, 0.2), dir=(0.8, 0.3, 0.1), len=0.2, r=0.07, curve=0.0), dict(at=(-0.3, 0.52, 0.2), dir=(-0.8, 0.3, 0.1), len=0.2, r=0.07, curve=0.0),
                     dict(at=(0.28, 0.3, -0.05), dir=(0.7, 0.4, -0.5), len=0.16, r=0.06, curve=0.0), dict(at=(-0.28, 0.3, -0.05), dir=(-0.7, 0.4, -0.5), len=0.16, r=0.06, curve=0.0)],
              eye=dict(at=(0.27, 0.35, 0.1), r=0.035), teeth=0),
    legs=dict(hind=dict(hip=(0.62, 0.8, 1.05), knee=(0.75, 0.55, 0.58), ankle=(0.75, 0.8, 0.22), foot=(0.75, 0.7, 0.0), toe_len=0.2,
                        r=(0.3, 0.2, 0.15, 0.12), stance="plantigrade"),
              front=dict(hip=(0.62, -1.15, 0.98), knee=(0.8, -1.1, 0.52), ankle=(0.78, -1.2, 0.2), foot=(0.78, -1.26, 0.0), toe_len=0.16,
                         r=(0.24, 0.17, 0.13, 0.11), stance="plantigrade")),
    claws=dict(hind=4, front=5, size=0.05, hoof=True),
    armor=dict(rows=6, spacing=0.36, size=0.13, side_spikes=True, club=dict(r=(0.34, 0.42, 0.2))), sail=None, feathers=False,
    speeds=dict(walk=1.2, run=3.2), gait=dict(walk=1.0, run=0.7, duty_walk=0.7, duty_run=0.5, lift=0.12),
)

# ---------------------------------------------------------------- 4. VELOCIRAPTOR (biped, 2 m, feathered)
SPECS["velociraptor"] = dict(
    name="Velociraptor", kind="biped", length=2.1, colors=dict(base=(0.42, 0.33, 0.24), belly=(0.72, 0.64, 0.5), dorsal=(0.24, 0.17, 0.12),
        pattern="stripes", accent=(0.66, 0.36, 0.16), scale=0.012, bumps=0.4),
    tail=chain([(1.25, 0.46, 0.012, 0.014), (1.0, 0.48, 0.02, 0.025), (0.72, 0.5, 0.035, 0.045), (0.42, 0.53, 0.06, 0.07), (0.18, 0.56, 0.085, 0.1)]),
    body=chain([(0.0, 0.58, 0.1, 0.12, 0.01), (-0.18, 0.6, 0.11, 0.13, 0.02), (-0.34, 0.63, 0.09, 0.11)]),
    neck=chain([(-0.44, 0.7, 0.05, 0.055), (-0.5, 0.8, 0.04, 0.045), (-0.55, 0.88, 0.035, 0.04)]),
    head=dict(pos=(0, -0.6, 0.9), pitch=-8, len=0.24, w=0.045, h=0.06, snout=0.02, beak=False, mouth_depth=0.12,
              eye=dict(at=(0.035, 0.07, 0.03), r=0.013), teeth=26, tooth=0.01),
    legs=dict(hind=dict(hip=(0.07, 0.02, 0.55), knee=(0.085, -0.1, 0.32), ankle=(0.08, 0.06, 0.12), foot=(0.08, -0.02, 0.0), toe_len=0.07,
                        r=(0.07, 0.035, 0.018, 0.014), stance="digitigrade"),
              arm=dict(hip=(0.075, -0.33, 0.6), knee=(0.1, -0.3, 0.46), ankle=(0.09, -0.42, 0.44), foot=(0.09, -0.48, 0.43), toe_len=0.05,
                       r=(0.028, 0.02, 0.014, 0.01), stance="hand")),
    claws=dict(hind=3, front=3, size=0.022, sickle=True, hoof=False), armor=None, sail=None, feathers=True,
    speeds=dict(walk=1.5, run=11.0), gait=dict(walk=0.6, run=0.36, duty_walk=0.6, duty_run=0.3, lift=0.36),
)

# ---------------------------------------------------------------- 5. CARNOTAURUS (biped, 8 m)
SPECS["carnotaurus"] = dict(
    name="Carnotaurus", kind="biped", length=8.0, colors=dict(base=(0.40, 0.23, 0.16), belly=(0.66, 0.52, 0.40), dorsal=(0.24, 0.12, 0.08),
        pattern="bands", accent=(0.55, 0.16, 0.1), scale=0.04, bumps=1.0),
    tail=chain([(4.4, 1.55, 0.04, 0.05), (3.6, 1.75, 0.1, 0.14), (2.7, 1.95, 0.2, 0.28), (1.8, 2.1, 0.32, 0.42), (0.9, 2.25, 0.45, 0.55)]),
    body=chain([(0.1, 2.35, 0.55, 0.62, 0.05), (-0.7, 2.4, 0.58, 0.68, 0.12), (-1.4, 2.5, 0.48, 0.58)]),
    neck=chain([(-1.85, 2.72, 0.34, 0.4), (-2.15, 2.95, 0.3, 0.34)]),
    head=dict(pos=(0, -2.35, 3.05), pitch=-6, len=0.85, w=0.26, h=0.42, snout=0.14, beak=False, mouth_depth=0.3,
              horns=[dict(at=(0.14, 0.62, 0.3), dir=(0.85, 0.2, 0.35), len=0.26, r=0.075, curve=0.1), dict(at=(-0.14, 0.62, 0.3), dir=(-0.85, 0.2, 0.35), len=0.26, r=0.075, curve=0.1)],
              eye=dict(at=(0.15, 0.55, 0.2), r=0.035), teeth=34, tooth=0.04),
    legs=dict(hind=dict(hip=(0.42, 0.25, 2.25), knee=(0.5, -0.3, 1.25), ankle=(0.46, 0.35, 0.38), foot=(0.46, 0.05, 0.0), toe_len=0.38,
                        r=(0.42, 0.2, 0.11, 0.09), stance="digitigrade"),
              arm=dict(hip=(0.38, -1.35, 2.2), knee=(0.44, -1.3, 2.0), ankle=(0.42, -1.4, 1.92), foot=(0.42, -1.46, 1.9), toe_len=0.05,
                       r=(0.07, 0.05, 0.04, 0.03), stance="hand")),
    claws=dict(hind=3, front=4, size=0.08, hoof=False), armor=dict(rows=2, spacing=0.3, size=0.06, side_spikes=False, club=None), sail=None, feathers=False,
    speeds=dict(walk=1.8, run=9.5), gait=dict(walk=1.0, run=0.55, duty_walk=0.62, duty_run=0.34, lift=0.25),
)

# ---------------------------------------------------------------- 6. SPINOSAURUS (biped, long snout, sail, 13 m)
SPECS["spinosaurus"] = dict(
    name="Spinosaurus", kind="biped", length=13.0, colors=dict(base=(0.27, 0.30, 0.30), belly=(0.62, 0.60, 0.52), dorsal=(0.15, 0.17, 0.18),
        pattern="stripes", accent=(0.62, 0.24, 0.14), scale=0.05, bumps=0.9),
    tail=chain([(7.2, 1.7, 0.03, 0.1), (6.0, 1.95, 0.08, 0.34), (4.6, 2.3, 0.2, 0.55), (3.2, 2.7, 0.35, 0.6), (1.8, 3.05, 0.55, 0.75)]),
    body=chain([(0.7, 3.3, 0.72, 0.85, 0.05), (-0.4, 3.35, 0.78, 0.9, 0.12), (-1.5, 3.3, 0.66, 0.78)]),
    neck=chain([(-2.2, 3.6, 0.4, 0.46), (-2.8, 3.95, 0.32, 0.38), (-3.25, 4.1, 0.28, 0.32)]),
    head=dict(pos=(0, -3.45, 4.12), pitch=-12, len=1.7, w=0.2, h=0.3, snout=0.08, beak=False, croc=True, mouth_depth=0.22, jaw_k=0.5,
              eye=dict(at=(0.13, 1.2, 0.2), r=0.04), teeth=50, tooth=0.045),
    legs=dict(hind=dict(hip=(0.55, 0.6, 3.1), knee=(0.62, 0.0, 1.8), ankle=(0.58, 0.7, 0.5), foot=(0.58, 0.35, 0.0), toe_len=0.45,
                        r=(0.5, 0.26, 0.14, 0.11), stance="digitigrade"),
              arm=dict(hip=(0.5, -1.6, 3.05), knee=(0.6, -1.7, 2.35), ankle=(0.56, -2.0, 1.95), foot=(0.56, -2.15, 1.8), toe_len=0.2,
                       r=(0.16, 0.12, 0.09, 0.07), stance="hand")),
    claws=dict(hind=3, front=3, size=0.12, hoof=False), armor=None,
    sail=dict(start=1.9, end=-1.6, height=1.9, thick=0.05), feathers=False,
    speeds=dict(walk=1.9, run=7.5), gait=dict(walk=1.25, run=0.7, duty_walk=0.64, duty_run=0.36, lift=0.3),
)

# ---------------------------------------------------------------- 7. APEX (original tyrannosaur-type, 12 m)
SPECS["apex"] = dict(
    name="Rift Tyrant", kind="biped", length=12.0, colors=dict(base=(0.40, 0.33, 0.24), belly=(0.64, 0.56, 0.44), dorsal=(0.20, 0.15, 0.11),
        pattern="bands", accent=(0.45, 0.1, 0.06), scale=0.05, bumps=1.2),
    tail=chain([(6.4, 2.3, 0.05, 0.06), (5.3, 2.65, 0.14, 0.18), (4.1, 3.0, 0.28, 0.36), (2.9, 3.3, 0.45, 0.58), (1.7, 3.55, 0.66, 0.8)]),
    body=chain([(0.6, 3.7, 0.92, 1.0, 0.08), (-0.5, 3.75, 1.0, 1.12, 0.2), (-1.5, 3.8, 0.85, 1.0)]),
    neck=chain([(-2.2, 4.15, 0.62, 0.72), (-2.65, 4.5, 0.55, 0.62)]),
    head=dict(pos=(0, -2.95, 4.62), pitch=-8, len=1.55, w=0.46, h=0.72, snout=0.2, beak=False, mouth_depth=0.55, brow=True,
              horns=[dict(at=(0.26, 0.45, 0.46), dir=(0.5, 0.2, 0.84), len=0.16, r=0.08, curve=0.0), dict(at=(-0.26, 0.45, 0.46), dir=(-0.5, 0.2, 0.84), len=0.16, r=0.08, curve=0.0)],
              eye=dict(at=(0.3, 0.46, 0.34), r=0.045), teeth=40, tooth=0.08),
    legs=dict(hind=dict(hip=(0.62, 0.3, 3.5), knee=(0.72, -0.35, 2.0), ankle=(0.66, 0.5, 0.6), foot=(0.66, 0.1, 0.0), toe_len=0.55,
                        r=(0.66, 0.32, 0.17, 0.13), stance="digitigrade"),
              arm=dict(hip=(0.58, -1.6, 3.3), knee=(0.66, -1.62, 2.95), ankle=(0.62, -1.82, 2.8), foot=(0.62, -1.9, 2.74), toe_len=0.08,
                       r=(0.12, 0.09, 0.07, 0.05), stance="hand")),
    claws=dict(hind=3, front=2, size=0.12, hoof=False), armor=dict(rows=2, spacing=0.4, size=0.06, side_spikes=False, club=None), sail=None, feathers=False,
    speeds=dict(walk=2.0, run=7.0), gait=dict(walk=1.3, run=0.78, duty_walk=0.64, duty_run=0.4, lift=0.35),
)

# ---------------------------------------------------------------- 8. PTERANODON (flyer, 6.5 m wingspan)
SPECS["pteranodon"] = dict(
    name="Pteranodon", kind="flyer", length=1.8, colors=dict(base=(0.55, 0.42, 0.28), belly=(0.82, 0.76, 0.64), dorsal=(0.34, 0.24, 0.15),
        pattern="plain", accent=(0.7, 0.32, 0.14), scale=0.01, bumps=0.2),
    tail=chain([(0.55, 0.62, 0.012, 0.012), (0.38, 0.64, 0.03, 0.03), (0.2, 0.66, 0.07, 0.07)]),
    body=chain([(0.05, 0.68, 0.11, 0.1), (-0.15, 0.7, 0.13, 0.12, 0.01), (-0.32, 0.72, 0.11, 0.1)]),
    neck=chain([(-0.45, 0.78, 0.06, 0.06), (-0.55, 0.86, 0.05, 0.05)]),
    head=dict(pos=(0, -0.62, 0.9), pitch=-5, len=0.95, w=0.05, h=0.08, snout=0.01, beak=True, mouth_depth=0.05, pointy=True,
              crest=dict(len=0.55, r=0.03, rise=0.2, flat=True), eye=dict(at=(0.04, 0.12, 0.03), r=0.015), teeth=0),
    legs=dict(hind=dict(hip=(0.07, 0.12, 0.65), knee=(0.09, 0.18, 0.42), ankle=(0.09, 0.26, 0.12), foot=(0.09, 0.22, 0.0), toe_len=0.07,
                        r=(0.04, 0.025, 0.018, 0.014), stance="plantigrade"),
              wing=dict(shoulder=(0.12, -0.32, 0.76), elbow=(0.55, -0.25, 0.76), wrist=(1.05, -0.3, 0.76), knuckle=(1.25, -0.32, 0.76), tip=(3.25, -0.05, 0.76),
                        r=(0.05, 0.035, 0.025, 0.012))),
    claws=dict(hind=4, front=3, size=0.02, hoof=False), armor=None, sail=None, feathers=False,
    speeds=dict(walk=0.8, run=2.0, fly=12.0), gait=dict(walk=0.8, run=0.5, duty_walk=0.65, duty_run=0.4, lift=0.06),
)

# ---------------------------------------------------------------- 9. MOSASAURUS (aquatic, 12 m)
SPECS["mosasaurus"] = dict(
    name="Mosasaurus", kind="swimmer", length=12.0, colors=dict(base=(0.20, 0.26, 0.30), belly=(0.72, 0.74, 0.7), dorsal=(0.10, 0.13, 0.16),
        pattern="countershade", accent=(0.3, 0.36, 0.4), scale=0.035, bumps=0.4),
    tail=chain([(6.4, 0.0, 0.05, 0.5), (5.2, 0.0, 0.16, 0.4), (3.8, 0.0, 0.32, 0.45), (2.4, 0.0, 0.55, 0.6), (1.2, 0.0, 0.75, 0.8)]),
    body=chain([(0.2, 0.0, 0.9, 0.95), (-0.9, 0.0, 0.92, 0.95), (-2.0, 0.0, 0.78, 0.8)]),
    neck=chain([(-2.9, 0.05, 0.62, 0.6), (-3.5, 0.1, 0.52, 0.5)]),
    head=dict(pos=(0, -3.8, 0.12), pitch=0, len=1.9, w=0.42, h=0.5, snout=0.12, beak=False, mouth_depth=0.4, jaw_k=0.7,
              eye=dict(at=(0.32, 0.6, 0.22), r=0.05), teeth=44, tooth=0.07),
    legs=dict(fin_front=dict(root=(0.7, -1.9, -0.3), tip=(1.9, -1.2, -0.75), w=0.45),
              fin_hind=dict(root=(0.6, 0.9, -0.3), tip=(1.4, 1.5, -0.65), w=0.32)),
    claws=None, armor=None, sail=None, feathers=False, fluke=dict(h=1.3, w=0.9),
    speeds=dict(walk=2.5, run=8.0), gait=dict(walk=2.0, run=1.1),
)

ORDER = ["triceratops", "parasaurolophus", "ankylosaurus", "velociraptor", "carnotaurus", "spinosaurus", "apex", "pteranodon", "mosasaurus"]
