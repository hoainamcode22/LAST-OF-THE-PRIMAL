# AI report (P_): herds, herd day, migration, predator territories, weight, tracking signs (PC phase, 2026-09-29)

Code in the cloud mirror (`src/`). Deploy zip `pf_up_P11` (18 files, only mine + my append to `Core/GameEvents.cs`) is on the
PC at `Tools/pf_up_P11.zip`, NOT extracted. Status words: DONE-VERIFIED (how), DONE-NOT-TESTED, PARTIAL, NOT COMPLETED.
Editor state: it has taken no bridge command since 15:33 UTC (the bridge skips commands in Play mode); my pings `P_16`
(19:51 UTC), `P_17` (20:00), `P_ping3` (20:02) and `P_ping4` (20:10) got no answer. Nothing of this round ran on the PC: every item is cloud-compiled only.

## 1. Result per item

| # | Item | Status | Where / how |
|---|---|---|---|
| 1a | Herds of 3-12, loose, own pace and idle timing | DONE-NOT-TESTED | `AI/HerdGroup.cs`: members = the creatures under the herd object; each keeps its own place in the herd (unit disc, kept apart, re-picked every 25-60 s), its own walk speed (x0.86-1.12) and idle / eat timing (x0.7-1.45). Grazing circle 6 m + 0.9 m per member; travel = a loose column (7 + 0.5 n by 16 + 3.5 n m). Stragglers trot (x1.7), the front ones dawdle, a member near its place snatches a bite while the herd waits. No formation |
| 1b | Herd day by world time | DONE-NOT-TESTED | `TimeManager.CurrentPhase`: Dawn graze at the night place, Morning graze, Noon walk to the nearest fresh water (DrinkSpots, 220 m), each member drinks on its own bit of bank (9-16 s, 30 % twice), then rest in the shade (spot with trees around, not inside them) until afternoon + 1.6 h, graze a second place, Dusk walk to safer ground (open, far from trees and predator homes), Night sleep huddled (spread x0.55, tighter for night-shy species). Individual thirst is off for herd members |
| 1c | Reactions to the player | DONE-NOT-TESTED | `DinosaurController.ReactToPlayer`: look up (Observe, eating stops), after 4-10 s of a calm, still player at a distance it goes back to feeding and ignores the player for 18-40 s, looking up every 4-9 s; inside its comfort distance (max(personal x1.8, observe x0.62)) or when the player runs / closes in faster than 1.1 m/s it walks away and the whole herd drifts 16-30 m away with it; close = flight. Defensive species with aggression >= 0.5 (triceratops, ankylosaurus) stand their ground instead, face the player and threaten |
| 1d | Alarm spreads | DONE-NOT-TESTED | awareness shared with the whole herd (`ShareAlert`); a member that bolts sends the alarm through the herd: each follows after distance / 22 m/s + 0.05-0.45 s, turning to look first (`HerdGroup.Alarm`, `FleeLater`) |
| 2a | Migration world event | DONE-NOT-TESTED | `AI/MigrationDirector.cs` on the crested herd (parasaurolophus, 9): every 2nd game day at morning + 0.25 h it walks `Markers/Migration/Route_00..13` meadow -> ford (35 s drinking in the water) -> waterfall -> terrace -> resting hollow; two days later back. Schedule from the clock (`CompletedBy`, `SideAfter`): slept through / loaded later = the herd is moved over while the player is > 170 m from both ends, else it walks now. About 5 real minutes, done before noon |
| 2b | The view from migration_view | DONE-NOT-TESTED | while it moves: dust puffs from the feet (`VfxId.DinoFootDust` x1.6 body, 3.5 / s, splashes at the ford, less in rain), an original synthesised rumble of many footfalls (loop made in code, heard to 260 m, follows the herd), calls every 5-11 s carrying 240 m (with the call pose when standing), bird flocks bursting from trees beside the leading animals every 7-14 s (`AI/BirdFlush.cs`, pooled particles, two-frame silhouettes, + leaves VFX, rustle, pteranodons startled), plants trampled flat behind the herd (track signs), bushes shake from the bodies (existing BushInteraction). The migrating herd keeps full animation to 250 m (tier distance x0.8) |
| 2c | Events for STORY | DONE-NOT-TESTED | appended to `Core/GameEvents.cs`: `HerdSighted` (id species, amount members in view; once per herd per game day: 3 in view within 95 m, terrain line of sight), `MigrationStarted` (id species, amount 1 out / 2 back, position start), `MigrationSeen` (id species, once per migration: camera within 170 m, looking at a member, terrain line of sight, 3 s), `TracksFound` (id species, amount 1 herbivore sign / 2 predator sign, position the sign) |
| 3a | Predators tied to prey territories | DONE-NOT-TESTED | `AI/WildlifePlan.cs` by ENV location ids: raptors (pack of 3) at the deep forest's edge facing the valley (small, forest edge), carnotaurus on the river / meadow edge (river + (-26, 35), medium), spinosaurus on the open grassland terrace by the migration's far side (route 11 + (-6, 32), large, open), Rift Tyrant in `predator_territory` by the kill site (apex, rare). `Markers/Habitats` HAB_* follow (builder) |
| 3b | observe / investigate / stalk / chase / attack / retreat | DONE-NOT-TESTED | a calm hunter sometimes (35 %) walks to watch the nearest herd from 30-42 m for 10-22 s (the herd sees it and watches back / moves away), now and then a 2.6 s test rush (aggression x 25 %, only with the player within 150 m) that scatters the herd; with the player: Engaged but beyond range = stalk (aggressive) or watch then lose interest; stalking freezes 1-2 s when the camera looks at it; chase / attack as before; retreat when badly hurt (all except the most aggressive giant). Predators eat 25-45 s at a carcass they smelled (STORY's "seen eating") |
| 3c | FireFear / NightFear / Aggression / Investigation per species | DONE-NOT-TESTED | `DinosaurDefinition.behaviour` (NightFear, Aggression, Investigation) + FireFear = `fireFear.predatorFear` (properties `FireFear`, `NightFear`, `Aggression`, `Investigation`). NightFear: night roam radius, herbivores jumpier and wider personal space at night, herd huddle. Aggression: attack range x0.65-1.3, stalk chance, charge distance, territory reach, rush chance. Investigation: search points x0.4-1.6, smell threshold x1.4-0.7, incurious hunters only look at far noises, herbivores go back to feeding sooner. `FireResponse` appended `Investigate` (paces the edge, half patience, leaves) and `AttackAnyway` (hesitates 3 s, then comes in while after the player) |
| 4 | Weight per species | DONE-NOT-TESTED | `DinosaurDefinition.locomotion` + controller + `DinoLife.Weight`: heavy = slow braking, slow pivot on the spot (pivotRate), arcs instead of spinning, torso roll in step with the walk clip, lean into turns, pitch on speeding up / braking, tail swings out against the turn with lag and lifts at a run; small = pivot and dart. Footfalls: sound by weight (x1.4 running), dust, camera shake by weight within 17-41 m. Every animal differs a little (pace, turn, patience) |
| 5 | Tracking signs | DONE-NOT-TESTED | `AI/TrackSigns.cs` (+ `TrackArt`, `TrackSignSpot`): footprints on every step (five original foot shapes with a normal map, atlas checked offline, left / right mirrored, fewer when far), flattened plants behind moving herds, claw grooves on trunks in predator territories, dung (simple original meshes) where herds graze and from grazing animals, blood drops from wounded animals and at a death, old kill sites (stain + bones + prints) in the big hunters' ground, old trails along the route. Fade over game hours (2 h fresh + 10 h, rain x3 faster), 900 signs max, drawn instanced within 48 m. One interactable follows the nearest unread sign: "Examine the tracks / trampled plants / claw marks / droppings / blood / bones" -> a short thought (ProtagonistVoice), `TracksFound`, and the DISCOVERIES page (`herd_tracks`, `footprint`, `claw_marks`, `blood_trail`, `kill_site`); the trail around it counts as read, and the same kind of sign of that creature is not offered again for 6 game hours (no prompt spam in a grazing area) |
| 6 | DINO's new clips | NOT COMPLETED - WAITING | `PCPhase/DINO_CLIPS.md` does not exist. Existing states used: Eat, Drink, Rest (Idle_Variation), Look, Alert, Roar (also for Call / Threaten), Charge, Attack, Hurt, Death |
| 7 | PrimalWildlifeBuilder | DONE-NOT-TESTED (compiles; not run) | `Editor/PrimalWildlifeBuilder.cs` `Build(""|"no-art")`, `Inspect`. A scene the builder never ran on gets the same layout at runtime (`DinosaurSpawner.Awake`, clones), so the game plays the same before the bake |

## 2. Numbers (per species, `WildlifePlan.Species`; the builder writes them into DINO_*.asset, `wildlifeTuned`)

| species | night fear | aggr. | invest. | weight | sway / lean / tail deg | pivot | shake | print (shape, m, stride) | dung | scratch h |
|---|---|---|---|---|---|---|---|---|---|---|
| triceratops | 0.4 | 0.6 | 0.3 | 0.85 | 2.2 / 1.5 / 9 | 0.6 | 0.022 | ceratopsian 0.6, 2.2 | 0.5 | - |
| parasaurolophus | 0.7 | 0.1 | 0.45 | 0.62 | 1.6 / 2.6 / 14 | 0.7 | 0.012 | hadrosaur 0.5, 2.3 | 0.38 | - |
| ankylosaurus | 0.2 | 0.7 | 0.2 | 0.9 | 2.6 / 1 / 8 | 0.55 | 0.02 | ankylosaur 0.48, 1.6 | 0.42 | - |
| velociraptor | 0 | 0.75 | 0.9 | 0.08 | 0.6 / 7 / 24 | 1 | 0 | dromaeosaur 0.17, 1.2 | 0.1 | 0.9 |
| carnotaurus | 0.2 | 0.8 | 0.6 | 0.55 | 1.2 / 4.5 / 18 | 0.75 | 0.012 | theropod 0.52, 2.6 | 0.25 | 2.2 |
| spinosaurus | 0.3 | 0.6 | 0.5 | 0.82 | 1.9 / 2.5 / 12 | 0.6 | 0.02 | theropod 0.72, 3.0 | 0.35 | 2.6 |
| apex | 0.1 | 0.9 | 0.7 | 1 | 2.4 / 2 / 12 | 0.55 | 0.032 | theropod 0.95, 3.6 | 0.45 | 3.2 |

FireFear stays the wave-1 campfire profile (PrimalPerceptionBuilder). Herds: crested herd 9 (migrates), horned herd 5
(`herbivore_valley` + (12, -22)), armoured group 3 (deep forest edge towards the old camp). Territories: raptors 3 (home r 38),
carnotaurus 1 (40), spinosaurus 1 (32), Rift Tyrant 1 (30). 27 creatures in all (was 15). Global values: `Resources/WildlifeConfig`.

## 3. Files

New: `AI/{HerdGroup, MigrationDirector, WildlifePlan, WildlifeConfig, WildlifeArt, TrackSigns, TrackSignSpot, TrackArt, BirdFlush}.cs`,
`Editor/PrimalWildlifeBuilder.cs`, `Tests/PlayMode/WildlifeTests.cs` (4 targeted tests).
Changed: `AI/{DinosaurController, DinosaurDefinition (appended profiles, FireResponse appended), DinoLife, DinosaurSpawner, CoverMap}.cs`,
`Tests/PlayMode/PerceptionIslandTests.cs` (two expectations: a herd drinks together, so the drink test uses a loner; the Animator is
stepped by hand in the far tier), `Core/GameEvents.cs` (append only). Not touched: GameManager (STORY), WeatherManager (WORLD),
VfxPool / SfxPlayer (no new ids needed). Assets the builder makes: `Resources/WildlifeConfig.asset`, `Resources/WildlifeArt.asset`,
`Art/Wildlife/` (track atlas, bird sheet, 5 materials).

## 4. Requests

| To | Request |
|---|---|
| STORY | `MigrationStarted` fires wherever the player is: the line "The ground is shaking..." may want a distance check (event position = the herd, e.g. < 250 m). `TracksFound` amount 2 = predator sign, if mission 10 should need predator tracks only |
| DINO | `DINO_CLIPS.md` with state / parameter names (Look, Call, Stop, Turn, Defend, Charge, Chase, Bite, Recover, Flee); I will drive them from the controller then (Call / Threaten now use the Roar state) |
| Lead | deploy list below; check `Core/GameEvents.cs` on the PC still has md5 `02f60da6...` (base of my append) before extracting, else merge the four ids at the end |

## 5. Deploy / verification state

- Cloud compile `./cc.sh all` (20:09 UTC): runtime rc 0, editor rc 0, tests rc 0. `Tools/pf_up_P11.zip` md5 `37609416...`. PC copies of every file I change were identical to
  the mirror base (md5) at 19:1x UTC.
- Ordered deploy list for when the editor answers (one lock cycle, fresh ids):
  1. `$HOME/deploy.sh pf_up_P11` (18 files; expect no `error CS`).
  2. `$HOME/run.sh P_18 PrimalWildlifeBuilder.Build "" 15` (config, track art, herds, territories, route, habitats, re-snap; log ends
     with `checks: OK` and `scene saved`).
  3. `$HOME/run.sh P_19 PrimalEditorBridge.ConsoleCheck "" 5` (no errors / exceptions / missing references).
  4. Only if the owner wants proof before playing: `$HOME/run.sh P_20 PrimalTestRunner.RunPlayMode "WildlifeTests" 3`, then
     `"PerceptionIslandTests.Herbivores_Sleep_At_Night_And_Go_To_Water|PerceptionIslandTests.AI_Tiers_Near_Medium_Far_Very_Far"`.
- Phase 3 integration report placeholders filled (`Phase3/_ai_report.md`: P10 compiled on the PC, its tests not run).

## 6. Known limits

- Nothing ran in the editor: look and feel of the sway / tail, dust size from the lookout, the rumble, bird flocks and the decals are
  MANUAL TEST REQUIRED; `WildlifeTests` cover herd shape, the noon drink, the migration events and track examining.
- Herds and the migration anchor walk straight lines between places; members step round rocks and each other, not round cliffs.
- Dynamic signs are not saved (a load starts with the old landmarks and fresh tracks); herd state is rebuilt from the positions.
- Predators do not kill herd animals (they watch, test-rush and scatter them); carcasses come from the player's kills.
