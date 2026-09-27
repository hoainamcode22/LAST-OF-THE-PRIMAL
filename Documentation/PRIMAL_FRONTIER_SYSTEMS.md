# PRIMAL FRONTIER - Systems (Phase 2)

Values below are the defaults in code; almost all are Inspector fields and can be tuned on the scene objects.

## Player movement and animation
- `PlayerMotor`: walk / run / sprint / crouch / jump, slopes and 0.2 m steps. Phase 2: turning is smoothed
  (`turnSmoothTime` 0.09 s, SmoothDampAngle) instead of snapping; `Burst(velocity, seconds)` drives the dodge.
- `PlayerAnimationDriver`: speed / direction parameters, action states, idle variation every 7-14 s (`IdleVariant`
  trigger), attack detection through `PlayerActions.IsAttack`.
- `PlayerIK` (new, on the Animator object; the controller's base layer has the IK pass on):
  - feet: raycast placement on slopes and steps, pelvis lowered to the lower foot, lighter while running;
  - lean into turns (up to 9 degrees);
  - head looks at the current interactable, a look target, or where the camera points;
  - climbing: hands and feet placed on the trunk surface, right hand reaches for the fruit being picked.
- 47 animation clips (list and test in the character pipeline doc).

## Combat
- Spear: tap = thrust, a second tap within 1.0 s = `Spear_Attack_2` (combo hit x1.25 damage); hold = heavy; aim + release
  = throw. Bow: aim, draw, release.
- Knife and tools: `Knife_Attack` (reach 1.35 m). The flint knife is now a melee weapon (`WeaponKind.Knife`).
- Dodge (V / gamepad North / touch DODGE): 5.2 m/s burst for 0.32 s, 14 stamina, 0.55 s cooldown, 0.34 s of
  invulnerability (a dodged hit raises `PlayerHealth.Evaded`), dust puff, `PlayerDodged` event.
- `InCombat` stays true 4 s after fighting (camera tightens, PlayerState = Combat).

## Climbing and fruit
- `Climbable` (fruit trees): start / end points, trunk radius, climb 0.6 m/s up, 0.55 m/s down, 3 stamina per second
  climbing up. `PlayerClimb`: E to start (Climb_Start), W/S to climb (Climb_Up / Climb_Down), E picks fruit in reach
  (Harvest_Fruit), Space / C / V lets go (Climb_End). Getting hurt or running out of stamina drops you. Saving is
  refused while climbing.
- `FruitCluster`: 3 Wild Fruit per bunch, grows back after 30 in-game hours. Wild Fruit: +16 food, +8 water, +5 stamina.
- Four fruit trees under `[Gameplay]/FruitTrees` (near camp, meadow edge, berry area, pond), placed on dry, flat, clear
  ground (a tree that ended up in the pond was moved out automatically).

## Survival
- `PlayerSurvival`: hunger, thirst, stamina, body temperature, wetness, sickness.
- Water purification: pond and stream water is **dirty**. Filling a container gives dirty water; drinking it (or drinking
  from the source by hand) has a 20 % chance of stomach sickness. Put dirty water in your pack near a **lit campfire** and
  use "Boil water": about 8 s, steam, the container becomes clean water (`WaterBoiled` event).
- Sickness: thirst drains x1.6 and stamina recovers at half speed until it passes. HUD shows "Stomach sick".
- The dirty flag is saved (save version 2) and shown on slots (colour) and in the inventory text ("unboiled").

## Resources, crafting, building
- Resource nodes wobble on each hit and shrink as they are used up; a dust puff when a node is emptied. Wood / stone chips,
  leaves and sounds per hit (existing VFX library).
- Crafting tabs: **ALL, TOOLS, WEAPONS, FOOD, WATER, BUILDING, SURVIVAL** (the waterskin moved to WATER). Queue of up to 5.
- Building (`BuildSystem`): green / red ghost, R or wheel rotates, left click builds (Build animation), right click / Esc
  cancels. Checks: aim at the ground, slope <= 24 degrees, not in water, not too close to you, nothing in the way.
  Touch: BUILD / ROTATE / CANCEL buttons.

## World: time, weather, landmark
- `TimeManager`: day / night, sun colour and angle, fog colour / density per hour, ambient light, air temperature.
- `WeatherManager`: **Clear -> Cloudy -> Rain -> Cloudy -> Clear**, plus the opening **Storm**:
  - clouds: 12 % per in-game hour on a clear day, 1.5-4 h; a cloudy sky rains with 25 % per hour, 1-3 h;
  - `Overcast` dims the sun and thickens fog, `Intensity` drives the rain particles;
  - `Wetness` (surfaces) soaks in ~40 s of rain and dries over a few minutes;
  - wind: calm with slow gusts, strong in storms;
  - storms: lightning (a directional light flash, two pulses) and thunder 0.3-3 s later depending on distance;
  - globals for shaders every frame: `_PF_Wind` (xy direction, z strength, w gust), `_PF_Wetness`, `_PF_Overcast`.
- Player wetness look (`PlayerWetLook`): skin and clothes get darker and shinier when wet (property blocks, no asset change).
- Volcano landmark (`World/Landmarks/Volcano`, `VolcanoLandmark`): across the sea about 560 m from the start beach,
  scenery only (not reachable, playable world unchanged). Smoke plume drifting with the wind, crater glow and a glowing
  lava flow that show at dusk / night, embers, and a rumble every 4-9 minutes (low boom, slight camera tremor, dark puff,
  `VolcanoRumble` event).

## Map and HUD
- Minimap (top right): baked top-down island picture (1024 px, no run-time camera), north up, player arrow; markers for
  camp fires and shelters, fresh water, the wreck and visited places, nearby resource areas, creatures only while spotted
  (red = dangerous), and the tutorial target clamped to the edge. M / tap opens the whole island.
- Compass moved to the top centre, objective under the minimap.
- Touch HUD: see the mobile guide.

## Creatures
- `DinosaurController` (FSM): idle, wander, eat, drink, rest, observe, alert, investigate, flee, chase, attack, return, dead.
- **Attack tell** (new): the animal stops, turns to you and growls for 0.32 s (normal) / 0.55 s (heavy) before the strike;
  the head is drawn back during the wind-up (DinoLife). The hit still misses if you are out of reach or behind it.
- `DinoLife` (new, on every creature): head turns toward what the animal watches (clamped 45 degrees yaw, 22 pitch,
  blended in / out, skipped beyond 70 m, never accumulates when the Animator is culled); eyes blink every 2.5-7 s,
  glance toward the target, half closed after death.
- Readable eyes for 8 species (Rift Tyrant keeps its modelled eyes and lids).

## Quality presets (Low / Medium / High / Ultra)
| | Low | Medium | High | Ultra |
|---|---|---|---|---|
| Quality level | Mobile | Mobile | PC | PC |
| Render scale | 0.75 | 0.9 | 1.0 | 1.0 |
| Shadow distance / cascades | 30 m / 1 | 45 m / 2 | 70 m / 2 | 110 m / 4 |
| MSAA | off | 2x | 4x | 4x |
| LOD bias | 0.7 | 1.0 | 1.5 | 2.0 |
| Pooled effects at once | 24 | 40 | 64 | 96 |
| Grass distance / density | 35 m / 0.4 | 55 m / 0.6 | 80 m / 0.85 | 110 m / 1 |
| Phone frame cap | 30 | 30 | 60 | 60 |

Render scale, shadows, MSAA and LOD bias apply in **builds only** (in the editor they would be written into the project's
assets). Low also skips small far-away puffs (footstep dust further than 22 m from the camera). Phones default to Low.
