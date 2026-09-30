using System;
using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.Survival;
using PrimalFrontier.World;

namespace PrimalFrontier.Player
{
    /// <summary>
    /// Finds the best interactable in front of the player, shows its prompt and runs the resulting action:
    /// one-shot (pick up, drink, eat, search) or loop (gather until depleted / cancelled). Gameplay happens on the
    /// animation event (tool contact, hand at mouth) with a timer fallback, so it never depends on a clip firing.
    /// Also owns the hotbar selection and the "use active item" button for food and water containers.
    /// </summary>
    [RequireComponent(typeof(PlayerMotor))]
    public class PlayerInteraction : MonoBehaviour
    {
        public float searchRadius = 3.2f;
        public float maxAngle = 75f;
        [Header("Face the work (eased, only once the motor stops steering)")]
        [Tooltip("deg/s max")] public float focusTurnSpeed = 180f;
        [Tooltip("s to settle on the focus")] public float focusSmoothTime = 0.12f;
        [Tooltip("deg per frame max (long frames must not snap)")] public float focusMaxStep = 5f;

        public PlayerMotor Motor { get; private set; }
        public PlayerAnimationDriver Driver { get; private set; }
        public PlayerHealth Health { get; private set; }
        public PlayerSurvival Survival { get; private set; }
        public PlayerFeedback Feedback { get; private set; }
        public InventorySystem Inventory { get; private set; }
        public CraftingSystem Crafting { get; private set; }
        public ItemDefinition ActiveItem => Inventory ? Inventory.ActiveItem : null;
        public ItemStack ActiveStack => Inventory ? Inventory.ActiveStack : null;

