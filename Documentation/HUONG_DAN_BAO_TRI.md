# PRIMAL FRONTIER - Hướng dẫn bảo trì bằng tay (không cần code)

Mọi thứ trong game (người chơi, khủng long, UI, hệ thống) đều là object thật trong scene. Bạn chỉnh bằng cách
kéo thả trong Hierarchy / Scene và sửa số trong Inspector, rồi **Ctrl+S**. Code chỉ đọc lại những gì bạn chỉnh.

## 1. Mở đúng project và đúng scene

| Việc | Cách làm |
|---|---|
| Project | Unity Hub > Open > chọn thư mục `E:\LAST OF THE PRIMAL` (không phải thư mục `My project` bên trong, đó là project trống Unity tự tạo). |
| Scene game | `Assets/_Project/Scenes/Island_VerticalSlice`. Nếu Unity mở ra "Untitled" hoặc "SampleScene" (scene mẫu trống), nó sẽ tự chuyển sang scene này. Có thể mở tay: menu **Primal Frontier > Scene > Open Island Scene**. |
| Lần đầu | Unity hỏi "Bake now?" thì bấm **Bake now**, hoặc vào menu **Primal Frontier > Scene > Bake Everything Into Scene**. Việc này đưa Player, khủng long, UI, hệ thống vào Hierarchy và lưu scene. Chạy lại bao nhiêu lần cũng an toàn: nó chỉ thêm cái còn thiếu, không xóa hay đặt lại cái bạn đã sửa. |
| Chơi thử | Bấm Play. Ra màn hình tiêu đề, chọn New Game. |

## 2. Bản đồ Hierarchy

| Object | Là gì | Chỉnh gì ở đây |
|---|---|---|
| `[Systems]/Time` | ngày đêm | độ dài 1 giờ trong game (Seconds Per Hour), giờ mặt trời mọc/lặn, màu trời, sương mù |
| `[Systems]/Weather` | mưa, bão | tỉ lệ mưa mỗi giờ, thời gian mưa, số hạt mưa |
| `[Systems]/Ambience` | âm thanh nền | clip biển / rừng / đêm / gió / mưa, âm lượng từng lớp |
| `[Systems]/Journal` | nhật ký | danh sách trang (Entries): tiêu đề, nội dung, hình vẽ, sự kiện mở khóa |
| `[Systems]/Tutorial` | hướng dẫn 19 bước | chữ mục tiêu và gợi ý từng bước (Texts) |
| `[Systems]/VfxPool`, `SfxPlayer`, `BloodDecals` | hiệu ứng, âm thanh, vệt máu | thư viện hiệu ứng / âm thanh, giới hạn số lượng |
| `[Systems]/Input`, `EventSystem`, `Build`, `Trees`, `OceanShore`, `Intro` | điều khiển, đặt công trình, chặt cây, nước biển, đoạn mở đầu | thường không cần đụng |
| `[Gameplay]/[Game]` | GameManager | điểm xuất phát, bật/tắt màn hình tiêu đề và intro, giờ bắt đầu |
| `[Gameplay]/[Dinosaurs]/<Loài>/...` | từng con khủng long | vị trí, hướng, vùng sống (Home Radius), loài (Def) |
| `[Gameplay]/[Zones]` | vùng có tên (Bãi biển, Xác tàu...) | tên, tâm, bán kính, nhiệt độ |
| `[Gameplay]/Cave`, `CaptainsLog`, `GiantFootprints` | hang, nhật ký thuyền trưởng, dấu chân | vị trí, lời thoại |
| `Player` | nhân vật | vị trí xuất phát, máu, sinh tồn, túi đồ (số ô, cân nặng tối đa) |
| `Main Camera` | camera góc nhìn thứ ba | khoảng cách, độ cao, FOV, vai trái/phải, góc ngẩng tối đa |
| `[UI]/[HUD]`, `[HUD Top]` | giao diện lúc chơi | thanh máu, la bàn, mục tiêu, hotbar, gợi ý phím, phụ đề |
| `[UI]/[Inventory]`, `[Journal]`, `[Pause]`, `[Title]`, `[Death]` | các màn hình menu (đang tắt, game tự bật khi cần) | bố cục, màu, chữ |
| `World/Resources`, `Shipwreck`, `Props`, `Vegetation`, `Cliffs`, `Rocks` | tài nguyên, xác tàu, đồ vật, cây đổ, vách đá, đá | kéo, xoay, nhân bản, xóa |
| `Markers` | điểm đánh dấu (vùng sống, vùng tài nguyên) | vị trí tham chiếu |
| `Water`, `ENV_Island_Terrain`, `Sun`, `Global Volume` | nước, địa hình, mặt trời, hậu kỳ | ánh sáng, màu |

## 3. Việc hay làm

**Đổi chỗ xuất phát của người chơi:** kéo object `Player` tới chỗ mới rồi lưu. (Muốn dùng điểm `ZONE_PlayerSpawn` thì tắt
`Start Where Player Is Placed` ở `[Gameplay]/[Game]`.)

