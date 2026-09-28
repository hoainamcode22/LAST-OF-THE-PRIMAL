# PERCEPTION SYSTEM (design, not implemented)

Owner request: the world reacts to the player. **PLAYER -> noise / smell / movement / light -> dinosaur detection ->
predator or prey behaviour.** This document designs a data-driven perception layer that plugs into the existing
classes (no new manager object), in three build phases: hearing + visibility, then scent + wind, then tree-density cover.
Source read: `src/Scripts` (AI, World, Player, Core, Survival, UI) and `Documentation/Survival/*` on 2026-09-28.

---

## 1. What the player should feel

| Player does | World notices | Who reacts | Counterplay |
|---|---|---|---|
| Fells trees | chop noise, a loud tree fall, the stumps leave an open patch (less cover, clearing bonus) until regrowth (72 h) | predators in earshot walk over to check; herds look up | chop when no predator is near, camp inside dense trees, leave a screen of trees standing |
| Cooks meat | meat scent puffs drifting downwind | predators downwind (meatDrive > 0) walk to the fire and lurk at the edge of the firelight | cook under a shelter, keep the fire fed (fire fear), cook upwind of predator ground, take food off before it burns (smoke) |
| Pushes through bushes | rustle noise + a visible shake of the bush | anything with line of sight to the bush looks there, predators investigate | crouch-walk around bushes, move only when unseen, stand still inside a bush (strong cover) |
| Holds a torch at night | full light on the player, beacon bonus, cover halved | predators see the player 2-3x farther than in the dark | put the torch away near danger, rely on firelight at camp |
| Sprints on rock | loud steps, big motion | heard at the species hearing range | walk or crouch, prefer grass and sand |
| Bleeds, carries raw meat | blood / meat trail | predators follow the trail | stop the bleeding, store meat in a box, wade through water |
| Shoots and misses | the arrow lands with a noise | the dinosaur investigates the landing spot | use missed shots as distractions |

Long-term loop fit (SURVIVE -> EXPLORE -> OBSERVE -> HUNT -> GATHER -> CRAFT -> BUILD -> TAME -> PROTECT -> EXPAND):

| Loop step | Perception makes it matter by |
|---|---|
| Survive | noise and scent pull predators; night + torch is a trade-off, not a rule |
| Explore | gait, surface and weather choose how far you are heard; rain is a stealth window |
| Observe | watching a species undetected unlocks journal notes about its senses (phase 4) |
| Hunt | stalk from downwind, crouch in cover, herds share alarms, a wounded animal's blood draws rivals |
| Gather | chopping and mining are loud; felling opens the ground around camp |
| Craft | later gear hooks: quiet footwear, scent mask (data fields reserved, section 13) |
| Build | a shelter over the fire halves scent and dims light; dense trees hide a camp |
| Tame | trust meter reuses the awareness meter in reverse (phase 4) |
| Protect | fire fear keeps most predators at the light edge while the fire burns |
| Expand | a bigger camp (more fires, more felling) has a bigger signature and draws more visits |

---

## 2. What exists today

| Area | Where | Today | Gap |
|---|---|---|---|
| Sight | `DinosaurController.CanSee` (L102) | range = `sightRange x alertSensitivity x 0.6 crouch x 0.6 night`, FOV, one linecast | binary, instant, ignores torch, fires, cover, movement |
| Hearing | `DinosaurController.CanHear` (L112) | radius = `hearingRange x gait` (sprint 1, walk 0.45, crouch 0.15, still 0.1) | only the player's gait; no bush, chop, fall, combat, surface or rain |
| Brain | `Think` (L147), cadence 0.2 s near / 0.6 s mid / frozen far (L127-133), staggered start | FSM `DinoState` with Observe / Alert / Investigate / Flee / Chase | no awareness build-up, no memory; `Chase` steers to the live player position (omniscient), `Observe` faces the live player |
| Herd | `Flee` (L250) | same species within 30 m flee too | no alert sharing below flee |
| Warning | `PredatorWarning` event (L376), `Telegraph` attack wind-up, `Roar` on Alert | good readable tells | none needed |
| Head look | `DinoLife` + `LookTarget` (L41) | looks at the player whenever in an alert state | should look at the stimulus, not the hidden player |
| Ambient | `AmbientCreature.Startle` | only bushes call it | loud events should startle flyers |
| Bushes | `BushInteraction` | shake, rustle, discovery, startle; `PlayerInside` per bush | no stimulus out, no cover in |
| Fire | `Campfire` (`All`, `IsLit`, cooking slots, `Tick` L234) | light + heat + cooking | no scent, no light term for visibility |
| Trees | `TreeHarvest` (8 m grid of TreeInstances, `_felled`) | chop, fell, regrow | no noise, no cover query |
| Torch | `PlayerEquipment.TorchLit` | light + warmth | no detection cost |
| Weather | `WeatherManager` (`Intensity`, `windDirection` fixed (0.8, 0.6), strength local) | rain, wind for shaders | wind not exposed, direction never changes |
| Time | `TimeManager.Daylight01`, `NightFactor`, `IsNight` | light curve | fine |
| Events | `GameEvents.Raise` | allocates per call (`GetInvocationList`) | never route footsteps or rustles through it |
| Tips | `OnboardingTips` + `ContextHints.QueueTip` (bilingual, saved) | 6 tips | add perception tips |

---

## 3. Architecture (no duplicate manager)

