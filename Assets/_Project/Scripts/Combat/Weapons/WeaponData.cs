using System;
using System.Collections.Generic;
using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Audio;
using PrimalFrontier.Items;
using PrimalFrontier.VFX;

namespace PrimalFrontier.Combat.Weapons
{
    /// <summary>where a weapon rides when it is not in the hand (read by the equip / carry visuals)</summary>
    public enum CarrySocket { None, Back, Hip }

    /// <summary>
    /// One step of a melee attack: which clip plays (PlayerActions id), its damage / stamina multipliers, the normalized
    /// window of the clip in which the hitbox is live when the clip has no OnAttackActive / OnAttackEnd events, and an
    /// optional forward lunge (PlayerMotor.Burst) when the strike starts.
    /// </summary>
    [Serializable]
    public class AttackProfile
    {
        [Tooltip("PlayerActions id of the clip (AttackSpear, SpearAttack2, KnifeAttack, SwordAttack1 ...). 0 = no attack")] public int action;
        [Tooltip("x the weapon damage (light) or heavy damage (heavy attack)")] public float damageMultiplier = 1f;
        [Tooltip("x the weapon stamina cost")] public float staminaMultiplier = 1f;
        [Tooltip("normalized clip time the hitbox turns on when the clip has no OnAttackActive event")] [Range(0, 1)] public float activeStart = 0.3f;
        [Tooltip("normalized clip time the hitbox turns off when the clip has no OnAttackEnd event")] [Range(0, 1)] public float activeEnd = 0.55f;
        [Tooltip("forward burst speed when the strike becomes active (m/s); 0 = none")] public float lunge;
        [Tooltip("duration of the lunge burst (s)")] public float lungeTime = 0.18f;

        public AttackProfile() { }
        public AttackProfile(int action, float damageMultiplier = 1f, float activeStart = 0.3f, float activeEnd = 0.55f, float lunge = 0f, float lungeTime = 0.18f, float staminaMultiplier = 1f)
        {
            this.action = action; this.damageMultiplier = damageMultiplier; this.activeStart = activeStart; this.activeEnd = activeEnd;
            this.lunge = lunge; this.lungeTime = lungeTime; this.staminaMultiplier = staminaMultiplier;
        }
        public bool IsValid => action != 0;
    }

    /// <summary>
    /// Numbers and references for one weapon (spear, knife, sword, tools, torch, bow). An item uses it through
    /// ItemDefinition.weaponData; the WeaponController builds a MeleeWeapon or RangedWeapon from it. Sword and bow are data
    /// assets, not extra classes. Created by PrimalWeaponBuilder (Assets/_Project/Data/Weapons); edit the numbers here.
    /// </summary>
    [CreateAssetMenu(menuName = "Primal Frontier/Weapon Data", fileName = "WPN_New")]
    public class WeaponData : ScriptableObject
    {
        [Header("Identity")]
        public WeaponKind kind;
        [Tooltip("ranged weapons (bow) draw and shoot instead of swinging")] public bool ranged;

        [Header("Damage / cost")]
        public float damage = 10f;
        public float heavyDamage = 16f;
        [Tooltip("stamina per light attack (x the profile's stamina multiplier)")] public float staminaCost = 10f;
        [Tooltip("heavy attack stamina = staminaCost x this")] public float heavyStaminaMultiplier = 1.8f;
        [Tooltip("animation speed multiplier for attacks (AttackSpeed parameter, when the controller has it)")] public float attackSpeed = 1f;
        [Tooltip("hold the attack button this long for the heavy attack (s)")] public float heavyHoldTime = 0.35f;
        [Tooltip("durability lost per swing that hits (heavy x2) or per arrow shot")] public float durabilityCost = 1f;

        [Header("Melee")]
        [Tooltip("reach of the fallback sphere (no blade mesh / legacy OnAttackHit) (m)")] public float reach = 1.35f;
        [Tooltip("reach of the heavy attack's fallback sphere (m); 0 = reach")] public float heavyReach;
        [Tooltip("radius of the blade capsule swept between frames (m)")] public float hitRadius = 0.12f;
        [Tooltip("the next light press within this many seconds of the last strike continues the chain")] public float comboWindow = 1.0f;
        [Tooltip("light attack chain, in order")] public AttackProfile[] attacks = new AttackProfile[0];
        [Tooltip("hold attack; action 0 = no heavy attack (light fires on press)")] public AttackProfile heavyAttack = new AttackProfile();

