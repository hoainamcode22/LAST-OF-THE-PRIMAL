# U (combat + animation, Unity): PC phase report

Scope: `PC_ROUND_OWNERSHIP.md` row U. Zips `pf_up_U20` to `pf_up_U26`, hand made, only U files. No git, no full suites.
Checks per step: cloud compile (`./cc.sh all` clean), PC compile after each deploy (no `error CS`), Console check after
builders, targeted PlayMode tests only for what nothing else can check.

## 1. CHAR clips (waiting)
`E:\Model game khủng long\export\staging\` is still empty (no FBX, no `clips_manifest.json`) at the time of this report.
Everything is ready for them:
- `PrimalCharacterBuilder` already has states for every clip CHAR is making. States without a clip play a placeholder
  (`Collect_Water` / `Drink_Kneel` = Crouch, `Gather_Stone_Hand` / `Gather_Branch` = Gather_Plant, `Bandage_Use` =
  Use_Item). When a real clip with that name is imported it replaces the placeholder on the next `BuildAndTest "Player"`.
- The procedural programs (kneel drink, fill, container drink) switch themselves off when the state plays a real clip
  (the clip then brings its own hands and events).
- Steps when the clips land (for me or whoever continues): back up the Assets FBX (dated copy), copy the staging FBX in,
  put `clips_manifest.json` next to `PLAYER_Survivor_anim.json`, add receivers for any new event names in
  `CharacterAnimationEvents`, `BuildAndTest "Player"`, reopen the island, Console check, probe once (`probe_on` = on, then
  off) against the Phase A numbers. `useStartStopClips` stays off unless the probe shows less foot slide with them.

## 2. Player motion polish (directive 19-20)
- **Turn / pivot foot slide:** foot lock in `PlayerIK`. During slow turns (planar speed under 2.2 m/s, turn rate over
  20 deg/s, not in an action) a planted foot keeps its world spot; it lets go when the animated foot drifts more than
  0.18 m or lifts. Blends in and out, no pops.
- **Chest vs hips in turns, look IK easing:** kept from Phase A (look body weight eased to 0.03 while moving or turning,
  look point SmoothDamp 0.15 s, weight step at most 0.08 per frame). Phase A measured 13.5-13.7 deg chest vs hips in a
  reversal, of which the Run clip alone is 11.7; the rest waits for CHAR's run clip. Re-measure with the probe (section 8).
- **Idle weight shift variety:** new `Idle_Variation_Mirror` state (same clip, mirrored) on the `IdleMirror` bool; the
  driver picks a side at random for each idle variation and never the same side three times in a row. Result: the
  weight shift goes to either leg.
- **Idle breathing:** `PlayerBodyFx` pitches the chest 0.9 deg at 15 breaths / min while standing still, the head
  compensates so the gaze stays level.
- **Head stabilisation:** `PlayerBodyFx` low-passes the head's rotation relative to the body while walking / running
  and removes half of the quick wobble (step bob, swing carried up the spine). Turns and look IK pass through. Off in
  actions, climbing and when still.
- Probe (end of phase): not run yet, see section 8.

## 3. Physical drinking / eating (directive 8, 10)
- **At water (river, pond, pool, sea shore):** `Drink` becomes the new action `DrinkKneel` (id 19, state `Drink_Kneel`).
  The body turns to face the water, takes a short step to the edge when the water is out of reach (at most 0.45 m,
  only onto ground at foot height), sinks into a kneel (0.22 m, feet stay planted), bends the upper body toward the
  water (look IK body weight), both hands go down to the reachable point of the surface, scoop (small splash +
  `WaterScoop` sound at the water), come up to the mouth, drink (`OnDrink("kneel")` at 1.35 s), drip, then release.
  Thirst and SURV's "+N Hydration" HUD text follow the existing drink event.
- **Filling a container:** same kneel, the container hand dips in, fills at 1.15 s (`OnDrink("fill")`), lifts it to the
  chest.
- **From a container:** the Drink clip lifts the hand; IK takes it the last bit to the lips around the swallow.
- **Eating:** `PlayerBodyFx` adds chewing after each bite (two quiet `Chew` sounds, small head nod at 2.3 Hz for 2 s);
  crumbs come from RES's existing `OnEat` handler (`FoodCrumbs`). Jaw opening stays with `PlayerFacial`.
- **Water droplets:** `DrinkDrips` from the chin 0.25 s after a drink.
- Test `PhysicalActionTests.Kneel_Drink_At_Water_Hands_To_Surface_Then_Mouth` (U_62 on U24 code, PASS): state
  `Drink_Kneel`, facing the water 0 deg, hand IK weight 1.0, hands on the IK goal (0.000 m), hands down 0.39 m from
  standing (still 0.53 m above the surface: the Crouch placeholder is too high, so U25 added the kneel sink and the edge
  step), to the mouth 0.026 m, one OnDrink at 1.37 s, thirst 40 -> 67.9. The U25 version (sink + step, asserts the edge
  under 0.75 m) is not run yet (section 8).
- With the placeholder the edge step is a short slide in the crouch; a real `Drink_Kneel` clip can include the step.
- Note: the Animator uses `CullUpdateTransforms`, so IK does not run while the body is off camera. In play the player is
  on camera; the test sets `AlwaysAnimate` because its camera does not frame the body.

## 4. Climbing (directive 21)
`Climbable` now has three kinds: `Tree` (as before), `Ledge` (pull up and over in one move) and `RockFace` (climb up /
down with W / S, pull over at the top or step off at the bottom). Hands go to holds on the lip / face, feet stay on the
face plane, then free during the pull-over. No teleport: the path is interpolated every frame.
Test `ClimbTests` (U_59, 3 PASS): ledge 1.5 m, on top at the right height, largest single-frame move 0.061 m; rock face
3.2 m up and over, largest move 0.055 m; rock face down and step off.
Sounds: `ClimbGrab` at the grab and lip grip (with `RockDust`), `ClimbScrape` on face steps. Prompts: "Climb up the
ledge" / "Pull yourself up", "Climb the rock face".

### Setup for ENV (builders)
One call per climbable spot, on the rock (or any host) object:
```csharp
var c = Climbable.CreateOn(rockGo, ClimbKind.Ledge,      // or ClimbKind.RockFace
    baseOnGround,           // foot of the face, on the terrain, where the climber stands in front
    lip,                    // the top edge the hands grab (same x / z as the face line, y = top)
    exit: null,             // optional standing point on top; default 0.6 m past the lip on the ground found there
    width: 1.2f,            // climbable width of the face
    name: "boulder");       // optional prompt name ("Climb up the boulder")
