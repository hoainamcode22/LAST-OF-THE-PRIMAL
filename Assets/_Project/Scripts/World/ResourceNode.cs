using System.Collections.Generic;
using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Combat;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.Player;

namespace PrimalFrontier.World
{
    /// <summary>
    /// The one gatherable node (stones, rocks, branches, logs, driftwood, fibre plants, grass, ferns, berry bushes, fallen
    /// fruit). Its ResourceDefinition holds the numbers: full amount, tool efficiency, respawn, looks, feedback.
    /// Interact (E / GATHER): one action per press, more while the key is held; each animation hit rolls the held tool's
    /// efficiency (GatheringSystem) and gives whole units. Bare-hand punches (IDamageable, HitInfo.unarmed) give a small
    /// result too. Looks: full, damaged (smaller, settled, tilted), depleted (hidden with a puff, rubble, stripped or
    /// mined), regrowing (grows back in the second half of the respawn time). No Update: ResourceManager runs the timers
    /// and the hit wobble for all nodes. Nodes without a definition (older builders) keep working from the legacy fields.
    /// </summary>
    public class ResourceNode : Interactable, IDamageable
    {
        [Tooltip("the data: yield, tools, respawn, looks. Empty = the legacy fields below")] public ResourceDefinition definition;
        [Header("Legacy fields (copied from the definition at start when one is set)")]
        public string displayName = "Resource";
        public string verb = "Gather";
        public ItemDefinition yieldItem;
        [Min(1)] public int yieldPerHit = 1;
        [Tooltip("units in this node when full (the builder rolls it in the definition's range)")] [Min(1)] public int charges = 4;
        public ItemDefinition bonusItem;
        [Range(0, 1)] public float bonusChance;
        [Tooltip("tool needed at all (None = bare hands work)")] public ToolKind requiredTool = ToolKind.None;
        [Tooltip("tool that works it best")] public ToolKind fasterTool = ToolKind.None;
        [Tooltip("animation with bare hands / with the fitting tool")] public int handAction = PlayerActions.GatherPlant;
        public int toolAction = PlayerActions.GatherPlant;
        [Tooltip("in-game hours until it grows back")] public float regrowHours = 18f;
        public bool hideWhenEmpty = true;
        public float toolWear = 1f;
        public float radius = 0.4f;
        [Header("Feedback")]
        [Tooltip("the node wobbles on every hit")] public float hitWobble = 0.07f;
        [Tooltip("fish shoals: water depth over the node (the ripple / fin cue plays at the surface); set by the builder")] public float cueHeight;
        [Tooltip("legacy: piles get smaller as they are used up (1 = no shrink)")] [Range(0.4f, 1f)] public float emptyScale = 0.7f;

        public enum NodeState { Full, Damaged, Depleted, Regrowing }

        public int Remaining { get; private set; }
        public double EmptyUntil { get; private set; } = -1;
        public bool IsEmpty => Remaining <= 0;
        public float Health01 => charges > 0 ? Mathf.Clamp01(Remaining / (float)charges) : 0f;
        public NodeState State => !IsEmpty ? (Remaining < charges ? NodeState.Damaged : NodeState.Full) : Regrow01 > 0f ? NodeState.Regrowing : NodeState.Depleted;
        /// <summary>0 while freshly depleted, rising to 1 over the second half of the respawn time (the node grows back)</summary>
        public float Regrow01 { get; private set; }
        public ResourceDefinition Def { get { if (_def == null) Init(); return _def; } }
        public override float Radius => radius;
        public override float Range => 1.9f;
        /// <summary>IDamageable: bare-hand strikes reach it while it has something to give</summary>
        public bool IsAlive => !IsEmpty && Def.punchable;

        ResourceDefinition _def; bool _init;
        Renderer[] _renderers; Collider[] _colliders;
        Vector3 _baseScale, _basePos; Quaternion _baseRot; float _height; Vector3 _tiltAxis;
        float _progress;                     // fractional work towards the next unit (slow hands, punches)
        internal float wobble;               // ResourceManager animates it
        ProduceSlot[] _produce;              // berries: material slots / renderers that show the crop
        string _subName, _subSlow, _subNeeds, _subEmpty, _subFaster;

