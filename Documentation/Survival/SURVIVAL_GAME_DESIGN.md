# SURVIVAL GAME DESIGN (Phase 1)

## Fantasy
Stranded on a prehistoric volcanic island with almost nothing. The island is learned, not explained: every need is solved
by touching the world (chop, gather, butcher, light, boil, build), and every solution opens the next need.

## Reference, not a copy
The owner showed a survival-crafting game as a **gameplay-loop reference** (harvest -> unlock recipes -> craft -> hunt ->
cook -> build). Nothing is taken from it: no names, UI layout, assets, creatures or signature mechanics. PRIMAL FRONTIER
keeps its own identity: realistic dinosaurs, volcanic island, flint / bone / hide technology, a quiet shipwreck survivor.

## Pillars
1. **Every system feeds another** (no isolated feature). 2. **Physical interaction over menus** (the craft menu only
turns materials into tools; cooking, boiling, fuelling, filling and sleeping happen at world objects). 3. **Pressure,
not accounting**: needs fall slowly and their effects grow in steps. 4. **Night changes behaviour**, it does not stop play.
5. **Small but complete** before big: one of each (fire, container, collector, tent, axe, knife, torch, storage, one
huntable species) working end to end beats many half systems.

## The dependency chain (why things exist)
Hunt a dinosaur -> meat -> needs fire -> needs wood -> needs an axe -> needs stone + wood -> go out for stone -> far
from water you get thirsty -> need a container -> sea water is salty -> boil it -> boiling needs fire -> night falls ->
need a torch / light -> need a safe place to sleep -> build a tent. Every link is real game logic.

## Systems map (reuse first; see PROJECT_SURVIVAL_AUDIT section 3)
| Need | System (class) | Feeds |
|---|---|---|
| Food | ItemDefinition food fields, `PlayerSurvival.Consume`, `Campfire` cooking slots | hunger -> stamina / health |
| Water | `WaterRules` + `WaterType`, `WaterSource`, `OceanShore`, `RainCollector`, `Campfire` boiling | thirst -> stamina / health |
| Fire | `Campfire` (fuel from item data), torch (`PlayerEquipment`) | cooking, boiling, warmth, light, safety |
| Shelter | `Shelter` + `Bedroll` (tent prefab), `BuildSystem` placement | sleep, save, warmth, rain cover |
| Tools | `CraftingSystem`, `ItemDefinition.tool`, `ResourceNode` / `TreeHarvest` tool rules | gathering tiers, butchering |
| Hunting | `DinosaurController`, `AmbientCreature`, `Carcass` | meat, hide, bone -> food, containers, tent |
| Time / weather | `TimeManager`, `WeatherManager`, `SurvivalEnvironment` | night cold, rain water, fire burn rate |
| Numbers | `SurvivalConfig` (ScriptableObject) | every tuning value, one place |
| Progress | `SaveSystem` v4 (+ `ISaveableStructure`) | everything above |
