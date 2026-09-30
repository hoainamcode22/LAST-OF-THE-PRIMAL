using System;
using System.Collections.Generic;
using UnityEngine;
using PrimalFrontier.Items;

namespace PrimalFrontier.Building
{
    /// <summary>what a piece is for; the enclosure logic (BuildingEnclosure) counts Walls and Doorways under Roofs</summary>
    public enum StructureCategory { Shelter = 0, Foundation = 1, Wall = 2, Doorway = 3, Roof = 4 }

    /// <summary>snap points on a placed piece. A piece lists the kinds it snaps to (StructureDefinition.snapsTo)</summary>
    [Flags]
    public enum SocketKind
    {
        None = 0,
        FoundationEdge = 1,     // top edge of a foundation: a wall / doorway stands here, its outside facing away from the platform
        FoundationSide = 2,     // next to a foundation: another foundation, level with it
        WallTop = 4,            // over a wall: a roof centred on the cell on that side of the wall
        WallEnd = 8,            // end of a wall on the ground: another wall in line or around the corner
    }

    /// <summary>where a piece needs support</summary>
    public enum SupportRule
    {
        Ground = 0,             // stands on the ground (foundation, lean-to)
        GroundOrFoundation = 1, // ground, or a foundation edge / wall end socket (walls)
        WallsBelow = 2,         // only on wall tops, and minWallsBelow walls around its cell (roof)
    }

    [Serializable]
    public struct StructureSocket
    {
        public SocketKind kind;
        [Tooltip("in the piece's local space: where the snapped piece's origin goes")] public Vector3 localPosition;
        [Tooltip("yaw the snapped piece gets, relative to this piece (deg)")] public float localYaw;
        public StructureSocket(SocketKind kind, Vector3 pos, float yaw) { this.kind = kind; localPosition = pos; localYaw = yaw; }
    }

    /// <summary>
    /// One buildable piece of the primitive shelter set (directive 14-15, 27-29): its prefab, the sockets other pieces snap
    /// to, what it needs under it, its cost (the recipe's ingredients) and category. Assets live in Resources/Structures
    /// (STR_&lt;id&gt;, made by PrimalBuildingBuilder); the grid is 3 m cells, walls 2.4 m high, foundations 0.35 m thick.
    /// </summary>
    [CreateAssetMenu(menuName = "Primal Frontier/Structure Definition", fileName = "STR_New")]
    public class StructureDefinition : ScriptableObject
    {
        public const float Cell = 3f, WallHeight = 2.4f, FoundationTop = 0.35f, WallThickness = 0.22f;

        public string id = "piece";
        public string displayName = "Piece";
        [TextArea(1, 3)] public string description;
        public StructureCategory category;
        [Tooltip("the placed piece (StructurePiece on the root)")] public GameObject prefab;
        [Tooltip("the item that stands for this piece (icon, name; placePrefab = prefab, so the save file restores it by item id)")] public ItemDefinition item;
        [Tooltip("cost and knowledge: the piece is offered once this recipe is known; its ingredients are taken when the piece is built")] public RecipeDefinition recipe;
        [Tooltip("cost when there is no recipe")] public Ingredient[] cost = Array.Empty<Ingredient>();
        public StructureSocket[] sockets = Array.Empty<StructureSocket>();
        [Tooltip("socket kinds this piece snaps to")] public SocketKind snapsTo;
        public SupportRule support;
        [Tooltip("WallsBelow: how many of the cell's four edges need a wall")] [Range(1, 4)] public int minWallsBelow = 2;
        [Tooltip("may be placed on open ground without a socket")] public bool allowFree = true;
        [Tooltip("aim distance to a socket that snaps (m)")] public float snapRadius = 1.6f;
        [Tooltip("rotation step while snapped (0 = the socket decides)")] public float snapYawStep = 90f;
        [Tooltip("overlap box in local space (centre / size); shrunk a little so touching pieces do not count")] public Vector3 footprintCenter = new Vector3(0, 1f, 0), footprintSize = new Vector3(3f, 2f, 3f);
        public float maxSlope = 24f;
        [Tooltip("free placement: how far the ground under the corners may drop below the origin (m)")] public float groundTolerance = 0.7f;
        [Tooltip("hits of the Build action before the piece appears")] [Range(1, 6)] public int buildHits = 2;
        [Tooltip("BuildDust size when the piece appears")] public float dustScale = 1f;
        [Tooltip("leaves / thatch rustle when built")] public bool leafy = true;
        [Tooltip("poles / logs knock when built")] public bool wooden = true;

        public string Name => !string.IsNullOrEmpty(displayName) ? displayName : item ? item.displayName : id;
        public IReadOnlyList<Ingredient> Cost => recipe && recipe.ingredients != null && recipe.ingredients.Length > 0 ? recipe.ingredients : cost;
        public string ItemId => item ? item.id : id;

        // ------------------------------------------------------------------ registry
        static List<StructureDefinition> _all;
        /// <summary>every definition in Resources/Structures plus the registered ones (tests), ordered by category then id</summary>
        public static IReadOnlyList<StructureDefinition> All
        {
            get
            {
                if (_all == null)
                {
                    _all = new List<StructureDefinition>();
                    foreach (var d in Resources.LoadAll<StructureDefinition>("Structures")) if (d && !_all.Contains(d)) _all.Add(d);
                    Sort();
                }
                _all.RemoveAll(d => !d);
                return _all;
            }
        }
        static void Sort() => _all.Sort((a, b) => a.category != b.category ? a.category.CompareTo(b.category) : string.CompareOrdinal(a.id, b.id));
        /// <summary>tests / builders: a definition made at run time</summary>
        public static void Register(StructureDefinition d) { if (!d) return; var all = (List<StructureDefinition>)All; if (!all.Contains(d)) { all.Add(d); Sort(); } }
        public static void Unregister(StructureDefinition d) { _all?.Remove(d); }
        /// <summary>forget the cache (after the builder made new assets)</summary>
        public static void Reload() { _all = null; }

