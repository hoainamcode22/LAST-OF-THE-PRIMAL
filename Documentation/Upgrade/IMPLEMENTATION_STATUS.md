# IMPLEMENTATION STATUS (upgrade: character, animation, combat, world interaction, crafting)

Updated: 2026-09-28. Legend:
- **DONE**: built and checked with the evidence listed.
- **DONE (not play-tested)**: builds, compiles, editor checks pass, but nobody has played it yet.
- **PARTIAL**: some of it is built. The note says what is missing.
- **NOT DONE**: not started.

"Compile" means the cloud compile check (runtime, editor and tests) and the Unity editor compile, with no `error CS` in either.

## Order of work (brief)

| # | Step | Status | Evidence / notes |
|---|---|---|---|
| 0 | Audit + plans | DONE | PROJECT_AUDIT, CHARACTER_HIERARCHY, CHARACTER_RIG_PLAN, ANIMATION_PLAN, COMBAT_PLAN, CRAFTING_PLAN, WORLD_INTERACTION_PLAN |
| 1 | Rig: forearm twist bones | DONE | `LowerArmTwist_L/R` (child of LowerArm at 50 %, Copy Rotation Y 0.6 from the hand in Blender, `TwistBoneDriver` in Unity). Script `scripts/pf_rig_upgrade.py`. Backup `PLAYER_Survivor_v2_work_before_upgrade.blend`. The upper-arm twist bone was left out: the shoulder was fixed by weights. |
| 2 | Weights: forearm split, shoulder, pelt, elbow | DONE | Checked: max 4 influences, 0 vertices not normalized (LOD0/1/2). Before/after sheet: `Screenshots/Upgrade/deform_before_after.png`. The shoulder raise no longer collapses and the pelt does not tear. The wrist twist is spread along the forearm. |
| 3 | Relaxed hands | DONE | `pf_anim.HumanRig.fingers`: curl grows from the index to the little finger, with a middle-joint bias and a small spread when relaxed. Every clip was re-baked. `Screenshots/Upgrade/hands_relaxed.png` |
| 4 | Locomotion: measured speed, analog, fall, 2D strafe | DONE (not play-tested) | Changes: `PlayerMotor.MeasuredPlanarSpeed` / `LocalPlanarVelocity` / continuous analog; `PlayerAnimationDriver` Speed, VelX / VelZ, Strafe and the fall delay; a `StrafeLocomotion` 2D tree. New clips: Run_Backward, Strafe_Run_L/R. Character test foot slide is 1-4 % on every locomotion clip. |
| 5 | IK: off-hand grip, elbow hints, bow string | DONE (not play-tested) | Code is in `PlayerIK`. The spear off-hand grip point is 0.42 m along the shaft, which matches the clip, so IK only closes the last few cm (the `HeldWeapons` capture measures it). |
| 6 | Sockets / hierarchy | DONE (not play-tested) | `PlayerHierarchy` sockets. Grip convention confirmed in Unity: the tool's long axis is local Z, with the working end leaving the fist on the thumb side. `WeaponHitbox.MeasureBlade` and the off-hand grip were fixed to use that convention. |
| 7 | Sword | DONE (not play-tested) | Model `WEAPON_FlintSword` (original design, 1,092 tris, shared M_Tools atlas, icon). Data: `WPN_flint_sword`, `ITEM_flint_sword`, recipe. Clips: Sword_Idle, Attack_1/2/3, Heavy (two-handed), Block, Equip, Unequip, with OnAttackStart / Active / End events. Animator states are in place. Captures: `held_*flint_sword*`. |
| 8 | Bow | DONE (not play-tested) | The bow is in the LEFT hand, belly towards the target (`WPN_bow` grip 0,0,180 / 0,-0.11,0). Clips Bow_Aim / Draw / Release were rebuilt so the draw hand reaches the cheek. Capture: `held_Bow_Draw*`. |
| 9 | Spear two-handed | DONE (not play-tested) | Attack_Spear, Spear_Attack_2, Attack_Spear_Heavy and Throw_Spear were rebuilt from weapon targets. The rear hand is underhand, the front hand overhand on the shaft, and the spear stays level through the pull-back. Capture: `held_Attack_Spear*`. |
| 10 | Hunting: carcass, butchering, flee | DONE (not play-tested) | `Carcass` (hold E; a knife gives the full yield), carcass on death, zig-zag flee. |
| 11 | Interactive bushes + thickets | DONE (not play-tested) | 150 single bushes (Plain 90, Berries 30, HiddenItem 18, AnimalFlush 12) plus 61 dense thickets with 452 interactive bushes for stalking (built around the new ENV_Bush_04 thicket and ENV_Bush_05 shrub models, one trigger each, LOD / distance cull). Contents 70 % plain / 20 % berries / 10 % flush; a flush startles the small creatures nearby. Materials remapped to the shared foliage. Captures: `bush_*`. |
| 12 | Climbing | DONE (existing) | PlayerClimb / Climbable kept. The equipment hides while climbing. The OnHarvest receiver was added. |
| 13 | Fruit | DONE (existing) | FruitCluster kept. The tree interaction range was fixed. |
| 14 | Crafting | DONE (not play-tested) | Requirements (tool / station / day), extra results, a RESOURCES tab and the queue saved in the save file. 9 new recipes, 8 new items, 33 items / 25 recipes in total. |
| 15 | Polish: sword block input | DONE (Play-verified by tests) | Hold right mouse with a sword / knife (`PlayerCombat.IsBlocking`): 70 % less damage from the front (110 deg arc), costs stamina, breaks when stamina runs out. Row in the F1 table. `CombatPolishTests` (3 block tests). |
| 16 | Polish: additive hit reaction layer | DONE (Play-verified by tests) | `HurtLight` on an additive HitReaction layer: a light hit flinches the upper body while the legs keep running; heavy hits still use the full Hurt state. `HitReactionTests` (2). |
| 17 | Polish: weapon on the back / hip when not in hand | DONE (Play-verified by tests) | `PlayerEquipment` shows the other hotbar weapons on the back / hip (`WeaponData.carrySocket`), no duplicate when drawn. `CombatPolishTests` (2), capture `Screenshots/Survival/holster_back_hip.png`. |
| 18 | Optimization | PARTIAL | Pooled arrows / VFX, cached layer masks, corpse Animator culling, bush LOD + distance cull. No profiler pass. |
| 19 | QA | PARTIAL | See QA_REPORT.md. PlayMode suite run on the owner's request: 68 / 68 pass. Nobody has play-tested by hand. |