| Piece | Kind | File | Job |
|---|---|---|---|
| `Stimuli` | static hub (like `GameEvents`) | `Core/Stimuli.cs` (new) | two fixed ring buffers of structs; emit / read API; counters; reset on domain reload |
| `PerceptionConfig` | ScriptableObject (like `SurvivalConfig`, default instance when missing) | `AI/PerceptionConfig.cs` (new), `Resources/PerceptionConfig.asset` | every global number in section 11 |
| Species senses | appended fields | `AI/DinosaurDefinition.cs` | per-species gains, night vision, memory, herd, fire fear, meat drive |
| `DinoSenses` | plain C# class owned by each `DinosaurController` | `AI/DinoSenses.cs` (new) | awareness, interest, memory, state, herd share; ticked from the existing `Think` cadence |
| `PlayerSignature` | component next to `PlayerState` (auto-added by `PlayerAnimationDriver` L46, same pattern) | `Player/PlayerSignature.cs` (new) | 10 Hz: light, posture, motion, cover, exposure; movement noise pulses; player scent puffs; values for the HUD |
| World hooks | one-line calls | existing classes (section 4) | emit stimuli where the sound / smell already happens |
| Builder | editor, idempotent | `Editor/PrimalPerceptionBuilder.cs` (new) | writes only perception fields on `DINO_*.asset`, `ItemDefinition.scent`, creates the config asset. `PrimalGameplayBuilder` is never re-run |

```
 Player + world actions                       Stimuli (static)                     DinosaurController.Think (5 Hz near)
 PlayerSignature step pulses ---------+
 BushInteraction, PlayerFeedback,     +-->  transient ring [128]  ----------->  DinoSenses.Tick
 TreeHarvest, Projectile, Melee ------+     noise + motion, life 1.5 s            sight (budgeted linecast)
                                                                                  hearing / motion (new seq only)
 Campfire, Carcass, PlayerSignature ------> scent ring [256]      ----------->    scent (lazy puff maths)
                                            puffs, life 120 s                     -> awareness / interest / memory
 PlayerSignature.Current (visibility factors, eye point) --------------------->   -> awareness state -> existing FSM
                                                                                  -> herd share (DinosaurController.All)
 HUDManager / ContextHints <--- PlayerSignature + max awareness over All (10 Hz)
```

Rules: sensing runs inside the existing `Think` tick (no second update loop); persistent sources already have an
`Update` (Campfire `Tick`, Carcass `Update`) and emit from there; `GameEvents` only for rare facts (two appended types,
section 9).

---

## 4. Stimulus model

### 4.1 Data

```csharp
public enum StimulusKind : byte { Noise, Motion, Scent }
public enum StimulusSource : byte { Player, Creature, World, Distraction }
public enum NoiseTag : byte { Footstep, Rustle, Chop, Mine, Gather, TreeFall, Build, Combat, Impact, Landing, Voice, Fire }
public enum ScentKind : byte { Meat, Blood, Smoke, Player }

public struct Stimulus            // about 28 bytes, stored by value
{
    public Vector3 pos;           // noise / motion: where; scent: emission origin
    public float time;            // Time.time at emission
    public float strength;        // noise: loudness (x hearingRange); motion: 0..1.5; scent: puff strength
    public int seq;               // monotonic, identifies the entry
    public StimulusKind kind; public StimulusSource source; public byte tag;   // NoiseTag or ScentKind
}

public static class Stimuli
{
    public static void Noise(Vector3 p, float loudness, NoiseTag tag, StimulusSource src);
    public static void Motion(Vector3 p, float strength, StimulusSource src);
    public static void Scent(Vector3 origin, float strength, ScentKind kind, StimulusSource src);
    public static int TransientHead { get; }                    // newest seq
    public static bool TryGetTransient(int seq, out Stimulus s); // false when overwritten or expired
    public static int ScentCapacity { get; } public static ref readonly Stimulus ScentAt(int i);
    public static void Clear();                                  // new game, load, after sleep
    // pure maths, used by DinoSenses and tests
    public static bool PuffAt(in Stimulus s, float now, Vector2 windDir, float windStrength, float rain,
                              out Vector3 center, out float radius, out float strength);
}
```

- Two rings with fixed arrays allocated once: **transient** (noise + motion, 128 slots, life `transientLife` 1.5 s)
  and **scent** (256 slots, life `scentMaxAge` 120 s). Separate so a burst of footsteps never evicts smells.
- Writing overwrites the oldest slot, O(1), no allocation. Readers keep `_lastSeq` and read only newer entries;
  if more than the capacity was written since, they jump to `head - capacity`.
- `Noise()` with loudness >= `ambientStartleLoudness` (1.0) also calls `Startle` on `AmbientCreature.All` within
  `ambientStartleRange` (40 m): event driven, no scanning per frame.
- Creature-sourced stimuli are ignored by other dinosaurs except herbivore alarm (section 6.5).

### 4.2 Scent puffs (lazy, no per-frame update)

A puff is only its emission record. Its state at time `now` (age `a = now - time`) is computed on read:

| Quantity | Formula | Start value |
|---|---|---|
| centre | `origin + windDir x (scentDrift x windStrength) x a` (downwind = the direction the wind blows toward, same as `_PF_Wind`) | drift 1.4 m/s per unit strength: calm 0.35 -> 0.5 m/s, storm 1.4 -> 2 m/s |
| radius | `puffRadius0 + puffSpread x a` | 2 m + 0.2 m/s (17 m at 75 s) |
| strength | `s0 x exp(-a / scentTau) x (1 - rainScentCut x rainIntensity)` | tau 45 s, rain cut 0.7 |
| concentration at x | `strength x max(0, 1 - |x - centre|^2 / radius^2)` | smelled when `x smellSensitivity >= smellThreshold` (0.12) |

