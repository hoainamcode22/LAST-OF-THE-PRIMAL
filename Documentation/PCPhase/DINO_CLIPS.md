# DINO_CLIPS: dinosaur Animator interface for AI (DINO agent, PC phase, 2026-09-30)

Seven land species were re-exported with the PC clip set: Parasaurolophus, Triceratops, Ankylosaurus (grazers),
Velociraptor, Carnotaurus, Spinosaurus, Apex / Rift Tyrant (hunters). Pteranodon and Mosasaurus are ambient and keep their
phase 2 clips. All clips are original procedural motion (`E:\Model game khủng long\scripts\pf_dino_anim_pc.py` on top of
`pf_dino_anim.py`), in place (the AI moves the object), 30 fps. The controller is rebuilt by
`PrimalCharacterBuilder.BuildDinoController` (called by `PrimalDinoBuilder.RebuildClips`). Status: imported in Unity 2026-09-30 07:22-07:24 UTC by
`PrimalDinoBuilder.RebuildClips` (7 x PASS); see `Documentation/Phase1/_D_report.md`.

## 1. Parameters (existing ones unchanged)

| Parameter | Type | Meaning | Default |
|---|---|---|---|
| Speed | float | planar m/s; Locomotion blends Idle 0 / Walk / Run at the clip speeds (table below) | 0 |
| ActionType + Action | int + trigger | set the id, then fire Action (ids in section 2) | 0 |
| Attack + AttackType | trigger + int | 0 lunge (Attack), 1 heavy (Heavy_Attack, then Recover), **2 quick snap (Bite)** | 0 |
| Hurt / Dead / Alert | trigger / bool / bool | as in phase 2 | |
| **Intensity** | float 0..1 | inside the Locomotion run slot: 0 = Run, 1 = **Chase** (hunters) or **Flee** (grazers); same speed as Run, so it only changes the body (head low and stretched / head up, tail high, faster cadence). Damp it (0.3 s) | 0 |
| **Turn** | float deg/s | signed yaw rate, **+ = clockwise seen from above (turning right)**. With Speed < 0.3: Turn > 15 plays Turn_Right, < -15 Turn_Left; back below 8 or Speed > 0.6 | 0 |
| **TurnMul** | float | playback speed of the turn clips = actual rate / authored rate (table below) | 1 |
| **Stop** | trigger | braking settle (lurch, last step, damped sway); fire when braking from walk / run to a stand while in Locomotion. Ends at 0.9 or when Speed > 1 | |

Nothing changes until AI sets the new parameters; DinoLife's `Locomotion` state name is unchanged (step phase still read).

## 2. ActionType ids

| id | name (DinoActions) | state | kind |
|---|---|---|---|
| 1 | Eat | Eat | loop while ActionType stays. Grazers crop (3 bites, then head half up chewing and glancing); hunters bite, pull back and shake, swallow. Mouth reaches the ground / a carcass (0.22 m + 2 % of hip height) |
| 2 | Drink | Drink | loop; mouth at the water line (ground level), 4 laps, lift to swallow |
| 3 | Rest | Rest_Down -> Rest_Loop -> Rest_Up | **changed**: lie down (hind, front, head), sleep loop (slow breathing), get up when ActionType changes. An Action pulse with ActionType 3 while lying plays Rest_Shift (head up, look, settle). Speed > max(2, 1.3 x walk) while lying bolts straight to Locomotion; Speed > max(1.5, 1.3 x walk) cuts Rest_Up short |
| 4 | LookAround | Look | one-shot |
| 5 | Roar | Roar | one-shot (threat) |
| 6 | Call | **Call** | one-shot (was Roar): hoot with crest raised (parasaurolophus), low bellow (triceratops / ankylosaurus), three barks (raptor), closed-mouth rumble + short open end (big hunters) |
| 7 | Threaten | **Defend** (grazers) / Roar (hunters) | grazers hold the Defend loop while ActionType is 7 or 8 |
| 8 | Defend | **Defend** | loop: triceratops horns low, pawing, hooking; ankylosaurus body turned, tail club raised and swinging; parasaurolophus rears and stamps |
| 9 | Investigate | Look | one-shot (was unmapped) |
| 10 | Charge | Charge | loop (grazers; reworked: head low, horns forward) |
| **11** | IdleVariant (new) | Idle_Variation | one-shot (it used to play for Rest) |
| **12** | Breathe (new) | Breathe | loop: heavy breathing after exertion, hunters pant with the mouth open |
| **13** | Recover (new) | Recover | one-shot: step back, head shake, reset (also plays automatically after Heavy_Attack) |

