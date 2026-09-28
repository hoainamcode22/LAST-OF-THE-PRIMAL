# Motion principles reference (LAST OF THE PRIMAL, human survivor)

Purpose: measurable targets for the animator / rigger, taken from observing real human movement (biomechanics data) and
animation craft. Principles only, no copied clips. Fixes the problems in `_video_findings.md` (V1 high flared arms,
V2 wrist roll, V3 turns without pivots, V4/V7 snapping transitions, V8 stiff walk) and prepares unarmed combat.

Conventions
- Frame counts are at **30 fps** unless stated. 24 fps sources are converted (x1.25).
- ROM = range of motion, peak to peak. "Amplitude" is kept as the source wrote it.
- Tags: **[n]** = source number in the Sources list (all opened by the researcher). **GK** = general knowledge, not
  verified in an opened source this session. **Derived** = computed here from our own numbers (formula given).
- Our motor (src/Scripts/Player/PlayerMotor.cs): walk 1.35, run 3.8, sprint 6.2, crouch 0.95 m/s; accel 10 m/s2,
  decel 14 m/s2, turnSpeed 600 deg/s. Character height 1.8 m.

---

## 1. Blender rigging and animation rules (arms first)

### 1.1 Bone roll and local axes
| Fact (Blender manual) | Source |
|---|---|
| Local Y always runs along the bone, head to tail; Y is the roll axis. X and Z orientation is set by Roll. | [1] |
| Roll = "Bone rotation around head-tail axis" (Edit Mode, Transform panel). | [2] |
| Recalculate Roll (Shift-N): Local Tangent (relative to the bone and its parent), Global axis, Active Bone (copy the active bone's Z), View axis, Cursor; options Flip Axis and Shortest Rotation (keeps abs roll below 90 deg). Set Roll: Ctrl-R. Clear Roll: Alt-R. | [3] |

Rules for arms
1. One convention for the whole limb: upper arm, forearm, forearm twist bone and hand share the same bend axis
   (for example local X is the elbow hinge axis on every arm bone). Recalculate with Active Bone or Global axis, then
   check with the Axes overlay. A roll mismatch between forearm and hand is read by Unity as wrist roll (V2).
2. Left and right must be exact mirrors (Symmetrize or X-Axis Mirror while editing, GK). Asymmetric roll gives
   asymmetric twist after retargeting.
3. Twist bones sit on the same axis line as their parent segment, with identical roll, so their rest twist is 0 (GK).
4. Keep a small elbow bend (about 5-15 deg) and a small knee bend in the rest pose so the IK solver knows the bend
   direction (GK).

### 1.2 Inverse Kinematics constraint
| Option | Manual definition [4] | Rule for our rig |
|---|---|---|
| Target | object/bone the chain points to; works without a target | hand / foot controller |
| Pole Target | "determines the roll of the IK chain ... the position of the elbow" | place behind the elbow (arm) / in front of the knee (leg), in the plane of the limb, about 1 forearm length away (GK) |
| Pole Angle | (not in the opened page) | rotate until the chain does not move when the pole is enabled; usually 0, 90, -90 or 180 depending on roll (GK) |
| Chain Length | number of bones from the owner up; 0 = up to the root | **2** for arm (upper arm + forearm) and leg (thigh + shin); never 0 |
| Use Tail | tail of the owner is the chain end | on |
| Stretch | bones may scale to reach the target | off for exported deform chains (Unity avatar stretch is separate) |
| Weight / Rotation | position / rotation matching and priority between chains | Rotation on for feet planting, optional for hands |

Important: IK constraints "ignore their position in the stack and always run after all other constraints" on the
affected bones; to apply constraints after IK, copy the result to a second chain first (Copy Transforms) [4]. So a
twist-bone constraint that reads the IK result must read a copy chain or be driven after baking.
Per-bone IK Lock / Stiffness / Limit exist per axis; with a Pole Target, IK locking does not work on the root bone [5].

### 1.3 Twist distribution (forearm and upper arm)
Facts
- Unity humanoid: `upperArmTwist` distributes upper-arm roll between shoulder and elbow; `lowerArmTwist` between
  elbow and wrist; range 0-1, **default 0.5** [6][7]. Same exists for legs [8].
- Our build sets `lowerArmTwist = 0` when LowerArmTwist_L/R exist and `TwistBoneDriver` puts `share = 0.6` of the hand
  twist on the twist bone (code, not a web source).
- Blender driver variables can read rotation as **Swing and X/Y/Z Twist**: "a Swing rotation that aims the specified
  axis ... followed by a Twist rotation around that axis"; Y Rotation = true twist angle [9]. This is the clean way to
  drive a forearm twist bone from the hand in Blender.
- Damped Track uses "pure swing rotation to minimize rolling around the tracking axis" [10]: good for a non-rolling
  reference bone (for example an upper-arm helper that aims at the elbow without twist).
- Copy Rotation copies chosen axes with Mix (Replace / Add / Before / After Original), Euler order, spaces,
  influence [11]. Copying only Y in Local Space at influence 0.5 is the common quick twist setup; it flips near large
  swing angles because it is Euler based (GK), so prefer the swing-twist driver.
- Stretch To aims Y at a target and scales, with volume preservation options and a Swing rotation mode [12]. Use it
  only on non-exported helper bones (bone scale on exported deform bones causes skinning problems in Unity, GK).
- Limit Rotation clamps per axis with Euler order; Legacy mode causes snapping and should stay off [13].

Rules
1. Forearm: one twist bone at 50-60% of the forearm length taking **50-60%** of the wrist twist (Unity default 0.5,
   our driver 0.6). Wrist keeps the rest. Clamp input twist to +-120 deg (our driver does this).
2. Upper arm (if added later): twist bone near the shoulder counter-rotating **40-50%** of the upper-arm roll (GK).
3. Never distribute the same twist twice: if a twist bone exists, the avatar setting for that segment must be 0.
4. In clips, the wrist itself should carry almost no roll during locomotion (see Targets, V2).

### 1.4 Weight painting / skinning
| Tool | Manual [14][15] | Rule |
|---|---|---|
| Normalize All | per vertex, weights across groups sum to 1; locked groups untouched; Lock Active option | run before every export |
| Limit Total | removes the lowest weights until the Limit is reached | **Limit = 4** (Unity skinning takes up to 4 bones per vertex by default quality; cost rises above 4 [16][17]) |
| Clean | removes weights below Limit; Keep Single avoids unassigned vertices | limit 0.01, Keep Single on (values GK) |
| Auto Normalize (paint option) | keeps deform groups summed to 1 while painting | on |
| Mirror | mirrors weights on symmetric meshes | paint one side, mirror, check seams |
| Armature modifier Preserve Volume | quaternion (dual quaternion) skinning, keeps volume | Blender preview only: Unity skins linearly (GK), so fix candy-wrapper with twist bones and weights, not with Preserve Volume |

Forearm weights (GK): hand bone owns the wrist ring and palm; twist bone blends 0 at elbow to about 0.5 at mid
forearm to about 0.5 near wrist; elbow ring split upper arm / forearm around 50/50.

### 1.5 Animation curves
- Interpolation: Constant (stairs), Linear ("prevents abrupt changes in value but not in speed"), Bezier (default,
  "smooth in both values and speed") [18].
- Handles: Auto Clamped prevents overshoot between keys; Aligned/Free for manual shaping; Vector for straight
  segments [18]. Use Auto Clamped for blocking, then shape overshoot deliberately (settles, follow-through).
- Cycles modifier: Repeat Motion; Repeat with Offset (for forward root travel); Count 0 = infinite. With default
  settings Blender makes handles smooth across the loop, and Cycle-Aware Keying keeps new keys cyclic [19].
  Every loop clip (walk, run, sprint, crouch, idle) uses it so first and last frames match exactly.
- Unity side: clip Loop Time + Loop Pose; Cycle Offset exists per clip and per state [20][21].

### 1.6 Export checklist (GK)
Bake all constraints and drivers to keys on deform bones before FBX export (Unity does not read Blender constraints);
export only deform bones; keep bone scale 1; one action per clip; frame rate 30 fps.

---

## 2. Walk and run kinematics (numbers)

| Quantity | Walk | Run (jog) | Sprint | Source |
|---|---|---|---|---|
| Stance share of gait cycle (one foot) | about 60% (swing 40%) | 39% at 3.2 m/s; duty factor 27-31% at 12 km/h | 36% at 3.9 m/s; 22% elite | [22][23][24] |
| Double support / flight | 2 double-support periods (about 10% each, GK) | 2 double-float periods | longer flight | [22] |
| Cadence vs speed | 100 steps/min at 0.9-1.1 m/s; 130 steps/min at 1.8 m/s | 145-195 spm at 3.35 m/s (wide individual spread); cadence and stride both rise with speed | at least 180 spm at elite race speeds (sprint 190-210 GK) | [25][26] |
| Vertical COM excursion | 3-5 cm (GK) | 8.1-9.5 cm at 10-15 km/h | smaller than jog (GK) | [27] |
| Lateral COM shift | 3-5 cm peak to peak (GK) | 1-3 cm (GK) | minimal (GK) | |
| Pelvis rotation (transverse) | about +-4 deg (8 deg total); 4.3 +- 2.3 deg measured | ROM 10-16 deg (larger in women) | increases (GK) | [28][29][30] |
| Pelvis obliquity (frontal) | swing side drops about 5 deg | ROM 7.8-9.6 deg | | [28][30] |
| Pelvis tilt (sagittal) | ROM 2-4 deg (GK) | ROM 5.9-7.4 deg | pelvis and trunk tilt further forward with speed | [30][22] |
| Thorax (shoulder girdle) yaw | about 8 deg at 1.5 m/s | about 24 deg at 3.0 m/s | | [31] |
| Thorax vs pelvis phase | in phase at 2 km/h, about 125 deg apart at 5.2 km/h; about 150 deg at 1.5 m/s | counter-rotation, arms act as dampers | | [32][31] |
| Head yaw | about 5 deg | about 6 deg (11 deg if arms bound) | | [31] |
| Shoulder flex/ext (arm swing) | ROM about 29 deg, centred near neutral | upper arm stays at or behind vertical at distance speeds | large, from the shoulder, matched to hip | [33][34][35] |
| Shoulder abduction | small (GK: under 15 deg) | 10-25 deg throughout the cycle | small, "armpits closed" in 80-85% of world-class sprinters | [34][36] |
| Elbow | always flexed; mean about 31-38 deg at 1.35 m/s, oscillation about +-7-9 deg (regression) | about 90 deg (GK, coaching); large flexion is actively controlled | opens behind, closes in front (GK) | [33][37] |
| Elbow timing | | 2 flexion peaks: main at contralateral foot strike, second at ipsilateral foot strike | | [34] |
| Foot contact | heel first; knee flexes 15-20 deg to foot flat | about 80% of distance runners rearfoot, rest midfoot | forefoot; heel may never touch | [28][22] |
| Knee in stance / swing | swing max about 60 deg | stance about 45 deg; swing about 90 deg | swing about 105 deg (elite to 130) | [22] |
| Arms and propulsion | mostly passive swing stabilised by muscles; saves energy | counterbalance leg rotation, keep speed constant | up to about 10% of vertical impulse | [38][22][35] |

Walking regressions (Hejrati et al. 2016 [33], v in m/s): elbow amplitude = 6.39v - 1.59g + ...; elbow offset =
26.4v - 6.69g + ... (g = sex code). Elbow flexion therefore increases with speed; at 1.35 m/s it is about 31-38 deg.

Starts and turns in humans
- Gait initiation: anticipatory phase about **500 ms** at natural speed, about **300 ms** at maximal speed; centre of
  pressure moves backward (and sideways to unload the swing leg) to tip the COM forward [39].
- Turning order is top-down: eyes, **head, trunk, pelvis, lead foot, trail foot**, preserved at every speed [40][41].
  Young adults: peak head-to-pelvis separation **40-50 deg** in fast 180 deg standing turns done in **1.5 s** [40].

---

## 3. Animation craft principles

### 3.1 Key poses
Walk (Richard Williams, Animator's Survival Kit, as summarised in [42][43]):
- Contact, Down, Passing, Up. Body lowest at Down, highest at Passing (matches biomechanics: COM lowest in double
  support, highest at mid-stance). Heel leads the foot. Pelvis moves in a wave like the head.
- Williams puts the widest arm swing at Contact and "breaks the elbow" on the swing.
- 24 fps timing [42]: 24 frames per cycle = natural brisk walk (12 per step), 32 = stroll, 16 = cartoon walk.
  Our walk at 30 fps: see Targets (32-frame cycle).

Run ([44][45]):
- Contact (front leg straight, hips rotate toward the front leg), Down (lowest, stance leg bent), Push/Passing,
  Up/Peak (highest, both feet off). A run has at least 2 frames with both feet off the ground (at 24 fps) [45].
- Run leans forward and twists more than a walk; chest and hips counter-rotate, with rotations offset 2-3 frames
  to make a figure-8 [45].

### 3.2 Weight, overlap, asymmetry, secondary, head
| Principle | Measurable rule | Source |
|---|---|---|
| Anticipation vs responsiveness | too little = no weight, too long = unresponsive; enemies get longer anticipation | [46] |
| Follow-through / overlap | offset each joint down a chain by **1-2 frames** (spine > neck > head; shoulder > elbow > wrist > fingers); swing peak **2-3 frames** after the base stops; greatest drag where the base is fastest | [47][48] |
| Tangents | never fully flat tangents on overlapping parts | [48] |
| Slow in / slow out | sword swing: fast in, slow out; falling object: slow in, fast out | [46] |
| Arcs | hands, head and feet travel on arcs; break an arc only on purpose | [46] |
| Twinning (asymmetry) | never mirror left/right poses or hit extremes on the same frame; offset keys, vary one arm/foot | [49] |
| Weight shift | COM goes fully over the support foot before the other foot lifts; pivot the support foot under the COM | [50] |
| Head stabilisation | head yaw stays about 5-6 deg even when the thorax yaws 24 deg in a run; neck counter-rotates | [31] |
| Secondary action | small additions (breathing, fingers, face) never change the silhouette of the main action | [46] |

### 3.3 Starts, stops, turns
- Start: cut the clip where the character is already committed: support foot starting to lift and body slightly
  forward; frame 1 must read as moving. Idle-to-start blend = shortest invisible blend. Real humans spend 300-500 ms
  in anticipation [39]; a game start cannot, so the anticipation is folded into the first step [51].
- Stop: a small counter-movement before braking signals the stop; two variants (left / right foot) chosen by foot
  phase; root motion ends fixed; last pose close to idle frame 1 [52].
- Overshoot and settle: body and arms continue past the stop pose and settle back (pendulum rule: peak 2-3 frames
  after the base stops) [47][46].
- Turn: head leads, then trunk, pelvis, lead foot, trail foot [40][41]; weight fully on the pivot foot before the free
  leg lifts [50]. Lean into acceleration and into curves (lean = atan(a / g), Derived, see Targets).

---

## 4. Unarmed striking (punch, heavy punch, kick)

### 4.1 Kinetic chain
| Evidence | Numbers | Source |
|---|---|---|
| Rear straight punch, onset to impact | about **0.3 s**; fist travels 0.655 m | [53] |
| Sequence | ankle extension starts at about **45%**, knee **60%**, elbow **80%** of the punch time | [53] |
| Peak linear speed rises distally | shoulder 3.1, elbow 6.7, fist 7.8 m/s | [53] |
| Joint angles onset to impact | elbow 67 to 137 deg; shoulder 20 to 86 deg | [53] |
| Hand speed | jab 8.1, cross 7.7 m/s (men); one jab extension lasted 0.148 s, then a pause at full extension, then retraction | [54] |
| Lead straight | start-up force from the lead leg dominates; elite peak fist speed 7.2 m/s | [55] |
| Chain description | rear hip extension + ankle plantarflexion; rear hip external / front hip internal rotation turns the pelvis; abdominals brace to transfer; chest, shoulder, triceps last; better boxers use the legs more | [56] |

Guard (GK, standard boxing): feet shoulder width, lead foot forward, knees soft; fists at cheek height, elbows in
front of the ribs, chin down. The non-striking hand stays at the chin during every strike; the striking hand returns
along the same line.

### 4.2 Kick
Roundhouse kick, expert practitioners [57]: total execution **1.02 s** (Muay Thai) to **1.54 s** (Taekwondo).
Phases as share of total: preparation 6%, chamber 14%, extension 11%, **recoil 69%**. Pelvis axial rotation velocity
about 2.5x its posterior tilt velocity (the hip turns over before the knee snaps). Knee extension 706-947 deg/s.
So contact comes at about 31% of the move (0.32-0.48 s); real recoil is long and must be compressed for a game.
Support foot pivots outward 45-90 deg on a roundhouse (GK).

### 4.3 Game timing references
Street Fighter 6, Ryu, official frame data at 60 fps [58] (startup / active frames / recovery), with the 30 fps
equivalent:

| Move | 60 fps | 30 fps equivalent | ms to first active frame |
|---|---|---|---|
| Standing light punch | 4 / 3 / 7 | 2 / 1.5 / 3.5 | 67 |
| Standing medium punch | 6 / 4 / 11 | 3 / 2 / 5.5 | 100 |
| Standing heavy punch | 10 / 5 / 18 | 5 / 2.5 / 9 | 167 |
| Standing light kick | 5 / 3 / 11 | 2.5 / 1.5 / 5.5 | 83 |
| Standing medium kick | 9 / 3 / 18 | 4.5 / 1.5 / 9 | 150 |
| Standing heavy kick | 12 / 4 / 20 | 6 / 2 / 10 | 200 |

Other game guidance
- Attack = Anticipation, Attack (active), Recovery; for the player, input to registration should stay under
  **100 ms**; enemy anticipation at least player reaction time (**8 frames at 30 fps**) plus buffer; Hollow Knight
  Hornet dash: 15 f anticipation, 10 f active, 10 f recovery at 30 fps [59].
- Skullgirls was animated to deliver a punch in as few as six frames by relying on strong keys, anticipation and
  timing [60].
- Decide exactly when the player may act again (cancel window) to keep weight and responsiveness [46]; use cancel
  windows / safe blend intervals and additive layers to keep control [61].

A fighting game is faster than a survival game; our targets (section Targets) sit between the SF6 numbers and the
real biomechanics (0.3 s punch, 0.3-0.5 s kick contact).

---

## 5. Game-specific practice

### 5.1 Transition durations
| Transition | Duration | Basis |
|---|---|---|
| Unity default for a new transition | **0.25 s**, Fixed Duration on, Has Exit Time on (exit time set so the blend ends at clip end) | Unity source [62]; property meanings [63] |
| Motion matching blend (For Honor) | 0.25 s | GDC 2016 [64] |
| Inertialization | keep under 0.4 s; do not use when poses are very different | Unreal docs [65]; technique [66] |
| Locomotion state to state (walk/run/sprint) | 0.15-0.25 s | [62][64] plus GK |
| Idle to start | shortest invisible blend (about 0.05-0.12 s, GK) | [51] |
| Stop to idle | 0.2-0.3 s (GK) | |
| Into an attack | 0.05-0.1 s (GK); out of an attack from its cancel window 0.15-0.25 s (GK) | |
| Stand to crouch / gather | 0.25-0.4 s or a dedicated transition clip (GK); never under 0.1 s (V4, V7) | |

Rule: every input-driven transition has Has Exit Time **off** (the Unity default is on when the state has a motion)
[62][63]. Blend tree parameters get damping 0.1-0.2 s (GK).

### 5.2 Root motion vs in-place with a motor-driven character
- Unity root motion: the Root Transform is the Body Transform projected on the ground; its per-frame change moves the
  GameObject. Bake Y into pose for most clips (not jumps); Based Upon Feet for Y prevents floating in blends; bake XZ
  for idles to stop drift [20][67].
- Code-driven movement is responsive but slides; data-driven is weighty but sluggish. Hybrid: let the visual follow
  the simulation with damped correction, clamp position (max distance) and rotation (max angle), speed up source
  motion about 10% if it feels slow, foot-lock IK as last resort [68].
- For Honor: spring-damper on velocity drives the simulation; animation is chosen to follow it; the entity is
  clamped within **15 cm** of the simulation; toes locked only when nearly still, small sliding accepted [64].
- Our case (capsule-driven, in-place clips): clip root speed must equal motor speed at the blend thresholds (walk
  1.35, run 3.8, sprint 6.2), the playback-rate or stride warp correction stays within **15-20%** [69]; outside that
  range, add or switch a clip.

### 5.3 Warping and turn in place (Unreal practice, transferable)
- Distance matching on starts / stops, then stride warping; too much distance matching gives very high play rates
  [69][70].
- Orientation warping turns the lower body toward the travel direction and spreads the counter-twist over the spine;
  default delta threshold **90 deg** [71].
- Turn in place: a Root Yaw Offset counters controller rotation (Accumulate / Hold / Blend Out); turn clips chosen by
  angle and wait time; turn yaw baked as curves from root-motion clips [70]; allowed offset range widened to
  -179..179 deg in the Lyra adaptation [69].
- 180 deg turn during a run: rotate the capsule quickly and play a turn clip synced to foot phase (two variants, one
  per foot) to avoid skating [61].

### 5.4 Foot IK and sliding
- Unity: humanoid state option **Foot IK**; custom IK needs IK Pass on the layer and `OnAnimatorIK` with
  `SetIKPosition/RotationWeight` [21][72].
- Ground placement: raycast from above each foot, add foot height, derive pitch/roll from toe/heel rays but keep the
  animated yaw; lower the pelvis when a leg would over-extend; blend IK weight in and out over time, back to FK when
  contact is lost; extend ray distance during stance, shorten in swing [73].
- Weight from a per-clip "foot planted" curve (1 in stance, 0 in swing) so IK never pins a swinging foot (GK).

---

## Targets for our character

Per-clip targets, 30 fps. Numbers are the check values for review in Blender (Graph Editor / measurement) and in Unity.

### Motor coupling (Derived)
| Clip | Speed (m/s) | Cadence (steps/min) | Frames per step | Cycle frames | Step length (m) | Basis |
|---|---|---|---|---|---|---|
| Walk | 1.35 | 110-118 (113 interpolated) | 15-16 | **32** | 0.70-0.74 | [25] interpolated |
| Run | 3.8 | 165-180 | 10-11 | **20-22** | 1.27-1.39 | [26] |
| Sprint | 6.2 | 190-205 (GK) | 9 | **18** | 1.8-1.95 | GK |
| Crouch walk | 0.95 | 95-105 (GK) | 17-19 | **36** | 0.55-0.6 | GK |

Step length = speed x 60 / cadence. Curve-lean limit (Derived, physics): lean = atan(v x omega / g). Keeping lean
under 20 deg gives max smooth yaw rates of about **150 deg/s at walk, 55 deg/s at run, 33 deg/s at sprint**; faster
direction changes need a plant / pivot clip or orientation warping. Current turnSpeed 600 deg/s (180 deg in 0.3 s) is
why V3 reads as a snap. Start lean upper bound atan(a / g): motor accel 10 m/s2 implies 45 deg (a sprinter's block
start), which a walk start cannot show; either lower walk/run accel or accept root-motion starts.

### Target table
| Clip | Timing (30 fps) | Pelvis / COM | Torso / head | Arms | Feet / contact | Transitions |
|---|---|---|---|---|---|---|
| **Walk** | 32 f cycle; contact f0/f16, down f2-3/f18-19, passing f8/f24, up f11-12/f27-28 | bob 3-5 cm (low at down, high at passing); lateral shift 3-5 cm toward stance foot; yaw +-4 deg; swing-side drop about 5 deg; tilt ROM 2-4 deg | thorax yaw about +-4 deg (8 deg ROM) opposite to pelvis; head yaw under 5 deg; spine rotation keys 1-2 f behind pelvis | shoulder flex/ext ROM 25-30 deg, extremes at contralateral contact; elbow 30-40 deg flexed, +-7-9 deg, more flexed on the forward swing; abduction under 15 deg; hands pass beside the thigh, never in front of the belly; forearm roll within +-10 deg (thumb forward) | heel contact with ankle dorsiflexed; knee near straight at contact, 15-20 deg at foot flat; stance about 60% (19 f), swing about 40%; left and right steps not identical (1 f or a few deg difference) | 0.15-0.25 s to/from run; idle to walk start 0.05-0.12 s |
| **Run** | 20-22 f cycle; foot contact 6-8 f, flight 3-4 f per step | bob 6-9 cm (low at down, high in flight); pelvis yaw ROM 10-16 deg, obliquity ROM about 8-9 deg, tilt ROM about 6-7 deg; forward trunk lean 5-10 deg (GK) | thorax yaw ROM about 20-25 deg counter to pelvis; head yaw under 6 deg and level | elbow **80-100 deg** with 2 small flexion peaks at foot strikes; upper arm abduction **10-25 deg**, elbows close to the ribs; upper arm swings from about vertical to 40-50 deg behind (GK for the back value); hand front peak at lower sternum height, back peak at the hip; hands do not cross the sternum line; fist loose, no roll | about 80% heel / 20% midfoot style, land under the knee; stance knee about 45 deg; swing knee about 90 deg | 0.15-0.25 s to/from walk and sprint |
| **Sprint** | 18 f cycle; contact 4-5 f per foot | bob 5-8 cm (GK); trunk lean 10-20 deg (more when accelerating, upright at top speed) | thorax counter-rotation large but head steady | swing from the shoulder, big fore-aft ROM; elbow closes in front (60-80 deg) and opens behind (100-130 deg) (GK); armpits closed, forearms drive fore-aft, not across | forefoot contact under the hips; swing knee about 105 deg | 0.2 s from run; sprint stop uses run stop with extra step |
| **Turn** | in place: 90 deg in 20-26 f, 180 deg in 28-36 f (human fast 180 = 45 f) | pelvis starts 2-3 f after head, COM over the pivot foot before the other foot lifts | order head > thorax > pelvis > feet; head-pelvis separation peak 30-50 deg | arms trail 2-3 f (overlap), settle 3-4 f after feet | pivot on the ball of the lead foot, trail foot steps; 2-3 steps for 180 deg; no foot rotates while fully loaded without a pivot | trigger at yaw error over 60 deg standing; moving: see yaw-rate limits above, pivot clip over 135 deg reversal |
| **Start / Stop** | start: first foot lifts at f0-2, loop reached in 1-2 steps (walk 12-20 f, run 12-18 f); stop: 1-2 steps, walk 10-16 f, run 15-24 f | start: COM ahead of the support foot, forward lean 8-15 deg peak; stop: counter-lean back 5-15 deg at the brake step, COM overshoots 2-5 cm forward then settles over 6-10 f | head leads the lean by 1-2 f; head settles last | start: arm opposite the first step drives forward; stop: arms swing past the body line, settle over 4-8 f | stop variants for left and right foot, chosen by foot phase; root ends fixed | idle to start 0.05-0.12 s; stop to idle 0.2-0.3 s |
| **Idle** | loop 120-240 f (4-8 s); breath cycle 3-5 s (12-20 breaths/min, GK) | weight about 60/40 on one leg, pelvis tilted 2-4 deg to the unloaded side; COM sway under 1-2 cm (GK) | chest rise with breath 0.5-1 cm; head drift under 2-3 deg | elbows relaxed 10-20 deg; hands not mirrored; no wrist roll | feet not symmetric (one toe out more) | 0.2-0.3 s from stops; 0.1-0.2 s to turn clips |
| **Crouch** | walk 36 f cycle; stand-to-crouch clip 8-12 f | pelvis at about 60-65% of standing height (capsule 1.15 / 1.8 m); bob 2-3 cm | trunk flexion 20-35 deg (GK), head up to keep eye line | elbows 30-60 deg, hands lower and closer, small swing (ROM 10-20 deg) | knees 60-90 deg, heel-toe rolling contact, no pops | 0.25-0.4 s or a clip; gather enter / exit never under 0.2 s (V4, V7) |
| **Punch (jab / light)** | anticipation 1-2 f; strike to contact at f4-6 (130-200 ms); active 1-2 f; recovery 6-9 f; total 12-16 f; cancel into next strike after active + 2 f | small weight shift forward (under 5 cm); pelvis yaw 5-15 deg (GK) | shoulder of the punching arm rotates forward 15-25 deg, chin behind the shoulder (GK) | guard hand stays at the chin; punching elbow extends last (after shoulder rotation starts); arm about 95% extended at contact, never locked; returns along the same line | lead foot planted; no heel pivot needed | into: 0.05-0.1 s; back to guard idle 0.1-0.15 s |
| **Heavy punch (cross)** | anticipation 4-6 f (load); strike 4-5 f, contact at f8-11 (0.27-0.37 s, matches 0.3 s real); active 2 f; follow-through 3-4 f; recovery 10-14 f; total 24-30 f | weight moves from rear to front leg (10-15 cm, GK); pelvis yaw 30-45 deg (GK) | shoulders yaw 45-70 deg through the strike (GK); pelvis peak rotation 1-2 f before shoulders, shoulders 1-2 f before elbow | sequence ankle/knee drive at about 45-60% of strike time, elbow extension from about 80%; elbow 65 to about 135-140 deg at contact; guard hand at the chin | rear heel lifts and pivots out 30-60 deg (GK) | same; cancel only after follow-through |
| **Kick (front / roundhouse)** | chamber 6-9 f; extension 3-4 f, contact at f9-13 (0.3-0.43 s; real 0.32-0.48 s); active 2 f; recoil 10-16 f (real recoil is 69% of the move, compressed); total 24-32 f | COM over the support foot before the knee lifts; pelvis turns over before the knee snaps; hip rotation leads | trunk leans back / away 10-30 deg to balance (GK) | arms counterbalance: kick-side arm drops back, guard hand stays up (GK) | support foot pivots 45-90 deg outward on a roundhouse; lands back under the COM | into 0.05-0.1 s; recoil must end in guard stance |
| **Sword swing (1H)** | anticipation 5-8 f; strike 3-5 f (fast in); active 2-4 f; follow-through 5-8 f (slow out); recovery 8-12 f; total 25-35 f (GK within the chain rules above) | step or weight shift first, pelvis leads | torso coil 20-40 deg back in anticipation, unwinds pelvis > chest > shoulder (1-2 f offsets) | elbow then wrist extend last; blade tip on a clean arc; free hand counterbalances | front foot planted at contact | into 0.05-0.1 s; chain next swing from follow-through |
| **Bow draw** | raise and nock 10-15 f; draw 15-25 f (0.5-0.8 s; real full draw about 1.4 s); hold loop with breath; release 1-2 f; follow-through 8-12 f (real about 2.4 s) | stance side-on, weight even; no bob while holding | head fixed on the target line; torso turns, not the head | draw elbow in line behind the arrow at anchor (real deviation about 4 deg); draw hand moves back along the arrow line on release; bow shoulder low, bow arm straight but not locked | feet planted; no foot motion | aim layer blended on upper body only (GK) |

Sources for the table: walk [22][25][28][29][31][32][33][42]; run [22][23][26][27][30][31][34][36][37][44][45];
sprint [22][35][36]; turn [40][41][50][61][71]; start/stop [39][51][52][47]; punch [53][54][55][56][58][59];
kick [57][58]; sword [46]; bow [74]. Unmarked values in the table without GK are Derived from these.

### Quick fixes mapped to the video findings
| Finding | Target that fixes it |
|---|---|
| V1 high flared arms in run | elbow 80-100 deg, abduction 10-25 deg, hands between lower sternum and hip, fore-aft swing |
| V2 wrist / fist roll | forearm roll within +-10 deg in locomotion; one twist convention; twist distributed once (avatar 0 when twist bones drive) |
| V3 fast yaw, no pivot | yaw-rate limits by speed (Derived), turn-in-place clips 90/180 deg, pivot clip over 135 deg, head-first order |
| V4 / V7 crouch snap and flailing blend | transition clips or 0.25-0.4 s blends; never blend through an abducted-arm pose |
| V5 45 deg yaw snap on interact | rotate over 8-12 f with head leading, or step-turn clip |
| V8 stiff walk | elbow 30-40 deg, shoulder ROM 25-30 deg, pelvis yaw +-4 deg and drop 5 deg, thorax counter-rotation, 1-2 f overlap, no twinning |

---

## Sources (all opened during this research)

Blender manual (rendered pages at docs.blender.org returned only navigation to the fetch tool; content was read from
the manual's source files on projects.blender.org, same text)
1. Bone structure: https://docs.blender.org/manual/en/latest/animation/armatures/bones/structure.html (source: https://projects.blender.org/blender/blender-manual/raw/branch/main/manual/animation/armatures/bones/structure.rst)
2. Bone Transform properties: https://docs.blender.org/manual/en/latest/animation/armatures/bones/properties/transform.html (source .../bones/properties/transform.rst)
3. Bone Roll: https://projects.blender.org/blender/blender-manual/raw/branch/main/manual/animation/armatures/bones/editing/bone_roll.rst
4. Inverse Kinematics constraint: https://docs.blender.org/manual/en/latest/animation/constraints/tracking/ik_solver.html (source .../constraints/tracking/ik_solver.rst)
5. IK introduction (posing): https://projects.blender.org/blender/blender-manual/raw/branch/main/manual/animation/armatures/posing/bone_constraints/inverse_kinematics/introduction.rst
9. Drivers panel (Swing and Twist modes): https://projects.blender.org/blender/blender-manual/raw/branch/main/manual/animation/drivers/drivers_panel.rst
10. Damped Track: https://projects.blender.org/blender/blender-manual/raw/branch/main/manual/animation/constraints/tracking/damped_track.rst
11. Copy Rotation: https://projects.blender.org/blender/blender-manual/raw/branch/main/manual/animation/constraints/transform/copy_rotation.rst
12. Stretch To: https://projects.blender.org/blender/blender-manual/raw/branch/main/manual/animation/constraints/tracking/stretch_to.rst
13. Limit Rotation: https://projects.blender.org/blender/blender-manual/raw/branch/main/manual/animation/constraints/transform/limit_rotation.rst
14. Weight paint editing (Normalize All, Limit Total, Clean, Mirror): https://projects.blender.org/blender/blender-manual/raw/branch/main/manual/sculpt_paint/weight_paint/editing.rst
15. Weight paint options (Auto Normalize, Multi-Paint): https://projects.blender.org/blender/blender-manual/raw/branch/main/manual/sculpt_paint/weight_paint/tool_settings/options.rst ; Armature modifier (Preserve Volume): https://projects.blender.org/blender/blender-manual/raw/branch/main/manual/modeling/modifiers/deform/armature.rst
18. F-Curve properties (interpolation, easing, handles): https://projects.blender.org/blender/blender-manual/raw/branch/main/manual/editors/graph_editor/fcurves/properties.rst
19. F-Curve modifiers (Cycles): https://projects.blender.org/blender/blender-manual/raw/branch/main/manual/editors/graph_editor/fcurves/modifiers.rst

Unity / Unreal / engines
6. HumanDescription.upperArmTwist: https://docs.unity3d.com/ScriptReference/HumanDescription-upperArmTwist.html
7. HumanDescription.lowerArmTwist: https://docs.unity3d.com/ScriptReference/HumanDescription-lowerArmTwist.html
8. HumanDescription: https://docs.unity3d.com/ScriptReference/HumanDescription.html
16. SkinWeights enum: https://docs.unity3d.com/ScriptReference/SkinWeights.html
17. QualitySettings.skinWeights: https://docs.unity3d.com/ScriptReference/QualitySettings-skinWeights.html
20. Animation clip properties: https://docs.unity3d.com/Manual/class-AnimationClip.html
21. Animator state properties (Foot IK, Cycle Offset): https://docs.unity3d.com/Manual/class-State.html
62. UnityCsReference StateMachine.cs (default transition 0.25 s): https://raw.githubusercontent.com/Unity-Technologies/UnityCsReference/master/Editor/Mono/Animation/StateMachine.cs
63. Animation transitions: https://docs.unity3d.com/6000.2/Documentation/Manual/class-Transition.html
67. Root motion: https://docs.unity3d.com/Manual/RootMotion.html
72. Inverse kinematics: https://docs.unity3d.com/Manual/InverseKinematics.html
65. Unreal transition rules (inertialization under 0.4 s): https://dev.epicgames.com/documentation/en-us/unreal-engine/transition-rules-in-unreal-engine
69. Adapting Lyra animation to your UE5 game (stride warp 15-20%): https://www.unrealengine.com/en-US/tech-blog/adapting-lyra-animation-to-your-ue5-game
70. Animation in Lyra: https://dev.epicgames.com/documentation/en-us/unreal-engine/animation-in-lyra-sample-game-in-unreal-engine
71. Pose warping: https://dev.epicgames.com/documentation/en-us/unreal-engine/pose-warping-in-unreal-engine

GDC and game-animation articles
64. Clavet, Motion Matching and The Road to Next-Gen Animation, GDC 2016 (transcript): https://archive.org/stream/GDC2016Clavet/GDC2016-Clavet_djvu.txt ; Vault page https://gdcvault.com/play/1023280/Motion-Matching-and-The-Road
66. Bollo, Inertialization: High-Performance Animation Transitions in Gears of War, GDC 2018 (abstract): https://www.gdcvault.com/play/1025331/Inertialization-High-Performance-Animation-Transitions
60. Cartwright, Animation Bootcamp: Fluid and Powerful Animation within Frame Restrictions, GDC 2014 (abstract): https://www.gdcvault.com/play/1020017/Animation-Bootcamp-Fluid-and-Powerful
46. Cooper, The 12 Principles of Animation in Video Games (Game Anim): https://www.gameanim.com/2019/05/15/the-12-principles-of-animation-in-video-games/
59. Kraj, Keys to Combat Design: Anatomy of an Attack (GDKeys): https://gdkeys.com/keys-to-combat-design-1-anatomy-of-an-attack/
61. Massoudi, The Challenge of Having Both Responsiveness and Naturalness in Game Animation (2015): https://www.gamedeveloper.com/design/the-challenge-of-having-both-responsiveness-and-naturalness-in-game-animation
73. Massoudi, Foot Placement Using Foot IK (2015): http://peyman-mass.blogspot.com/2015/06/foot-placement-using-foot-ik.html
68. Holden, Code vs Data Driven Displacement (2021): https://theorangeduck.com/page/code-vs-data-driven-displacement
51. Crafting a gameplay Start animation (AnimotionX): https://www.animotionx.com/en/post/crafting-a-gameplay-start-animation-the-impulse-under-pressure
52. Building a stop in gameplay animation (AnimotionX): https://www.animotionx.com/en/post/building-a-stop-in-gameplay-animation-conventions-engine-constraints-and-concrete-production-exam
58. Street Fighter 6, Ryu frame data (Capcom): https://www.streetfighter.com/6/character/ryu/frame

Animation craft
42. Monmouth University, Movement: Walk Cycle: https://animation.monmouth.edu/instruct/animation/walk-cycle/
43. Summary of Williams, Animator's Survival Kit (walk): https://edwardboyleanimation.wordpress.com/2016/01/04/walk-cycle-research-3-animators-survival-kit/
44. Sta Catalina, The Key Poses of a Run Cycle (AnimSchool): https://blog.animschool.edu/2024/04/10/the-key-poses-of-a-run-cycle/
45. Run Cycle tutorial (Rusty Animator): https://rustyanimator.com/run-cycle/
47. Wong, Overlap and Pendulum Motion (Animation Mentor): https://www.animationmentor.com/blog/tutorial-overlap-pendulum-motion-animation/
48. Seymour, How to Animate Overlap and Follow-Through (Animation Mentor): https://www.animationmentor.com/blog/tutorial-animate-overlap-and-follow-through/
49. Kelly, Twinning and Why You Should (Usually) Avoid It (Animation Mentor): https://www.animationmentor.com/blog/twinning-and-why-you-should-usually-avoid-it/
50. Martinsen, How to Animate a Character Turnaround (Animation Mentor): https://www.animationmentor.com/blog/animate-a-character-turnaround/

Biomechanics
22. Novacheck, The biomechanics of running, Gait & Posture 7 (1998) 77-95: https://www.henriquetateixeira.com.br/up_artigo/the_biomechanics_of_running_1998_novacheck_ba0bo4.pdf ; summary https://www.physio-pedia.com/Running_Biomechanics
23. Patoz et al., Duty Factor Is a Viable Measure to Classify Spontaneous Running Forms, Sports 2019: https://www.mdpi.com/2075-4663/7/11/233
24. (Novacheck 1998, as 22)
25. Tudor-Locke et al., Walking cadence and intensity in 21-40 year olds: CADENCE-Adults, IJBNPA 2019: https://ijbnpa.biomedcentral.com/articles/10.1186/s12966-019-0769-6
26. Davis, A comprehensive guide to the science of cadence for runners (2026): https://runningwritings.com/2026/01/science-of-cadence.html
27. Fadillioglu et al., Changes in Key Biomechanical Parameters According to the Expertise Level in Runners at Different Running Speeds, Bioengineering 2022: https://www.mdpi.com/2306-5354/9/11/616
28. Determinants of Gait (Saunders, Inman and Eberhart 1953 values, with critique): https://podiapaedia.org/wiki/biomechanics/gait/determinants-of-gait/
29. Kerrigan et al., Quantification of pelvic rotation as a determinant of gait, Arch Phys Med Rehabil 2001 (abstract): https://www.sciencedirect.com/science/article/abs/pii/S0003999301598585
30. Perpina-Martinez et al., Differences between Sexes and Speed Levels in Pelvic 3D Kinematic Patterns during Running Using an IMU, IJERPH 2023: https://www.btsbioengineering.com/wp-content/uploads/2023/07/Perpina-Martinez-g-walk.pdf
31. Pontzer et al., Control and function of arm swing in human walking and running, J Exp Biol 2009: https://journals.biologists.com/jeb/article/212/4/523/18953/Control-and-function-of-arm-swing-in-human-walking
32. Bruijn et al., Coordination of leg swing, thorax rotations, and pelvis rotations during gait, Gait & Posture 2008 (abstract): https://www.sciencedirect.com/science/article/abs/pii/S096663620700135X
33. Hejrati et al., Comprehensive quantitative investigation of arm swing during walking at various speed and surface slope conditions, Human Movement Science 2016: https://mmrobotics.mech.utah.edu/wp-content/uploads/2023/01/Hejrati_HMS16.pdf
34. Smoliga, dissertation, University of Pittsburgh 2007 (reports Hinrichs's running arm kinematics): https://d-scholarship.pitt.edu/10066/1/SmoligaJM_ETD2007.pdf
35. Biomechanics of sprint running (Wikipedia, citing Hinrichs 1987): https://en.wikipedia.org/wiki/Biomechanics_of_sprint_running
36. Hiruma and Kariyama, Direction of arm swing of world top-class sprinters, J Phys Educ Sport 2022: https://efsupit.ro/images/stories/mai2022/Art%20145.pdf
37. Koo, Ogihara and Koo, Active Arm Swing During Running Improves Rotational Stability of the Upper Body, Ann Biomed Eng 2025: https://link.springer.com/article/10.1007/s10439-025-03688-0
38. Meyns, Bruijn and Duysens, The how and why of arm swing during human walking, Gait & Posture 2013 (abstract): https://www.sciencedirect.com/science/article/abs/pii/S0966636213001185
39. Farinelli et al., A Novel Viewpoint on the Anticipatory Postural Adjustments During Gait Initiation, Front Hum Neurosci 2021: https://www.frontiersin.org/journals/human-neuroscience/articles/10.3389/fnhum.2021.709780/full
40. Khobkhun, Hollands and Richards, The Effect of Different Turn Speeds on Whole-Body Coordination, Sensors 2021: https://www.mdpi.com/1424-8220/21/8/2827
41. Khobkhun and Thanakamchokchai, Biological sex-related differences in whole-body coordination during standing turns, Sci Rep 2023: https://www.nature.com/articles/s41598-023-49201-2
53. Cheraghi et al., Kinematics of Straight Right Punch in Boxing, Annals of Applied Sport Science 2014: http://aassjournal.com/article-1-136-en.pdf
54. Kimm and Thiel, Hand Speed Measurements in Boxing, Procedia Engineering 2015: https://d-nb.info/1203379595/34
55. Liu et al., Biomechanics of the lead straight punch of different level boxers, Front Physiol 2022: https://www.frontiersin.org/journals/physiology/articles/10.3389/fphys.2022.1015154/full
56. The Biomechanics of a Knockout Punch (The Science of Striking): https://www.thescienceofstriking.com/training/the-biomechanics-of-a-knockout-punch/
57. Gavagan and Sayers, A biomechanical analysis of the roundhouse kicking technique of expert practitioners, PLOS ONE 2017: https://journals.plos.org/plosone/article?id=10.1371%2Fjournal.pone.0182645
74. Lau et al., Comparison of Shooting Time Characteristics and Shooting Posture Between High- and Low-Performance Archers, Annals of Applied Sport Science 2023: http://aassjournal.com/article-1-1115-en.pdf

Not readable this session (not cited for numbers): PubMed / PMC pages (captcha), Europe PMC (rate limited),
Game Anim "When Will The Attack Land?" (rate limited), ResearchGate (rate limited), J Appl Physiol (403),
GeroScience trunk kinematics (rate limited), YouTube (not attempted, per brief). Walking vertical / lateral COM
values are therefore marked GK.