        struct ProduceSlot { public Renderer r; public int slot; public Material ripe, empty; public bool whole; }

        void Awake() => Init();

        void Init()
        {
            if (_init) return; _init = true;
            if (definition) { _def = definition; ApplyDefinition(); }
            else { _def = LegacyDefinition(this); charges = Mathf.Max(1, charges * yieldPerHit); yieldPerHit = 1; }
            if (radius <= 0.01f) radius = _def.radius;
            Remaining = charges;
            _baseScale = transform.localScale; _basePos = transform.localPosition; _baseRot = transform.localRotation;
            _renderers = GetComponentsInChildren<Renderer>(true);
            _colliders = GetComponentsInChildren<Collider>(true);
            var b = RendererBounds(); _height = Mathf.Max(0.05f, b.size.y);
            float a = (Mathf.Abs(transform.position.x * 13.1f + transform.position.z * 7.7f) % 360f) * Mathf.Deg2Rad;
            _tiltAxis = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            FindProduce();
            _subName = _def.displayName;
            _subSlow = string.IsNullOrEmpty(_def.handHint) ? _def.displayName : _def.displayName + ": " + _def.handHint;
            _subNeeds = GatheringSystem.NeedText(_def.requiredTool != ToolKind.None ? _def.requiredTool : _def.bestTool);
            _subFaster = _def.bestTool != ToolKind.None ? _def.displayName + ": " + GatheringSystem.ToolName(_def.bestTool) + " is faster" : _def.displayName;
            _subEmpty = _def.category switch
            {
                ResourceCategory.Stone => "Mined out. Loose stone comes back in time",
                ResourceCategory.Wood => "Nothing left to take for now",
                _ => "Picked clean. Grows back later",
            };
        }

        /// <summary>the legacy fields mirror the definition (tests, bushes and older tools read them)</summary>
        void ApplyDefinition()
        {
            var d = _def;
            displayName = d.displayName; verb = d.prompt; yieldItem = d.item; yieldPerHit = 1;
            bonusItem = d.bonusItem; bonusChance = d.bonusChance;
            requiredTool = d.requiredTool; fasterTool = d.bestTool; handAction = d.handAction; toolAction = d.toolAction;
            regrowHours = d.respawnHours; hideWhenEmpty = d.depletedLook == DepletedLook.Hide; toolWear = d.toolWear;
            charges = Mathf.Max(1, charges);
        }

        static readonly Dictionary<string, ResourceDefinition> _legacy = new Dictionary<string, ResourceDefinition>();
        static ResourceDefinition LegacyDefinition(ResourceNode n)
        {
            string key = $"{(n.yieldItem ? n.yieldItem.id : "?")}|{n.displayName}|{n.verb}|{n.yieldPerHit}|{n.charges}|{n.requiredTool}|{n.fasterTool}|{n.regrowHours}|{n.hideWhenEmpty}";
            if (_legacy.TryGetValue(key, out var d) && d) return d;
            d = ScriptableObject.CreateInstance<ResourceDefinition>(); d.hideFlags = HideFlags.DontSave;
            d.id = "legacy_" + (n.yieldItem ? n.yieldItem.id : "none"); d.displayName = n.displayName; d.prompt = n.verb + " " + n.displayName;
            d.item = n.yieldItem; d.yieldRange = new Vector2Int(n.charges * n.yieldPerHit, n.charges * n.yieldPerHit);
            d.category = CategoryOf(n.yieldItem);
            d.bestTool = n.fasterTool | n.requiredTool; d.requiredTool = n.requiredTool;
            d.handsFactor = n.requiredTool != ToolKind.None ? 0f : n.yieldPerHit;
            d.handAction = n.handAction; d.toolAction = n.toolAction; d.toolWear = n.toolWear;
            d.bonusItem = n.bonusItem; d.bonusChance = n.bonusChance;
            d.respawnHours = Mathf.Max(0.5f, n.regrowHours);
            d.depletedLook = n.hideWhenEmpty ? DepletedLook.Hide : DepletedLook.Stripped;
            d.damagedScale = d.depletedScale = n.emptyScale; d.sink = 0f;
            d.FeedbackDefaults();
            _legacy[key] = d;
            return d;
        }

