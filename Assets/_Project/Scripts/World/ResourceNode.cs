using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.Player;

namespace PrimalFrontier.World
{
    /// <summary>
    /// Gatherable node (driftwood pile, stone pile, fibre plant, berry bush, big rock). Every animation hit gives
    /// items, wears the tool and uses one charge; empty nodes hide (or wait) and regrow after a game-time delay.
    /// </summary>
    public class ResourceNode : Interactable
    {
        public string displayName = "Resource";
        public string verb = "Gather";
        public ItemDefinition yieldItem;
        [Min(1)] public int yieldPerHit = 1;
        [Min(1)] public int charges = 4;
        public ItemDefinition bonusItem;
        [Range(0, 1)] public float bonusChance;
        [Tooltip("tool needed at all (None = bare hands work)")] public ToolKind requiredTool = ToolKind.None;
        [Tooltip("tool that doubles the yield")] public ToolKind fasterTool = ToolKind.None;
        [Tooltip("animation with bare hands / with the faster tool")] public int handAction = PlayerActions.GatherPlant;
        public int toolAction = PlayerActions.GatherPlant;
        [Tooltip("in-game hours until it grows back")] public float regrowHours = 18f;
        public bool hideWhenEmpty = true;
        public float toolWear = 1f;
        public float radius = 0.4f;
        [Header("Feedback")]
        [Tooltip("the node wobbles on every hit")] public float hitWobble = 0.07f;
        [Tooltip("piles get smaller as they are used up (1 = no shrink)")] [Range(0.4f, 1f)] public float emptyScale = 0.7f;

        public int Remaining { get; private set; }
        public double EmptyUntil { get; private set; } = -1;
        public bool IsEmpty => Remaining <= 0;
        public override float Radius => radius;
        public override float Range => 1.9f;

        Renderer[] _renderers; Collider[] _colliders;
        Vector3 _baseScale; float _wobble;

        void Awake()
        {
            _baseScale = transform.localScale;
            Remaining = charges;
            _renderers = GetComponentsInChildren<Renderer>(true);
            _colliders = GetComponentsInChildren<Collider>(true);
        }

        void Update()
        {
            if (IsEmpty && EmptyUntil >= 0 && GameClock.Now >= EmptyUntil) Regrow();
            if (_wobble > 0f)
            {
                _wobble = Mathf.Max(0f, _wobble - Time.deltaTime * 3.5f);
                float w = Mathf.Sin(_wobble * 26f) * _wobble * hitWobble;
                transform.localScale = Vector3.Scale(SizeNow(), new Vector3(1f + w, 1f - w, 1f + w));
            }
        }

        Vector3 SizeNow() => _baseScale * Mathf.Lerp(emptyScale, 1f, charges > 0 ? Mathf.Clamp01(Remaining / (float)charges) : 1f);
        void ApplySize() { _wobble = 0f; transform.localScale = SizeNow(); }

        string EventName => "OnGatherHit";

        public override string GetPrompt(PlayerInteraction p, out string sub)
        {
            sub = null;
            if (IsEmpty) { if (hideWhenEmpty) return null; sub = "Grows back later"; return displayName + " (empty)"; }
            if (requiredTool != ToolKind.None && !p.HasTool(requiredTool, out _)) { sub = "Needs " + ToolName(requiredTool) + " in hand"; return verb + " " + displayName; }
            if (fasterTool != ToolKind.None && !p.HasTool(fasterTool, out _)) sub = ToolName(fasterTool) + " would be faster";
            return verb + " " + displayName;
        }

        public override bool CanInteract(PlayerInteraction p) => !IsEmpty && (requiredTool == ToolKind.None || p.HasTool(requiredTool, out _));

        public static string ToolName(ToolKind k) => k switch
        {
            ToolKind.Chop => "an axe", ToolKind.Mine => "a pick", ToolKind.Cut => "a knife", ToolKind.Hammer => "a hammer",
            ToolKind.Chop | ToolKind.Mine => "a stone tool", _ => "a tool"
        };

        public override void Interact(PlayerInteraction p)
        {
            if (!CanInteract(p)) return;
            bool tool = (fasterTool != ToolKind.None && p.HasTool(fasterTool, out _)) || requiredTool != ToolKind.None;
            p.DoLoop(tool ? toolAction : handAction, EventName, () => Hit(p), FocusPoint, 1.25f, this);
        }

        /// <summary>one hit; false = stop gathering</summary>
        public bool Hit(PlayerInteraction p)
        {
            if (IsEmpty) return false;
            if (requiredTool != ToolKind.None && !p.HasTool(requiredTool, out _)) return false;
            int n = yieldPerHit;
            ItemDefinition tool = null;
            if (fasterTool != ToolKind.None && p.HasTool(fasterTool, out tool)) n = Mathf.Max(n + 1, Mathf.RoundToInt(n * 2f * tool.toolPower));
            else if (requiredTool != ToolKind.None) p.HasTool(requiredTool, out tool);
            if (p.Inventory.SpaceFor(yieldItem) <= 0) { PlayerInteraction.Notify(p.Inventory.IsOverweight ? "Too heavy to carry more." : "Inventory full."); return false; }
            int got = p.GiveOrDrop(yieldItem, n);
            if (bonusItem && Random.value < bonusChance) p.GiveOrDrop(bonusItem, 1);
            if (tool != null && tool.HasDurability && p.Inventory.WearActive(toolWear)) PlayerInteraction.Notify(tool.displayName + " broke!");
            Remaining--;
            _wobble = 1f;
            GameEvents.Raise(GameEventType.ResourceGathered, yieldItem ? yieldItem.id : "?", got, transform.position);
            if (IsEmpty) { Deplete(); return false; }
            return true;
        }

        void Deplete()
        {
            EmptyUntil = GameClock.Now + GameClock.Hours(regrowHours);
            if (hideWhenEmpty)
            {
                VFX.VfxPool.Instance.Play(VFX.VfxId.HitDust, FocusPoint, Vector3.up);
                SetVisible(false);
            }
        }

        public void Regrow()
        {
            Remaining = charges; EmptyUntil = -1; SetVisible(true); ApplySize();
        }

        void SetVisible(bool on)
        {
            foreach (var r in _renderers) if (r) r.enabled = on;
            foreach (var c in _colliders) if (c) c.enabled = on;
        }

        /// <summary>save / load</summary>
        public void Restore(int remaining, double emptyUntil)
        {
            Remaining = Mathf.Clamp(remaining, 0, charges); EmptyUntil = emptyUntil;
            SetVisible(!(IsEmpty && hideWhenEmpty)); ApplySize();
        }
    }
}