string problem = c.Validate();   // null = fine; log anything else and skip / move the spot
```
- It makes (or reuses, so re-runs are idempotent) a child `Climb_Ledge` / `Climb_RockFace` whose forward points INTO the
  face, with points `ClimbStart`, `ClimbEnd` and optional `ClimbExit`. Move them in the Scene view; the gizmo shows the
  face and the exit.
- Heights: Ledge 0.8-2.4 m, RockFace 1.8-8 m (base to lip). `Validate` also checks ground on top near the exit, room to
  stand at the exit and room in front of the face (capsules). Call it after the rock collider exists (it syncs physics).
- The rock needs a collider for the top (the player stands on it). The face itself needs no special collider.
- Only designated spots are climbable (not every rock). Suggested: a few ledges on the canyon and ridge paths, one or two
  rock faces as shortcuts, one near the migration viewpoint.

## 5. VFX / audio (directive 55, player side)
New ids (appended): `VfxId` FootWood, FootGrass, BuildDust, BloodDrip, DrinkDrips, RockDust; `SfxId` FootWood,
FootGrass, Chew, WaterScoop, ClimbGrab, ClimbScrape. Prefabs in `PrimalVfxBuilder.BuildPcPhase` (pooled, same muted
earth palette and particle style as the rest); 19 new WAVs from `sfx_synth.py` (section `pcphase`, seed 4242, original
synthesis). Libraries after the builders: audio 56 ids / 134 clips, VFX 38 entries, none missing.
- Footstep dust per surface: sand, dirt, mud, rock existed; wood (bark bits) and grass (blades) added.
- Eating particles: `FoodCrumbs` (existing, RES plays it on `OnEat`); chewing added (section 3).
- Water droplets: `DrinkDrips`, scoop splash at the water.
- Blood: hit spray / heavy spray and bleed existed; `BloodDrip` adds drops falling to the ground while bleeding (every
  1.6 s standing, 0.7 s moving).
- Building dust: `VfxId.BuildDust` (dust + fibre bits + a few leaves) ready for BUILD.
- Wood chips / stone particles checked: sizes, speeds and colours fit the world (bark browns, grey stone, low counts).
  The 4 short sparks on stone chips stay (flint on stone), no change.

## 6. Requests for other agents
- **RES (`Player/PlayerFeedback.Footstep`):** map `Surface.Wood -> VfxId.FootWood / SfxId.FootWood` (now no dust and the
  rock sound) and `Surface.Grass -> VfxId.FootGrass / SfxId.FootGrass` (now dirt).
- **RES (`PlayerFeedback`, `OnDrink`):** when `param` is `"kneel"` or `"fill"` skip the hand `WaterSplash` (the program
  already splashes at the water surface); for `"fill"` also skip `WaterDrops` at the mouth and the `Drink` sound (no
  drinking happens). Plain `OnDrink` (container) unchanged.
- **BUILD:** on a placed piece / build hit call `VfxPool.Instance.Play(VfxId.BuildDust, point, Vector3.up, null, size)`
  (size 0.6-1.5 by piece size) with `SfxId.Build`.
- **ENV:** place ledges / rock faces with `Climbable.CreateOn` + `Validate` (section 4).
- **CHAR:** clip names as in the builder: `Drink_Kneel`, `Collect_Water`, `Gather_Stone_Hand`, `Gather_Branch`,
  `Bandage_Use`, `BareHand_*`, climb clips; events in `clips_manifest.json` (`OnDrink` at the swallow, `OnEat` at the
  bite, `OnGatherHit`, `OnUseItem`, `OnFootstep` L / R, `OnClimbStep`, `OnClimbGrab`).

## 7. Files (all U owned)
`Scripts/World/Climbable.cs`, `Scripts/Player/PlayerClimb.cs`, `PlayerIK.cs`, `PlayerInteraction.cs`,
`PlayerAnimationDriver.cs`, `PlayerState.cs`, new `PlayerBodyFx.cs`, `Scripts/Animation/AnimParams.cs`,
`Scripts/Audio/SfxPlayer.cs` (ids appended), `Scripts/VFX/VfxPool.cs` (ids appended), `Scripts/Editor/PrimalCharacterBuilder.cs`,
`PrimalAudioBuilder.cs`, `PrimalVfxBuilder.cs`, `Tools/Audio/sfx_synth.py`, `Audio/SFX/SFX_{FootWood,FootGrass,Chew,WaterScoop,ClimbGrab,ClimbScrape}_*.wav`,
new tests `Tests/PlayMode/ClimbTests.cs`, `PhysicalActionTests.cs`.

## 8. Open (the PC editor stopped taking bridge commands at about 15:33 UTC)
After `PhysicalActionTests` passed at 15:32:56 the editor took no further command (Refresh 15:34, 15:43, 16:12 all still
queued; ScriptAssemblies unchanged since 15:31; `Library/SourceAssetDB` touched until 15:55; the laptop was also briefly
disconnected). Lead told at 15:40. I released the bridge lock (free since 16:12). State:
- **Deployed and PC compiled:** U20-U24 (Console check 0 errors after U20 + builders).
- **Extracted, not compiled yet:** U25 (`PlayerIK` kneel sink, `PlayerInteraction` edge step + arm reach + spine bend,
  `PhysicalActionTests`) and U26 (`PlayerBodyFx` head stabilisation). Both compile clean in the cloud (`cc.sh all`).
  A Refresh is queued in `command.json`, so the next working editor compiles them.
- **To do once the editor answers:** check the compile (no `error CS`), `PrimalEditorBridge.ConsoleCheck`, rerun only
  `PhysicalActionTests`, then the probe once (`probe_on.txt` on, `PrimalTestRunner.RunPlayMode "CharacterMotionProbe"`,
  back to off) and compare with the Phase A table in `Character/_unity_phaseA_report.md` (hip drop, yaw steps, step
  cycle, turn rates, pelvis dip, chest vs hips, look weight step). `probe_on.txt` is "off" now.
- **CHAR clips:** not staged yet (section 1).
