# PRIMAL FRONTIER - Điều khiển PC / PC Controls

Bàn phím + chuột (Input System). Trong game: nhấn **F1** hoặc **Esc > CONTROLS / PHÍM** để xem bảng này.
Keyboard + mouse (Input System). In game: press **F1** or **Esc > CONTROLS / PHÍM** to see this table.

Nguồn / Source: `PlayerInputReader.cs` (các action tạo bằng code / actions built in code), `Minimap.cs` (M),
`PauseMenuUI.cs` (F1), `BuildSystem.cs`, `SlotView.cs` / `InventoryUI.cs`, `IntroSequence.cs`.
Bảng trong game (`ControlsGuide` trong `UI/ControlsPanel.cs`) được test `ControlsGuideTests` so với phím thật.
The in-game table (`ControlsGuide` in `UI/ControlsPanel.cs`) is checked against the real bindings by `ControlsGuideTests`.

## Di chuyển / Movement

| Hành động | Action | Phím / Key |
|---|---|---|
| Di chuyển | Move | W A S D / phím mũi tên (Arrow keys) |
| Nhìn / xoay camera | Look / camera | Chuột (Mouse) |
| Thu phóng camera | Camera zoom | `=` / `-` (Numpad `+` / `-`) |
| Chạy nhanh (khi đi tới) | Sprint (moving forward) | Left Shift (giữ / hold) |
| Đi chậm | Walk slowly | Left Alt (giữ / hold) |
| Ngồi xuống / đứng dậy | Crouch on / off | C hoặc / or Left Ctrl |
| Nhảy | Jump | Space |
| Né | Dodge | V |

## Chiến đấu / Combat

| Hành động | Action | Phím / Key |
|---|---|---|
| Tấn công | Attack | Chuột trái (Left mouse) |
| Đòn mạnh (vũ khí có đòn mạnh) | Heavy attack (weapons that have one) | Giữ chuột trái (Hold left mouse) |
| Ngắm | Aim | Chuột phải, giữ (Right mouse, hold) |
| Cung: kéo / bắn | Bow: draw / shoot | Giữ chuột phải, giữ chuột trái để kéo, thả để bắn (Hold RMB, hold LMB, release) |
| Ném giáo | Throw spear | Giữ chuột phải + chuột trái (Hold RMB + left mouse) |
| Ăn / uống đồ đang cầm | Eat / drink held item | Chuột trái (Left mouse) |

## Tương tác / Interaction

| Hành động | Action | Phím / Key |
|---|---|---|
| Tương tác / nhặt | Interact / pick up | E |
| Thao tác giữ (khi có gợi ý giữ) | Hold action (when the prompt says hold) | Giữ E (Hold E) |
| Leo cây | Climb | E, rồi W / S; Space hoặc C để buông (E, then W / S; Space or C lets go) |
| Vứt đồ đang cầm | Drop held item | G |
| Ô nhanh | Quick slots | 1 - 8 |
| Đổi ô nhanh | Next / previous slot | Con lăn chuột (Mouse wheel) |

## Xây dựng / Building

| Hành động | Action | Phím / Key |
|---|---|---|
| Bắt đầu xây | Start building | Cầm đồ trại (lửa trại, lều...) ở ô nhanh, chuột trái (Hold a camp item, left mouse) |
| Đặt | Place | Chuột trái (Left mouse) |
| Xoay | Rotate | R (45°) / con lăn chuột (Mouse wheel, 15°) |
| Hủy | Cancel | Chuột phải / Esc (Right mouse / Esc) |

## Menu / Menus

| Hành động | Action | Phím / Key |
|---|---|---|
| Túi đồ | Inventory | Tab hoặc / or I |
| Chế tạo | Crafting | Q |
| Nhật ký | Journal | J |
| Bản đồ đảo | Island map | M |
| Bảng phím | Controls | F1 |
| Tạm dừng / đóng menu | Pause / close menu | Esc |

## Túi đồ / Inventory (chuột / mouse)

| Hành động | Action | Phím / Key |
|---|---|---|
| Di chuyển đồ | Move an item | Kéo bằng chuột trái (Drag with left mouse) |
| Dùng / trang bị | Use / equip | Nhấp đúp (Double click) |
| Dùng | Use | Chuột phải (Right click) |
| Chuyển sang / từ rương | Move to / from storage | Shift + chuột trái (Shift + left click) |

## Khác / Other

| Hành động | Action | Phím / Key |
|---|---|---|
| Bỏ qua đoạn mở đầu | Skip the intro | Space / Esc |

## Tay cầm / Gamepad (đã gán một phần / partly bound)

| Hành động | Action | Nút / Button |
|---|---|---|
| Di chuyển | Move | Cần trái (Left stick) |
| Nhìn | Look | Cần phải (Right stick) |
| Chạy nhanh | Sprint | Nhấn cần trái (Left stick press) |
| Ngắm | Aim | LT |
| Tấn công | Attack | RT |
| Nhảy | Jump | A / Cross (South) |
| Ngồi | Crouch | B / Circle (East) |
| Tương tác | Interact | X / Square (West) |
| Né | Dodge | Y / Triangle (North) |
| Túi đồ | Inventory | Select / View |
| Tạm dừng | Pause | Start / Menu |

Chưa gán trên tay cầm / not bound on gamepad: chế tạo (crafting), nhật ký (journal), bản đồ (map), vứt đồ (drop),
xoay khi xây (rotate), ô nhanh 1-8 (quick slots), thu phóng (zoom), bảng phím (controls).
