# Velociraptor automated test

Result: **PASS**

```
=== Velociraptor === 2026-09-30 14:23:07
Avatar: valid=True human=False
Importer: 25 clips configured (12 loops, 32 events)
Materials: M_Dino_Velociraptor
Clips in FBX: 25 (meta: 25)
Controller: Assets/Art/Characters/Dinosaurs/Velociraptor/Animations/VelociraptorAnimator.controller (walk 1,50 m/s, run 11,00 m/s, 22 states, 160 standing-start transitions, run slot Run / Chase on Intensity, rest Rest_Down / Rest_Loop / Rest_Shift / Rest_Up)
LOD: 3 levels (LOD0:16600 tris, LOD1:7470 tris, LOD2:2656 tris)
Prefab: Assets/Art/Characters/Dinosaurs/Velociraptor/Prefab/DINO_Velociraptor.prefab
Scale: bounds size (0.34, 1.10, 2.10), min y -0,035
Collider: capsule r=0,15 h=1,51 (mesh (0.34, 1.10, 2.10))
Facing: pelvis -> head (0.00, 0.56, 0.83) (expected +Z)
Alert              2,00s loop=Y events=0 loopGap=0,0cm
Attack             1,40s loop=N events=1
Bite               0,47s loop=N events=1
Breathe            1,17s loop=Y events=2 loopGap=0,0cm
Call               1,50s loop=N events=3
Chase              0,33s loop=Y events=2 loopGap=0,0cm footSlide=0,03m/s (0% of 11,00) contactVel L(-0.02, 0.00, -11.00) (1f) R(0.03, 0.00, -11.01) (1f)
Death              2,50s loop=N events=1
Drink              4,00s loop=Y events=4 loopGap=0,0cm
Eat                5,00s loop=Y events=3 loopGap=0,0cm
Heavy_Attack       2,00s loop=N events=1
Hurt               1,00s loop=N events=0
Idle               4,00s loop=Y events=0 loopGap=0,0cm
Idle_Variation     5,00s loop=Y events=0 loopGap=0,0cm
Look               3,00s loop=N events=0
Recover            0,87s loop=N events=2
Rest_Down          1,57s loop=N events=1
Rest_Loop          4,23s loop=Y events=0 loopGap=0,0cm
Rest_Shift         2,30s loop=N events=0
Rest_Up            1,27s loop=N events=2
Roar               3,00s loop=N events=0
Run                0,37s loop=Y events=2 loopGap=0,0cm footSlide=0,31m/s (3% of 11,00) contactVel L(-0.05, 0.00, -11.00) (1f) R(0.01, 0.00, -10.70) (1f)
Stop               0,63s loop=N events=1
Turn_Left          0,50s loop=Y events=2 loopGap=0,0cm
Turn_Right         0,50s loop=Y events=2 loopGap=0,0cm
Walk               0,60s loop=Y events=2 loopGap=0,0cm footSlide=0,06m/s (4% of 1,50) contactVel L(0.00, 0.00, -1.47) (11f) R(-0.01, 0.00, -1.47) (11f)
Sampler check: max bone travel across clips 0,90 m
Ground contact (IDLE frame 0, skinned vertices): min y -0,009 m
Skin weights: 0 of 11170 LOD0 vertices without bones
Skin check (Walk): 100,0% of LOD0 vertices move with the bones
Screenshots: Documentation/CharacterTests/Velociraptor_clips.png (25 clips, 3/4 front view, character faces +Z)
RESULT: PASS
```
