# Bare-hand combat

The player starts with nothing in hand and can still fight. Hands are the emergency option: weak damage, short reach,
stamina on every punch. Tools and weapons are the progression (a stone spear does about 3-4x a jab).

Bare hands are not a second combat system. They are one more `WeaponData` asset that runs through the same
`WeaponController` / `MeleeWeapon` / `WeaponHitbox` path as the spear, knife and sword.

## 1. When the fists are the weapon

`WeaponController.ResolveData(item)` decides what the attack button fights with:

| Active hotbar item | Attack button |
|---|---|
| none (empty slot) | bare hands |
| an item with no weapon role: no `weaponData`, `weapon == None`, `damage == 0`, not food, water, camp item or torch, and no use handler claims it (wood, stone, fibre ...) | bare hands (the held item is neither used nor worn) |
| item with `weaponData` (spear, knife, sword, axe, pick, hand stone, torch) | that weapon, unchanged |
| bow (with or without data) | the bow, unchanged |
| food, water container | eat / drink (`PlayerInteraction.UseActiveConsumable`) |
| camp item | build mode |
| an item a use handler claims (`PlayerInteraction.UseHandlers`, e.g. SURV's bandage) | that handler |

The helper is `WeaponController.FightsBareHanded(item)`, and `WeaponController.BareHanded` is true while the fists are
the weapon. Switching the hotbar re-selects every frame (`WeaponController.Sync`). So empty slot -> fists, spear slot
-> spear, and back again, with a running attack cancelled on the switch.

## 2. Attack flow

Input goes through `PlayerInputReader` into `PlayerCombat.Update`, then `WeaponController.HandleInput`, then `MeleeWeapon`.

1. **Tap the attack button:** light attack.
   - The next light press within the combo window (1.1 s from the start of the last punch) continues the chain:
     `BareHand_Punch_1` (jab) -> `BareHand_Punch_2` (cross) -> `BareHand_Punch_3` (finisher), then back to 1.
   - A press during a punch is queued (one step, 1.2 s lifetime). It starts as soon as the body is free, and always
     continues the chain.
2. **Hold the attack button 0.35 s:** `BareHand_Heavy`.
   - The separate HEAVY button (touch, gamepad right shoulder) starts the heavy at once.
3. **Each attack runs Startup -> Active -> Recovery.** The hitbox is live only in Active. Control comes back when the
   body blends out of the attack state (`PlayerAnimationDriver.IsBusy` false). The third punch settles through
   `BareHand_Combo_End` once that clip exists.
4. **Stamina is spent when the attack really starts:**
   - light: `staminaCost` x the step's multiplier (punch 3: x1.3);
   - heavy: x `heavyStaminaMultiplier`;
   - below half the cost the punch is refused ("Too tired to strike.").
5. **Movement:**
   - `MeleeWeapon` owns the body during the attack; the motor does not move it (`CanMove` false while busy).
   - The finisher and the heavy have a small step in (lunge).
   - Blocking is not available with bare hands (`PlayerCombat.CanBlockWith` is false for unarmed data).
6. **Feedback:**
   - `PunchWhoosh` on the swing;
   - `PlayerGrunt` when a heavy or the finisher starts;
   - on contact, `PunchHit` / `PunchHeavyHit` plus `PunchImpactSmall` / `PunchImpactHeavy` on creatures (blood comes from
     the creature itself), or `DustImpact` on other targets;
   - a small camera shake.

`PlayerState.Activity` reports `Attack` / `HeavyAttack` while a punch runs (with Idle, Move, Run, Sprint, Jump, Block,
Aim, Gather, Interact, Climb, Build, Eat, Drink, Hurt, Dead, Sleep). Other systems read it instead of attack booleans
from several scripts.

## 3. Damage flow

1. **The hitbox:**
   - `MeleeWeapon` sweeps the fallback volume of `WeaponHitbox` on `Combat/AttackOrigin`, in front of the chest.
   - With `reachDown` > 0 that volume is a capsule from the chest down to 0.3 m above the feet, so small creatures and
     stone piles on the ground are reached.
   - A held item's model is never used as a blade with bare hands.
2. **Each `IDamageable` is hit at most once per punch.** The `HitZone` multiplier applies.
3. **The target gets a normal `HitInfo`:**
   - `damage` = damage (or heavy damage) x step multiplier x zone;
   - `unarmed = true`;
   - `knockback` = the light or heavy push speed (the light one x the step multiplier);
   - `heavy` for the heavy and the finisher, which stagger;
   - `weapon = None`.
4. **Each target decides what an unarmed hit means:**
   - Creatures (AI agent, `DinosaurController` / `AmbientCreature`) scale unarmed damage by size, so large ones barely
     notice, and apply knockback when small.
   - Small resource nodes (RES agent) accept bare-hand strikes through `IDamageable`.
   - The punch path needs nothing special for either.
5. **Stagger and bleeding are the target's own rules,** exactly as for weapons. `WeaponController.TargetHit` fires for
   listeners (tests, HUD).

Measured on the island (2026-09-29, before the AI scaling landed):
- a jab (4 damage) takes 5 % of a velociraptor's health (80);
- the same jab takes 0.53 % of an ankylosaurus' health (750).

## 4. Data: `Resources/Combat/WPN_bare_hands` (WeaponData)

Built by `PrimalCharacterBuilder.BuildBareHands` (bridge; `"reset"` restores the defaults). An existing asset keeps
hand-tuned numbers. The same defaults live in `WeaponData.ApplyBareHandDefaults`, so the game still fights if the asset
is missing.

| Field | Value | Meaning |
|---|---|---|
| `unarmed` | true | HitInfo.unarmed, no blade, no block |
| `damage` / `heavyDamage` | 4 / 9 | before the step and zone multipliers |
| attack chain | Punch_1 x1.0, Punch_2 x1.1, Punch_3 x1.4 (lunge 1.2 m/s, stamina x1.3) | light combo |
| `heavyAttack` | BareHand_Heavy x1.0 (lunge 1.6 m/s) | hold / HEAVY button |
| `staminaCost` / `heavyStaminaMultiplier` | 5 / 2.2 (heavy 11) | stamina per punch |
| `attackSpeed` | 1.1 | AttackSpeed parameter (clip speed) |
| `heavyHoldTime` | 0.35 s | hold time for the heavy |
| `comboWindow` | 1.1 s | from the start of a punch |
| `reach` / `heavyReach` / `reachDown` | 1.0 / 1.15 / 0.3 m | fallback volume |
| `knockback` / `heavyKnockback` | 0.6 / 2.5 m/s | HitInfo.knockback |
| hit windows (clips without window events) | 0.28-0.50, 0.28-0.50, 0.30-0.55, heavy 0.40-0.62 | normalized clip time |
| `hitVfx` / `hitVfxHeavy` / `hitVfxHard` | PunchImpactSmall / PunchImpactHeavy / DustImpact | contact effects |
| `hitSfx` / `hitSfxHeavy` / `swingSfx` / `effortSfx` | PunchHit / PunchHeavyHit / PunchWhoosh / PlayerGrunt | sounds |
| `durabilityCost` | 0 | fists never wear the held item |

New `WeaponData` fields, usable by any weapon (defaults keep the old behaviour):
- `unarmed`;
- `knockback` / `heavyKnockback`;
- `reachDown`;
- `hitVfxHeavy` / `hitSfxHeavy`;
- `effortSfx`.

## 5. Animation and events

| Action id (`PlayerActions`) | State | Clip now | Clip when CHAR delivers |
|---|---|---|---|
| `BareHandPunch1` = 50 | BareHand_Punch_1 (tag Attack, speed x AttackSpeed) | placeholder Knife_Attack | BareHand_Punch_1 |
| `BareHandPunch2` = 51 | BareHand_Punch_2 | placeholder Sword_Attack_1 | BareHand_Punch_2 |
| `BareHandPunch3` = 54 | BareHand_Punch_3 (-> Combo_End when it exists) | placeholder Sword_Attack_2 | BareHand_Punch_3 |
| `BareHandHeavy` = 52 | BareHand_Heavy | placeholder Sword_Heavy | BareHand_Heavy |
| `BareHandComboEnd` = 55 | BareHand_Combo_End (after Punch_3, tag Attack) | none (state skipped) | BareHand_Combo_End |
| upper body | BareHand_Idle (WeaponType 0 + CombatMode, like Sword_Idle) | none | BareHand_Idle |
| HitReaction layer | BareHand_HitReaction on HurtLight with WeaponType 0 (weapons keep Hurt_Additive) | none (Hurt_Additive plays) | BareHand_HitReaction |

**Hit timing comes from the clip events** (`CharacterAnimationEvents` -> `WeaponAnimatorBridge` -> `MeleeWeapon`):
- `OnAttackStart`: the swing sound.
- `OnAttackActive`: the hitbox on.
- `OnAttackHit`: the contact frame. A clip with Start / Hit / End but no Active opens the window here, sweeps at once and
  stays live until OnAttackEnd.
- `OnAttackEnd`: the hitbox off, recovery.
- A clip without window events uses the profile's normalized window, with `OnAttackHit` as the legacy contact.

Damage is never applied every frame: one hit per target per punch.

Clip events come from `clips_manifest.json` next to the anim json, merged by `PrimalCharacterBuilder`. If the manifest
has no events for a bare-hand clip, the builder writes OnAttackStart 0.10 / OnAttackActive (contact - 0.06) /
OnAttackHit (contact) / OnAttackEnd (contact + 0.2). Contact: Punch_1 / 2 0.33, Punch_3 0.38, Heavy 0.42, Kick 0.42.

## 6. Mobile controls (`UI/MobileHUD`)

**Attack button:**
- The label says what it does: PUNCH (bare hands), ATTACK (weapons), EAT / DRINK / PLACE (items it uses), BUILD
  (build mode).
- With bare hands or a melee weapon, a tap is the next light strike of the combo; holding it repeats light strikes
  every 0.32 s, so the combo runs.
- A bow keeps "hold to draw, release to shoot".

**HEAVY button** (up-left of ATTACK): the heavy attack at once. It shows only for melee weapons and bare hands, not in
build mode.

**Contextual button:**
- Shows only when there is something to use, labelled from the prompt: GATHER, DRINK, FILL, HARVEST, CLIMB, PICK UP or
  USE (`MobileHUD.ContextLabel`).
- Checked on the island: Gather Driftwood -> GATHER, Climb the fruit tree -> CLIMB (and the button climbs), Drink
  stream water -> DRINK, Fill Leaf Cup -> FILL.

**Keyboard / mouse:**
- tap left mouse: punch or combo;
- hold left mouse: heavy.

The F1 controls table and the key hints (`UI/ContextHints`: "[LMB] Punch   [Hold LMB] Heavy punch", dodge) read the real
bindings.

## 7. Tests (`Tests/PlayMode/BareHandCombatTests.cs`)

**BareHandCombatTests** (greybox course):
- starts unarmed with the fists;
- light combo 1-2-3 with events, hits, stamina;
- heavy on hold and on the HEAVY button;
- too tired to punch;
- items with a use keep it, items without a role punch;
- spear still fights, and the hotbar returns to the fists;
- dodge i-frames / stamina / cooldown;
- touch labels.

**BareHandIslandTests** (island):
- a small creature takes the punch, a large one barely;
- touch buttons show the context, and CLIMB starts a climb.

Full PlayMode suite on 2026-09-29: 80 passed, 0 failed, 7 skipped (probe / capture tests).
