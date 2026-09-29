# Resource system (Phase 3.5)

How gathering works in PRIMAL FRONTIER: what the island gives, how much, with which tool, how nodes look and come
back, and how the resource pass placed them. Everything below is data driven: tune the ScriptableObjects, re-run the
builder, never edit numbers in code.

## 1. One architecture

| Piece | File | Job |
|---|---|---|
| `ResourceDefinition` (ScriptableObject) | `World/ResourceDefinition.cs`, assets `Data/Resources/RES_*.asset` | one kind of node: id, name, category, size, prompt, item, yield range, best / required tool, hand rate, gather time, animations, respawn hours, looks, feedback |
| `GatherToolDefinition` (ScriptableObject) | `World/GatherToolDefinition.cs`, assets `Data/Resources/TOOL_*.asset` | a tool's gathering side: tool kinds, units per action per category, action speed, durability cost per action (the durability pool stays on the item's `maxDurability`) |
| `ResourceDatabase` | `World/ResourceDatabase.cs`, `Resources/ResourceDatabase.asset` | lists every definition and tool, bare hands, the unripe crop material; tool lookup by item; tools without a definition get numbers derived from their `ToolKind` + `toolPower` |
| `GatheringSystem` (static) | `World/GatheringSystem.cs` | the rules in one place: which tool counts, units per action, whole units from fractional work |
| `ResourceNode` (the only node class) | `World/ResourceNode.cs` | Interactable + `IDamageable`: prompt, gather action, bare-hand strikes, looks (full / damaged / depleted / regrowing). No `Update` |
| `ResourceManager` | `World/ResourceManager.cs` | one object runs every node: respawn timers, regrowing looks near the player, fruit regrowth, hit wobble, 16 m grid, the save section |
| `InteractionHighlight` | `World/InteractionHighlight.cs` | the subtle glow on the current target only |
| `TreeHarvest` | `World/TreeHarvest.cs` | terrain trees (TreeInstances): chop with an axe, twigs by hand, punches via a trigger proxy |
| `FruitCluster` | `World/FruitCluster.cs` | fruit high on a climbable tree (picked while climbing) |
| `PlayerFeedback` (resource part) | `Player/PlayerFeedback.cs` | pooled particles and sounds per type, inventory sound, hints, tool break |
| `ResourceSaveSection` | `World/ResourceSaveSection.cs` | save section `resources`: picked fruit and chop hits (nodes and felled trees stay in `SaveData`) |
| `PrimalResourceBuilder` (editor) | `Editor/PrimalResourceBuilder*.cs` | data assets, node prefabs, the island pass, captures, audit |

Carcasses (`World/Carcass.cs`, AI) and water (`WaterSource`, SURV) keep their own interactables; they use the same
`IInteractable` contract and the same gather animation event.

## 2. Resource types and yields

Units in a full node are rolled once per node (stable per save id) inside the range. Hands: units per action by hand
(the hands tool gives 1 on stone / wood, 1-2 on plants / food; `handsFactor` scales it).