A dinosaur takes the **max** over puffs (not the sum) and remembers that puff's origin and kind. Upwind of a source
the puffs drift away and are never smelled: wind direction becomes counterplay by construction.

### 4.3 Emitters (hook table)

Noise loudness is a multiple of the listener's `hearingRange` (today `0.6 x sightRange`, 18-33 m). Loudness 1.0 = a
sprint footstep = today's `CanHear` sprint radius, so existing data keeps its meaning.

| Source | Hook (existing file, line) | Kind / tag | Value | Phase |
|---|---|---|---|---|
| Movement pulse every 0.25 s while moving | `PlayerSignature` | Noise / Footstep | gait x surface (5.2); weight 0.25 per pulse | 1 |
| Jump landing | `PlayerFeedback.OnLanded` (L153) | Noise / Landing | 0.4, hard (> 8 m/s) 0.8 | 1 |
| Brushing inside a bush while moving | `BushInteraction.TrackPlayer` (L280) | Noise / Rustle | 0.25 (crouched 0.12) | 1 |
| Bush rustle feedback | `BushInteraction.Feedback` (L303) | Noise / Rustle + Motion | light 0.4, strong 0.7, flush or bird burst 1.0; motion = same value | 1 |
| Chop hit | `PlayerFeedback.Gather` "Gather_Wood" (L102) | Noise / Chop | 1.2 | 1 |
| Stone / mining hit | `PlayerFeedback.Gather` "Gather_Stone" | Noise / Mine | 1.4 | 1 |
| Hand gathering (plants, berries) | `PlayerFeedback.Gather` default | Noise / Gather | 0.2 | 1 |
| Tree falls | `TreeHarvest.Fell` when `fx` (L137) | Noise / TreeFall | 2.5 (startles flyers) | 1 |
| Building hit | `PlayerFeedback` "OnBuildHit" (L81) | Noise / Build | 0.9 | 1 |
| Melee hit | `MeleeWeapon.OnHit` (L278), `PlayerCombat` (L315) | Noise / Combat | 1.0 | 1 |
| Bow release | `PlayerFeedback` "OnBowRelease" | Noise / Combat | 0.2 | 1 |
| Missed arrow / spear lands | `Projectile.Land` (L84) when no creature was hit | Noise / Impact, source Distraction | rock 0.5, wood 0.45, water 0.4, ground 0.3 | 1 |
| Dodge roll | `PlayerCombat` dodge (L152) | Noise / Footstep | 0.35 | 1 |
| Player hurt grunt | `PlayerFeedback` "OnHurt" (L83) | Noise / Voice | 0.6, heavy 0.8 | 1 |
| Butchering cut | `Carcass.Cut` (L116) | Noise / Gather | 0.3 | 1 |
| Campfire lit | `Campfire.SetLit(true)` (L309) | Noise / Fire | 0.3 | 1 |
| Creature roar / hurt | `DinosaurController.Roar` (L259), `TakeHit` | Noise / Voice, source Creature | 2.0 (flyers, alarm only) | 1 |
| Campfire crackle | `Campfire.Tick` every 2 s | Noise / Fire | 0.15 (grazers keep away) | 2 |
| Meat cooking | `Campfire.Tick` every 3 s | Scent / Meat | `item.scent` of the hottest meat slot + 0.25 per extra meat slot, cap 1.5 | 2 |
| Cooked meat left on the fire | `Campfire.Tick` | Scent / Meat | `cooked.scent` (0.6) | 2 |
| Burnt food | `Campfire.Tick` | Scent / Smoke | 0.8 | 2 |
| Wood smoke of any lit fire | `Campfire.Tick` every 6 s | Scent / Smoke | 0.3 | 2 |
| Fire under a shelter | `Campfire.Tick` (`Shelter.Covers(pos)`) | all fire scents | x 0.5 (light x 0.6 in 5.1) | 2 |
| Raw meat carried | `PlayerSignature` (count cached on `InventorySystem.Changed`) | Scent / Meat | `scent x carriedScentMul (0.1)` per unit, cap 0.5, every 3 s | 2 |
| Player bleeding | `PlayerSignature` (`PlayerHealth.IsBleeding`) | Scent / Blood | 0.8 every 2 s (a trail) | 2 |
| Player body | `PlayerSignature` | Scent / Player | 0.12 every 3 s, x 0.5 when wet | 2 |
| Carcass | `Carcass.Update` every 4 s | Scent / Meat | 1.2 falling to 0.5 at `expireHours` | 2 |
| Butchering cut | `Carcass.Cut` | Scent / Blood | 1.5 pulse | 2 |
| Wounded dinosaur | `DinosaurController` blood drip (L142) | Scent / Blood | 0.6 per drip | 2 |

Data field (appended, builder-set): `ItemDefinition.scent` (0 = none; raw_meat 1.0, cooked_meat 0.6). Any future food
gets a smell by data, never by id.

---

## 5. Player signature (`PlayerSignature`, 10 Hz, cached)

### 5.1 Visibility

`V = Light x Posture x Motion x (1 - Cover) x Exposure`, clamped to 0..1.5. The component stores the factors
separately (`Ambient`, `Local` = torch / fire, `Beacon`, `CaveMul`, posture, motion, cover, exposure) because the
ambient part is re-weighted per species: `Light' = max(lerp(Ambient, 1, nightVision), Local) x Beacon x CaveMul` (6.2).
Night vision compensates darkness, never the torch.

