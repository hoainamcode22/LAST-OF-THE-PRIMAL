# PrimalAtmosphereBuilder.Build 2026-09-30T16:38:02

## Assets
- Assets/_Project/Resources/WeatherConfig.asset kept (hand-tuned values stay)
- ambience clips: 0 import settings changed, 0 missing (run Tools/Audio/ambience_synth.py)
## Scene
- TimeManager: lighting keys current (kept)
- WeatherManager: config WeatherConfig
- AmbienceManager: 9/9 layers, birds 8, distant calls 6, critters 4, thunder 2+3
- Documentation/PCPhase/LOCATIONS.md: radius for 18 ids
  - zone beach: r 70 (EnvLocation), day 0 / night +0,5 C, humidity 0,60
  - zone shipwreck: r 25 (EnvLocation), day 0 / night +0,5 C, humidity 0,60
  - zone forest: r 60 (EnvLocation), day -1,0 / night +0,5 C, humidity 0,60
  - zone deep_forest: r 60 (EnvLocation), day -2,5 / night +1,0 C, humidity 0,80
  - zone river: r 55 (EnvLocation), day -1,0 / night -1,0 C, humidity 0,75
  - zone waterfall: r 24 (EnvLocation), day -2,0 / night -1,5 C, humidity 0,95
  - zone meadow: r 70 (EnvLocation), day +0,5 / night -0,5 C, humidity 0,50
  - zone canyon: r 36 (EnvLocation), day -2,0 / night -1,0 C, humidity 0,55
  - zone wetland: r 38 (EnvLocation), day -1,5 / night -0,5 C, humidity 0,95
  - zone cave: r 15 (EnvLocation), day 0 / night 0 C, humidity 0,80, indoor, stable air 15 C
  - zone ridge: r 45 (EnvLocation), day -1,0 / night -1,5 C, humidity 0,40
  - zone volcano: r 45 (EnvLocation), day +3,0 / night +3,0 C, humidity 0,30
  - zone predator_territory: r 45 (EnvLocation), day 0 / night 0 C, humidity 0,60
  - zone herbivore_valley: r 85 (EnvLocation), day 0 / night 0 C, humidity 0,60
  - zone old_camp: r 12 (EnvLocation), day 0 / night 0 C, humidity 0,60
  - zone fossil_bed: r 10 (EnvLocation), day 0 / night 0 C, humidity 0,50
  - zone nest: r 14 (EnvLocation), day 0 / night 0 C, humidity 0,60
  - zone migration_view: r 10 (EnvLocation), day -0,5 / night -1,0 C, humidity 0,45
- ZoneManager: 18 ENV locations registered (31 zones in all); no marker yet: none
- [Atmosphere]: 34 generated children cleared, rebuilding
- [Atmosphere]/Sky: CloudVeil (weather clouds over the procedural sky)
- emitters: River 13, Custom 3, Wetland 3, Stream 2, Waterfall 1
- HZ_volcano at (-122.00, 44.37, -216.00) rings 61 / 32 / 10 m, heat +6 / +14 / +24 C, smoke 0,35
- ENV lava line: 6 points, vent (-100.00, 44.70, -232.00)
- HZ_vent at (-100.00, 44.70, -232.00): rings 20 / 11 / 5 m, +7 / +15 / +30 C, smoke 0.55 over 55 m
- lava: 5 small hazard zones along the lava, 3 lava emitters
- reverb: 1 cave reverb zones
scene saved
