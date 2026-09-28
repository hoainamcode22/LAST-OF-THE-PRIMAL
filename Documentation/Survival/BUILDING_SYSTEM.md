# BUILDING SYSTEM

## Milestone 1
Single placeables through the existing `BuildSystem` ghost (select item -> preview -> validate slope / water / overlap
-> place -> build hits). New: **tent** (prefab = `Shelter` cover + warmth 6 C + `Bedroll` sleep) and **rain collector**.
Both are `PlacedStructure`s (saved), extra state through `ISaveableStructure`.

## Later milestones (architecture kept ready)
Modular pieces (foundation, floor, wall, door, roof, support) will reuse the same ghost / validation / resource flow
with snapping sockets; stations define `StationType`, allowed recipes, interaction range and visual state. Base safety
comes from real walls, distance and light, never from a magic radius that blocks creatures.
