using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using PrimalFrontier.Building;
using PrimalFrontier.Items;
using PrimalFrontier.Player;
using PrimalFrontier.Story;
using PrimalFrontier.Survival;
using PrimalFrontier.World;

namespace PrimalFrontier.Core
{
    /// <summary>
    /// Writes / reads SaveData as JSON in persistentDataPath (atomic: temp file then replace). Captures player,
    /// inventory, recipes, crafting queue, time, weather, journal, tutorial, resource nodes, trees, pickups, loot, structures.
    /// Unknown item ids are skipped (never crash on an old save), newer versions are refused.
    /// </summary>
    public static class SaveSystem
    {
        public static string Folder => Path.Combine(Application.persistentDataPath, "PrimalFrontier");
        public static string PathFor(int slot) => Path.Combine(Folder, $"save_{slot}.json");
        public static bool Exists(int slot = 0) => File.Exists(PathFor(slot));
        public static string LastError { get; private set; }

        /// <summary>scene pickups taken this session (WorldPickup.Taken)</summary>
        static readonly HashSet<string> _taken = new HashSet<string>();
        public static void Track() { WorldPickup.Taken -= OnTaken; WorldPickup.Taken += OnTaken; }
        static void OnTaken(WorldPickup p) => _taken.Add(p.SaveId);
        public static void ResetTracking() => _taken.Clear();

        public static bool Save(GameManager gm, int slot = 0)
        {
            try
            {
                var d = Capture(gm);
                Directory.CreateDirectory(Folder);
                string path = PathFor(slot), tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(d, true));
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                LastError = null;
                GameEvents.Raise(GameEventType.GameSaved, path);
                return true;
            }
            catch (Exception e) { LastError = e.Message; Debug.LogError("[SaveSystem] save failed: " + e); return false; }
        }

        public static SaveData Read(int slot = 0)
        {
            try
            {
                if (!Exists(slot)) { LastError = "No save file."; return null; }
                var d = JsonUtility.FromJson<SaveData>(File.ReadAllText(PathFor(slot)));
                if (d == null) { LastError = "Save file is empty or damaged."; return null; }
                if (d.version > SaveData.CurrentVersion) { LastError = $"Save version {d.version} is newer than this game ({SaveData.CurrentVersion})."; return null; }
                return d;
            }
            catch (Exception e) { LastError = "Save file is damaged: " + e.Message; Debug.LogError("[SaveSystem] " + e); return null; }
        }

        public static void Delete(int slot = 0) { try { if (Exists(slot)) File.Delete(PathFor(slot)); } catch { } }

        static List<SlotData> Slots(InventorySystem inv)
        {
            var l = new List<SlotData>();
            if (!inv) return l;
            for (int i = 0; i < inv.Slots.Length; i++)
            {
                var s = inv.Slots[i]; if (s.IsEmptyOrNull()) continue;
                l.Add(new SlotData { slot = i, item = s.item.id, count = s.count, durability = s.durability, water = s.water, dirty = s.dirty });
            }
            return l;
        }

        static void FillSlots(InventorySystem inv, List<SlotData> data, ItemDatabase db)
        {
            if (!inv) return;
            inv.EnsureSlots();
            for (int i = 0; i < inv.Slots.Length; i++) inv.Slots[i] = null;
            foreach (var s in data)
            {
                var it = db.Item(s.item); if (it == null || s.count <= 0 || s.slot < 0 || s.slot >= inv.Slots.Length) continue;
                inv.Slots[s.slot] = new ItemStack(it, Mathf.Min(s.count, it.maxStack)) { durability = s.durability, water = Mathf.Clamp(s.water, 0, it.waterCharges), dirty = s.dirty && s.water > 0 };
            }
            inv.ForceNotify();
        }

