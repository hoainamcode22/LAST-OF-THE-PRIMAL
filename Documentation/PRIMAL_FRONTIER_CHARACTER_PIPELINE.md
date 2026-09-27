# PRIMAL FRONTIER - Character Pipeline (Phase 2)

All characters are original, made by code in Blender 5.2 (`Tools/BlenderPipeline`, working files in
`E:\Model game khủng long`) and imported into Unity by `PrimalCharacterBuilder`. No model, animation or texture from
another game or store.

## 1. Player: inspection first (checkpoint_player_rig_v2)

The brief asked to inspect the character before changing anything and fix root causes.

| Checked | Finding | Action |
|---|---|---|
| Skeleton (humanoid, Unity avatar) | valid, T-pose enforced by the builder, bone roll and hierarchy correct | kept (no rebuild) |
| Skin weights | every LOD0 vertex weighted, 100 % of vertices follow the bones in Walk | kept |
| Deformation in extreme poses (shoulder raise, deep crouch, twist) | acceptable; no candy-wrapper at elbows / knees | twist bones **not** added (not needed for this camera distance) |
| Why the player looked stiff | **animation authoring**: arms straight and close to the body, small arm swing, frozen idle; no IK; snapping turns; idle variation clip never triggered | reworked clips, added IK, smoothed turning, idle variation wired |

## 2. Animation v2 (checkpoint_player_animation_v2)

Authoring: `pf_anim.HumanRig` (arm / elbow / wrist / clavicle / fingers / spine / pelvis controls, leg IK with planted feet)
and `pf_clips_human.build_all` (previous version kept as `pf_clips_human_v1_backup.py`). Export:
`pf_char_export.export(..., face_unity_forward=True)` to `Assets/Art/Characters/Player`.

47 clips: Attack_Spear, Attack_Spear_Heavy, Spear_Attack_2, Knife_Attack, Throw_Spear, Bow_Aim, Bow_Draw, Bow_Release,
Dodge, Idle, Idle_Variation, Walk, Walk_Backward, Walk_Left, Walk_Right, Run, Sprint, Crouch, Crouch_Walk, Turn_Left,
Turn_Right, Jump, Fall, Land, Climb_Start, Climb_Idle, Climb_Up, Climb_Down, Climb_End, Harvest_Fruit, Gather_Wood,
Gather_Stone, Gather_Plant, Pickup, Carry_Item, Craft, Build, Interact, Use_Item, Eat, Drink, Hurt, Hurt_Heavy, Death,
Sleep, Wake_Up, Get_Up.

What changed in the motion: relaxed arms with elbow bend, fuller counter-swing of arms and shoulders, breathing idle with
weight shifts plus a 5 s idle variation, turn-in-place clips, attack anticipation and follow-through, climbing set.

Controller (built by `PrimalCharacterBuilder`): 36 base states + 5 upper-body states, IK pass on the base layer,
`iKOnFeet` on locomotion states, AnyState Dodge, climb states (Climb_Start -> Climb_Idle, Harvest_Fruit -> Climb_Idle,
Climb_End -> locomotion), IdleVariant trigger.

## 3. Automated character test (Unity, `PrimalCharacterBuilder.BuildAndTest("Player")`)

Result **PASS** (report `Documentation/CharacterTests/Player_test.md`, contact sheet `Player_clips.png`):
- 47 clips imported, 24 loops seamless (0.0 cm gap), 55 animation events;
- walk / run foot sliding 1-2 % of the speed (climb clips are vertical and skip the ground foot-slide check);
- facing +Z, feet on the ground, 3 LODs (72,952 / 17,107 / 5,668 triangles), thresholds 0.25 / 0.08 / 0.01;
- skin: 0 unweighted vertices, 100 % of vertices move in Walk.

## 4. Dinosaurs

Procedural pipeline (`pf_dino.py`, `dino_specs.py`, `dino_tex.py`; SDF body -> decimated mesh, generic skeleton,
7-16 in-place clips, 3 LODs) for Triceratops, Parasaurolophus, Ankylosaurus, Velociraptor, Carnotaurus, Spinosaurus,
Pteranodon, Mosasaurus; hero pipeline (`dino_hero.py`) for the Rift Tyrant (46 bones, modelled mouth, teeth, tongue,
eyes and lids, 4 LODs). Each has a character test report in `Documentation/CharacterTests`.

### Eyes (Phase 2, `PrimalCreatureEyes`)
The modelled eyes of the 8 procedural species are small and sunk into the skin, so from play distance the faces looked
blank. The tool adds a glossy eyeball (`Eye_L` / `Eye_R`, 224-triangle sphere) under each creature's `Head` bone:
1. eye centres and radius come from the Blender build data (`characters/work/dino/<species>_joints.json`, copied into the
   tool);
2. the mesh's vertex space is found automatically (axis order, sign, units) by matching both eye centres to modelled
   vertices; the modelled eyeball's vertices are then read in the **posed** default mesh (baked skin), because the
   prefab's default pose differs from the bind pose (head raised);
3. the new eye sits at that centre, 1.25x the modelled size, slightly out of the socket, facing out and a little forward;
4. iris texture made by code (fibres, dark limbal ring; round pupil for plant eaters, slit for hunters), species colour,
   smooth and slightly emissive so the eye reads in shade; added to LOD0 / LOD1 of the creature's LOD group.
`DinoLife` makes them blink and glance at the player. Review shots: `Documentation/Screenshots/Review/pface_*.png`.

## 5. Other Phase 2 models
- Fruit tree (leaning fern-palm with fruit bunches under the crown) and fruit bunch: `pf_phase2_assets.py`.
- Volcano landmark (stratovolcano with a sector collapse scar, parasitic cone, breached crater, lava lake and flow):
  `pf_volcano.py`, 12,288 / 2,816 triangles + 84 lava.

## 6. How to redo a character
1. Blender: run the character's build script (see `Tools/BlenderPipeline`), export with `face_unity_forward=True`.
2. Unity: `Primal Frontier > Advanced (overwrites hand edits) > Re-import Player Model + Test` for the player (or bridge `PrimalCharacterBuilder.BuildAndTest`).
3. Read `Documentation/CharacterTests/<id>_test.md`; it must end with `RESULT: PASS`.
4. For creatures, run `Primal Frontier > Tools > Add Creature Eyes` again if the prefab was rebuilt (bridge `PrimalCreatureEyes.Build("force")`).
