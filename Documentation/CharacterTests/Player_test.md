# Player automated test

Result: **PASS**

```
=== Player === 2026-09-30 16:30:09
T-pose: from the skin bind pose (57 bones), arms to horizontal by side (L = -X, R = +X)
T-pose L: upper arm (-1.00, 0.00, 0.00), forearm (-1.00, 0.00, 0.00)
T-pose R: upper arm (1.00, 0.00, 0.00), forearm (1.00, 0.00, 0.00)
T-pose mirror L vs R (deg): Clavicle 0.0 UpperArm 0.0 LowerArm 0.0 Hand 0.0 Thigh 0.0 Calf 0.0 Foot 0.0
Twist bones LowerArmTwist_L/R found: avatar lowerArmTwist = 0 (TwistBoneDriver spreads the wrist twist)
Avatar: valid=True human=True
Importer: 89 clips configured (38 loops, 143 events)
Materials: M_Player_Cloth, M_Player_Eye, M_Player_Hair, M_Player_Skin
Clips in FBX: 89 (meta: 89)
Locomotion: Idle time-scaled x5,63 to Walk's cycle, state speed = LocoRate (standing: LocoIdleRate 0,178)
waiting for clip (state skipped): Butcher
Gather_Plant: enter / exit clips wired (0.12 / 0.1 / 0.15 s blends)
waiting for clip (state skipped): Spear_Recovery
waiting for clip (state skipped): Bow_FullDraw
waiting for clip (state skipped): Bow_Equip
waiting for clip (state skipped): Bow_Nock
waiting for clip (state skipped): Bow_Idle
waiting for clip (state skipped): Spear_Idle
Controller: Assets/Art/Characters/Player/Animations/PlayerAnimator.controller (states: 62 base + 11 upper body + 3 hit reaction, params 25, IK pass on)
Controller: new states StrafeLocomotion (2D VelX/VelZ, on Strafe), Sword_Attack_1/2/3 + Sword_Heavy (Attack, speed x AttackSpeed); upper body Sword_Idle, Sword_Block, Sword_Equip, Sword_Unequip; HitReaction (additive) Hurt_Additive on HurtLight
TwistBoneDriver on the model (LowerArmTwist bones found)
LOD: 3 levels (LOD0:72952 tris, LOD1:17107 tris, LOD2:5668 tris)
Prefab: Assets/Art/Characters/Player/Prefab/PFB_Player_Survivor.prefab
Scale: bounds size (0.78, 1.89, 0.99), min y -0,073
Collider: CharacterController h=1,8 r=0,3 (mesh height 1,89)
Clips not referenced by the controller: BareHand_Block, BareHand_Heavy_Straight, Climb_Rock_Down, Climb_Rock_Idle, Climb_Rock_Up, Drink_Container, Ledge_Mantle
Attack_Spear       1,00s loop=N events=4
Attack_Spear_Heavy 1,53s loop=N events=5
Bandage_Use        1,93s loop=N events=1
BareHand_Block     1,33s loop=Y events=0 loopGap=0,0cm
BareHand_Combo_End 0,80s loop=N events=0
BareHand_Heavy     1,00s loop=N events=4
BareHand_Heavy_Straight 0,93s loop=N events=4
BareHand_HitReaction 0,60s loop=N events=1
BareHand_Idle      2,00s loop=Y events=0 loopGap=0,0cm
BareHand_Punch_1   0,47s loop=N events=4
BareHand_Punch_2   0,53s loop=N events=4
BareHand_Punch_3   0,67s loop=N events=4
Bow_Aim            2,00s loop=Y events=0 loopGap=0,0cm
Bow_Draw           0,87s loop=N events=1
Bow_Release        0,93s loop=N events=1
Build              1,33s loop=Y events=1 loopGap=0,0cm
Carry_Item         2,00s loop=Y events=0 loopGap=0,0cm
Climb_Down         1,20s loop=Y events=2 loopGap=0,0cm (vertical -0,50m/s, foot-slide check skipped)
Climb_End          0,93s loop=N events=1 (vertical 0,00m/s, foot-slide check skipped)
Climb_Idle         2,00s loop=Y events=0 loopGap=0,0cm (vertical 0,00m/s, foot-slide check skipped)
Climb_Rock_Down    1,20s loop=Y events=2 loopGap=0,0cm (vertical -0,55m/s, foot-slide check skipped)
Climb_Rock_Idle    2,00s loop=Y events=0 loopGap=0,0cm (vertical 0,00m/s, foot-slide check skipped)
Climb_Rock_Up      1,20s loop=Y events=2 loopGap=0,0cm (vertical 0,60m/s, foot-slide check skipped)
Climb_Start        0,80s loop=N events=1 (vertical 0,00m/s, foot-slide check skipped)
Climb_Up           1,20s loop=Y events=2 loopGap=0,0cm (vertical 0,60m/s, foot-slide check skipped)
Collect_Water      2,13s loop=N events=2
Craft              2,00s loop=Y events=2 loopGap=0,0cm
Crouch             2,00s loop=Y events=0 loopGap=0,0cm
Crouch_Walk        1,33s loop=Y events=2 loopGap=0,0cm footSlide=0,01m/s (1% of 0,95) contactVel L(0.00, 0.00, -0.95) (27f) R(0.00, 0.00, -0.95) (27f)
Death              2,00s loop=N events=1
Dodge              0,73s loop=N events=2
Drink              2,33s loop=N events=1
Drink_Container    1,47s loop=N events=1
Drink_Kneel        2,40s loop=N events=2
Eat                1,60s loop=N events=2
Fall               1,00s loop=Y events=0 loopGap=0,0cm
Gather_Branch      1,33s loop=Y events=1 loopGap=0,0cm
Gather_Enter       0,40s loop=N events=0
Gather_Exit        0,47s loop=N events=0
Gather_Plant       1,60s loop=Y events=1 loopGap=0,0cm
Gather_Stone       1,13s loop=Y events=1 loopGap=0,0cm
Gather_Stone_Hand  1,33s loop=Y events=1 loopGap=0,0cm
Gather_Wood        1,20s loop=Y events=1 loopGap=0,0cm
Get_Up             2,00s loop=N events=0
Harvest_Fruit      1,47s loop=N events=1
Hurt               0,80s loop=N events=1
Hurt_Heavy         1,33s loop=N events=1
Idle               6,00s loop=Y events=0 loopGap=0,0cm
Idle_Variation     5,00s loop=Y events=0 loopGap=0,0cm
Interact           1,00s loop=N events=1
Jump               0,80s loop=N events=1
Kick               0,87s loop=N events=5
Knife_Attack       0,87s loop=N events=1
Land               0,67s loop=N events=1
Ledge_Mantle       1,80s loop=N events=3
Pickup             1,33s loop=N events=1
Run                0,73s loop=Y events=2 loopGap=0,0cm footSlide=0,01m/s (0% of 3,80) contactVel L(0.00, 0.00, -3.80) (6f) R(0.00, 0.00, -3.79) (7f)
Run_Backward       0,80s loop=Y events=2 loopGap=0,0cm footSlide=0,14m/s (6% of 2,40) contactVel L(0.00, 0.00, 2.27) (7f) R(0.00, 0.00, 2.34) (7f)
Run_Pivot_180      0,67s loop=N events=2
Run_Start          0,47s loop=N events=1
Run_Stop           0,87s loop=N events=2
Sleep              4,00s loop=Y events=0 loopGap=0,0cm
Spear_Attack_2     1,13s loop=N events=4
Sprint             0,60s loop=Y events=2 loopGap=0,0cm footSlide=0,66m/s (11% of 6,20) contactVel L(0.00, 0.00, -5.57) (6f) R(0.00, 0.00, -5.76) (5f)
Strafe_Run_L       0,60s loop=Y events=2 loopGap=0,0cm footSlide=0,01m/s (0% of 3,00) contactVel L(3.00, 0.00, 0.00) (8f) R(3.00, 0.00, -0.01) (8f)
Strafe_Run_R       0,60s loop=Y events=2 loopGap=0,0cm footSlide=0,02m/s (1% of 3,00) contactVel L(-3.00, 0.00, 0.00) (8f) R(-3.00, 0.00, 0.01) (8f)
Sword_Attack_1     1,00s loop=N events=4
Sword_Attack_2     0,93s loop=N events=4
Sword_Attack_3     1,13s loop=N events=5
Sword_Block        1,33s loop=Y events=0 loopGap=0,0cm
Sword_Equip        0,73s loop=N events=1
Sword_Heavy        1,53s loop=N events=5
Sword_Idle         2,00s loop=Y events=0 loopGap=0,0cm
Sword_Unequip      0,73s loop=N events=1
Throw_Spear        1,53s loop=N events=1
Turn_180           1,13s loop=N events=3
Turn_Left          1,00s loop=Y events=2 loopGap=0,0cm
Turn_Right         1,00s loop=Y events=2 loopGap=0,0cm
Unarmed_Block      1,33s loop=Y events=0 loopGap=0,0cm
Unconscious        5,00s loop=Y events=0 loopGap=0,0cm
Unconscious_Collapse 1,87s loop=N events=2
Use_Item           1,33s loop=N events=1
Wake_Up            6,00s loop=N events=3
Walk               1,07s loop=Y events=2 loopGap=0,0cm footSlide=0,04m/s (3% of 1,35) contactVel L(0.00, 0.00, -1.34) (19f) R(0.00, 0.00, -1.33) (19f)
Walk_Backward      1,00s loop=Y events=2 loopGap=0,0cm footSlide=0,01m/s (1% of 1,05) contactVel L(0.00, 0.00, 1.05) (12f) R(0.00, 0.00, 1.05) (12f)
Walk_Left          0,80s loop=Y events=2 loopGap=0,0cm footSlide=0,02m/s (2% of 1,10) contactVel L(1.10, 0.00, 0.00) (15f) R(1.10, 0.00, 0.00) (15f)
Walk_Right         0,80s loop=Y events=2 loopGap=0,0cm footSlide=0,02m/s (2% of 1,10) contactVel L(-1.10, 0.00, 0.00) (15f) R(-1.10, 0.00, 0.00) (15f)
Walk_Start         0,60s loop=N events=1
Walk_Stop          0,73s loop=N events=1
Sampler check: max bone travel across clips 1,99 m
Ground contact (IDLE frame 0, skinned vertices): min y 0,000 m
Skin weights: 0 of 62418 LOD0 vertices without bones
Skin check (Walk): 100,0% of LOD0 vertices move with the bones
Facing: shoulders -> (0.02, 0.00, 1.00), left foot -> toes (-0.11, 0.00, 0.99) (expected +Z)
Screenshots: Documentation/CharacterTests/Player_clips.png (89 clips, 3/4 front view, character faces +Z)
RESULT: PASS
```