        /// <summary>"a pick", "an axe" ... (kept for callers of the old node API; the words live in GatheringSystem)</summary>
        public static string ToolName(ToolKind k) => GatheringSystem.ToolName(k);

        public static ResourceCategory CategoryOf(ItemDefinition item)
        {
            string id = item ? item.id : "";
            if (id == "stone" || id.Contains("flint")) return ResourceCategory.Stone;
            if (id == "wood" || id.Contains("log") || id.Contains("branch")) return ResourceCategory.Wood;
            if (id == "fiber" || id.Contains("fibre") || id.Contains("reed")) return ResourceCategory.Fiber;
            if (id.Contains("fish")) return ResourceCategory.Fish;
            return ResourceCategory.Food;
        }

        protected override void OnEnable() { base.OnEnable(); Init(); ResourceManager.Register(this); }
        protected override void OnDisable() { base.OnDisable(); ResourceManager.Unregister(this); }

        // ------------------------------------------------------------------ interaction
        public override string GetPrompt(PlayerInteraction p, out string sub)
        {
            var d = Def; sub = null;
            if (IsEmpty) { if (d.depletedLook == DepletedLook.Hide) return null; sub = _subEmpty; return d.displayName; }
            var plan = GatheringSystem.Plan(d, p ? p.ActiveItem : null);
            if (!plan.allowed) { sub = _subNeeds; return d.prompt; }
            sub = plan.slowByHand ? _subSlow : plan.byHand && d.bestTool != ToolKind.None && d.handsFactor < 1.5f ? _subFaster : _subName;
            return d.prompt;
        }

        public override bool CanInteract(PlayerInteraction p) => !IsEmpty && GatheringSystem.Plan(Def, p ? p.ActiveItem : null).allowed;

        public override void Interact(PlayerInteraction p)
        {
            if (!CanInteract(p)) return;
            var d = Def; var plan = GatheringSystem.Plan(d, p.ActiveItem);
            int anim = plan.byHand ? d.handAction : d.toolAction;
            var fb = p.Feedback;
            if (p.DoLoop(anim, "OnGatherHit", () => Hit(p), FocusPoint, d.gatherSeconds / Mathf.Max(0.1f, plan.speed), this, () => { if (fb) fb.NodeDrivesGatherFx = false; }) && fb)
                fb.NodeDrivesGatherFx = true;
        }

        /// <summary>the player is at the node (closest point within reach): no gathering from meters away</summary>
        public bool InReach(Vector3 playerPos, float slack = 0.6f)
        {
            Vector3 d = FocusPoint - playerPos; float dy = d.y; d.y = 0f;
            return d.magnitude - Radius <= Range + slack && dy > -2f && dy < 2.8f;
        }

        /// <summary>one gather action (animation hit); false = stop gathering</summary>
        public bool Hit(PlayerInteraction p)
        {
            if (IsEmpty || p == null) return false;
            var d = Def; var item = d.item; if (item == null) return false;
            var plan = GatheringSystem.Plan(d, p.ActiveItem);
            if (!plan.allowed || !InReach(p.transform.position)) return false;
            if (p.Inventory.SpaceFor(item) <= 0) { PlayerInteraction.Notify(p.Inventory.IsOverweight ? "Too heavy to carry more." : "Inventory full."); return false; }
            int n = Mathf.Min(Remaining, GatheringSystem.Roll(ref _progress, plan.min, plan.max));
            int got = n > 0 ? p.GiveOrDrop(item, n) : 0;
            if (n > 0 && d.bonusItem && Random.value < d.bonusChance) p.GiveOrDrop(d.bonusItem, 1);
            Wear(p, plan, d);
            Remaining -= n;
            Vector3 at = FocusPoint;
            PlayerFeedback.GatherHit(d, at, (p.transform.position + Vector3.up - at).normalized, plan.byHand, n);
            if (n > 0) PlayerFeedback.GatherCollected(p.transform.position + Vector3.up);
            else if (plan.slowByHand) PlayerFeedback.GatherHint(d.handHint);
            GameEvents.Raise(GameEventType.ResourceGathered, item.id, got, transform.position);
            if (IsEmpty) { Deplete(); return false; }
            Struck();
            return ResourceManager.KeepGathering();
        }

