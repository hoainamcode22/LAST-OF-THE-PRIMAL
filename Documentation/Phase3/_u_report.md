# U report (COMBAT + ANIM, Unity): bare hands, FX / audio, mobile, dodge. Phase 3 / 3.5 wave 1, 2026-09-29

Design and data: `Documentation/BARE_HAND_COMBAT.md`.

Evidence:
- PlayMode results `Documentation/Tests/playmode_results.xml` (copies in `Tools/playmode_results_U45.xml` and `_U49.xml`);
- builder log `Documentation/CharacterTests/Player_test.md`;
- bridge results `Library/PrimalBridge/result_U_3x.json` / `result_U_4x.json`.

## 1. Result per task

| # | Task | Status | Evidence |
|---|---|---|---|
| 1a | Bare-hand combat inside the existing architecture | **PASS** | Bare hands = `WeaponData` `Resources/Combat/WPN_bare_hands` through `WeaponController` / `MeleeWeapon` / `WeaponHitbox`. No second controller, no new attack booleans; `PlayerState.Activity` added. |
| 1b | Light combo 1-2-3 (buffered, window), hold = heavy, Combo_End | **PASS** (Combo_End waits for its clip) | Test: actions 50 -> 51 -> 54, one hit each, 4.0 / 4.4 / 5.6 damage, finisher staggers; heavy on hold 9 damage; HEAVY button heavy at once |
| 1c | Data-driven, weak damage | **PASS** | damage 4 / heavy 9, stamina 5 / 11, speed x1.1, reach 1.0 / 1.15 m (down to 0.3 m), knockback 0.6 / 2.5 m/s, windows per step |
| 1d | Hit timing from animation events, same hit path as weapons | **PASS** | OnAttackHit fired during each punch (punch 1 placeholder: Hit only; punches 2 / 3: Start, Active, Hit, End); one hit per target per punch |
| 1e | `HitInfo.unarmed = true`, knockback | **PASS** | every punch unarmed, knockback 0.60 / 0.66 / 0.84 (light chain), 2.5 (heavy); spear hits unarmed = false |
| 1f | Creatures scale unarmed damage (AI side) | **NOT TESTED (AI side not landed)** | Island test: velociraptor (80 HP) loses 4.0 = 5.00 % per jab, ankylosaurus (750 HP) 4.0 = 0.53 %; plain damage, no scaling yet |
| 1g | Hit reaction BareHand_HitReaction when unarmed | **WIRED, waiting for the clip** | HitReaction layer: BareHand_HitReaction on HurtLight with WeaponType 0 once the clip exists; Hurt_Additive meanwhile |
| 1h | Spear unchanged, hotbar switching | **PASS** | test: spear thrust 14 damage (not unarmed), empty slot -> fists, back to the spear |
| 1i | Placeholder clips until CHAR's arrive | **PASS** | BuildAndTest PASS: BareHand_Punch_1 / 2 / 3 / Heavy = Knife_Attack / Sword_Attack_1 / Sword_Attack_2 / Sword_Heavy; Collect_Water = Drink, Gather_Stone_Hand / Gather_Branch = Gather_Plant, Bandage_Use = Use_Item |
| 1j | Staging FBX + manifest -> import, probe, captures | **NOT DONE: nothing staged** | `E:\Model game khủng long\export\staging\` empty at every check (last 08:54 UTC) |
| 1k | Action ids for RES / SURV | **PASS** | `PlayerActions.GatherStoneHand = 16`, `GatherBranch = 17` (loops, OnGatherHit), `BandageUse = 18`, `CollectWater = 14` (one-shots); states exist now (placeholders) |
| 2a | Original WAVs for the 11 new SfxIds | **PASS** | `Tools/Audio/sfx_synth.py` `phase3()` (own seed, `--only phase3`): 26 WAVs, WaterBoil a seamless 4 s loop (seam step 0.014); the 87 older WAVs regenerate byte-identical |
| 2b | PrimalAudioBuilder with mix values | **PASS** | `PrimalAudioBuilder.BuildLibrary`: 50 ids, 115 clips, none missing (was 39 / 89) |
| 2c | Pooled prefabs for the 5 new VfxIds | **PASS** | `PrimalVfxBuilder.AppendPhase3`: VfxLibrary 27 -> 32 (other effects, campfire and blood untouched); pool test passes |
| 3a | Touch attack: tap = light / combo, heavy separate | **PASS** | HEAVY button (melee / bare hands), ATTACK tap = next light strike, hold repeats light every 0.32 s, bow keeps hold-to-draw; label PUNCH / ATTACK / EAT / DRINK / PLACE / BUILD |
| 3b | Contextual GATHER / DRINK / FILL / HARVEST / CLIMB | **PASS** (HARVEST by unit test) | island: Gather Driftwood -> GATHER, Climb the fruit tree -> CLIMB (and it climbs), Drink stream water -> DRINK, Fill Leaf Cup -> FILL; label unit test covers HARVEST / PICK UP / USE |
| 3c | F1 table and ContextHints for bare hands | **PASS** | rows "Attack / punch, tap again: combo" and "Heavy attack / punch" (ControlsGuideTests pass); hints "[LMB] Punch  [Hold LMB] Heavy punch", dodge; `docs/CONTROLS.md` rows updated |
| 4 | Dodge check | **PASS after a fix** | i-frames, stamina, cooldown tested. Fixed: the Dodge clip's `OnDodge` event had no receiver, so Unity logged an error on every dodge. |
| 5 | Tests | **PASS** | 10 new tests (below) pass; full suite **80 passed, 0 failed, 7 skipped** (U_49), compile rc=0 |
| 6 | Docs | done | `BARE_HAND_COMBAT.md`, this report |

## 2. Found and fixed on the way (not in the task list)

- **Climbing could not start on a fresh player.**
  - `Climbable.CanInteract` needs a `PlayerClimb` component, and only `Climbable.Interact` added one.
  - `Interact` is never called while `CanInteract` is false. So the prompt was greyed and E did nothing. The island test
    showed "enabled False, PlayerClimb False".
  - Fix: `PlayerInteraction.Start` adds `PlayerClimb`. The island test now climbs with the CLIMB button and lets go.
- **Clip events without receivers:** `OnClimbStep`, `OnClimbGrab` and `OnDodge`. Unity logged "AnimationEvent has no
  receiver" on every climb step and dodge. Added to `CharacterAnimationEvents`.
- **Hit window for manifest clips:** a clip with OnAttackStart / OnAttackHit / OnAttackEnd but no OnAttackActive never
  opened the hitbox. `MeleeWeapon` now opens the window at OnAttackHit. The builder's default events also include
  OnAttackActive.
- **Swing sound:** clips without OnAttackStart (knife, placeholders) had no swing sound; it now plays when the window
  opens.
- **Tool break feedback:** a weapon that breaks on a hit (`WeaponBase.Wear`) now plays `ToolBreak` and a small
  `DustImpact` at the hand, as well as showing the note.

## 3. Files changed (mirror paths; deployed U12-U18)

- **Combat:**
  - `Scripts/Combat/Weapons/WeaponData.cs` (unarmed, knockback, reachDown, heavy / effort feedback, `BareHands`, defaults)
  - `Scripts/Combat/Weapons/WeaponController.cs` (bare-hand resolve, `FightsBareHanded`, `BareHanded`, `CurrentIsHeavy`, HEAVY input)
  - `Scripts/Combat/Weapons/MeleeWeapon.cs`
  - `Scripts/Combat/Weapons/WeaponHitbox.cs`
  - `Scripts/Combat/Weapons/IWeapon.cs`
  - `Scripts/Combat/Weapons/WeaponBase.cs`
- **Player:**
  - `Scripts/Player/PlayerCombat.cs` (routing, no bare-hand block)
  - `Scripts/Player/PlayerInputReader.cs` (`HeavyPressed`: touch, `Sim.Heavy`, gamepad right shoulder)
  - `Scripts/Player/PlayerInteraction.cs` (`UseHandlers`, PlayerClimb)
  - `Scripts/Player/PlayerState.cs` (`PlayerActivity`)
  - `Scripts/Player/PlayerAnimationDriver.cs` (`IsHurtState`)
- **Animation:**
  - `Scripts/Animation/AnimParams.cs` (ids 16-18, 50-55, `IsGather`, `IsHeavyAttack`)
  - `Scripts/Animation/CharacterAnimationEvents.cs`
- **UI:**
  - `Scripts/UI/MobileHUD.cs`
  - `Scripts/UI/ControlsPanel.cs`
  - `Scripts/UI/ContextHints.cs`
- **Editor:**
  - `Scripts/Editor/PrimalCharacterBuilder.cs` (placeholders, BareHand_* states, Combo_End, BareHand_Idle, BareHand_HitReaction, default events)
  - `Scripts/Editor/PrimalCharacterBuilder.Combat.cs` (new: `BuildBareHands`)
  - `Scripts/Editor/PrimalAudioBuilder.cs` (mix values, `BuildLibrary`, no reimport of configured clips)
  - `Scripts/Editor/PrimalVfxBuilder.cs` (`BuildPhase3`, `AppendPhase3`)
- **Tests:** `Tests/PlayMode/BareHandCombatTests.cs` (new)
- **Assets:**
  - `Audio/SFX/SFX_{PunchWhoosh 1-3, PunchHit 1-3, PunchHeavyHit 1-2, PlayerGrunt 1-3, WaterFill 1-2, WaterBoil 1, BandageWrap 1-2, ToolBreak 1-2, FireHiss 1-2, BranchSnap 1-3, StoneGatherHand 1-3}.wav`
  - `Resources/SfxLibrary.asset`
  - `VFX/Prefabs/VFX_{PunchImpactSmall, PunchImpactHeavy, DustImpact, Heal, BoilBubbles}.prefab`
  - `Resources/VfxLibrary.asset`
  - `Resources/Combat/WPN_bare_hands.asset`
  - `PlayerAnimator.controller` and the player prefab (BuildAndTest)
- **Tools:** `Tools/Audio/sfx_synth.py` (copied to the PC directly; backup `sfx_synth.py.before_phase3`)
- **Docs:** `docs/CONTROLS.md` (two combat rows)

## 4. Tests (all pass, full suite U_49: 80 / 0 / 7)

`BareHandCombatTests`:

| Test | Time |
|---|---|
| Starts_Unarmed_With_The_Fists | 0.5 s |
| Light_Combo_1_2_3_Hits_With_Events_And_Spends_Stamina | 3.3 s |
| Heavy_On_Hold_And_On_The_Heavy_Button | 3.8 s |
| Too_Tired_To_Punch | 0.7 s |
| Items_With_A_Use_Keep_It_Items_Without_A_Role_Punch (wood punches and is not used up; berry eats) | 1.4 s |
| Spear_Still_Fights_And_Hotbar_Returns_To_The_Fists | 2.3 s |
| Dodge_Has_IFrames_Stamina_And_Cooldown | 2.6 s |
| Touch_Labels_Follow_The_Prompt | 0.3 s |

`BareHandIslandTests`:

| Test | Time |
|---|---|
| Punch_Small_Creature_Hurts_Large_Barely | 9.3 s |
| Touch_Buttons_Show_The_Context | 7.4 s |

All have `[Timeout]`, the island tests call `UseTestSaves`, and none uses `WaitForEndOfFrame`. Any error log fails a test,
so "no NullReference" is covered by every run.

## 5. Waiting for CHAR's clips

**Clips that replace placeholders:**
- BareHand_Punch_1, BareHand_Punch_2, BareHand_Punch_3, BareHand_Heavy;
- Collect_Water, Gather_Stone_Hand, Gather_Branch, Bandage_Use.

**States that appear once their clips exist:**
- BareHand_Combo_End, BareHand_Idle, BareHand_HitReaction;
- Gather_Enter / Gather_Exit;
- Walk_Start, Walk_Stop, Run_Start, Run_Stop, Run_Pivot_180, Turn_180.

**When `PLAYER_Survivor.fbx` + `clips_manifest.json` are staged:**
1. Back up the Assets FBX and copy the staging FBX in.
2. Put the manifest next to `PLAYER_Survivor_anim.json`.
3. Run `BuildAndTest "Player"` and reopen the island.
4. Add receivers for any new event names in the manifest.
5. Re-run the probe against the Phase A table, and the held-weapon captures.
6. Turn on `useStartStopClips` only if the probe shows less foot slide.
7. `CombatPolishTests.ExpectAdditiveLightHurt` expects Hurt_Additive with empty hands. Once BareHand_HitReaction exists,
   that test needs to accept it: the old expectation becomes wrong by design.

## 6. Requests to other agents

- **AI (P_):**
  - `DinosaurController` / `AmbientCreature.TakeHit`: scale damage when `hit.unarmed` by size (large creatures barely),
    and apply `hit.knockback` (m/s) to small ones.
  - Today a jab is plain 4 damage: 5 % of a raptor, 0.5 % of an ankylosaurus.
- **RES (R_):**
  - Small nodes: implement `IDamageable`. Bare-hand hits arrive as normal `HitInfo` with `unarmed = true`, 4-9 damage,
    point and direction.
  - Use `PlayerActions.GatherStoneHand` / `GatherBranch` as `handAction`.
  - PlayerFeedback: play `StoneGatherHand` / `BranchSnap` and `DustImpact` for them.
  - `ResourceNode.cs:103`: play `ToolBreak` where it shows "X broke!".
- **SURV (S_):**
  - Bandage: register a `PlayerInteraction.UseHandlers` entry (`handles` = bandage, `use` = `DoOneShot(PlayerActions.BandageUse, "OnUseItem", ...)`
    + `StopBleeding` + `BandageWrap` + `VfxId.Heal`). The attack button and the inventory Use then route to it, and it is
    never punched with.
  - `CollectWater` (14) for filling.
  - `WaterFill` on fill.
  - `WaterBoil`: its clip loops seamlessly; play it on a looping AudioSource while boiling.
  - `BoilBubbles`: a 1.5 s burst, not a loop; replay it about once a second while boiling.
  - `FireHiss` when rain puts a fire out.
  - `Heal` on healing.
- **CHAR (C_):**
  - Clip names as listed in section 5.
  - Punch events OnAttackStart / OnAttackActive / OnAttackHit / OnAttackEnd. Contact: jab at frame 4-6, heavy at 8-11.
  - Gathers: OnGatherHit. Bandage: OnUseItem.
  - Any new event name needs a receiver (tell U).
- **Lead:**
  - Directive 44 "gather once on press, optional hold to repeat" is not done. Gathering still loops until the node is
    empty or you cancel, on every platform (`PlayerInteraction.DoLoop` with RES's nodes). Changing it is a design choice
    for RES + U.
  - `Climbable` / `PlayerClimb` are unowned; the climb fix is in `PlayerInteraction` only.

## 7. Notes

- Placeholder punches are sword and knife motions, 0.8-0.9 s at attack speed 1.1. The combo window (1.1 s from a punch's
  start) is set for them; with 14-frame jabs it leaves about 0.6 s after a jab.
- Blocking is off for bare hands, so the aim / block button does nothing unarmed. `Unarmed_Block` (id 36) is wired for a
  later clip.
- The bridge lock was held once for about 13 minutes (08:33-08:46, deploy + full suite U_45).

## Integration round (2026-09-29)

Code is done and compiles (`cc.sh all`: runtime / editor / tests rc=0). The zip `Tools/pf_up_U19.zip` is on the PC.

**NOT DEPLOYED, NOT TESTED: bridge lock.** The lock read "LEAD 12:48:52" from 12:48 to at least 13:37 UTC. The Lead's full
suite finished at 13:02 (143 / 0 / 8) and the bridge was idle after that. Reported to the Lead; the lock was not broken.

To finish: take the lock, run `$HOME/deploy.sh pf_up_U19`, then `run.sh <id> PrimalTestRunner.RunPlayMode "BareHand" 3`.
`BuildAndTest` is not needed: the builder change only affects clips exported without events.

| # | Task | Change | Test (in `BareHandCombatTests.cs`) |
|---|---|---|---|
| 1 | Arm injury on attack damage | One helper, `WeaponHitbox.AttackerMultiplier(owner)` = the owner's `PlayerStatusEffects.AttackMultiplier` (1 without it). It is applied in `WeaponHitbox.Apply` (every data-driven melee weapon and the bare hands); the legacy `PlayerCombat.ResolveMelee` calls the same helper. Throws and arrows are not scaled. | `Arm_Injury_Weakens_The_Punch`: jab = 4 x AttackMultiplier (0.7 at full severity) |
| 2 | One ToolBreak per break | The active-slot break goes through `InventorySystem.WearActive` -> `ToolBroke` (HUD: sound, puff, note). The duplicate in `WeaponBase` (sound, puff, note) is removed for that path; `WeaponBase.Broke` stays only for a stack broken outside the active slot, where `ToolBroke` does not fire. The legacy melee "broke!" note is removed too. `SfxPlayer.Played` event added so tests can count sounds. | `BareHandIslandTests.Weapon_Break_Plays_One_ToolBreak`: exactly 1 ToolBreak sound, 1 ToolBroken event |
| 3 | Receivers `OnUseItem` / `OnDrink` | Both already existed; documented as the Bandage_Use / Collect_Water receivers. Added `OnBandage`, `OnCollectWater`, `OnScoop` for likely manifest names. The builder now gives Collect_Water a default `OnDrink` at 0.6 when exported without events (as Bandage_Use gets `OnUseItem`). | `Every_Clip_Event_Has_A_Receiver`: every event in the player controller's clips, plus the new names, has a method |
| 4 | Mobile labels | `MobileHUD.ContextLabel(prompt, target, climbing)`: a `ResourceNode` is HARVEST when its definition category is Food, else GATHER (whatever the verb). Prompts starting with "Catch" / "Fish" are GATHER. | Unit: "Catch Fish" -> GATHER, "Harvest" -> HARVEST. Island `Touch_Buttons_Show_The_Context`: explicit food node -> HARVEST; holding the GATHER touch button (`TouchButton.Down`, touch input path with `Simulate` off) repeats the gather (at least 2 hits), and a tap gathers exactly 1 |
| 5 | `GameEventType.ProjectileLanded` | Appended at the end of `Core/GameEvents.cs` (re-read first). Raised in `Projectile.Impact` when an arrow or thrown spear hits anything but a living creature (ground, rock, tree, resource node, dead body), at the point where it comes to rest. Flights that time out or fall out of the world raise nothing. | `Missed_Projectiles_Raise_ProjectileLanded`: arrow and spear dropped on the course floor, id / amount / position checked |

**`ProjectileLanded` payload** (standard `GameEvent`):
- `id`: the projectile's item id (e.g. `arrow`, `stone_spear`), or `"arrow"` / `"spear"` without one;
- `amount`: loudness, 1 for an arrow, 2 for a spear;
- `position`: where it lies (the ground contact point, or 0.3 m off the surface it bounced from).

**Files:**
- `Scripts/Core/GameEvents.cs` (append only)
- `Scripts/Combat/Projectile.cs`
- `Scripts/Combat/Weapons/WeaponHitbox.cs`
- `Scripts/Combat/Weapons/WeaponBase.cs`
- `Scripts/Player/PlayerCombat.cs`
- `Scripts/Audio/SfxPlayer.cs`
- `Scripts/Animation/CharacterAnimationEvents.cs`
- `Scripts/Editor/PrimalCharacterBuilder.cs`
- `Scripts/UI/MobileHUD.cs`
- `Tests/PlayMode/BareHandCombatTests.cs`
