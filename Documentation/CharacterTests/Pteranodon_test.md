# Pteranodon automated test

Result: **PASS**

```
=== Pteranodon === 2026-09-27 15:41:03
Avatar: valid=True human=False
Importer: 11 clips configured (5 loops, 2 events)
Materials: M_Dino_Pteranodon
Clips in FBX: 11 (meta: 11)
Controller: Assets/Art/Characters/Dinosaurs/Pteranodon/Animations/PteranodonAnimator.controller (walk 0,80 m/s, run 6,00 m/s, 10 states)
LOD: 3 levels (LOD0:14353 tris, LOD1:6456 tris, LOD2:2294 tris)
Prefab: Assets/Art/Characters/Dinosaurs/Pteranodon/Prefab/DINO_Pteranodon.prefab
Scale: bounds size (2.42, 3.05, 2.13), min y -0,015
Collider: capsule r=0,78 h=1,55 (mesh (2.42, 3.05, 2.13))
Facing: pelvis -> head (0.00, 0.31, 0.95) (expected +Z)
Attack             1,00s loop=N events=1
Death              2,00s loop=N events=1
Fly                0,90s loop=Y events=0 loopGap=0,0cm
Glide              3,00s loop=Y events=0 loopGap=0,0cm
Hurt               0,80s loop=N events=0
Idle               3,00s loop=Y events=0 loopGap=0,0cm
Idle_Variation     4,00s loop=Y events=0 loopGap=0,0cm
Land               1,33s loop=N events=0
Roar               2,00s loop=N events=0
Takeoff            1,33s loop=N events=0
Walk               0,80s loop=Y events=0 loopGap=0,0cm footSlide=0,06m/s (8% of 0,80) contactVel L(0.00, 0.00, -0.78) (12f) R(0.00, 0.00, -0.78) (12f)
Sampler check: max bone travel across clips 3,84 m
Ground contact (IDLE frame 0, skinned vertices): min y -0,007 m
Skin weights: 0 of 10408 LOD0 vertices without bones
Skin check (Walk): 100,0% of LOD0 vertices move with the bones
Screenshots: Documentation/CharacterTests/Pteranodon_clips.png (11 clips, 3/4 front view, character faces +Z)
RESULT: PASS
```