        static void Wear(PlayerInteraction p, GatherPlan plan, ResourceDefinition d)
        {
            var tool = plan.toolItem; if (tool == null || !tool.HasDurability) return;
            float w = d.toolWear * (plan.tool ? plan.tool.wearPerAction : 1f);
            if (w > 0f) p.Inventory.WearActive(w);          // a break raises InventorySystem.ToolBroke: sound, puff and "X broke!" in one place (HUD)
        }

        /// <summary>bare-hand strike (U's punches, HitInfo.unarmed): a small gather result scaled by handsFactor; tools use Interact</summary>
        public void TakeHit(HitInfo hit)
        {
            var d = Def;
            if (IsEmpty || !d.punchable || !hit.unarmed || d.item == null) return;
            var pi = hit.attacker ? hit.attacker.GetComponentInParent<PlayerInteraction>() : null;
            if (pi && pi.Inventory && pi.Inventory.SpaceFor(d.item) <= 0) { PlayerInteraction.Notify(pi.Inventory.IsOverweight ? "Too heavy to carry more." : "Inventory full."); Struck(); return; }
            int n = Mathf.Min(Remaining, GatheringSystem.Pay(ref _progress, hit.damage / d.damagePerUnit * d.handsFactor));
            int got = 0;
            if (n > 0)
            {
                if (pi) { got = pi.GiveOrDrop(d.item, n); PlayerFeedback.GatherCollected(pi.transform.position + Vector3.up); }
                else { WorldPickup.Drop(d.item, n, hit.point + Vector3.up * 0.2f); got = n; }
            }
            else if (d.handsFactor < 1f) PlayerFeedback.GatherHint(string.IsNullOrEmpty(d.handHint) ? "Fists barely scratch it." : d.handHint);
            Remaining -= n;
            PlayerFeedback.GatherHit(d, hit.point, -hit.direction, true, n);
            GameEvents.Raise(GameEventType.ResourceGathered, d.item.id, got, transform.position);
            if (IsEmpty) Deplete(); else Struck();
        }

        void Struck() { wobble = 1f; ApplyVisual(); ResourceManager.Animate(this); }

        void Deplete()
        {
            var d = Def;
            EmptyUntil = GameClock.Now + GameClock.Hours(d.respawnHours);
            _progress = 0f; Regrow01 = 0f; wobble = 0f;
            PlayerFeedback.GatherDepleted(d, FocusPoint);
            GameEvents.Raise(GameEventType.ResourceDepleted, d.id, 1, transform.position);
            ApplyVisual();
            ResourceManager.Depleted(this);
        }

        /// <summary>back to full (respawn, new game)</summary>
        public void Regrow()
        {
            Init();
            Remaining = charges; EmptyUntil = -1; _progress = 0f; Regrow01 = 0f; wobble = 0f;
            ApplyVisual();
            ResourceManager.Restored(this);
        }

        /// <summary>save / load</summary>
        public void Restore(int remaining, double emptyUntil)
        {
            Init();
            Remaining = Mathf.Clamp(remaining, 0, charges); EmptyUntil = IsEmpty ? emptyUntil : -1; _progress = 0f; Regrow01 = 0f;
            if (IsEmpty && EmptyUntil < 0) EmptyUntil = GameClock.Now + GameClock.Hours(Def.respawnHours);
            ApplyVisual();
            if (IsEmpty) ResourceManager.Depleted(this); else ResourceManager.Restored(this);
        }

        /// <summary>ResourceManager: the regrowing look for an emptied node that stays visible (0..1)</summary>
        internal void SetRegrow(float k)
        {
            k = Mathf.Clamp01(k);
            if (Mathf.Abs(k - Regrow01) < 0.02f && k < 1f) return;
            Regrow01 = k; ApplyVisual();
        }

