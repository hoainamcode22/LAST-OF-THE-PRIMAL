using System.Collections.Generic;
using UnityEngine;

namespace PrimalFrontier.Building
{
    /// <summary>
    /// A placed building piece (on the prefab root, next to PlacedStructure). Knows its definition (sockets, category),
    /// registers itself for snapping and the enclosure logic, and saves its door through ISaveableStructure.
    /// </summary>
    public class StructurePiece : MonoBehaviour, ISaveableStructure
    {
        public StructureDefinition definition;
        public static readonly List<StructurePiece> All = new List<StructurePiece>();

        [System.Serializable] class State { public bool door; }

        public StructureCategory Category => definition ? definition.category : StructureCategory.Shelter;
        public bool IsWall => Category == StructureCategory.Wall || Category == StructureCategory.Doorway;
        public DoorPiece Door { get { if (!_doorChecked) { _door = GetComponentInChildren<DoorPiece>(true); _doorChecked = true; } return _door; } }
        DoorPiece _door; bool _doorChecked;

        void OnEnable()
        {
            if (!definition)
            {
                var ps = GetComponent<PlacedStructure>();
                if (ps && !string.IsNullOrEmpty(ps.itemId)) definition = StructureDefinition.Find(ps.itemId) ?? StructureDefinition.ForItem(Items.ItemDatabase.Instance ? Items.ItemDatabase.Instance.Item(ps.itemId) : null);
            }
            if (!All.Contains(this)) All.Add(this);
            BuildingEnclosure.MarkDirty();
        }

        void OnDisable() { All.Remove(this); BuildingEnclosure.MarkDirty(); }

        public int SocketCount => definition ? definition.sockets.Length : 0;
        public StructureSocket Socket(int i) => definition.sockets[i];
        public Vector3 SocketPosition(int i) => transform.TransformPoint(definition.sockets[i].localPosition);
        public float SocketYaw(int i) => transform.eulerAngles.y + definition.sockets[i].localYaw;

        /// <summary>the nearest piece of the given kinds whose origin is within r of p (walls under a roof edge, a foundation under a wall...)</summary>
        public static StructurePiece At(Vector3 p, float r, bool wallsOnly)
        {
            StructurePiece best = null; float bd = r * r;
            for (int i = 0; i < All.Count; i++)
            {
                var s = All[i]; if (!s || (wallsOnly && !s.IsWall)) continue;
                float d = (s.transform.position - p).sqrMagnitude;
                if (d <= bd) { bd = d; best = s; }
            }
            return best;
        }

        // ------------------------------------------------------------------ save (door open / closed)
        public string CaptureState()
        {
            var d = Door; if (!d) return null;
            return JsonUtility.ToJson(new State { door = d.IsOpen });
        }

        public void RestoreState(string state)
        {
            var d = Door; if (!d || string.IsNullOrEmpty(state)) return;
            try { var s = JsonUtility.FromJson<State>(state); d.SetOpen(s.door, true); }
            catch (System.Exception e) { Debug.LogWarning("[StructurePiece] bad door state: " + e.Message, this); }
        }
    }
}
