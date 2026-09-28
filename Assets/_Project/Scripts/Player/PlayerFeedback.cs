using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Audio;
using PrimalFrontier.UI;
using PrimalFrontier.VFX;

namespace PrimalFrontier.Player
{
    /// <summary>
    /// Connects animation events, health and movement to VFX / SFX / screen feedback: footsteps by surface, gathering
    /// debris, crafting dust, eating crumbs (+ steam for hot food), drinking splashes, hit blood (small, short), bleeding,
    /// directional hit indicator, landing dust. Pure presentation - no gameplay rules here.
    /// </summary>
    [RequireComponent(typeof(PlayerMotor))]
    public class PlayerFeedback : MonoBehaviour
    {
        public float footstepMinSpeed = 0.3f;
        [Tooltip("blend trees fire the footsteps of every clip they mix: steps from clips weighted below this are ignored")]
        [Range(0, 1)] public float footstepMinClipWeight = 0.5f;
        [Tooltip("set by interaction code: where the tool hits (tree / rock / plant) for gather debris")]
        public Vector3? ActionFocusPoint { get; set; }
        public Vector3 ActionFocusNormal { get; set; } = Vector3.up;
        [Tooltip("set by the food system before Eat: cooked food steams")]
        public bool HotFood { get; set; }

        PlayerMotor _motor; PlayerAnimationDriver _drv; PlayerHealth _hp; PlayerFacial _face; Animator _anim; CharacterAnimationEvents _ev;
        PooledEffect _bleed;

        void Awake()
        {
            _motor = GetComponent<PlayerMotor>(); _drv = GetComponent<PlayerAnimationDriver>(); _hp = GetComponent<PlayerHealth>(); _face = GetComponent<PlayerFacial>();
            _anim = _drv ? _drv.animator : GetComponentInChildren<Animator>();
            _ev = GetComponentInChildren<CharacterAnimationEvents>();
        }

        void OnEnable()
        {
            if (_ev) _ev.AnimationEventRaised += OnAnimEvent;
            if (_hp) { _hp.Damaged += OnDamaged; _hp.Died += OnDied; _hp.Healed += OnHealed; _hp.BleedingChanged += OnBleeding; _hp.Revived += OnRevived; }
            if (_motor) _motor.Landed += OnLanded;
        }

        void OnDisable()
        {
            if (_ev) _ev.AnimationEventRaised -= OnAnimEvent;
            if (_hp) { _hp.Damaged -= OnDamaged; _hp.Died -= OnDied; _hp.Healed -= OnHealed; _hp.BleedingChanged -= OnBleeding; _hp.Revived -= OnRevived; }
            if (_motor) _motor.Landed -= OnLanded;
        }

        Vector3 Bone(HumanBodyBones b, Vector3 fallback)
        {
            var t = _anim && _anim.isHuman ? _anim.GetBoneTransform(b) : null; return t ? t.position : fallback;
        }
        Vector3 HandsMid => (Bone(HumanBodyBones.LeftHand, transform.position + Vector3.up) + Bone(HumanBodyBones.RightHand, transform.position + Vector3.up)) * 0.5f;
        Vector3 Mouth => Bone(HumanBodyBones.Head, transform.position + Vector3.up * 1.65f) + transform.forward * 0.11f - Vector3.up * 0.03f;
        Vector3 Chest => Bone(HumanBodyBones.Chest, transform.position + Vector3.up * 1.35f);

        static VfxPool Fx => VfxPool.Instance;
        static SfxPlayer Sfx => SfxPlayer.Instance;

        void OnAnimEvent(string fn, string param)
        {
            switch (fn)
            {
                case "OnFootstep": if (_ev.EventClipWeight >= footstepMinClipWeight) Footstep(param == "R"); break;
                case "OnGatherHit": Gather(param); break;
                case "OnCraftTick":
                    Fx.Play(VfxId.CraftDust, HandsMid, Vector3.up); Sfx.Play(SfxId.Craft, HandsMid, 0.7f);
                    if (param == "knap" || Random.value < 0.25f) { Fx.Play(VfxId.CraftSparks, HandsMid, transform.forward); Sfx.Play(SfxId.CraftKnap, HandsMid, 0.6f); }
                    break;
                case "OnEat":
                    Fx.Play(VfxId.FoodCrumbs, Mouth, transform.forward); Sfx.Play(SfxId.Eat, Mouth, 0.8f);
                    if (HotFood) { var s = Fx.Play(VfxId.Steam, Bone(HumanBodyBones.RightHand, Mouth), Quaternion.identity, _anim ? _anim.GetBoneTransform(HumanBodyBones.RightHand) : null, 0.6f); HotFood = false; }
                    break;
                case "OnDrink":
                    Fx.Play(VfxId.WaterSplash, HandsMid - Vector3.up * 0.05f, Vector3.up, null, 0.6f); Fx.Play(VfxId.WaterDrops, Mouth, Vector3.down);
                    Sfx.Play(SfxId.Drink, Mouth, 0.8f); break;
                case "OnAttackHit": case "OnThrowRelease": Sfx.Play(SfxId.SpearWhoosh, HandsMid, 0.8f); break;
                case "OnBowDrawStart": Sfx.Play(SfxId.BowDraw, HandsMid, 0.7f); break;
                case "OnBowRelease": Sfx.Play(SfxId.BowRelease, HandsMid, 0.9f); break;
                case "OnBuildHit": Fx.Play(VfxId.WoodChips, ActionFocusPoint ?? HandsMid, Vector3.up, null, 0.6f); Sfx.Play(SfxId.Build, HandsMid, 0.8f); break;
                case "OnPickup": Sfx.Play(SfxId.Pickup, HandsMid, 0.6f); break;
                case "OnHurt": Sfx.Play(SfxId.HurtGrunt, Mouth, param == "heavy" ? 1f : 0.7f); break;
                case "OnBodyFall": Fx.Play(VfxId.LandDust, transform.position, Vector3.up); Sfx.Play(SfxId.Land, transform.position, 0.9f); break;
                case "OnJumpTakeoff": Footstep(false, 0.8f); break;
            }
        }

