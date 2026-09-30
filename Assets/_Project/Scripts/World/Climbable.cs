using UnityEngine;
using PrimalFrontier.Player;

namespace PrimalFrontier.World
{
    /// <summary>what is climbed: a trunk (round, climbed around its axis), a short ledge (pull up and over in one move), or a
    /// short rock face (climb up / down the face, pull over the top)</summary>
    public enum ClimbKind { Tree, Ledge, RockFace }

    /// <summary>
    /// Something the player can climb (only designated ones: not every tree or rock).
    /// - Tree: on the tree root with two child points, ClimbStart (bottom of the trunk, at the ground) and ClimbEnd (the
    ///   highest point the feet may reach). Fruit on the tree = FruitCluster children.
    /// - Ledge / RockFace: on an empty in front of the rock whose FORWARD points INTO the face (the way the climber looks).
    ///   ClimbStart = the base of the face at the ground, ClimbEnd = the top edge (the lip the hands grab), ClimbExit
    ///   (optional) = where the player stands on top after pulling over (default: 0.6 m past the lip, on the ground found
    ///   there). A Ledge is pulled over in one move (lip about 0.9-2.3 m above the base); a RockFace is climbed with W / S
    ///   and pulled over at the top (or climbed down again). `CreateOn` builds the points for a builder.
    /// Move the points in the Scene view; the gizmo shows the climbable part and the exit.
    /// </summary>
    public class Climbable : Interactable
    {
        public ClimbKind kind = ClimbKind.Tree;
        public string displayName = "tree";
        [Tooltip("bottom of the climbable trunk / face, on the ground")] public Transform climbStart;
        [Tooltip("tree: highest point the feet may reach. Ledge / rock face: the top edge the hands grab")] public Transform climbEnd;
        [Tooltip("ledge / rock face: where the player stands after pulling over the top (empty = 0.6 m past the lip)")] public Transform climbExit;
        public float trunkRadius = 0.2f;
        [Tooltip("ledge / rock face: width of the climbable face (hands and feet stay inside it)")] public float faceWidth = 1.2f;
        public float climbSpeed = 0.6f, descendSpeed = 0.55f;
        [Tooltip("stamina per second while climbing up")] public float staminaPerSecond = 3f;
        [Tooltip("stamina for pulling up over a ledge / the top of a rock face")] public float mantleStamina = 6f;

        public bool IsFace => kind != ClimbKind.Tree;
        public override float Range => IsFace ? 1.5f : 1.7f;
        public override float Radius => IsFace ? 0.3f : trunkRadius;
        public override int Priority => 1;
        public override Vector3 FocusPoint => IsFace ? Bottom + Vector3.up * Mathf.Min(1.1f, Length * 0.5f) - Forward * 0.1f : Bottom + Vector3.up * 1.3f;
        public Vector3 Bottom => climbStart ? climbStart.position : transform.position;
        public Vector3 Top => climbEnd ? climbEnd.position : transform.position + Vector3.up * (IsFace ? 1.5f : 4f);
        public float Length => Mathf.Max(0.5f, Top.y - Bottom.y);
        /// <summary>trunk centre / face line at a height above the bottom (follows a leaning trunk or a sloped face)</summary>
        public Vector3 AxisAt(float h) => Vector3.Lerp(Bottom, Top, Mathf.Clamp01(h / Length));
        /// <summary>into the face (flat): the way the climber looks</summary>
        public Vector3 Forward { get { var f = transform.forward; f.y = 0f; return f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward; } }
        public Vector3 Right => Vector3.Cross(Vector3.up, Forward);
        /// <summary>out of the face toward the climber (the face plane holds the Bottom-Top line and Right)</summary>
        public Vector3 FaceNormal
        {
            get
            {
                Vector3 line = Top - Bottom; line = line.sqrMagnitude > 1e-4f ? line.normalized : Vector3.up;
                var n = Vector3.Cross(line, Right).normalized;
                return Vector3.Dot(n, Forward) > 0f ? -n : n;
            }
        }
        /// <summary>standing point on top after a pull-over</summary>
        public Vector3 Exit
        {
            get
            {
                if (climbExit) return climbExit.position;
                Vector3 p = Top + Forward * 0.6f;
                if (Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out var hit, 3f, ~0, QueryTriggerInteraction.Ignore)) p.y = hit.point.y;
                return p;
            }
        }
        /// <summary>point on the face plane closest to p, kept inside the face width, pushed out by extra (m)</summary>
        public Vector3 OnFace(Vector3 p, float extra)
        {
            Vector3 a = Bottom, n = FaceNormal;
            Vector3 q = p - n * Vector3.Dot(p - a, n);                              // onto the plane
            Vector3 line = Top - Bottom; float len2 = Mathf.Max(0.01f, line.sqrMagnitude);
            float t = Mathf.Clamp01(Vector3.Dot(q - a, line) / len2);
            Vector3 axis = a + line * t;
            float side = Mathf.Clamp(Vector3.Dot(q - axis, Right), -faceWidth * 0.5f, faceWidth * 0.5f);
            return axis + Right * side + n * extra;
        }

        FruitCluster[] _fruits; string _prompt, _fruitSub; int _fruitSubN = -1;
        /// <summary>fruit on this tree (cached; refreshed when children change or by RefreshFruits)</summary>
        public FruitCluster[] Fruits => _fruits ??= GetComponentsInChildren<FruitCluster>();
        public void RefreshFruits() => _fruits = GetComponentsInChildren<FruitCluster>();
        void Awake() => RefreshFruits();
        void OnTransformChildrenChanged() => RefreshFruits();

