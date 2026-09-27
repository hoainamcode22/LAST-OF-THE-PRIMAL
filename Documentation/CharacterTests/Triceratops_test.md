# Triceratops automated test

Result: **PASS**

```
=== Triceratops === 2026-09-27 15:41:12
Avatar: valid=True human=False
Importer: 16 clips configured (10 loops, 9 events)
Materials: M_Dino_Triceratops
Clips in FBX: 16 (meta: 16)
Controller: Assets/Art/Characters/Dinosaurs/Triceratops/Animations/TriceratopsAnimator.controller (walk 1,60 m/s, run 5,50 m/s, 12 states)
LOD: 3 levels (LOD0:26528 tris, LOD1:11936 tris, LOD2:4244 tris)
Prefab: Assets/Art/Characters/Dinosaurs/Triceratops/Prefab/DINO_Triceratops.prefab
Scale: bounds size (2.41, 3.98, 9.62), min y -0,250
Collider: capsule r=0,77 h=6,93 (mesh (2.41, 3.98, 9.62))
Facing: pelvis -> head (0.00, -0.04, 1.00) (expected +Z)
Clips not referenced by the controller: Turn_Left, Turn_Right
Alert              2,00s loop=Y events=0 loopGap=0,0cm
Attack             1,40s loop=N events=1
Charge             0,73s loop=Y events=2 loopGap=0,0cm footSlide=0,13m/s (3% of 4,95) contactVel L(0.00, 0.00, -5.06) (9f) R(0.00, 0.00, -5.07) (9f)
Death              2,50s loop=N events=1
Drink              4,00s loop=Y events=0 loopGap=0,0cm
Eat                4,00s loop=Y events=0 loopGap=0,0cm
Heavy_Attack       2,00s loop=N events=1
Hurt               1,00s loop=N events=0
Idle               4,00s loop=Y events=0 loopGap=0,0cm
Idle_Variation     5,00s loop=Y events=0 loopGap=0,0cm
Look               3,00s loop=N events=0
Roar               3,00s loop=N events=0
Run                0,73s loop=Y events=2 loopGap=0,0cm footSlide=0,15m/s (3% of 5,50) contactVel L(0.00, 0.00, -5.62) (9f) R(0.00, 0.00, -5.63) (9f)
Turn_Left          1,10s loop=Y events=0 loopGap=0,0cm
Turn_Right         1,10s loop=Y events=0 loopGap=0,0cm
Walk               1,10s loop=Y events=2 loopGap=0,0cm footSlide=0,07m/s (4% of 1,60) contactVel L(0.01, 0.00, -1.62) (22f) R(-0.01, 0.00, -1.60) (22f)
Sampler check: max bone travel across clips 3,13 m
Ground contact (IDLE frame 0, skinned vertices): min y 0,000 m
Skin weights: 0 of 17341 LOD0 vertices without bones
Skin check (Walk): 100,0% of LOD0 vertices move with the bones
Screenshots: Documentation/CharacterTests/Triceratops_clips.png (16 clips, 3/4 front view, character faces +Z)
RESULT: PASS
```
