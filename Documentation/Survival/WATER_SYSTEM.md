# WATER SYSTEM

## Water types (`WaterType`, saved as int, append only)
| Type | From | Drink (per charge) | Boil at a lit campfire |
|---|---|---|---|
| SaltWater | sea (fill at the shore) | -6 thirst (worse) | 20 s -> CleanWater, 1 charge lost (boils down) |
| DirtyWater | pond, stream | +25 thirst, 20 % sick 60 s | 8 s -> CleanWater |
| CleanWater | rain collector, boiled water | +30 thirst, +5 stamina | not needed |
Drinking straight from pond / stream by hand: +28 thirst with the dirty-water sickness roll. The sea by hand makes
thirst worse (the island teaches "boil it"). Boiling salt water is a game simplification, tunable in `SurvivalConfig.water`.

## Containers (data: `ItemDefinition.waterCharges`)
| Container | Capacity | Unlock |
|---|---|---|
| Leaf cup (new) | 1 | known at start (fibre 3) |
| Gourd (`water_container`) | 3 | wreck loot / recipe |
| Leather waterskin | 5 | hide + sinew cord |
Actions: **Fill** (at a source; only the same water type or an empty container), **Drink** (use), **Empty** (pour out,
inventory button), **Boil** (put the container on a campfire slot, take it back when done).

## Rain collector (new placeable)
Hide funnel over a wooden frame and basin. While `WeatherManager.RainingAt(position)` it gains
`collectorChargesPerRainHour` (2 per in-game hour at normal rain) up to `collectorCapacity` (6) of CleanWater.
Interact: fill the container in hand, or drink a charge with empty hands. Saved (ISaveableStructure).

## Rules live in one place
`Survival/WaterRules` (fill, empty, drink, boil, labels, colours). `WaterSource`, `OceanShore`, `RainCollector`,
`Campfire`, `PlayerInteraction`, HUD and inventory call it; no water numbers anywhere else.
