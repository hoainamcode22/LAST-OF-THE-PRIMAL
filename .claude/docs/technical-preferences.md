# Technical Preferences

<!-- Configured 2026-09-26 for PRIMAL FRONTIER. All agents reference this file. -->

## Engine & Language

- **Engine**: Unity 6000.3.10f1 (Unity 6.3 LTS), installed at `E:\game\6000.3.10f1`
- **Language**: C# (Unity 6 API, nullable disabled, no `dynamic`)
- **Rendering**: URP 17.3, `Assets/Settings/PC_RPAsset` (Forward+), linear colour space, SRP Batcher on
- **Physics**: PhysX (built-in 3D physics), NavMesh via com.unity.ai.navigation

## Input & Platform

- **Target Platforms**: PC (Windows) only for the vertical slice
- **Input Methods**: Keyboard/Mouse primary, Gamepad later (not in slice)
- **Primary Input**: Keyboard/Mouse
- **Gamepad Support**: None in vertical slice (design input actions so it can be added)
- **Touch Support**: None
- **Platform Notes**: TAB inventory, J journal, 1-8 hotbar, third-person camera

## Naming Conventions

- **Classes**: PascalCase (`PlayerSurvival`, `DinosaurAI`)
- **Variables**: camelCase fields, `_camelCase` for private serialized backing fields is NOT used; use `[SerializeField] private float moveSpeed;`
- **Signals/Events**: C# `event Action<T>` named `OnSomethingHappened`; ScriptableObject event channels for cross-system
- **Files**: one public type per file, file name = type name
- **Scenes/Prefabs**: PascalCase scenes (`Island_VerticalSlice`); prefabs/models keep Blender names:
  `ENV_`, `PROP_`, `RES_`, `DINO_`, `PLAYER_`, `WEAPON_`, `VFX_` prefixes (e.g. `ENV_Rock_Large_01`, `PROP_Ship_Hull`)
- **Materials/Textures**: `M_<Name>`, `T_<Name>_D` (albedo), `_N` (normal, OpenGL), `_M` (R metal, G AO, B height, A smoothness)
- **Constants**: PascalCase `const` / `static readonly`

## Performance Budgets

- **Target Framerate**: 60 fps at 1080p High on a GTX 1660 / RTX 2060 class GPU
- **Frame Budget**: 16.6 ms total; gameplay scripts <= 2 ms; dinosaur AI <= 2 ms (all animals)
- **Draw Calls**: <= 1500 SetPass/batches in the densest view (SRP Batcher + GPU instancing for vegetation)
- **Triangles**: <= 2.5 M visible; hero dinosaur LOD0 <= 25k tris, player <= 20k, tree LOD0 <= 4k, rock L <= 2.5k
- **Textures**: 1024 default, 2048 max for hero atlases, 512 for small props; no 4K
- **Memory Ceiling**: 4 GB RAM, 3 GB VRAM
- **AI LOD**: near full FSM (< 60 m), medium simplified (60-150 m), far tick-only (150-250 m), beyond: disabled/pooled

## Testing

- **Framework**: Unity Test Framework 1.6 (EditMode for systems/data, PlayMode for loop smoke tests)
- **Minimum Coverage**: all survival formulas, inventory stacking, crafting consumption, save/load round-trip
- **Required Tests**: Balance formulas, gameplay systems, save versioning. Anything not automatable is reported as
  "NOT TESTED — REQUIRES MANUAL TEST" in `Documentation/PRIMAL_FRONTIER_STATUS.md`

## Forbidden Patterns

- Any ARK asset, name, UI, icon, map, text, creature copy or progression copy
- `GameObject.Find*` / `FindObjectOfType` in Update loops; `SendMessage`
- Per-frame allocations in Update (LINQ, string concat, `new` lists) in hot paths
- Saving the scene hierarchy; save files must be compact, versioned DTOs
- Legacy `UnityEngine.Input` (use the Input System)
- Scope creep beyond the brief's Content Limit (section 46) and Scope Rule (section 52)

## Allowed Libraries / Addons

- Unity packages already in `Packages/manifest.json` (URP, Input System, AI Navigation, Timeline, Test Framework, UGUI)
- TextMeshPro (bundled with UGUI 2.0)
- No third-party asset store packages without explicit approval

## Architecture Decisions Log

- ADR-001 (2026-09-26): Unity Terrain built from Blender-generated 16-bit heightmap + splat maps (not a mesh terrain),
  vegetation via terrain tree/detail instancing, props as prefabs placed from `placement.json`. See `docs/architecture/`.
- ADR-002 (2026-09-26): Blender -> Unity axis mapping: FBX (-Z forward, Y up, apply transform). Unity position =
  (-Bx, Bz, -By); rotation R_u = M R_b M^T with M = [[-1,0,0],[0,0,1],[0,-1,0]]. Implemented once in `BlenderSpace.cs`.

## Engine Specialists

- **Primary**: unity-specialist
- **Language/Code Specialist**: unity-specialist (C#), lead-programmer for architecture review
- **Shader Specialist**: unity-shader-specialist (URP Shader Graph, terrain, water, foliage)
- **UI Specialist**: unity-ui-specialist (UGUI for HUD/inventory/journal)
- **Additional Specialists**: unity-addressables-specialist (only if memory budget requires), technical-artist (Blender pipeline)
- **Routing Notes**: DOTS is out of scope for the slice; MonoBehaviour + ScriptableObjects only

### File Extension Routing

| File Extension / Type | Specialist to Spawn |
|-----------------------|---------------------|
| Game code (.cs) | unity-specialist (gameplay-programmer / ai-programmer for domain logic) |
| Shader / material files (.shadergraph, .shader, .mat) | unity-shader-specialist |
| UI / screen files (UI prefabs, .uxml/.uss) | unity-ui-specialist |
| Scene / prefab / level files (.unity, .prefab) | level-designer + unity-specialist |
| Blender pipeline (.py in Model folder, .fbx) | technical-artist |
| General architecture review | unity-specialist |
