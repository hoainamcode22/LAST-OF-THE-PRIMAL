# CRAFTING PROGRESSION

## Milestone 1 set (small on purpose)
| Tier | Recipe | Cost | Unlock |
|---|---|---|---|
| 0 | Leaf cup | fibre 3 | start |
| 1 | Stone axe | stone 2 (unchanged, pinned by a test) | start |
| 1 | Flint knife / butcher knife | existing | finding bone / sharpened flint |
| 1 | Campfire | wood 4, stone 5 | start |
| 1 | Torch | existing | start |
| 1 | Rain collector | wood 4, fibre 6, hide 1 | first rain or first container |
| 1 | Tent | wood 6, fibre 8, hide 2 | building the first campfire |
| 1 | Storage | existing | existing |
Recipes stay ScriptableObjects (`RecipeDefinition`) added by the additive `PrimalSurvivalBuilder`; never by re-running
`PrimalGameplayBuilder`. Total recipes stay within the test cap (40).

## Unlock rules (existing `CraftingSystem` learning + requirements)
Find fibre -> rope; find flint / bone -> knife; build a campfire -> tent; first rain -> rain collector; collect hide ->
waterskin and better storage. Stations: campfire (cooking, boiling, fruit mash) now; workbench / drying rack later.
