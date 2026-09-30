# DINO report (D_): dinosaur clips, weight, eyes (PC phase, 2026-09-30)

Unity was closed the whole session: nothing was imported, compiled or tested by Unity. Blender work ran on the PC in
background Blender processes (started from the owner's interactive Blender with `subprocess`, on a copy; the interactive
scene was never touched). Status words: DONE-VERIFIED (how), DONE-NOT-TESTED, PARTIAL, NOT COMPLETED.

## 1. Audit matrix (before this pass -> now)

Legend: K kept as it was (verified identical, section 5), R reworked (was poor), N new (was missing), P present but poor
and not changed. Grazers = parasaurolophus (PARA), triceratops (TRI), ankylosaurus (ANK); hunters = velociraptor (VEL),
carnotaurus (CAR), spinosaurus (SPI), Rift Tyrant (APX).

| clip (directive 43) | before | PARA | TRI | ANK | VEL | CAR | SPI | APX |
|---|---|---|---|---|---|---|---|---|
| Idle | present (with breathing) | K | K | K | K | K | K | K |
| Breathing | only inside Idle | N Breathe | N | N | N (pant) | N (pant) | N (pant) | N (pant) |
| Look | present | K | K | K | K | K | K | K |
| Walk / Run | present, foot slide 2-4 % (Unity test) | K | K | K | K | K | K | K |
| Turn | poor: not wired in the controller, a slow walk in place (feet slide against the turn), no body bend | R | R | R | R | R | R | R |
| Stop | missing | N | N | N | N | N | N | N |
| Eat | poor: fixed neck angle + chew, mouth not on the ground, hunters "grazed" | R graze | R graze | R graze | R tear | R tear | R tear | R tear |
| Drink | poor: fixed neck angle, mouth not at the water | R | R | R | R | R | R | R |
| Rest | missing (standing Idle_Variation fidget) | N x4 | N x4 | N x4 | N x4 | N x4 | N x4 | N x4 |
| Alert | present | K | K | K | K | K | K | K |
| Call | missing (Roar reused) | N hoot | N bellow | N bellow | N barks | N rumble | N rumble | N rumble |
| Hurt | present | K | K | K | K | K | K | K |
| Death | present, limbs sink 12-74 cm under the ground while rolling | P | P | P | P | P | P | P |
| Chase (hunters) | missing (Run reused) | | | | N | N | N | N |
| Attack (hunters) | present (Attack + Heavy_Attack) | | | | K | K | K | K |
| Bite (hunters) | missing (Attack reused) | | | | N | N | N | N |
| Recover (hunters) | missing | | | | N | N | N | N |
| Flee (grazers) | missing (Run reused) | N | N | N | | | | |
| Defend (grazers) | missing | N rear-stamp | N horns / paw | N tail club | | | | |
| Charge (grazers) | poor: same posture as Run | R | R | R | | | | |

Rest x4 = Rest_Down, Rest_Loop (sleep), Rest_Shift (fidget while lying), Rest_Up. Clips per species: 16 / 15 -> 25.
Pteranodon and Mosasaurus are ambient (AmbientCreature: fly / glide / swim): out of the directive 43 set, unchanged.

Eyes (directive 42):

| species | before | now |
|---|---|---|
| TRI, PARA, ANK, VEL, CAR, SPI, Pteranodon, Mosasaurus | modelled eye tiny and sunk; Unity eyeball (PrimalCreatureEyes): iris fibres yes, pupil yes (round / slit), specular only from smoothness 0.86 (no highlight in the review shots), sits on the skin like a bead (no lids), raptor eye small | per-species size (raptor 1.5x, ankylosaurus / carnotaurus 1.35x, pteranodon 1.4x ...), iris colour baked per species with a **catchlight**, smoothness 0.92 + faint emission, **skin eyelids** (thin shell with an almond opening, heavier upper lid on hunters, average skin colour around the eye): the eye sits in a socket. DinoLife blink / glance unchanged (the eyeball squashes behind the lids) |
| APX | hero eyes: modelled eyeball, iris / slit pupil / specular textures, lids blinking in the clips | unchanged (checked in `D_apex_Idle_00_face.png`); lids now also close in Rest_Loop, half in Rest_Down / Rest_Up, open in Rest_Shift |

## 2. Result per deliverable

