# QA REPORT (upgrade cycle, 2026-09-28)

What was really checked, how it was checked, and what was NOT checked. Nothing here was play-tested by a person.
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

## 4. World look (edit-mode captures)

| Check | Result |
|---|---|
| Wind shader on one test tree, then on all trees / bushes | renders like the original (no magenta, alpha clip, shadows); editor stayed stable. Motion NOT seen (Play only). Test tree removed |
| Night sky at 23:00 | stars visible, drawn behind the volcano, trees and ocean (`night_stars.png`) |
| Volcano ash + shimmer | 320 flakes and 7 shimmer billboards created (`volcano_fx.png`); the shimmer is very faint by design |

## 5. NOT tested

- Play mode. Nobody played:
  - combat feel, combo timing, hitbox windows in play
  - bow aiming and arrows from the new left-hand bow
  - off-hand and bow IK at run time
  - strafe blend (moonwalk check)
  - measured speed against walls
  - fall delay
  - carcass butchering
  - bush rustle
  - wind motion, ash in play
  - F1 panel
- PlayMode test suite: not run (it enters Play mode; the owner asked for no automatic Play). `ControlsGuideTests` and the updated recipe cap (15-40) are untested.
- Performance: no profiler numbers for the bushes, ash or wind.
- Build for phone: not wanted now.

## 6. Known issues / risks

1. No input for sword block (clip and state exist). No additive hit-reaction layer. Weapons are not shown on the back / hip when holstered.
2. The wind material switch is global: fallen logs, ferns, grass, detail bushes and resource nodes that share M_Bark / M_Foliage sway a little too. Switched leaves are lit by the sun / moon only, so campfire light does not reach them. Rollback: `PrimalShaderBuilder.WindRevert`.
3. Ash fall is on by default near the volcano (`PrimalVolcanoBuilder.Atmosphere "off"` turns it off).
4. Strafe_Run clips only show when aim speed is raised above walk speed (aim mode caps it at walk today).
5. `bone_arrow` damage is 0 until arrows get their own damage value.
6. Follow-ups from the agents are not done:
   - PlayerFeedback: flesh feedback for carcass cuts
   - PlayerInteraction.Scan: FocusPoint
   - bone arrow selection in RangedWeapon
   - Mosasaurus carcass depth
