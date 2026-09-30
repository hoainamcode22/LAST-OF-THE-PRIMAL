# Phase 1 gap audit (AUDIT agent A_, read-only, 2026-09-30), Lead summary

Implemented already: stats (Health, Hunger, Thirst, Stamina, Temperature, Wetness, status effects bleeding / leg + arm
injury / sickness / cold / wet / heat), food stages Fresh / Aging / Spoiled + raw / cooked / burnt items, configurable
food poisoning, campfire (warmth, light, cook, boil with bubbles / steam / audio, rest, rain + storm extinguish,
species FireResponse), 13 core recipes, fruit tree climb -> harvest -> climb down (FruitCluster bug fixed), hurt /
blood / bleeding / bandage, inventory 32 slots + hotbar 1-8 + weight + durability, journal 6 categories, day phases,
weather Clear / Cloudy / Rain / Storm, waterfall chain (14.5 m drop, spring -> falls -> pool -> river -> ocean), all
listed shaders, 38 pooled VFX, ambience + player + dino audio, dino states incl. Rest / sleeping.

## Gaps (owner)
1. BUILD: no StructureDefinition / prefab data (Resources/Structures, Prefabs/Building missing): Foundation, Wall,
   Roof, Door not buildable; `BuildSystem.cs:89` `Start(params)` Console error (callers :77 :86 :407 :412 :413).
2. ENV: Vegetation, Volcano, Story, Wet passes not run into the saved scene: 23 ENV_PC_* models (tree ferns, cycads,
   vines, moss rocks, roots, basalt, giant ferns) unplaced; no lava on the island; no ferns / moss at the falls.
   Volcano landmark sits offshore at (-360, -2, 640). Story step would place `PROP_PC_Petroglyph` "carvings, someone
   made these" (conflicts with no-ancient-structures).
3. CHAR: bare-hand + survival clips are placeholders (punches use knife / sword clips; Gather_Branch,
   Gather_Stone_Hand -> Gather_Plant; Drink_Kneel, Collect_Water -> Crouch; Bandage_Use -> Use_Item; Unconscious ->
   Wake_Up); Kick, BareHand_Combo_End, Unarmed_Block states missing. Source: `scripts/pf_clips_c.py`.
4. U: MobileHUD live in the PC build (`GameManager.cs:97`, scene [UI] + [UI]/[Touch], Settings "Touch controls",
   `GameSettings.cs:21-23`, `ContextHints.cs:174,294`, `PlayerInputReader.cs:47-56,168-177`, `PrimalSceneBaker.cs:237`,
   `PrimalPhase2Builder.cs:306`, `HUDManager.cs:147`; tests use `MobileHUD.AttackLabel / ContextLabel`).
5. SURV: WaterType has no Cold / Hot; hot water does not warm; boiled salt water becomes clean (ocean drinkable);
   `ITEM_water_container` has no hand prefab; RecipeCategory lacks Fire and Storage.
6. RES: no Charcoal, no shipwreck material items (`wreck_scraps` referenced, missing); drops have colliders disabled
   and no Rigidbody (`WorldPickup.cs:85-105`); tool gating soft (hands 0.34 on stone, 0.25 on trees); 28 nodes in water
   after terrain v2; duplicate resource sets ([Resources] and World/Resources).
7. U / ENV: no ledge / rock-face Climbables placed (code ready).
8. DINO / AI: no dino Sleep / Rest clips in controllers (new FBX pending in `Tools/pf_up_D2.zip`); predators never
   hunt; wildlife ignores weather.
9. HIER: scene hierarchy differs from the owner's target layout.
