# Carnotaurus automated test

Result: **PASS**

```
=== Carnotaurus === 2026-09-27 15:24:10
Avatar: valid=True human=False
Importer: 15 clips configured (9 loops, 7 events)
Materials: M_Dino_Carnotaurus
Clips in FBX: 15 (meta: 15)
Controller: Assets/Art/Characters/Dinosaurs/Carnotaurus/Animations/CarnotaurusAnimator.controller (walk 1,80 m/s, run 9,50 m/s, 11 states)
LOD: 3 levels (LOD0:24656 tris, LOD1:11094 tris, LOD2:3944 tris)
Prefab: Assets/Art/Characters/Dinosaurs/Carnotaurus/Prefab/DINO_Carnotaurus.prefab
Scale: bounds size (1.91, 4.16, 7.80), min y -0,215
Collider: capsule r=0,61 h=5,62 (mesh (1.91, 4.16, 7.80))
Facing: pelvis -> head (0.00, 0.35, 0.94) (expected +Z)
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
Run                0,53s loop=Y events=2 loopGap=0,0cm footSlide=0,32m/s (3% of 9,50) contactVel L(-0.02, 0.00, -9.80) (5f) R(-0.01, 0.00, -9.81) (5f)
Turn_Left          1,00s loop=Y events=0 loopGap=0,0cm
Turn_Right         1,00s loop=Y events=0 loopGap=0,0cm
Walk               1,00s loop=Y events=2 loopGap=0,0cm footSlide=0,10m/s (6% of 1,80) contactVel L(0.01, 0.00, -1.80) (18f) R(-0.01, 0.00, -1.80) (18f)
Sampler check: max bone travel across clips 2,87 m
Ground contact (IDLE frame 0, skinned vertices): min y -0,021 m
Skin weights: 0 of 15442 LOD0 vertices without bones
Skin check (Walk): 100,0% of LOD0 vertices move with the bones
Screenshots: Documentation/CharacterTests/Carnotaurus_clips.png (15 clips, 3/4 front view, character faces +Z)
RESULT: PASS
```