        public Interactable Target { get; private set; }
        public string Prompt { get; private set; }
        public string PromptSub { get; private set; }
        public bool PromptEnabled { get; private set; }
        public string HoldLabel { get; private set; }
        public float HoldProgress { get; private set; }
        public bool InAction => _act != null;
        public int CurrentActionId => _act?.anim ?? PlayerActions.None;
        /// <summary>HUD message feed</summary>
        public static event Action<string> Message;
        public static void Notify(string msg) => Message?.Invoke(msg);
        /// <summary>
        /// items another system uses with the attack button / inventory Use (e.g. SURV: a bandage plays PlayerActions.BandageUse
        /// and stops bleeding). handles(item) claims the item (it is then never punched with); use(player, stack) runs it and
        /// returns true when something happened. Checked before food / water.
        /// </summary>
        public struct ItemUseHandler { public Func<ItemDefinition, bool> handles; public Func<PlayerInteraction, ItemStack, bool> use; }
        public static readonly System.Collections.Generic.List<ItemUseHandler> UseHandlers = new System.Collections.Generic.List<ItemUseHandler>();
        public static bool HasUseHandler(ItemDefinition item)
        {
            if (!item) return false;
            for (int i = 0; i < UseHandlers.Count; i++) { var h = UseHandlers[i]; if (h.handles != null && h.handles(item)) return true; }
            return false;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetUseHandlers() => UseHandlers.Clear();
        /// <summary>world systems that are not in the Interactable list (terrain trees, ocean)</summary>
        public static readonly System.Collections.Generic.List<Func<PlayerInteraction, Interactable>> ExtraProviders = new System.Collections.Generic.List<Func<PlayerInteraction, Interactable>>();
        /// <summary>true while a menu / build mode / climbing owns the input</summary>
        public bool Suspended { get; set; }
        /// <summary>while suspended, a system that owns the player (climbing) can still show its own prompt</summary>
        public Func<(string prompt, string sub)> ExternalPrompt;

        /// <summary>procedural body program layered on a one-shot (placeholder clips until CHAR's physical drink / fill clips exist)</summary>
        enum Program { None, KneelDrink, KneelFill, ContainerDrink }

        class RunningAction
        {
            public int anim; public string evt; public bool loop;
            public Program program; public int procedural = -1; public bool progRaised, progScooped, progApproach;   // -1 = not known yet (placeholder clip?)
            public Action onEvent; public Func<bool> onHit; public Action onEnd;
            public float started, lastHit, period; public bool fired; public Vector3 focus; public bool hasFocus;
            public Interactable source;
        }
        RunningAction _act;
        CharacterAnimationEvents _events;
        PlayerInputReader _in;
        float _holdT; float _nextScan; float _focusVel;

        void Awake()
        {
            Motor = GetComponent<PlayerMotor>(); Driver = GetComponent<PlayerAnimationDriver>(); Health = GetComponent<PlayerHealth>();
            Survival = GetComponent<PlayerSurvival>(); Feedback = GetComponent<PlayerFeedback>();
            Inventory = GetComponent<InventorySystem>(); Crafting = GetComponent<CraftingSystem>();
            _events = GetComponentInChildren<CharacterAnimationEvents>();
        }

        void OnEnable()
        {
            if (_events) _events.AnimationEventRaised += OnAnimEvent;
            if (Driver) Driver.ActionFinished += OnActionFinished;
            if (Health) Health.Damaged += OnDamaged;
        }
        void OnDisable()
        {
            if (_events) _events.AnimationEventRaised -= OnAnimEvent;
            if (Driver) Driver.ActionFinished -= OnActionFinished;
            if (Health) Health.Damaged -= OnDamaged;
        }

        void Start()
        {
            _in = PlayerInputReader.Instance;
            // Climbable.CanInteract needs a PlayerClimb, which only Climbable.Interact used to add: Interact is never called while
            // CanInteract is false, so a fresh player could not start climbing (prompt greyed, E did nothing; test 2026-09-29).
            // Added in Start: every Awake (the driver's Animator) has run.
            if (!GetComponent<PlayerClimb>()) gameObject.AddComponent<PlayerClimb>();
            if (Crafting) Crafting.HandsFree = () => !InAction && Motor.PlanarSpeed < 0.25f && Motor.IsGrounded && Driver && !Driver.IsDead &&
                                                     (!Driver.IsBusy || Driver.CurrentAction == PlayerActions.Craft);
        }

        void Update()
        {
            if (_in == null) _in = PlayerInputReader.Instance;
            bool dead = Health && Health.IsDead;
            UpdateAction();
            UpdateCraftAnimation();
            if (dead || Suspended || _in == null)
            {
                if (!dead && Suspended && ExternalPrompt != null) { var (pr, sb) = ExternalPrompt(); Prompt = pr; PromptSub = sb; PromptEnabled = pr != null; HoldLabel = null; HoldProgress = 0f; }
                else ClearPrompt();
                return;
            }

            // hotbar
            if (_in.HotbarPressed >= 0) Inventory.SetActiveSlot(_in.HotbarPressed);
            else if (Mathf.Abs(_in.HotbarScroll) > 0.01f && !_in.Aim) Inventory.SetActiveSlot(Inventory.ActiveSlot + (_in.HotbarScroll > 0 ? -1 : 1));
            if (_in.DropPressed && !InAction) DropActive();

            if (InAction)
            {
                ClearPrompt();
                bool cancel = _act.loop && (_in.InteractPressed || _in.Move.sqrMagnitude > 0.2f || _in.JumpPressed);
                if (cancel) StopAction();
                return;
            }

            if (Time.time >= _nextScan) { _nextScan = Time.time + 0.08f; Scan(); }
            UpdatePrompt();
            if (Target != null && !(Driver && Driver.IsBusy))
            {
                string hold = Target.HoldPrompt(this);
                if (hold != null && _in.InteractHeld)
                {
                    _holdT += Time.deltaTime; HoldLabel = hold; HoldProgress = Mathf.Clamp01(_holdT / 0.8f);
                    if (_holdT >= 0.8f) { _holdT = -999f; Target.HoldInteract(this); }
                }
                else if (!_in.InteractHeld)
                {
                    if (_holdT > 0f && _holdT < 0.8f && hold != null && PromptEnabled) Target.Interact(this);   // tap = normal action
                    _holdT = 0f; HoldProgress = 0f; HoldLabel = hold;
                }
                if (hold == null && _in.InteractPressed && PromptEnabled) Target.Interact(this);
            }
            else { _holdT = 0f; HoldProgress = 0f; }
        }

        // ------------------------------------------------------------------ target selection
        void Scan()
        {
            Interactable best = null; float bestScore = float.MaxValue;
            Vector3 me = transform.position; Vector3 fwd = transform.forward;
            var cam = Motor.CameraTransform;
            Vector3 camF = cam ? Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized : fwd;
            void Consider(Interactable it)
            {
                if (it == null || !it.isActiveAndEnabled) return;
                Vector3 f = it.FocusPoint;
                Vector3 d = f - me; float dy = d.y; d.y = 0f;
                float dist = Mathf.Max(0f, d.magnitude - it.Radius);
                if (dist > it.Range || dy < -1.6f || dy > 2.6f) return;
                float ang = d.sqrMagnitude < 0.04f ? 0f : Mathf.Min(Vector3.Angle(fwd, d), Vector3.Angle(camF, d));
                if (ang > maxAngle && dist > 0.6f) return;
                float score = dist + ang * 0.02f - it.Priority * 0.5f;
                if (score < bestScore) { bestScore = score; best = it; }
            }
            var list = Interactable.Active;
            float r2 = (searchRadius + 6f) * (searchRadius + 6f);
            for (int i = 0; i < list.Count; i++)
            {
                var it = list[i];
                if (it == null) continue;
                if (!it.LargeArea && it.Radius < 2f && (it.transform.position - me).sqrMagnitude > r2) continue;   // water bodies: focus decides
                Consider(it);
            }
            foreach (var prov in ExtraProviders) Consider(prov(this));
            Target = best;
        }

        void UpdatePrompt()
        {
            if (Target == null) { ClearPrompt(); return; }
            Prompt = Target.GetPrompt(this, out var sub);
            PromptSub = sub;
            PromptEnabled = Prompt != null && Target.CanInteract(this);
            HoldLabel = Target.HoldPrompt(this);
            if (Prompt == null && HoldLabel == null) Target = null;
        }

        void ClearPrompt() { Prompt = null; PromptSub = null; PromptEnabled = false; HoldLabel = null; HoldProgress = 0f; }

        // ------------------------------------------------------------------ action runner (used by interactables & items)
        /// <summary>one-shot animation; onEvent runs at the named animation event (or after fallback seconds)</summary>
        public bool DoOneShot(int anim, string evt, Action onEvent, Vector3? focus = null, float fallback = 1.2f, Interactable source = null, Action onEnd = null)
        {
            if (InAction || (Driver && (Driver.IsBusy || Driver.IsDead))) return false;
            // physical drinking (directive 8): at a water source the body kneels at the edge facing the water and the hands scoop
            // (Drink_Kneel), filling a container dips it in (Collect_Water); from a container the right hand brings it to the face
            var program = Program.None;
            bool atWater = focus.HasValue && (source is WaterSource || source is OceanShore);
            if (atWater && anim == PlayerActions.Drink) { anim = PlayerActions.DrinkKneel; program = Program.KneelDrink; }
            else if (atWater && anim == PlayerActions.CollectWater) program = Program.KneelFill;
            else if (anim == PlayerActions.Drink && source == null) program = Program.ContainerDrink;
            _act = new RunningAction { anim = anim, evt = evt, onEvent = onEvent, loop = false, started = Time.time, period = fallback, source = source, onEnd = onEnd, program = program };
            SetFocus(focus);
            if (Driver) Driver.PlayAction(anim);
            return true;
        }

        /// <summary>looping action; onHit runs on every animation hit event, return false to stop (depleted, full...)</summary>
        public bool DoLoop(int anim, string evt, Func<bool> onHit, Vector3? focus = null, float period = 1.2f, Interactable source = null, Action onEnd = null)
        {
            if (InAction || (Driver && (Driver.IsBusy || Driver.IsDead))) return false;
            _act = new RunningAction { anim = anim, evt = evt, onHit = onHit, loop = true, started = Time.time, lastHit = Time.time, period = period, source = source, onEnd = onEnd };
            SetFocus(focus);
            if (Driver) Driver.PlayAction(anim);
            return true;
        }

        void SetFocus(Vector3? focus)
        {
            _act.hasFocus = focus.HasValue; _act.focus = focus ?? Vector3.zero; _focusVel = 0f;
            if (Feedback) { Feedback.ActionFocusPoint = focus; if (focus.HasValue) Feedback.ActionFocusNormal = (transform.position + Vector3.up - focus.Value).normalized; }
        }

        public void StopAction()
        {
            if (_act == null) return;
            var a = _act; _act = null;
            if (a.loop && Driver && Driver.CurrentAction == a.anim) Driver.StopAction();
            if (Feedback) Feedback.ActionFocusPoint = null;
            a.onEnd?.Invoke();
        }

        void UpdateAction()
        {
            if (_act == null) return;
            // face the work: eased (SmoothDampAngle, max focusTurnSpeed and focusMaxStep per frame), and only once the motor no longer
            // steers the body (it braked below 0.2 m/s) and the action is not waiting for the brake: two scripts turning the body
            // in the same frames gave single-frame jumps of 13-16 deg (probe 2026-09-28, was 540 deg/s)
            if (_act.hasFocus && !(Motor && Motor.IsSteering) && !(Driver && Driver.Braking))
            {
                Vector3 d = _act.focus - transform.position; d.y = 0;
                if (d.sqrMagnitude > 0.01f)
                {
                    float cur = transform.eulerAngles.y, want = Quaternion.LookRotation(d).eulerAngles.y;
                    float y = Mathf.SmoothDampAngle(cur, want, ref _focusVel, focusSmoothTime, focusTurnSpeed, Mathf.Max(1e-4f, Time.deltaTime));
                    float step = Mathf.Clamp(Mathf.DeltaAngle(cur, y), -focusMaxStep, focusMaxStep);
                    transform.rotation = Quaternion.Euler(0f, cur + step, 0f);
                }
            }
            float t = Time.time - _act.started;
            if (_act.program != Program.None) RunProgram(t);
            if (_act == null) return;
            if (!_act.loop)
            {
                if (!_act.fired && t > _act.period) FireOneShot();             // clip had no event / animator culled
                if (_act != null && t > _act.period + 2.5f) { FireOneShot(); StopAction(); }   // safety: never stuck
            }
            else if (Time.time - _act.lastHit > _act.period * 1.6f) LoopHit();   // fallback cadence
        }

        // ------------------------------------------------------------------ physical drink / fill programs
        [Header("Physical drinking (placeholder programs)")]
        [Tooltip("how far the upper body bends toward the water while scooping (look body weight added, 0-0.8)")] [Range(0f, 0.8f)] public float drinkBend = 0.6f;
        [Tooltip("how far the body sinks into a kneel while scooping (m; feet stay planted)")] [Range(0f, 0.3f)] public float drinkSink = 0.22f;
        [Tooltip("kneel this far (flat) from the water's edge; a farther edge is approached by a short step of at most kneelMaxStep")] public float kneelEdgeDistance = 0.6f, kneelMaxStep = 0.45f;

        void StepToEdge(Vector3 water)
        {
            if (!Motor) return;
            Vector3 flat = water - transform.position; flat.y = 0f;
            float gap = flat.magnitude - kneelEdgeDistance;
            if (gap < 0.08f) return;
            float step = Mathf.Min(gap, kneelMaxStep); Vector3 dir = flat.normalized, to = transform.position + dir * step;
            // ground there, close to the feet's height (no stepping off a bank or into a hole)
            int mask = ~(1 << gameObject.layer) & ~(1 << 2);
            if (!Physics.Raycast(to + Vector3.up * 0.8f, Vector3.down, out var hit, 1.6f, mask, QueryTriggerInteraction.Ignore)) return;
            if (Mathf.Abs(hit.point.y - transform.position.y) > 0.3f) return;
            const float time = 0.5f;                                                 // Burst eases out: mean speed 0.53 of the start
            Motor.Burst(dir * (step / (0.533f * time)), time);
        }
        PlayerIK _ikCache; float _armLen;

        /// <summary>the water point, or the nearest point toward it both hands can reach (0.95 of the arm from each shoulder)</summary>
        Vector3 Reachable(Animator a, Vector3 p)
        {
            Transform ul = a.GetBoneTransform(HumanBodyBones.LeftUpperArm), ur = a.GetBoneTransform(HumanBodyBones.RightUpperArm);
            if (!ul || !ur) return p;
            if (_armLen <= 0f)
            {
                Transform ll = a.GetBoneTransform(HumanBodyBones.LeftLowerArm), lh = a.GetBoneTransform(HumanBodyBones.LeftHand);
                _armLen = ll && lh ? Vector3.Distance(ul.position, ll.position) + Vector3.Distance(ll.position, lh.position) : 0.55f;
            }
            Vector3 s = (ul.position + ur.position) * 0.5f, d = p - s;
            float reach = _armLen * 0.95f;
            return d.magnitude > reach ? s + d.normalized * reach : p;
        }

        /// <summary>the state plays the Crouch placeholder (no physical drink clip yet): the program moves the hands and raises the event</summary>
        bool IsPlaceholder()
        {
            var a = Driver ? Driver.animator : null; if (!a) return false;
            foreach (var ci in a.GetCurrentAnimatorClipInfo(0)) if (ci.clip && ci.clip.name == "Crouch" && ci.weight > 0.3f) return true;
            if (a.IsInTransition(0)) foreach (var ci in a.GetNextAnimatorClipInfo(0)) if (ci.clip && ci.clip.name == "Crouch") return true;
            return false;
        }

        void RunProgram(float t)
        {
            var a = Driver ? Driver.animator : null;
            if (!_ikCache && a) _ikCache = a.GetComponent<PlayerIK>();
            if (!_ikCache || !a || !a.isHuman) return;
            Transform head = a.GetBoneTransform(HumanBodyBones.Head), chest = a.GetBoneTransform(HumanBodyBones.UpperChest) ?? a.GetBoneTransform(HumanBodyBones.Chest);
            if (!head || !chest) return;
            Vector3 fwd = transform.forward, right = transform.right;
            Vector3 mouth = head.position + fwd * 0.1f - Vector3.up * 0.05f;
            if (_act.program == Program.ContainerDrink)
            {
                // the Drink clip lifts the hand; the IK takes the container the last bit to the lips around the swallow (bell over 0.4-0.8)
                var st = a.GetCurrentAnimatorStateInfo(0);
                if (!st.IsName("Drink")) return;
                float n = st.normalizedTime, w = Mathf.Clamp01(1f - Mathf.Abs(n - 0.62f) / 0.22f) * 0.65f;
                _ikCache.SetActionHands(Vector3.zero, 0f, mouth - fwd * 0.07f - Vector3.up * 0.06f + right * 0.03f, w);
                return;
            }
            if (_act.procedural < 0 && t > 0.15f) _act.procedural = IsPlaceholder() ? 1 : t > 0.6f ? 0 : -1;
            if (_act.procedural == 0) return;                                        // a real clip: its own hands and events
            float S(float x) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(x));
            Vector3 w0 = _act.focus;
            // the upper body bends toward the water while the hands go down (the look's body weight bends the spine, the legs
            // keep the crouch), then straightens as the hands come up to the mouth
            float bend = S((t - 0.25f) / 0.4f) * (1f - S((t - 0.95f) / 0.4f));
            float lookW = S((t - 0.2f) / 0.3f) * (1f - S((t - 1.6f) / 0.35f));
            Vector3 lookAt = Vector3.Lerp(w0, head.position + fwd * 2f - Vector3.up * 0.3f, S((t - 0.95f) / 0.4f));
            _ikCache.SetActionLook(lookAt, lookW, 0.2f + bend * drinkBend, bend * drinkSink);
            // shuffle up to the edge first when the water is out of the arms' reach (a short eased step, only onto ground
            // about as high as the feet)
            if (!_act.progApproach) { _act.progApproach = true; StepToEdge(w0); }
            // where the hands can get to: the water point, pulled in to the arm's reach from each shoulder
            Vector3 water = Reachable(a, w0);
            if (_act.program == Program.KneelDrink)
            {
                // 0.35-0.75 reach down, 0.75-0.95 scoop, 0.95-1.35 up to the mouth, 1.35 drink, 1.6-1.9 hands down
                float w = S((t - 0.35f) / 0.4f) * (1f - S((t - 1.6f) / 0.3f));
                Vector3 c = Vector3.Lerp(water, mouth - fwd * 0.03f, S((t - 0.95f) / 0.4f));
                _ikCache.SetActionHands(c - right * 0.07f, w, c + right * 0.07f, w);
                if (!_act.progScooped && t >= 0.8f) { _act.progScooped = true; Scoop(w0); }
                if (!_act.progRaised && t >= 1.35f) { _act.progRaised = true; if (_events) _events.OnDrink("kneel"); }
            }
            else
            {
                // filling: the container hand dips in (0.35-0.8), holds while it fills (fills at 1.15), lifts it to the chest (1.3-1.7)
                float w = S((t - 0.35f) / 0.45f) * (1f - S((t - 1.7f) / 0.25f));
                Vector3 hold = chest.position + fwd * 0.32f - Vector3.up * 0.1f;
                Vector3 c = Vector3.Lerp(water - Vector3.up * 0.05f, hold, S((t - 1.3f) / 0.4f));
                _ikCache.SetActionHands(c - right * 0.12f + Vector3.up * 0.04f, w * 0.6f, c, w);
                if (!_act.progScooped && t >= 0.85f) { _act.progScooped = true; Scoop(w0); }
                if (!_act.progRaised && t >= 1.15f) { _act.progRaised = true; if (_events) _events.OnDrink("fill"); }
            }
        }

