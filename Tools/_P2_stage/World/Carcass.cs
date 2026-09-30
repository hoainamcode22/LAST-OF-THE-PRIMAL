using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Audio;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.Player;
using PrimalFrontier.VFX;

namespace PrimalFrontier.World
{
    /// <summary>
    /// The body of a killed creature (added at death by DinosaurController / AmbientCreature). E (or hold E) starts a
    /// loop of cuts on the gather animation; every cut gives one item in turn meat -> hide -> bone. A cutting tool gets
    /// everything, one item per cut. Bare hands only tear off meat, half of it (rounded up) and slower
    /// (handCutsPerItem cuts per piece). When empty, or expireHours of game time after death, the body sinks into the
    /// ground and the creature object is switched off (not destroyed: the spawner owns it).
    /// The interaction distance is measured to the body's long axis, so big animals can be cut from any side.
    /// </summary>
    public class Carcass : Interactable
    {
        public string displayName = "carcass";
        [Tooltip("creature id (CarcassButchered event)")] public string creatureId;
        [Header("Loot left")]
        public int meat;
        public int hide, bone;
        [Tooltip("bare hands: cuts per piece of meat (a cutting tool gives one item per cut)")] [Min(1)] public int handCutsPerItem = 2;
        public float toolWear = 0.5f;
        [Header("Removal")]
        [Tooltip("in-game hours before an untouched / unfinished carcass sinks")] public float expireHours = 24f;
        public float sinkDepth = 1.5f, sinkSeconds = 6f;
        [Tooltip("switched off once the carcass has sunk (the creature root). Empty = this object")] public GameObject body;

        public int Remaining => meat + hide + bone;
        public bool IsEmpty => Remaining <= 0;
        public bool Sinking => _sinking;
        /// <summary>game clock time the body starts to sink (save)</summary>
        public double ExpireAt => _expireAt;
        /// <summary>carcasses in the world (perception: they smell)</summary>
        public static readonly System.Collections.Generic.List<Carcass> All = new System.Collections.Generic.List<Carcass>();
        float _nextScent;
        /// <summary>meat bare hands can still tear off: half of the original meat (rounded up) minus what was taken</summary>
        public int HandMeatLeft => Mathf.Min(meat, Mathf.Max(0, (_meat0 + 1) / 2 - _meatTaken));

        public override float Range => 1.5f;
        public override float Radius { get { Shaped(); return _halfWidth; } }
        /// <summary>the point of the body's long axis nearest to the player (where the player kneels and cuts)</summary>
        public override Vector3 FocusPoint
        {
            get
            {
                Shaped();
                Vector3 a = transform.TransformPoint(_axisA), b = transform.TransformPoint(_axisB), p = (a + b) * 0.5f;
                var pl = PlayerLocator.Position;
                if (pl.HasValue)
                {
                    Vector3 ab = b - a; float l2 = ab.sqrMagnitude;
                    p = l2 > 1e-4f ? a + ab * Mathf.Clamp01(Vector3.Dot(pl.Value - a, ab) / l2) : a;
                }
                p.y = transform.position.y + _focusHeight;
                return p;
            }
        }

        static readonly string[] KindIds = { "raw_meat", "hide", "bone" };
        const string HandsOnly = "Needs a cutting tool for full yield", HandsDone = "Needs a cutting tool for the hide and bones";
        readonly ItemDefinition[] _items = new ItemDefinition[3];
        int _meat0, _meatTaken, _next, _handCuts, _total0;
        double _expireAt = -1; bool _sinking; float _sinkT; Vector3 _sinkFrom;
        Vector3 _axisA, _axisB; float _halfWidth = 0.5f, _focusHeight = 0.6f; bool _shaped;
        string _prompt;

        /// <summary>fill the carcass with the creature's loot (called once at death). Kinds missing from the item database are skipped.</summary>
        public void Setup(string name, string id, int meatCount, int hideCount, int boneCount)
        {
            displayName = string.IsNullOrEmpty(name) ? "carcass" : name; creatureId = id;
            _prompt = "Butcher " + displayName;
            var db = ItemDatabase.Instance;
            for (int i = 0; i < 3; i++) _items[i] = db ? db.Item(KindIds[i]) : null;
            meat = _items[0] ? Mathf.Max(0, meatCount) : 0;
            hide = _items[1] ? Mathf.Max(0, hideCount) : 0;
            bone = _items[2] ? Mathf.Max(0, boneCount) : 0;
            _meat0 = meat; _meatTaken = 0; _next = 0; _handCuts = 0; _total0 = Remaining;
            _expireAt = GameClock.Now + GameClock.Hours(expireHours);
            _sinking = false; _shaped = false; Shaped();
            if (!body) body = gameObject;
            enabled = true;                                // nothing to take: the body just lies there until it expires
            _nextScent = 0f;
        }

