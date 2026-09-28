# CHARACTER HIERARCHY (adapted to the existing project)

The brief's target tree is adapted to Unity rules and to what already works. Rules used:
- The **CharacterController and all movement / gameplay components stay on the `Player` root**: a CharacterController
  moves the object it is on, and PlayerMotor, tests and saves expect them there. "Movement" and "CharacterController"
  in the brief are component groups on the root, not child objects.
- The **Animator stays on `Model`** (it must sit above the bones it animates; the prefab `PFB_Player_Survivor` is nested).
- **Sockets must be children of bones** (they move with the hands). Groups like `Equipment`, `Combat`, `Interaction`,
  `Animation/IK` are plain empties on the root that hold targets and references.
- Nothing is duplicated: `PlayerHierarchy` (runtime) and `PrimalPlayerHierarchy.Build` (editor) find nodes by name and
  only create what is missing, like the rest of the project.

```
Player                         [root] tag Player, layer Player
|  CharacterController, PlayerMotor (Movement), PlayerAnimationDriver, PlayerCombat, WeaponController (new),
|  PlayerEquipment, PlayerInteraction, PlayerHealth, PlayerSurvival, InventorySystem, CraftingSystem, PlayerFeedback,
|  PlayerFacial, PlayerHierarchy (new), runtime: PlayerState, PlayerWetLook, PlayerClimb
|
|-- Model                      nested PFB_Player_Survivor: Animator, CharacterAnimationEvents, LODGroup, PlayerIK,
|   |                          TwistBoneDriver (new)
|   |-- Root > Pelvis > Spine > Spine_Upper > Chest > Neck > Head
|   |     Chest > Clavicle_L/R (shoulder) > UpperArm_L/R > [UpperArmTwist_L/R new]
|   |                                      > LowerArm_L/R > [LowerArmTwist_L/R new] ; LowerArm > Hand_L/R > fingers
|   |     Pelvis > Thigh_L/R (upper leg) > Calf_L/R (lower leg) > Foot_L/R > Toe_L/R
|   |     Hand_R > Weapon_R (helper, existing)   Hand_L > Weapon_L (helper, existing)
|   |-- PLAYER_Survivor_LOD0 / LOD1 / LOD2      SkinnedMeshRenderers
|   |
|   |   Sockets (created under bones):
|   |   Hand_R  > RightHandWeaponSocket   sword / spear / knife / tools grip (SwordSocket, ToolSocket)
|   |   Hand_R  > ArrowSocket             nocked arrow while drawing the bow
|   |   Hand_L  > LeftHandWeaponSocket    bow grip (BowSocket), off-hand items
|   |   Chest   > BackWeaponSocket        spear / sword / bow carried on the back when not in hand
|   |   Pelvis  > HipToolSocket           knife / small tool when not in hand
|
|-- Animation
|   |-- IK
|       |-- RightHandIK, LeftHandIK       IK goals (weapon grip points, climbing, reach) written by PlayerIK / weapons
|       |-- RightElbowHint, LeftElbowHint elbow hint positions (behind / outside the arm)
|
|-- Equipment                  EquipmentSockets component: references to the sockets above (one place to look them up)
|-- Interaction
|   |-- InteractionOrigin      chest height, used for interaction scans and look-at
|-- Combat
|   |-- AttackOrigin           front of the chest: fallback melee sweep origin, projectile aim origin
|   |-- HitPoint               centre of the body for creatures aiming at the player
|   (WeaponHitbox and the trail live on the held weapon model itself)
|-- CameraTarget               follows the head height, lowered when crouching; ThirdPersonCamera follows it
|-- VFX                        anchors only; footstep dust, landing dust, hit and bush effects are pooled (VfxPool),
                               never persistent children
```

## Responsibilities
| Node | Owner script | Responsibility |
|---|---|---|
| Player (root) | PlayerMotor | movement, gravity, jump, crouch, facing |
| Model | Animator + PlayerAnimationDriver | animation state; IK pass in PlayerIK; TwistBoneDriver spreads wrist twist along the forearm |
| Sockets | PlayerEquipment / WeaponController | where held and carried items attach; grip alignment per WeaponData |
| Animation/IK | PlayerIK | hand goals (off-hand on spear / sword / bow), elbow hints, climbing, feet |
| Equipment | EquipmentSockets | lookup of sockets by role |
| Interaction/InteractionOrigin | PlayerInteraction | scan origin |
| Combat/AttackOrigin, HitPoint | WeaponController, creatures | fallback sweep origin, target point |
| CameraTarget | ThirdPersonCamera | camera pivot that follows crouching |
| VFX | VfxPool | anchors only |