**Thêm / bớt / dời khủng long:**
- Dời: chọn con đó trong `[Gameplay]/[Dinosaurs]`, kéo trong Scene. Vòng tròn khi chọn là vùng sống của nó (Home Radius).
- Thêm một con cùng loài: chọn một con, **Ctrl+D**, kéo bản sao ra chỗ khác.
- Thêm loài khác: kéo prefab `Assets/Art/Characters/Dinosaurs/<Loài>/Prefab/DINO_<Loài>` vào nhóm, Add Component
  `Dinosaur Controller` (bay/bơi thì `Ambient Creature`), gán `Def` = `Assets/_Project/Data/Dinosaurs/DINO_<Loài>`.
- Bớt: xóa object, hoặc bỏ dấu tick để tạm tắt.
- Khi New Game hoặc Load, mọi con được đặt lại đúng chỗ bạn để trong editor và sống lại.

**Chỉnh độ khó khủng long:** `Assets/_Project/Data/Dinosaurs/DINO_*.asset` (máu, tốc độ, tầm nhìn, sát thương, thời gian
hồi đòn, thịt/da/xương rơi ra). Sửa một file là áp dụng cho cả loài.

**Tài nguyên (gỗ, đá, sợi, quả):** trong `World/Resources`, Ctrl+D để thêm, kéo để dời. Prefab gốc ở
`Assets/_Project/Prefabs/Resources` (sửa prefab là đổi tất cả).

**Đồ vật và công thức chế tạo:** `Assets/_Project/Data/Items` (tên, icon, cân nặng, độ bền, dinh dưỡng) và
`Assets/_Project/Data/Recipes` (nguyên liệu, thời gian). Đồ vật hoặc công thức mới phải được thêm vào danh sách
Items / Recipes trong `Assets/_Project/Resources/ItemDatabase`.

**Giao diện (UI):**
- Chọn object trong `[UI]`, bấm phím **T** (Rect Tool) rồi kéo, đổi cỡ trong Scene view (bật nút 2D cho dễ nhìn).
- Đổi màu, sprite, font, cỡ chữ, nội dung chữ cố định (ví dụ "PAUSED") ngay trong Inspector.
- Muốn sửa một menu đang tắt (Inventory, Pause...): tick bật nó lên, sửa xong tắt lại cũng được, không tắt cũng không sao
  (game tự ẩn menu khi Play).
- Được thêm hình trang trí, chữ mới tùy ý.
- **Không đổi tên** các object UI có sẵn: code tìm chúng theo tên. Nếu lỡ xóa một phần, bấm Play là nó tự hiện lại theo
  mặc định. Muốn lưu lại phần đó vào scene thì chạy lại **Bake Everything Into Scene**.
- Chữ nào game tự điền lúc chơi (máu, giờ, mục tiêu, gợi ý nhặt đồ) thì sửa trong scene sẽ bị thay khi chơi. Đó là bình thường.

**Chữ hướng dẫn và nhật ký:** `[Systems]/Tutorial` > Texts (mục tiêu, gợi ý từng bước; ô để trống thì dùng chữ mặc
định), `[Systems]/Journal` > Entries (tiêu đề, nội dung từng trang; chuột phải vào component > Reset pages để lấy lại
chữ gốc).

**Ngày đêm, thời tiết:** `[Systems]/Time` > Seconds Per Hour (100 = một ngày khoảng 40 phút), `[Systems]/Weather` >
Rain Chance Per Hour. Màu trời đã chỉnh hỏng: chuột phải component Time > Reset colours to defaults.

## 4. Lưu đúng cách (quan trọng)

- **Ctrl+S** để lưu scene sau khi sửa.
- **Mọi thay đổi lúc đang Play sẽ mất khi bấm Stop** (luật của Unity). Muốn giữ: Stop trước rồi mới sửa.
- Object xanh dương trong Hierarchy là prefab. Sửa ở scene chỉ áp dụng cho object đó. Muốn áp cho mọi bản:
  Inspector > **Overrides > Apply All**.

## 5. Menu không nên bấm khi đang chỉnh tay

`Primal Frontier > Advanced (overwrites hand edits) > ...` là các máy tạo lại từ đầu (đảo, gameplay, khủng long, hiệu
ứng, nhân vật). Chúng **ghi đè** những gì bạn sửa tay trong phần chúng tạo lại, và luôn hỏi lại trước khi chạy. Làm việc
bình thường không cần đụng tới.

## 6. Lỡ tay thì sao

- **Ctrl+Z** để hoàn tác.
- Chưa lưu thì đóng scene và chọn Don't Save.
- Đã lưu: `git checkout -- Assets/_Project/Scenes/Island_VerticalSlice.unity` để lấy lại bản đã commit.
- Mất Player / UI / hệ thống: chạy lại **Primal Frontier > Scene > Bake Everything Into Scene**.

## 7. Thư mục

| Thư mục | Nội dung |
|---|---|
| `Assets/_Project/Scenes` | scene game (`Island_VerticalSlice`) và scene thử điều khiển |
| `Assets/_Project/Prefabs` | prefab: Player, tài nguyên, công trình, đồ vật |
| `Assets/_Project/Data` | dữ liệu chỉnh bằng Inspector: đồ vật, công thức, khủng long, loot |
| `Assets/Art`, `Assets/_Project/Art` | mô hình, texture, vật liệu |
| `Assets/_Project/Audio`, `VFX` | âm thanh, hiệu ứng |
| `Assets/_Project/Scripts` | code (không cần mở để chỉnh game) |
| `Assets/Scenes/SampleScene`, `Assets/TutorialInfo`, `Assets/Readme` | đồ mẫu Unity tạo sẵn, không thuộc game, xóa được |
