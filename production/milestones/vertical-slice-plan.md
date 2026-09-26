# Vertical Slice Plan — "Day Zero" (PRIMAL FRONTIER)

Owner: producer. Scope guard: creative-director (vision) + technical-director (tech) enforce brief s46/s52.
Each phase ends with a Blender save and/or Unity save and a git checkpoint commit.

## Milestones

| Milestone | Phases | Exit criteria |
|-----------|--------|---------------|
| M1 World | 1-3 | Island terrain in Unity, beach/forest/rocks/shipwreck placed, Blender kit exported, scene opens with no errors |
| M2 Survivor | 4-11 | Player walks the island in 3rd person, gathers, inventory, crafting, campfire, food/water, shelter |
| M3 Wildlife | 12-15 | 6 species with FSM + AI LOD, predator warnings, hunting/combat |
| M4 Day Zero | 16-21 | Journal, day/night, rain, tutorial + Chapter 1 beats, UI pass, save/load |
| M5 Ship it | 22-24 | Audio/VFX, performance pass on budget, full-loop QA, status report |

## Phase -> agent allocation

| # | Phase | Lead agent | Supporting agents | Main deliverables |
|---|-------|-----------|-------------------|-------------------|
| 1 | Project audit | technical-director | producer, unity-specialist | Tool inventory, Unity project, studio config |
| 2 | Blender world blockout | level-designer | world-builder, technical-artist | Heightfield, zones, habitats, scale refs |
| 3 | Terrain, beach, forest, shipwreck | art-director | technical-artist, level-designer, unity-shader-specialist | Texture sets, rock/vegetation kit, shipwreck, Unity terrain + placement |
| 4 | Player character | art-director | technical-artist, narrative-director (look), accessibility-specialist | PLAYER_Survivor mesh, rig, base animations |
| 5 | Movement + camera | gameplay-programmer | unity-specialist, ux-designer | PlayerController, PlayerCamera (Input System) |
| 6 | Resource gathering | gameplay-programmer | systems-designer, sound-designer, technical-artist | Interactable, ResourceNode, feedback VFX/SFX, pooling |
| 7 | Inventory | ui-programmer | unity-ui-specialist, systems-designer | InventorySystem, grid + hotbar UI |
| 8 | Crafting | systems-designer | economy-designer, gameplay-programmer, ui-programmer | ItemDefinition/RecipeDefinition SOs, 15-25 recipes, CraftingUI |
| 9 | Campfire | gameplay-programmer | technical-artist, sound-designer | Campfire prefab, light/cook/extinguish, fire VFX |
| 10 | Food / water | systems-designer | gameplay-programmer, economy-designer | PlayerSurvival, WaterSource, food items, cooking |
| 11 | Shelter | gameplay-programmer | level-designer | Shelter prefab, sleep/save/respawn |
| 12 | First dinosaur | art-director | technical-artist, ai-programmer | DINO_Triceratops mesh/rig/anims, DinosaurDefinition |
| 13 | Dinosaur AI | ai-programmer | game-designer, performance-analyst | FSM, senses, AI LOD, DinosaurSpawner/habitats |
| 14 | Predators | ai-programmer | art-director, sound-designer, game-designer | Raptor, Carnotaurus, Apex + warning signs |
| 15 | Hunting / combat | gameplay-programmer | game-designer, systems-designer | PlayerCombat, spear/throw/bow, damage, loot |
| 16 | Journal | ui-programmer | writer, unity-ui-specialist, art-director | JournalUI, discovery entries |
| 17 | Day / night | engine-programmer | unity-shader-specialist, technical-artist | TimeManager, lighting cycle |
| 18 | Rain | technical-artist | engine-programmer, sound-designer | WeatherManager, rain VFX, wet look |
| 19 | Tutorial / story | narrative-director | writer, level-designer, game-designer | Opening, 21-step tutorial, Chapter 1 beats |
| 20 | UI polish | unity-ui-specialist | ux-designer, art-director, accessibility-specialist | HUD, settings, notifications |
| 21 | Save system | lead-programmer | security-engineer (save integrity), qa-tester | SaveSystem.cs, versioned DTOs |
| 22 | Audio / VFX | audio-director | sound-designer, technical-artist | Ambience, SFX events, pooled VFX |
| 23 | Performance | performance-analyst | unity-specialist, technical-artist | LODs, culling, instancing, AI LOD budget |
| 24 | QA | qa-lead | qa-tester, release-manager | Full-loop test, bug list, status report |

## How to run a phase with the studio

1. `producer` opens a sprint: `/sprint-plan` (scope = one phase), `/scope-check` against brief s46/s52.
2. Lead agent drafts design/tech notes (`/quick-design` or `/design-system`), user approves.
3. Specialists implement (`/dev-story`), `lead-programmer` runs `/code-review` for C#.
4. `qa-tester` runs `/smoke-check`; untestable items are logged as NOT TESTED — REQUIRES MANUAL TEST.
5. Checkpoint: Blender save + Unity save + `git commit -m "checkpoint <phase>"`; update `Documentation/PRIMAL_FRONTIER_STATUS.md`.

## Status

| Phase | Status | Notes |
|-------|--------|-------|
| 1 | Done | See status report |
| 2 | Done | Blender blockout `PRIMAL_FRONTIER_WORLD.blend`, checkpoint cp01 |
| 3 | Done (first pass) | Kit + Unity terrain/placement built, 0 errors, screenshots in Documentation/Screenshots |
| 4-24 | Not started | |