        /// <summary>save / load: what is left on the body and when it sinks (after Setup)</summary>
        public void RestoreLeft(int meatLeft, int hideLeft, int boneLeft, double expireAt)
        {
            meat = Mathf.Clamp(meatLeft, 0, meat); hide = Mathf.Clamp(hideLeft, 0, hide); bone = Mathf.Clamp(boneLeft, 0, bone);
            _meatTaken = Mathf.Max(0, _meat0 - meat);
            if (expireAt > 0) _expireAt = expireAt;
            if (IsEmpty) BeginSink();
        }

        /// <summary>a predator ate from its kill (AI hunting): up to n pieces of meat are gone, at least one stays for the player / scavengers</summary>
        public void PredatorFeed(int n)
        {
            if (n <= 0 || _sinking) return;
            int k = Mathf.Min(n, Mathf.Max(0, meat - 1));
            meat -= k; _meatTaken += k;
        }

        // ---- Phase 2 (AI): scavenging hooks (pteranodons land by a body, a raptor feeds at it; WildlifeRoutine)
        /// <summary>scavengers at this body right now (flyers on the ground next to it, a feeding raptor)</summary>
        public int Scavengers { get; private set; }
        public void ScavengerArrived() { Scavengers++; }
        public void ScavengerLeft() { Scavengers = Mathf.Max(0, Scavengers - 1); }
        /// <summary>a scavenger tore off up to n pieces of meat (at least one stays for the player); returns how many</summary>
        public int Scavenge(int n)
        {
            if (n <= 0 || _sinking) return 0;
            int k = Mathf.Min(n, Mathf.Max(0, meat - 1));
            meat -= k; _meatTaken += k; return k;
        }
        /// <summary>the nearest body within r of p that is not sinking (meat left or not: bones draw scavengers too); null = none</summary>
        public static Carcass NearestBody(Vector3 p, float r)
        {
            Carcass best = null; float bq = r * r;
            for (int i = 0; i < All.Count; i++)
            {
                var c = All[i]; if (!c || c._sinking || !c.isActiveAndEnabled) continue;
                float q = (c.transform.position - p).sqrMagnitude;
                if (q < bq) { bq = q; best = c; }
            }
            return best;
        }

        protected override void OnEnable() { base.OnEnable(); if (!All.Contains(this)) All.Add(this); }
        protected override void OnDisable() { base.OnDisable(); All.Remove(this); }

        void Update()
        {
            if (_sinking) { Sink(); return; }
            if (_expireAt >= 0 && GameClock.Now >= _expireAt) BeginSink();
            // a body smells of meat and blood, strongest while fresh (predators with a meat drive come to it)
            if (Time.time >= _nextScent && Remaining > 0)
            {
                var pc = AI.PerceptionConfig.Instance;
                _nextScent = Time.time + pc.carcassScentInterval;
                float age01 = _expireAt > 0 ? Mathf.Clamp01(1f - (float)((_expireAt - GameClock.Now) / GameClock.Hours(Mathf.Max(0.1f, expireHours)))) : 0f;
                Stimuli.Scent(transform.position + Vector3.up * 0.5f, Mathf.Lerp(pc.carcassScentFresh, pc.carcassScentOld, age01), ScentKind.Meat, StimulusSource.World);
            }
        }

        // ------------------------------------------------------------------ interaction
        public override string GetPrompt(PlayerInteraction p, out string sub)
        {
            sub = null;
            if (_sinking || IsEmpty) return null;
            if (!p.HasTool(ToolKind.Cut, out _)) sub = HandMeatLeft > 0 ? HandsOnly : HandsDone;
            return _prompt ??= "Butcher " + displayName;
        }

        public override bool CanInteract(PlayerInteraction p) => !_sinking && !IsEmpty && (HandMeatLeft > 0 || p.HasTool(ToolKind.Cut, out _));
        /// <summary>the HUD shows it as "Hold E: Butcher"; a tap works too</summary>
        public override string HoldPrompt(PlayerInteraction p) => CanInteract(p) ? "Butcher" : null;
        public override void HoldInteract(PlayerInteraction p) => Interact(p);

        public override void Interact(PlayerInteraction p)
        {
            if (!CanInteract(p)) return;
            Vector3 at = FocusPoint;
            bool knife = p.HasTool(ToolKind.Cut, out _);
            // same gather clip / event as plants and fibre (ResourceNode with a Cut tool); the period is only the fallback cadence
            p.DoLoop(PlayerActions.GatherPlant, "OnGatherHit", () => Cut(p, at), at, knife ? 1.1f : 1.4f, this);
        }

        /// <summary>one cut (tests / tools); false = stop butchering</summary>
        public bool Hit(PlayerInteraction p) => Cut(p, FocusPoint);

