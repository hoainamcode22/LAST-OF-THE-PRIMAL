using UnityEngine;

namespace PrimalFrontier.Player
{
    /// <summary>
    /// Finds or creates the player's helper nodes (Documentation/Upgrade/CHARACTER_HIERARCHY.md) and exposes them:
    /// plain empties on the root (Animation/IK goals and elbow hints, Equipment, Interaction/InteractionOrigin,
    /// Combat/AttackOrigin + HitPoint, CameraTarget, VFX) and weapon sockets under the humanoid bones
    /// (RightHandWeaponSocket + ArrowSocket on the right hand, LeftHandWeaponSocket on the left hand, BackWeaponSocket
    /// on the (upper) chest, HipToolSocket on the hips). Nodes are looked up by name first, so nothing is ever
    /// duplicated and poses set by hand or by an editor builder are kept; only missing nodes are created.
    /// The hand sockets are created on the grip frame computed from the finger bones (the frame PlayerEquipment always
    /// used), so held items keep their look.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerHierarchy : MonoBehaviour
    {
        public const string RightHandSocketName = "RightHandWeaponSocket", LeftHandSocketName = "LeftHandWeaponSocket", ArrowSocketName = "ArrowSocket",
            BackSocketName = "BackWeaponSocket", HipSocketName = "HipToolSocket";

        [Tooltip("the humanoid Animator on Model (empty = found in children)")] public Animator animator;

        Transform _ik, _rightHandIK, _leftHandIK, _rightElbowHint, _leftElbowHint, _equipment, _interactionOrigin, _attackOrigin, _hitPoint, _cameraTarget, _vfx;
        Transform _rightSocket, _leftSocket, _arrowSocket, _backSocket, _hipSocket;
        bool _groups, _built, _noBones;
        /// <summary>the bone sockets exist (they wait until the humanoid Animator has bound its bones)</summary>
        public bool SocketsReady { get { Ensure(); return _built; } }

        public Transform IKRoot { get { Ensure(); return _ik; } }
        public Transform RightHandIK { get { Ensure(); return _rightHandIK; } }
        public Transform LeftHandIK { get { Ensure(); return _leftHandIK; } }
        public Transform RightElbowHint { get { Ensure(); return _rightElbowHint; } }
        public Transform LeftElbowHint { get { Ensure(); return _leftElbowHint; } }
        public Transform Equipment { get { Ensure(); return _equipment; } }
        public Transform InteractionOrigin { get { Ensure(); return _interactionOrigin; } }
        public Transform AttackOrigin { get { Ensure(); return _attackOrigin; } }
        public Transform HitPoint { get { Ensure(); return _hitPoint; } }
        public Transform CameraTarget { get { Ensure(); return _cameraTarget; } }
        public Transform VFX { get { Ensure(); return _vfx; } }
        /// <summary>sword / spear / knife / tools grip (child of the right hand bone; the root when the rig is not humanoid)</summary>
        public Transform RightHandWeaponSocket { get { Ensure(); return _rightSocket; } }
        /// <summary>bow grip / off-hand items (child of the left hand bone)</summary>
        public Transform LeftHandWeaponSocket { get { Ensure(); return _leftSocket; } }
        /// <summary>nock point of the arrow while drawing (child of the right hand bone)</summary>
        public Transform ArrowSocket { get { Ensure(); return _arrowSocket; } }
        /// <summary>weapon carried on the back (child of the upper chest / chest bone)</summary>
        public Transform BackWeaponSocket { get { Ensure(); return _backSocket; } }
        /// <summary>knife / small tool at the hip (child of the hips bone)</summary>
        public Transform HipToolSocket { get { Ensure(); return _hipSocket; } }

        public static PlayerHierarchy Of(GameObject player) => player ? player.GetOrAdd<PlayerHierarchy>() : null;

        void Awake() => Ensure();
        void Start()
        {
            Ensure();
            if (!_built) { _noBones = true; Ensure(); }          // Animator disabled / never bound: sockets on the root, as before
        }

        /// <summary>find-or-create every node (idempotent; cheap once built)</summary>
        public void Ensure()
        {
            if (_built) return;
            if (!animator)
            {
                var drv = GetComponent<PlayerAnimationDriver>();
                animator = drv && drv.animator ? drv.animator : GetComponentInChildren<Animator>();
            }
            if (!_groups) BuildGroups();
            // a humanoid whose Animator has not bound its bones yet (sibling Awake order): try again on the next access
            if (!_noBones && animator && animator.isHuman && !animator.GetBoneTransform(HumanBodyBones.RightHand)) return;
            BuildSockets();
            _built = true;
        }

        void BuildGroups()
        {
            var root = transform;
            // groups on the root (targets and references only)
            var anim = Node(root, "Animation", Vector3.zero);
            _ik = Node(anim, "IK", Vector3.zero);
            _rightHandIK = Node(_ik, "RightHandIK", new Vector3(0.25f, 1.05f, 0.3f));
            _leftHandIK = Node(_ik, "LeftHandIK", new Vector3(-0.25f, 1.05f, 0.3f));
            _rightElbowHint = Node(_ik, "RightElbowHint", new Vector3(0.45f, 1.1f, -0.3f));
            _leftElbowHint = Node(_ik, "LeftElbowHint", new Vector3(-0.45f, 1.1f, -0.3f));
            _equipment = Node(root, "Equipment", Vector3.zero);
            var interaction = Node(root, "Interaction", Vector3.zero);
            _interactionOrigin = Node(interaction, "InteractionOrigin", new Vector3(0f, 1.35f, 0.1f));
            var combat = Node(root, "Combat", Vector3.zero);
            _attackOrigin = Node(combat, "AttackOrigin", new Vector3(0f, 1.1f, 0.12f));
            _hitPoint = Node(combat, "HitPoint", new Vector3(0f, 1.0f, 0f));
            _cameraTarget = Node(root, "CameraTarget", new Vector3(0f, 1.62f, 0f));
            _vfx = Node(root, "VFX", Vector3.zero);
            _groups = true;
        }

        void BuildSockets()
        {
            var root = transform;
            bool human = !_noBones && animator && animator.isHuman;
            Transform rHand = human ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
            Transform lHand = human ? animator.GetBoneTransform(HumanBodyBones.LeftHand) : null;
            Transform chest = human ? (animator.GetBoneTransform(HumanBodyBones.UpperChest) ? animator.GetBoneTransform(HumanBodyBones.UpperChest) : animator.GetBoneTransform(HumanBodyBones.Chest)) : null;
            Transform hips = human ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;

            // right hand: the grip frame from the finger bones (what PlayerEquipment used), else the bone itself
            _rightSocket = Find(rHand ? rHand : root, RightHandSocketName);
            if (!_rightSocket)
            {
                if (rHand && TryComputeGrip(animator, false, out var p, out var r)) _rightSocket = Create(rHand, RightHandSocketName, p, r);
                else if (rHand) _rightSocket = Create(rHand, RightHandSocketName, Vector3.zero, Quaternion.identity);
                else _rightSocket = Create(root, RightHandSocketName, new Vector3(0.25f, 1.0f, 0.3f), Quaternion.identity);
            }
            _leftSocket = Find(lHand ? lHand : root, LeftHandSocketName);
            if (!_leftSocket)
            {
                if (lHand && TryComputeGrip(animator, true, out var p, out var r)) _leftSocket = Create(lHand, LeftHandSocketName, p, r);
                else if (lHand) _leftSocket = Create(lHand, LeftHandSocketName, Vector3.zero, Quaternion.identity);
                else _leftSocket = Create(root, LeftHandSocketName, new Vector3(-0.25f, 1.0f, 0.3f), Quaternion.identity);
            }
            // arrow nock: between the thumb and the index finger of the right hand, same frame as the grip
            _arrowSocket = Find(rHand ? rHand : root, ArrowSocketName);
            if (!_arrowSocket)
            {
                if (rHand)
                {
                    var idx = animator.GetBoneTransform(HumanBodyBones.RightIndexProximal);
                    var thumb = animator.GetBoneTransform(HumanBodyBones.RightThumbIntermediate);
                    Vector3 w = idx && thumb ? Vector3.Lerp(idx.position, thumb.position, 0.5f) : rHand.position + rHand.rotation * Vector3.up * 0.08f;
                    _arrowSocket = Create(rHand, ArrowSocketName, rHand.InverseTransformPoint(w), _rightSocket.parent == rHand ? _rightSocket.localRotation : Quaternion.identity);
                }
                else _arrowSocket = Create(root, ArrowSocketName, new Vector3(0.2f, 1.55f, 0.2f), Quaternion.identity);
            }
            // carry sockets: posed from the character's facing (blade up across the back, knife down at the right hip)
            _backSocket = Find(chest ? chest : root, BackSocketName);
            if (!_backSocket)
            {
                Transform parent = chest ? chest : root;
                Vector3 w = (chest ? chest.position : root.position + Vector3.up * 1.35f) - root.forward * 0.17f;
                Quaternion wr = root.rotation * Quaternion.Euler(0f, 0f, -35f);
                _backSocket = Create(parent, BackSocketName, parent.InverseTransformPoint(w), Quaternion.Inverse(parent.rotation) * wr);
            }
            _hipSocket = Find(hips ? hips : root, HipSocketName);
            if (!_hipSocket)
            {
                Transform parent = hips ? hips : root;
                Vector3 w = (hips ? hips.position : root.position + Vector3.up * 0.95f) + root.right * 0.17f - root.up * 0.04f + root.forward * 0.02f;
                Quaternion wr = root.rotation * Quaternion.Euler(0f, 0f, 180f);
                _hipSocket = Create(parent, HipSocketName, parent.InverseTransformPoint(w), Quaternion.Inverse(parent.rotation) * wr);
            }
        }

        /// <summary>
        /// grip frame in the hand bone's space: +Y along the fingers (projected off the knuckle line), +Z across the
        /// knuckles towards the little finger, origin in the palm between the wrist and the finger roots.
        /// </summary>
        public static bool TryComputeGrip(Animator a, bool left, out Vector3 localPos, out Quaternion localRot)
        {
            localPos = Vector3.zero; localRot = Quaternion.identity;
            if (!a || !a.isHuman) return false;
            var hand = a.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            var idx = a.GetBoneTransform(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
            var lit = a.GetBoneTransform(left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal);
            var mid = a.GetBoneTransform(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
            if (!hand || !idx || !lit || !mid) return false;
            Vector3 across = idx.position - lit.position;          // thumb side
            Vector3 fingers = mid.position - hand.position;
            Vector3 up = Vector3.ProjectOnPlane(fingers, across).normalized;
            Quaternion world = Quaternion.LookRotation(-across.normalized, up);
            localRot = Quaternion.Inverse(hand.rotation) * world;
            // palm centre: between the wrist and the finger roots, slightly into the palm
            Vector3 palmN = Vector3.Cross(across, fingers).normalized;         // right hand: out of the palm (left: back of the hand)
            if (left) palmN = -palmN;
            Vector3 p = Vector3.Lerp(hand.position, mid.position, 0.75f) + palmN * 0.015f;
            localPos = hand.InverseTransformPoint(p);
            return true;
        }

        // ------------------------------------------------------------------ helpers
        static Transform Find(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++) { var c = parent.GetChild(i); if (c.name == name) return c; }
            return null;
        }

        static Transform Node(Transform parent, string name, Vector3 localPos)
        {
            var t = Find(parent, name);
            return t ? t : Create(parent, name, localPos, Quaternion.identity);
        }

        static Transform Create(Transform parent, string name, Vector3 localPos, Quaternion localRot)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos; t.localRotation = localRot; t.localScale = Vector3.one;
            t.gameObject.layer = parent.gameObject.layer;
            return t;
        }
    }
}
