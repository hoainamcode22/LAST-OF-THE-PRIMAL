# Apex automated test

Result: **PASS**

```
=== Apex === 2026-09-27 18:03:20
Avatar: valid=True human=False
Importer: 15 clips configured (9 loops, 7 events)
Materials: M_Dino_Apex, M_Dino_Apex_Eye, M_Dino_Apex_Mouth
Clips in FBX: 15 (meta: 15)
Controller: Assets/Art/Characters/Dinosaurs/Apex/Animations/ApexAnimator.controller (walk 2,00 m/s, run 7,00 m/s, 11 states)
LOD: 4 levels (LOD0:66260 tris, LOD1:27828 tris, LOD2:9120 tris, LOD3:3040 tris)
Prefab: Assets/Art/Characters/Dinosaurs/Apex/Prefab/DINO_Apex.prefab
Scale: bounds size (2.14, 6.11, 11.31), min y -0,284
Collider: capsule r=0,69 h=8,14 (mesh (2.14, 6.11, 11.31))
Facing: pelvis -> head (0.00, 0.32, 0.95) (expected +Z)
Clips not referenced by the controller: Turn_Left, Turn_Right
Alert              2,00s loop=Y events=0 loopGap=0,0cm
Attack             1,40s loop=N events=1
Death              2,50s loop=N events=1
Drink              4,00s loop=Y events=0 loopGap=0,0cm
Eat                4,00s loop=Y events=0 loopGap=0,0cm
Heavy_Attack       2,00s loop=N events=1
Hurt               1,00s loop=N events=0
Idle               4,00s loop=Y events=0 loopGap=0,0cm
Idle_Variation     5,00s loop=Y events=0 loopGap=0,0cm
Look               3,00s loop=N events=0
Roar               3,00s loop=N events=0
Run                0,77s loop=Y events=2 loopGap=0,0cm footSlide=0,17m/s (2% of 7,00) contactVel L(0.00, 0.00, -7.00) (9f) R(-0.04, 0.00, -7.03) (6f)
Turn_Left          1,30s loop=Y events=0 loopGap=0,0cm
Turn_Right         1,30s loop=Y events=0 loopGap=0,0cm
Walk               1,30s loop=Y events=2 loopGap=0,0cm footSlide=0,07m/s (3% of 2,00) contactVel L(0.01, 0.00, -2.00) (25f) R(-0.01, 0.00, -1.98) (24f)
Sampler check: max bone travel across clips 4,39 m
Ground contact (IDLE frame 0, skinned vertices): min y 0,000 m
Skin weights: 0 of 42651 LOD0 vertices without bones
Skin check (Walk): 100,0% of LOD0 vertices move with the bones
Screenshots: Documentation/CharacterTests/Apex_clips.png (15 clips, 3/4 front view, character faces +Z)
RESULT: PASS
```
