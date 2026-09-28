# CHARACTER IK PLAN (player), 2026-09-28

Rule: IK is used selectively. Clips carry the motion; IK only closes small gaps (feet on uneven ground, second hand on a
weapon, bow string, climb holds, where the head looks). IK never replaces a missing clip.

## 1. What exists today

| Goal | Where | State | Measured problem |
|---|---|---|---|
| Foot IK (both feet) | Unity `PlayerIK` (OnAnimatorIK, Base layer IK pass) | on in Locomotion / Crouch / Strafe; 0.35 weight at run speed | pelvis follows the lowest planted foot at **full** weight: hip dips of 0.21-0.25 m when the capsule steps up onto an obstacle (probe R7) |
| Look-at (head / chest) | `PlayerIK` | weight 0.75, body 0.15, head 0.75, clamp 0.55 | twists the chest up to +-33 deg against the hips during turns; drops to 0 when the target leaves a 95 deg cone and comes back (reads as a jerk) |
| Off-hand grip (spear, two-handed sword heavy) | `PlayerIK` + `PlayerHierarchy.TryComputeGrip`, grip point on the weapon | on only with a two-handed weapon | fine in captures; clip already puts the hand within 0.1-0.2 m |
| Elbow hints | `PlayerIK` | only when hand IK is on | fixed offsets; should follow the clip's elbow plane |
| Bow string / draw hand | `PlayerIK` | on while aiming a bow | draw hand reaches the cheek in `held_Bow_Draw*` captures |
| Body lean into turns | `PlayerIK` | max 9 deg | fine |
| Authoring IK in Blender | `pf_weapon_ik.py` weapon-target solver (keys solved per frame, warm start) | used to bake weapon clips | wrist flips of up to 164 deg in one frame in some weapon clips (no continuity term); FK/IK switch pops at k = 0.5 in Get_Up, Wake_Up, Death, Climb_End |

Unarmed, the hand IK and elbow hints are at weight 0 in every frame (probe), so they play no part in the run arm pose.

## 2. Spec targets mapped to the project

| Spec item | Plan |
|---|---|
| RightHandIK / LeftHandIK | Keep Unity goal-based hand IK (humanoid `SetIKPosition/Rotation`). Targets come from sockets on the held item (`PlayerHierarchy`), never world constants. |
| RightElbowPole / LeftElbowPole | Replace fixed hint offsets with a hint computed from the clip's own elbow position each frame (sample before IK, push 0.3 m outward along the arm's bend normal). Keeps the clip's elbow plane, so no elbow flip. |
| RightFootIK / LeftFootIK | Keep. Fix the pelvis: pelvis offset = min(foot offsets) x current foot weight, rate-limited (max 1.5 m/s), and no step-up onto resource-node colliders. |
| WeaponGrip targets | Keep the item grip sockets (tool axis convention: handle along local Z, working end at -Z). Off-hand grip point per weapon in `WeaponData`. |
| BowGrip / ArrowGrip | Bow in the left hand (grip socket on the bow), string point on the bow for the right hand; arrow nock target = string point; arrow shaft follows the draw hand. Draw elbow kept behind the arrow line by the elbow hint (target from the motion reference: within ~5 deg of the arrow line at anchor). |
| Climb points | `Climbable` already gives the path; add hand / foot IK to hold points only on Climb_Idle and the grab frames, weights from clip curves. |
| Look direction | Lower the body weight to 0.05 while moving faster than walk or turning faster than 90 deg/s; ease the target (SmoothDamp 0.15 s) instead of dropping it at 95 deg. |

## 3. Blender authoring IK

- `pf_weapon_ik`: add a frame-to-frame continuity term (penalize wrist rotation change > 15 deg per frame) and a narrower wrist twist limit (+-70 deg).
- Twist bone: bake a true swing-twist per frame (twist of the hand about the forearm axis x 0.6) instead of `Copy Rotation` of Euler Y. The Unity `TwistBoneDriver` already does the correct math at run time; the Blender bake should match it so the FBX preview and the game agree.
- FK / IK switches in Get_Up, Wake_Up, Death, Climb_End: blend over 4-6 frames with an ease curve, never a single-frame switch.

## 4. Order and acceptance

| Step | Depends on | Acceptance (measured with `CharacterMotionProbe`) |
|---|---|---|
| IK-1 pelvis drop fix | none | hip dip under 0.05 m on step-ups; no dip on flat ground |
| IK-2 look IK easing | none | chest vs hips under +-12 deg during a 180 deg reversal; no single-frame weight change > 0.1 |
| IK-3 elbow hints from the clip | avatar fix (animation plan step A1) | elbow plane change under 10 deg between IK on and off with a spear / sword |
| IK-4 bow targets | IK-3 | draw elbow within 5 deg of the arrow line at full draw; right hand on the string within 2 cm |
| IK-5 weapon clip continuity (Blender) | none | no wrist step > 25 deg per frame in any weapon clip |
| IK-6 climb holds | none | hands within 3 cm of hold points on grab frames |

IK work starts only after the avatar fix (A1 in the animation plan), because the broken left-arm encoding would make every
arm IK measurement on the left side wrong.
