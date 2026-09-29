using UnityEngine;
using PrimalFrontier.Audio;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.VFX;

namespace PrimalFrontier.World
{
    /// <summary>
    /// A bunch of fruit high on a Climbable tree. Picked from the trunk (Harvest_Fruit): the fruit that fits goes into the
    /// pack (InventorySystem.Add returns what did NOT fit), the picked fruit meshes (children) disappear, leaves and a
    /// pickup sound play; fruit that did not fit stays on the branch. Emptied bunches grow back after regrowHours of game
    /// time (sleeping counts): hidden for the first half, then small green fruit that swell and ripen. No Update:
    /// ResourceManager ticks it. Saved through ResourceSaveSection (fruit left + regrow time).
    /// </summary>
    public class FruitCluster : MonoBehaviour
    {
        public ItemDefinition item;
        [Min(1)] public int amount = 3;
        public float regrowHours = 30f;
        [Tooltip("stable id for the save file (generated from the position when empty)")] [SerializeField] string saveId;

        public double EmptyUntil { get; private set; } = -1;
        /// <summary>fruit still hanging (0..amount)</summary>
        public int Left { get; private set; }
        public bool Ripe => EmptyUntil < 0 && Left > 0;
        /// <summary>0 while freshly picked, rising to 1 over the second half of the regrow time</summary>
        public float Grow01 { get; private set; } = 1f;
        public string SaveId
        {
            get
            {
                if (string.IsNullOrEmpty(saveId)) { var p = transform.position; saveId = $"fruit@{Mathf.RoundToInt(p.x * 10)},{Mathf.RoundToInt(p.y * 10)},{Mathf.RoundToInt(p.z * 10)}"; }
                return saveId;
            }
            set => saveId = value;
        }

        Renderer[] _fruit; Vector3[] _scale; Material[][] _mats; bool _init;

        void Awake() => Init();
        void OnEnable() { Init(); ResourceManager.Register(this); }
        void OnDisable() { ResourceManager.Unregister(this); }

        void Init()
        {
            if (_init) return; _init = true;
            _fruit = GetComponentsInChildren<Renderer>(true);
            _scale = new Vector3[_fruit.Length]; _mats = new Material[_fruit.Length][];
            for (int i = 0; i < _fruit.Length; i++) { _scale[i] = _fruit[i].transform.localScale; _mats[i] = _fruit[i].sharedMaterials; }
            Left = amount;
        }

        /// <summary>pick the fruit into the inventory; false if nothing to pick or the pack is full</summary>
        public bool Harvest(InventorySystem inv)
        {
            Init();
            if (!Ripe || item == null || inv == null) return false;
            int want = Left;
            int notFit = inv.Add(item, want);                 // Add returns how many did NOT fit
            int added = want - notFit;
            if (added <= 0) { Player.PlayerInteraction.Notify("Your pack is full."); return false; }
            Left -= added;
            if (Left <= 0) { Left = 0; EmptyUntil = GameClock.Now + GameClock.Hours(regrowHours); Grow01 = 0f; }
            else Player.PlayerInteraction.Notify("Your pack is full. Some fruit is still on the branch.");
            ApplyLook();
            VfxPool.Instance.Play(VfxId.Leaves, transform.position, Vector3.up);
            SfxPlayer.Instance.Play(SfxId.Pickup, transform.position, 0.8f);
            SfxPlayer.Instance.Play(SfxId.LeafRustle, transform.position, 0.7f);
            GameEvents.Raise(GameEventType.FruitHarvested, item.id, added, transform.position);
            return true;
        }

        public void Regrow()
        {
            Init();
            Left = amount; EmptyUntil = -1; Grow01 = 1f;
            ApplyLook();
        }

        /// <summary>save / load</summary>
        public void Restore(int left, double emptyUntil)
        {
            Init();
            Left = Mathf.Clamp(left, 0, amount);
            EmptyUntil = Left > 0 ? -1 : (emptyUntil >= 0 ? emptyUntil : GameClock.Now + GameClock.Hours(regrowHours));
            Grow01 = Left > 0 ? 1f : 0f;
            ApplyLook();
        }

        /// <summary>ResourceManager, twice a second: regrow when due, otherwise the growing look</summary>
        internal void Tick(double now)
        {
            if (EmptyUntil < 0) return;
            if (now >= EmptyUntil) { Regrow(); return; }
            double dur = GameClock.Hours(regrowHours), start = EmptyUntil - dur;
            float g = Mathf.Clamp01((float)((now - start - dur * 0.5) / (dur * 0.5)));
            if (Mathf.Abs(g - Grow01) >= 0.02f) { Grow01 = g; ApplyLook(); }
        }

        /// <summary>ripe: one mesh per fruit left; regrowing: small green fruit that swell; freshly picked: none</summary>
        void ApplyLook()
        {
            int n = _fruit.Length;
            int show = EmptyUntil < 0 ? Mathf.CeilToInt(n * (Left / (float)Mathf.Max(1, amount)) - 1e-3f) : (Grow01 > 0f ? n : 0);
            var unripe = ResourceManager.UnripeMaterial;
            for (int i = 0; i < n; i++)
            {
                var r = _fruit[i]; if (!r) continue;
                r.enabled = i < show;
                bool growing = EmptyUntil >= 0;
                r.transform.localScale = _scale[i] * (growing ? Mathf.Lerp(0.35f, 0.9f, Grow01) : 1f);
                var src = _mats[i];
                if (growing && unripe)
                {
                    var m = (Material[])src.Clone();
                    for (int k = 0; k < m.Length; k++) if (m[k] && m[k].name.Contains("Fruit") && !m[k].name.Contains("Stem")) m[k] = unripe;
                    r.sharedMaterials = m;
                }
                else if (r.sharedMaterials != src) r.sharedMaterials = src;
            }
        }

#if UNITY_EDITOR
        void OnDrawGizmos() { Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.9f); Gizmos.DrawWireSphere(transform.position, 0.25f); }
#endif
    }
}
