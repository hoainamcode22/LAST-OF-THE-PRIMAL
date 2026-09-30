# Carnotaurus automated test

Result: **PASS**

```
=== Carnotaurus === 2026-09-30 14:23:20
Avatar: valid=True human=False
Importer: 25 clips configured (12 loops, 30 events)
Materials: M_Dino_Carnotaurus
Clips in FBX: 25 (meta: 25)
Controller: Assets/Art/Characters/Dinosaurs/Carnotaurus/Animations/CarnotaurusAnimator.controller (walk 1,80 m/s, run 9,50 m/s, 22 states, 160 standing-start transitions, run slot Run / Chase on Intensity, rest Rest_Down / Rest_Loop / Rest_Shift / Rest_Up)
LOD: 3 levels (LOD0:24656 tris, LOD1:11094 tris, LOD2:3944 tris)
Prefab: Assets/Art/Characters/Dinosaurs/Carnotaurus/Prefab/DINO_Carnotaurus.prefab
Scale: bounds size (1.91, 4.11, 7.80), min y -0,215
Collider: capsule r=0,61 h=5,62 (mesh (1.91, 4.11, 7.80))
Facing: pelvis -> head (0.00, 0.35, 0.94) (expected +Z)
Alert              2,00s loop=Y events=0 loopGap=0,0cm
Attack             1,40s loop=N events=1
Bite               0,67s loop=N events=1
Breathe            2,10s loop=Y events=2 loopGap=0,0cm
Call               3,50s loop=N events=1
Chase              0,53s loop=Y events=2 loopGap=0,0cm footSlide=0,17m/s (2% of 9,50) contactVel L(-0.02, 0.00, -9.50) (5f) R(-0.01, 0.00, -9.51) (5f)
Death              2,50s loop=N events=1
Drink              4,00s loop=Y events=4 loopGap=0,0cm
Eat                5,00s loop=Y events=3 loopGap=0,0cm
Heavy_Attack       2,00s loop=N events=1
Hurt               1,00s loop=N events=0
Idle               4,00s loop=Y events=0 loopGap=0,0cm
Idle_Variation     5,00s loop=Y events=0 loopGap=0,0cm
Look               3,00s loop=N events=0
Recover            1,23s loop=N events=2
Rest_Down          2,60s loop=N events=1
Rest_Loop          5,67s loop=Y events=0 loopGap=0,0cm
Rest_Shift         2,97s loop=N events=0
Rest_Up            2,13s loop=N events=2
Roar               3,00s loop=N events=0
Run                0,53s loop=Y events=2 loopGap=0,0cm footSlide=0,11m/s (1% of 9,50) contactVel L(-0.02, 0.00, -9.50) (5f) R(-0.01, 0.00, -9.51) (5f)
Stop               1,13s loop=N events=1
Turn_Left          1,03s loop=Y events=2 loopGap=0,0cm
Turn_Right         1,03s loop=Y events=2 loopGap=0,0cm
Walk               1,00s loop=Y events=2 loopGap=0,0cm footSlide=0,10m/s (6% of 1,80) contactVel L(0.01, 0.00, -1.80) (18f) R(-0.01, 0.00, -1.80) (18f)
Sampler check: max bone travel across clips 2,87 m
Ground contact (IDLE frame 0, skinned vertices): min y -0,021 m
Skin weights: 0 of 15442 LOD0 vertices without bones
Skin check (Walk): 100,0% of LOD0 vertices move with the bones
Screenshots: Documentation/CharacterTests/Carnotaurus_clips.png (25 clips, 3/4 front view, character faces +Z)
RESULT: PASS
```