        /// <summary>hands (or the container) break the surface: a small splash and the scoop sound at the water</summary>
        static void Scoop(Vector3 at)
        {
            VFX.VfxPool.Instance.Play(VFX.VfxId.WaterSplash, at, Vector3.up, null, 0.45f);
            Audio.SfxPlayer.Instance.Play(Audio.SfxId.WaterScoop, at, 0.8f);
        }

        void FireOneShot()
        {
            if (_act == null || _act.fired) return;
            _act.fired = true;
            try { _act.onEvent?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
        }

        void LoopHit()
        {
            if (_act == null) return;
            _act.lastHit = Time.time;
            bool go;
            try { go = _act.onHit == null || _act.onHit(); } catch (Exception e) { Debug.LogException(e); go = false; }
            if (!go) StopAction();
        }

        void OnAnimEvent(string fn, string param)
        {
            if (_act == null || fn != _act.evt) return;
            if (_act.loop) LoopHit(); else FireOneShot();
        }

        void OnActionFinished(int id)
        {
            if (_act == null || _act.loop || id != _act.anim) return;
            FireOneShot();
            var a = _act; _act = null;
            if (Feedback) Feedback.ActionFocusPoint = null;
            a.onEnd?.Invoke();
        }

        void OnDamaged(float amount, Vector3 src, bool heavy) { if (_act != null) { if (!_act.loop) FireOneShot(); StopAction(); } }

        void UpdateCraftAnimation()
        {
            if (!Crafting || !Driver) return;
            bool want = Crafting.IsWorking && !InAction;
            if (want && Driver.CurrentAction != PlayerActions.Craft && !Driver.IsBusy) Driver.PlayAction(PlayerActions.Craft);
            else if (!want && Driver.CurrentAction == PlayerActions.Craft) Driver.StopAction();
        }

        // ------------------------------------------------------------------ item use (food, water container, drop)
        // Routing only: the rules live in PlayerSurvival.ConsumeItem (food) and WaterRules (water).
        const string SaltDrinkWarning = "Salt water makes you thirstier, and boiling does not remove the salt. Find fresh water.";

        /// <summary>eat / drink the active item; true if something happened</summary>
        public bool UseActiveConsumable()
        {
            var st = ActiveStack; if (st == null) return false;
            var item = st.item;
            for (int i = 0; i < UseHandlers.Count; i++)
            {
                var h = UseHandlers[i];
                if (h.handles != null && h.use != null && h.handles(item)) return h.use(this, st);
            }
            if (item.IsWaterContainer)
            {
                if (st.water <= 0) { Notify("The " + item.displayName + " is empty. Fill it at water or a rain collector."); return false; }
                int slot = Inventory.ActiveSlot;
                return DoOneShot(PlayerActions.Drink, "OnDrink", () =>
                {
                    var s = Inventory.Get(slot); if (s == null || s.item != item || s.water <= 0) return;
                    bool salt = WaterRules.TypeOf(s) == WaterType.SaltWater;
                    if (!WaterRules.Drink(Survival, s)) return;
                    Inventory.ForceNotify();
                    if (salt) Notify(SaltDrinkWarning);
                }, null, 2.0f);
            }
            if (item.IsFood) return Eat(item);
            return false;
        }

        public bool Eat(ItemDefinition item)
        {
            if (item == null || !Inventory.Has(item)) return false;
            if (Feedback) Feedback.HotFood = item.isHot;
            return DoOneShot(PlayerActions.Eat, "OnEat", () =>
            {
                if (!Inventory.Remove(item, 1)) return;
                if (Survival) Survival.ConsumeItem(item);
            }, null, 0.9f);
        }

        /// <summary>pour the water out of the container in this slot (inventory Empty button); true when there was water</summary>
        public bool EmptyContainer(int slot)
        {
            var s = Inventory ? Inventory.Get(slot) : null;
            if (!WaterRules.IsContainer(s) || s.water <= 0) return false;
            var type = WaterRules.TypeOf(s); int charges = s.water;
            WaterRules.Empty(s);
            Inventory.ForceNotify();
            GameEvents.Raise(GameEventType.WaterEmptied, SurvivalConfig.Instance.Water(type).eventId, charges, transform.position);
            Notify("You pour the " + WaterRules.Label(type) + " out of the " + s.item.displayName + ".");
            return true;
        }

        public void DropActive()
        {
            var st = ActiveStack; if (st == null) return;
            var taken = Inventory.TakeFromSlot(Inventory.ActiveSlot, st.count);
            if (taken == null) return;
            WorldPickup.DropStack(taken, transform.position + transform.forward * 0.7f + Vector3.up * 0.4f);
            GameEvents.Raise(GameEventType.ItemDropped, taken.item.id, taken.count, transform.position);
        }

        /// <summary>add to inventory, spill the rest on the ground; returns how many went into the inventory</summary>
        public int GiveOrDrop(ItemDefinition item, int count)
        {
            if (item == null || count <= 0) return 0;
            int left = Inventory.Add(item, count);
            if (left > 0)
            {
                WorldPickup.Drop(item, left, transform.position + transform.forward * 0.6f + Vector3.up * 0.4f);
                Notify(Inventory.IsOverweight || Inventory.SpaceFor(item, true) > 0 ? "Too heavy. Some items were left on the ground." : "Inventory full. Some items were left on the ground.");
            }
            return count - left;
        }

        public bool HasTool(ToolKind k, out ItemDefinition tool)
        {
            tool = ActiveItem;
            if (tool != null && (tool.tool & k) != 0) return true;
            tool = null; return false;
        }
    }
}
