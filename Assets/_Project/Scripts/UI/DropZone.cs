using UnityEngine;
using UnityEngine.EventSystems;

namespace PrimalFrontier.UI
{
    /// <summary>background behind the inventory window: dropping a dragged item here drops it in the world</summary>
    public class DropZone : MonoBehaviour, IDropHandler
    {
        public System.Action Dropped;
        public void OnDrop(PointerEventData e) => Dropped?.Invoke();
    }
}
