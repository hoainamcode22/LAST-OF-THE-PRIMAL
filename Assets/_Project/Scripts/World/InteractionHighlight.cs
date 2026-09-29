using UnityEngine;
using PrimalFrontier.Player;

namespace PrimalFrontier.World
{
    /// <summary>
    /// A subtle glow on the one thing the player can gather right now: the current interaction target's renderers get a
    /// slightly brighter base colour (material property block, pulsing gently) while its prompt shows. Only resource nodes
    /// and pickups, only the target, nothing floating over every node. One component (on [ResourceManager]).
    /// </summary>
    public class InteractionHighlight : MonoBehaviour
    {
        [Range(0f, 0.5f)] public float strength = 0.16f;
        [Range(0f, 0.3f)] public float pulse = 0.06f;
        public float pulseSpeed = 3f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"), ColorId = Shader.PropertyToID("_Color");
        PlayerInteraction _pi; Interactable _target; Renderer[] _rs; Color[][] _base; int[][] _prop;
        MaterialPropertyBlock _mpb;
        public Interactable Current => _target;

        void LateUpdate()
        {
            if (!_pi) { var t = PlayerLocator.Player; _pi = t ? t.GetComponent<PlayerInteraction>() : null; }
            Interactable want = null;
            if (_pi && _pi.isActiveAndEnabled && !_pi.InAction && _pi.Prompt != null)
            {
                var t = _pi.Target;
                if (t is ResourceNode rn && !rn.IsEmpty) want = t;
                else if (t is WorldPickup) want = t;
            }
            if (want != _target) { Clear(); _target = want; if (want) Capture(want); }
            if (_rs == null) return;
            float k = 1f + strength + pulse * Mathf.Sin(Time.unscaledTime * pulseSpeed);
            _mpb ??= new MaterialPropertyBlock();
            for (int i = 0; i < _rs.Length; i++)
            {
                var r = _rs[i]; if (!r || !r.enabled) continue;
                for (int m = 0; m < _base[i].Length; m++)
                {
                    if (_prop[i][m] == 0) continue;
                    var c = _base[i][m]; var b = new Color(c.r * k, c.g * k, c.b * k, c.a);
                    _mpb.Clear(); _mpb.SetColor(_prop[i][m], b); r.SetPropertyBlock(_mpb, m);
                }
            }
        }

        void Capture(Interactable t)
        {
            _rs = t is ResourceNode n ? n.Renderers : t.GetComponentsInChildren<Renderer>();
            _base = new Color[_rs.Length][]; _prop = new int[_rs.Length][];
            for (int i = 0; i < _rs.Length; i++)
            {
                var r = _rs[i]; var mats = r && !(r is ParticleSystemRenderer) ? r.sharedMaterials : new Material[0];
                _base[i] = new Color[mats.Length]; _prop[i] = new int[mats.Length];
                for (int m = 0; m < mats.Length; m++)
                {
                    var mat = mats[m]; if (!mat) continue;
                    if (mat.HasProperty(BaseColorId)) { _prop[i][m] = BaseColorId; _base[i][m] = mat.GetColor(BaseColorId); }
                    else if (mat.HasProperty(ColorId)) { _prop[i][m] = ColorId; _base[i][m] = mat.GetColor(ColorId); }
                }
            }
        }

        void Clear()
        {
            if (_rs != null)
                for (int i = 0; i < _rs.Length; i++)
                {
                    var r = _rs[i]; if (!r) continue;
                    for (int m = 0; m < _prop[i].Length; m++) if (_prop[i][m] != 0) r.SetPropertyBlock(null, m);
                }
            _rs = null; _base = null; _prop = null; _target = null;
        }

        void OnDisable() => Clear();
    }
}