## Owner's list (done last)

| Item | Status | Notes |
|---|---|---|
| Wind for trees | DONE (Play-verified by test) | `PF/Foliage Wind` (lean, vertex sway with the same sway in the shadow pass). `PrimalShaderBuilder.WindSplit` gives trees and bushes their own wind copies (M_Bark_Wind, M_Foliage_Wind, *_BushWind), so fallen logs, ferns, grass and resource nodes stay still. `WindTests`: a tree vertex moves in Play. Rollback: `PrimalShaderBuilder.WindRevert`. |
| Heat haze + ash | DONE (not play-tested) | Ash fall around the player (at most 320 flakes, denser near the volcano) and faint heat shimmer billboards over the crater. Edit-mode preview: `volcano_fx.png`. |
| Starry sky | DONE | The NightSky dome fades in after sunset. Edit-mode preview at 23:00: `night_stars.png` (stars behind the volcano and the trees). |
| PC controls guide + key hints | DONE (tests) | F1 or Esc > CONTROLS / PHÍM, table from the real bindings (`ControlsGuideTests`). New: context key hints in a corner (`UI/ContextHints`: held weapon, fire, water, night, menus) and one-time tips, saved; on / off in Settings (`ContextHintsTests`). `Documentation/CONTROLS.md` + README. |
| Build for phone | NOT DONE (not wanted now) | The owner plays on PC for now. |
| Clean up My project | DONE | Earlier in this cycle. |
