using System.Collections.Generic;
using UnityEngine;
using PrimalFrontier.Core;

namespace PrimalFrontier.World
{
    /// <summary>
    /// A dead animal placed in the scene by a zone builder (BONE, Phase 2: Bone / Carcass Valley), not killed in play.
    /// Data for AI and other systems: every placed body carries one (<see cref="All"/>), with its decay <see cref="stage"/>.
    /// A Fresh body also has a <see cref="Carcass"/> on the same object: Start fills it once with the preset loot
    /// (Carcass.Setup), so it can be butchered like any kill (knife: meat, hide, bone; bare hands: some meat). What is left
    /// is saved in the "zone_carcasses" save section by SaveId; a butchered body stays gone after loading.
    /// Older stages are props only (no loot): scavenger / predator points, smell of old meat for AI to decide.
    /// </summary>
    public class ZoneCarcass : MonoBehaviour
    {
        public enum Stage { Fresh, Rotting, Old, Ancient }
        public Stage stage = Stage.Fresh;
        [Tooltip("stable id (save, AI lookups)")] public string id;
        [Tooltip("creature the body was (DinosaurDefinition id)")] public string creatureId;
        public string displayName = "carcass";
        [Header("Fresh: loot put on the Carcass at start")]
        public int meat = 3;
        public int hide = 2, bone = 2;
        [Tooltip("game hours before the untouched body sinks (large = stays until butchered)")] public float expireHours = 100000f;
        [Tooltip("xz radius of the body (scavengers gather around it)")] public float radius = 2f;

        /// <summary>every placed body in the loaded scenes (active or not), set up in Awake</summary>
        public static readonly List<ZoneCarcass> All = new List<ZoneCarcass>();
        public Carcass Body { get; private set; }
        public bool HasMeat => Body && Body.isActiveAndEnabled && !Body.Sinking && Body.meat > 0;

        static readonly Dictionary<string, Entry> _pending = new Dictionary<string, Entry>();
        static Section _section;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics() { All.Clear(); _pending.Clear(); _section = null; }
        bool _ready;

        void Awake()
        {
            if (!All.Contains(this)) All.Add(this);
            if (_section == null) _section = new Section();
            SaveSystem.RegisterSection(_section);
        }
        void OnDestroy() { All.Remove(this); }

        void Start()
        {
            if (stage != Stage.Fresh) return;
            Body = GetComponent<Carcass>();
            if (!Body) return;
            Body.expireHours = expireHours;
            Body.Setup(displayName, creatureId, meat, hide, bone);
            _ready = true;
            if (!string.IsNullOrEmpty(id) && _pending.TryGetValue(id, out var e)) { _pending.Remove(id); Apply(e); }
        }

        void Apply(Entry e)
        {
            if (!Body) return;
            if (e.gone) { gameObject.SetActive(false); return; }
            Body.RestoreLeft(e.meat, e.hide, e.bone, -1);
        }

        [System.Serializable] class Entry { public string id; public int meat, hide, bone; public bool gone; }
        [System.Serializable] class Data { public List<Entry> list = new List<Entry>(); }

        sealed class Section : ISaveSection
        {
            public string SectionKey => "zone_carcasses";
            public string CaptureSection()
            {
                var d = new Data();
                foreach (var z in All)
                {
                    if (!z || z.stage != Stage.Fresh || string.IsNullOrEmpty(z.id) || !z._ready || !z.Body) continue;
                    d.list.Add(new Entry { id = z.id, meat = z.Body.meat, hide = z.Body.hide, bone = z.Body.bone, gone = !z.gameObject.activeInHierarchy || z.Body.Sinking || z.Body.IsEmpty });
                }
                return d.list.Count > 0 ? JsonUtility.ToJson(d) : null;
            }
            public void RestoreSection(string json)
            {
                if (string.IsNullOrEmpty(json)) return;
                var d = JsonUtility.FromJson<Data>(json); if (d == null || d.list == null) return;
                foreach (var e in d.list)
                {
                    if (e == null || string.IsNullOrEmpty(e.id)) continue;
                    ZoneCarcass live = null;
                    foreach (var z in All) if (z && z.id == e.id && z._ready) { live = z; break; }
                    if (live) live.Apply(e); else _pending[e.id] = e;
                }
            }
        }
    }
}
