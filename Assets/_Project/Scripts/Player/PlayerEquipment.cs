using System;
using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Combat.Weapons;
using PrimalFrontier.Items;
using PrimalFrontier.VFX;

namespace PrimalFrontier.Player
{
    /// <summary>
    /// Shows the active hotbar item in the hand: RightHandWeaponSocket for tools and melee weapons, LeftHandWeaponSocket
    /// for the bow (PlayerHierarchy). The hand sockets sit on the grip frame computed from the humanoid hand bones
    /// (across the knuckles and along the fingers), so any Humanoid rig holds the tools the same way.
    /// Tool models: handle along local -Z... blade/head towards local +Y, origin at the grip. The per-item offset comes
    /// from WeaponData.gripPosition / gripEuler when the item has weapon data, else from the item's own grip fields.
    /// Two-handed weapons get an "OffHandGrip" child (WeaponData.offHandGrip) for the off-hand IK.
    /// </summary>
    public class PlayerEquipment : MonoBehaviour
    {
        public Animator animator;
        [Tooltip("VFX_CampfireLoop, scaled down on the torch head")] public GameObject torchFlamePrefab;
        public float torchLightRange = 9f, torchLightIntensity = 1.6f;

        InventorySystem _inv; PlayerAnimationDriver _drv; PlayerHierarchy _hier; PlayerClimb _climb;
        GameObject _held; ItemDefinition _heldItem; CampfireFx _flame; bool _deferred;
        public ItemDefinition HeldItem => _heldItem;
        public GameObject HeldObject => _held;
        public bool TorchLit => _flame != null && _held != null && _held.activeSelf;
        /// <summary>weapon data of the item in the hand (null = none / not a data-driven weapon)</summary>
        public WeaponData HeldWeaponData => _heldItem ? _heldItem.weaponData : null;
        /// <summary>where the off hand grips the held weapon (child of the held model), null = one-handed</summary>
        public Transform OffHandGrip { get; private set; }
        /// <summary>the socket the held model hangs on (null = nothing held)</summary>
        public Transform HeldSocket { get; private set; }
        /// <summary>raised after the held model was created / removed</summary>
        public event Action HeldChanged;

        void Awake()
        {
            _inv = GetComponent<InventorySystem>(); _drv = GetComponent<PlayerAnimationDriver>();
            if (!animator) animator = _drv ? _drv.animator : GetComponentInChildren<Animator>();
            _hier = gameObject.GetOrAdd<PlayerHierarchy>();
            if (!_hier.animator) _hier.animator = animator;
        }
        void OnEnable() { if (_inv) { _inv.ActiveSlotChanged += OnSlot; _inv.Changed += Refresh; } }
        void OnDisable() { if (_inv) { _inv.ActiveSlotChanged -= OnSlot; _inv.Changed -= Refresh; } }
        void Start() { _hier.Ensure(); Refresh(); }

        void OnSlot(int _) => Refresh();

        /// <summary>bow in the left hand, everything else in the right hand</summary>
        Transform SocketFor(ItemDefinition item)
        {
            var wd = item.weaponData;
            bool left = item.weapon == WeaponKind.Bow || (wd && wd.IsRanged);
            var s = left ? _hier.LeftHandWeaponSocket : _hier.RightHandWeaponSocket;
            return s ? s : transform;
        }

        public void Refresh()
        {
            var item = _inv ? _inv.ActiveItem : null;
            if (item != null && item.handPrefab == null) item = null;
            if (item == _heldItem) return;
            if (item != null && !_hier.SocketsReady) { _deferred = true; return; }    // hand bones not bound yet: LateUpdate retries
            _deferred = false;
            if (_held) Destroy(_held);
            _held = null; _flame = null; _heldItem = item; OffHandGrip = null; HeldSocket = null;
            if (item == null) { HeldChanged?.Invoke(); return; }
            var parent = SocketFor(item);
            _held = Instantiate(item.handPrefab, parent, false);
            _held.name = "Held_" + item.id;
            foreach (var c in _held.GetComponentsInChildren<Collider>()) Destroy(c);
            foreach (var r in _held.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            var wd = item.weaponData;
            _held.transform.localRotation = Quaternion.Euler(wd ? wd.gripEuler : item.gripEuler);
            _held.transform.localPosition = wd ? wd.gripPosition : item.gripPosition;
            _held.layer = gameObject.layer; foreach (Transform t in _held.GetComponentsInChildren<Transform>()) t.gameObject.layer = gameObject.layer;
            if (wd && wd.offHandGrip != Vector3.zero)
            {
                var g = new GameObject("OffHandGrip").transform;
                g.SetParent(_held.transform, false); g.localPosition = wd.offHandGrip; g.localRotation = Quaternion.identity;
                g.gameObject.layer = gameObject.layer;
                OffHandGrip = g;
            }
            HeldSocket = parent;
            if (item.lightRange > 0f && torchFlamePrefab) AddFlame(item);
            HeldChanged?.Invoke();
        }

        void AddFlame(ItemDefinition item)
        {
            // head of the torch: highest point along the model's +Y
            float top = 0.35f;
            foreach (var mf in _held.GetComponentsInChildren<MeshFilter>()) if (mf.sharedMesh) top = Mathf.Max(top, mf.sharedMesh.bounds.max.y * mf.transform.localScale.y);
            var f = Instantiate(torchFlamePrefab, _held.transform, false);
            f.transform.localPosition = new Vector3(0, top - 0.04f, 0);
            f.transform.localScale = Vector3.one * 0.22f;
            _flame = f.GetComponent<CampfireFx>();
            if (_flame) { _flame.lightRange = item.lightRange > 0 ? item.lightRange : torchLightRange; _flame.lightIntensity = torchLightIntensity; _flame.SetLit(true, false); }
        }

        void LateUpdate()
        {
            if (_deferred) Refresh();
            if (!_held) return;
            if (!_climb && (Time.frameCount & 31) == 0) TryGetComponent(out _climb);              // added at run time by Climbable (no editor alloc)
            // put the tool away for actions where the hand is busy (eating, drinking, picking up, sleeping, climbing)
            int a = _drv ? _drv.CurrentAction : 0;
            bool hide = a == PlayerActions.Eat || a == PlayerActions.Drink || a == PlayerActions.Pickup || a == PlayerActions.Sleep ||
                        a == PlayerActions.WakeUp || a == PlayerActions.Craft || a == PlayerActions.GatherPlant || a == PlayerActions.Interact ||
                        (_drv && _drv.IsDead) || (_climb && _climb.IsClimbing);
            if (_held.activeSelf == hide) _held.SetActive(!hide);
        }
    }
}