| Factor | Rule | Values |
|---|---|---|
| Ambient | `lerp(nightAmbient, 1, Daylight01) x (1 - overcastPenalty x Overcast) x (1 - rainVisPenalty x Intensity)` | night 0.25, overcast 0.25, rain 0.2 |
| Local (torch) | `TorchLit`: Local = 1.0; Beacon = `torchNightBeacon` at night (`NightFactor > 0.5`), else 1 | beacon 1.4 |
| Local (fire) | nearest lit `Campfire` within `fireLightRadius`: Local = max(Local, `fireLight x (1 - d / r)`); shelter over the fire x 0.6 | r 10 m, 0.9 |
| CaveMul | `ZoneManager.IsIndoor` and no torch: `caveLight`, else 1 | 0.4 |
| Posture | standing / crouched (`IsCrouching`) / climbing | 1.0 / 0.55 / 1.2 |
| Motion | `MeasuredPlanarSpeed`: still < 0.2, crouch-walk, walk, run, sprint; a gather / build action counts as run | 0.5 / 0.7 / 0.8 / 1.0 / 1.2 |
| Cover | max of: inside a bush (static `BushInteraction.PlayerBush`, set on player enter / exit); tree density (phase 3); under a shelter roof | bush 0.6, crouched in bush 0.8, moving in bush x 0.5; shelter 0.3; trees 0.08 per standing tree within 6 m, cap 0.35 |
| Cover with torch | cover x `torchCoverMul` | 0.5 |
| Exposure | clearing: >= 3 felled trees within 12 m and no bush cover (phase 3) | 1.15, else 1.0 |

Aim point for line of sight: `position + up x (controller height x 0.75)`, so crouching also hides behind low rocks.

### 5.2 Movement noise

`loudness = gait x surface x (IsGrounded ? 1 : 0)`, emitted as a pulse every 0.25 s while `MeasuredPlanarSpeed > 0.2`.

| Gait | Crouch | Walk | Run | Sprint |
|---|---|---|---|---|
| Loudness | 0.15 | 0.45 | 0.6 | 1.0 |

| Surface (`PlayerFeedback.LastFootSurface`, new read-only property set in `Footstep`) | Grass | Sand | Dirt | Mud | Rock | Wood | Water |
|---|---|---|---|---|---|---|---|
| Multiplier | 0.8 | 0.75 | 1.0 | 1.1 | 1.2 | 1.3 | 1.5 |

Reusing the last footstep surface avoids a second `SurfaceDetector` raycast (its terrain path allocates a
`GetAlphamaps` array per call; not added to).

### 5.3 Test hook

`PlayerSignature.TestSnapshot` (nullable struct, like `BushInteraction.ReactionRoll`) replaces the live values so
`DinoSenses` can be tested without a player.

---

## 6. Detection per species

### 6.1 `DinosaurDefinition` fields

Existing fields keep their meaning: `sightRange`, `fov`, `hearingRange` (where), `alertSensitivity` (skittish
multiplier on both ranges). Appended under `// ---- appended (perception)` (how fast, how long, how it reacts):

| Field | Type | Meaning |
|---|---|---|
| `nightVision` | 0..1 | 0 = sight follows the light fully, 1 = sees as well at night |
| `sightGain` | >= 0 | awareness rate from sight |
| `hearingGain` | >= 0 | awareness bump from noise |
| `smellSensitivity` | >= 0 | multiplies scent concentration |
| `awarenessDecay` | per s | fall rate after `decayHold` without stimulus |
| `memorySeconds` | s | how long the last known position stays useful |
| `herdShareRadius` | m | alert share to the same species (0 = solitary) |
| `alarmCall` | bool | herbivore call that also warns other herbivores |
| `fireFear` | 0..1 | keeps out of `fireFearRadius x fireFear` around a lit fire |
| `meatDrive` | 0..1 | interest in meat / blood scent (0 = ignores it) |
| `scentTrackRange` | m | while Engaged, follows the player by smell within this range after losing sight (0 = never) |

### 6.2 Senses per tick