| Id | Name (prompt) | Category / size | Units | By hand | Best tool | Respawn (game h) | Emptied look |
|---|---|---|---|---|---|---|---|
| stone_small | Small stones (Gather Stone) | Stone / Small | 2-4 | 1 per action | pick 3-5 | 30 | hidden with a puff |
| stone_medium | Stone (Gather Stone) | Stone / Medium | 4-8 | 1 per action | pick 3-5 | 40 | rubble (45 %, settles) |
| stone_large | Large rock (Gather Stone) | Stone / Large | 8-15 | 1 per 3 actions + hint | pick 3-5 | 48 | mined (82 %, settles) |
| stone_boulder | Boulder (Gather Stone) | Stone / Huge | 8-15 | 1 per 3 actions + hint | pick 3-5 | 60 | mined (stays) |
| wood_branch | Fallen branch (Gather Wood) | Wood / Small | 1-3 | 1 per action | axe 2-3 | 20 | hidden with a puff |
| wood_driftwood | Driftwood (Gather Wood) | Wood / Medium | 2-5 | 1 per action | axe 2-3 | 24 | hidden with a puff |
| wood_small_log | Small log (Gather Wood) | Wood / Medium | 4-8 | 1 per action | axe 2-3 | 36 | rubble |
| wood_deadwood | Fallen deadwood (Gather Wood) | Wood / Large | 5-8 | 1 per action | axe 2-3 | 48 | rubble |
| wood_tree | Tree (Chop Tree / Gather Wood) | Wood / Huge | 6 hits, + 3 on the fall | dry twigs: 1 per 4 actions, max 2 per tree, never fells | axe 2-3 per hit, hand stone 1-2 | 72 | felled (hidden), regrows |
| fiber_plant | Fibre plant (Gather Fiber) | Fiber / Small | 2-4 | 1-2 per action | knife 2-3 | 16 | hidden |
| fiber_grass | Long grass (Gather Fiber) | Fiber / Small | 1-3 | 1-2 | knife 2-3 | 12 | hidden |
| fiber_fern | Fern (Gather Fiber) | Fiber / Small | 1-3 | 1-2 | knife 2-3 | 16 | hidden |
| fiber_bush | Small bush (Gather Fiber) | Fiber / Small | 2-4 | 1-2 | knife 2-3 | 20 | stripped |
| food_berry_bush | Berry bush (Harvest) | Food / Medium | 4-6 berries | 1-2 | - | 20 | stripped, berries gone, green berries in the 2nd half |
| food_fallen_fruit | Fallen fruit (Harvest) | Food / Small | 1-3 fruit | 1-2 | - | 20 | hidden |
| food_edible_plant | Edible plant (Harvest) | Food / Small | 1-2 | 1-2 | knife | 18 | hidden (placed once SURV's `edible_plant` exists) |
| fish_shoal | Fish in the shallows (Catch Fish) | Fish | 2-3 | 1 per 3 actions | - | 16 | hidden (placed once SURV's `raw_fish` exists) |
| fruit cluster | on the 4 climbable trees (Pick the fruit) | Food | 3 | all that fits | - | 30 | fruit hidden, green fruit swell in the 2nd half |

Respawn is never instant (minimum 0.5 h, real values 12-72 game hours; 90 real seconds per game hour by default):
trees slow, stone slow, fibre moderate, fruit regrows. A hidden node that is due waits until the player is 18 m away or
looking elsewhere, so nothing pops back in in front of the player.

## 3. Tools and efficiency

| Tool (item) | Kinds | Efficiency (units per action) | Durability cost per action |
|---|---|---|---|
| Bare hands | none | stone 1, wood 1, fibre 1-2, food 1-2, fish 1 (x the node's `handsFactor`) | - |
| Stone pick | Mine | stone 3-5 | 1 |
| Stone axe | Chop | wood 2-3 (trees: fells in 6 hits) | 1 |
| Hand stone | Chop + Mine | stone 2, wood 1-2 | 1 |
| Stone hammer | Mine + Hammer | stone 2-3 | 1 |
| Flint knife / butcher knife | Cut | fibre 2-3, food 2-3 (meat and hide: `Carcass`, AI) | 0.5 |

Rules (`GatheringSystem.Plan`): a held tool counts when it has the node's `bestTool` kind and an efficiency row for the
node's category; anything else (empty hands, a spear, a log) gathers by hand. Nodes with a `requiredTool` (large rocks,
boulders, trees) can still be worked by hand at the low `handsFactor` rate and show the hint ("Too big to break by hand.
A pick breaks it." / "Bare hands only strip dry twigs. An axe fells the tree."). A whole-number range rolls inclusive
(pick 3-5 gives 3, 4 or 5); a fractional one carries over (0.34 per action = one stone every third action). A tool that
breaks in the gathering wear path raises SURV's `InventorySystem.ToolBroke`: the HUD shows "X broke!" and plays `ToolBreak`
and a small `DustImpact` (one place for every caller).

Progression: bare hands (small stones, branches, fibre, fruit, berries) -> stone axe / pick from those -> 3-5 stone and
2-3 wood per action, trees and large rocks open up.

## 4. Interaction

- Prompt only when close (the one interaction scan): first line `Gather Stone` / `Gather Wood` / `Gather Fiber` /
  `Harvest` / `Chop Tree`, second line the node name and a tool hint (`Large rock: Too big to break by hand...`,
  `Fallen branch: an axe is faster`). Empty rubble / stripped nodes show a greyed name with "Grows back later".
- The player must be at the node: reach = the node's footprint radius + 1.9 m; every hit re-checks it (walking away
  stops gathering). The body turns to the node while working (PlayerInteraction focus).
- One action per press; holding the key / GATHER button keeps gathering (`ResourceManager.HoldToRepeat`, directive 44).
  Moving, jumping or pressing again stops.
- Animations: bare-hand stone `PlayerActions.GatherStoneHand`, branches / wood by hand `GatherBranch`, pick `GatherStone`,
  axe `GatherWood`, plants and food `GatherPlant` (U's placeholders until the clips arrive). The gather happens on the
  clip's `OnGatherHit` event, with a timer fallback (`gatherSeconds / tool speed`).
- Mobile: the contextual button reads the prompt (`Gather ...`, `Chop` -> GATHER, `Harvest` -> HARVEST).

## 5. Bare-hand strikes

`ResourceNode` and `TreeHarvest` are `IDamageable`. U's punches (`HitInfo.unarmed = true`, 4 jab / 9 heavy damage)
give `damage / damagePerUnit x handsFactor` units (small stones: a jab = 1 stone, a heavy punch = 2; a large rock
needs three jabs for one). Weapons and tools do nothing to nodes by hitting: they gather with the interact key. Trees
take punches through a trigger capsule (Ignore Raycast layer) that follows the tree in front of the player: leaves, the
hint, a twig now and then, never a fall. Plants and branches have trigger colliders on Ignore Raycast so fists reach
them while the player walks through them.

## 6. Looks (full / damaged / depleted / regrowing)

No material instances, no per-node Update:
- full: the placed transform;
- damaged: shrinks towards `damagedScale`, settles `sink` x height into the ground, tilts up to 6 degrees;
- depleted: `Hide` (small piles, plants: renderers and colliders off after a bigger burst of fragments / chips / leaves,
  so nothing vanishes without a reason), `Rubble` (medium stones, logs: 40-45 % size, sunk), `Stripped` (bushes: berries
  gone), `Mined` (large rocks, boulders: settle a little);
- regrowing: in the second half of the respawn time visible nodes grow back and berries / fruit come back green
  (`M_Berry_Unripe`), ripe when the timer ends;
- every hit wobbles the node (ResourceManager animates only the struck nodes).

## 7. Feedback

Per hit: pooled particles by definition (stone: `StoneChips` + `DustImpact`; wood: `WoodChips` + `HitDust`, trees add
`Leaves`; plants and fruit: `Leaves`), sound by hand / tool (`StoneGatherHand` / `StoneHit`, `BranchSnap` / `WoodChop`,
`LeafRustle`), a soft `Pickup` inventory sound (at most every 0.15 s), and the HUD pickup feed "+3 Stone" (the HUD merges
gains of one item within 3 s into one line, at most 6 lines, fading after 5 s). The last unit plays a bigger burst
(`WoodBreak` / `StoneHit` / leaves). Events: `ResourceGathered` (every action, the AI hears it), `ResourceDepleted`,
`TreeFelled` (AI: a loud noise at the tree), `FruitHarvested`.

## 8. Readability

The current interaction target (resource node or pickup) gets a gentle brighter base colour (material property block,
+16 % pulsing 6 %) while its prompt shows; nothing is highlighted from afar and no icon floats over nodes. Nodes differ
from decoration by shape (loose stone piles, forked branches, straw-coloured long grass, bushes with berries) and by
placement (small clusters at beaches, forest edges, rock bases, water edges). Decoration that still exists: cliffs and
the cave (scenery), terrain detail grass / ferns / bushes (ground cover), walk-through bushes without berries.

## 9. Placement (PrimalResourceBuilder.Build)

Bridge: `run.sh <id> PrimalResourceBuilder.Build "" 6` (idempotent, fixed seed, logs to
`Documentation/Resources/resource_build.txt`, saves the scene). Never run `PrimalGameplayBuilder` (it deletes
`[Gameplay]` and rewrites item data); this builder touches no terrain and no item.
1. Data: definitions, tools, database, node prefabs `Prefabs/Resources/Nodes/Resource_*` (model child, collider,
   LODGroup cull at 1.2 % screen height for loose nodes, ResourceNode).
2. Conversion of what already looks gatherable: stone piles, driftwood, fibre plants, berry plants, berry bushes (plus a
   visible berry crop on the bush's shaking Visual), the medium rocks (large rock), the large rocks (boulder), the 136
   small decorative rocks (now Stone nodes) and the 14 fallen logs (deadwood).
3. `[Resources]` root: `StartArea` (loose stones a few steps from the spawn on their own, then stone, wood, fibre and
   food in five groups within 40 m), `TrailToWater` (a small cluster every ~22 m from the beach past the camp and the
   fibre meadow to the pond), `FruitTreeDrops` (fallen fruit under each fruit tree), `Island` (one small cluster per
   ~34 m cell, recipe by biome: beach driftwood / stones / fruit; meadow grass / fibre / berries; forest edge fibre /
   branches / stones / berries; forest ferns / branches / logs / stones; rocky stones; water edge reeds / stones /
   edible plants). Spots avoid water, steep slopes, tree trunks, every collider and the camp site.
Other commands: `Audit` (read-only island map), `BuildData`, `Capture "before" / "after"` (edit-mode renders to
`Documentation/Screenshots/Resources/`), `ProbeAssets`.

## 10. Performance

No node has an Update (checked by a test); `ResourceManager` runs once per frame (wobble of struck nodes) and twice a
second (respawns, regrowing looks within 80 m, fruit). Effects are pooled (`VfxPool`), sounds pooled (`SfxPlayer`),
strings for prompts built once per node. Loose nodes cull by LODGroup; small ones cast no shadow.

## 11. Save

Nodes: `SaveData.nodes` (remaining, empty-until) as before. Felled trees: `SaveData.felledTrees`. Section `resources`
(`ResourceSaveSection`, SURV's `ISaveSection`): fruit left / regrow time per cluster (stable ids `fruit_<tree>_<n>`)
and chop hits on standing trees. A missing or broken section leaves the reset state.

## 12. The island after the pass (2026-09-29)

1429 nodes (803 before, all with a definition): stone 112 small / 184 medium / 426 large / 254 boulders; wood 71
branches, 22 driftwood, 23 small logs, 14 deadwood + 1322 terrain trees; fibre 42 plants, 48 long grass, 52 ferns, 34
small bushes; food 97 berry bushes, 26 fallen fruit, 17 edible plants, 4 fruit trees (12 clusters); 7 fish shoals
(exact counts per run in `Documentation/Resources/resource_build.txt`). Within 40 m of the beach spawn: stone 6 nodes /
26 units, wood 8 / 22, fibre 6 / 17, food 6 / 21; the first stones lie 7 m from the spawn; a chain of clusters leads
137 m to the pond with no gap over 30 m. Captures: `Documentation/Screenshots/Resources/{before,after}_*.png`.
