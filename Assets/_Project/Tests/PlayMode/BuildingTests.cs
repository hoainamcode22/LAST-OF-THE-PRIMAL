using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using PrimalFrontier.Building;
using PrimalFrontier.Core;
using PrimalFrontier.Items;
using PrimalFrontier.Player;
using PrimalFrontier.World;
using Object = UnityEngine.Object;

namespace PrimalFrontier.Tests
{
    /// <summary>
    /// Building pieces (BUILD agent, PC phase): sockets and snapping, the enclosure rule (roof + 2 walls = shelter cover
    /// and warmth, a campfire inside is sheltered), the door (open / close, saved state), and on the island the real
    /// assets: save / load restores every piece, the door and the enclosure, and the build menu lists known pieces with
    /// have / need. The plain tests run on run-time definitions and stand-in meshes (no builder assets needed); the
    /// island tests skip when PrimalBuildingBuilder.Build has not run.
    /// </summary>
    public class BuildingTests
    {
        static ItemDatabase Db => ItemDatabase.Instance;
        readonly List<Object> _made = new List<Object>();
        readonly Dictionary<StructureCategory, StructureDefinition> _defs = new Dictionary<StructureCategory, StructureDefinition>();

        [UnitySetUp] public IEnumerator ClearScene() { yield return TestScenes.ClearIfGameplayLeft(); }

        [TearDown] public void TearDown()
        {
            foreach (var d in _defs.Values) StructureDefinition.Unregister(d);
            _defs.Clear();
            foreach (var o in _made) if (o) Object.Destroy(o);
            _made.Clear();
            BuildSystem.MenuKeyPressed = null;
            GameManager.ForceShowTitle = null; GameManager.ForcePlayIntro = null; PlayerInputReader.Simulate = false; Time.timeScale = 1f;
        }

        [UnityTearDown] public IEnumerator Unload() { yield return TestScenes.ClearIfGameplayLeft(); }

        // ------------------------------------------------------------------ helpers
        T Keep<T>(T o) where T : Object { _made.Add(o); return o; }

        /// <summary>a run-time definition with a stand-in prefab (kept inactive as a template) and an item that places it</summary>
        StructureDefinition Def(StructureCategory cat)
        {
            if (_defs.TryGetValue(cat, out var d)) return d;
            d = Keep(ScriptableObject.CreateInstance<StructureDefinition>());
            d.id = "t_" + cat.ToString().ToLower(); d.displayName = cat.ToString(); d.ApplyStandard(cat);
            var template = Keep(PieceMeshes.Build(cat, "T_" + cat, null));
            template.AddComponent<StructurePiece>().definition = d;
            template.AddComponent<PlacedStructure>();
            switch (cat)
            {
                case StructureCategory.Foundation: Col(template, new Vector3(0, 0.17f, 0), new Vector3(3, 0.35f, 3)); break;
                case StructureCategory.Wall: Col(template, new Vector3(0, 1.2f, 0), new Vector3(3, 2.4f, 0.22f)); break;
                case StructureCategory.Doorway:
                    {
                        Col(template, new Vector3(-1.0f, 1.2f, 0), new Vector3(0.95f, 2.4f, 0.22f)); Col(template, new Vector3(1.0f, 1.2f, 0), new Vector3(0.95f, 2.4f, 0.22f));
                        var hinge = new GameObject("Door"); hinge.transform.SetParent(template.transform, false); hinge.transform.localPosition = new Vector3(-0.5f, 0, 0);
                        var leaf = new GameObject("Door_LOD0"); leaf.transform.SetParent(hinge.transform, false);
                        leaf.AddComponent<MeshFilter>().sharedMesh = PieceMeshes.Door(0); leaf.AddComponent<MeshRenderer>();
                        hinge.AddComponent<DoorPiece>(); Col(hinge, new Vector3(0.45f, 0.95f, 0), new Vector3(0.9f, 1.9f, 0.08f));
                        break;
                    }
                case StructureCategory.Roof:
                    {
                        Col(template, new Vector3(0, 0.55f, 0), new Vector3(3.3f, 1.3f, 3.4f));
                        var enc = new GameObject(BuildingEnclosure.ChildName); enc.transform.SetParent(template.transform, false); enc.transform.localPosition = new Vector3(0, -2.4f, 0);
                        var sh = enc.AddComponent<Shelter>(); sh.enabled = false; sh.bedInsideCounts = true;
                        break;
                    }
                default: template.AddComponent<Shelter>().coverRadius = 1.7f; break;
            }
            template.SetActive(false);
            d.prefab = template;
            var it = Keep(ScriptableObject.CreateInstance<ItemDefinition>());
            it.id = d.id; it.name = d.id; it.displayName = d.displayName; it.category = ItemCategory.Structure; it.placePrefab = template;
            d.item = it;
            StructureDefinition.Register(d);
            _defs[cat] = d;
            return d;
        }

