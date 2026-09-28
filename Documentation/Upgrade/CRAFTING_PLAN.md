# CRAFTING PLAN

Owner roles: `systems-designer` (recipes, numbers), `gameplay-programmer` (code).

## Keep
`CraftingSystem` (Check -> Enqueue -> queue of 5 -> station -> hands free -> result), `RecipeDefinition` assets in
`Data/Recipes`, `ItemDatabase`, InventoryUI tiles built from data (no recipe in UI code), recipe learning on first pickup.

## Add (data, append-only)
| Type | What |
|---|---|
| `RecipeCategory` | append `Resources` (intermediate materials); UI tab RESOURCES added to the category list |
| `CraftingRequirement` (serializable) | extra conditions: `tool` (ToolKind needed in the pack, e.g. Cut for arrows, Hammer for axes), `nearStation`, `minDay` (unlock later) |
| `CraftingResult` (serializable) | extra outputs beyond the main one (e.g. butchering scraps), optional |
| `RecipeDefinition` fields | `CraftingRequirement[] requirements`, `CraftingResult[] extraResults` (both optional, empty = old behaviour) |
| Save | the crafting queue is saved and restored (ingredients are no longer lost) |

## New recipes (first set)
| Recipe | Category | Ingredients | Requirement |
|---|---|---|---|
| sharpened_flint (Resources) | Resources | stone 2 | - |
| sinew_cord (Resources) | Resources | hide 1 | Cut tool |
| flint_sword | Weapons | wood 3, sharpened_flint 3, rope 2 | Hammer or Cut tool |
| bone_arrows (x5) | Weapons | wood 2, bone 1, fiber 1 | Cut tool |
| hunting_bow | Weapons | wood 4, sinew_cord 2 | Cut tool |
| butcher_knife | Tools | sharpened_flint 1, bone 1, rope 1 | - |
| leather_waterskin | Water | hide 2, sinew_cord 1 | Cut tool |
| fruit_mash (Food, campfire) | Food | fruit 2, berries 3 | Campfire |
| bandage | Survival | fiber 4, hide 1 | - |
The legacy recipe for cooked meat stays; the 10 s / 14 s mismatch is resolved by using the campfire time for both.
New items are added by a new additive builder (never by re-running `PrimalGameplayBuilder`, which overwrites the list).

## Acceptance
Recipes appear in the right tabs, missing requirements show a reason, queue survives save / load, compile clean.
