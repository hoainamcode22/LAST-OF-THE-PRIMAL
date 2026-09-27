using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.Player;
using PrimalFrontier.World;

namespace PrimalFrontier.Building
{
    /// <summary>
    /// Placing camp prefabs (campfire, shelter, storage, bedroll): a green / red ghost follows the camera aim,
    /// R or mouse wheel rotates, left click builds (the Build animation plays, then the object appears), right click
    /// or Esc cancels. Checks slope, overlap, water and distance. No modular base building (scope rule).
    /// </summary>
    public class BuildSystem : MonoBehaviour
    {
        public static BuildSystem Instance { get; private set; }
        public float maxDistance = 6f;
        public float maxSlope = 24f;
        public Material ghostValid, ghostInvalid;

        public bool Active => _item != null;
        public bool Valid { get; private set; }
        public string InvalidReason { get; private set; }
        public ItemDefinition Item => _item;

        ItemDefinition _item; GameObject _ghost; Renderer[] _ghostR; float _yaw; Vector3 _pos;
        PlayerInteraction _pi; PlayerInputReader _in; InventorySystem _inv; PlayerCombat _combat;
        public event System.Action<GameObject> Placed;

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        public void Bind(GameObject player)
        {
            _pi = player.GetComponent<PlayerInteraction>(); _inv = player.GetComponent<InventorySystem>(); _combat = player.GetComponent<PlayerCombat>();
            if (_combat) _combat.PrimaryBlocked = () => Active;
        }

        public void Begin(ItemDefinition item)
        {
            if (item == null || !item.IsPlaceable || _pi == null) return;
            Cancel();
            _item = item; _yaw = _pi.transform.eulerAngles.y;
            _ghost = Instantiate(item.placePrefab);
            _ghost.name = "Ghost_" + item.id;
            foreach (var mb in _ghost.GetComponentsInChildren<MonoBehaviour>()) mb.enabled = false;
            foreach (var c in _ghost.GetComponentsInChildren<Collider>()) Destroy(c);
            foreach (var l in _ghost.GetComponentsInChildren<Light>()) l.enabled = false;
            foreach (var ps in _ghost.GetComponentsInChildren<ParticleSystem>()) ps.gameObject.SetActive(false);
            foreach (var a in _ghost.GetComponentsInChildren<AudioSource>()) a.enabled = false;
            _ghostR = _ghost.GetComponentsInChildren<Renderer>();
            foreach (var r in _ghostR) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
            _pi.Suspended = true;
            PlayerInteraction.Notify("Place " + item.displayName + ": left click to build, R to rotate, right click to cancel.");
        }

        public void Cancel()
        {
            if (_ghost) Destroy(_ghost);
            _ghost = null; _item = null;
            if (_pi) _pi.Suspended = false;
        }

        void Update()
        {
            if (!Active) return;
            if (_in == null) _in = PlayerInputReader.Instance;
            if (_in == null || _inv == null) return;
            if (!_inv.Has(_item) || _inv.ActiveItem != _item) { Cancel(); return; }
            if (_in.CancelPressed) { Cancel(); return; }
            if (_in.RotatePressed) _yaw += 45f;
            if (Mathf.Abs(_in.HotbarScroll) > 0.01f) _yaw += Mathf.Sign(_in.HotbarScroll) * 15f;
            Evaluate();
            if (_in.AttackPressed)
            {
                if (Valid) Build(); else PlayerInteraction.Notify(InvalidReason ?? "Can't build here.");
            }
        }

        void Evaluate()
        {
            var cam = Camera.main; var player = _pi.transform;
            int mask = ~LayerMask.GetMask("Player", "Ignore Raycast");
            Vector3 p; Vector3 n = Vector3.up; bool hit = false;
            if (cam && Physics.Raycast(cam.ViewportPointToRay(new Vector3(0.5f, 0.5f)), out var h, maxDistance + 6f, mask, QueryTriggerInteraction.Ignore) &&
                Vector3.Distance(h.point, player.position) <= maxDistance)
            { p = h.point; n = h.normal; hit = true; }
            else
            {
                p = player.position + player.forward * 2.5f;
                if (Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out var d, 8f, mask, QueryTriggerInteraction.Ignore)) { p = d.point; n = d.normal; hit = true; }
            }
            _pos = p;
            _ghost.transform.SetPositionAndRotation(p, Quaternion.Euler(0, _yaw, 0));
            InvalidReason = null;
            if (!hit) InvalidReason = "Aim at the ground.";
            else if (Vector3.Angle(n, Vector3.up) > maxSlope) InvalidReason = "Too steep.";
            else if (p.y < 0.25f || WaterSource.IsNearFresh(p, 1.2f)) InvalidReason = "Not in the water.";
            else if (Vector3.Distance(p, player.position) < _item.placeRadius * 0.6f + 0.5f) InvalidReason = "Too close to you.";
            else
            {
                var cols = Physics.OverlapBox(p + Vector3.up * 0.9f, new Vector3(_item.placeRadius, 0.8f, _item.placeRadius), Quaternion.Euler(0, _yaw, 0), mask, QueryTriggerInteraction.Ignore);
                foreach (var c in cols) { if (c is TerrainCollider) continue; InvalidReason = "Something is in the way."; break; }
            }
            Valid = InvalidReason == null;
            var mat = Valid ? ghostValid : ghostInvalid;
            if (mat) foreach (var r in _ghostR) { var arr = r.sharedMaterials; for (int i = 0; i < arr.Length; i++) arr[i] = mat; r.sharedMaterials = arr; }
        }

        void Build()
        {
            var item = _item; Vector3 pos = _pos; Quaternion rot = Quaternion.Euler(0, _yaw, 0);
            Cancel();
            int hits = 0;
            _pi.DoLoop(PlayerActions.Build, "OnBuildHit", () => ++hits < 2, pos, 1.3f, null, () =>
            {
                if (hits < 2) return;                                        // interrupted: nothing used
                if (!_inv.Remove(item, 1)) return;
                var go = Spawn(item, pos, rot, null);
                GameEvents.Raise(GameEventType.StructurePlaced, item.id, 1, pos);
                Placed?.Invoke(go);
            });
        }

        /// <summary>also used by the save system</summary>
        public static GameObject Spawn(ItemDefinition item, Vector3 pos, Quaternion rot, string uid)
        {
            var go = Instantiate(item.placePrefab, pos, rot);
            go.name = item.placePrefab.name;
            var ps = go.GetOrAdd<PlacedStructure>();
            ps.itemId = item.id; ps.uid = string.IsNullOrEmpty(uid) ? System.Guid.NewGuid().ToString("N").Substring(0, 12) : uid;
            foreach (var it in go.GetComponentsInChildren<Interactable>()) it.SaveId = "build_" + ps.uid;
            return go;
        }
    }
}
