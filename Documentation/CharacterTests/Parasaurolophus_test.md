# Parasaurolophus automated test

Result: **PASS**

```
=== Parasaurolophus === 2026-09-27 15:40:58
Avatar: valid=True human=False
Importer: 16 clips configured (10 loops, 9 events)
Materials: M_Dino_Parasaurolophus
Clips in FBX: 16 (meta: 16)
Controller: Assets/Art/Characters/Dinosaurs/Parasaurolophus/Animations/ParasaurolophusAnimator.controller (walk 1,70 m/s, run 7,00 m/s, 12 states)
LOD: 3 levels (LOD0:24492 tris, LOD1:11020 tris, LOD2:3918 tris)
Prefab: Assets/Art/Characters/Dinosaurs/Parasaurolophus/Prefab/DINO_Parasaurolophus.prefab
Scale: bounds size (1.73, 4.73, 9.08), min y -0,194
Collider: capsule r=0,55 h=6,54 (mesh (1.73, 4.73, 9.08))
Facing: pelvis -> head (0.00, 0.24, 0.97) (expected +Z)
Clips not referenced by the controller: Turn_Left, Turn_Right
Alert              2,00s loop=Y events=0 loopGap=0,0cm
Attack             1,40s loop=N events=1
Charge             0,70s loop=Y events=2 loopGap=0,0cm footSlide=0,11m/s (2% of 6,30) contactVel L(0.00, 0.00, -6.28) (8f) R(0.00, 0.00, -6.31) (7f)
Death              2,50s loop=N events=1
Drink              4,00s loop=Y events=0 loopGap=0,0cm
Eat                4,00s loop=Y events=0 loopGap=0,0cm
Heavy_Attack       2,00s loop=N events=1
Hurt               1,00s loop=N events=0
Idle               4,00s loop=Y events=0 loopGap=0,0cm
Idle_Variation     5,00s loop=Y events=0 loopGap=0,0cm
Look               3,00s loop=N events=0
Roar               3,00s loop=N events=0
Run                0,70s loop=Y events=2 loopGap=0,0cm footSlide=0,16m/s (2% of 7,00) contactVel L(0.00, 0.00, -6.98) (8f) R(0.00, 0.00, -7.01) (7f)
Turn_Left          1,13s loop=Y events=0 loopGap=0,0cm
Turn_Right         1,13s loop=Y events=0 loopGap=0,0cm
Walk               1,13s loop=Y events=2 loopGap=0,0cm footSlide=0,10m/s (6% of 1,70) contactVel L(0.01, 0.00, -1.73) (22f) R(-0.01, 0.00, -1.73) (22f)
Sampler check: max bone travel across clips 3,40 m
Ground contact (IDLE frame 0, skinned vertices): min y -0,002 m
Skin weights: 0 of 15306 LOD0 vertices without bones
Skin check (Walk): 100,0% of LOD0 vertices move with the bones
Screenshots: Documentation/CharacterTests/Parasaurolophus_clips.png (16 clips, 3/4 front view, character faces +Z)
RESULT: PASS
```
