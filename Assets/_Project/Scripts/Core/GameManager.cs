using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Building;
using PrimalFrontier.Items;
using PrimalFrontier.Player;
using PrimalFrontier.Story;
using PrimalFrontier.Survival;
using PrimalFrontier.UI;
using PrimalFrontier.World;

namespace PrimalFrontier.Core
{
    public enum GameState { Boot, Title, Intro, Playing, Dead, Sleeping }

    /// <summary>
    /// Game flow for the island scene: title, new game (intro + tutorial), continue (load), save, sleep, death and
    /// respawn. The survival rules and numbers live in PlayerSurvival / SurvivalEnvironment / SurvivalConfig; this class
    /// only runs the flow (fade, time skip, save) and hooks the environment up once the player exists.
    /// Scene first: the player, the systems ([Systems]/...) and the UI ([UI]) placed in the scene are used as they are,
    /// with their Inspector values. Anything missing is created here from the fallback fields below, so an empty
    /// scene still runs.
    /// </summary>
    [DefaultExecutionOrder(-80)]
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("Setup")]
        [Tooltip("only used when the scene has no object tagged Player")] public GameObject playerPrefab;
        public Transform spawnPoint;
        [Tooltip("the Player placed in the scene starts (and respawns on a new game) where it stands in the editor; off = at the spawn point")]
        public bool startWherePlayerIsPlaced = true;
        public Transform titleCameraFocus;
        public ItemDatabase database;
        [Header("Fallbacks (only fill what the scene objects leave empty)")]
        public GameObject torchFlamePrefab;
        public Material ghostValid, ghostInvalid, rainMaterial;
        public AudioClip ambOcean, ambForest, ambNight, ambWind, ambRain, ambStorm;
        [Header("Flow")]
        public bool showTitle = true;
        public bool playIntro = true;
        public float startHour = 9f;
        [Tooltip("only used when the scene has no TimeManager; otherwise edit [Systems]/Time")] public float secondsPerHour = 100f;
        [Header("Habitats (tutorial fallback when no creature exists)")]
        public Vector3 herbivoreHabitat; public float herbivoreHabitatRadius = 25f;
        public Vector3 campArea; public float campRadius = 30f;
        [Tooltip("legacy (pre-mission tutorial markers); unused: missions place their own markers")] public List<string> targetSteps = new List<string>();
        public List<Vector3> targetPositions = new List<Vector3>();

        /// <summary>automated tests: skip the title / intro without editing the scene</summary>
        public static bool? ForceShowTitle, ForcePlayIntro;
        public GameState State { get; private set; } = GameState.Boot;
        public GameObject Player { get; private set; }
        public bool IntroDone { get; set; }
        public float PlaySeconds { get; set; }
        public bool HasRespawn { get; private set; }
        public Vector3 RespawnPoint { get; private set; }

        TimeManager _time; WeatherManager _weather; AmbienceManager _amb; JournalSystem _journal; TutorialManager _tutorial;
        IntroSequence _intro; ZoneManager _zones; BuildSystem _build; UIManager _ui; HUDManager _hud; DeathScreenUI _death;
        MissionSystem _missions; ProtagonistVoice _voice; DeathSystem _deathSys; Minimap _minimap;
        readonly List<WorldPickup> _scenePickups = new List<WorldPickup>();
        float _deadTime; float _autosave; ThirdPersonCamera _cam; float _titleOrbit;

        void Awake()
        {
            Instance = this;
            if (!database) database = ItemDatabase.Instance;
            _zones = FindFirstObjectByType<ZoneManager>();
            _time = Ensure<TimeManager>("[Time]", out bool newTime); if (newTime) _time.secondsPerHour = secondsPerHour;
            GameClock.SecondsPerHour = _time.secondsPerHour;
            _weather = Ensure<WeatherManager>("[Weather]", out _); if (!_weather.rainMaterial) _weather.rainMaterial = rainMaterial;
            _amb = Ensure<AmbienceManager>("[Ambience]", out _);
            if (!_amb.ocean) _amb.ocean = ambOcean; if (!_amb.forest) _amb.forest = ambForest; if (!_amb.night) _amb.night = ambNight;
            if (!_amb.wind) _amb.wind = ambWind; if (!_amb.rain) _amb.rain = ambRain; if (!_amb.storm) _amb.storm = ambStorm;
            _journal = Ensure<JournalSystem>("[Journal]", out _);
            _tutorial = Ensure<TutorialManager>("[Tutorial]", out _);
            _intro = Ensure<IntroSequence>("[Intro]", out _);
            // story: missions, the survivor's lines and the death rules (one [Story] object unless the scene placed them elsewhere)
            _missions = Ensure<MissionSystem>("[Story]", out _);
            _voice = FindFirstObjectByType<ProtagonistVoice>(); if (!_voice) _voice = _missions.gameObject.AddComponent<ProtagonistVoice>();
            _deathSys = FindFirstObjectByType<DeathSystem>(); if (!_deathSys) _deathSys = _missions.gameObject.AddComponent<DeathSystem>();
            _build = Ensure<BuildSystem>("[Build]", out _); if (!_build.ghostValid) _build.ghostValid = ghostValid; if (!_build.ghostInvalid) _build.ghostInvalid = ghostInvalid;
            Ensure<OceanShore>("[OceanShore]", out _);
            var th = Ensure<TreeHarvest>("[Trees]", out _);
            if (!th.wood && database) th.wood = database.Item("wood");
            if (!th.fiber && database) th.fiber = database.Item("fiber");
            // UI: the [UI] object in the scene (its canvases are saved there too), or a new one
            var sceneUi = FindFirstObjectByType<UIManager>();
            var uiRoot = sceneUi ? sceneUi.gameObject : new GameObject("[UI]");
            _ui = uiRoot.GetOrAdd<UIManager>();
            _hud = uiRoot.GetOrAdd<HUDManager>();
            uiRoot.GetOrAdd<InventoryUI>(); uiRoot.GetOrAdd<JournalUI>(); uiRoot.GetOrAdd<PauseMenuUI>(); uiRoot.GetOrAdd<TitleScreenUI>();
            _death = uiRoot.GetOrAdd<DeathScreenUI>();
            _minimap = uiRoot.GetOrAdd<Minimap>(); if (MobileHUD.Supported) uiRoot.GetOrAdd<MobileHUD>();   // touch controls: mobile builds only
            uiRoot.GetOrAdd<ObjectiveUI>();                   // current objective line + chapter (MissionSystem)
            uiRoot.GetOrAdd<ContextHints>();                  // key hints + onboarding tips (also in scenes baked before it existed)
            uiRoot.GetOrAdd<PerceptionIndicator>();           // stealth eye / noise ring / threat markers (AI perception)
            SaveSystem.Track();
            // creatures in the save file (alive / dead, health, carcass left): restored after ResetWorld respawned them
            var spawner = FindFirstObjectByType<AI.DinosaurSpawner>();
            if (spawner) { _creatureSave = new AI.CreatureSaveSection(spawner); SaveSystem.RegisterSection(_creatureSave); }
            foreach (var p in FindObjectsByType<WorldPickup>(FindObjectsInactive.Include, FindObjectsSortMode.None)) _scenePickups.Add(p);
        }

        /// <summary>the scene's component of this type, or a new object with it (created = true)</summary>
        T Ensure<T>(string name, out bool created) where T : Component
        {
            var c = FindFirstObjectByType<T>(); created = !c;
            return c ? c : new GameObject(name).AddComponent<T>();
        }

        void OnDestroy() { if (Instance == this) Instance = null; Time.timeScale = 1f; if (_creatureSave != null) SaveSystem.UnregisterSection(_creatureSave); }
        AI.CreatureSaveSection _creatureSave;

        void Start()
        {
            SpawnPlayer();
            HookEnvironment();
            GameSettings.Apply(); GameSettings.ApplyQuality();
            if (ForceShowTitle.HasValue) showTitle = ForceShowTitle.Value;
            if (ForcePlayIntro.HasValue) playIntro = ForcePlayIntro.Value;
            if (showTitle) EnterTitle(); else NewGame();
        }

        // ------------------------------------------------------------------ setup
        void SpawnPlayer()
        {
            Player = GameObject.FindGameObjectWithTag("Player");
            if (Player && startWherePlayerIsPlaced)
            {
                // the Player placed in the scene decides where the game starts
                if (!spawnPoint) spawnPoint = new GameObject("[PlayerStart]").transform;
                spawnPoint.SetPositionAndRotation(Player.transform.position, Player.transform.rotation);
            }
            Vector3 pos = spawnPoint ? spawnPoint.position : Vector3.up; Quaternion rot = spawnPoint ? spawnPoint.rotation : Quaternion.identity;
            if (!Player && playerPrefab) Player = Instantiate(playerPrefab, pos, rot);
            if (!Player) { Debug.LogError("[GameManager] no player"); return; }
            Player.name = "Player";
            if (!PlayerInputReader.Instance) new GameObject("[Input]").AddComponent<PlayerInputReader>();
            // gameplay components (the prefab has them already; Inspector values on the Player are kept)
            var inv = Player.GetComponent<InventorySystem>();
            if (!inv) { inv = Player.AddComponent<InventorySystem>(); inv.slotCount = 32; inv.hotbarSize = 8; }
            inv.EnsureSlots();
            var craft = Player.GetOrAdd<CraftingSystem>(); if (!craft.inventory) craft.inventory = inv;
            if (!Player.GetComponent<PlayerSurvival>()) Player.AddComponent<PlayerSurvival>();
            if (!Player.GetComponent<PlayerInteraction>()) Player.AddComponent<PlayerInteraction>();
            var eq = Player.GetOrAdd<PlayerEquipment>(); if (!eq.torchFlamePrefab && torchFlamePrefab) eq.torchFlamePrefab = torchFlamePrefab;
            if (!Player.GetComponent<PlayerCombat>()) Player.AddComponent<PlayerCombat>();
            Player.GetOrAdd<PlayerSignature>();               // what creatures can see / hear / smell of the player (AI perception)
            PlayerLocator.Player = Player.transform;
            // camera
            var camGo = Camera.main ? Camera.main.gameObject : new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)) { tag = "MainCamera" };
            _cam = camGo.GetOrAdd<ThirdPersonCamera>();
            _cam.target = Player.transform; _cam.ignoreMask = 1 << Player.layer;
            Player.GetComponent<PlayerMotor>().CameraTransform = camGo.transform;
            _build.Bind(Player); _tutorial.Bind(Player);
            _hud.Bind(Player); InventoryUI.Instance.Bind(Player);
            _ui.BlockPause = () => _build.Active;
            var hp = Player.GetComponent<PlayerHealth>(); hp.Died += OnDied;
        }

        void HookEnvironment()
        {
            SurvivalEnvironment.Hook(_time, _weather, _zones, Player);          // air temperature, rain, heat
            Bedroll.Hour = () => _time ? _time.hour : 12f;
            Bedroll.SleepRequested = (b, p) => { if (State == GameState.Playing) StartCoroutine(Sleep(b)); };
            Shelter.Rested = s => { SetRespawn(true, s.transform.position + s.transform.forward * 1.8f); _tutorial.NotifyRested(); SaveGame(); };
            _tutorial.NearCamp = p =>
            {
                foreach (var c in Campfire.All) if (c && (c.transform.position - p).sqrMagnitude < 12f * 12f) return true;
                return campRadius > 0f && (p - campArea).sqrMagnitude < campRadius * campRadius && campArea != Vector3.zero;
            };
            _tutorial.InHerbivoreHabitat = p => herbivoreHabitat != Vector3.zero && Vector2.Distance(new Vector2(p.x, p.z), new Vector2(herbivoreHabitat.x, herbivoreHabitat.z)) < herbivoreHabitatRadius;
            // the objective marker now comes from the focused mission (MissionSystem.MarkerTarget); the predator roar is the
            // "Survive the First Night" mission's start cue
        }

        // ------------------------------------------------------------------ states
        void EnterTitle()
        {
            State = GameState.Title;
            ResetWorld();
            _ui.allowGameplayMenus = false; _ui.Open(UIScreen.Title);
            _hud.SetHudVisible(false); _hud.Fade(1f, 0f); _hud.Fade(0f, 2f);
            _weather.SetWeather(WeatherState.Clear, -1f, true); _weather.allowRandom = false;
            _time.Set(1, 17.2f); _time.paused = true;
            if (_cam) { _cam.InputEnabled = false; _cam.enabled = false; }     // the title orbit drives the camera
            var sv = Player.GetComponent<PlayerSurvival>(); sv.Paused = true;
        }

        public void NewGame()
        {
            StopAllCoroutines();
            ResetWorld();
            SaveSystem.ResetTracking();
            _ui.Open(UIScreen.None); _ui.allowGameplayMenus = false;
            _time.paused = false; _time.Set(1, startHour); GameClock.Now = 0;
            _weather.allowRandom = false; _weather.SetWeather(WeatherState.Clear, -1f, true);
            PlaySeconds = 0f; HasRespawn = false;
            var craft = Player.GetComponent<CraftingSystem>(); craft.InitKnown(database);
            var motor = Player.GetComponent<PlayerMotor>();
            if (spawnPoint) motor.Warp(spawnPoint.position, spawnPoint.rotation);
            if (_cam) { _cam.enabled = true; _cam.InputEnabled = true; _cam.Yaw = Player.transform.eulerAngles.y; _cam.SnapBehindTarget(); }
            if (playIntro)
            {
                State = GameState.Intro;
                _intro.Finished -= OnIntroDone; _intro.Finished += OnIntroDone;
                _intro.Play(Player);
            }
            else OnIntroDone();
        }

        void OnIntroDone()
        {
            IntroDone = true;
            State = GameState.Playing;
            _ui.allowGameplayMenus = true;
            _hud.SetHudVisible(true); if (_hud.FadeAlpha > 0.01f) _hud.Fade(0f, 0.8f);
            var sv = Player.GetComponent<PlayerSurvival>(); sv.Paused = false;
            ThirdPersonCamera.LockCursor(true);
            _tutorial.Bind(Player);
            if (!_tutorial.Running && !_tutorial.Completed) _tutorial.Begin(0);
            _missions.Begin();
            _weather.allowRandom = true;
        }

        public void ContinueGame() => LoadGame();

        public void LoadGame()
        {
            var d = SaveSystem.Read();
            if (d == null) { _hud.Notify("Load failed: " + SaveSystem.LastError); return; }
            StopAllCoroutines();
            ResetWorld();
            _time.paused = false;
            SaveSystem.Apply(this, d);
            State = GameState.Playing;
            _ui.Open(UIScreen.None); _ui.allowGameplayMenus = true;
            _hud.SetHudVisible(true); _hud.Fade(1f, 0f); _hud.Fade(0f, 1.2f);
            var sv = Player.GetComponent<PlayerSurvival>(); sv.Paused = false;
            var drv = Player.GetComponent<PlayerAnimationDriver>(); drv.Respawn(); drv.StopAction();
            if (_cam) { _cam.enabled = true; _cam.InputEnabled = true; _cam.Yaw = Player.transform.eulerAngles.y; _cam.SnapBehindTarget(); }
            if (!IntroDone) IntroDone = true;
            _weather.allowRandom = _missions.Chapter > 1 || _missions.IsDone("make_fire");
            _hud.ShowBanner("DAY " + _time.day, "", 2.5f);
        }

        public bool SaveGame()
        {
            if (State != GameState.Playing && State != GameState.Sleeping) return false;
            var climb = Player ? Player.GetComponent<PlayerClimb>() : null;
            if (climb && climb.IsClimbing) { _hud.Notify("Get down before saving."); return false; }
            return SaveSystem.Save(this);
        }

        public void QuitToTitle() { StopAllCoroutines(); EnterTitle(); }

        /// <summary>back to the untouched island (new game / before loading)</summary>
        public void ResetWorld()
        {
            Time.timeScale = 1f;
            _build.Cancel();
            PlacedStructure.DestroyAll();
            WorldPickup.ClearDropped();
            foreach (var p in _scenePickups) if (p) p.gameObject.SetActive(true);
            foreach (var it in Interactable.Active.ToArray())
            {
                if (it is ResourceNode n) n.Regrow();
                else if (it is LootContainer l) l.Restore(false);
                else if (it is Examinable e) e.Restore(false);
            }
            var th = FindFirstObjectByType<TreeHarvest>(); if (th) th.RestoreAll();
            foreach (var fc in FindObjectsByType<FruitCluster>(FindObjectsSortMode.None)) fc.Regrow();
            var dinos = FindFirstObjectByType<AI.DinosaurSpawner>(); if (dinos && State != GameState.Boot) dinos.SpawnAll();
            Stimuli.Clear();                                   // no noise / smell carries over into the new or loaded game
            if (Player) { var sig = Player.GetComponent<PlayerSignature>(); if (sig) sig.ResetState(); }
            _journal.ResetAll();                               // pages and creature notes
            if (_zones) _zones.SetVisited(new string[0]);
            _tutorial.ResetIdle();
            _missions.ResetAll(); _voice.ResetAll(); _deathSys.ResetAll();
            if (_minimap) _minimap.ResetDiscovery();
            OnboardingTips.Clear();                            // a load puts the saved ones back (SaveSystem.Apply)
            if (Player)
            {
                var hp = Player.GetComponent<PlayerHealth>(); hp.Revive(1f);
                Player.GetComponent<PlayerSurvival>().ResetToStart();                 // start stats: SurvivalConfig
                var craft = Player.GetComponent<CraftingSystem>(); craft.CancelAll(); craft.InitKnown(database);
                var inv = Player.GetComponent<InventorySystem>(); inv.Clear(); inv.SetActiveSlot(0);
                var pi = Player.GetComponent<PlayerInteraction>(); pi.StopAction(); pi.Suspended = false;
                var drv = Player.GetComponent<PlayerAnimationDriver>(); drv.Respawn(); drv.StopAction();
                if (spawnPoint) Player.GetComponent<PlayerMotor>().Warp(spawnPoint.position, spawnPoint.rotation);
            }
            IntroDone = false;
        }

        public void SetRespawn(bool has, Vector3 p) { HasRespawn = has; RespawnPoint = p; }

        // ------------------------------------------------------------------ loop
        void Update()
        {
            if (State == GameState.Playing) PlaySeconds += Time.deltaTime;
            if (State == GameState.Title && _cam && titleCameraFocus)
            {
                _titleOrbit += Time.deltaTime * 2.5f;
                var c = _cam.transform; var f = titleCameraFocus.position;
                c.position = f + Quaternion.Euler(0, _titleOrbit + 200f, 0) * new Vector3(0, 7f, -26f);
                c.rotation = Quaternion.LookRotation(f + Vector3.up * 2f - c.position);
            }
            if (State == GameState.Dead && Time.time - _deadTime > 3f && _ui.Current != UIScreen.Death)
            {
                _death.SetCause(DeathCause());
                _death.SetDetails(LostText(), RespawnAtCamp(out _) ? "You wake where you last rested." : "You wake on the beach.");
                _ui.Open(UIScreen.Death);
            }
            if (State == GameState.Playing)
            {
                _autosave += Time.deltaTime;
                if (_autosave > 600f && _ui.Current == UIScreen.None && !Player.GetComponent<PlayerInteraction>().InAction) { _autosave = 0f; SaveGame(); }
            }
        }

        void LateUpdate()
        {
            if (State == GameState.Title && _cam && titleCameraFocus)
            {
                var c = _cam.transform; var f = titleCameraFocus.position;
                c.position = f + Quaternion.Euler(0, _titleOrbit + 200f, 0) * new Vector3(0, 7f, -26f);
                c.rotation = Quaternion.LookRotation(f + Vector3.up * 2f - c.position);
            }
        }

        string DeathCause()
        {
            var sv = Player.GetComponent<PlayerSurvival>();
            if (sv.IsDehydrated) return "Thirst took you.";
            if (sv.IsStarving) return "Hunger took you.";
            if (sv.IsFreezing) return "The cold took you.";
            return "Your wounds were too much.";
        }

        string LostText()
        {
            int n = _deathSys ? _deathSys.LastItems : 0;
            return n <= 0 ? "You carried nothing worth losing." : "What you dropped lies where you fell (" + n + (n == 1 ? " item)." : " items).");
        }

        void OnDied()
        {
            State = GameState.Dead; _deadTime = Time.time;
            _build.Cancel();
            _deathSys.OnPlayerDied(Player);                    // a share of the pack stays at the death spot as a bundle
            GameEvents.Raise(GameEventType.PlayerDied, "player");
        }

        /// <summary>the last rested shelter / bed still stands (a respawn point with nothing there any more falls back to the beach)</summary>
        bool RespawnAtCamp(out Vector3 at)
        {
            at = RespawnPoint;
            if (!HasRespawn) return false;
            if (Shelter.Nearest(RespawnPoint, 6f) != null) return true;
            foreach (var b in FindObjectsByType<Bedroll>(FindObjectsSortMode.None)) if (b && (b.transform.position - RespawnPoint).sqrMagnitude < 6f * 6f) return true;
            return false;
        }

        /// <summary>
        /// wake at the last rested shelter / bed (else the beach); the journal, recipes, world and structures stay; the pack keeps
        /// what the death rules left in it; saved at once so the death counts
        /// </summary>
        public void Respawn()
        {
            if (State != GameState.Dead) return;
            var p = RespawnAtCamp(out var camp) ? camp : (spawnPoint ? spawnPoint.position : Player.transform.position);
            Player.GetComponent<PlayerMotor>().Warp(p + Vector3.up * 0.1f, Player.transform.rotation);
            Player.GetComponent<PlayerHealth>().Revive(SurvivalConfig.Instance.respawnHealth);
            Player.GetComponent<PlayerSurvival>().ApplyRespawn();
            var drv = Player.GetComponent<PlayerAnimationDriver>(); if (drv) { drv.Respawn(); drv.StopAction(); }
            var pi = Player.GetComponent<PlayerInteraction>(); if (pi) { pi.StopAction(); pi.Suspended = false; }
            _ui.Close(); State = GameState.Playing;
            if (_cam) { _cam.enabled = true; _cam.InputEnabled = true; _cam.Yaw = Player.transform.eulerAngles.y; _cam.SnapBehindTarget(); }
            _hud.Fade(1f, 0f); _hud.Fade(0f, 1.5f);
            GameEvents.Raise(GameEventType.PlayerRespawned, "player");
            SaveGame();
        }

        IEnumerator Sleep(Bedroll b)
        {
            State = GameState.Sleeping;
            var input = PlayerInputReader.Instance; if (input) input.GameplayBlocked = true;
            var drv = Player.GetComponent<PlayerAnimationDriver>();
            var motor = Player.GetComponent<PlayerMotor>();
            motor.Warp(b.transform.position + Vector3.up * 0.05f, b.transform.rotation);
            drv.PlayAction(PlayerActions.Sleep);
            _hud.Fade(1f, 1.6f);
            yield return new WaitForSeconds(1.8f);
            float hours = _time.HoursUntil(6f);
            _time.SkipHours(hours);
            Stimuli.Clear(); AI.DinosaurController.ForgetPlayerAll();          // a night passed: old noises and smells are gone
            Player.GetComponent<PlayerSurvival>().ApplySleep(hours);            // sleep costs: SurvivalConfig
            SetRespawn(true, b.transform.position + b.transform.right * 1.2f);
            var placed = b.GetComponentInParent<PlacedStructure>();              // bedroll, tent... (any number of beds)
            GameEvents.Raise(GameEventType.Slept, placed ? placed.itemId : b.name, Mathf.RoundToInt(hours), b.transform.position);
            drv.StopAction();
            yield return new WaitForSeconds(0.4f);
            State = GameState.Playing;
            SaveGame();
            _hud.Fade(0f, 2f);
            _hud.ShowBanner("DAY " + _time.day, "Dawn", 3f);
            if (input) input.GameplayBlocked = _ui.Current != UIScreen.None;
        }
    }
}