| Sense | Test | Awareness effect |
|---|---|---|
| Sight | `R = sightRange x alertSensitivity x V'` where `V'` uses `Light'` from 5.1, capped at `sightRange x 1.4`; dist <= R, inside `fov`, then one linecast (budget 6.6) to the aim point | gain per s = `sightRate x sightGain x lerp(edgeMul, closeMul, 1 - d / R)`; inside `bodyRadius x 4` with V > 0.3: jump to Engaged (keeps today's touch rule) |
| Hearing | each new transient Noise (not Creature): `r = hearingRange x alertSensitivity x loudness x (1 - rainMask x rainIntensity)`; heard if d < r | bump = `hearingGain x lerp(noiseBumpMin, noiseBumpMax, 1 - d / r) x weight` (weight 0.25 for movement pulses, 1 otherwise); ceiling Investigating, or Alerted when d < 0.35 r |
| Motion | each new Motion stimulus within `sightRange x Light' x motionRangeMul x strength`, in FOV, linecast to it | bump `lerp(0.1, 0.4, 1 - d / r)`; ceiling Investigating; look point = the bush |
| Scent | every 2nd tick: max concentration over puffs | Player / Blood-of-player: awareness gain `smellRate x c`, ceiling Suspicious (herbivore) or Investigating (predator); Meat / Blood: predator `interest += interestRate x c x meatDrive x dt`; Smoke: herbivores move away |

Noise and scent give a **position, not an identity**: without sight, a dinosaur can reach Investigating (Alerted for a
loud noise close by) but never Engaged. Engaged needs sight, touch range, damage (`_provoked`), a herd share at
Engaged, or scent tracking inside `scentTrackRange` while already Engaged.

### 6.3 Awareness meter and states

One float `awareness` 0..1 (threat, i.e. the player) and one `interest` 0..1 (food scent). After `decayHold` (3 s)
without a stimulus, awareness falls at `awarenessDecay` per s; interest at 0.05 per s. A state drops only when the
meter is `hysteresis` (0.1) below its threshold.

| State | Threshold | Readable tell |
|---|---|---|
| Unaware | < 0.25 | normal idle / wander / eat / rest |
| Suspicious | 0.25 | stops, head turns to the look point, low call (40 %, 8 s cooldown) |
| Investigating | 0.5 | walks to the last known position, head low |
| Alerted | 0.8 | roar (predator) or alarm call (herbivore), faces the threat, herd share |
| Engaged | 1.0 | hunting (Chase / Attack) or fleeing |

`DinoState` and the Animator are unchanged: the awareness state only decides which existing state `Think` enters.

### 6.4 Memory

`lastKnownPos`, `lastKnownTime`, `lastSense` (Sight / Noise / Motion / Scent / Herd), `lookPoint`.
- Sight writes the exact player position; noise writes the event position + an error up to `noiseLocError x d / r`
  (6 m); scent writes the puff origin + `0.1 m per s of puff age` (cap 8 m).
- **Chase fix**: steer to the live player position only while seen within `trackGrace` (1.5 s) or scent-tracked;
  otherwise go to `lastKnownPos`, then **search** (up to 3 points within `searchRadius` 8 m, `searchSeconds` 10),
  then Return when awareness < Suspicious, or at the existing leash (`territoryRadius x 2.5`).
- `LookTarget` returns the player only while seen; new `LookPoint` (Vector3?) feeds `DinoLife` otherwise.
- Memory expires after `memorySeconds`; all of it is transient.

### 6.5 Herd sharing

| Trigger | Receivers | Effect |
|---|---|---|
| Crossing Alerted upward (2 s cooldown) | same species within `herdShareRadius` | awareness = max(own, `herdShareLevel` 0.6), copy `lastKnownPos` |
| Engaged flee | same species within 30 m (existing `Flee` loop) | Flee (unchanged) |
| Herbivore with `alarmCall` reaches Alerted | other herbivores within half its radius | awareness = max(own, `alarmShareLevel` 0.4) |
| Raptor pack Engaged | same species within radius | Engaged with the target position (pack hunt) |
| `PredatorWarning` event | herbivores within 160 m (existing `OnGameEvent`) | Flee (unchanged, keeps its test) |

A stampede is also a Creature noise: predators do not react to it in phase 1 (possible prey hunting later).

---

## 7. Behaviour responses

### 7.1 By temperament

| Awareness | Passive (Parasaurolophus) | Defensive (Triceratops, Ankylosaurus) | Predator / Territorial |
|---|---|---|---|
| Unaware | graze, wander | graze, wander | patrol, rest |
| Suspicious | Observe toward look point | Observe | Observe (sniff) |
| Investigating | Observe + back away at walk speed | Observe, face the point | Investigate: walk to `lastKnownPos` at 1.3x walk, then search |
| Alerted | alarm call, herd share, trot away | Alert pose + roar, face; charge only inside `personalSpace` | Alert: roar, 1.8 s, then Chase if seen (today's rule) |
| Engaged | Flee (zig-zag, existing) | Chase inside personal space, Flee when hurt (existing) | Chase / Attack with memory; Territorial still needs `inTerritory` |

Distance rules stay but are gated by awareness: an Unaware herbivore does not flee from a crouched, unseen player at
12 m; touch range and damage still trigger at once.

### 7.2 Scent investigation (predators, phase 2)

1. `interest >= interestThreshold` (0.5) and awareness < Alerted: Investigate toward the puff origin.
2. Fire fear: a lit `Campfire` blocks a circle of `fireFearRadius (8 m) x fireFear x (night ? 1.3 : 1)`. The
   predator stops at the edge and **lurks** (Observe, circling slowly) up to `lurkSeconds` (20 s). If the fire goes out
   it walks in. Damage overrides fear for 5 s.
3. At the source: sniff 5 s (Observe). If it sees the player the sight rules take over with an awareness floor of 0.5
   (arriving primed). Else the source cell goes on a 60 s cooldown (`interestCooldown`) so it does not ping-pong.
4. Later (phase 4, owner decision): a predator reaching unattended ready food takes it (`Campfire.TryTake` path).

### 7.3 Counterplay summary

| Tool | Effect |
|---|---|
| Crouch | Posture 0.55, footsteps 0.15, bush cover 0.8 |
| Stay still | Motion 0.5, no pulses |
| Douse the torch | loses light 1.0, the 1.4 night beacon and the cover halving |
| Cook under a shelter | fire scents x 0.5, firelight on the player x 0.6 |
| Keep the fire fed | fire fear holds most predators at the light edge |
| Wind | puffs travel downwind only; approach prey from downwind, cook downwind of predator ground |
| Rain | hearing x (1 - 0.4 I), scent x (1 - 0.7 I), light x (1 - 0.2 I) |
| Surfaces | grass and sand are quieter than rock, wood and water |
| Distraction | missed arrows are noises that pull an investigation away |
| Store meat, stop bleeding | removes the meat / blood trail |

---

## 8. Performance

| Band (distance to player, existing LOD) | Tick | Senses |
|---|---|---|
| Near < 90 m | 0.2 s (5 Hz), random start offset (exists) | sight (budgeted), hearing, motion; scent every 2nd tick |
| Mid 90-200 m | 0.6 s | scent every tick, noise only with loudness >= 2 (tree fall); no linecasts |
| Far > 200 m | frozen (exists) | none; on wake, awareness decays by the elapsed time |

- **Linecast budget**: at most `maxLosPerFrame` (6) for all creatures, 1 per creature per tick; a static frame
  counter inside `DinoSenses` (no manager). Order: range -> FOV -> budget -> linecast (today's order).
- **Allocation free**: fixed struct arrays, no LINQ, no closures, no `GameEvents` for pulses, cached components,
  `GameSettings` values cached at 1 Hz (they read `PlayerPrefs`).
- **Spatial queries**: rings are scanned linearly (128 / 256 entries, squared distances); creatures via
  `DinosaurController.All`; trees via the existing `TreeHarvest` 8 m grid (3x3 cells), fires via `Campfire.All`,
  shelters via `Shelter.All`.
- Estimate: 12 land creatures x 5 Hz = about 2 ticks per frame at 30 fps, each under 20 us; `PlayerSignature` under
  20 us at 10 Hz. **Budget: perception under 0.25 ms per frame on the Low preset, 0 B GC in steady state.**
- Instrumentation: `ProfilerMarker "PF.Perception"`, static counters `Stimuli.EmittedThisFrame`,
  `DinoSenses.TicksThisFrame`, `DinoSenses.LosThisFrame` (read by `PerformanceTests`).

---

## 9. Save / load

| Data | Saved? | Why |
|---|---|---|
| Rings, awareness, interest, memory | no | transient; `Stimuli.Clear()` and senses reset in `GameManager.ResetWorld` (L264), after `LoadGame` (L234) and after `Sleep` (L359, `SkipHours` does not advance `Time.time`) |
| Campfire lit, fuel, cooking slots | already saved | scent resumes by itself after a load |
| Felled trees | already saved | cover recomputes from `TreeHarvest` |
| Seen tips | already saved (`SaveData.tipsSeen`) | new tip ids need nothing |
| HUD indicator setting | `GameSettings` (PlayerPrefs `pf_stealthhud`) | not in the save file |

No `SaveData` change, no version bump. Appended `GameEventType` values (append only, stored as ints):
`PlayerNoticed` (id = species, amount = state 1..4, raised once per creature per 10 s when it rises toward the player)
and `ScentInvestigated` (id = species, position = source). Journal, tips and ambience listen; both are rare.

---

## 10. UI feedback and tips

| Element | Where | Behaviour |
|---|---|---|
| Presence indicator | `HUDManager`, small eye + sound ring by the vitals; on touch next to CROUCH (`MobileHUD`) | eye opacity = V for a reference observer in 4 steps (Hidden < 0.25, Low, Visible, Exposed > 0.9); ring pulses with each player noise, size = loudness. Auto mode shows it while crouched or with a predator within 60 m |
| Threat markers | `HUDManager`, up to 3 pooled edge arcs | direction of creatures at Suspicious or above toward the player; white -> amber (Investigating) -> red (Engaged); sampled at 10 Hz from `DinosaurController.All` |
| Wind | tick on the existing compass strip | shows the downwind direction; always subtle |
| Setting | `SettingsPanel`, `GameSettings.StealthHud` | Auto / Always / Off |
| Creature tells | existing | Suspicious low call, Alerted roar or alarm call, attack `Telegraph`, head look via `LookPoint` |

New `OnboardingTips` ids (bilingual like the existing ones, queued through `ContextHints.QueueTip`):

| Id | Trigger | Text |
|---|---|---|
| `stealth` | first `PlayerNoticed` | "Something noticed you: [C] crouch and keep still to lose it (Có con vật để ý bạn: ngồi xuống và đứng yên)" |
| `noise` | first chop / mine / tree fall with a predator within 80 m | "Chopping and mining are loud: predators come to check (Chặt cây, đào đá rất ồn: thú săn mồi sẽ tìm đến)" |
| `torch_stealth` | torch lit at night with a predator within 80 m | "A torch lights the way and shows you: put it away to hide (Đuốc soi đường nhưng làm bạn lộ diện: cất đuốc để ẩn nấp)" |
| `bush` | first strong rustle while a creature is Suspicious | "Bushes hide you when still, pushing through shakes them (Bụi cây che bạn khi đứng yên, lao qua sẽ làm lá rung)" |
| `scent` | first `ScentInvestigated` or first meat cooked (phase 2) | "Cooking meat smells and the wind carries it: cook under a shelter, keep the fire burning (Mùi thịt nướng bay theo gió: nấu trong lều, giữ lửa cháy)" |
| `wind` | first time smelled by prey (phase 2) | "They smelled you: approach from downwind (Chúng đánh hơi thấy bạn: hãy tiếp cận từ cuối gió)" |

Debug: `OnDrawGizmosSelected` draws effective sight radius, hearing radius for loudness 1, awareness bar,
`lastKnownPos`; editor-only scene overlay of active stimuli.

---

## 11. Tuning (starting values)

### 11.1 Global (`PerceptionConfig`)

| Group | Key | Value |
|---|---|---|
| States | suspicious / investigate / alerted / engaged | 0.25 / 0.5 / 0.8 / 1.0 |
| | hysteresis, decayHold | 0.1, 3 s |
| Sight | sightRate, edgeMul, closeMul | 1.0 /s, 0.25, 2.0 |
| | range cap (x sightRange), trackGrace | 1.4, 1.5 s |
| | maxLosPerFrame | 6 |
| Light | nightAmbient, overcastPenalty, rainVisPenalty | 0.25, 0.25, 0.2 |
| | torchNightBeacon, torchCoverMul | 1.4, 0.5 |
| | fireLightRadius, fireLight, caveLight | 10 m, 0.9, 0.4 |
| Posture / motion | crouch, climbing | 0.55, 1.2 |
| | still, crouch-walk, walk, run, sprint | 0.5, 0.7, 0.8, 1.0, 1.2 |
| Cover | bush, crouched in bush, moving in bush | 0.6, 0.8, x 0.5 |
| | shelter, per tree (6 m), tree cap | 0.3, 0.08, 0.35 |
| | clearing (>= 3 felled within 12 m) | exposure 1.15 |
| Hearing | noiseBumpMin / Max, pulse weight | 0.15 / 0.6, 0.25 |
| | rainMask, noiseLocError, transientLife | 0.4, 6 m, 1.5 s |
| Motion | motionRangeMul | 0.8 |
| Scent | puff interval (fire, player, carcass) | 3 s, 2-3 s, 4 s |
| | scentTau, scentMaxAge, puffRadius0, puffSpread | 45 s, 120 s, 2 m, 0.2 m/s |
| | scentDrift, rainScentCut, smellThreshold | 1.4 m/s per wind unit, 0.7, 0.12 |
| | smellRate, carriedScentMul | 0.5 /s, 0.1 |
| Interest | interestRate, threshold, decay, cooldown | 1.0, 0.5, 0.05 /s, 60 s |
| Fire fear | fireFearRadius, night mul, lurkSeconds | 8 m, 1.3, 20 s |
| Herd | herdShareLevel, alarmShareLevel | 0.6, 0.4 |
| Search | searchRadius, searchSeconds | 8 m, 10 s |
| Ambient | ambientStartleLoudness, range | 1.0, 40 m |
| Rings | transient, scent capacity | 128, 256 |

### 11.2 Species (appended fields; sight / hearing are today's values)

| Species | Temp. | sight / hear (m) | nightVision | sightGain | hearingGain | smell | decay /s | memory s | herd m | alarm | fireFear | meatDrive | scentTrack m |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Triceratops | Defensive | 40 / 24 | 0.10 | 0.8 | 0.8 | 0.5 | 0.10 | 8 | 30 | yes | 0.8 | 0 | 0 |
| Parasaurolophus | Passive | 55 / 33 | 0.15 | 1.2 | 1.4 | 0.9 | 0.06 | 12 | 40 | yes | 0.9 | 0 | 0 |
| Ankylosaurus | Defensive | 30 / 18 | 0.10 | 0.6 | 0.7 | 0.6 | 0.12 | 6 | 0 | no | 0.5 | 0 | 0 |
| Velociraptor | Predator | 45 / 27 | 0.45 | 1.1 | 1.3 | 1.4 | 0.05 | 20 | 45 | no | 0.7 | 1.0 | 15 |
| Carnotaurus | Territorial | 50 / 30 | 0.30 | 1.0 | 1.0 | 1.0 | 0.07 | 15 | 0 | no | 0.5 | 0.7 | 8 |
| Spinosaurus | Territorial | 40 / 24 | 0.25 | 0.9 | 0.9 | 0.8 | 0.08 | 12 | 0 | no | 0.6 | 0.5 | 6 |
| Rift Tyrant | Territorial | 55 / 33 | 0.30 | 0.9 | 1.1 | 1.3 | 0.04 | 30 | 0 | no | 0.15 | 0.9 | 12 |
| Pteranodon, Mosasaurus | Ambient | - | - | - | - | - | - | - | - | - | - | - | Startle only |

### 11.3 Worked checks (Velociraptor, sight 45 m)

| Situation | V' | Effective sight | Compared with today |
|---|---|---|---|
| Day, open, standing still (the `Predator_Chases` test setup, 14 m) | 0.5 | 22.5 m, Engaged in about 1 s | today 45 m; test still passes (attack well inside 20 s) |
| Day, walking | 0.8 | 36 m | 45 m |
| Day, crouch-walk | 0.39 | 17 m | 27 m |
| Night, walking, no torch | 0.47 | 21 m | 27 m |
| Night, walking, torch | 1.12 | 50 m | 27 m (torch had no cost) |
| Night, crouched still in a bush | 0.03 | 1.4 m; the touch rule needs V > 0.3, so only its nose (phase 2 body scent) can find the player | 16 m |
| Parasaurolophus, day, walking | 0.8 | 44 m (observe 40 m) | 55 m |

---

## 12. Tests (PlayMode, new file `Tests/PlayMode/PerceptionTests.cs`)

Scene-free (fast, pure maths and `TestSnapshot`):

| Test | Asserts |
|---|---|
| `Stimuli_Ring_Overwrites_Oldest_And_Keeps_Sequence` | 300 emits into 128: oldest lost, readers jump, seq monotonic |
| `Stimuli_Emit_And_Read_Allocate_Nothing` | `GC.GetAllocatedBytesForCurrentThread` delta 0 over 10 000 emits + reads after warm-up |
| `Scent_Puff_Drifts_Downwind_Decays_And_Rain_Shortens_It` | centre moves along wind, strength halves near `tau ln 2`, rain lowers strength |
| `Visibility_Factors_Order` | crouch < stand, still < sprint, torch at night > no torch, bush cover, clamp |
| `Hearing_Radius_By_Surface_And_Rain` | rock > grass, storm masks |
| `Awareness_Thresholds_Hysteresis_And_Decay` | states rise and fall with hysteresis, decay after the hold |
| `Noise_Alone_Never_Engages` | repeated noises cap at Investigating (Alerted when close) |

Island (same `LoadIsland` helper as `DinosaurTests`, `Time.timeScale` up to 4 for long waits):

| Test | Phase | Asserts |
|---|---|---|
| `Crouched_Still_In_Bush_At_Night_Is_Not_Detected` | 1 | raptor at 12 m stays below Suspicious for 10 s |
| `Sprinting_In_View_Is_Detected_Quickly` | 1 | Engaged within 3 s at 25 m by day |
| `Chopping_Draws_Predator_To_Investigate` | 1 | chop noise 25 m from a Carnotaurus out of sight: Investigate, destination within 6 m of the tree |
| `Tree_Fall_Startles_Flyers` | 1 | `AmbientCreature` within 40 m boosted |
| `Torch_At_Night_Extends_Detection` | 1 | effective sight with torch > 2x without |
| `Bush_Rustle_Reveals_Position` | 1 | Motion stimulus: Suspicious, `LookPoint` at the bush |
| `Lost_Target_Goes_To_Last_Known_Position` | 1 | after LOS break, destination = last known, not the live player; Return within the search time |
| `Herd_Shares_Alert` | 1 | one Parasaurolophus Alerted: herd >= 0.6 next tick |
| `Missed_Arrow_Lures_Investigation` | 1 | Distraction noise pulls Investigate to the landing spot |
| `Cooking_Meat_Downwind_Draws_Raptor`, `..._Upwind_Does_Not` | 2 | wind set through `WeatherManager` test override |
| `Rain_Reduces_Scent_Reach` | 2 | same setup, rain 1: no investigation |
| `Fire_Fear_Stops_Predator_At_Light_Edge` | 2 | raptor stays outside `8 x 0.7` m while lit, enters after `SetLit(false)` |
| `Shelter_Halves_Cooking_Scent` | 2 | puff strength ratio 0.5 |
| `Felling_Trees_Lowers_Cover_At_Spot` | 3 | `TreeHarvest.Fell` on the 4 nearest trees: cover drops, exposure 1.15 |

Must stay green unchanged: `DinosaurTests` (4 tests, incl. `Predator_Chases_And_Bites_Player_Player_Can_Kill_It` and
`Herbivores_Flee_From_Predator_Warning`), `SurvivalM1Tests` (cooking), `ContextHintsTests` (tip queue cap 6).
`PerformanceTests.Island_Frame_Budget_With_All_Creatures` gains: `DinoSenses.LosThisFrame <= 6` every frame and the
perception marker under budget.

---

## 13. Phasing

| Phase | Build | Acceptance |
|---|---|---|
| **1. Hearing + visibility** | `Stimuli` (transient ring), `PerceptionConfig`, appended `DinosaurDefinition` fields + builder, `DinoSenses` (awareness, states, memory, herd share, linecast budget), `PlayerSignature` (light, posture, motion, bush cover, pulses), hooks marked phase 1 in 4.3, Chase / Observe / `LookPoint` fixes, presence indicator, tips `stealth` `noise` `torch_stealth` `bush`, `PlayerNoticed` event | scene-free tests + phase 1 island tests pass; existing `DinosaurTests` unchanged and green; perf budget met |
| **2. Scent + wind** | scent ring and puffs, `ItemDefinition.scent`, Campfire / Carcass / player emitters, `WeatherManager.WindDir` / `WindStrength` accessors + slow veer (Perlin over `GameClock`, +-60 deg; foliage follows), interest + scent investigation, fire fear lurk, shelter factor, rain effects, compass wind tick, threat markers, tips `scent` `wind`, `ScentInvestigated` | phase 2 tests pass; a night cook downwind of the raptor area draws a visit that stops at the firelight |
| **3. Tree-density cover** | `TreeHarvest.CoverAt(Vector3 p, float r, out int standing, out int felled)` on the existing grid, tree cover + clearing exposure in `PlayerSignature` | `Felling_Trees_Lowers_Cover_At_Spot`; camp in the forest measurably harder to spot than a cleared camp |
| 4. Loop hooks (later) | journal "observed undetected" notes, trust meter for taming, predators taking unattended food, camp signature for night visits, gear fields `noiseMul` / `scentMask` | owner decisions first |

### First build step

Phase 1, step 1: add `Core/Stimuli.cs` (transient ring only), `AI/PerceptionConfig.cs`, the appended
`DinosaurDefinition` fields, `Player/PlayerSignature.cs` (visibility factors + movement pulses) and `AI/DinoSenses.cs`,
then replace the bodies of `CanSee` / `CanHear` in `DinosaurController.Think` with `DinoSenses` behind the same FSM
calls. Ship with the seven scene-free tests and the four existing `DinosaurTests` green; world hooks (bush, chop, tree
fall, combat, arrows) follow as step 2.

---

## 14. Risks and owner decisions

| Item | Note |
|---|---|
| Chase loses its omniscience | raptors will lose the player more often; tune `trackGrace` and `scentTrackRange` before changing speeds |
| Fire as a safe zone | `fireFear` values make camp fires strong protection; Rift Tyrant (0.15) is the exception. Owner to confirm |
| Wind veer | makes wind counterplay real but also turns the foliage sway; today the direction never changes |
| HUD default | Auto vs Off for a more realistic feel |
| Builder ownership | perception data only through the new `PrimalPerceptionBuilder`; `PrimalGameplayBuilder` is never re-run, `PrimalDinoBuilder` fields untouched |
| Kills not saved (existing) | carcass scent disappears on load; acceptable until carcass persistence (Milestone 2) |
| Night raids while sleeping | out of scope; possible phase 4 use of the camp signature |
