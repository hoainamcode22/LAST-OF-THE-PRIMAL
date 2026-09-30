# Parasaurolophus automated test

Result: **PASS**

```
=== Parasaurolophus === 2026-09-30 14:22:20
Avatar: valid=True human=False
Importer: 25 clips configured (14 loops, 33 events)
Materials: M_Dino_Parasaurolophus
Clips in FBX: 25 (meta: 25)
Controller: Assets/Art/Characters/Dinosaurs/Parasaurolophus/Animations/ParasaurolophusAnimator.controller (walk 1,70 m/s, run 7,00 m/s, 22 states, 176 standing-start transitions, run slot Run / Flee on Intensity, rest Rest_Down / Rest_Loop / Rest_Shift / Rest_Up)
LOD: 3 levels (LOD0:24492 tris, LOD1:11020 tris, LOD2:3918 tris)
Prefab: Assets/Art/Characters/Dinosaurs/Parasaurolophus/Prefab/DINO_Parasaurolophus.prefab
Scale: bounds size (1.73, 4.73, 9.08), min y -0,194
Collider: capsule r=0,55 h=6,54 (mesh (1.73, 4.73, 9.08))
Facing: pelvis -> head (0.00, 0.24, 0.97) (expected +Z)
Alert              2,00s loop=Y events=0 loopGap=0,0cm
Attack             1,40s loop=N events=1
Breathe            2,23s loop=Y events=2 loopGap=0,0cm
Call               4,00s loop=N events=2
Charge             0,70s loop=Y events=2 loopGap=0,0cm footSlide=0,18m/s (3% of 6,30) contactVel L(0.00, 0.00, -6.28) (8f) R(0.00, 0.00, -6.30) (7f)
Death              2,50s loop=N events=1
Defend             3,00s loop=Y events=2 loopGap=0,0cm
Drink              4,00s loop=Y events=4 loopGap=0,0cm
Eat                5,00s loop=Y events=3 loopGap=0,0cm
Flee               0,67s loop=Y events=2 loopGap=0,0cm footSlide=0,16m/s (2% of 7,00) contactVel L(-0.01, 0.00, -7.02) (7f) R(0.00, 0.00, -7.02) (7f)
Heavy_Attack       2,00s loop=N events=1
Hurt               1,00s loop=N events=0
Idle               4,00s loop=Y events=0 loopGap=0,0cm
Idle_Variation     5,00s loop=Y events=0 loopGap=0,0cm
Look               3,00s loop=N events=0
Rest_Down          2,77s loop=N events=1
Rest_Loop          5,87s loop=Y events=0 loopGap=0,0cm
Rest_Shift         3,07s loop=N events=0
Rest_Up            2,27s loop=N events=2
Roar               3,00s loop=N events=0
Run                0,70s loop=Y events=2 loopGap=0,0cm footSlide=0,16m/s (2% of 7,00) contactVel L(0.00, 0.00, -6.98) (8f) R(0.00, 0.00, -7.01) (7f)
Stop               1,20s loop=N events=2
Turn_Left          1,23s loop=Y events=2 loopGap=0,0cm
Turn_Right         1,23s loop=Y events=2 loopGap=0,0cm
Walk               1,13s loop=Y events=2 loopGap=0,0cm footSlide=0,09m/s (5% of 1,70) contactVel L(0.01, 0.00, -1.70) (22f) R(-0.01, 0.00, -1.70) (22f)
Sampler check: max bone travel across clips 3,97 m
Ground contact (IDLE frame 0, skinned vertices): min y -0,002 m
Skin weights: 0 of 15306 LOD0 vertices without bones
Skin check (Walk): 100,0% of LOD0 vertices move with the bones
Screenshots: Documentation/CharacterTests/Parasaurolophus_clips.png (25 clips, 3/4 front view, character faces +Z)
RESULT: PASS
```
