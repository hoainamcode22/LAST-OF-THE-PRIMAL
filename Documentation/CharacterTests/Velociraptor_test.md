# Velociraptor automated test

Result: **PASS**

```
=== Velociraptor === 2026-09-27 18:04:25
Avatar: valid=True human=False
Importer: 15 clips configured (9 loops, 7 events)
Materials: M_Dino_Velociraptor
Clips in FBX: 15 (meta: 15)
Controller: Assets/Art/Characters/Dinosaurs/Velociraptor/Animations/VelociraptorAnimator.controller (walk 1,50 m/s, run 11,00 m/s, 11 states)
LOD: 3 levels (LOD0:16600 tris, LOD1:7470 tris, LOD2:2656 tris)
Prefab: Assets/Art/Characters/Dinosaurs/Velociraptor/Prefab/DINO_Velociraptor.prefab
Scale: bounds size (0.34, 1.10, 2.10), min y -0,035
Collider: capsule r=0,15 h=1,51 (mesh (0.34, 1.10, 2.10))
Facing: pelvis -> head (0.00, 0.56, 0.83) (expected +Z)
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
Run                0,37s loop=Y events=2 loopGap=0,0cm footSlide=0,31m/s (3% of 11,00) contactVel L(-0.05, 0.00, -11.00) (1f) R(0.01, 0.00, -10.70) (1f)
Turn_Left          0,60s loop=Y events=0 loopGap=0,0cm
Turn_Right         0,60s loop=Y events=0 loopGap=0,0cm
Walk               0,60s loop=Y events=2 loopGap=0,0cm footSlide=0,06m/s (4% of 1,50) contactVel L(0.00, 0.00, -1.47) (11f) R(-0.01, 0.00, -1.47) (11f)
Sampler check: max bone travel across clips 0,90 m
Ground contact (IDLE frame 0, skinned vertices): min y -0,009 m
Skin weights: 0 of 11170 LOD0 vertices without bones
Skin check (Walk): 100,0% of LOD0 vertices move with the bones
Screenshots: Documentation/CharacterTests/Velociraptor_clips.png (15 clips, 3/4 front view, character faces +Z)
RESULT: PASS
```
