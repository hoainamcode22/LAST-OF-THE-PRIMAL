using UnityEngine;
using PrimalFrontier.World;

namespace PrimalFrontier.Building
{
    /// <summary>
    /// Turns built pieces into shelter: a roof with walls (or doorways) on at least two of its cell's four edges is an
    /// enclosure. The roof's "Enclosure" child (a Shelter at floor level, box cover of the cell) is switched on with a
    /// warmth that grows with the wall count, and faces the doorway or an open side (rest / respawn point). Shelter.Covers,
    /// SurvivalEnvironment.ShelteredAt (rain, sun, temperature), Campfire.Sheltered and the rest / sleep / respawn flow all
    /// read Shelter.All, so nothing else needs to know about pieces. Re-evaluated lazily whenever a piece appears or goes
    /// (placement, load, destruction): MarkDirty + RefreshIfDirty (BuildSystem.Update and Shelter's queries call it).
    /// </summary>
    public static class BuildingEnclosure
    {
        public const float EdgeTolerance = 0.5f;
        public const string ChildName = "Enclosure";
        static bool _dirty = true;
        public static bool Dirty => _dirty;
        public static void MarkDirty() => _dirty = true;

        static readonly Vector3[] EdgeOffsets =
        {
            new Vector3(0, -StructureDefinition.WallHeight, StructureDefinition.Cell * 0.5f),
            new Vector3(StructureDefinition.Cell * 0.5f, -StructureDefinition.WallHeight, 0),
            new Vector3(0, -StructureDefinition.WallHeight, -StructureDefinition.Cell * 0.5f),
            new Vector3(-StructureDefinition.Cell * 0.5f, -StructureDefinition.WallHeight, 0),
        };
        static readonly float[] EdgeYaw = { 0f, 90f, 180f, -90f };

        public static void RefreshIfDirty() { if (_dirty) Refresh(); }

        /// <summary>evaluate every roof now</summary>
        public static void Refresh()
        {
            _dirty = false;
            var all = StructurePiece.All;
            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                if (!p || p.Category != StructureCategory.Roof) continue;
                try { Evaluate(p); }
                catch (System.Exception e) { Debug.LogException(e, p); }
            }
        }

        /// <summary>walls around the cell under a roof origin (pos at wall-top height, rot = the roof's yaw)</summary>
        public static int CountWalls(Vector3 pos, Quaternion rot, out bool hasDoor, out int openEdge)
        {
            int n = 0; hasDoor = false; openEdge = -1;
            for (int e = 0; e < 4; e++)
            {
                var w = StructurePiece.At(pos + rot * EdgeOffsets[e], EdgeTolerance, true);
                if (w) { n++; if (w.Category == StructureCategory.Doorway) hasDoor = true; }
                else if (openEdge < 0) openEdge = e;
            }
            return n;
        }

        /// <summary>the edge (0 +Z, 1 +X, 2 -Z, 3 -X) holding a doorway, -1 = none</summary>
        public static int DoorEdge(Vector3 pos, Quaternion rot)
        {
            for (int e = 0; e < 4; e++)
            {
                var w = StructurePiece.At(pos + rot * EdgeOffsets[e], EdgeTolerance, true);
                if (w && w.Category == StructureCategory.Doorway) return e;
            }
            return -1;
        }

        /// <summary>warmth of an enclosure with n walls (2: open hut 4 C, 3: 5.5 C, 4 closed: 7 C; a tent gives 6)</summary>
        public static float WarmthFor(int walls) => walls >= 4 ? 7f : walls == 3 ? 5.5f : walls == 2 ? 4f : 0f;

        static void Evaluate(StructurePiece roof)
        {
            var t = roof.transform;
            int walls = CountWalls(t.position, t.rotation, out _, out int open);
            int door = DoorEdge(t.position, t.rotation);
            bool enclosed = walls >= 2;
            var child = t.Find(ChildName);
            if (!child)
            {
                if (!enclosed) return;
                var go = new GameObject(ChildName);
                go.transform.SetParent(t, false);
                child = go.transform;
            }
            child.localPosition = new Vector3(0, -StructureDefinition.WallHeight, 0);
            int face = door >= 0 ? door : open >= 0 ? open : 0;
            child.localRotation = Quaternion.Euler(0, EdgeYaw[face], 0);
            var sh = child.GetComponent<Shelter>();
            if (!sh) sh = child.gameObject.AddComponent<Shelter>();
            sh.coverBox = new Vector3(StructureDefinition.Cell * 0.5f + 0.35f, 0f, StructureDefinition.Cell * 0.5f + 0.35f);
            sh.coverHeight = StructureDefinition.WallHeight + 1.4f;
            sh.coverBelow = 0.6f;
            sh.restDistance = 1.0f;
            sh.bedInsideCounts = true;
            sh.warmth = WarmthFor(walls);
            sh.note = enclosed ? (walls >= 4 ? "A closed hut" : $"Sheltered by {walls} walls") : null;
            sh.enclosureWalls = walls;
            if (sh.enabled != enclosed) sh.enabled = enclosed;
        }

        /// <summary>is p inside a built enclosure (roof with 2+ walls)? Shelters that are not enclosures do not count</summary>
        public static bool Inside(Vector3 p)
        {
            RefreshIfDirty();
            var all = Shelter.All;
            for (int i = 0; i < all.Count; i++) { var s = all[i]; if (s && s.bedInsideCounts && s.isActiveAndEnabled && s.CoversPoint(p)) return true; }
            return false;
        }
    }
}
