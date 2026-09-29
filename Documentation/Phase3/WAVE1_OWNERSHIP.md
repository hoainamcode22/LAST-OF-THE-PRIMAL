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

## Cross-agent interfaces (agreed up front)

- **Unity availability:** before any bridge use, ping with `$HOME/run.sh <fresh id> PrimalEditorBridge.Refresh "" 3`. No result = the editor is busy (owner in Play mode, or a stuck test run): keep working on code and retry later. Do not start a PlayMode run while another agent's run is going (the lock covers this).
- **Deployed but not compiled yet** (Unity was stuck when the Lead wrote them): `Audio/SfxPlayer.cs` and `VFX/VfxPool.cs` (new ids), `Combat/IDamageable.cs` (`HitInfo.unarmed`, `HitInfo.knockback`). They compile at the next refresh.
- **Save sections (SURV builds first):** `ISaveSection { string SectionKey { get; } string CaptureSection(); void RestoreSection(string json); }` plus `SaveSystem.RegisterSection(ISaveSection)` / `UnregisterSection`, stored as named JSON blobs in the save file (missing or broken section = skipped, never a crash). AI uses it for creatures; any agent may use it instead of editing SaveData.
- **Status effects (SURV builds):** one player component (e.g. `PlayerStatusEffects`) with ScriptableObject definitions (`StatusEffectDefinition`): `Apply(def or id, severity, seconds)`, `Has(id)`, `Remove(id)`, event `Changed`. Bleeding is applied by the player's damage path (PlayerHealth, SURV) from hit severity; AI reads `Has(Bleeding)` for blood scent; U never applies effects directly.
- **Campfire read API for AI (SURV):** `Campfire.All`, `IsLit`, `Fuel01` / `Intensity01`, `State` (Unlit / Lighting / Burning / LowFuel / Extinguished), `CookingCount`, `Sheltered`. AI only reads.
- **Bare-hand hits (U / AI):** U sends normal `HitInfo` with `unarmed = true`, small damage and optional `knockback`; each creature (AI, `DinosaurController`) scales unarmed damage by species / size data (large creatures barely hurt) and applies knockback if small.
- **Fish / edible plant:** SURV creates the items (`raw_fish`, `cooked_fish`, `edible_plant`) with icons and simple original models; RES places the nodes (fish near the stream / pond, edible plants at the forest edge) with its builder, skipping ids that do not exist yet.
- **Clips from CHAR:** staging FBX + `clips_manifest.json` in `E:\Model game khủng long\export\staging\`. U moves them into Assets and wires them (BareHand_* names from the Phase 3.5 directive).
