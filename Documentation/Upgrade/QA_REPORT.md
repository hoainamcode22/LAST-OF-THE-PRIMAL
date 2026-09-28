# QA REPORT (upgrade cycle, 2026-09-28)

What was really checked, how it was checked, and what was NOT checked. Nothing here was play-tested by a person
(the PlayMode tests drive the game automatically; that is not the same as a person playing).
Images are in `Documentation/Screenshots/Upgrade/`.

## 1. Build / compile

| Check | How | Result |
|---|---|---|
| C# compile (runtime, editor, PlayMode tests) | cloud `csc` against the Unity 6000.3 reference DLLs, then the Unity editor compile after every deploy (pf_up_3 .. pf_up_8) | 0 errors |
| New shaders `PF/Foliage Wind`, `PF/Night Sky`, `PF/Heat Shimmer` | bridge `PrimalCharacterDiagnostics.ShaderCheck` (ShaderUtil messages) | supported, 0 errors, 0 messages (variants compiled so far) |

## 2. Character (rig, weights, clips)

| Check | How | Result |
|---|---|---|
| Weights | Blender script: influences per vertex, normalization (LOD0/1/2) | max 4 influences, 0 vertices not normalized |
| Deformation | renders of shoulder raise 90, shoulder forward 90, wrist twist +-80 and elbow 110 after the fix, next to the audit renders | shoulder keeps volume, pelt follows without tearing, wrist twist spread over the forearm (`deform_before_after.png`; the before/after poses are not identical, the audit used a smaller shoulder raise) |
| Relaxed hands | close-up renders (Idle, Walk, Run, Idle_Variation) | graded finger curl, thumb along the index (`hands_relaxed.png`, `hands_thumb.png`) |
| Weapon key poses | Blender weapon-target solver: grip position / blade direction error per key | most keys < 2 cm and < 5 deg. The largest misses are the sword down-cut keys (up to 5 cm / 22 deg) and the spear front hand at full thrust (8-10 cm), which the off-hand IK closes in game |
| Motion | filmstrips of the baked clips | `clips_sword.png`, `clips_spear_bow.png`, `clips_strafe.png` |
| Unity character test | bridge `PrimalCharacterBuilder.BuildAndTest("Player")` | **PASS**: avatar valid / humanoid, 58 clips (29 loops, 90 events), foot slide 1-4 % on all locomotion clips incl. Run_Backward / Strafe_Run_L/R, 0 unskinned vertices, 100 % of vertices move with bones, facing +Z, twist bones found (avatar lowerArmTwist = 0, TwistBoneDriver added) |
| Retargeting keeps the hand orientation | bridge `HandPose`: thumb-side direction of each hand in Unity vs the Blender target | matches within a few degrees (Sword_Idle, Sword_Attack_2, Attack_Spear, Throw_Spear, Bow_Draw, Sword_Block) |
| Held weapons in Unity | bridge `HeldWeapons` (player prefab, sampled clip, game grip frame + WeaponData grip) | sword in the right fist along the fist, spear level with both hands on the shaft (off-hand 0.1-0.2 m from the wrist before IK), bow in the left hand with the belly to the target and the draw hand at the cheek, axe unchanged (`held_weapons.png`) |

## 3. Gameplay data

| Check | How | Result |
|---|---|---|
| Weapons | bridge `PrimalWeaponBuilder.Build` | 10 WeaponData assets linked to their items, `ITEM_flint_sword` created, in the ItemDatabase, model + icon, M_Tools remapped |
| Crafting | bridge `PrimalCraftingBuilder.Build` | +8 items, +9 recipes (33 items / 25 recipes), tool / station requirements shown |
| Bushes | bridge `PrimalBushBuilder.Build` | 150 placed (Plain 90, Berries 30, HiddenItem 18, AnimalFlush 12), materials remapped to the shared foliage (the first capture showed square cards before the remap; fixed) |
| Thickets | bridge `PrimalBushBuilder.Build "thickets"` | 61 thickets, 452 interactive bushes (one trigger each, LOD cull), new models ENV_Bush_04 / 05; the first thicket model looked like umbrellas on stilts and was rebuilt with foliage domes down to the ground |

## 4. World look (edit-mode captures)

| Check | Result |
|---|---|
| Wind shader on one test tree, then on all trees / bushes | renders like the original (no magenta, alpha clip, shadows); editor stayed stable. Motion NOT seen (Play only). Test tree removed |
| Night sky at 23:00 | stars visible, drawn behind the volcano, trees and ocean (`night_stars.png`) |
| Volcano ash + shimmer | 320 flakes and 7 shimmer billboards created (`volcano_fx.png`); the shimmer is very faint by design |

## 5. PlayMode suite (run later on the owner's request)

| Run | Result |
|---|---|
| First run | 47 passed, 1 failed (`LightHit_ReducesHealth_PlaysReaction_Blood_Overlay`: the new flinch outlasted the blood effect the test waited for; the check now runs before the wait) |
| After the combat / hints / wind / survival work | 68 passed, 0 failed (237 s), incl. `CombatPolishTests`, `HitReactionTests`, `ContextHintsTests`, `WindTests`, `ControlsGuideTests` and the recipe cap 15-40 |

## 6. NOT tested

- Play mode. Nobody played:
  - combat feel, combo timing, hitbox windows in play
  - bow aiming and arrows from the new left-hand bow
  - off-hand and bow IK at run time
  - strafe blend (moonwalk check)
  - measured speed against walls
  - fall delay
  - carcass butchering
  - bush rustle
  - wind motion by eye (`WindTests` only measures that a tree vertex moves), ash in play
  - F1 panel
- Performance: no profiler numbers for the bushes, ash or wind.
- Build for phone: not wanted now.

## 7. Known issues / risks

1. (fixed) Sword block, the additive hit layer and the back / hip weapons are built and tested.
2. (fixed) Wind no longer sways logs, ferns and grass: trees and bushes use their own wind materials. Still true: wind leaves are lit by the sun / moon only, so campfire light does not reach them. Rollback: `PrimalShaderBuilder.WindRevert`.
3. Ash fall is on by default near the volcano (`PrimalVolcanoBuilder.Atmosphere "off"` turns it off).
4. Strafe_Run clips only show when aim speed is raised above walk speed (aim mode caps it at walk today).
5. `bone_arrow` damage is 0 until arrows get their own damage value.
6. Follow-ups from the agents are not done:
   - PlayerFeedback: flesh feedback for carcass cuts
   - PlayerInteraction.Scan: FocusPoint
   - bone arrow selection in RangedWeapon
   - Mosasaurus carcass depth
