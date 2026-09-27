# Player automated test

Result: **FAIL**

```
=== Player === 2026-09-27 02:35:09
Avatar: valid=True human=True
Importer: 35 clips configured (20 loops, 38 events)
Materials: M_Player_Cloth, M_Player_Eye, M_Player_Hair, M_Player_Skin
Clips in FBX: 35 (meta: 35)
Controller: Assets/Art/Characters/Player/Animations/AC_Player.controller (states: 23 base + 5 upper body)
LOD: 2 levels (LOD0:23514 tris, LOD1:9875 tris)
Prefab: Assets/Art/Characters/Player/Prefab/PFB_Player_Survivor.prefab
Scale: bounds size (0.87, 1.85, 0.99), min y -0,096
Collider: CharacterController h=1,8 r=0,3 (mesh height 1,85)
Clips not referenced by the controller: IDLE_VARIATION
ATTACK_SPEAR       1,25s loop=N events=1
ATTACK_SPEAR_ALT   1,42s loop=N events=1
BOW_DRAW           1,08s loop=N events=1
BOW_IDLE           2,50s loop=Y events=0 loopGap=0,0cm
BOW_RELEASE        1,17s loop=N events=1
BUILD              1,67s loop=Y events=1 loopGap=0,0cm
CARRY_ITEM         2,50s loop=Y events=0 loopGap=0,0cm
CRAFT              2,50s loop=Y events=2 loopGap=0,0cm
CROUCH             2,50s loop=Y events=0 loopGap=0,0cm
FAIL: CROUCH_WALK: foot sliding 1,49 m/s
CROUCH_WALK        1,67s loop=Y events=2 loopGap=0,0cm footSlide=1,49m/s (157% of 0,95) contactVel L(-0.02, 0.00, 0.54) (3f) R(0.09, 0.00, 0.47) (3f)
DEATH              2,50s loop=N events=1
DRINK              2,92s loop=N events=1
EAT                2,00s loop=N events=2
FALL               1,25s loop=Y events=0 loopGap=0,0cm
GATHER_STONE       1,42s loop=Y events=1 loopGap=0,0cm
GATHER_WOOD        1,50s loop=Y events=1 loopGap=0,0cm
HURT               1,00s loop=N events=1
HURT_HEAVY         1,67s loop=N events=1
IDLE               3,75s loop=Y events=0 loopGap=0,0cm
IDLE_VARIATION     6,25s loop=Y events=0 loopGap=0,0cm
INTERACT           1,25s loop=N events=1
JUMP               1,00s loop=N events=1
LAND               0,83s loop=N events=1
PICKUP             1,67s loop=N events=1
REVIVE             2,50s loop=N events=0
FAIL: RUN_FORWARD: foot sliding 999,00 m/s
RUN_FORWARD        0,92s loop=Y events=2 loopGap=0,0cm footSlide=999,00m/s (26289% of 3,80) contactVel L(0.00, 0.00, 0.00) (0f) R(0.00, 0.00, 0.00) (0f)
SLEEP              5,00s loop=Y events=0 loopGap=0,0cm
FAIL: SPRINT: foot sliding 999,00 m/s
SPRINT             0,75s loop=Y events=2 loopGap=0,0cm footSlide=999,00m/s (16113% of 6,20) contactVel L(0.00, 0.00, 0.00) (0f) R(0.00, 0.00, 0.00) (0f)
THROW_SPEAR        1,92s loop=N events=1
TURN_LEFT          1,25s loop=Y events=2 loopGap=0,0cm
TURN_RIGHT         1,25s loop=Y events=2 loopGap=0,0cm
FAIL: WALK_BACKWARD: foot sliding 1,82 m/s
WALK_BACKWARD      1,50s loop=Y events=2 loopGap=0,0cm footSlide=1,82m/s (173% of 1,05) contactVel L(0.00, 0.00, -0.77) (30f) R(0.06, 0.00, -0.72) (4f)
FAIL: WALK_FORWARD: foot sliding 2,05 m/s
WALK_FORWARD       1,42s loop=Y events=2 loopGap=0,0cm footSlide=2,05m/s (152% of 1,35) contactVel L(0.01, 0.00, 0.36) (2f) R(0.00, 0.00, 0.70) (2f)
FAIL: WALK_LEFT: foot sliding 2,02 m/s
WALK_LEFT          1,42s loop=Y events=2 loopGap=0,0cm footSlide=2,02m/s (183% of 1,10) contactVel L(-0.79, 0.00, 0.02) (16f) R(-0.92, 0.00, 0.01) (5f)
FAIL: WALK_RIGHT: foot sliding 2,02 m/s
WALK_RIGHT         1,42s loop=Y events=2 loopGap=0,0cm footSlide=2,02m/s (184% of 1,10) contactVel L(0.81, 0.00, 0.01) (16f) R(0.92, 0.00, 0.01) (5f)
Sampler check: max bone travel across clips 1,86 m
Ground contact (IDLE frame 0, skinned vertices): min y 0,004 m
Facing: shoulders -> (0.00, 0.00, -1.00), left foot -> toes (0.05, 0.00, -1.00) (expected +Z)
FAIL: character does not face +Z
Screenshots: Documentation/CharacterTests/Player_clips.png (35 clips, 3/4 front view, character faces +Z)
RESULT: FAIL (8 failures)
```