        public static StructureDefinition Find(string id)
        {
            var all = All;
            for (int i = 0; i < all.Count; i++) if (all[i].id == id) return all[i];
            return null;
        }
        public static StructureDefinition ForItem(ItemDefinition item)
        {
            if (!item) return null;
            var all = All;
            for (int i = 0; i < all.Count; i++) if (all[i].item == item || all[i].ItemId == item.id) return all[i];
            return null;
        }

        /// <summary>world position / yaw of socket i on a placed piece</summary>
        public Vector3 SocketPosition(Transform piece, int i) => piece.TransformPoint(sockets[i].localPosition);
        public float SocketYaw(Transform piece, int i) => piece.eulerAngles.y + sockets[i].localYaw;

        // ------------------------------------------------------------------ defaults for the five pieces (builder + tests)
        /// <summary>fills the geometry data of a standard piece (sockets, footprint, support); names / prefab / item stay</summary>
        public void ApplyStandard(StructureCategory cat)
        {
            category = cat;
            float c = Cell * 0.5f, h = WallHeight;
            switch (cat)
            {
                case StructureCategory.Foundation:
                    sockets = new[]
                    {
                        new StructureSocket(SocketKind.FoundationEdge, new Vector3(0, FoundationTop, c), 0), new StructureSocket(SocketKind.FoundationEdge, new Vector3(0, FoundationTop, -c), 180),
                        new StructureSocket(SocketKind.FoundationEdge, new Vector3(c, FoundationTop, 0), 90), new StructureSocket(SocketKind.FoundationEdge, new Vector3(-c, FoundationTop, 0), -90),
                        new StructureSocket(SocketKind.FoundationSide, new Vector3(0, 0, Cell), 0), new StructureSocket(SocketKind.FoundationSide, new Vector3(0, 0, -Cell), 0),
                        new StructureSocket(SocketKind.FoundationSide, new Vector3(Cell, 0, 0), 0), new StructureSocket(SocketKind.FoundationSide, new Vector3(-Cell, 0, 0), 0),
                    };
                    snapsTo = SocketKind.FoundationSide; support = SupportRule.Ground; allowFree = true; snapYawStep = 0f; snapRadius = 1.8f;
                    footprintCenter = new Vector3(0, FoundationTop * 0.5f + 0.02f, 0); footprintSize = new Vector3(Cell, FoundationTop, Cell);
                    buildHits = 3; dustScale = 1.4f; leafy = false; wooden = true; groundTolerance = 0.7f; maxSlope = 24f;
                    break;
                case StructureCategory.Wall:
                case StructureCategory.Doorway:
                    sockets = new[]
                    {
                        new StructureSocket(SocketKind.WallTop, new Vector3(0, h, -c), 0), new StructureSocket(SocketKind.WallTop, new Vector3(0, h, c), 0),
                        new StructureSocket(SocketKind.WallEnd, new Vector3(Cell, 0, 0), 0), new StructureSocket(SocketKind.WallEnd, new Vector3(-Cell, 0, 0), 0),
                        new StructureSocket(SocketKind.WallEnd, new Vector3(c, 0, -c), 90), new StructureSocket(SocketKind.WallEnd, new Vector3(-c, 0, -c), -90),
                    };
                    snapsTo = SocketKind.FoundationEdge | SocketKind.WallEnd; support = SupportRule.GroundOrFoundation; allowFree = true; snapYawStep = 180f; snapRadius = 1.6f;
                    footprintCenter = new Vector3(0, h * 0.5f + 0.05f, 0); footprintSize = new Vector3(Cell - 0.45f, h - 0.1f, WallThickness);   // narrower than the piece: a wall around the corner touches this one
                    buildHits = 2; dustScale = 1f; leafy = true; wooden = true; groundTolerance = 0.8f; maxSlope = 30f;
                    break;
                case StructureCategory.Roof:
                    sockets = Array.Empty<StructureSocket>();
                    snapsTo = SocketKind.WallTop; support = SupportRule.WallsBelow; minWallsBelow = 2; allowFree = false; snapYawStep = 90f; snapRadius = 2.4f;
                    footprintCenter = new Vector3(0, 0.6f, 0); footprintSize = new Vector3(Cell - 0.4f, 1f, Cell - 0.4f);
                    buildHits = 3; dustScale = 1.5f; leafy = true; wooden = true; maxSlope = 90f;
                    break;
                default:      // lean-to / leaf shelter: stands alone
                    sockets = Array.Empty<StructureSocket>();
                    snapsTo = SocketKind.None; support = SupportRule.Ground; allowFree = true; snapYawStep = 0f;
                    footprintCenter = new Vector3(0, 0.9f, -0.1f); footprintSize = new Vector3(2.8f, 1.7f, 2.3f);
                    buildHits = 2; dustScale = 1.2f; leafy = true; wooden = true; groundTolerance = 0.6f; maxSlope = 24f;
                    break;
            }
        }
    }
}
