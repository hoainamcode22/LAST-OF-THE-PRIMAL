# PRIMAL FRONTIER - Architecture (Phase 2)

Unity 6000.3.10f1, URP, one scene: `Assets/_Project/Scenes/Island_VerticalSlice.unity`. PC first, phone ready (touch HUD,
quality presets). This page explains how the game is put together so it can be edited by hand without code.

## 1. Scene hierarchy (what is in the scene)

The game is the Hierarchy. Code looks for objects in the scene first and only creates what is missing, so anything moved,
re-coloured or re-tuned by hand and saved (Ctrl+S) is kept.

| Scene root | What it holds | Brief's name for the group |
|---|---|---|
| `[Systems]` | Time, Weather, Ambience, Journal, Tutorial, Intro, Build, OceanShore, Trees, Input, VfxPool, SfxPlayer, BloodDecals, EventSystem | Managers / WorldSystems / VFX / Audio |
| `[Gameplay]` | CaptainsLog, pickups, Cave, GiantFootprints, `[Zones]`, `[Game]` (GameManager), `[Dinosaurs]` (placed prefab instances), `FruitTrees` | Gameplay |
| `Player` | the survivor prefab instance (`Model` child with the Animator) | Gameplay / Player |
| `Main Camera` | third-person camera rig (`ThirdPersonCamera`) | Camera |
| `[UI]` | `[HUD]` (vitals, hotbar, compass, **Minimap**), `[HUD Top]`, `[Touch]` (phone controls), `[Inventory]`, `[Journal]`, `[Pause]`, `[Title]`, `[Death]`, `[DamageOverlay]` | UI |
| `World` | Rocks, Cliffs, Shipwreck, Props, Resources, Vegetation, **Landmarks** (Volcano) | World / Environment |
| `Markers` | Zones, Habitats, ResourceAreas, Water (named empties used by the tutorial, minimap and spawners) | Gameplay data |
| `ENV_Island_Terrain`, `Water` (ocean, pond, stream) | terrain and water surfaces | World / Environment |
| `Sun`, `Global Volume` | directional light driven by TimeManager, post-processing volume | Lighting |

The brief suggested regrouping into World/Environment, Gameplay, WorldSystems, Lighting, VFX, Audio, Managers. The roots
were **not renamed**: the UI and several systems find their pieces by name (`GameObject.Find("[UI]")`, `[HUD]/Hotbar`,
`World`, `Markers/...`), and renaming would break hand edits and saves. The table above is the mapping.

## 2. Folders

```
Assets/_Project/
  Scenes/                 Island_VerticalSlice.unity
  Scripts/                runtime code (namespace PrimalFrontier.*)
    AI/                   DinosaurController (FSM), DinoLife (head look, attack tell, eyes), AmbientCreature, spawner
    Animation/            AnimParams (hashes, PlayerActions), CharacterAnimationEvents, HitZone
    Audio/                SfxPlayer (pooled), SfxLibrary
    Building/             BuildSystem (ghost placement), PlacedStructure
    Combat/               IDamageable, Projectile
    Core/                 GameManager (game state), TimeManager, WeatherManager, SaveSystem/SaveData, GameSettings,
                          GameEvents, GameClock, AmbienceManager, TerrainQuality
    Items/                ItemDefinition, ItemDatabase, InventorySystem, CraftingSystem, RecipeDefinition, enums
    Player/               PlayerMotor, PlayerAnimationDriver, PlayerIK, PlayerCombat, PlayerClimb, PlayerInteraction,
                          PlayerInputReader (+ Virtual touch input), PlayerState, PlayerWetLook, ThirdPersonCamera, ...
    Story/                JournalSystem, TutorialManager, IntroSequence
    Survival/             PlayerSurvival (hunger, thirst, stamina, temperature, wetness, sickness)
    UI/                   UIFactory (find-or-create), UIManager, HUDManager, Minimap, MobileHUD, InventoryUI, ...
    VFX/                  VfxPool / VfxLibrary, BloodDecals, CampfireFx
    World/                Interactable (IInteractable), Climbable, FruitCluster, WaterSource, Campfire, ResourceNode,
                          TreeHarvest, Shelter, Bedroll, StorageBox, VolcanoLandmark, ZoneManager, ...
    Editor/               builders and tools (see 7)
  Shaders/                PF_Water, PF_LandmarkLit, PF_ParticlesAdditiveNoFog, PF_Foliage (not assigned), PF_Wind.hlsl
  Art/ Data/ Prefabs/ Resources/ VFX/   assets
  Tests/PlayMode/         automated tests
Assets/Art/Characters/    player + dinosaur FBX, controllers, prefabs (character pipeline output)
Tools/BlenderPipeline/    Blender scripts that make the original models (player, dinosaurs, fruit tree, volcano)
Documentation/            docs, test reports, screenshots
```

