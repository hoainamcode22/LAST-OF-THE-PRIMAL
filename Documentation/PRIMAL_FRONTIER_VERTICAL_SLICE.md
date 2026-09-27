# PRIMAL FRONTIER - Vertical Slice

Original single-player third-person prehistoric survival game. Unity 6000.3.10f1 (URP), PC first, phone settings prepared.
Scope: one small island, first night. No multiplayer. Every asset in this slice is original (see `IP_AND_ASSET_PROVENANCE.md`).

## How to play (PC)

1. Open the project folder `E:\LAST OF THE PRIMAL` in Unity 6000.3.10f1. The editor opens `Assets/_Project/Scenes/Island_VerticalSlice.unity`
   by itself when it starts on an empty or template scene (menu `Primal Frontier > Scene > Open Island Scene` also does it).
   First time: `Primal Frontier > Scene > Bake Everything Into Scene` (or "Bake now" in the startup prompt), then press Play.
   A Windows player can also be built with `Primal Frontier > Build > Windows` (output `Builds/Windows/PrimalFrontier.exe`).
2. Title screen > New Game > intro (skippable with Space / Esc) > tutorial.

| Action | Key |
|---|---|
| Move / run / walk | WASD / Shift / Alt |
| Look / zoom | Mouse / + and - |
| Jump / crouch | Space / C |
| Interact / hold to gather | E / hold E |
| Attack / aim (spear throw, bow) | Left mouse / right mouse |
| Hotbar | 1-8 or mouse wheel |
| Inventory / crafting / journal / pause | Tab or I / Q / J / Esc |
| Drop / rotate building piece | G / R |

Gamepad bindings exist for movement, camera, jump, crouch, interact, attack, aim, dodge and pause (NOT TESTED with a real gamepad). Phase 2 added dodge (V), the big map (M), climbing (W / S / E / Space on a fruit tree) and phone touch controls (see `PRIMAL_FRONTIER_MOBILE_GUIDE.md`, NOT TESTED on a device). Phase 2 overview: `PRIMAL_FRONTIER_SYSTEMS.md`.

## What is in the slice

### Player
- Original survivor model, humanoid rig, 38 animation clips, face blendshapes (blink, pain, effort, jaw).
- Third-person controller: walk / run / crouch / jump / slopes / 0.2 m steps, camera collision, turn in place.
- Actions driven by animation events: gather wood / stone / plants, pick up, eat, drink, craft, build, sleep, wake up.
- Combat: spear (tap, heavy hold, throw), bow (draw / release), hurt / heavy hurt / death / respawn.
- Feedback VFX + SFX: hit blood, bleeding, eat / drink / gather / craft particles, pooled (no instantiation after warm-up).

### World
- Small island: beach with the shipwreck, forest, stream and pond (fresh water), cave, 13 named zones.
- Resource nodes (wood 10, stone 12, fibre 14, berries 9, 680 rocks), 8 loot containers, captain's log, footprints.
- Day / night with sun, sky, fog and air temperature; clear / rain / storm weather; ambience per zone.

### Story and tutorial
- Intro sequence (storm, wreck, waking on the beach), journal with story entries and creature pages.
- 19-step tutorial: look, walk to the wreck, search it, wood, stone, crafting menu, stone axe, fibre, fresh water, food,
  campfire, cook meat, stone spear, explore, examine tracks, observe the herbivore, return to camp, shelter, survive the night.
  Compass marker to the current objective.

### Survival loop
- 23 items, 16 recipes, inventory with weight (45 kg) and 8-slot hotbar, crafting queue (up to 5, needs free hands).
- Hunger, thirst, stamina, body temperature, wetness, sickness; sea water makes thirst worse, stream / pond water is fresh.
- Campfire (light, cook, warmth), shelter, bedroll (sleep / skip night), storage box, building placement.
- Save / load (JSON, atomic write, version check).

### Dinosaurs
| Species | Role on the island | Temperament |
|---|---|---|
| Triceratops | herd near the meadow | defensive |
| Parasaurolophus | herd, calls | passive (flees) |
| Ankylosaurus | slow, armoured | defensive |
| Velociraptor | fast hunter | predator |
| Carnotaurus | inland territory | territorial |
| Spinosaurus | at the stream | territorial |
| Rift Tyrant | apex predator (original tyrannosaur-type) | territorial |
| Pteranodon | circles the wreck | ambient flyer |
| Mosasaurus | offshore | ambient swimmer |

- Each: original procedural mesh, generic skeleton, 7-16 in-place clips, 3 LODs (Rift Tyrant: 4), colliders + head hit zone,
  synthesized voices (call, roar, hurt, death), loot on death (meat, hide, bone).
- AI: idle / wander / eat / drink / rest / observe / alert / investigate / flee / chase / attack / return / dead; sight and
  hearing (crouching and night reduce detection); herds flee together; AI level of detail at 90 m / 200 m.
- **Rift Tyrant hero model**: lofted skull and mandible with a real mouth cavity, 58 individually modelled serrated teeth,
  gums, tongue (3 bones), eyeballs with eyelids (blink, squint, closed in death), curved claws, 46-bone skeleton,
  59.8k / 27.8k / 9.1k / 3.0k triangle LODs, 4096 skin atlas (normals baked from the high-resolution sculpt), mouth and eye textures.

### Blood (setting: Off / Reduced / Normal)
- Hit: directional spray sized to the creature, droplets on the ground, a bleeding wound attached to the nearest bone.
- Badly hurt creatures leave a blood trail; a blood pool spreads under a dead creature.
- Off: neutral dust impacts instead, no ground blood. Pooled decals with hard caps (48 on Normal, 16 on Reduced).

## Editing the game by hand
Everything is a scene object in `Island_VerticalSlice`: `[Systems]` (time, weather, ambience, journal, tutorial texts, VFX /
sound / blood pools, input, event system), `[Gameplay]` (game manager, zones, every dinosaur as a placed prefab instance),
`Player`, `Main Camera` (third-person rig), `[UI]` (all canvases: HUD, inventory, journal, pause, title, death), `World`,
`Markers`, `Water`, terrain and lights. Code reads what is in the scene and only creates what is missing. The UI screens
find their pieces by name, so layout / colours / fonts / fixed texts edited by hand are kept. Placed dinosaurs are put
back where they stand on a new game. Generators that rebuild content live under `Primal Frontier > Advanced (overwrites
hand edits)` and ask before running. Maintenance guide (Vietnamese): `Documentation/HUONG_DAN_BAO_TRI.md`.

## Technical notes
- Code: `Assets/_Project/Scripts` (runtime), `Scripts/Editor` (builders), `Tests/PlayMode`.
- Rebuild steps (batch): `PrimalGameplayBuilder`, `PrimalVfxBuilder`, `PrimalAudioBuilder`, `PrimalCharacterBuilder.BuildAllDinosFromCommandLine`,
  `PrimalOptimizer.RunFromCommandLine`, `PrimalBuild.WindowsFromCommandLine`.
- Blender pipeline sources: `Tools/BlenderPipeline` (player) and `Tools/BlenderPipeline/dino` (dinosaurs, hero pipeline).
- Performance settings: `Documentation/Performance_Optimization.md`; measured numbers: `Documentation/Performance_Island.md`.

## Test status
See the final report (`Documentation/FINAL_REPORT.md`) for PASS / FAIL / NOT TESTED per area.
