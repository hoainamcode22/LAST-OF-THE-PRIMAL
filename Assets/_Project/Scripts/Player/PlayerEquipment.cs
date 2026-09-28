using System;
using System.Collections.Generic;
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
    /// Tool models: handle along local Z, working end at -Z (the blade leaves the fist on the thumb side), origin at the
    /// grip. The per-item offset comes from WeaponData.gripPosition / gripEuler when the item has weapon data, else from
    /// the item's own grip fields. Two-handed weapons get an "OffHandGrip" child (WeaponData.offHandGrip) for the off-hand IK.
    /// Carried weapons: the other hotbar weapons ride on the body by WeaponData.carrySocket, at most one on the back
    /// (BackWeaponSocket: the most recently held back weapon, else the first in the hotbar; diagonal, handle over the right
    /// shoulder) and one at the hip (HipToolSocket: hanging, handle up at the right hip). One cached instance per item id.
    /// A weapon with an equip clip (sword) stays on its carry socket until the clip's grab frame (OnEquip "draw"); OnEquip
    /// "sheathe" puts the held weapon back on its socket. The hand copy and the carried copy are never visible together.
    /// </summary>
    public class PlayerEquipment : MonoBehaviour
    {
        public Animator animator;
        [Tooltip("VFX_CampfireLoop, scaled down on the torch head")] public GameObject torchFlamePrefab;
        public float torchLightRange = 9f, torchLightIntensity = 1.6f;

        [Header("Carried weapons (back / hip)")]
        [Tooltip("show the other hotbar weapons on the back and at the hip (WeaponData.carrySocket)")] public bool showCarried = true;
        [Tooltip("back item: offset in BackWeaponSocket space (m) after the model's centre is put on the socket (socket +Z = forward)")]
        public Vector3 backOffset = new Vector3(0f, 0f, -0.03f);
        [Tooltip("back item: extra rotation in BackWeaponSocket space (deg) on top of the base pose (handle over the right shoulder, flat on the back)")]
        public Vector3 backEuler;
        [Tooltip("hip item: offset in HipToolSocket space (m) after the handle end is hung on the socket (socket +X = the body's left, +Y = down)")]
        public Vector3 hipOffset = new Vector3(-0.03f, -0.02f, 0f);
        [Tooltip("hip item: extra rotation in HipToolSocket space (deg) on top of the base pose (hanging handle up, flat against the thigh)")]
        public Vector3 hipEuler;

        // base poses in the PlayerHierarchy socket frames, for models on the pipeline convention (handle +Z, working end -Z,
        // edge +Y): back socket +Y runs up the diagonal to the right shoulder -> handle there, blade flat on the back;
        // hip socket +Y points down -> handle up, blade flat against the thigh, edge forward
        static readonly Quaternion BackBase = Quaternion.LookRotation(Vector3.up, Vector3.left);
        static readonly Quaternion HipBase = Quaternion.LookRotation(Vector3.down, Vector3.forward);
        // models whose long axis is local Y (working end +Y): turn them onto the convention first
        static readonly Quaternion YLongFix = Quaternion.Inverse(Quaternion.FromToRotation(Vector3.forward, Vector3.down));
        const float DrawStartWindow = 0.15f;       // the equip clip must start this soon after the slot change, else the hand shows

        InventorySystem _inv; PlayerAnimationDriver _drv; PlayerHierarchy _hier; PlayerClimb _climb; CharacterAnimationEvents _ev;
        GameObject _held; ItemDefinition _heldItem; CampfireFx _flame; bool _deferred;

        class Carried { public GameObject go; public CarrySocket socket; public Bounds bounds; public bool hasBounds, yLong; }
        readonly Dictionary<string, Carried> _carried = new Dictionary<string, Carried>();
        readonly List<ItemDefinition> _recent = new List<ItemDefinition>(8);   // carried weapons last held, newest first
        Carried _back, _hip;
        bool _carryDirty = true;
        enum Holster { None, Drawing, Sheathed }
        Holster _holster; float _holsterT; int _holsterFrame; bool _drawSeen;

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
        /// <summary>the weapon shown on the back / at the hip now (null = none; the model may be hidden while climbing)</summary>
        public ItemDefinition CarriedBackItem { get; private set; }
        public ItemDefinition CarriedHipItem { get; private set; }
        public GameObject CarriedBackObject => _back != null ? _back.go : null;
        public GameObject CarriedHipObject => _hip != null ? _hip.go : null;
        /// <summary>the held item is on its carry socket instead of the hand (equip clip before its grab frame, or sheathed)</summary>
        public bool HeldOnCarrySocket => _holster != Holster.None;

        void Awake()
        {
            _inv = GetComponent<InventorySystem>(); _drv = GetComponent<PlayerAnimationDriver>();
            if (!animator) animator = _drv ? _drv.animator : GetComponentInChildren<Animator>();
            _hier = gameObject.GetOrAdd<PlayerHierarchy>();
            if (!_hier.animator) _hier.animator = animator;
            _ev = GetComponentInChildren<CharacterAnimationEvents>();
        }
        void OnEnable()
        {
            if (_inv) { _inv.ActiveSlotChanged += OnSlot; _inv.Changed += Refresh; }
            if (_ev) _ev.AnimationEventRaised += OnAnimEvent;
            _carryDirty = true;
        }
        void OnDisable()
        {
            if (_inv) { _inv.ActiveSlotChanged -= OnSlot; _inv.Changed -= Refresh; }
            if (_ev) _ev.AnimationEventRaised -= OnAnimEvent;
        }
        void Start() { _hier.Ensure(); Refresh(); }
        void OnValidate() { _carryDirty = true; }

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
            _carryDirty = true;                                  // the hotbar may have changed even when the hand did not
            var item = _inv ? _inv.ActiveItem : null;
            if (item != null && item.handPrefab == null) item = null;
            if (item == _heldItem) return;
            if (item != null && !_hier.SocketsReady) { _deferred = true; return; }    // hand bones not bound yet: LateUpdate retries
            _deferred = false;
            if (_held) Destroy(_held);
            _held = null; _flame = null; _heldItem = item; OffHandGrip = null; HeldSocket = null;
            _holster = Holster.None;
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
            var carry = CarryOf(item);
            if (carry != CarrySocket.None)
            {
                Remember(item);
                // drawn with an equip clip (sword from the back): the carried copy stays until the grab frame, the hand copy waits
                if (showCarried && wd.equipAction != PlayerActions.None)
                {
                    _holster = Holster.Drawing; _holsterT = Time.time; _holsterFrame = Time.frameCount; _drawSeen = false;
                    _held.SetActive(false);
                }
            }
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
            if (!_climb && (Time.frameCount & 31) == 0) TryGetComponent(out _climb);              // added at run time by Climbable (no editor alloc)
            int a = _drv ? _drv.CurrentAction : 0;
            bool dead = _drv && _drv.IsDead, climbing = _climb && _climb.IsClimbing;
            if (_holster != Holster.None) UpdateHolster(a);
            if (_carryDirty) RefreshCarried();
            // carried weapons: away while climbing, lying down (sleep, the opening wake-up) or dead
            bool hideCarried = climbing || dead || a == PlayerActions.Sleep || a == PlayerActions.WakeUp;
            SetVisible(_back, !hideCarried); SetVisible(_hip, !hideCarried);
            if (!_held) return;
            // put the tool away for actions where the hand is busy (eating, drinking, picking up, sleeping, climbing)
            bool hide = a == PlayerActions.Eat || a == PlayerActions.Drink || a == PlayerActions.Pickup || a == PlayerActions.Sleep ||
                        a == PlayerActions.WakeUp || a == PlayerActions.Craft || a == PlayerActions.GatherPlant || a == PlayerActions.Interact ||
                        dead || climbing || _holster != Holster.None;
            if (_held.activeSelf == hide) _held.SetActive(!hide);
        }

        // ------------------------------------------------------------------ draw / sheathe
        void OnAnimEvent(string fn, string param)
        {
            if (fn != "OnEquip" || !_heldItem) return;
            if (param == "draw") EndHolster();                                             // the hand grabs it: back -> hand
            else if (param == "sheathe" && _holster == Holster.None && CarryOf(_heldItem) != CarrySocket.None && showCarried)
            {
                _holster = Holster.Sheathed; _carryDirty = true;                          // the hand lets go: hand -> back / hip
            }
        }

        /// <summary>
        /// Drawing: ends at OnEquip "draw", or when the equip clip never started / was replaced (attack, block, dodge,
        /// action), or after the equip time: an attack never swings an invisible blade. Sheathed: ends when the weapon is used.
        /// </summary>
        void UpdateHolster(int a)
        {
            var wd = HeldWeaponData;
            if (!_held || !wd) { EndHolster(); return; }
            float t = Time.time - _holsterT;
            if (_holster == Holster.Drawing)
            {
                if (a == wd.equipAction) _drawSeen = true;
                bool other = a != PlayerActions.None && a != wd.equipAction;
                // the previous weapon's upper-body pose (bow aim) ends with its Unequip a frame later: only that waits
                if (other && (!PlayerActions.IsUpperBody(a) || a == PlayerActions.SwordBlock || t > DrawStartWindow)) EndHolster();
                else if (!_drawSeen && t > DrawStartWindow && Time.frameCount - _holsterFrame > 2) EndHolster();
                else if (t > Mathf.Max(0.2f, wd.equipTime) + 0.3f) EndHolster();
            }
            else if (PlayerActions.IsAttack(a) || a == PlayerActions.SwordBlock || (_drv && _drv.IsAttackingState)) EndHolster();
        }

        void EndHolster()
        {
            if (_holster == Holster.None) return;
            _holster = Holster.None; _carryDirty = true;
        }

        // ------------------------------------------------------------------ carried weapons
        static CarrySocket CarryOf(ItemDefinition item)
        {
            var wd = item ? item.weaponData : null;
            return wd && item.handPrefab ? wd.carrySocket : CarrySocket.None;
        }

        void Remember(ItemDefinition item)
        {
            _recent.Remove(item);
            if (_recent.Count >= 8) _recent.RemoveAt(_recent.Count - 1);
            _recent.Insert(0, item);
        }

        /// <summary>the item sits in a hotbar slot other than skipSlot</summary>
        bool InHotbar(ItemDefinition item, int skipSlot)
        {
            var slots = _inv.Slots; int n = Mathf.Min(_inv.hotbarSize, slots.Length);
            for (int i = 0; i < n; i++) if (i != skipSlot && !slots[i].IsEmptyOrNull() && slots[i].item == item) return true;
            return false;
        }

        ItemDefinition Pick(CarrySocket socket, int skipSlot)
        {
            for (int r = 0; r < _recent.Count; r++)
            {
                var it = _recent[r];
                if (it && CarryOf(it) == socket && InHotbar(it, skipSlot)) return it;
            }
            var slots = _inv.Slots; int n = Mathf.Min(_inv.hotbarSize, slots.Length);
            for (int i = 0; i < n; i++)
            {
                if (i == skipSlot || slots[i].IsEmptyOrNull()) continue;
                var it = slots[i].item;
                if (CarryOf(it) == socket) return it;
            }
            return null;
        }

        /// <summary>choose what rides on the back / hip (only when the hotbar, the hand or the holster state changed)</summary>
        void RefreshCarried()
        {
            if (!showCarried || !_inv || _inv.Slots == null) { _carryDirty = false; Show(ref _back, null, CarrySocket.Back); Show(ref _hip, null, CarrySocket.Hip); CarriedBackItem = CarriedHipItem = null; return; }
            if (!_hier.SocketsReady) return;                                    // bones not bound yet: stays dirty, next frame
            _carryDirty = false;
            ItemDefinition back = null, hip = null;
            // the active slot never gives a second copy: its item is in the hand, or rides on its socket while drawn / sheathed
            int skip = _heldItem && _inv.ActiveItem == _heldItem ? _inv.ActiveSlot : -1;
            if (_heldItem && _holster != Holster.None)
            {
                var s = CarryOf(_heldItem);
                if (s == CarrySocket.Back) back = _heldItem; else if (s == CarrySocket.Hip) hip = _heldItem;
            }
            if (!back) back = Pick(CarrySocket.Back, skip);
            if (!hip) hip = Pick(CarrySocket.Hip, skip);
            Show(ref _back, back, CarrySocket.Back);
            Show(ref _hip, hip, CarrySocket.Hip);
            CarriedBackItem = _back != null ? back : null;
            CarriedHipItem = _hip != null ? hip : null;
        }

        void Show(ref Carried current, ItemDefinition item, CarrySocket socket)
        {
            var next = item ? GetCarried(item, socket) : null;
            if (current != null && current != next && current.go) current.go.SetActive(false);
            current = next;
            if (current != null && current.go) Pose(current);                     // tuning fields may have changed
        }

        static void SetVisible(Carried c, bool on)
        {
            if (c != null && c.go && c.go.activeSelf != on) c.go.SetActive(on);
        }

        /// <summary>the cached carried copy of this item (created once: colliders stripped, shadows only for back items)</summary>
        Carried GetCarried(ItemDefinition item, CarrySocket socket)
        {
            if (_carried.TryGetValue(item.id, out var c) && c != null && c.go && c.socket == socket) return c;
            if (c != null && c.go) Destroy(c.go);
            var parent = socket == CarrySocket.Back ? _hier.BackWeaponSocket : _hier.HipToolSocket;
            if (!parent || !item.handPrefab) return null;
            var go = Instantiate(item.handPrefab, parent, false);
            go.name = "Carry_" + item.id;
            go.SetActive(false);
            foreach (var col in go.GetComponentsInChildren<Collider>(true)) Destroy(col);
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) Destroy(rb);
            var shadows = socket == CarrySocket.Back ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = shadows;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = gameObject.layer;
            c = new Carried { go = go, socket = socket };
            c.hasBounds = WeaponHitbox.MeasureLocalBounds(go.transform, out c.bounds);
            c.yLong = c.hasBounds && c.bounds.size.y > c.bounds.size.z * 1.25f;
            _carried[item.id] = c;
            return c;
        }

        /// <summary>back: the model's centre on the socket; hip: the handle end on the socket, hanging; then the tuning offset</summary>
        void Pose(Carried c)
        {
            bool back = c.socket == CarrySocket.Back;
            var t = c.go.transform;
            Quaternion rot = Quaternion.Euler(back ? backEuler : hipEuler) * (back ? BackBase : HipBase);
            if (c.yLong) rot *= YLongFix;
            Vector3 anchor = Vector3.zero;
            if (c.hasBounds)
            {
                var b = c.bounds;
                anchor = back ? b.center
                       : c.yLong ? new Vector3(b.center.x, b.min.y, b.center.z)        // handle end of a Y-long model is -Y
                       : new Vector3(b.center.x, b.center.y, b.max.z);
            }
            t.localRotation = rot;
            t.localPosition = (back ? backOffset : hipOffset) - rot * Vector3.Scale(anchor, t.localScale);
        }
    }
}
