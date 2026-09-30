using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Player;
using PrimalFrontier.World;

namespace PrimalFrontier.AI
{
    /// <summary>
    /// The one interactable for every tracking sign (like the tree harvest point): a few times a second it moves onto the
    /// nearest sign the player has not read yet, so the prompt "Examine the tracks" appears beside prints, trampled plants,
    /// claw marks, droppings, blood and bones without a component per sign.
    /// </summary>
    public class TrackSignSpot : Interactable
    {
        int _sign = -1; float _next; Vector3 _focus;
        public int Current => _sign;
        public override float Range => WildlifeConfig.Instance.examineRange;
        public override float Radius => 0.35f;
        public override int Priority => 1;
        public override Vector3 FocusPoint => _sign >= 0 ? _focus : transform.position + Vector3.down * 999f;

        void Update()
        {
            if (Time.time < _next) return;
            _next = Time.time + 0.2f;
            var pp = PlayerLocator.Position;
            _sign = pp.HasValue ? TrackSigns.FindExaminable(pp.Value, Range + 1.2f) : -1;
            if (_sign < 0) return;
            _focus = TrackSigns.PositionOf(_sign) + Vector3.up * (TrackSigns.KindOf(_sign) == TrackKind.Scratch ? 0f : 0.15f);
            transform.position = _focus;
        }

        public override string GetPrompt(PlayerInteraction p, out string sub)
        {
            sub = null;
            if (_sign < 0 || !TrackSigns.IsExaminable(_sign)) return null;
            return TrackSigns.PromptFor(_sign);
        }

        public override void Interact(PlayerInteraction p)
        {
            int s = _sign; if (s < 0) return;
            p.DoOneShot(PlayerActions.Interact, "OnInteract", () => { TrackSigns.Examine(s); _next = 0f; }, _focus, 0.7f, this);
        }
    }
}
