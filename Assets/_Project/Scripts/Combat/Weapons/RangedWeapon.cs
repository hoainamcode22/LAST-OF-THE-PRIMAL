using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Items;
using PrimalFrontier.Player;

namespace PrimalFrontier.Combat.Weapons
{
    /// <summary>
    /// Bow: aim (Bow_Aim upper body), hold attack to draw (Bow_Draw, an arrow nocked on the ArrowSocket and pointed at
    /// the bow hand), let go to shoot (Bow_Release). Speed and damage grow with the draw; below the minimum draw nothing
    /// is shot. The arrow (pooled) leaves the nock point toward what the camera centre looks at. Ammo, stamina and wear
    /// work as before (the old PlayerCombat bow code lives here now).
    /// </summary>
    public class RangedWeapon : WeaponBase
    {
        const float BackToAimDelay = 0.45f;

        bool _aiming, _aimPose;
        float _drawStart = -1f, _backToAimAt = -1f;
        ItemStack _stack;
        Transform _nocked;                                 // pivot at the nock, arrow model as its child (one instance, reused)

        public RangedWeapon(WeaponData data, ItemDefinition item) : base(data, item) { }

        public bool IsDrawing => _drawStart >= 0f;
        public float DrawProgress => _drawStart < 0f ? 0f : Mathf.Clamp01((Time.time - _drawStart) / Mathf.Max(0.05f, Data.drawTime));
        public override bool Busy => IsDrawing;
        /// <summary>the nocked arrow while drawing (null otherwise)</summary>
        public Transform NockedArrow => _nocked && _nocked.gameObject.activeSelf ? _nocked : null;

        public override void Unequip()
        {
            Cancel();
            if (_aimPose) { Ctx.anim.Stop(); _aimPose = false; }
            if (_nocked) Object.Destroy(_nocked.gameObject);
            _nocked = null; _aiming = false;
            base.Unequip();
        }

        public override void OnAimChanged(bool aiming)
        {
            _aiming = aiming;
            if (aiming) { Ctx.anim.Play(PlayerActions.BowAim); _aimPose = true; }
            else if (_aimPose) { Ctx.anim.Stop(); _aimPose = false; Cancel(); }
        }

        public override void Cancel()
        {
            _drawStart = -1f; _backToAimAt = -1f;
            ShowNocked(false);
        }

        public override void HandleInput(WeaponInput input)
        {
            if (!_aiming)
            {
                if (input.pressed) PlayerInteraction.Notify("Hold right mouse button to aim the bow.");
                return;
            }
            var ammo = Data.ammo;
            if (input.pressed)
            {
                if (ammo && !Ctx.inventory.Has(ammo)) { PlayerInteraction.Notify("No arrows."); return; }
                _drawStart = Time.time; _backToAimAt = -1f; _stack = ActiveStack;
                Ctx.anim.Play(PlayerActions.BowDraw);
                ShowNocked(true);
            }
            else if (_drawStart >= 0f && !input.held) Release();
        }

        void Release()
        {
            float k = DrawProgress; _drawStart = -1f;
            ShowNocked(false);
            Ctx.anim.Play(PlayerActions.BowRelease);
            if (k < Data.minDraw) { Ctx.anim.Play(PlayerActions.BowAim); return; }          // let go too early: no shot
            var ammo = Data.ammo;
            if (ammo && !Ctx.inventory.Remove(ammo, 1)) return;
            if (Ctx.survival) Ctx.survival.UseStamina(Data.staminaCost);
            Vector3 from = NockPoint(out bool fromSocket);
            Vector3 dir = AimDirection(from);
            if (!fromSocket) from += dir * 0.7f;
            float speed = Mathf.Lerp(Data.projectileSpeedMin, Data.projectileSpeedMax, k);
            Projectile.Launch(ammo ? new ItemStack(ammo, 1) : null, from, dir * speed,
                new HitInfo { damage = Data.damage * Mathf.Lerp(0.4f, 1f, k), attacker = Ctx.owner, weapon = WeaponKind.Bow }, Ctx.owner, Data.recoverChance, true);
            Wear(_stack, Data.durabilityCost);
            MarkCombat();
            _backToAimAt = Time.time + BackToAimDelay;
        }

        /// <summary>the ArrowSocket on the right hand; the old head-height point when the rig has none</summary>
        Vector3 NockPoint(out bool fromSocket)
        {
            var s = Ctx.hierarchy ? Ctx.hierarchy.ArrowSocket : null;
            fromSocket = s && s.parent != Ctx.root;
            return fromSocket ? s.position : Ctx.root.position + Vector3.up * 1.55f;
        }

        /// <summary>toward what the camera centre looks at (200 m, the player ignored)</summary>
        Vector3 AimDirection(Vector3 from)
        {
            var cam = Camera.main;
            if (!cam) return Ctx.root.forward;
            var ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f));
            Vector3 target = Physics.Raycast(ray, out var h, 200f, Ctx.hitMask, QueryTriggerInteraction.Ignore) ? h.point : ray.origin + ray.direction * 200f;
            Vector3 d = target - from;
            // something between the camera and the hand: shoot along the view instead of backwards
            if (d.sqrMagnitude < 0.25f || Vector3.Dot(d, ray.direction) <= 0f) return ray.direction;
            return d.normalized;
        }

        public override void Tick(float dt)
        {
            if (_backToAimAt > 0f && Time.time >= _backToAimAt)
            {
                _backToAimAt = -1f;
                if (_aiming) Ctx.anim.Play(PlayerActions.BowAim);
            }
        }

        // ------------------------------------------------------------------ nocked arrow
        void ShowNocked(bool on)
        {
            if (on && !_nocked) CreateNocked();
            if (_nocked && _nocked.gameObject.activeSelf != on) _nocked.gameObject.SetActive(on);
            if (on) LateTick();
        }

        void CreateNocked()
        {
            var socket = Ctx.hierarchy ? Ctx.hierarchy.ArrowSocket : null;
            if (!socket) return;
            var pivot = new GameObject("NockedArrow").transform;
            pivot.SetParent(socket, false);
            var ammo = Data.ammo;
            GameObject model = ammo && ammo.handPrefab ? Object.Instantiate(ammo.handPrefab, pivot, false) : ProjectilePool.CreateArrowModel(pivot);
            foreach (var c in model.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
            int layer = Ctx.owner.layer;
            foreach (var t in pivot.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            // put the nock (lowest point along +Y) on the pivot
            float nock = WeaponHitbox.MeasureLocalBounds(model.transform, out var bounds) ? bounds.min.y : 0f;
            model.transform.localPosition = new Vector3(0f, -nock * model.transform.localScale.y, 0f);
            _nocked = pivot;
            _nocked.gameObject.SetActive(false);
        }

        /// <summary>after the Animator: the arrow lies from the nock (right hand) toward the bow hand</summary>
        public override void LateTick()
        {
            if (!_nocked || !_nocked.gameObject.activeSelf) return;
            var h = Ctx.hierarchy; var socket = _nocked.parent;
            var bow = h ? h.LeftHandWeaponSocket : null;
            Vector3 dir = bow ? bow.position - socket.position : Ctx.root.forward;
            if (dir.sqrMagnitude < 1e-4f) dir = Ctx.root.forward;
            _nocked.SetPositionAndRotation(socket.position, Quaternion.FromToRotation(Vector3.up, dir.normalized));
        }
    }
}