        bool Cut(PlayerInteraction p, Vector3 at)
        {
            if (p == null || _sinking || IsEmpty) return false;
            bool knife = p.HasTool(ToolKind.Cut, out var tool);
            int kind = NextKind(knife);
            if (kind < 0) { PlayerInteraction.Notify("You need a cutting tool for the hide and bones."); return false; }
            var item = _items[kind];
            if (p.Inventory.SpaceFor(item) <= 0) { PlayerInteraction.Notify(p.Inventory.IsOverweight ? "Too heavy to carry more." : "Inventory full."); return false; }
            CutFx(at);
            if (!knife && ++_handCuts < handCutsPerItem) return true;       // tearing by hand is slower
            _handCuts = 0;
            p.GiveOrDrop(item, 1);
            if (kind == 0) { meat--; _meatTaken++; } else if (kind == 1) hide--; else bone--;
            _next = (kind + 1) % 3;
            if (tool != null && tool.HasDurability && p.Inventory.WearActive(toolWear)) PlayerInteraction.Notify(tool.displayName + " broke!");
            if (IsEmpty)
            {
                GameEvents.Raise(GameEventType.CarcassButchered, creatureId, _total0, transform.position);
                BeginSink();
                return false;
            }
            if (!CanInteract(p)) { PlayerInteraction.Notify("Only a cutting tool can take the hide and bones."); return false; }
            return true;
        }

        /// <summary>next kind in the cycle meat -> hide -> bone that is left and allowed (-1 = none)</summary>
        int NextKind(bool knife)
        {
            for (int k = 0; k < 3; k++)
            {
                int i = (_next + k) % 3;
                int left = i == 0 ? (knife ? meat : HandMeatLeft) : knife ? (i == 1 ? hide : bone) : 0;
                if (left > 0) return i;
            }
            return -1;
        }

        void CutFx(Vector3 at)
        {
            var pc = AI.PerceptionConfig.Instance;
            Stimuli.Noise(at, pc.butcherLoudness, NoiseTag.Gather, StimulusSource.Player);
            Stimuli.Scent(at, pc.butcherBlood, ScentKind.Blood, StimulusSource.World);
            VfxPool.Instance.Play(VfxId.BloodSpray, at, Vector3.up, null, 0.35f);   // becomes dust when blood is off
            BloodFX.Drip(at, 0.5f, transform);
            SfxPlayer.Instance.Play(SfxId.HitFlesh, at, 0.4f);
        }

        // ------------------------------------------------------------------ removal
        void BeginSink()
        {
            if (_sinking) return;
            _sinking = true; _sinkT = 0f; _sinkFrom = transform.position;
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
        }

        void Sink()
        {
            _sinkT += Time.deltaTime;
            float k = Mathf.Clamp01(_sinkT / Mathf.Max(0.1f, sinkSeconds));
            transform.position = _sinkFrom + Vector3.down * (sinkDepth * k * k);
            if (k < 1f) return;
            enabled = false;
            (body ? body : gameObject).SetActive(false);
        }

        // ------------------------------------------------------------------ shape (reach measured to the body)
        void Shaped()
        {
            if (_shaped) return;
            _shaped = true;
            Vector3 c, ext;       // local centre, half extents
            var cap = GetComponent<CapsuleCollider>();
            if (cap)
            {
                float r = cap.radius, h = Mathf.Max(cap.height * 0.5f, r);
                c = cap.center;
                ext = cap.direction == 0 ? new Vector3(h, r, r) : cap.direction == 1 ? new Vector3(r, h, r) : new Vector3(r, r, h);
            }
            else if (LocalBounds(out c, out ext))
            {
                if (!GetComponentInChildren<Collider>())
                {
                    // no collider at all (some ambient creatures): a trigger box so the body is still a physical target
                    var box = gameObject.AddComponent<BoxCollider>(); box.isTrigger = true; box.center = c; box.size = ext * 2f;
                }
            }
            else { c = Vector3.up * 0.4f; ext = Vector3.one * 0.4f; }
            bool alongZ = ext.z >= ext.x;
            float half = alongZ ? ext.z : ext.x, w = alongZ ? ext.x : ext.z;
            Vector3 dir = (alongZ ? Vector3.forward : Vector3.right) * Mathf.Max(0f, half - w);
            _axisA = c - dir; _axisB = c + dir;
            Vector3 s = transform.lossyScale;
            _halfWidth = Mathf.Clamp(w * Mathf.Abs(alongZ ? s.x : s.z), 0.3f, 2.5f);
            _focusHeight = Mathf.Clamp(c.y * Mathf.Abs(s.y), 0.3f, 1.1f);
        }

        bool LocalBounds(out Vector3 c, out Vector3 ext)
        {
            c = ext = Vector3.zero;
            bool any = false; Bounds b = default;
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (!(r is SkinnedMeshRenderer) && !(r is MeshRenderer)) continue;    // skip attached blood / particle effects
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (!any) return false;
            c = transform.InverseTransformPoint(b.center);
            Vector3 e = b.extents, s = transform.lossyScale;
            ext = new Vector3(Proj(transform.right, e) / Mathf.Max(1e-4f, Mathf.Abs(s.x)),
                              Proj(transform.up, e) / Mathf.Max(1e-4f, Mathf.Abs(s.y)),
                              Proj(transform.forward, e) / Mathf.Max(1e-4f, Mathf.Abs(s.z)));
            return true;
        }

        static float Proj(Vector3 axis, Vector3 e) => Mathf.Abs(axis.x) * e.x + Mathf.Abs(axis.y) * e.y + Mathf.Abs(axis.z) * e.z;
    }
}
