# DAY / NIGHT SYSTEM

Existing: `TimeManager` (90 s per game hour, sunrise 6:00, sunset 19:30), `WeatherManager` (clear / cloudy / rain /
storm), night sky with stars, predators see 40 % less at night.
Milestone 1 makes night matter through `SurvivalConfig` (no new manager):
- air is `nightAirOffset` (-4 C) colder at night (blended over dusk / dawn) in `SurvivalEnvironment`, so a wet night
  without fire or shelter makes the player cold (stamina penalty, then freezing damage);
- campfire (+16 C near it), tent (+6 C, rain cover), torch (+2 C, light) are the answers;
- sleeping in a tent / bedroll from 18:00 skips to 6:00 with sleep costs from the config and saves the game;
- the tutorial asks "Shelter before night" around 17:00.
Exploring at night stays possible, just riskier.
