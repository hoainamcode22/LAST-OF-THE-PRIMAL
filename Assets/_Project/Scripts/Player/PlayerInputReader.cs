using UnityEngine;
using UnityEngine.InputSystem;

namespace PrimalFrontier.Player
{
    /// <summary>
    /// Keyboard/mouse + gamepad input built in code (no asset dependency), plus the on-screen touch controls
    /// (<see cref="Virtual"/>, written by MobileHUD). Other systems read the public properties; one-frame buttons use
    /// *Pressed. UI can block gameplay input with <see cref="GameplayBlocked"/>. Gameplay never reads touch directly.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class PlayerInputReader : MonoBehaviour
    {
        public static PlayerInputReader Instance { get; private set; }

        public Vector2 Move { get; private set; }
        public Vector2 Look { get; private set; }         // degrees this frame (already scaled by sensitivity)
        public float Zoom { get; private set; }
        public bool Sprint { get; private set; }
        public bool Walk { get; private set; }
        public bool Aim { get; private set; }
        public bool JumpPressed { get; private set; }
        public bool CrouchPressed { get; private set; }
        public bool InteractPressed { get; private set; }
        public bool InteractHeld { get; private set; }
        public bool AttackPressed { get; private set; }
        public bool AttackHeld { get; private set; }
        public bool InventoryPressed { get; private set; }
        public bool JournalPressed { get; private set; }
        public bool CraftPressed { get; private set; }
        public bool PausePressed { get; private set; }
        public int HotbarPressed { get; private set; } = -1;   // 0..7, -1 none
        public float HotbarScroll { get; private set; }         // mouse wheel: hotbar / build rotation (camera zoom is on +/-)
        public bool DropPressed { get; private set; }
        public bool RotatePressed { get; private set; }
        public bool CancelPressed { get; private set; }         // right mouse / Esc in build mode
        public bool DodgePressed { get; private set; }

        /// <summary>on-screen controls (MobileHUD) write here; merged with keyboard / gamepad every frame</summary>
        public static class Virtual
        {
            public static bool Active;                          // touch HUD is showing
            public static Vector2 Move;                         // joystick, -1..1
            public static Vector2 LookDelta;                    // degrees since last frame (consumed)
            public static bool Sprint, Aim, AttackHeld, InteractHeld;
            public static bool Jump, Crouch, Attack, Interact, Dodge, Inventory, Journal, Craft, Pause, Drop, Rotate;   // one frame
            public static int Hotbar = -1;
            public static void ClearFrame() { LookDelta = Vector2.zero; Jump = Crouch = Attack = Interact = Dodge = Inventory = Journal = Craft = Pause = Drop = Rotate = false; Hotbar = -1; }
        }

        [Tooltip("degrees per mouse pixel")] public float mouseSensitivity = 0.12f;
        [Tooltip("degrees per second at full stick")] public float stickSensitivity = 160f;
        public bool invertY;
        /// <summary>Set by UI (inventory, journal, pause): gameplay input reads as zero and the cursor is free.</summary>
        public bool GameplayBlocked { get; set; }

        /// <summary>Automated tests / cutscenes drive the character through this instead of devices.</summary>
        public static bool Simulate;
        public class SimState { public Vector2 Move, Look; public bool Sprint, Walk, Aim, Jump, Crouch, Interact, InteractHold, Attack, AttackHold, Inventory, Journal, Craft, Pause, Drop, Dodge; public int Hotbar = -1; }
        public static readonly SimState Sim = new SimState();

        InputAction _move, _look, _lookStick, _zoom, _zoomKeys, _drop, _rotate, _sprint, _walk, _aim, _jump, _crouch, _interact, _attack, _inv, _journal, _craft, _pause, _dodge;
        readonly InputAction[] _hot = new InputAction[8];

        void Awake()
        {
            Instance = this;
            _move = new InputAction("Move", InputActionType.Value);
            _move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s").With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            _move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow").With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            _move.AddBinding("<Gamepad>/leftStick");
            _look = new InputAction("Look", InputActionType.Value, "<Mouse>/delta");
            _lookStick = new InputAction("LookStick", InputActionType.Value, "<Gamepad>/rightStick");
            _zoom = new InputAction("Scroll", InputActionType.Value, "<Mouse>/scroll/y");
            _zoomKeys = new InputAction("ZoomKeys", InputActionType.Value);
            _zoomKeys.AddCompositeBinding("1DAxis").With("Positive", "<Keyboard>/equals").With("Negative", "<Keyboard>/minus");
            _zoomKeys.AddCompositeBinding("1DAxis").With("Positive", "<Keyboard>/numpadPlus").With("Negative", "<Keyboard>/numpadMinus");
            _drop = new InputAction("Drop", InputActionType.Button, "<Keyboard>/g");
            _rotate = new InputAction("Rotate", InputActionType.Button, "<Keyboard>/r");
            _sprint = new InputAction("Sprint", InputActionType.Button, "<Keyboard>/leftShift"); _sprint.AddBinding("<Gamepad>/leftStickPress");
            _walk = new InputAction("Walk", InputActionType.Button, "<Keyboard>/leftAlt");
            _aim = new InputAction("Aim", InputActionType.Button, "<Mouse>/rightButton"); _aim.AddBinding("<Gamepad>/leftTrigger");
            _jump = new InputAction("Jump", InputActionType.Button, "<Keyboard>/space"); _jump.AddBinding("<Gamepad>/buttonSouth");
            _crouch = new InputAction("Crouch", InputActionType.Button, "<Keyboard>/c"); _crouch.AddBinding("<Keyboard>/leftCtrl"); _crouch.AddBinding("<Gamepad>/buttonEast");
            _interact = new InputAction("Interact", InputActionType.Button, "<Keyboard>/e"); _interact.AddBinding("<Gamepad>/buttonWest");
            _attack = new InputAction("Attack", InputActionType.Button, "<Mouse>/leftButton"); _attack.AddBinding("<Gamepad>/rightTrigger");
            _inv = new InputAction("Inventory", InputActionType.Button, "<Keyboard>/tab"); _inv.AddBinding("<Keyboard>/i"); _inv.AddBinding("<Gamepad>/select");
            _journal = new InputAction("Journal", InputActionType.Button, "<Keyboard>/j");
            _craft = new InputAction("Craft", InputActionType.Button, "<Keyboard>/q");
            _pause = new InputAction("Pause", InputActionType.Button, "<Keyboard>/escape"); _pause.AddBinding("<Gamepad>/start");
            _dodge = new InputAction("Dodge", InputActionType.Button, "<Keyboard>/v"); _dodge.AddBinding("<Gamepad>/buttonNorth");
            for (int i = 0; i < 8; i++) _hot[i] = new InputAction("Hotbar" + (i + 1), InputActionType.Button, $"<Keyboard>/{i + 1}");
        }

