using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Items;
using PrimalFrontier.VFX;

namespace PrimalFrontier.Player
{
    /// <summary>
    /// Shows the active hotbar item in the right hand. The grip frame comes from the humanoid hand bones
    /// (across the knuckles and along the fingers), so any Humanoid rig holds the tools the same way.
    /// Tool models: handle along local -Z... blade/head towards local +Y, origin at the grip.
    /// </summary>
    public class PlayerEquipment : MonoBehaviour
    {
        public Animator animator;
        [Tooltip("VFX_CampfireLoop, scaled down on the torch head")] public GameObject torchFlamePrefab;
        public float torchLightRange = 9f, torchLightIntensity = 1.6f;

        InventorySystem _inv; PlayerAnimationDriver _drv;
        GameObject _held; ItemDefinition _heldItem; CampfireFx _flame;
        Transform _hand; Quaternion _gripRot = Quaternion.identity; Vector3 _gripPos;
        public ItemDefinition HeldItem => _heldItem;
        public GameObject HeldObject => _held;
        public bool TorchLit => _flame != null && _held != null && _held.activeSelf;

        void Awake()
        {
            _inv = GetComponent<InventorySystem>(); _drv = GetComponent<PlayerAnimationDriver>();
            if (!animator) animator = _drv ? _drv.animator : GetComponentInChildren<Animator>();
        }
        void OnEnable() { if (_inv) { _inv.ActiveSlotChanged += OnSlot; _inv.Changed += Refresh; } }
        void OnDisable() { if (_inv) { _inv.ActiveSlotChanged -= OnSlot; _inv.Changed -= Refresh; } }
        void Start() { ComputeGrip(); Refresh(); }

        void OnSlot(int _) => Refresh();

        void ComputeGrip()
        {
            if (!animator || !animator.isHuman) return;
            _hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            var idx = animator.GetBoneTransform(HumanBodyBones.RightIndexProximal);
            var lit = animator.GetBoneTransform(HumanBodyBones.RightLittleProximal);
            var mid = animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            if (!_hand || !idx || !lit || !mid) return;
            Vector3 across = idx.position - lit.position;          // thumb side
            Vector3 fingers = mid.position - _hand.position;
            Vector3 up = Vector3.ProjectOnPlane(fingers, across).normalized;
            Quaternion world = Quaternion.LookRotation(-across.normalized, up);
            _gripRot = Quaternion.Inverse(_hand.rotation) * world;
            // palm centre: between the wrist and the finger roots, slightly into the palm
            Vector3 palmN = Vector3.Cross(across, fingers).normalized;        // right hand: points out of the palm
            Vector3 p = Vector3.Lerp(_hand.position, mid.position, 0.75f) + palmN * 0.015f;
            _gripPos = _hand.InverseTransformPoint(p);
        }

        public void Refresh()
        {
            var item = _inv ? _inv.ActiveItem : null;
            if (item != null && item.handPrefab == null) item = null;
            if (item == _heldItem) return;
            if (_held) Destroy(_held);
            _held = null; _flame = null; _heldItem = item;
            if (item == null) return;
            if (_hand == null) ComputeGrip();
            var parent = _hand ? _hand : transform;
            _held = Instantiate(item.handPrefab, parent, false);
            _held.name = "Held_" + item.id;
            foreach (var c in _held.GetComponentsInChildren<Collider>()) Destroy(c);
            foreach (var r in _held.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            _held.transform.localRotation = _gripRot * Quaternion.Euler(item.gripEuler);
            _held.transform.localPosition = _gripPos + _gripRot * item.gripPosition;
            _held.layer = gameObject.layer; foreach (Transform t in _held.GetComponentsInChildren<Transform>()) t.gameObject.layer = gameObject.layer;
            if (item.lightRange > 0f && torchFlamePrefab) AddFlame(item);
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
            if (!_held) return;
            // put the tool away for actions where the hand is busy (eating, drinking, picking up, sleeping)
            int a = _drv ? _drv.CurrentAction : 0;
            bool hide = a == PlayerActions.Eat || a == PlayerActions.Drink || a == PlayerActions.Pickup || a == PlayerActions.Sleep ||
                        a == PlayerActions.WakeUp || a == PlayerActions.Craft || a == PlayerActions.GatherPlant || a == PlayerActions.Interact || (_drv && _drv.IsDead);
            if (_held.activeSelf == hide) _held.SetActive(!hide);
        }
    }
}