        static void Col(GameObject go, Vector3 c, Vector3 s) { var b = go.AddComponent<BoxCollider>(); b.center = c; b.size = s; }
        static void Near(Vector3 expected, Vector3 actual, string what) => Assert.Less(Vector3.Distance(expected, actual), 0.02f, $"{what}: expected {expected}, got {actual}");

        GameObject Place(StructureCategory cat, Vector3 pos, float yaw)
        {
            var go = BuildSystem.Spawn(Def(cat), pos, Quaternion.Euler(0, yaw, 0), null);
            go.SetActive(true);
            return Keep(go);
        }

        /// <summary>foundation at origin o, walls on its +Z and +X edges, a roof over the cell</summary>
        (GameObject foundation, GameObject wallN, GameObject wallE, GameObject roof) Hut(Vector3 o, bool door = false)
        {
            var f = Place(StructureCategory.Foundation, o, 0f);
            var fd = Def(StructureCategory.Foundation);
            var wn = Place(door ? StructureCategory.Doorway : StructureCategory.Wall, fd.SocketPosition(f.transform, 0), fd.SocketYaw(f.transform, 0));
            var we = Place(StructureCategory.Wall, fd.SocketPosition(f.transform, 2), fd.SocketYaw(f.transform, 2));
            var wd = Def(StructureCategory.Wall);
            var roofPos = wd.SocketPosition(we.transform, 0);                 // WallTop socket toward the inside of the cell
            var roof = Place(StructureCategory.Roof, roofPos, 0f);
            return (f, wn, we, roof);
        }

        // ------------------------------------------------------------------ sockets + snapping
        [Test] public void Foundation_Sockets_Put_Walls_On_Its_Edges_Facing_Out_And_Wall_Tops_Centre_A_Roof()
        {
            var f = Place(StructureCategory.Foundation, new Vector3(100, 0, 100), 0f);
            var fd = Def(StructureCategory.Foundation);
            var north = fd.SocketPosition(f.transform, 0); var east = fd.SocketPosition(f.transform, 2);
            Near(new Vector3(100, StructureDefinition.FoundationTop, 101.5f), north, "north edge socket");
            Assert.AreEqual(0f, fd.SocketYaw(f.transform, 0), 0.01f, "north wall faces +Z");
            Near(new Vector3(101.5f, StructureDefinition.FoundationTop, 100f), east, "east edge socket");
            Assert.AreEqual(90f, fd.SocketYaw(f.transform, 2), 0.01f, "east wall faces +X");
            var wall = Place(StructureCategory.Wall, east, 90f);
            var wd = Def(StructureCategory.Wall);
            var top = wd.SocketPosition(wall.transform, 0);
            Assert.AreEqual(100f, top.x, 0.01f); Assert.AreEqual(100f, top.z, 0.01f); Assert.AreEqual(StructureDefinition.FoundationTop + StructureDefinition.WallHeight, top.y, 0.01f, "roof socket sits over the cell centre at wall-top height");
            // a second foundation snaps level next to the first
            var side = fd.SocketPosition(f.transform, 4);
            Near(new Vector3(100, 0, 103), side, "side socket: the next foundation, level with this one");
        }

