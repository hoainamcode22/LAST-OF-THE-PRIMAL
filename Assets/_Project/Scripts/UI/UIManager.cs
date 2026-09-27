using System;
using UnityEngine;
using UnityEngine.EventSystems;
using PrimalFrontier.Core;
using PrimalFrontier.Player;

namespace PrimalFrontier.UI
{
    public enum UIScreen { None, Inventory, Journal, Pause, Title, Death }

    /// <summary>
    /// One screen at a time. Opening a screen frees the cursor and blocks gameplay input; the pause menu also
    /// stops time. Keys: Tab/I inventory, Q crafting, J journal, Esc pause / close.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }
        public UIScreen Current { get; private set; } = UIScreen.None;
        public event Action<UIScreen, UIScreen> Changed;
        public bool allowGameplayMenus = true;        // false during title / intro
        public Func<bool> BlockPause = () => false;   // build mode uses Esc to cancel

        void Awake() { Instance = this; EnsureEventSystem(); }
        void OnDestroy() { if (Instance == this) { Instance = null; Time.timeScale = 1f; } }

        public static void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include) != null) return;   // the scene's own ([Systems]/EventSystem)
            CreateEventSystem(null);
        }

        /// <summary>an EventSystem driven by the Input System (also used by the editor baker)</summary>
        public static GameObject CreateEventSystem(Transform parent)
        {
            var go = new GameObject("EventSystem", typeof(EventSystem));
            if (parent) go.transform.SetParent(parent, false);
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            return go;
        }

        public void Open(UIScreen s)
        {
            if (s == Current) return;
            var old = Current; Current = s;
            bool menu = s != UIScreen.None;
            var input = PlayerInputReader.Instance; if (input) input.GameplayBlocked = menu;
            ThirdPersonCamera.LockCursor(!menu);
            Time.timeScale = s == UIScreen.Pause ? 0f : 1f;
            Changed?.Invoke(old, s);
            if (s == UIScreen.Inventory) GameEvents.Raise(GameEventType.MenuOpened, "inventory");
            if (s == UIScreen.Journal) GameEvents.Raise(GameEventType.MenuOpened, "journal");
        }

        public void Close() => Open(UIScreen.None);

        void Update()
        {
            var i = PlayerInputReader.Instance; if (i == null) return;
            if (Current == UIScreen.Title || Current == UIScreen.Death) return;
            if (i.PausePressed)
            {
                if (Current != UIScreen.None) Close();
                else if (allowGameplayMenus && !BlockPause()) Open(UIScreen.Pause);
                return;
            }
            if (!allowGameplayMenus || Current == UIScreen.Pause) return;
            if (i.InventoryPressed) { if (Current == UIScreen.Inventory) Close(); else { Open(UIScreen.Inventory); InventoryUI.Instance?.ShowTab(0); } }
            else if (i.CraftPressed) { if (Current == UIScreen.Inventory && InventoryUI.Instance && InventoryUI.Instance.Tab == 1) Close(); else { Open(UIScreen.Inventory); InventoryUI.Instance?.ShowTab(1); } }
            else if (i.JournalPressed) { if (Current == UIScreen.Journal) Close(); else Open(UIScreen.Journal); }
        }
    }
}
