# Player automated test

Result: **PASS**

```
=== Player === 2026-09-27 21:27:55
Avatar: valid=True human=True
Importer: 47 clips configured (24 loops, 55 events)
Materials: M_Player_Cloth, M_Player_Eye, M_Player_Hair, M_Player_Skin
Clips in FBX: 47 (meta: 47)
Controller: Assets/Art/Characters/Player/Animations/PlayerAnimator.controller (states: 36 base + 5 upper body, params 9, IK pass on)
LOD: 3 levels (LOD0:72952 tris, LOD1:17107 tris, LOD2:5668 tris)
Prefab: Assets/Art/Characters/Player/Prefab/PFB_Player_Survivor.prefab
Scale: bounds size (0.96, 1.89, 1.02), min y -0,073
Collider: CharacterController h=1,8 r=0,3 (mesh height 1,89)
Clips not referenced by the controller: Walk_Backward, Walk_Left, Walk_Right
Attack_Spear       1,00s loop=N events=1
Attack_Spear_Heavy 1,53s loop=N events=2
Bow_Aim            2,00s loop=Y events=0 loopGap=0,0cm
Bow_Draw           0,87s loop=N events=1
Bow_Release        0,93s loop=N events=1
Build              1,33s loop=Y events=1 loopGap=0,0cm
Carry_Item         2,00s loop=Y events=0 loopGap=0,0cm
Climb_Down         1,20s loop=Y events=2 loopGap=0,0cm (vertical -0,50m/s, foot-slide check skipped)
Climb_End          0,93s loop=N events=1 (vertical 0,00m/s, foot-slide check skipped)
Climb_Idle         2,00s loop=Y events=0 loopGap=0,0cm (vertical 0,00m/s, foot-slide check skipped)
Climb_Start        0,80s loop=N events=1 (vertical 0,00m/s, foot-slide check skipped)
Climb_Up           1,20s loop=Y events=2 loopGap=0,0cm (vertical 0,60m/s, foot-slide check skipped)
Craft              2,00s loop=Y events=2 loopGap=0,0cm
Crouch             2,00s loop=Y events=0 loopGap=0,0cm
Crouch_Walk        1,33s loop=Y events=2 loopGap=0,0cm footSlide=0,01m/s (1% of 0,95) contactVel L(0.00, 0.00, -0.95) (27f) R(0.00, 0.00, -0.95) (27f)
Death              2,00s loop=N events=1
Dodge              0,73s loop=N events=2
Drink              2,33s loop=N events=1
Eat                1,60s loop=N events=2
Fall               1,00s loop=Y events=0 loopGap=0,0cm
Gather_Plant       1,60s loop=Y events=1 loopGap=0,0cm
Gather_Stone       1,13s loop=Y events=1 loopGap=0,0cm
Gather_Wood        1,20s loop=Y events=1 loopGap=0,0cm
Get_Up             2,00s loop=N events=0
Harvest_Fruit      1,47s loop=N events=1
Hurt               0,80s loop=N events=1
Hurt_Heavy         1,33s loop=N events=1
Idle               6,00s loop=Y events=0 loopGap=0,0cm
Idle_Variation     5,00s loop=Y events=0 loopGap=0,0cm
Interact           1,00s loop=N events=1
Jump               0,80s loop=N events=1
Knife_Attack       0,87s loop=N events=1
Land               0,67s loop=N events=1
Pickup             1,33s loop=N events=1
Run                0,67s loop=Y events=2 loopGap=0,0cm footSlide=0,02m/s (1% of 3,80) contactVel L(0.00, 0.00, -3.79) (7f) R(0.00, 0.00, -3.79) (7f)
Sleep              4,00s loop=Y events=0 loopGap=0,0cm
Spear_Attack_2     1,13s loop=N events=1
Sprint             0,47s loop=Y events=2 loopGap=0,0cm footSlide=0,01m/s (0% of 6,20) contactVel L(0.00, 0.00, -6.20) (3f) R(0.00, 0.00, -6.20) (3f)
Throw_Spear        1,53s loop=N events=1
Turn_Left          1,00s loop=Y events=2 loopGap=0,0cm
Turn_Right         1,00s loop=Y events=2 loopGap=0,0cm
Use_Item           1,33s loop=N events=1
Wake_Up            6,00s loop=N events=3
Walk               1,00s loop=Y events=2 loopGap=0,0cm footSlide=0,03m/s (2% of 1,35) contactVel L(0.00, 0.00, -1.35) (16f) R(0.00, 0.00, -1.35) (16f)
Walk_Backward      1,00s loop=Y events=2 loopGap=0,0cm footSlide=0,04m/s (4% of 1,05) contactVel L(0.00, 0.00, 1.04) (12f) R(0.00, 0.00, 1.05) (12f)
Walk_Left          0,80s loop=Y events=2 loopGap=0,0cm footSlide=0,02m/s (2% of 1,10) contactVel L(1.10, 0.00, 0.00) (15f) R(1.10, 0.00, 0.00) (15f)
Walk_Right         0,80s loop=Y events=2 loopGap=0,0cm footSlide=0,02m/s (2% of 1,10) contactVel L(-1.10, 0.00, 0.00) (15f) R(-1.10, 0.00, 0.00) (15f)
Sampler check: max bone travel across clips 1,99 m
Ground contact (IDLE frame 0, skinned vertices): min y 0,000 m
Skin weights: 0 of 62418 LOD0 vertices without bones
Skin check (Walk): 100,0% of LOD0 vertices move with the bones
Facing: shoulders -> (0.00, 0.00, 1.00), left foot -> toes (-0.04, 0.00, 1.00) (expected +Z)
Screenshots: Documentation/CharacterTests/Player_clips.png (47 clips, 3/4 front view, character faces +Z)
RESULT: PASS
```