        // ------------------------------------------------------------------ looks
        /// <summary>full / damaged / depleted / regrowing: scale, settle, tilt, crop visibility (no material instances)</summary>
        internal void ApplyVisual()
        {
            var d = Def;
            if (IsEmpty && d.depletedLook == DepletedLook.Hide) { SetVisible(false); SetPose(1f, 0f, 0f); return; }
            SetVisible(true);
            float scale, settle, tilt;
            if (IsEmpty)
            {
                float r = Regrow01;
                scale = Mathf.Lerp(d.depletedScale, Mathf.Max(d.depletedScale, d.damagedScale), r);
                settle = Mathf.Lerp(d.sink * 2f, d.sink, r); tilt = Mathf.Lerp(7f, 3f, r);
                SetProduce(r > 0.5f ? ProduceLook.Unripe : ProduceLook.Empty);
            }
            else
            {
                float h = Health01;
                scale = Mathf.Lerp(d.damagedScale, 1f, h); settle = d.sink * (1f - h); tilt = 6f * (1f - h);
                SetProduce(ProduceLook.Ripe);
            }
            SetPose(scale, settle * _height, tilt);
        }

        void SetPose(float scale, float sinkM, float tiltDeg)
        {
            float w = wobble > 0f ? Mathf.Sin(wobble * 26f) * wobble * hitWobble : 0f;
            transform.localScale = Vector3.Scale(_baseScale * scale, new Vector3(1f + w, 1f - w, 1f + w));
            var up = transform.parent ? transform.parent.InverseTransformDirection(Vector3.up) : Vector3.up;
            transform.localPosition = _basePos - up * sinkM;
            transform.localRotation = Quaternion.AngleAxis(tiltDeg, _tiltAxis) * _baseRot;
        }

        /// <summary>ResourceManager: one wobble step; false when it has settled</summary>
        internal bool StepWobble(float dt)
        {
            wobble = Mathf.Max(0f, wobble - dt * 3.5f);
            ApplyVisual();
            return wobble > 0f;
        }

        void SetVisible(bool on)
        {
            if (_renderers == null) return;
            foreach (var r in _renderers) if (r) r.enabled = on && !IsHiddenProduce(r);
            foreach (var c in _colliders) if (c) c.enabled = on;
        }

        enum ProduceLook { Ripe, Unripe, Empty }
        ProduceLook _produceLook = ProduceLook.Ripe;

        /// <summary>berries: material slots named like the definition's crop material, or whole renderers made only of it</summary>
        void FindProduce()
        {
            var list = new List<ProduceSlot>();
            string key = "Berry";
            foreach (var r in _renderers)
            {
                if (!r || r is ParticleSystemRenderer) continue;
                var mats = r.sharedMaterials; int crop = 0; Material other = null;
                for (int i = 0; i < mats.Length; i++) { if (mats[i] && mats[i].name.Contains(key)) crop++; else if (mats[i] && !other) other = mats[i]; }
                if (crop == 0) continue;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] && mats[i].name.Contains(key)) list.Add(new ProduceSlot { r = r, slot = i, ripe = mats[i], empty = other, whole = other == null });
            }
            _produce = list.ToArray();
        }

        bool IsHiddenProduce(Renderer r)
        {
            if (_produceLook == ProduceLook.Ripe || _produce == null) return false;
            foreach (var p in _produce) if (p.r == r && p.whole) return _produceLook == ProduceLook.Empty || !ResourceManager.UnripeMaterial;
            return false;
        }

        void SetProduce(ProduceLook look)
        {
            if (_produce == null || _produce.Length == 0 || look == _produceLook) return;
            _produceLook = look;
            foreach (var p in _produce)
            {
                if (!p.r) continue;
                if (p.whole) { p.r.enabled = look == ProduceLook.Ripe || (look == ProduceLook.Unripe && ResourceManager.UnripeMaterial); }
                var mats = p.r.sharedMaterials;
                mats[p.slot] = look == ProduceLook.Ripe ? p.ripe : look == ProduceLook.Unripe && ResourceManager.UnripeMaterial ? ResourceManager.UnripeMaterial : (p.whole ? p.ripe : p.empty);
                p.r.sharedMaterials = mats;
            }
        }

        Bounds RendererBounds()
        {
            Bounds b = new Bounds(transform.position, Vector3.one * 0.3f); bool any = false;
            foreach (var r in _renderers) { if (!r || r is ParticleSystemRenderer) continue; if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            return b;
        }

        /// <summary>renderers for the interaction highlight</summary>
        public Renderer[] Renderers { get { Init(); return _renderers; } }
    }
}