        [Header("Impact / bare hands")]
        [Tooltip("bare hands: HitInfo.unarmed (each target scales it: large creatures barely notice), no blade on a held model, no block")]
        public bool unarmed;
        [Tooltip("push-back speed a light hit asks the target for (HitInfo.knockback, m/s); 0 = none")] public float knockback;
        [Tooltip("push-back speed of the heavy attack (m/s)")] public float heavyKnockback;
        [Tooltip("fallback volume (no blade): a capsule from the chest down to this height above the feet (m), so low targets " +
                 "(small creatures, stone piles) are reached; 0 = the chest sphere only")] public float reachDown;

        [Header("Throw (aim + attack)")]
        public bool throwable;
        public float throwSpeed = 22f;
        [Tooltip("thrown damage = heavyDamage x this")] public float throwDamageMultiplier = 1.2f;

        [Header("Feedback")]
        [Tooltip("played at the contact point on creatures (blood comes from the creature itself); None = only blood")] public VfxId hitVfx = VfxId.None;
        public SfxId hitSfx = SfxId.HitFlesh;
        public SfxId swingSfx = SfxId.SpearWhoosh;
        [Tooltip("played on non-creature targets (breakables)")] public VfxId hitVfxHard = VfxId.HitDust;
        [Tooltip("heavy attack on creatures; None = hitVfx")] public VfxId hitVfxHeavy = VfxId.None;
        [Tooltip("heavy attack contact sound; None = hitSfx")] public SfxId hitSfxHeavy = SfxId.None;
        [Tooltip("effort sound when a heavy attack or a chain finisher starts (breath / grunt); None = silent")] public SfxId effortSfx = SfxId.None;
        public bool trail;
        public Color trailColor = new Color(1f, 0.93f, 0.8f, 0.55f);
        [Tooltip("additive material for the trail (M_VFX_Additive); empty = a default sprite material")] public Material trailMaterial;

        [Header("Grip / carry")]
        [Tooltip("offset in the hand socket's frame (m)")] public Vector3 gripPosition;
        [Tooltip("rotation in the hand socket's frame (deg)")] public Vector3 gripEuler;
        [Tooltip("off-hand grip point, local on the weapon model (m); zero = one-handed")] public Vector3 offHandGrip;
        public CarrySocket carrySocket = CarrySocket.None;
        [Tooltip("PlayerActions id played when the weapon is taken out (0 = none), e.g. SwordEquip")] public int equipAction;
        [Tooltip("seconds the equip action owns the upper body")] public float equipTime = 0.5f;

        [Header("Ranged")]
        [Tooltip("seconds to full draw")] public float drawTime = 0.9f;
        [Tooltip("arrow speed at the minimum draw (m/s)")] public float projectileSpeedMin = 14f;
        [Tooltip("arrow speed at full draw (m/s)")] public float projectileSpeedMax = 42f;
        [Tooltip("fraction of the full draw below which letting go does not shoot")] [Range(0, 1)] public float minDraw = 0.25f;
        [Tooltip("chance the arrow can be picked up again")] [Range(0, 1)] public float recoverChance = 0.6f;
        public ItemDefinition ammo;

        public bool IsRanged => !unarmed && (ranged || kind == WeaponKind.Bow);
        public bool HasHeavy => heavyAttack != null && heavyAttack.IsValid;
        public bool IsTwoHanded => offHandGrip != Vector3.zero;
        public float HeavyReach => heavyReach > 0f ? heavyReach : reach;

        // ------------------------------------------------------------------ bare hands
        /// <summary>asset path under a Resources folder (built by PrimalCharacterBuilder.BuildBareHands; tune the numbers there)</summary>
        public const string BareHandsResource = "Combat/WPN_bare_hands";
        static WeaponData _bareHands;

