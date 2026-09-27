# PRIMAL FRONTIER - Mobile Guide (Phase 2)

Status: touch controls and quality presets are implemented and compile; **no phone build has been made or tested**
(NOT TESTED - MANUAL TEST REQUIRED). The touch HUD can be tried in the editor with `Settings > Touch controls: On`
(mouse clicks act as touches).

## 1. Touch controls (`[UI]/[Touch]`, `UI/MobileHUD`)

Shown automatically on phones / tablets (or when a touchscreen is the only pointer); `Settings > Touch controls`
= Auto / On / Off. Everything sits inside the device safe area (notch, rounded corners, home bar) and scales with the
screen (16:9, 18:9, 19.5:9, tablets). The HUD only writes `PlayerInputReader.Virtual`; gameplay never reads touch.

| Where | Control |
|---|---|
| left 42 % of the screen (lower part) | floating move stick: appears under the thumb, radius 110 px |
| right side | drag to look (sensitivity follows the mouse setting, invert Y respected) |
| bottom right | ATTACK (hold = heavy), JUMP, DODGE, contextual USE button (label USE / CLIMB / PICK / DRINK / FILL, only when there is something to use), AIM (only with spear or bow) |
| by the stick | RUN (toggle), CROUCH |
| top left, under the vitals | II (pause), BAG, CRAFT, BOOK |
| hotbar | tap a slot to hold that item |
| minimap | tap to open the whole island, tap again to close |
| build mode | ATTACK becomes BUILD, ROTATE and CANCEL appear, DODGE hides |

The layout is in the scene (`[UI]/[Touch]/SafeArea/...`): move or resize buttons there and save; the code keeps it.

## 2. Performance plan for phones
- Default preset **Low** on phones: Mobile URP asset, render scale 0.75, shadows 30 m / 1 cascade, no MSAA, LOD bias 0.7,
  grass 35 m at 40 % density, 24 pooled effects, 30 fps cap. Medium keeps 30 fps; High / Ultra ask for 60 fps.
- Minimap: one baked 1024 px picture (ASTC 6x6 on Android), no second camera.
- Creatures: AI level of detail (full < 90 m, slower thinking < 200 m, frozen beyond, Animator off), DinoLife skipped beyond 70 m.
- Water: `PF/Water` needs no depth or opaque texture.
- Effects: hard caps, far puffs skipped on Low, pooled (no instantiation after warm-up).
- Textures: phone overrides (see `Performance_Optimization.md`).

## 3. What to test on a device (manual)
1. Build Android (`File > Build Profiles`, Android, IL2CPP ARM64) with the island scene.
2. Check: stick and look at the same time (two fingers), buttons reachable with thumbs on 6" and 6.7" phones,
   safe area on a notched phone, hotbar taps, minimap tap, climbing / fruit picking with the USE button, building
   with BUILD / ROTATE / CANCEL, menus (BAG / CRAFT / BOOK / pause).
3. Frame rate at the camp, in the forest and at the meadow with the herd; temperature after 15 minutes.
4. If the frame rate is low: lower render scale (GameSettings arrays), grass density, shadow distance.
