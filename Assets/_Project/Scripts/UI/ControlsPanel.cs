using UnityEngine;
using UnityEngine.UI;

namespace PrimalFrontier.UI
{
    /// <summary>
    /// The PC (keyboard + mouse) controls, taken from the real bindings: PlayerInputReader (actions built in code),
    /// Minimap (M), PauseMenuUI (F1), BuildSystem / SlotView / IntroSequence (mouse and Shift). <see cref="Row.actions"/> /
    /// <see cref="Row.paths"/> name the InputActions and binding paths a row shows; ControlsGuideTests checks them against
    /// PlayerInputReader, so a changed binding fails the test until this table (and docs/CONTROLS.md) follow.
    /// </summary>
    public static class ControlsGuide
    {
        public struct Row
        {
            public string label, key;          // "Sprint (Chạy nhanh)", "Left Shift (hold)"; label only = group header
            public string[] actions, paths;    // InputAction names and their keyboard / mouse binding paths (null: not an action)
            public int column;
            public bool IsHeader => key == null;
        }

        static Row H(string label, int col) => new Row { label = label, column = col };
        static Row R(string label, string key, int col, string[] actions = null, params string[] paths) => new Row { label = label, key = key, column = col, actions = actions, paths = paths };
        static string[] A(params string[] a) => a;

        public static readonly Row[] Rows =
        {
            H("MOVEMENT  ·  DI CHUYỂN", 0),
            R("Move (Di chuyển)", "W A S D / Arrow keys", 0, A("Move"), "<Keyboard>/w", "<Keyboard>/s", "<Keyboard>/a", "<Keyboard>/d", "<Keyboard>/upArrow", "<Keyboard>/downArrow", "<Keyboard>/leftArrow", "<Keyboard>/rightArrow"),
            R("Look / camera (Nhìn / xoay camera)", "Mouse", 0, A("Look"), "<Mouse>/delta"),
            R("Camera zoom (Thu phóng camera)", "= / -  (Numpad + / -)", 0, A("ZoomKeys"), "<Keyboard>/equals", "<Keyboard>/minus", "<Keyboard>/numpadPlus", "<Keyboard>/numpadMinus"),
            R("Sprint (Chạy nhanh)", "Left Shift (hold)", 0, A("Sprint"), "<Keyboard>/leftShift"),
            R("Walk slowly (Đi chậm)", "Left Alt (hold)", 0, A("Walk"), "<Keyboard>/leftAlt"),
            R("Crouch on / off (Ngồi xuống / đứng dậy)", "C or Left Ctrl", 0, A("Crouch"), "<Keyboard>/c", "<Keyboard>/leftCtrl"),
            R("Jump (Nhảy)", "Space", 0, A("Jump"), "<Keyboard>/space"),
            R("Dodge (Né)", "V", 0, A("Dodge"), "<Keyboard>/v"),

            H("COMBAT  ·  CHIẾN ĐẤU", 0),
            R("Attack (Tấn công)", "Left mouse", 0, A("Attack"), "<Mouse>/leftButton"),
            R("Heavy attack (Đòn mạnh)", "Hold left mouse", 0, A("Attack"), "<Mouse>/leftButton"),
            R("Aim (Ngắm)", "Right mouse (hold)", 0, A("Aim"), "<Mouse>/rightButton"),
            R("Bow: draw / shoot (Cung: kéo / bắn)", "Hold RMB, hold LMB, release", 0),
            R("Throw spear (Ném giáo)", "Hold RMB + left mouse", 0),
            R("Eat / drink held item (Ăn / uống)", "Left mouse", 0),

            H("INTERACTION  ·  TƯƠNG TÁC", 0),
            R("Interact / pick up (Tương tác / nhặt)", "E", 0, A("Interact"), "<Keyboard>/e"),
            R("Hold action (Thao tác giữ)", "Hold E", 0, A("Interact"), "<Keyboard>/e"),
            R("Climb (Leo cây)", "E; W / S; Space or C lets go", 0),
            R("Drop held item (Vứt đồ đang cầm)", "G", 0, A("Drop"), "<Keyboard>/g"),
            R("Quick slots (Ô nhanh)", "1 - 8", 0, A("Hotbar1", "Hotbar2", "Hotbar3", "Hotbar4", "Hotbar5", "Hotbar6", "Hotbar7", "Hotbar8"),
              "<Keyboard>/1", "<Keyboard>/2", "<Keyboard>/3", "<Keyboard>/4", "<Keyboard>/5", "<Keyboard>/6", "<Keyboard>/7", "<Keyboard>/8"),
            R("Next / previous slot (Đổi ô)", "Mouse wheel", 0, A("Scroll"), "<Mouse>/scroll/y"),

            H("BUILDING  ·  XÂY DỰNG", 1),
            R("Start building (Bắt đầu xây)", "Hold a camp item, left mouse", 1),
            R("Place (Đặt)", "Left mouse", 1),
            R("Rotate (Xoay)", "R (45°) / Mouse wheel (15°)", 1, A("Rotate"), "<Keyboard>/r"),
            R("Cancel (Hủy)", "Right mouse / Esc", 1),

            H("MENUS  ·  MENU", 1),
            R("Inventory (Túi đồ)", "Tab or I", 1, A("Inventory"), "<Keyboard>/tab", "<Keyboard>/i"),
            R("Crafting (Chế tạo)", "Q", 1, A("Craft"), "<Keyboard>/q"),
            R("Journal (Nhật ký)", "J", 1, A("Journal"), "<Keyboard>/j"),
            R("Island map (Bản đồ đảo)", "M", 1),
            R("Controls (Bảng phím)", "F1", 1),
            R("Pause / close menu (Tạm dừng / đóng)", "Esc", 1, A("Pause"), "<Keyboard>/escape"),

            H("INVENTORY  ·  TÚI ĐỒ", 1),
            R("Move an item (Di chuyển đồ)", "Drag with left mouse", 1),
            R("Use / equip (Dùng / trang bị)", "Double click", 1),
            R("Use (Dùng)", "Right click", 1),
            R("Move to / from storage (Chuyển rương)", "Shift + left click", 1),

            H("OTHER  ·  KHÁC", 1),
            R("Skip the intro (Bỏ qua mở đầu)", "Space / Esc", 1),
        };
    }

