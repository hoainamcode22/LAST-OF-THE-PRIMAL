# PROJECT AUDIT - Character, Animation, Combat, World Interaction, Crafting upgrade

Date: 2026-09-28. Scope: Unity 6000.3.10f1 URP project `E:\LAST OF THE PRIMAL`, Blender sources `E:\Model game khủng long`.
Method: four read-only audit agents (Explore) over the full C# source (15,709 lines: 9,885 runtime), plus a Blender
inspection of the player rig / mesh / weights with deformation test renders (`renders/characters/audit*`), plus the
two gameplay screenshots from the owner. Nothing was changed during the audit.

## 1. Current architecture (short)
- One scene `Island_VerticalSlice`; everything is a scene object (find-or-create by name). Roots: `[Systems]`, `[Gameplay]`,
  `Player`, `Main Camera`, `[UI]`, `World`, `Markers`, terrain, `Water`, `Sun`, `Global Volume`.
- Managers (singleton `Instance`, set in Awake, no duplicate guard): GameManager (-80), UIManager (-50), TimeManager,
  WeatherManager, JournalSystem, TutorialManager, BuildSystem, HUDManager, InventoryUI, PlayerInputReader, ZoneManager;
  on-demand pools VfxPool / SfxPlayer / BloodDecals (-200, DontDestroyOnLoad); ItemDatabase (Resources SO).
- Static event bus `GameEvents` (append-only enum, 47 values). Save: JsonUtility v2, slot 0.
- Editor builders generate content (`Scripts/Editor`), the editor bridge runs them in the open editor.

## 2. Current character architecture
- `Player` root: CharacterController (h 1.8, r 0.3), PlayerMotor, PlayerAnimationDriver, PlayerFacial, PlayerHealth,
  PlayerFeedback, InventorySystem, CraftingSystem, PlayerSurvival, PlayerInteraction, PlayerEquipment, PlayerCombat;
  runtime-added: PlayerIK (on Model), PlayerState, PlayerWetLook, PlayerClimb. Child `Model` = `PFB_Player_Survivor`
  (Animator, CharacterAnimationEvents, LODGroup 3 LODs 72,952 / 17,107 / 5,668 tris).
- Movement is **code-driven** (CharacterController), clips are in place (root motion baked, `applyRootMotion` false).
  Speeds walk 1.35 / run 3.8 / sprint 6.2 / crouch 0.95; accel 10, decel 14; facing SmoothDampAngle 0.09 s.
- Animator (built by `PrimalCharacterBuilder.BuildPlayerController`): base layer 36 states (IK pass on), UpperBody
  override layer (mask arms/body/head) used only by the bow. Blend trees are **1D on Speed** only (Idle-Walk-Run-Sprint,
  Crouch, Turn in place). Parameters: Speed, IsGrounded, VerticalVelocity, IsCrouching, IsAttacking, Action (int),
  HealthState, TurnSpeed, IdleVariant. `VelX/VelZ/Turn/Jump/UpperBody/HurtType` hashes exist in AnimParams but are
  never created or set.
- Rig (Blender `PLAYER_Survivor_Rig`, 55 bones): Root, Pelvis, Spine, Spine_Upper, Chest, Neck, Head, Clavicle,
  UpperArm, LowerArm, Hand, 3-bone fingers + thumb, Thigh, Calf, Foot, Toe, non-deforming Weapon_L / Weapon_R.
  **No twist bones.** Rest pose: A-pose (arms ~48 degrees down). Unity avatar: humanoid, T-pose enforced by the builder.
- Skin (exported LOD meshes): max 4 influences, all weights normalized (checked on LOD0/1/2).
- Equipment: held item parented straight to the RightHand bone; grip frame computed from finger bones; per-item grip
  offsets exist but are zeroed by the builder. No sockets, no off-hand placement, bow also in the right hand.
- IK (`PlayerIK`): feet placement + pelvis drop, lean, head look, climbing hands/feet. **No weapon hand IK, no elbow hints.**
- Camera: follows root + fixed pivot (0, 1.62, 0) (no CameraTarget; does not follow crouch).

## 3. Reusable systems (keep and extend)
PlayerMotor, PlayerAnimationDriver, PlayerIK, PlayerEquipment, PlayerCombat (partly), Projectile, IDamageable/HitInfo,
HitZone, InventorySystem, CraftingSystem (Check/Enqueue/Cancel/queue), RecipeDefinition, ItemDefinition/ItemDatabase,
ResourceNode (wobble, charges, regrow), TreeHarvest, Climbable + PlayerClimb + FruitCluster, DinosaurController (13-state
FSM, perception, flee with herd, loot drop), DinoLife, VfxPool (27 ids, pooled), SfxPlayer, SurfaceDetector, BloodFX,
UIFactory screens, SaveSystem, GameEvents, editor bridge + review capture.

## 4. Problems discovered
Character / animation
1. **Hands and arms read as robotic mainly because of the animation authoring**, not the mesh: in Idle / Walk / Run the
   palms face forward or outward (supinated) with claw-curled fingers, arms held away from the body; spear attack poses
   have both arms spread with no two-handed grip (renders `audit_sheet.png`).
2. Deformation tests (`audit_deform_sheet.png`): elbow flexion 110 degrees keeps volume (acceptable); wrist flexion 60
   acceptable; **wrist twist +-80 concentrates all twist at the wrist** (hidden by the bracer, but the forearm stays a rigid
   cylinder); **shoulder raise 90 collapses the deltoid / chest transition and the shoulder pelt deforms badly**.