        [Test] public void Aim_Snaps_A_Wall_To_The_Nearest_Free_Foundation_Edge()
        {
            var o = new Vector3(200, 0, 200);
            var f = Place(StructureCategory.Foundation, o, 0f);
            var wd = Def(StructureCategory.Wall);
            var ray = new Ray(o + new Vector3(0, 3f, 4f), (o + new Vector3(0, 0.35f, 1.5f) - (o + new Vector3(0, 3f, 4f))).normalized);
            Assert.IsTrue(BuildSystem.FindSnap(wd, ray, o + new Vector3(0, 0.35f, 1.5f), o + new Vector3(0, 0, 5f), 12f, null, out var pos, out var yaw, out var host, out var kind), "snaps");
            Assert.AreEqual(SocketKind.FoundationEdge, kind); Assert.AreEqual(f.GetComponent<StructurePiece>(), host);
            Near(o + new Vector3(0, 0.35f, 1.5f), pos, "wall origin on the edge"); Assert.AreEqual(0f, yaw, 0.01f);
            // that edge taken: the same aim goes to the next free socket, never onto the standing wall
            Place(StructureCategory.Wall, pos, yaw);
            bool again = BuildSystem.FindSnap(wd, ray, pos, o + new Vector3(0, 0, 5f), 12f, null, out var pos2, out _, out _, out _);
            Assert.IsFalse(again && Vector3.Distance(pos, pos2) < 0.5f, "never onto the occupied edge");
            // aiming at the east edge from the same spot snaps there
            var east = o + new Vector3(1.5f, 0.35f, 0f);
            var rayE = new Ray(o + new Vector3(0, 3f, 4f), (east - (o + new Vector3(0, 3f, 4f))).normalized);
            Assert.IsTrue(BuildSystem.FindSnap(wd, rayE, east, o + new Vector3(0, 0, 5f), 12f, null, out var posE, out var yawE, out _, out _), "east edge snaps");
            Near(east, posE, "east edge"); Assert.AreEqual(90f, Mathf.DeltaAngle(0f, yawE) < 0 ? yawE + 360f : yawE, 0.01f, "faces +X");
            // far away: nothing
            Assert.IsFalse(BuildSystem.FindSnap(wd, new Ray(o + new Vector3(30, 2, 30), Vector3.down), o + new Vector3(30, 0, 30), o + new Vector3(30, 0, 30), 12f, null, out _, out _, out _, out _), "no socket near a far aim");
            // the roof snaps only to wall tops: aiming at the standing wall's top puts it over the cell centre at wall-top height
            var rd = Def(StructureCategory.Roof);
            var top = o + new Vector3(0, 0.35f + 2.4f, 1.5f);
            var rayTop = new Ray(o + new Vector3(0, 4f, 6f), (top - (o + new Vector3(0, 4f, 6f))).normalized);
            Assert.IsTrue(BuildSystem.FindSnap(rd, rayTop, top, o + new Vector3(0, 0, 5f), 12f, null, out var rpos, out _, out _, out var rkind), "roof snaps");
            Assert.AreEqual(SocketKind.WallTop, rkind);
            Near(o + new Vector3(0, 0.35f + 2.4f, 0f), rpos, "roof centred over the cell");
            Assert.IsFalse(BuildSystem.FindSnap(Def(StructureCategory.Foundation), rayTop, top, o + new Vector3(0, 0, 5f), 12f, null, out _, out _, out _, out var fk) && fk != SocketKind.FoundationSide, "a foundation never snaps to walls");
        }

