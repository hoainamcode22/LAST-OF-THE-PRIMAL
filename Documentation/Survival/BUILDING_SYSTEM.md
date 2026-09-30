# BUILDING SYSTEM

## Milestone 1
Single placeables through the existing `BuildSystem` ghost (select item -> preview -> validate slope / water / overlap
-> place -> build hits). New: **tent** (prefab = `Shelter` cover + warmth 6 C + `Bedroll` sleep) and **rain collector**.
Both are `PlacedStructure`s (saved), extra state through `ISaveableStructure`.

## Later milestones (architecture kept ready)
Modular pieces (foundation, floor, wall, door, roof, support) will reuse the same ghost / validation / resource flow
with snapping sockets; stations define `StationType`, allowed recipes, interaction range and visual state. Base safety
comes from real walls, distance and light, never from a magic radius that blocks creatures.

## PC phase (BUILD agent, 2026-09-30): the piece set
Directive 27-29 replaced the "no modular building" rule with a small prefab set (Lead decision, `Phase3/WAVE2_OWNERSHIP.md`):
Log Foundation (3 x 3 m platform on stones), Woven Wall, Doorway Wall + Door, Thatch Roof (gable over one cell), Leaf Shelter
(lean-to, the cheap first shelter). Data: `StructureDefinition` assets in `Resources/Structures` (sockets, support rule, cost
through the recipe, footprint). Flow: B opens the piece menu (have / need), or a crafted piece bundle is placed from the hotbar;
one ghost flow in `BuildSystem` (snap to sockets, ground / slope / water / support / distance / overlap / materials checks, Build
action, BuildDust + wood / leaf sounds). Enclosure: a roof with walls on 2+ edges switches on a `Shelter` at floor level
(`BuildingEnclosure`), so rain, sun, temperature, campfire protection, rest / save and a bedroll inside all work through the
existing `Shelter` API. Pieces are `PlacedStructure`s (saved by item id, position, yaw; door state through `ISaveableStructure`).
Content: `PrimalBuildingBuilder`; art: `Tools/BlenderPipeline/build_pieces.py` -> `Art/Models/Building/BLD_*.fbx`, atlas
`Art/Building/Textures/T_Building_*`. Report: `PCPhase/_build_report.md`.