    /// <summary>
    /// Controls screen inside the pause menu (the same leather / stone look as the settings block). Opened with F1 in
    /// play or the pause menu's CONTROLS button (PauseMenuUI). The rows are re-texted from <see cref="ControlsGuide"/>
    /// on every build, so the table always matches the code; layout, fonts and colours can be edited in the scene.
    /// </summary>
    public static class ControlsPanel
    {
        public static RectTransform Build(Transform parent, System.Action onBack)
        {
            var c = new Vector2(0.5f, 0.5f);
            var p = UIFactory.Image(parent, "Controls", UIStyle.Leather, Color.white, c, c, c, Vector2.zero, new Vector2(1400, 920), true).rectTransform;
            UIFactory.Label(p, "Title", "CONTROLS  ·  PHÍM ĐIỀU KHIỂN", 38, UIStyle.Text, TextAnchor.UpperCenter, UIStyle.Head, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -28), new Vector2(0, 50));
            UIFactory.Label(p, "Sub", "Keyboard + mouse  ·  Bàn phím + chuột", 19, UIStyle.TextDim, TextAnchor.UpperCenter, UIStyle.Body, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -80), new Vector2(0, 28));
            var cols = new[]
            {
                UIFactory.Rect(p, "ColA", new Vector2(0, 1), new Vector2(0.5f, 1), new Vector2(0, 1), new Vector2(50, -126), new Vector2(-70, 700)),
                UIFactory.Rect(p, "ColB", new Vector2(0.5f, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(20, -126), new Vector2(-70, 700)),
            };
            const float line = 29f;
            var y = new float[2];
            var rows = ControlsGuide.Rows;
            for (int i = 0; i < rows.Length; i++)
            {
                var r = rows[i]; int col = Mathf.Clamp(r.column, 0, 1); var parentCol = cols[col];
                if (r.IsHeader)
                {
                    if (y[col] < 0f) y[col] -= 8f;                           // a little air above every group but the first
                    var h = UIFactory.Label(parentCol, "H" + i, r.label, 21, UIStyle.Accent, TextAnchor.MiddleLeft, UIStyle.Head, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(0, y[col]), new Vector2(0, line));
                    h.text = r.label;
                    y[col] -= line + 2f;
                    continue;
                }
                Cell(parentCol, "R" + i + "L", r.label, UIStyle.Text, 0f, 0.55f, 8f, y[col], line);
                Cell(parentCol, "R" + i + "K", r.key, UIStyle.Accent, 0.55f, 1f, 6f, y[col], line);
                y[col] -= line;
            }
            UIFactory.Label(p, "Hint", "F1 / Esc: close  ·  đóng", 17, UIStyle.TextDim, TextAnchor.LowerLeft, UIStyle.Body, new Vector2(0, 0), new Vector2(0.5f, 0), new Vector2(0, 0), new Vector2(50, 36), new Vector2(-50, 26));
            UIFactory.Button(p, "Back", "BACK  ·  QUAY LẠI", () => onBack?.Invoke(), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 26), new Vector2(300, 54), 22);
            return p;
        }

        /// <summary>one table cell, always re-texted; a new cell shrinks its font to stay on one line</summary>
        static void Cell(RectTransform col, string name, string text, Color color, float x0, float x1, float pad, float y, float h)
        {
            var t = UIFactory.Label(col, name, text, 18, color, TextAnchor.MiddleLeft, UIStyle.Body, new Vector2(x0, 1), new Vector2(x1, 1), new Vector2(0, 1), new Vector2(pad, y), new Vector2(-pad, h));
            if (UIFactory.Fresh(t)) { t.verticalOverflow = VerticalWrapMode.Truncate; t.resizeTextForBestFit = true; t.resizeTextMinSize = 12; t.resizeTextMaxSize = 18; }
            t.text = text;
        }
    }
}