## 3. State

**Game state** (`Core/GameManager`, the brief's GameStateManager): `GameState { Boot, Title, Intro, Playing, Dead, Sleeping }`.
GameManager ensures the systems exist, starts a new game / loads, handles death and respawn, and refuses to save while
the player is climbing.

**Player state** (`Player/PlayerState`, new): read-only `PlayerMode { Explore, Combat, Aiming, Climbing, Building, Busy,
Sleeping, Dead }`, worked out every frame from the systems that own each situation (health, climbing, build mode, combat,
the animation driver, the game state), so it can never disagree with them. `Changed(old, new)` event for camera, HUD,
music or AI. Priority: Dead > Sleeping > Climbing > Building > Aiming > Busy > Combat > Explore.

**UI state** (`UI/UIManager`): `UIScreen` (None, Inventory, Journal, Pause, Title, Death). Opening a menu blocks gameplay
input.

## 4. Interaction

`World/Interactable.cs` defines `IInteractable { GetPrompt, CanInteract, Interact }`; the abstract `Interactable`
MonoBehaviour implements it (focus point, radius, range, save id). Everything the player can use derives from it:
resource nodes, trees, water, campfire, shelter, bedroll, storage, loot, examinables, climbable trees. `PlayerInteraction`
picks the best target in front of the camera, shows the prompt, runs actions on animation events (`DoLoop`), and has
`Suspended` / `ExternalPrompt` for states that take over (building, climbing).

## 5. Input

`Player/PlayerInputReader` is the only place that reads devices (Input System, created in code): keyboard / mouse,
gamepad, test simulation (`Sim`), and **touch** through `PlayerInputReader.Virtual` which `UI/MobileHUD` writes every
frame (stick, look drag, buttons, hotbar taps). Gameplay code only reads the reader's properties (`Move`, `Look`,
`JumpPressed`, `DodgePressed`, ...), never a device.

## 6. Events, save, settings

- `Core/GameEvents`: one static event bus (`GameEventType` enum, append-only). Phase 2 added PlayerDodged, ClimbStarted,
  FruitHarvested, WaterBoiled, GotSick, VolcanoRumble. Journal pages and the tutorial listen to it.
- `Core/SaveSystem` + `SaveData`: JSON, atomic write, **version 2** (`CurrentVersion = 2`: slots and drops carry the
  `dirty` water flag; older saves load with clean water). Weather saves by name (the new Cloudy state loads fine).
- `Core/GameSettings`: PlayerPrefs (volumes, sensitivity, quality preset, blood level, touch controls on/off/auto).

## 7. Editor tools (menus under `Primal Frontier`, and the editor bridge)

| Tool | What it does |
|---|---|
| `PrimalSceneBaker.Bake` | puts every system, the player, dinosaurs and all UI into the scene (adds only what is missing) |
| `PrimalCharacterBuilder.BuildAndTest(id)` | imports a character FBX, builds controller / prefab / LODs and runs the character test (report in `Documentation/CharacterTests`) |
| `PrimalPhase2Builder.Build` | fruit item, flint knife as a weapon, fruit tree + bunch prefabs, four fruit trees, journal pages, minimap bake, HUD layout |
| `PrimalCreatureEyes.Build` | readable eyeballs for 8 species (see the character pipeline doc) |
| `PrimalVolcanoBuilder.Build` | volcano landmark prefab + placement |
| `PrimalShaderBuilder.Water / Foliage / Restore` | switches materials to the PF shaders after a compile check, with backups |
| `PrimalReviewCapture.Shots / LookFrom / DinoFaces / PrefabFaces` | review screenshots without touching the scene |
| `PrimalEditorBridge` | runs any of the commands above in the open editor from `Library/PrimalBridge/command.json` (only when idle, never in Play mode) |

Generators that rebuild content from scratch live under `Primal Frontier > Advanced` and ask before overwriting hand edits.
