# AI agent report (P_), Phase 1 wave: weather reactions, predator hunts, rest / sleep clips (2026-09-30)

Verification policy of this wave: compile + Console + reference checks only (no PlayMode runs). Scope kept: existing
species only, no new dinosaurs. No git, no deletes. `.before_P1` copies were made of every existing file I changed.

## 1. What was built

| # | Task | Status | How |
|---|---|---|---|
| 1 | Weather reaction | DONE (compiled, not play-tested) | New `AI/WildlifeWeather.cs`: reads `WeatherManager.Intensity / StormK / IsStorm` once every `weatherCheckSeconds` (2 s) for all creatures, caches the factors. Rain: herd grazing place moves halfway to its tree cover (`HerdPlaces.rest`), herd spread x0.75, idle animals lie down more (rest bias 0.35), loner herbivores roam x0.6. Rain >= 0.6 or storm: herds go to their trees and Rest there (Sleep at night), loner herbivores walk to a shade spot (`HerdGroup.ShadeNear`). Storm: herd spread x0.5, predators lie low (rest phase) and never start a hunt, pteranodons glide down to a dry perch under their circle (Land clip), take off 4-15 s after it clears or at once when startled / hit. Heavy rain: predators patrol x0.55 radius, idle longer, watch herds less, hunt chance x0.25. All creatures call less (x0.4 rain, x0.2 storm), suspicious calls too. Senses: hearing keeps `rainMask` 0.4 and gets `stormHearingLoss` 0.15; smell x (1 - `rainSmellLoss` 0.3 x rain) on top of `rainScentCut` on the puffs |
| 2 | Predator hunting | DONE (compiled, not play-tested) | New `AI/HuntDirector.cs` + hunt code in `DinosaurController`. Hunger per predator (rises per game hour, saved). A hungry hunter checks every ~6 s (chance 0.25 x hunger x weather), picks a straggler of its prey list within 110 m (far from its herd centre, wounded, close), stalks (brisk walk far, 0.75 x walk near), charges at `chargeDistance` or when the prey bolts (prey flees with its herd via `FleeFrom(.., true)`, kept running), strikes up to `strikes` times: kill with `killChance` (x0.5 when the herd is one above its minimum), else wound (`woundFraction`, never the last HP, blood, hurt, flees). A kill makes the prey's normal Carcass (no `CreatureKilled`, which stays the player's); the hunter walks to the body, eats (`carcassEatSeconds` x hunger drop), takes `eatsMeat` pieces (at least 1 stays for the player), then sleeps `restHoursAfterMeal` game hours. Rules: one hunt on the island at a time; cooldown 12 min after a kill, 6 after a fail, 8 after new game / load (game clock, x0.8-1.25); herd at or below 3 alive (loner species at or below 2) never hunted; migration herd on its route never hunted; by day never within 90 m of `GameManager.spawnPoint`; ends when the player becomes a threat (Alerted), when hit, hurt, in a storm, at a fire (existing fire fear), after `chaseSeconds` or 55 m, after 100 s of stalking |
| 3 | Rest / Sleep clips | DONE (compiled, not play-tested) | New `AI/DinoAnimCaps.cs`: finds per creature at runtime whether the controller has Rest_Down / Rest_Loop / Rest_Up (+ Rest_Shift), Breathe, Bite, Land, Takeoff (from the controller clips) and the float `Intensity` (from `Animator.parameters` once initialised). With the rest set: Rest and night sleep use ActionType 3 (lie down, loop), sleep fidgets pulse Rest_Shift, a noticed player makes a lying animal lift its head (Rest_Shift) instead of standing up for a look, lying rests last 20-45 s (+ rain bias) before the usual idle time, getting up holds speed 0 while the state tag is `Rest` (no sliding; fleeing / charging bolt at once), no Call clip while lying. Without the set: unchanged phase 2 fallback (Idle_Variation), no hold. Extras from DINO_CLIPS requests: `Intensity` 0 -> 1 while chasing (hunters) / fleeing (grazers), Breathe after running 6 s+, Bite (AttackType 2) for hunt strikes when the clip exists (else lunge 0). Night: diurnal species now sleep through the whole `TimeManager.Phase.Night` (was only NightFactor > 0.7) |

Tuning (data, editable): `Resources/WildlifeConfig.asset` (created, defaults: Weather / Hunting / Resting headers),
`Resources/PerceptionConfig.asset` (new fields `rainSmellLoss`, `stormHearingLoss` use their defaults until edited),
`DINO_*.asset` field `hunt` (HuntProfile, written by `PrimalWildlifeBuilder.TuneHunting`):

| Hunter | Prey | hunger/h | hunts at | charge m | chase s | kill | strikes | wound | eats | rest h |
|---|---|---|---|---|---|---|---|---|---|---|
| Velociraptor | parasaurolophus | 0.09 | 0.6 | 16 | 10 | 0.30 | 3 | 0.10 | 1 | 2 |
| Carnotaurus | parasaurolophus, triceratops | 0.07 | 0.6 | 24 | 12 | 0.45 | 2 | 0.15 | 2 | 3 |
| Spinosaurus | parasaurolophus | 0.05 | 0.65 | 18 | 9 | 0.35 | 2 | 0.15 | 2 | 3 |
| Rift Tyrant (apex) | triceratops, parasaurolophus | 0.06 | 0.6 | 26 | 12 | 0.55 | 2 | 0.20 | 3 | 4 |