        // ------------------------------------------------------------------ enclosure
        [UnityTest, Timeout(60000)] public IEnumerator Roof_With_Two_Walls_Covers_The_Cell_Warms_It_And_Shelters_A_Fire()
        {
            var o = new Vector3(300, 0, 300);
            var (f, wn, we, roof) = Hut(o);
            BuildingEnclosure.Refresh();
            var inside = o + new Vector3(0, 1.2f, 0);
            Assert.IsTrue(BuildingEnclosure.Inside(inside), "the cell is an enclosure");
            Assert.IsTrue(Shelter.Covers(inside), "covered inside");
            Assert.AreEqual(BuildingEnclosure.WarmthFor(2), Shelter.WarmthAt(inside), 0.01f, "two-wall warmth");
            Assert.IsFalse(Shelter.Covers(o + new Vector3(0, 1.2f, -4f)), "not covered outside the cell");
            Assert.IsFalse(Shelter.Covers(o + new Vector3(0, 6f, 0)), "not covered above the roof");
            var sh = roof.GetComponentInChildren<Shelter>();
            Assert.IsNotNull(sh); Assert.IsTrue(sh.enabled, "enclosure shelter enabled"); Assert.IsTrue(sh.IsEnclosure);
            Assert.IsNotNull(Shelter.Nearest(o, 3f), "Shelter.Nearest finds the enclosure (missions, respawn)");
            // a third wall makes it warmer, a bedroll inside is its bed
            var fd = Def(StructureCategory.Foundation);
            Place(StructureCategory.Wall, fd.SocketPosition(f.transform, 1), fd.SocketYaw(f.transform, 1));
            BuildingEnclosure.Refresh();
            Assert.AreEqual(BuildingEnclosure.WarmthFor(3), Shelter.WarmthAt(inside), 0.01f, "three-wall warmth");
            Assert.IsFalse(sh.HasBed, "no bed yet");
            var bed = Keep(new GameObject("TestBed")); bed.transform.position = o + new Vector3(0.5f, 0.35f, -0.5f); bed.AddComponent<Bedroll>();
            Assert.IsTrue(sh.HasBed, "a bedroll inside counts as the hut's bed");
            Assert.IsNotNull(Shelter.Nearest(o, 3f, true), "Nearest with a bed");
            // a lit campfire inside reads Shelter.Covers: sheltered from rain
            if (Db != null && Db.Item("campfire") && Db.Item("campfire").placePrefab && Db.Item("wood"))
            {
                var fire = Keep(BuildSystem.Spawn(Db.Item("campfire"), o + new Vector3(-0.6f, 0.35f, 0.4f), Quaternion.identity, null)).GetComponent<Campfire>();
                var pack = Keep(new GameObject("Pack")).AddComponent<InventorySystem>(); pack.raiseGameEvents = false; pack.maxWeight = 0f; pack.EnsureSlots();
                pack.Add(Db.Item("wood"), 1);
                Assert.IsTrue(fire.TryLight(pack), "fire lit");
                float t = 0f; while (!fire.Sheltered && t < 3f) { t += Time.deltaTime; yield return null; }
                Assert.IsTrue(fire.Sheltered, "Campfire.Sheltered inside the hut");
            }
            // the roof alone (walls gone) is no shelter
            Object.DestroyImmediate(wn); Object.DestroyImmediate(we);
            BuildingEnclosure.Refresh();
            Assert.IsFalse(Shelter.Covers(inside), "one wall left: no enclosure");
            yield return null;
        }

        [Test] public void Wall_Count_Sees_Doorways_And_Reports_The_Door_Edge()
        {
            var o = new Vector3(400, 0, 400);
            var (f, wn, we, roof) = Hut(o, door: true);
            int n = BuildingEnclosure.CountWalls(roof.transform.position, roof.transform.rotation, out bool hasDoor, out int open);
            Assert.AreEqual(2, n); Assert.IsTrue(hasDoor, "doorway counts as a wall"); Assert.AreEqual(2, open, "-Z edge open first");
            Assert.AreEqual(0, BuildingEnclosure.DoorEdge(roof.transform.position, roof.transform.rotation), "door on the +Z edge");
            BuildingEnclosure.Refresh();
            var sh = roof.GetComponentInChildren<Shelter>();
            Assert.IsTrue(sh.enabled);
            Assert.AreEqual(0f, Vector3.Angle(sh.transform.forward, Vector3.forward), 0.5f, "the enclosure faces its door: rest point toward the doorway");
            Assert.IsTrue(sh.RestPoint.z > o.z, "rest point on the door side");
        }

