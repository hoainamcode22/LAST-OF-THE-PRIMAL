# Phase 3 / 3.5, wave 1: agents and file ownership (2026-09-29)

Read `Documentation/AGENT_PROTOCOL.md` first (bridge lock, deploy zips, fresh run ids, no git, no deletes).
An agent edits only its own files. Need a change elsewhere: write it in your report as a request (the Lead routes it).
`Core/GameEvents.cs` is shared append-only: re-read it right before editing, append new values / events at the end only.
New sound ids (`SfxId`: PunchWhoosh, PunchHit, PunchHeavyHit, PlayerGrunt, WaterFill, WaterBoil, BandageWrap, ToolBreak,
FireHiss, BranchSnap, StoneGatherHand) and effect ids (`VfxId`: PunchImpactSmall, PunchImpactHeavy, DustImpact, Heal,
BoilBubbles) are already registered by the Lead; any agent may call them. Until the combat agent generates the clips /
prefabs they play nothing (safe).

| Agent | Prefix | Owns |
|---|---|---|
| CHAR (Blender) | C_ | Blender files, `E:\Model game khủng long\scripts\*`, renders, staging export `E:\Model game khủng long\export\staging\` (no Unity use) |
| COMBAT + ANIM (Unity) | U_ | Player/PlayerCombat, Combat/*, Player/PlayerInputReader, PlayerAnimationDriver, PlayerMotor, PlayerIK, PlayerInteraction, PlayerState, Animation/*, Editor/PrimalCharacterBuilder*, Editor/PrimalAnimAudit, Tests/PlayMode/CharacterMotionProbe, UI/ControlsPanel, UI/ContextHints, UI touch-control files, Audio/*, Editor/PrimalAudioBuilder, Tools/Audio/*, VFX/VfxPool, Editor/PrimalVfxBuilder, player FBX import + PlayerAnimator.controller |
| SURV (Unity) | S_ | Survival/*, Player/PlayerHealth, Items/*, World/Campfire, VFX/CampfireFx, World/WaterSource, World/OceanShore, World/RainCollector, World/Shelter, World/Bedroll, UI/HUDManager, UI/InventoryUI, UI/SlotView, crafting UI, Core/SaveData, Core/SaveSystem, Editor/PrimalSurvivalBuilder, survival tests |
| RES (Unity) | R_ | World/ResourceNode, World/TreeHarvest, World/FruitCluster, World/WorldPickup, Player/PlayerFeedback, new resource data / manager files, Editor/PrimalResourceBuilder (new), resource placement in the scene, resource tests |
| AI (Unity) | P_ | AI/*, Core/Stimuli (new), Player/PlayerSignature (new), Core/WeatherManager, Core/GameManager (add / reset components only), World/BushInteraction, World/Carcass, perception HUD indicator (new file), UI/SettingsPanel, Core/GameSettings, Editor/PrimalPerceptionBuilder (new), AI tests |

Wave 2 (after wave 1): BUILD (building pieces, snapping, validation, shelter purpose), WORLD (weather, day phases, wet
surfaces, volcano danger, minimap / journal discovery, tutorial rework), MOBILE / UI, test scenes, QA end-to-end.