3. Sliding causes: Speed parameter = commanded velocity (runs in place against walls, slower on ramps); aim-mode
   strafing/backpedal plays the forward Walk clip (moonwalk); Walk_Left / Walk_Right / Walk_Backward clips exist but are
   unused; stick push >= 0.55 jumps to full run (speed discontinuity); Fall triggers on the first airborne frame of any
   small drop (grounded vertical velocity pinned at -3); no start / stop / pivot; motor keeps moving during Hurt / Land.
4. Footstep events fire from every clip of a blend (no weight filter) -> doubled footsteps.
5. `OnHarvest` event has no receiver method (harvest always uses the 1.5 s fallback).
Combat / items
6. **Buffered melee bug**: a second press during a swing overwrites the pending attack; the first hit applies the second
   swing's parameters and the buffered swing deals nothing; stamina spent for dropped attacks.
7. Wear applied to the active slot at hit time, not the item that attacked.
8. Melee hit = one OverlapSphere at the `OnAttackHit` event, single target, no active window, no fallback, always
   SpearImpact VFX/SFX. No IWeapon / WeaponData / hitbox windows; weapon logic is `if WeaponKind` chains.
9. Arrows probably fly as primitive capsules (arrow `handPrefab` null); projectiles Instantiate/Destroy (no pool);
   projectiles spawn from root offsets, not from the hand / bow.
10. No sword exists. Bow drawn with the right hand only, no string pull, no hand IK.
11. Re-running `PrimalGameplayBuilder` overwrites the item list (drops `fruit`, resets the knife).
12. Crafting queue not saved (ingredients lost on save/load); two different cook paths for meat (10 s recipe vs 14 s fire).
World / hunting
13. No small huntable animals; only Parasaurolophus is real prey. Loot drops as pickups; no carcass butchering.
14. **No bush interaction at all** (bushes are terrain detail meshes: no collider, no rustle, no VFX).
15. Probable bug: tree chopping only reachable within ~9 m of the `[Systems]/Trees` object (interaction scan range vs
    Radius 0.45).
16. Per-frame allocations: `LayerMask.GetMask` per dinosaur per frame, `Climbable.Fruits` GetComponentsInChildren every
    read, prompt strings rebuilt each frame, HUDManager StringBuilder per frame, Minimap HashSet per frame.
17. Vegetation does not move (foliage wind shader reverted after an editor crash).

## 5. Duplicate systems / responsibilities
- Player rotation written by PlayerMotor, PlayerInteraction and PlayerClimb. Animator states set by parameters and also by
  direct CrossFade / Play in PlayerClimb, the driver and IntroSequence.
- Bootstrap lists duplicated in GameManager and PrimalSceneBaker. Seconds-per-hour defined three times.
- Two cook paths for meat. Player referenced four ways (GameManager.Player, PlayerLocator, PlayerState, tag).
- No duplicate managers found for weapons / crafting / VFX (nothing to merge; new systems must not duplicate
  PlayerCombat, PlayerEquipment, CraftingSystem, VfxPool, SfxPlayer).

## 6. Missing systems
Twist bones, weapon sockets, off-hand IK and elbow hints, 2D locomotion, WeaponData / hitbox windows, sword (model,
clips, gameplay), bow hand IK + string, pooled arrows with a real model, small prey + butchering, interactive bushes,
weapon trails, crafting data for weapons/tools tiers, crafting queue save.

## 7. Risk areas
- `PrimalCharacterBuilder` (controller + avatar + prefab, 515 lines) and `PlayerCombat` are covered by PlayMode tests
  (light / heavy attack, buffering): changes must keep those APIs.
- GameEvents enum order is serialized (append only). WeaponKind / ItemCategory / RecipeCategory stored as ints (append only).
- UI object names are used by find-or-create (do not rename).
- Unity editor crashed once after a custom lit shader switch: shader work only with backups and one material at a time.
- Re-running old generators (`Primal Frontier > Advanced`) overwrites hand edits and data.

## 8. Files that should NOT be touched in this upgrade
Core/GameEvents.cs (append only), Core/SaveSystem.cs + SaveData.cs (only additive fields with a version bump),
UI/UIFactory.cs, PrimalWorldBuilder.cs / PrimalGameplayBuilder.cs (legacy generators: do not re-run; new data goes
through new additive builders), terrain data, all dinosaur models / controllers except where hunting needs data,
Story/*, Minimap, MobileHUD (except new buttons if a weapon needs them).

## 9. Files that need modification
Blender: `pf_rig_v2.py` / `pf_player_v2.py` (twist bones, weights), `pf_clips_human.py` (+ `pf_anim.py`: hand / arm
posing, new clips), export. Unity: `PrimalCharacterBuilder(.Prefab).cs` (controller layers, 2D blend trees, twist
driver, avatar twist setting), PlayerAnimationDriver, PlayerMotor (measured speed, analog speed), PlayerIK (hand IK,
hints), PlayerEquipment (sockets), PlayerCombat (split weapon logic out, bug fixes), ItemDefinition (+WeaponData ref),
Projectile (pool, spawn point), PlayerFeedback (footstep weight filter, trails), CharacterAnimationEvents (OnHarvest,
attack window events), DinosaurController (butcher, small prey hooks), CraftingSystem (queue save, requirements).

## 10. Recommended implementation order
1 audit -> 2 hierarchy -> 3 topology evaluation -> 4 rig (twist bones) -> 5 weights (shoulder, forearm, pelt) ->
6 locomotion (measured speed, 2D strafe / backward, fall threshold, footstep filter) -> 7 IK (off-hand, hints) ->
8 sockets -> 9 sword -> 10 bow -> 11 hunting -> 12 bushes + VFX -> 13 climbing (exists: verify) -> 14 fruit (exists:
verify) -> 15 crafting -> 16 polish -> 17 optimization -> 18 QA.