        // ------------------------------------------------------------------ door
        [Test] public void Door_Toggles_And_Its_State_Round_Trips_Through_The_Piece_Save()
        {
            var o = new Vector3(500, 0, 500);
            var wall = Place(StructureCategory.Doorway, o, 0f);
            var piece = wall.GetComponent<StructurePiece>(); var door = piece.Door;
            Assert.IsNotNull(door, "doorway has a door");
            Assert.IsFalse(door.IsOpen);
            Assert.IsFalse((piece.CaptureState() ?? "").Contains("true"), "closed door saves closed");
            door.Toggle(); Assert.IsTrue(door.IsOpen, "opened");
            string state = piece.CaptureState(); Assert.IsTrue(state.Contains("true"), "open door saved: " + state);
            door.SetOpen(true, true);
            Assert.AreEqual(door.openAngle, Quaternion.Angle(Quaternion.identity, door.transform.localRotation), 0.5f, "leaf swung by openAngle");
            var wall2 = Place(StructureCategory.Doorway, o + new Vector3(0, 0, 6), 0f);
            var piece2 = wall2.GetComponent<StructurePiece>();
            piece2.RestoreState(state);
            Assert.IsTrue(piece2.Door.IsOpen, "restored open"); Assert.IsFalse(piece2.Door.Moving, "restored instantly");
            piece2.RestoreState("{\"door\":false}");
            Assert.IsFalse(piece2.Door.IsOpen);
            Assert.AreEqual("Open door", piece2.Door.GetPrompt(null, out _));
        }

        // ------------------------------------------------------------------ island: real assets, save / load, menu
        GameManager _gm;
        IEnumerator LoadIsland()
        {
            TestScenes.UseTestSaves(); GameManager.ForceShowTitle = false; GameManager.ForcePlayIntro = false;
            PlayerInputReader.Simulate = true;
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Project/Scenes/Island_VerticalSlice.unity", new LoadSceneParameters(LoadSceneMode.Single));
#endif
            float t = 0; while ((_gm = GameManager.Instance) == null || _gm.State != GameState.Playing) { t += Time.unscaledDeltaTime; if (t > 20f) break; yield return null; }
            Assert.IsNotNull(_gm, "GameManager"); Assert.AreEqual(GameState.Playing, _gm.State, "game started");
            yield return new WaitForSeconds(1f);
        }

        static bool BuilderRan() => Db != null && new[] { "foundation", "wall", "doorway", "roof", "leaf_shelter" }.All(id => Db.Item(id) && Db.Item(id).placePrefab) && StructureDefinition.Find("wall");

