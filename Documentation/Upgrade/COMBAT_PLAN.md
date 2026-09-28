# COMBAT PLAN (weapons, sword, bow, hunting)

Owner roles: `gameplay-programmer` (weapon code), `ai-programmer` (hunting), lead (clips, models, integration).

## 1. Architecture (modular, data-driven, no new manager)
New folder `Scripts/Combat/Weapons/`:
| Type | Kind | Responsibility |
|---|---|---|
| `WeaponData` | ScriptableObject | numbers and references for one weapon: kind, damage, heavy damage, stamina, attack speed, reach, hit radius, durability cost, combo window, `AttackProfile[]` (action id, damage x, stamina x, active window fallback start / end, lunge), hit / swing VFX + SFX ids, trail, grip offsets, off-hand grip point, carry socket, ranged fields (draw time, projectile speed min / max, ammo item) |
| `IWeapon` | interface | `Data`, `Equip(ctx)`, `Unequip()`, `Tick(dt)`, `OnAnimEvent(name)`, `Busy` |
| `WeaponBase` | abstract C# class | shared: stamina, wear on the attacking item (not the active slot), feedback |
| `MeleeWeapon` | WeaponBase | spear / knife / sword / tools: combo chain from `AttackProfile[]`, attack phases Startup -> Active -> Recovery, drives `WeaponHitbox` |
| `RangedWeapon` | WeaponBase | bow: aim, draw / charge, release, pooled arrow from `ArrowSocket` toward the camera aim point |
| `WeaponHitbox` | MonoBehaviour on the held model | capsule sweep base -> tip between frames **only while Active**, each target hit once per swing, multiple targets, HitZone multiplier, per-weapon impact VFX / SFX |
| `WeaponController` | MonoBehaviour on Player | owns the current IWeapon (created from the active item's `WeaponData`), routes input; PlayerCombat keeps food / placeables / torch and hands weapons to it |
| `WeaponAnimatorBridge` | plain class used by WeaponController | Action / WeaponType / CombatMode parameters, attack events -> weapon |
| `ProjectilePool` | static helper | reuses Projectile instances per prefab (no Instantiate / Destroy per arrow) |
`ItemDefinition` gets one new field `WeaponData weaponData` (appended; old fields kept for compatibility and saves).
`WeaponKind` gets `Sword = 4` (appended). Sword and Bow are **data assets**, not extra classes.

## 2. Attack timing
Clips carry `OnAttackStart`, `OnAttackActive`, `OnAttackEnd` events; if an event is missing, the `AttackProfile` normalized
window is used (fallback), so the hitbox is never active for the whole clip. A press during Active / Recovery is **queued as
the next combo step** with its own parameters (fixes the overwrite bug). The legacy `OnAttackHit` still resolves the
old single-hit path for clips without window events.

## 3. Sword
Model `WEAPON_FlintSword` (original: hardwood blade edged with flint teeth, grip wrapped in rawhide). Item `flint_sword`,
WeaponKind.Sword, recipe in CRAFTING_PLAN. Clips per ANIMATION_PLAN: idle, attack 1-2-3, heavy, block (if the controller
supports it cleanly), equip / unequip, hit reaction. Grip: `RightHandWeaponSocket`. Pipeline tool convention (checked
in Unity with `ModelBounds`): the handle runs along the model's local Z with the working end at -Z, which leaves the fist on
the thumb side of the grip frame; the edge faces +Y (the knuckles). `WeaponHitbox.MeasureBlade` and the off-hand grip
point follow this (the first draft assumed +Y).

## 4. Bow
Bow in `LeftHandWeaponSocket`, arrow nocked at `ArrowSocket` (right hand) during draw, string pull shown by the right hand
IK goal moving from the bow to the cheek with the draw amount; upper body turns toward the aim (spine look weight), head
looks along the aim. Release spawns a pooled arrow (real arrow model) from the nock point toward the camera ray hit.

## 5. Hunting loop
Detect (creature sighted) -> approach (crouch lowers detection, existing) -> aim / attack -> reaction (hurt, flee with the
herd, existing) -> death -> **carcass**: hold E with a cutting tool to butcher (loop of cuts gives meat / hide / bone,
knife faster, bare hands slow); carcass sinks and is removed after butchering or after a day. Loot no longer sprays as
separate pickups. Small prey species (skittish, fast, low HP) added through the creature pipeline in a later step.

## 6. Bug fixes in scope
Buffered melee overwrite, wear on the wrong slot, missing melee fallback, arrow primitive capsule, projectile spawn from the
root, SpearImpact on flesh, doubled footstep events.

## 7. Acceptance
Compiles; existing PlayMode combat tests keep their API; sword visible in the hand with correct grip (capture); hitbox
active only in the window (log / gizmo); bow held in the left hand with the arrow nocked (capture); butchering gives items.
Play feel: NOT TESTED until a person plays.
