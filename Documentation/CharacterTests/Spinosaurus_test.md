# Spinosaurus automated test

Result: **PASS**

```
=== Spinosaurus === 2026-09-27 15:24:26
Avatar: valid=True human=False
Importer: 15 clips configured (9 loops, 7 events)
Materials: M_Dino_Spinosaurus
Clips in FBX: 15 (meta: 15)
Controller: Assets/Art/Characters/Dinosaurs/Spinosaurus/Animations/SpinosaurusAnimator.controller (walk 1,90 m/s, run 7,50 m/s, 11 states)
LOD: 3 levels (LOD0:26816 tris, LOD1:12066 tris, LOD2:4290 tris)
Prefab: Assets/Art/Characters/Dinosaurs/Spinosaurus/Prefab/DINO_Spinosaurus.prefab
Scale: bounds size (2.28, 7.14, 12.75), min y -0,247
Collider: capsule r=0,73 h=9,18 (mesh (2.28, 7.14, 12.75))
Facing: pelvis -> head (0.00, 0.28, 0.96) (expected +Z)
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
Run                0,70s loop=Y events=2 loopGap=0,0cm footSlide=0,16m/s (2% of 7,50) contactVel L(-0.01, 0.00, -7.50) (7f) R(-0.02, 0.00, -7.51) (7f)
Turn_Left          1,27s loop=Y events=0 loopGap=0,0cm
Turn_Right         1,27s loop=Y events=0 loopGap=0,0cm
Walk               1,27s loop=Y events=2 loopGap=0,0cm footSlide=0,11m/s (6% of 1,90) contactVel L(0.01, 0.00, -1.88) (24f) R(-0.01, 0.00, -1.88) (24f)
Sampler check: max bone travel across clips 3,97 m
Ground contact (IDLE frame 0, skinned vertices): min y -0,039 m
Skin weights: 0 of 17600 LOD0 vertices without bones
Skin check (Walk): 100,0% of LOD0 vertices move with the bones
Screenshots: Documentation/CharacterTests/Spinosaurus_clips.png (15 clips, 3/4 front view, character faces +Z)
RESULT: PASS
```