        void Footstep(bool right, float vol = 1f)
        {
            if (_motor.PlanarSpeed < footstepMinSpeed && vol >= 1f) return;
            var p = Bone(right ? HumanBodyBones.RightFoot : HumanBodyBones.LeftFoot, transform.position); p.y = transform.position.y;
            var s = SurfaceDetector.At(p);
            float speedK = Mathf.Clamp01(_motor.PlanarSpeed / 6f);
            float quiet = _motor.IsCrouching ? 0.35f : 1f;
            VfxId fx = s switch { Surface.Sand => VfxId.FootSand, Surface.Mud => VfxId.FootMud, Surface.Rock => VfxId.FootRock, Surface.Water => VfxId.WaterDrops, Surface.Wood => VfxId.None, _ => VfxId.FootDirt };
            SfxId sfx = s switch { Surface.Sand => SfxId.FootSand, Surface.Mud => SfxId.FootMud, Surface.Rock => SfxId.FootRock, Surface.Water => SfxId.FootWater, Surface.Wood => SfxId.FootRock, _ => SfxId.FootDirt };
            if (fx != VfxId.None && (speedK > 0.35f || s == Surface.Sand || s == Surface.Mud)) Fx.Play(fx, p, Vector3.up, null, 0.6f + 0.6f * speedK);
            Sfx.Play(sfx, p, (0.45f + 0.55f * speedK) * quiet * vol);
        }

        void Gather(string kind)
        {
            Vector3 p = ActionFocusPoint ?? (HandsMid + transform.forward * 0.35f);
            Vector3 n = ActionFocusPoint.HasValue ? ActionFocusNormal : -transform.forward;
            switch (kind)
            {
                case "Gather_Wood": Fx.Play(VfxId.WoodChips, p, n); Sfx.Play(SfxId.WoodChop, p); break;
                case "Gather_Stone": Fx.Play(VfxId.StoneChips, p, n); Sfx.Play(SfxId.StoneHit, p); break;
                default: Fx.Play(VfxId.Leaves, p, Vector3.up); Sfx.Play(SfxId.LeafRustle, p); break;
            }
        }

        void OnDamaged(float amount, Vector3 source, bool heavy)
        {
            if (_hp && _hp.IsDead) return;
            if (_drv) _drv.Hurt(heavy);
            if (_face) _face.OnHit(heavy);
            Vector3 chest = Chest; Vector3 dir = source - chest; dir.y = 0; dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : transform.forward;
            Fx.Play(heavy ? VfxId.HitHeavy : VfxId.HitLight, chest + dir * 0.16f, dir + Vector3.up * 0.3f);
            if (heavy) BloodDecals.Instance.Splat(chest - dir * 0.4f, 0.35f, transform, 60f);
            Sfx.Play(heavy ? SfxId.HitHeavy : SfxId.HitFlesh, chest);
            DamageOverlay.Instance.ShowHit(dir, heavy);
            var cam = Camera.main ? Camera.main.GetComponent<ThirdPersonCamera>() : null;
            if (cam) cam.AddShake(heavy ? 0.12f : 0.05f, heavy ? 0.35f : 0.18f);
        }

        void OnBleeding(bool on)
        {
            if (on && _bleed == null)
            {
                var chest = _anim && _anim.isHuman ? _anim.GetBoneTransform(HumanBodyBones.Chest) : transform;
                _bleed = Fx.Play(VfxId.Bleed, Chest + transform.forward * 0.12f, Quaternion.identity, chest);
            }
            else if (!on && _bleed != null) { _bleed.StopEmitting(); _bleed = null; }
        }

        void OnDied()
        {
            if (_bleed != null) { _bleed.StopEmitting(); _bleed = null; }
            if (_drv) _drv.Die();
            Sfx.Play(SfxId.Death, Chest);
        }

        void OnRevived() { if (_drv) _drv.Respawn(); }
        void OnHealed(float amount) { if (amount >= 2f) DamageOverlay.Instance.ShowHeal(); }

        void OnLanded(float impactSpeed)
        {
            if (impactSpeed < 4f) return;
            var s = SurfaceDetector.At(transform.position);
            Fx.Play(s == Surface.Water ? VfxId.WaterSplash : VfxId.LandDust, transform.position, Vector3.up, null, Mathf.Clamp(impactSpeed / 8f, 0.5f, 1.2f));
            Sfx.Play(SfxId.Land, transform.position, Mathf.Clamp01(impactSpeed / 10f));
        }
    }
}
