using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Audio;
using PrimalFrontier.Player;
using PrimalFrontier.World;

namespace PrimalFrontier.Building
{
    /// <summary>
    /// The woven door of a doorway wall: sits on the hinge pivot (the door leaf hangs from it toward +X), swings open by
    /// openAngle around the hinge, E toggles it. Its collider moves with the leaf. State saved by the wall's StructurePiece.
    /// </summary>
    public class DoorPiece : Interactable
    {
        [Tooltip("deg the leaf turns when open (positive = into the cell, -Z side of the wall)")] public float openAngle = 105f;
        public float swingSeconds = 0.45f;
        public bool IsOpen { get; private set; }
        public bool Moving => Mathf.Abs(_angle - Target) > 0.5f;
        public override float Radius => 0.7f;
        public event System.Action<DoorPiece, bool> Toggled;

        float _angle, _vel; Quaternion _closed; bool _init;
        float Target => IsOpen ? openAngle : 0f;

        protected override void OnEnable() { base.OnEnable(); Init(); }
        void Init() { if (_init) return; _closed = transform.localRotation; _init = true; }

        public override string GetPrompt(PlayerInteraction p, out string sub) { sub = null; return IsOpen ? "Close door" : "Open door"; }

        public override void Interact(PlayerInteraction p)
        {
            p.DoOneShot(PlayerActions.Interact, "OnInteract", () => Toggle(), transform.position, 0.5f, this);
        }

        public void Toggle() => SetOpen(!IsOpen, false);

        public void SetOpen(bool open, bool instant)
        {
            Init();
            if (IsOpen != open) { IsOpen = open; Toggled?.Invoke(this, open); }
            if (instant) { _angle = Target; _vel = 0f; Apply(); }
            else if (Application.isPlaying && SfxPlayer.Instance) { SfxPlayer.Instance.Play(SfxId.LeafRustle, transform.position, 0.7f); SfxPlayer.Instance.Play(SfxId.BranchSnap, transform.position, 0.25f); }
        }

        void Update()
        {
            if (!Moving) { if (_angle != Target) { _angle = Target; Apply(); } return; }
            _angle = Mathf.SmoothDamp(_angle, Target, ref _vel, Mathf.Max(0.05f, swingSeconds) * 0.5f, 720f, Time.deltaTime);
            Apply();
        }

        void Apply() { transform.localRotation = _closed * Quaternion.Euler(0f, _angle, 0f); }
    }
}
