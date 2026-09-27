using System.Collections.Generic;
using UnityEngine;
using PrimalFrontier.Player;

namespace PrimalFrontier.World
{
    /// <summary>
    /// Anything the player can use with the interact key. Registered in a static list (no physics queries);
    /// PlayerInteraction picks the best one in front of the player and shows its prompt.
    /// </summary>
    public abstract class Interactable : MonoBehaviour
    {
        public static readonly List<Interactable> Active = new List<Interactable>(256);

        [Tooltip("stable id for the save file (set by the builder; generated from name + position if empty)")]
        [SerializeField] string saveId;
        public string SaveId
        {
            get
            {
                if (string.IsNullOrEmpty(saveId))
                {
                    var p = transform.position;
                    saveId = $"{name}@{Mathf.RoundToInt(p.x * 10)},{Mathf.RoundToInt(p.y * 10)},{Mathf.RoundToInt(p.z * 10)}";
                }
                return saveId;
            }
            set => saveId = value;
        }

        public virtual float Range => 2.3f;
        Bounds? _b;
        /// <summary>where the player looks / aims the tool (bounds centre by default)</summary>
        public virtual Vector3 FocusPoint
        {
            get
            {
                if (_b == null)
                {
                    var rs = GetComponentsInChildren<Renderer>();
                    if (rs.Length > 0) { var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); _b = b; }
                    else _b = new Bounds(transform.position + Vector3.up * 0.4f, Vector3.one * 0.5f);
                }
                var c = _b.Value.center; c.y = Mathf.Min(c.y, transform.position.y + 1.2f); return c;
            }
        }
        /// <summary>distance measured to the closest point of the object (big rocks, shelters)</summary>
        public virtual float Radius => 0.3f;
        public void RefreshBounds() => _b = null;

        protected virtual void OnEnable() { if (!Active.Contains(this)) Active.Add(this); }
        protected virtual void OnDisable() { Active.Remove(this); }

        /// <summary>prompt for the current situation, or null when nothing can be done right now. sub = second line (grey)</summary>
        public abstract string GetPrompt(PlayerInteraction p, out string sub);
        /// <summary>false: the prompt is shown greyed out (e.g. "Needs an axe") and E does nothing</summary>
        public virtual bool CanInteract(PlayerInteraction p) => true;
        public abstract void Interact(PlayerInteraction p);
        /// <summary>optional hold-E action (extinguish...). null = none</summary>
        public virtual string HoldPrompt(PlayerInteraction p) => null;
        public virtual void HoldInteract(PlayerInteraction p) { }
        public virtual int Priority => 0;
    }
}