        public static SaveData Capture(GameManager gm)
        {
            var d = new SaveData { savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"), playSeconds = gm.PlaySeconds };
            var tm = TimeManager.Instance; if (tm) { d.day = tm.day; d.hour = tm.hour; }
            d.clock = GameClock.Now;
            var wm = WeatherManager.Instance; if (wm) { d.weather = wm.State.ToString(); d.weatherIntensity = wm.Intensity; }
            var p = gm.Player;
            if (p)
            {
                d.playerPos = p.transform.position; d.playerYaw = p.transform.eulerAngles.y;
                var hp = p.GetComponent<PlayerHealth>(); d.health = hp.Health;
                var sv = p.GetComponent<PlayerSurvival>(); d.hunger = sv.Hunger; d.thirst = sv.Thirst; d.stamina = sv.Stamina; d.bodyTemp = sv.BodyTemperature; d.wetness = sv.Wetness;
                var inv = p.GetComponent<InventorySystem>(); d.inventory = Slots(inv); d.activeSlot = inv.ActiveSlot;
                var cr = p.GetComponent<CraftingSystem>(); d.knownRecipes = cr.KnownIds.ToList();
                foreach (var (id, progress) in cr.GetQueueForSave()) d.craftQueue.Add(new CraftJobData { recipe = id, progress = progress });
            }
            d.hasRespawn = gm.HasRespawn; d.respawnPos = gm.RespawnPoint;
            d.introDone = gm.IntroDone;
            var tut = TutorialManager.Instance; if (tut) { d.tutorialStep = Mathf.Max(0, tut.Index); d.tutorialDone = tut.Completed; }
            var j = JournalSystem.Instance; if (j) d.journal = j.UnlockedInOrder.ToList();
            var z = ZoneManager.Instance; if (z) d.zonesVisited = z.Visited.ToList();
            foreach (var it in Interactable.Active.ToArray())
            {
                switch (it)
                {
                    case ResourceNode n when n.IsEmpty || n.Remaining < n.charges: d.nodes.Add(new NodeData { id = n.SaveId, remaining = n.Remaining, emptyUntil = n.EmptyUntil }); break;
                    case LootContainer l when l.Opened: d.openedLoot.Add(l.SaveId); break;
                    case Examinable e when e.Examined: d.examined.Add(e.SaveId); break;
                }
            }
            // hidden (empty) nodes are not in Interactable.Active? They stay enabled - renderers only are hidden.
            d.takenPickups = _taken.ToList();
            var th = UnityEngine.Object.FindFirstObjectByType<TreeHarvest>();
            if (th) foreach (var kv in th.Felled) d.felledTrees.Add(new TreeData { index = kv.Key, regrowAt = kv.Value });
            foreach (var s in PlacedStructure.All)
            {
                if (!s) continue;
                var sd = new StructureData { item = s.itemId, uid = s.uid, pos = s.transform.position, yaw = s.transform.eulerAngles.y };
                var cf = s.GetComponent<Campfire>(); if (cf) { sd.lit = cf.IsLit; sd.fuel = cf.Fuel; }
                var sb = s.GetComponent<StorageBox>(); if (sb) sd.contents = Slots(sb.Inventory);
                d.structures.Add(sd);
            }
            foreach (var pk in WorldPickup.Dropped)
            {
                if (!pk || !pk.item) continue;
                var st = pk.uniqueStack;
                d.dropped.Add(new DropData { item = pk.item.id, count = st != null ? st.count : pk.count, durability = st != null ? st.durability : pk.item.maxDurability, water = st != null ? st.water : 0, dirty = st != null && st.dirty, pos = pk.transform.position });
            }
            return d;
        }

        /// <summary>apply onto a freshly reset world (GameManager.ResetWorld first)</summary>
        public static void Apply(GameManager gm, SaveData d)
        {
            var db = ItemDatabase.Instance;
            // the reset before a load refunds the old crafting queue; whatever did not fit was spilled on the ground: not part of the save
            WorldPickup.ClearDropped();
            var tm = TimeManager.Instance; if (tm) tm.Set(Mathf.Max(1, d.day), d.hour);
            GameClock.Now = d.clock;
            var wm = WeatherManager.Instance;
            if (wm && Enum.TryParse<WeatherState>(d.weather, out var ws)) wm.SetWeather(ws == WeatherState.Storm ? WeatherState.Rain : ws, 2f, true);
            gm.PlaySeconds = d.playSeconds;
            var p = gm.Player;
            if (p)
            {
                p.GetComponent<PlayerMotor>().Warp(d.playerPos + Vector3.up * 0.05f, Quaternion.Euler(0, d.playerYaw, 0));
                var hp = p.GetComponent<PlayerHealth>(); hp.SetHealth(Mathf.Max(1f, d.health));
                p.GetComponent<PlayerSurvival>().SetStats(d.hunger, d.thirst, d.stamina, d.bodyTemp > 1f ? d.bodyTemp : 37f, d.wetness);
                var inv = p.GetComponent<InventorySystem>(); FillSlots(inv, d.inventory, db); inv.SetActiveSlot(d.activeSlot);
                var cr = p.GetComponent<CraftingSystem>(); if (d.knownRecipes != null && d.knownRecipes.Count > 0) cr.SetKnown(d.knownRecipes);
                if (db != null) foreach (var r in db.recipes) if (r && r.knownAtStart) cr.Learn(r, false);   // recipes added after the save was made
                // the queued jobs were paid when queued: restore them as they were (replaces any queue from before the load, no refund)
                cr.RestoreQueue(d.craftQueue != null ? d.craftQueue.Where(q => q != null).Select(q => (q.recipe, q.progress)) : null, db);
            }
            gm.SetRespawn(d.hasRespawn, d.respawnPos);
            gm.IntroDone = d.introDone;
            var tut = TutorialManager.Instance; if (tut) tut.Restore(d.tutorialStep, d.tutorialDone);
            var j = JournalSystem.Instance; if (j) j.SetUnlocked(d.journal);
            var z = ZoneManager.Instance; if (z) z.SetVisited(d.zonesVisited);
            var nodes = d.nodes.ToDictionary(n => n.id, n => n);
            var opened = new HashSet<string>(d.openedLoot); var examined = new HashSet<string>(d.examined); var taken = new HashSet<string>(d.takenPickups);
            _taken.Clear(); foreach (var t in taken) _taken.Add(t);
            foreach (var it in Interactable.Active.ToArray())
            {
                switch (it)
                {
                    case ResourceNode n when nodes.TryGetValue(n.SaveId, out var nd): n.Restore(nd.remaining, nd.emptyUntil); break;
                    case LootContainer l: l.Restore(opened.Contains(l.SaveId)); break;
                    case Examinable e: e.Restore(examined.Contains(e.SaveId)); break;
                    case WorldPickup w when taken.Contains(w.SaveId): w.gameObject.SetActive(false); break;
                }
            }
            var th = UnityEngine.Object.FindFirstObjectByType<TreeHarvest>();
            if (th) th.Restore(d.felledTrees.ToDictionary(t => t.index, t => t.regrowAt));
            foreach (var s in d.structures)
            {
                var item = db.Item(s.item); if (item == null || !item.IsPlaceable) continue;
                var go = BuildSystem.Spawn(item, s.pos, Quaternion.Euler(0, s.yaw, 0), s.uid);
                var cf = go.GetComponent<Campfire>(); if (cf) cf.Restore(s.lit, s.fuel);
                var sb = go.GetComponent<StorageBox>(); if (sb) FillSlots(sb.Inventory, s.contents, db);
            }
            foreach (var dr in d.dropped)
            {
                var item = db.Item(dr.item); if (item == null) continue;
                var st = new ItemStack(item, dr.count) { durability = dr.durability, water = dr.water, dirty = dr.dirty };
                WorldPickup.DropStack(st, dr.pos + Vector3.up * 0.3f);
            }
            GameEvents.Raise(GameEventType.GameLoaded, "slot");
        }
    }
}
