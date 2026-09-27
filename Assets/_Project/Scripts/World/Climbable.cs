using UnityEngine;
using PrimalFrontier.Player;

namespace PrimalFrontier.World
{
    /// <summary>
    /// A tree / pole / short rock face the player can climb (only designated ones: not every tree). Put it on the tree
    /// root with two child points: ClimbStart (bottom of the trunk, at the ground) and ClimbEnd (the highest point the
    /// feet may reach). Fruit on the tree = FruitCluster children. Move the points in the Scene view; the gizmo shows
    /// the climbable part of the trunk.
    /// </summary>
    public class Climbable : Interactable
    {
        public string displayName = "tree";
        [Tooltip("bottom of the climbable trunk, on the ground")] public Transform climbStart;
        [Tooltip("highest point the feet may reach")] public Transform climbEnd;
        public float trunkRadius = 0.2f;
        public float climbSpeed = 0.6f, descendSpeed = 0.55f;
        [Tooltip("stamina per second while climbing up")] public float staminaPerSecond = 3f;

        public override float Range => 1.7f;
        public override float Radius => trunkRadius;
        public override int Priority => 1;
        public override Vector3 FocusPoint => Bottom + Vector3.up * 1.3f;
        public Vector3 Bottom => climbStart ? climbStart.position : transform.position;
        public Vector3 Top => climbEnd ? climbEnd.position : transform.position + Vector3.up * 4f;
        public float Length => Mathf.Max(0.5f, Top.y - Bottom.y);
        /// <summary>trunk centre at a height above the bottom (follows a leaning trunk)</summary>
        public Vector3 AxisAt(float h) => Vector3.Lerp(Bottom, Top, Mathf.Clamp01(h / Length));

        public FruitCluster[] Fruits => GetComponentsInChildren<FruitCluster>();

        public override string GetPrompt(PlayerInteraction p, out string sub)
        {
            int ripe = 0; foreach (var f in Fruits) if (f.Ripe) ripe++;
            sub = ripe > 0 ? $"{ripe} bunch{(ripe > 1 ? "es" : "")} of fruit up high" : null;
            return "Climb the " + displayName;
        }

        public override bool CanInteract(PlayerInteraction p)
        {
            var c = p.GetComponent<PlayerClimb>();
            return c != null && !c.IsClimbing && (p.Motor == null || p.Motor.IsGrounded);
        }

        public override void Interact(PlayerInteraction p)
        {
            var c = p.GetComponent<PlayerClimb>();
            if (!c) c = p.gameObject.AddComponent<PlayerClimb>();
            c.Begin(this);
        }

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.4f, 0.9f, 0.5f, 0.8f);
            Gizmos.DrawLine(Bottom, Top);
            Gizmos.DrawWireSphere(Bottom, trunkRadius + 0.3f); Gizmos.DrawWireSphere(Top, trunkRadius + 0.3f);
        }
#endif
    }
}
