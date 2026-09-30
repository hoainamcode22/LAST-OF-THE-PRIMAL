# Spinosaurus automated test

Result: **PASS**

```
=== Spinosaurus === 2026-09-30 14:23:30
Avatar: valid=True human=False
Importer: 25 clips configured (12 loops, 30 events)
Materials: M_Dino_Spinosaurus
Clips in FBX: 25 (meta: 25)
Controller: Assets/Art/Characters/Dinosaurs/Spinosaurus/Animations/SpinosaurusAnimator.controller (walk 1,90 m/s, run 7,50 m/s, 22 states, 160 standing-start transitions, run slot Run / Chase on Intensity, rest Rest_Down / Rest_Loop / Rest_Shift / Rest_Up)
LOD: 3 levels (LOD0:26816 tris, LOD1:12066 tris, LOD2:4290 tris)
Prefab: Assets/Art/Characters/Dinosaurs/Spinosaurus/Prefab/DINO_Spinosaurus.prefab
Scale: bounds size (2.28, 7.14, 12.75), min y -0,247
Collider: capsule r=0,73 h=9,18 (mesh (2.28, 7.14, 12.75))
Facing: pelvis -> head (0.00, 0.28, 0.96) (expected +Z)
Alert              2,00s loop=Y events=0 loopGap=0,0cm
Attack             1,40s loop=N events=1
Bite               0,77s loop=N events=1
Breathe            2,63s loop=Y events=2 loopGap=0,0cm
Call               3,50s loop=N events=1
Chase              0,67s loop=Y events=2 loopGap=0,0cm footSlide=0,18m/s (2% of 7,50) contactVel L(0.00, 0.00, -7.50) (7f) R(-0.01, 0.00, -7.51) (7f)
Death              2,50s loop=N events=1
Drink              4,00s loop=Y events=4 loopGap=0,0cm
Eat                5,00s loop=Y events=3 loopGap=0,0cm
Heavy_Attack       2,00s loop=N events=1
Hurt               1,00s loop=N events=0
Idle               4,00s loop=Y events=0 loopGap=0,0cm
Idle_Variation     5,00s loop=Y events=0 loopGap=0,0cm
Look               3,00s loop=N events=0
Recover            1,47s loop=N events=2
Rest_Down          3,20s loop=N events=1
Rest_Loop          6,47s loop=Y events=0 loopGap=0,0cm
Rest_Shift         3,33s loop=N events=0
Rest_Up            2,67s loop=N events=2
Roar               3,00s loop=N events=0
Run                0,70s loop=Y events=2 loopGap=0,0cm footSlide=0,16m/s (2% of 7,50) contactVel L(-0.01, 0.00, -7.50) (7f) R(-0.02, 0.00, -7.51) (7f)
Stop               1,40s loop=N events=1
Turn_Left          1,47s loop=Y events=2 loopGap=0,0cm
Turn_Right         1,47s loop=Y events=2 loopGap=0,0cm
Walk               1,27s loop=Y events=2 loopGap=0,0cm footSlide=0,11m/s (6% of 1,90) contactVel L(0.01, 0.00, -1.90) (24f) R(-0.01, 0.00, -1.90) (24f)
Sampler check: max bone travel across clips 3,97 m
Ground contact (IDLE frame 0, skinned vertices): min y -0,039 m
Skin weights: 0 of 17600 LOD0 vertices without bones
Skin check (Walk): 100,0% of LOD0 vertices move with the bones
Screenshots: Documentation/CharacterTests/Spinosaurus_clips.png (25 clips, 3/4 front view, character faces +Z)
RESULT: PASS
```
