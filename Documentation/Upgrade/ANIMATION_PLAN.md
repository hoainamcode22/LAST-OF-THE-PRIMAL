# ANIMATION PLAN

## 1. Root motion decision (Phase 15)
Locomotion stays **code-driven** (CharacterController + in-place clips): the controller is responsive, works with
slopes / steps / climbing / building, and the tests depend on it. Root motion is **not** enabled. Attacks and the dodge
keep code-driven bursts (PlayerMotor.Burst) timed to the clips. Documented here so nobody switches it on blindly.

## 2. Clip fixes (Blender, `pf_clips_human.py` / `pf_anim.py`)
- All idle / locomotion / action clips: relaxed hands (fingers 15-35 degrees, thumb relaxed), wrist neutral with the palm
  toward the body, slight elbow bend (idle 12-18, walk 18-30, run 75-95 degrees), forearm follow-through (lags the upper arm
  by ~2 frames), shoulders counter-rotating the hips, pelvis rotation and weight shift, no perfectly straight limbs.
- Spear clips (Attack_Spear, Spear_Attack_2, Attack_Spear_Heavy, Throw_Spear): **two-handed grip** (left hand forward on
  the shaft), anticipation (pull back), contact, follow-through, recovery.
- Bow clips (Bow_Aim, Bow_Draw, Bow_Release): bow in the **left hand** (arm extended, slight elbow bend), right hand draws
  the string to the cheek, shoulders squared, head aligned; release with follow-through.

## 3. New clips
| Clip | Layer | Notes |
|---|---|---|
| Sword_Idle | upper body | blade low-forward, right hand grip, left hand relaxed or on the pommel |
| Sword_Attack_1 / 2 / 3 | full body | diagonal down-right, horizontal back-hand, overhead; each startup / active / recovery |
| Sword_Heavy | full body | charged overhead, longer startup |
| Sword_Block | upper body | blade across the body |
| Sword_Equip / Unequip | upper body | from / to the back socket |
| Run_Backward, Strafe_Run_L / R | base | for aim / combat 2D locomotion (Walk_Backward, Walk_Left, Walk_Right already exist) |
Events: `OnAttackStart` (startup end), `OnAttackActive` (hitbox on), `OnAttackEnd` (hitbox off), `OnFootstep`,
`OnEquip` (item moves socket), `OnHarvest` (receiver fixed).

## 4. Animator architecture (built by `PrimalCharacterBuilder`)
| Layer | Blending | Mask | Content |
|---|---|---|---|
| Base Movement | - | full | free locomotion 1D (Speed), **combat / aim locomotion 2D (VelX, VelZ)**, crouch, jump / fall / land, turn in place, actions |
| Upper Body Combat | override | AM_Player_UpperBody | sword idle / block / equip, bow aim / draw / release, carry |
| Hit Reaction | additive | upper body | Hurt additive so hits do not stop movement |
| IK | (IK pass on base) | - | feet, off-hand grip, elbow hints, look, climbing |
Parameters added: VelX, VelZ (local planar velocity, damped), CombatMode (bool), WeaponType (int), IsMoving (bool).
Speed becomes the **measured** planar speed.

## 5. Movement fixes (code)
Measured speed for the animator, analog stick speed without the 0.55 jump, fall only after a real drop (vertical speed
below -4 for 0.12 s or 0.6 m below the last ground), motor stops during Hurt / Land lock, footstep events filtered by
clip weight (only the dominant clip plays steps), start / stop smoothing by the existing accel / decel plus a stop
blend on the Idle end.

## 6. Acceptance
Contact sheet with relaxed hands; foot slide <= 5 % for walk / run / strafe / backward in the character test; no
moonwalking in aim mode (2D tree); character test PASS.
