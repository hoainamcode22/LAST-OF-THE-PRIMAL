# Ankylosaurus automated test

Result: **PASS**

```
=== Ankylosaurus === 2026-09-30 14:22:53
Avatar: valid=True human=False
Importer: 25 clips configured (14 loops, 31 events)
Materials: M_Dino_Ankylosaurus
Clips in FBX: 25 (meta: 25)
Controller: Assets/Art/Characters/Dinosaurs/Ankylosaurus/Animations/AnkylosaurusAnimator.controller (walk 1,20 m/s, run 3,20 m/s, 22 states, 176 standing-start transitions, run slot Run / Flee on Intensity, rest Rest_Down / Rest_Loop / Rest_Shift / Rest_Up)
LOD: 3 levels (LOD0:26528 tris, LOD1:11936 tris, LOD2:4244 tris)
Prefab: Assets/Art/Characters/Dinosaurs/Ankylosaurus/Prefab/DINO_Ankylosaurus.prefab
Scale: bounds size (3.30, 3.06, 7.06), min y -0,351
Collider: capsule r=0,98 h=5,08 (mesh (3.30, 3.06, 7.06))
Facing: pelvis -> head (0.00, -0.01, 1.00) (expected +Z)
Alert              2,00s loop=Y events=0 loopGap=0,0cm
Attack             1,40s loop=N events=1
Breathe            2,80s loop=Y events=2 loopGap=0,0cm
Call               3,50s loop=N events=1
Charge             0,70s loop=Y events=2 loopGap=0,0cm footSlide=0,04m/s (1% of 2,88) contactVel L(0.00, 0.00, -2.89) (10f) R(0.00, 0.00, -2.88) (10f)
Death              2,50s loop=N events=1
Defend             4,00s loop=Y events=1 loopGap=0,0cm
Drink              4,00s loop=Y events=4 loopGap=0,0cm
Eat                5,00s loop=Y events=3 loopGap=0,0cm
Flee               0,67s loop=Y events=2 loopGap=0,0cm footSlide=0,03m/s (1% of 3,20) contactVel L(0.01, 0.00, -3.20) (10f) R(0.00, 0.00, -3.20) (10f)
Heavy_Attack       2,00s loop=N events=1
Hurt               1,00s loop=N events=0
Idle               4,00s loop=Y events=0 loopGap=0,0cm
Idle_Variation     5,00s loop=Y events=0 loopGap=0,0cm
Look               3,00s loop=N events=0
Rest_Down          3,37s loop=N events=1
Rest_Loop          6,70s loop=Y events=0 loopGap=0,0cm
Rest_Shift         3,47s loop=N events=0
Rest_Up            2,80s loop=N events=2
Roar               3,00s loop=N events=0
Run                0,70s loop=Y events=2 loopGap=0,0cm footSlide=0,08m/s (2% of 3,20) contactVel L(0.00, 0.00, -3.20) (10f) R(0.00, 0.00, -3.21) (10f)
Stop               1,50s loop=N events=2
Turn_Left          1,20s loop=Y events=2 loopGap=0,0cm
Turn_Right         1,20s loop=Y events=2 loopGap=0,0cm
Walk               1,00s loop=Y events=2 loopGap=0,0cm footSlide=0,04m/s (3% of 1,20) contactVel L(0.00, 0.00, -1.20) (21f) R(0.00, 0.00, -1.21) (21f)
Sampler check: max bone travel across clips 2,31 m
Ground contact (IDLE frame 0, skinned vertices): min y 0,000 m
Skin weights: 0 of 18137 LOD0 vertices without bones
Skin check (Walk): 100,0% of LOD0 vertices move with the bones
Screenshots: Documentation/CharacterTests/Ankylosaurus_clips.png (25 clips, 3/4 front view, character faces +Z)
RESULT: PASS
```
