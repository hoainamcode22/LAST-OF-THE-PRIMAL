# Directory Structure (Unity project root = repo root)

```text
/
├── CLAUDE.md                         # Master configuration
├── .claude/                          # Agents, skills, hooks, rules, docs (Claude Code Game Studios)
├── Assets/
│   ├── _Project/
│   │   ├── Scripts/
│   │   │   ├── Core/                 # GameManager, GameStateManager, TimeManager, WeatherManager, SaveManager
│   │   │   ├── Player/               # PlayerController, PlayerCamera, PlayerInteraction, PlayerSurvival, PlayerInventory, PlayerCombat
│   │   │   ├── Gameplay/
│   │   │   │   ├── Inventory/        # InventorySystem
│   │   │   │   ├── Crafting/         # CraftingSystem, ItemDefinition, RecipeDefinition
│   │   │   │   └── World/            # Interactable, ResourceNode, WaterSource, Campfire, Shelter
│   │   │   ├── AI/                   # DinosaurDefinition, DinosaurController, DinosaurAI, DinosaurSpawner
│   │   │   ├── UI/                   # HUDManager, InventoryUI, CraftingUI, JournalUI, SettingsUI
│   │   │   └── Editor/               # Blender import pipeline + world builder (editor-only)
│   │   ├── Data/                     # ScriptableObjects: Items, Recipes, Dinosaurs, Weapons, Food, Loot
│   │   ├── Art/
│   │   │   ├── Models/               # FBX from Blender: Environment, Rocks, Shipwreck, Props, Resources, Dinosaurs, Player, Weapons
│   │   │   ├── Textures/             # Generated PBR sets (T_<Name>_D / _N / _M)
│   │   │   ├── Materials/            # URP Lit materials (M_<Name>)
│   │   │   └── Terrain/              # Heightmap (.r16), splat maps, TerrainLayers, TerrainData
│   │   ├── Prefabs/                  # Environment, Props, Resources, Dinosaurs, Player, Weapons, VFX
│   │   ├── Scenes/                   # Island_VerticalSlice.unity (+ Boot / MainMenu later)
│   │   ├── Audio/  VFX/  Shaders/  Settings/
│   │   └── Tests/                    # EditMode / PlayMode tests
│   └── _Prototypes/                  # Throwaway prototypes (isolated)
├── design/                           # Brief, GDDs, narrative, levels
├── docs/                             # Architecture, ADRs, engine reference
├── production/                       # Milestones, sprints, session state
├── Documentation/                    # PRIMAL_FRONTIER_STATUS.md (final/live report)
├── Packages/  ProjectSettings/       # Unity
└── (Library/ Temp/ Logs/ UserSettings/ are gitignored)
```

Blender source (outside the repo): `E:\Model game khủng long\` -> `PRIMAL_FRONTIER_WORLD.blend`, `scripts/`, `textures/`, `export/`, `checkpoints/`.