        [UnityTest, Timeout(180000)] public IEnumerator Island_Save_Load_Restores_Pieces_Door_And_Enclosure_And_The_Menu_Shows_Have_Need()
        {
            yield return LoadIsland();
            if (!BuilderRan()) Assert.Ignore("run PrimalBuildingBuilder.Build first (items / prefabs / definitions)");
            StructureDefinition.Reload();
            var p = _gm.Player; var inv = p.GetComponent<InventorySystem>();
            // level spot in front of the player, a bit above the ground so the platform sits on it
            var o = p.transform.position + p.transform.forward * 5f;
            if (Physics.Raycast(o + Vector3.up * 5f, Vector3.down, out var hit, 20f, ~LayerMask.GetMask("Player"), QueryTriggerInteraction.Ignore)) o = hit.point;
            var fd = StructureDefinition.Find("foundation"); var wd = StructureDefinition.Find("wall"); var dd = StructureDefinition.Find("doorway"); var rd = StructureDefinition.Find("roof");
            var f = BuildSystem.Spawn(fd, o, Quaternion.identity, null);
            var wn = BuildSystem.Spawn(dd, fd.SocketPosition(f.transform, 0), Quaternion.Euler(0, fd.SocketYaw(f.transform, 0), 0), null);
            var we = BuildSystem.Spawn(wd, fd.SocketPosition(f.transform, 2), Quaternion.Euler(0, fd.SocketYaw(f.transform, 2), 0), null);
            var roof = BuildSystem.Spawn(rd, wd.SocketPosition(we.transform, 0), Quaternion.identity, null);
            Assert.IsNotNull(roof.GetComponent<StructurePiece>(), "prefab has StructurePiece");
            Assert.IsNotNull(wn.GetComponentInChildren<DoorPiece>(), "doorway prefab has a door");
            wn.GetComponentInChildren<DoorPiece>().SetOpen(true, true);
            BuildingEnclosure.Refresh();
            var inside = o + new Vector3(0, 1.2f, 0);
            Assert.IsTrue(Shelter.Covers(inside), "hut covers its cell before the save");
            yield return null;
            Assert.IsTrue(_gm.SaveGame(), "save: " + SaveSystem.LastError);
            _gm.LoadGame(); yield return new WaitForSeconds(0.6f);
            var pieces = PlacedStructure.All.Where(s => s && s.GetComponent<StructurePiece>()).ToList();
            CollectionAssert.AreEquivalent(new[] { "foundation", "doorway", "wall", "roof" }, pieces.Select(s => s.itemId).ToList(), "every piece restored by item id");
            var door2 = pieces.Select(s => s.GetComponentInChildren<DoorPiece>()).FirstOrDefault(d => d);
            Assert.IsNotNull(door2, "door restored"); Assert.IsTrue(door2.IsOpen, "door state restored (open)");
            var roof2 = pieces.First(s => s.itemId == "roof");
            Assert.AreEqual(roof.transform.position.y, roof2.transform.position.y, 0.01f, "roof height restored");
            BuildingEnclosure.RefreshIfDirty();
            Assert.IsTrue(Shelter.Covers(inside), "enclosure restored after the load");
            Assert.IsTrue(roof2.GetComponentInChildren<Shelter>().enabled, "restored roof's shelter enabled");
            // build menu: known pieces with have / need, selecting starts a ghost, cost check
            var bs = BuildSystem.Instance; Assert.IsNotNull(bs, "BuildSystem");
            bs.OpenMenu();
            Assert.IsTrue(bs.MenuOpen && bs.Active, "menu open"); Assert.IsTrue(BuildMenuUI.Instance && BuildMenuUI.Instance.Visible, "menu visible");
            var shown = BuildMenuUI.Instance.Shown;
            Assert.IsTrue(shown.Any(d => d.id == "leaf_shelter"), "leaf shelter known at start");
            var leaf = StructureDefinition.Find("leaf_shelter");
            Assert.IsNotNull(bs.MissingCost(leaf), "empty pack: something missing");
            foreach (var ing in leaf.Cost) inv.Add(ing.item, ing.count, true);
            Assert.IsNull(bs.MissingCost(leaf), "pack holds the cost");
            bs.Select(leaf);
            Assert.IsFalse(bs.MenuOpen, "menu closed on pick"); Assert.IsTrue(bs.Placing && bs.Structure == leaf, "ghost of the picked piece");
            yield return null;
            Assert.IsTrue(bs.Active);
            bs.Cancel();
            Assert.IsFalse(bs.Active);
            SaveSystem.Delete();
        }

        [Test] public void Building_Content_Is_Built()
        {
            if (!BuilderRan()) Assert.Ignore("run PrimalBuildingBuilder.Build first");
            Assert.That(Db.recipes.Count, Is.InRange(25, 40), "25-40 recipes with the building pieces");
            foreach (var id in new[] { "foundation", "wall", "doorway", "roof", "leaf_shelter" })
            {
                var it = Db.Item(id); Assert.IsNotNull(it.icon, "icon " + id);
                Assert.IsNotNull(Db.Recipe(id), "recipe " + id);
                var d = StructureDefinition.Find(id); Assert.IsNotNull(d, "definition " + id); Assert.AreEqual(it, d.item); Assert.IsNotNull(d.recipe, "definition recipe " + id);
                Assert.IsNotNull(it.placePrefab.GetComponent<StructurePiece>(), "StructurePiece on " + id);
                Assert.Greater(it.placePrefab.GetComponentsInChildren<Collider>(true).Length, 0, "colliders on " + id);
                Assert.Greater(it.placePrefab.GetComponentsInChildren<LODGroup>(true).Length, 0, "LODGroup on " + id);
            }
            Assert.IsTrue(Db.Recipe("leaf_shelter").knownAtStart, "leaf shelter known at start");
            Assert.IsNotNull(Db.Item("roof").placePrefab.GetComponentInChildren<Shelter>(true), "roof carries the enclosure shelter");
            Assert.IsNotNull(Db.Item("doorway").placePrefab.GetComponentInChildren<DoorPiece>(true), "doorway carries a door");
            Assert.IsNotNull(Db.Item("leaf_shelter").placePrefab.GetComponent<Shelter>(), "leaf shelter is a shelter");
        }
    }
}
