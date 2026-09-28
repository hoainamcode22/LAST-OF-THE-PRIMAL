using UnityEngine;

namespace PrimalFrontier.Animation
{
    /// <summary>
    /// Spreads the wrist twist along the forearm. Each LateUpdate (after the Animator and its IK pass) the hand's twist
    /// relative to the lower arm, about the forearm axis (lower arm -> hand), is taken from a swing-twist split in
    /// lower-arm space, and a share of it goes onto LowerArmTwist_L/R (deforming, non-humanoid bones at mid forearm).
    /// The avatar's lowerArmTwist is 0 when these bones exist (PrimalCharacterBuilder), so the elbow does not roll.
    /// Sits on the Animator object; switches itself off when the rig has no twist bones.
    /// </summary>
    [DefaultExecutionOrder(90)]
    [DisallowMultipleComponent]
    public class TwistBoneDriver : MonoBehaviour
    {
        const string LowerL = "LowerArm_L", LowerR = "LowerArm_R", HandL = "Hand_L", HandR = "Hand_R", TwistL = "LowerArmTwist_L", TwistR = "LowerArmTwist_R";

        [Tooltip("share of the hand twist put on the twist bone (Blender constraint: 0.6)")]
        [Range(0f, 1f)] public float share = 0.6f;
        [Tooltip("hand twist is clamped to +- this before the share (deg)")]
        [Range(0f, 180f)] public float maxAngle = 120f;

        struct Arm
        {
            public Transform lower, hand, twist;
            public Quaternion handRestInv;   // inverse of the hand's rest rotation relative to the lower arm
            public Quaternion twistRest;     // twist bone rest rotation relative to the lower arm
            public Vector3 axis;             // forearm axis in lower-arm space
            public bool direct, ok;          // direct: the twist bone is a child of the lower arm
        }

        Arm _l, _r;

        public bool IsReady => _l.ok || _r.ok;
        /// <summary>last hand twist measured (deg, before the share), for checks in the Inspector / tests</summary>
        public float LeftTwist { get; private set; }
        public float RightTwist { get; private set; }

        void Awake()
        {
            _l = Bind(LowerL, HandL, TwistL);
            _r = Bind(LowerR, HandR, TwistR);
            CaptureRest(true);
            if (!IsReady) enabled = false;
        }

        Arm Bind(string lower, string hand, string twist)
        {
            return new Arm { lower = FindDeep(transform, lower), hand = FindDeep(transform, hand), twist = FindDeep(transform, twist) };
        }

        [ContextMenu("Capture Rest (bind pose)")] void CaptureBindPose() => CaptureRest(true);
        [ContextMenu("Capture Rest (current pose)")] void CaptureCurrentPose() => CaptureRest(false);

        /// <summary>
        /// store the rest relations: from the skinned meshes' bind poses (independent of the current pose), or from the
        /// current transforms when asked / when no skinned mesh maps these bones (the skeleton must be at rest then)
        /// </summary>
        public void CaptureRest(bool fromBindPose)
        {
            var smrs = fromBindPose ? GetComponentsInChildren<SkinnedMeshRenderer>(true) : null;
            Capture(ref _l, smrs);
            Capture(ref _r, smrs);
        }

        static void Capture(ref Arm a, SkinnedMeshRenderer[] smrs)
        {
            a.ok = false;
            if (!a.lower || !a.hand || !a.twist) return;
            Quaternion lowerRot, handRot, twistRot; Vector3 lowerPos, handPos;
            if (smrs == null || !FromBindPose(smrs, a, out lowerRot, out lowerPos, out handRot, out handPos, out twistRot))
            {
                lowerRot = a.lower.rotation; lowerPos = a.lower.position;
                handRot = a.hand.rotation; handPos = a.hand.position; twistRot = a.twist.rotation;
            }
            Quaternion inv = Quaternion.Inverse(lowerRot);
            Vector3 axis = inv * (handPos - lowerPos);
            if (axis.sqrMagnitude < 1e-8f) return;
            a.axis = axis.normalized;
            a.handRestInv = Quaternion.Inverse(inv * handRot);
            a.twistRest = inv * twistRot;
            a.direct = a.twist.parent == a.lower;
            a.ok = true;
        }

        /// <summary>rest pose of the three bones from the first skinned mesh that is bound to all of them</summary>
        static bool FromBindPose(SkinnedMeshRenderer[] smrs, Arm a, out Quaternion lowerRot, out Vector3 lowerPos, out Quaternion handRot, out Vector3 handPos, out Quaternion twistRot)
        {
            lowerRot = handRot = twistRot = Quaternion.identity; lowerPos = handPos = Vector3.zero;
            foreach (var smr in smrs)
            {
                if (!smr || !smr.sharedMesh) continue;
                var bones = smr.bones;
                int il = -1, ih = -1, it = -1;
                for (int i = 0; i < bones.Length; i++)
                {
                    if (bones[i] == a.lower) il = i; else if (bones[i] == a.hand) ih = i; else if (bones[i] == a.twist) it = i;
                }
                if (il < 0 || ih < 0 || it < 0) continue;
                var bp = smr.sharedMesh.bindposes;
                if (bp.Length != bones.Length) continue;
                Matrix4x4 ml = bp[il].inverse, mh = bp[ih].inverse, mt = bp[it].inverse;       // bone -> mesh space at bind time
                lowerRot = ml.rotation; lowerPos = ml.GetColumn(3);
                handRot = mh.rotation; handPos = mh.GetColumn(3);
                twistRot = mt.rotation;
                return true;
            }
            return false;
        }

        void LateUpdate()
        {
            if (_l.ok) LeftTwist = Drive(ref _l);
            if (_r.ok) RightTwist = Drive(ref _r);
        }

        float Drive(ref Arm a)
        {
            Quaternion lowerRot = a.lower.rotation;
            // hand rotation change since rest, expressed in lower-arm space
            Quaternion d = Quaternion.Inverse(lowerRot) * a.hand.rotation * a.handRestInv;
            // twist part about the forearm axis (swing-twist split): angle = 2 atan2(v . axis, w)
            float proj = d.x * a.axis.x + d.y * a.axis.y + d.z * a.axis.z;
            float angle = 2f * Mathf.Atan2(proj, d.w) * Mathf.Rad2Deg;
            if (angle > 180f) angle -= 360f; else if (angle < -180f) angle += 360f;
            angle = Mathf.Clamp(angle, -maxAngle, maxAngle);
            Quaternion local = Quaternion.AngleAxis(angle * share, a.axis) * a.twistRest;
            if (a.direct) a.twist.localRotation = local;
            else a.twist.rotation = lowerRot * local;
            return angle;
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var r = FindDeep(root.GetChild(i), name);
                if (r) return r;
            }
            return null;
        }
    }
}