Ankylosaurus (group of 3 = minimum) is never prey.

## 2. Files

New: `AI/WildlifeWeather.cs`, `AI/HuntDirector.cs`, `AI/DinoAnimCaps.cs`, asset `Resources/WildlifeConfig.asset` (by the builder).
Changed (backup `<file>.before_P1`): `AI/DinosaurController.cs` (hunt, weather, rest clips, Intensity, Breathe; public members kept,
added `HuntTarget`, `Hunting`, `Hunted`, `Hunger01`, `AnimCaps`, `SavedState.hunger`), `AI/DinosaurDefinition.cs` (appended
`hunt` + `HuntProfile`), `AI/WildlifeConfig.cs` (appended 3 headers), `AI/PerceptionConfig.cs` (2 fields), `AI/DinoSenses.cs`
(`SmellMul`), `AI/HerdGroup.cs` (shelter / bunching, `ShadeNear` internal), `AI/AmbientCreature.cs` (flyers land, `Landed`,
`Air`), `AI/CreatureSave.cs` (`hunger` in the record, old saves read 0), `World/Carcass.cs` (`PredatorFeed`),
`Editor/PrimalWildlifeBuilder.cs` (`TuneHunting` bridge command, no scene change), `Core/GameEvents.cs` (shared, appended
`PredatorHunt`: id predator, amount 1 start / 2 kill / 3 fail). DINO_*.asset: only the `hunt` field.
Read only: `Core/WeatherManager`, `Core/TimeManager`, `Editor/PrimalDinoBuilder` (and DINO's `PrimalCharacterBuilder.Dino` to
match the controller interface).

## 3. Commands and results (bridge lock held 07:33:54 - 07:47 UTC; waited from 06:33 behind E, then DINO, who held it 06:42 - 07:33)

| id | command | result |
|---|---|---|
| P1_r_073400 | PrimalEditorBridge.Refresh | refreshed |
| P1_p_073500 | PrimalEditorBridge.Ping | ok, editor 6000.3.10f1, Island_VerticalSlice (after compile) |
| P1_c_073800 | PrimalEditorBridge.ConsoleCheck | 5 entries, **0 errors, 0 warnings** |
| P1_t_074100 | PrimalWildlifeBuilder.TuneHunting "" | created WildlifeConfig.asset; hunt values written: 9 (4 hunters, 5 none) |
| P1_c2_074400 | PrimalEditorBridge.ConsoleCheck | 18 entries, **0 errors, 0 warnings** |

Reference checks (files): the 7 land controllers rebuilt by DINO (07:22-07:23) contain Rest_Loop 1, Breathe 1, Intensity 1,
4 states tagged `Rest` (triceratops), Rest_Shift (raptor); Bite in the 4 hunters; Pteranodon keeps phase 2 with Land 1 and
Takeoff 1; Mosasaurus phase 2. `DINO_Carnotaurus.asset` shows the written `hunt` block. No scene change (no SaveScene needed).
Before my cycle, DINO's own refresh (06:45) and ConsoleCheck (07:13, 0 errors) already compiled these changes.

## 4. Requests

| To | Request |
|---|---|
| Lead / QA | PlayMode checks when the policy allows: a storm (`WildlifeWeather.SetOverride(1, 1)`) sends the crested herd to its rest place and lands the pteranodons; `HuntDirector.ResetForTests()` + a hungry carnotaurus near the triceratops herd produces one `PredatorHunt` 1 then 2 or 3; `DinosaurTests` / `PerceptionIslandTests` should stay green (hunts wait 8 min of game clock after a new game, so tests should not see one) |
| STORY | `CreatureJournal` can use `DinosaurController.Hunting` / `Hunted`, `AmbientCreature.Landed` and `GameEventType.PredatorHunt` (amount 2 = a kill) for journal notes ("hunting", "sheltering") |
| DINO | visual check of a perched pteranodon (pivot height on the ground, Land then Idle) and of Rest_Up hold times; turn-in-place (`Turn` / `TurnMul`), `Stop` and `Recover` after a missed lunge are not wired yet |
| WORLD | none (WeatherManager only read: `Intensity`, `StormK`, `IsStorm`, `Instance`) |

## 5. Not done / limits

- No PlayMode run (wave policy): behaviour is compiled and reference-checked only.
- `Turn` / `TurnMul`, `Stop` trigger and Recover after a missed lunge (DINO_CLIPS 3.3 / 3.4 / 3.5) not wired.
- Hunts are solo (no raptor pack hunt); predators do not hunt each other or the ambient creatures; the mosasaur ignores weather.
- A hunt is not saved: a load ends it and restarts the delay (hunger and the carcass are saved).
- Perch search is random under each flyer's circle (dry, walkable, >= 1.2 m high); a circle fully over the sea finds none and keeps flying.
