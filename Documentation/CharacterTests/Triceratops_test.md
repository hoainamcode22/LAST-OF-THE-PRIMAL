# Triceratops automated test

Result: **PASS**

```
=== Triceratops === 2026-09-30 14:22:39
Avatar: valid=True human=False
Importer: 25 clips configured (14 loops, 32 events)
Materials: M_Dino_Triceratops
Clips in FBX: 25 (meta: 25)
Controller: Assets/Art/Characters/Dinosaurs/Triceratops/Animations/TriceratopsAnimator.controller (walk 1,60 m/s, run 5,50 m/s, 22 states, 176 standing-start transitions, run slot Run / Flee on Intensity, rest Rest_Down / Rest_Loop / Rest_Shift / Rest_Up)
LOD: 3 levels (LOD0:26528 tris, LOD1:11936 tris, LOD2:4244 tris)
Prefab: Assets/Art/Characters/Dinosaurs/Triceratops/Prefab/DINO_Triceratops.prefab
Scale: bounds size (2.41, 3.98, 9.62), min y -0,250
Collider: capsule r=0,77 h=6,93 (mesh (2.41, 3.98, 9.62))
Facing: pelvis -> head (0.00, -0.04, 1.00) (expected +Z)
Alert              2,00s loop=Y events=0 loopGap=0,0cm
Attack             1,40s loop=N events=1
Breathe            2,70s loop=Y events=2 loopGap=0,0cm
Call               3,50s loop=N events=1
Charge             0,73s loop=Y events=2 loopGap=0,0cm footSlide=0,15m/s (3% of 4,95) contactVel L(0.00, 0.00, -4.95) (9f) R(0.00, 0.00, -4.95) (9f)
Death              2,50s loop=N events=1
Defend             3,00s loop=Y events=2 loopGap=0,0cm
Drink              4,00s loop=Y events=4 loopGap=0,0cm
Eat                5,00s loop=Y events=3 loopGap=0,0cm
Flee               0,70s loop=Y events=2 loopGap=0,0cm footSlide=0,16m/s (3% of 5,50) contactVel L(0.00, 0.00, -5.50) (8f) R(0.00, 0.00, -5.50) (8f)
Heavy_Attack       2,00s loop=N events=1
Hurt               1,00s loop=N events=0
Idle               4,00s loop=Y events=0 loopGap=0,0cm
Idle_Variation     5,00s loop=Y events=0 loopGap=0,0cm
Look               3,00s loop=N events=0
Rest_Down          3,27s loop=N events=1
Rest_Loop          6,53s loop=Y events=0 loopGap=0,0cm
Rest_Shift         3,40s loop=N events=0
Rest_Up            2,70s loop=N events=2
Roar               3,00s loop=N events=0
Run                0,73s loop=Y events=2 loopGap=0,0cm footSlide=0,10m/s (2% of 5,50) contactVel L(0.00, 0.00, -5.50) (9f) R(0.00, 0.00, -5.50) (9f)
Stop               1,43s loop=N events=2
Turn_Left          1,30s loop=Y events=2 loopGap=0,0cm
Turn_Right         1,30s loop=Y events=2 loopGap=0,0cm
Walk               1,10s loop=Y events=2 loopGap=0,0cm footSlide=0,07m/s (4% of 1,60) contactVel L(0.01, 0.00, -1.62) (22f) R(-0.01, 0.00, -1.60) (22f)
Sampler check: max bone travel across clips 3,13 m
Ground contact (IDLE frame 0, skinned vertices): min y 0,000 m
Skin weights: 0 of 17341 LOD0 vertices without bones
Skin check (Walk): 100,0% of LOD0 vertices move with the bones
Screenshots: Documentation/CharacterTests/Triceratops_clips.png (25 clips, 3/4 front view, character faces +Z)
RESULT: PASS
```