The runtime constants 11-13 do not exist in `Animation/AnimParams.cs` (U's file): request to append
`IdleVariant = 11, Breathe = 12, Recover = 13` to `DinoActions`; until then use the numbers. Editor mirror:
`PrimalCharacterBuilder.DinoAnimIds`.

**Standing-only starts:** one-shots and loops start from standing states (Locomotion, Alert, Turn, Stop, the other actions),
not from the rest states. A lying animal given another ActionType first plays Rest_Up, then the pending trigger fires.
Attack, Hurt and Death still start from anywhere. State tags: `Rest` (all four rest states), `Action`, `Alert`, `Turn`,
`Stop`, `Attack`, `Hurt`, `Dead`.

## 3. What AI should do (requests)

1. Rest: keep Speed 0 while the state tag is `Rest` (Rest_Up takes 1.3 s raptor to 3.0 s Rift Tyrant; see tables), else
   the body slides while it gets up. Midday rest and night sleep can both use ActionType 3 (herds lying together).
2. Chase / Flee: set Intensity toward 1 while chasing (hunters) or fleeing (grazers), 0 otherwise.
3. Turning in place: Turn = signed yaw rate (deg/s, + right), TurnMul = |rate| / authored rate.
4. Stop: fire when Speed drops from above walk speed to below 0.3 within about a second (not while turning).
5. Bite (AttackType 2) for quick close snaps (raptor pack nips, small hunters), Recover (ActionType 13) after a missed lunge.
6. Call (6) now has its own clip; Threaten (7) on triceratops / ankylosaurus / parasaurolophus holds Defend until the next state.
7. Breathe (12) after a long chase or flight.
8. Events added: OnEat (crop / bite / tear / swallow), OnDrink (laps), OnCall, OnBreath, OnBodyFall "rest" (lying down: dust +
   thud already handled by DinosaurController), OnFootstep "F" (front stamp in Defend), footsteps in Stop / Turn / Rest_Up /
   Recover. All names exist on CharacterAnimationEvents (no "no receiver" errors); DinosaurController reacts to OnFootstep,
   OnBite / OnHornHit / OnTailHit (only with a pending hit) and OnBodyFall.

## 4. Speeds, turn rates, weight

| species | weight | walk | run = Chase / Flee | Charge | turn clip authored for | Stop | Rest_Down / Rest_Up |
|---|---|---|---|---|---|---|---|
| parasaurolophus | 0.62 | 1.7 | 7.0 | 6.3 | 49 deg/s | 1.20 s | 2.77 / 2.27 s |
| triceratops | 0.85 | 1.6 | 5.5 | 4.95 | 27 deg/s | 1.43 s | 3.27 / 2.70 s |
| ankylosaurus | 0.90 | 1.2 | 3.2 | 2.88 | 22 deg/s | 1.50 s | 3.37 / 2.80 s |
| velociraptor | 0.08 | 1.5 | 11.0 | - | 240 deg/s | 0.63 s | 1.57 / 1.27 s |
| carnotaurus | 0.55 | 1.8 | 9.5 | - | 52 deg/s | 1.13 s | 2.60 / 2.13 s |
| spinosaurus | 0.82 | 1.9 | 7.5 | - | 27 deg/s | 1.40 s | 3.20 / 2.67 s |
| apex | 1.00 | 2.0 | 7.0 | - | 22 deg/s | 1.60 s | 3.60 / 3.00 s |

Weight (same numbers as AI's WildlifePlan) scales: stop lurch and damped settle length, step timing, body sway / roll,
tail lag and counter-swing, impact dip after each footfall in Chase / Flee / Charge, head stabilisation (small animals keep
the head still), lie-down / get-up duration and the settle bounce at ground contact. Walk and Run are unchanged
(AI's DinoLife adds the runtime sway / lean / tail against turns on top).

## 5. Clips per species

### Parasaurolophus

| clip | status | frames (s) | loop | speed m/s | events (frame: function param) |
|---|---|---|---|---|---|
| Idle | kept | 120 (4.00) | Y | - | - |
| Idle_Variation | kept | 150 (5.00) | Y | - | - |
| Walk | kept | 34 (1.13) | Y | 1.7 | 1: OnFootstep L, 17: OnFootstep R |
| Run | kept | 21 (0.70) | Y | 7 | 1: OnFootstep L, 10: OnFootstep R |
| Turn_Left (authored for 49 deg/s in place) | reworked | 37 (1.23) | Y | - | 1: OnFootstep L, 19: OnFootstep R |
| Turn_Right (authored for 49 deg/s in place) | reworked | 37 (1.23) | Y | - | 1: OnFootstep L, 19: OnFootstep R |
| Eat | reworked | 150 (5.00) | Y | - | 25: OnEat crop, 55: OnEat crop, 85: OnEat crop |
| Drink | reworked | 120 (4.00) | Y | - | 17: OnDrink, 32: OnDrink, 46: OnDrink, 61: OnDrink |
| Look | kept | 90 (3.00) | N | - | - |
| Alert | kept | 60 (2.00) | Y | - | - |
| Roar | kept | 90 (3.00) | N | - | - |
| Attack | kept | 42 (1.40) | N | - | 17: OnHornHit |
| Heavy_Attack | kept | 60 (2.00) | N | - | 31: OnHornHit heavy |
| Hurt | kept | 30 (1.00) | N | - | - |
| Death | kept | 75 (2.50) | N | - | 60: OnBodyFall |
| Charge | reworked | 21 (0.70) | Y | 6.3 | 1: OnFootstep L, 10: OnFootstep R |
| Stop | new | 36 (1.20) | N | - | 11: OnFootstep L, 14: OnFootstep R |
| Breathe | new | 67 (2.23) | Y | - | 26: OnBreath, 59: OnBreath |
| Rest_Down | new | 83 (2.77) | N | - | 74: OnBodyFall rest |
| Rest_Loop | new | 176 (5.87) | Y | - | - |
| Rest_Shift | new | 92 (3.07) | N | - | - |
| Rest_Up | new | 68 (2.27) | N | - | 41: OnFootstep L, 58: OnFootstep R |
| Call | new | 120 (4.00) | N | - | 29: OnCall 1, 69: OnCall 2 |
| Flee | new | 20 (0.67) | Y | 7 | 1: OnFootstep L, 10: OnFootstep R |
| Defend | new | 90 (3.00) | Y | - | 55: OnFootstep F, 19: OnCall |

### Triceratops

| clip | status | frames (s) | loop | speed m/s | events (frame: function param) |
|---|---|---|---|---|---|
| Idle | kept | 120 (4.00) | Y | - | - |
| Idle_Variation | kept | 150 (5.00) | Y | - | - |
| Walk | kept | 33 (1.10) | Y | 1.6 | 1: OnFootstep L, 16: OnFootstep R |
| Run | kept | 22 (0.73) | Y | 5.5 | 1: OnFootstep L, 11: OnFootstep R |
| Turn_Left (authored for 27 deg/s in place) | reworked | 39 (1.30) | Y | - | 1: OnFootstep L, 20: OnFootstep R |
| Turn_Right (authored for 27 deg/s in place) | reworked | 39 (1.30) | Y | - | 1: OnFootstep L, 20: OnFootstep R |
| Eat | reworked | 150 (5.00) | Y | - | 25: OnEat crop, 55: OnEat crop, 85: OnEat crop |
| Drink | reworked | 120 (4.00) | Y | - | 17: OnDrink, 32: OnDrink, 46: OnDrink, 61: OnDrink |
| Look | kept | 90 (3.00) | N | - | - |
| Alert | kept | 60 (2.00) | Y | - | - |
| Roar | kept | 90 (3.00) | N | - | - |
| Attack | kept | 42 (1.40) | N | - | 17: OnHornHit |
| Heavy_Attack | kept | 60 (2.00) | N | - | 31: OnHornHit heavy |
| Hurt | kept | 30 (1.00) | N | - | - |
| Death | kept | 75 (2.50) | N | - | 60: OnBodyFall |
| Charge | reworked | 22 (0.73) | Y | 4.95 | 1: OnFootstep L, 11: OnFootstep R |
| Stop | new | 43 (1.43) | N | - | 13: OnFootstep L, 17: OnFootstep R |
| Breathe | new | 81 (2.70) | Y | - | 31: OnBreath, 72: OnBreath |
| Rest_Down | new | 98 (3.27) | N | - | 87: OnBodyFall rest |
| Rest_Loop | new | 196 (6.53) | Y | - | - |
| Rest_Shift | new | 102 (3.40) | N | - | - |
| Rest_Up | new | 81 (2.70) | N | - | 49: OnFootstep L, 69: OnFootstep R |
| Call | new | 105 (3.50) | N | - | 45: OnCall |
| Flee | new | 21 (0.70) | Y | 5.5 | 1: OnFootstep L, 10: OnFootstep R |
| Defend | new | 90 (3.00) | Y | - | 38: OnFootstep F, 63: OnBreath snort |

### Ankylosaurus

| clip | status | frames (s) | loop | speed m/s | events (frame: function param) |
|---|---|---|---|---|---|
| Idle | kept | 120 (4.00) | Y | - | - |
| Idle_Variation | kept | 150 (5.00) | Y | - | - |
| Walk | kept | 30 (1.00) | Y | 1.2 | 1: OnFootstep L, 15: OnFootstep R |
| Run | kept | 21 (0.70) | Y | 3.2 | 1: OnFootstep L, 10: OnFootstep R |
| Turn_Left (authored for 22 deg/s in place) | reworked | 36 (1.20) | Y | - | 1: OnFootstep L, 19: OnFootstep R |
| Turn_Right (authored for 22 deg/s in place) | reworked | 36 (1.20) | Y | - | 1: OnFootstep L, 19: OnFootstep R |
| Eat | reworked | 150 (5.00) | Y | - | 25: OnEat crop, 55: OnEat crop, 85: OnEat crop |
| Drink | reworked | 120 (4.00) | Y | - | 17: OnDrink, 32: OnDrink, 46: OnDrink, 61: OnDrink |
| Look | kept | 90 (3.00) | N | - | - |
| Alert | kept | 60 (2.00) | Y | - | - |
| Roar | kept | 90 (3.00) | N | - | - |
| Attack | kept | 42 (1.40) | N | - | 17: OnTailHit |
| Heavy_Attack | kept | 60 (2.00) | N | - | 31: OnHornHit heavy |
| Hurt | kept | 30 (1.00) | N | - | - |
| Death | kept | 75 (2.50) | N | - | 60: OnBodyFall |
| Charge | reworked | 21 (0.70) | Y | 2.88 | 1: OnFootstep L, 10: OnFootstep R |
| Stop | new | 45 (1.50) | N | - | 13: OnFootstep L, 17: OnFootstep R |
| Breathe | new | 84 (2.80) | Y | - | 32: OnBreath, 74: OnBreath |
| Rest_Down | new | 101 (3.37) | N | - | 89: OnBodyFall rest |
| Rest_Loop | new | 201 (6.70) | Y | - | - |
| Rest_Shift | new | 104 (3.47) | N | - | - |
| Rest_Up | new | 84 (2.80) | N | - | 51: OnFootstep L, 72: OnFootstep R |
| Call | new | 105 (3.50) | N | - | 45: OnCall |
| Flee | new | 20 (0.67) | Y | 3.2 | 1: OnFootstep L, 10: OnFootstep R |
| Defend | new | 120 (4.00) | Y | - | 31: OnBreath snort |

### Velociraptor

| clip | status | frames (s) | loop | speed m/s | events (frame: function param) |
|---|---|---|---|---|---|
| Idle | kept | 120 (4.00) | Y | - | - |
| Idle_Variation | kept | 150 (5.00) | Y | - | - |
| Walk | kept | 18 (0.60) | Y | 1.5 | 1: OnFootstep L, 9: OnFootstep R |
| Run | kept | 11 (0.37) | Y | 11 | 1: OnFootstep L, 5: OnFootstep R |
| Turn_Left (authored for 240 deg/s in place) | reworked | 15 (0.50) | Y | - | 1: OnFootstep L, 8: OnFootstep R |
| Turn_Right (authored for 240 deg/s in place) | reworked | 15 (0.50) | Y | - | 1: OnFootstep L, 8: OnFootstep R |
| Eat | reworked | 150 (5.00) | Y | - | 28: OnEat bite, 55: OnEat tear, 113: OnEat swallow |
| Drink | reworked | 120 (4.00) | Y | - | 17: OnDrink, 32: OnDrink, 46: OnDrink, 61: OnDrink |
| Look | kept | 90 (3.00) | N | - | - |
| Alert | kept | 60 (2.00) | Y | - | - |
| Roar | kept | 90 (3.00) | N | - | - |
| Attack | kept | 42 (1.40) | N | - | 17: OnBite |
| Heavy_Attack | kept | 60 (2.00) | N | - | 31: OnBite heavy |
| Hurt | kept | 30 (1.00) | N | - | - |
| Death | kept | 75 (2.50) | N | - | 60: OnBodyFall |
| Stop | new | 19 (0.63) | N | - | 7: OnFootstep L |
| Breathe | new | 35 (1.17) | Y | - | 14: OnBreath, 31: OnBreath |
| Rest_Down | new | 47 (1.57) | N | - | 38: OnBodyFall rest |
| Rest_Loop | new | 127 (4.23) | Y | - | - |
| Rest_Shift | new | 69 (2.30) | N | - | - |
| Rest_Up | new | 38 (1.27) | N | - | 23: OnFootstep L, 33: OnFootstep R |
| Call | new | 45 (1.50) | N | - | 11: OnCall 0, 22: OnCall 1, 33: OnCall 2 |
| Chase | new | 10 (0.33) | Y | 11 | 1: OnFootstep L, 5: OnFootstep R |
| Bite | new | 14 (0.47) | N | - | 6: OnBite bite |
| Recover | new | 26 (0.87) | N | - | 11: OnFootstep R, 24: OnFootstep R |

### Carnotaurus

| clip | status | frames (s) | loop | speed m/s | events (frame: function param) |
|---|---|---|---|---|---|
| Idle | kept | 120 (4.00) | Y | - | - |
| Idle_Variation | kept | 150 (5.00) | Y | - | - |
| Walk | kept | 30 (1.00) | Y | 1.8 | 1: OnFootstep L, 15: OnFootstep R |
| Run | kept | 16 (0.53) | Y | 9.5 | 1: OnFootstep L, 8: OnFootstep R |
| Turn_Left (authored for 52 deg/s in place) | reworked | 31 (1.03) | Y | - | 1: OnFootstep L, 16: OnFootstep R |
| Turn_Right (authored for 52 deg/s in place) | reworked | 31 (1.03) | Y | - | 1: OnFootstep L, 16: OnFootstep R |
| Eat | reworked | 150 (5.00) | Y | - | 28: OnEat bite, 55: OnEat tear, 113: OnEat swallow |
| Drink | reworked | 120 (4.00) | Y | - | 17: OnDrink, 32: OnDrink, 46: OnDrink, 61: OnDrink |
| Look | kept | 90 (3.00) | N | - | - |
| Alert | kept | 60 (2.00) | Y | - | - |
| Roar | kept | 90 (3.00) | N | - | - |
| Attack | kept | 42 (1.40) | N | - | 17: OnBite |
| Heavy_Attack | kept | 60 (2.00) | N | - | 31: OnBite heavy |
| Hurt | kept | 30 (1.00) | N | - | - |
| Death | kept | 75 (2.50) | N | - | 60: OnBodyFall |
| Stop | new | 34 (1.13) | N | - | 11: OnFootstep L |
| Breathe | new | 63 (2.10) | Y | - | 24: OnBreath, 56: OnBreath |
| Rest_Down | new | 78 (2.60) | N | - | 63: OnBodyFall rest |
| Rest_Loop | new | 170 (5.67) | Y | - | - |
| Rest_Shift | new | 89 (2.97) | N | - | - |
| Rest_Up | new | 64 (2.13) | N | - | 39: OnFootstep L, 55: OnFootstep R |
| Call | new | 105 (3.50) | N | - | 15: OnCall rumble |
| Chase | new | 16 (0.53) | Y | 9.5 | 1: OnFootstep L, 8: OnFootstep R |
| Bite | new | 20 (0.67) | N | - | 9: OnBite bite |
| Recover | new | 37 (1.23) | N | - | 15: OnFootstep R, 34: OnFootstep R |

### Spinosaurus

| clip | status | frames (s) | loop | speed m/s | events (frame: function param) |
|---|---|---|---|---|---|
| Idle | kept | 120 (4.00) | Y | - | - |
| Idle_Variation | kept | 150 (5.00) | Y | - | - |
| Walk | kept | 38 (1.27) | Y | 1.9 | 1: OnFootstep L, 19: OnFootstep R |
| Run | kept | 21 (0.70) | Y | 7.5 | 1: OnFootstep L, 10: OnFootstep R |
| Turn_Left (authored for 27 deg/s in place) | reworked | 44 (1.47) | Y | - | 1: OnFootstep L, 23: OnFootstep R |
| Turn_Right (authored for 27 deg/s in place) | reworked | 44 (1.47) | Y | - | 1: OnFootstep L, 23: OnFootstep R |
| Eat | reworked | 150 (5.00) | Y | - | 28: OnEat bite, 55: OnEat tear, 113: OnEat swallow |
| Drink | reworked | 120 (4.00) | Y | - | 17: OnDrink, 32: OnDrink, 46: OnDrink, 61: OnDrink |
| Look | kept | 90 (3.00) | N | - | - |
| Alert | kept | 60 (2.00) | Y | - | - |
| Roar | kept | 90 (3.00) | N | - | - |
| Attack | kept | 42 (1.40) | N | - | 17: OnBite |
| Heavy_Attack | kept | 60 (2.00) | N | - | 31: OnBite heavy |
| Hurt | kept | 30 (1.00) | N | - | - |
| Death | kept | 75 (2.50) | N | - | 60: OnBodyFall |
| Stop | new | 42 (1.40) | N | - | 13: OnFootstep L |
| Breathe | new | 79 (2.63) | Y | - | 31: OnBreath, 70: OnBreath |
| Rest_Down | new | 96 (3.20) | N | - | 77: OnBodyFall rest |
| Rest_Loop | new | 194 (6.47) | Y | - | - |
| Rest_Shift | new | 100 (3.33) | N | - | - |
| Rest_Up | new | 80 (2.67) | N | - | 49: OnFootstep L, 69: OnFootstep R |
| Call | new | 105 (3.50) | N | - | 15: OnCall rumble |
| Chase | new | 20 (0.67) | Y | 7.5 | 1: OnFootstep L, 10: OnFootstep R |
| Bite | new | 23 (0.77) | N | - | 10: OnBite bite |
| Recover | new | 44 (1.47) | N | - | 18: OnFootstep R, 40: OnFootstep R |

### Apex (Rift Tyrant)

| clip | status | frames (s) | loop | speed m/s | events (frame: function param) |
|---|---|---|---|---|---|
| Idle | kept | 120 (4.00) | Y | - | - |
| Idle_Variation | kept | 150 (5.00) | Y | - | - |
| Walk | kept | 39 (1.30) | Y | 2 | 1: OnFootstep L, 19: OnFootstep R |
| Run | kept | 23 (0.77) | Y | 7 | 1: OnFootstep L, 11: OnFootstep R |
| Turn_Left (authored for 22 deg/s in place) | reworked | 49 (1.63) | Y | - | 1: OnFootstep L, 25: OnFootstep R |
| Turn_Right (authored for 22 deg/s in place) | reworked | 49 (1.63) | Y | - | 1: OnFootstep L, 25: OnFootstep R |
| Eat | reworked | 150 (5.00) | Y | - | 28: OnEat bite, 55: OnEat tear, 113: OnEat swallow |
| Drink | reworked | 120 (4.00) | Y | - | 17: OnDrink, 32: OnDrink, 46: OnDrink, 61: OnDrink |
| Look | kept | 90 (3.00) | N | - | - |
| Alert | kept | 60 (2.00) | Y | - | - |
| Roar | kept | 90 (3.00) | N | - | - |
| Attack | kept | 42 (1.40) | N | - | 17: OnBite |
| Heavy_Attack | kept | 60 (2.00) | N | - | 31: OnBite heavy |
| Hurt | kept | 30 (1.00) | N | - | - |
| Death | kept | 75 (2.50) | N | - | 60: OnBodyFall |
| Stop | new | 48 (1.60) | N | - | 14: OnFootstep L |
| Breathe | new | 90 (3.00) | Y | - | 35: OnBreath, 80: OnBreath |
| Rest_Down | new | 108 (3.60) | N | - | 87: OnBodyFall rest |
| Rest_Loop | new | 210 (7.00) | Y | - | - |
| Rest_Shift | new | 108 (3.60) | N | - | - |
| Rest_Up | new | 90 (3.00) | N | - | 55: OnFootstep L, 77: OnFootstep R |
| Call | new | 105 (3.50) | N | - | 15: OnCall rumble |
| Chase | new | 22 (0.73) | Y | 7 | 1: OnFootstep L, 11: OnFootstep R |
| Bite | new | 26 (0.87) | N | - | 11: OnBite bite |
| Recover | new | 48 (1.60) | N | - | 20: OnFootstep R, 44: OnFootstep R |