        /// <summary>
        /// the bare-hand "weapon": Resources/Combat/WPN_bare_hands when it exists, else a runtime copy of the same defaults
        /// (so empty hands always fight). Light chain BareHand_Punch_1 -> 2 -> 3, hold = BareHand_Heavy; weak damage.
        /// </summary>
        public static WeaponData BareHands
        {
            get
            {
                if (_bareHands) return _bareHands;
                _bareHands = Resources.Load<WeaponData>(BareHandsResource);
                if (!_bareHands)
                {
                    _bareHands = CreateInstance<WeaponData>();
                    _bareHands.name = "WPN_bare_hands (defaults)"; _bareHands.hideFlags = HideFlags.DontSave;
                    ApplyBareHandDefaults(_bareHands);
                }
                return _bareHands;
            }
        }

        /// <summary>
        /// the tuned bare-hand numbers (one place for the asset builder and the runtime fallback). Hands are the emergency
        /// option: a stone spear does 3-4x this. Windows are the placeholder clips' contact frames; the BareHand_* clips'
        /// OnAttackActive / OnAttackHit / OnAttackEnd events take over once they are imported.
        /// </summary>
        public static void ApplyBareHandDefaults(WeaponData d)
        {
            d.kind = WeaponKind.None; d.ranged = false; d.unarmed = true;
            d.damage = 4f; d.heavyDamage = 9f; d.staminaCost = 5f; d.heavyStaminaMultiplier = 2.2f;
            d.attackSpeed = 1.1f; d.heavyHoldTime = 0.35f; d.durabilityCost = 0f;
            d.reach = 1.0f; d.heavyReach = 1.15f; d.hitRadius = 0.12f; d.reachDown = 0.3f; d.comboWindow = 1.1f;   // from the start of a punch: ~0.35 s after a placeholder punch ends, ~0.6 s after a 14-frame jab
            d.knockback = 0.6f; d.heavyKnockback = 2.5f;
            d.attacks = new[]
            {
                new AttackProfile(PlayerActions.BareHandPunch1, 1.0f, 0.28f, 0.50f),                       // jab
                new AttackProfile(PlayerActions.BareHandPunch2, 1.1f, 0.28f, 0.50f),                       // cross
                new AttackProfile(PlayerActions.BareHandPunch3, 1.4f, 0.30f, 0.55f, 1.2f, 0.12f, 1.3f),    // finisher: a small step in
            };
            d.heavyAttack = new AttackProfile(PlayerActions.BareHandHeavy, 1.0f, 0.40f, 0.62f, 1.6f, 0.15f);
            d.throwable = false;
            d.hitVfx = VfxId.PunchImpactSmall; d.hitVfxHeavy = VfxId.PunchImpactHeavy; d.hitVfxHard = VfxId.DustImpact;
            d.hitSfx = SfxId.PunchHit; d.hitSfxHeavy = SfxId.PunchHeavyHit; d.swingSfx = SfxId.PunchWhoosh; d.effortSfx = SfxId.PlayerGrunt;
            d.trail = false; d.carrySocket = CarrySocket.None; d.equipAction = 0;
        }

        // ------------------------------------------------------------------ legacy items (no WeaponData asset)
        static readonly Dictionary<ItemDefinition, WeaponData> Legacy = new Dictionary<ItemDefinition, WeaponData>();

        /// <summary>
        /// runtime data for a bow item that has no WeaponData asset: the same numbers the old PlayerCombat bow used
        /// (speed = arrowSpeed x 0.5..1, damage x 0.4..1, min draw 25 %, arrows recovered 60 %). Cached per item.
        /// </summary>
        public static WeaponData LegacyBow(ItemDefinition bow, float arrowSpeed, float fullDraw)
        {
            if (bow == null) return null;
            if (Legacy.TryGetValue(bow, out var d) && d) return d;
            d = CreateInstance<WeaponData>();
            d.name = "WPN_legacy_" + bow.id; d.hideFlags = HideFlags.DontSave;
            d.kind = WeaponKind.Bow; d.ranged = true;
            d.damage = bow.damage; d.heavyDamage = bow.damage; d.staminaCost = bow.staminaCost; d.durabilityCost = 1f;
            d.drawTime = fullDraw; d.projectileSpeedMin = arrowSpeed * 0.5f; d.projectileSpeedMax = arrowSpeed; d.minDraw = 0.25f;
            d.ammo = bow.ammo; d.recoverChance = 0.6f; d.gripPosition = bow.gripPosition; d.gripEuler = bow.gripEuler;
            Legacy[bow] = d;
            return d;
        }
    }
}