        void OnEnable()
        {
            foreach (var a in All()) a.Enable();
        }

        void OnDisable()
        {
            foreach (var a in All()) a?.Disable();
        }

        void OnDestroy()
        {
            foreach (var a in All()) a?.Dispose();
            if (Instance == this) Instance = null;
        }

        System.Collections.Generic.IEnumerable<InputAction> All()
        {
            yield return _move; yield return _look; yield return _lookStick; yield return _zoom; yield return _zoomKeys; yield return _drop; yield return _rotate; yield return _sprint; yield return _walk; yield return _aim;
            yield return _jump; yield return _crouch; yield return _interact; yield return _attack; yield return _inv; yield return _journal; yield return _craft; yield return _pause; yield return _dodge;
            foreach (var h in _hot) yield return h;
        }

        void Update()
        {
            // menu keys work even while gameplay is blocked
            InventoryPressed = _inv.WasPressedThisFrame() || (Simulate && Sim.Inventory) || Virtual.Inventory;
            JournalPressed = _journal.WasPressedThisFrame() || (Simulate && Sim.Journal) || Virtual.Journal;
            CraftPressed = _craft.WasPressedThisFrame() || (Simulate && Sim.Craft) || Virtual.Craft;
            PausePressed = _pause.WasPressedThisFrame() || (Simulate && Sim.Pause) || Virtual.Pause;
            if (Simulate) Sim.Inventory = Sim.Journal = Sim.Craft = Sim.Pause = false;
            CancelPressed = PausePressed || (!Simulate && Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame);
            if (GameplayBlocked)
            {
                Move = Vector2.zero; Look = Vector2.zero; Zoom = 0; Sprint = Walk = Aim = false;
                JumpPressed = CrouchPressed = InteractPressed = InteractHeld = AttackPressed = AttackHeld = DropPressed = RotatePressed = DodgePressed = false;
                HotbarPressed = -1; HotbarScroll = 0;
                Virtual.ClearFrame();
                return;
            }
            if (Simulate)
            {
                Move = Vector2.ClampMagnitude(Sim.Move, 1f); Look = Sim.Look; Zoom = 0; Sprint = Sim.Sprint; Walk = Sim.Walk; Aim = Sim.Aim;
                JumpPressed = Sim.Jump; CrouchPressed = Sim.Crouch; InteractPressed = Sim.Interact; InteractHeld = Sim.Interact || Sim.InteractHold;
                AttackPressed = Sim.Attack; AttackHeld = Sim.Attack || Sim.AttackHold; HotbarPressed = Sim.Hotbar; HotbarScroll = 0;
                DropPressed = Sim.Drop; RotatePressed = false; DodgePressed = Sim.Dodge;
                Sim.Jump = Sim.Crouch = Sim.Interact = Sim.Attack = Sim.Drop = Sim.Dodge = false; Sim.Hotbar = -1;       // one-frame buttons
                return;
            }
            Move = Vector2.ClampMagnitude(_move.ReadValue<Vector2>(), 1f);
            var md = _look.ReadValue<Vector2>() * mouseSensitivity;
            var sd = _lookStick.ReadValue<Vector2>() * stickSensitivity * Time.unscaledDeltaTime;
            var look = md + sd; if (invertY) look.y = -look.y;
            Look = Cursor.lockState == CursorLockMode.Locked || sd.sqrMagnitude > 0 ? look : Vector2.zero;
            Zoom = _zoomKeys.ReadValue<float>() * 6f;
            Sprint = _sprint.IsPressed(); Walk = _walk.IsPressed(); Aim = _aim.IsPressed();
            JumpPressed = _jump.WasPressedThisFrame(); CrouchPressed = _crouch.WasPressedThisFrame();
            InteractPressed = _interact.WasPressedThisFrame(); InteractHeld = _interact.IsPressed();
            AttackPressed = _attack.WasPressedThisFrame(); AttackHeld = _attack.IsPressed();
            HotbarPressed = -1;
            for (int i = 0; i < 8; i++) if (_hot[i].WasPressedThisFrame()) HotbarPressed = i;
            HotbarScroll = _zoom.ReadValue<float>();
            DropPressed = _drop.WasPressedThisFrame(); RotatePressed = _rotate.WasPressedThisFrame();
            DodgePressed = _dodge.WasPressedThisFrame();
            if (Virtual.Active)
            {
                if (Virtual.Move.sqrMagnitude > Move.sqrMagnitude) Move = Vector2.ClampMagnitude(Virtual.Move, 1f);
                Look += Virtual.LookDelta;
                Sprint |= Virtual.Sprint; Aim |= Virtual.Aim;
                JumpPressed |= Virtual.Jump; CrouchPressed |= Virtual.Crouch; DodgePressed |= Virtual.Dodge;
                AttackPressed |= Virtual.Attack; AttackHeld |= Virtual.AttackHeld || Virtual.Attack;
                InteractPressed |= Virtual.Interact; InteractHeld |= Virtual.InteractHeld || Virtual.Interact;
                DropPressed |= Virtual.Drop; RotatePressed |= Virtual.Rotate;
                if (Virtual.Hotbar >= 0) HotbarPressed = Virtual.Hotbar;
            }
            Virtual.ClearFrame();
        }
    }
}