        public override string GetPrompt(PlayerInteraction p, out string sub)
        {
            if (kind == ClimbKind.Ledge) { sub = "Pull yourself up"; return _prompt ??= "Climb up the " + displayName; }
            if (kind == ClimbKind.RockFace) { sub = "W / S climb, over the top at the end"; return _prompt ??= "Climb the " + displayName; }
            int ripe = 0; foreach (var f in Fruits) if (f && f.Ripe) ripe++;
            if (ripe != _fruitSubN) { _fruitSubN = ripe; _fruitSub = ripe > 0 ? $"{ripe} bunch{(ripe > 1 ? "es" : "")} of fruit up high" : null; }
            sub = _fruitSub;
            return _prompt ??= "Climb the " + displayName;
        }

        public override bool CanInteract(PlayerInteraction p)
        {
            var c = p.GetComponent<PlayerClimb>();
            if (c == null || c.IsClimbing || (p.Motor != null && !p.Motor.IsGrounded)) return false;
            if (!IsFace) return true;
            // a face is climbed from its front, not from the top or the side
            Vector3 d = Bottom - p.transform.position; d.y = 0f;
            return Vector3.Dot(d, Forward) > -0.2f && p.transform.position.y < Top.y - 0.4f;
        }

        public override void Interact(PlayerInteraction p)
        {
            var c = p.GetComponent<PlayerClimb>();
            if (!c) c = p.gameObject.AddComponent<PlayerClimb>();
            c.Begin(this);
        }

        // ------------------------------------------------------------------ builders
        /// <summary>
        /// For environment builders: a Ledge / RockFace on host (a child "Climb_Ledge" / "Climb_RockFace" empty facing into the
        /// rock) with ClimbStart at baseOnGround, ClimbEnd at lip and ClimbExit at exit (null = default). The face is assumed
        /// to run from base to lip; width = climbable width. Returns the component (existing child of that name is reused).
        /// </summary>
        public static Climbable CreateOn(GameObject host, ClimbKind kind, Vector3 baseOnGround, Vector3 lip, Vector3? exit = null, float width = 1.2f, string name = null)
        {
            string n = "Climb_" + kind;
            var t = host.transform.Find(n);
            var go = t ? t.gameObject : new GameObject(n);
            go.transform.SetParent(host.transform, true);
            Vector3 into = lip - baseOnGround; into.y = 0f;
            if (into.sqrMagnitude < 0.01f) into = host.transform.position - baseOnGround;
            into.y = 0f; if (into.sqrMagnitude < 1e-4f) into = Vector3.forward;
            go.transform.SetPositionAndRotation(baseOnGround, Quaternion.LookRotation(into.normalized, Vector3.up));
            var c = go.GetComponent<Climbable>(); if (!c) c = go.AddComponent<Climbable>();
            c.kind = kind; c.faceWidth = width; c.displayName = name ?? (kind == ClimbKind.Ledge ? "ledge" : "rock face");
            c.climbStart = Point(go.transform, "ClimbStart", baseOnGround);
            c.climbEnd = Point(go.transform, "ClimbEnd", lip);
            if (exit.HasValue) c.climbExit = Point(go.transform, "ClimbExit", exit.Value);
            c._prompt = null;
            return c;
        }

        static Transform Point(Transform parent, string n, Vector3 at)
        {
            var t = parent.Find(n); if (!t) { t = new GameObject(n).transform; t.SetParent(parent, false); }
            t.position = at; return t;
        }

        /// <summary>builder check: null when the climb can work, else what is wrong (logged by the builder)</summary>
        public string Validate()
        {
            Physics.SyncTransforms();                                                // a builder just moved / made the rock
            if (!IsFace) return Length < 1f ? "trunk shorter than 1 m" : null;
            float h = Length;
            if (kind == ClimbKind.Ledge && (h < 0.8f || h > 2.4f)) return $"ledge height {h:F2} m (0.8-2.4 m)";
            if (kind == ClimbKind.RockFace && (h < 1.8f || h > 8f)) return $"rock face height {h:F2} m (1.8-8 m)";
            Vector3 e = Exit;
            if (e.y < Top.y - 0.6f) return "exit is well below the lip (no ground on top?)";
            // room to stand on top: a capsule 1.8 m tall at the exit
            if (Physics.CheckCapsule(e + Vector3.up * 0.4f, e + Vector3.up * 1.5f, 0.3f, ~0, QueryTriggerInteraction.Ignore)) return "no room to stand at the exit";
            // room in front of the face at the bottom
            Vector3 s = Bottom - Forward * 0.5f;
            if (Physics.CheckCapsule(s + Vector3.up * 0.4f, s + Vector3.up * 1.5f, 0.28f, ~0, QueryTriggerInteraction.Ignore)) return "no room to stand in front of the face";
            return null;
        }

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.4f, 0.9f, 0.5f, 0.8f);
            Gizmos.DrawLine(Bottom, Top);
            if (!IsFace) { Gizmos.DrawWireSphere(Bottom, trunkRadius + 0.3f); Gizmos.DrawWireSphere(Top, trunkRadius + 0.3f); return; }
            Vector3 r = Right * faceWidth * 0.5f;
            Gizmos.DrawLine(Bottom - r, Bottom + r); Gizmos.DrawLine(Top - r, Top + r); Gizmos.DrawLine(Bottom - r, Top - r); Gizmos.DrawLine(Bottom + r, Top + r);
            Gizmos.color = new Color(0.9f, 0.8f, 0.3f, 0.9f);
            var e = climbExit ? climbExit.position : Top + Forward * 0.6f;
            Gizmos.DrawLine(Top, e); Gizmos.DrawWireSphere(e, 0.3f);
        }
#endif
    }
}