| # | Item | Status | How / where |
|---|---|---|---|
| 1 | Audit species x clip + eyes | DONE-VERIFIED | FBX takes + anim json + controller code + the Unity test reports read; matrix above |
| 2 | Missing / poor clips, per-species weight | DONE-VERIFIED in Blender (renders looked at, numeric checks), NOT tested in Unity | `scripts/pf_dino_anim_pc.py` (new module; pf_dino_anim.py untouched). Herbivores first. Eat / Drink / lying use a search that puts the mouth on the ground / water / carcass ahead of the feet with the snout down-forward (log: mouth within 1-3 cm of target for all 7). Weight 0..1 per species drives stop lurch + damped settle, step timing, sway, tail lag, impact dip, head stabilisation, lie-down time and settle bounce |
| 3 | Readable eyes | DONE-NOT-TESTED in Unity (code compiles; the same geometry / numbers previewed in Blender and looked at) | `Editor/PrimalCreatureEyes.cs`; preview `scripts/pf_dino_eyes_preview.py`, renders `Screenshots/PCPhase/Dino/eyes/` (face + 10 m + 20 m per species) |
| 4a | Export (backups first) | DONE-VERIFIED (files on disk, 25 takes each) | backups of FBX + .meta + json + controller: `E:\Model game khủng long\characters\dino_pc\D_20260930\fbx_backup\<Species>\`; work copy `D_20260930\DINO_Work_D.blend` (+ `DINO_Work_backup_20260930.blend`); new FBX + `DINO_<Species>_anim.json` written to `Assets/Art/Characters/Dinosaurs/<Species>/` with the pipeline's exporter settings (`pf_char_export.export`, face_unity_forward, no mesh modifiers). Sizes: 8.9-11.0 MB, Rift Tyrant 20.3 MB (was 15.2) |
| 4b | Import / controller code | DONE-NOT-TESTED (cloud compile runtime / editor / tests rc 0) | `PrimalCharacterBuilder.Dino.cs` (controller: new params, rest chain, turn, stop, bite, recover, defend, Run/Chase-Flee blend, standing-only starts; every old parameter and state name kept), `PrimalDinoBuilder.RebuildClips` (bridge: BuildAndTest x7 + eyes "force", keeps the open scene, refuses a dirty scene). Zip `Tools/pf_up_D2.zip` (3 files; `pf_up_D1.zip` beside it is an older copy, do not use) |
| 4c | Import commands for tomorrow | DONE | `NEXT_SESSION.md`, DINO section |
| 5 | DINO_CLIPS.md | DONE | states, params / triggers, ActionType ids, lengths, loops, events, speeds, turn rates per species |
| 6 | Review renders | DONE-VERIFIED (looked at; fixed: mouth under the chest, head curled backwards (para), crest into the ground, body sunk while half lying, tail curled under the Rift Tyrant, pale lid ring) | `E:\LAST OF THE PRIMAL\Documentation\Screenshots\PCPhase\Dino\` (94 poses `D_<species>_<clip>_<pct>_<view>.png`, sheets `D_sheet_<species>.jpg`, eyes in `eyes/`) |

## 3. Numbers checked in Blender (every clip, every species, `logs/final1.json`)

- Loop gap: 0.0 cm on every loop.
- Foot slide (planted toe vs clip speed): Walk / Run / Chase / Flee / Charge 0.00-0.06 m/s (Unity's test fails above max(0.15, 12 %)).
- Turn clips: planted feet move 0.00-0.04 m/s in the world at the authored turn rate (they counter-rotate as the object turns).
- Reach: Eat / Drink mouth at target (graze 0.05 m, water 0.00 m, carcass 0.22-0.29 m) within 0.01-0.02 m; lying chin 0.07-0.16 m.
- Toes under the ground: new clips at most 1.5-5.4 cm, only during Rest_Down (toe curl while folding). Death (kept) 12-74 cm.

## 4. Files

Blender (`E:\Model game khủng long\scripts\`, copies in `E:\LAST OF THE PRIMAL\Tools\BlenderPipeline\dino\pc_phase\` and the
cloud mirror `src_assets/phase3/Tools/D_art/`): `pf_dino_anim_pc.py` (clips), `pf_dino_pc.py` (driver: build on the existing
rig, checks, renders, export), `pf_dino_eyes_preview.py`, `pf_dino_verify.py`, `launch_background_blender.py`.
Unity code (mirror `src/Scripts/Editor/`): `PrimalCharacterBuilder.Dino.cs`, `PrimalDinoBuilder.cs`, `PrimalCreatureEyes.cs`.
Unity assets on the PC (Unity closed): 7 x `DINO_<Species>.fbx` + `DINO_<Species>_anim.json`. Nothing else in Assets was changed.

## 5. Kept clips really kept

`pf_dino_verify.py` rebuilt the kept clips on the work rig and compared them bone by bone (Head, Jaw, toes, tail, hand,
4 frames per clip) with the FBX that was in Unity: 0.00-0.01 cm for every kept clip of VEL, CAR, SPI, APX and for all
TRI / PARA / ANK clips except the grazers' **Attack** (horn thrust / tail club differs by 23-61 cm) and TRI Run / PARA Walk
(1-2 cm). Cause: `pf_dino_anim.py` was edited at 10:31 UTC on 2026-09-27, after the grazers were exported (07:57) and before
the Rift Tyrant (11:01). The exported grazer Attack is therefore the generator's current (later) version; the older one is in
the FBX backups if the owner prefers it.

## 6. Requests

| To | Request |
|---|---|
| AI | drive the new interface (`DINO_CLIPS.md` section 3): Speed 0 while state tag `Rest`, Intensity for chase / flee, Turn + TurnMul, Stop, AttackType 2, ActionType 11-13; lying rest for midday and night |
| U / Lead | append to `DinoActions` in `Animation/AnimParams.cs`: `IdleVariant = 11, Breathe = 12, Recover = 13` (U's file) |
| Lead | after `RebuildClips` re-run AI's `PrimalWildlifeBuilder.Build` (idempotent): the prefabs are re-saved from the FBX, so the placed herds are re-checked against them (prefab paths and walk / run speeds are unchanged) |

## 7. Known limits

- Unity-side unverified: import, the Animator graph (about 160-180 standing-start transitions per species), the eyelid
  placement on the real prefabs, catchlight look under URP lighting. `RebuildClips` runs the character test per species
  (loops, foot slide, NaN, facing, skin, screenshots) and must report 7 x PASS.
- Death (kept) still pushes the limbs into the ground while rolling; not changed (owner's existing clip); a later pass can
  lift the legs in the generator.
- Rest transitions: tails dip a little into the ground halfway through Rest_Up (ankylosaurus, Rift Tyrant); the settled poses are clean.
- Lids are one colour (average skin texel) with no scale texture; fine at play distance, plain in a close-up.
- Walk / Run are unchanged; per-species weight on straight locomotion comes from AI's runtime layer (DinoLife sway / lean /
  tail) plus the new Stop / Turn / Chase / Flee / Charge.
