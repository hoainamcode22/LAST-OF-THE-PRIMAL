using System.Collections.Generic;
using UnityEngine;
using PrimalFrontier.Survival;

namespace PrimalFrontier.Player
{
    /// <summary>
    /// Wet skin and clothes: as PlayerSurvival.Wetness rises (rain, swimming) the body gets a little darker and
    /// shinier, and dries back as the wetness drops. Per-renderer property blocks (no material asset is changed);
    /// the blocks are cleared again when dry so the renderers batch normally. Eyes are left alone.
    /// </summary>
    public class PlayerWetLook : MonoBehaviour
    {
        [Range(0, 1)] public float darken = 0.22f;
        [Range(0, 1)] public float extraSmoothness = 0.38f;

        struct Slot { public Renderer r; public int index; public Color baseColor; public float smooth; public bool hasColor, hasSmooth; }
        readonly List<Slot> _slots = new List<Slot>();
        PlayerSurvival _sv; MaterialPropertyBlock _mpb; float _applied = -1f;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"), SmoothId = Shader.PropertyToID("_Smoothness");

        void Start()
        {
            _sv = GetComponent<PlayerSurvival>(); _mpb = new MaterialPropertyBlock();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is SkinnedMeshRenderer) && !(r is MeshRenderer)) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i]; if (!m || m.name.ToLower().Contains("eye")) continue;
                    var s = new Slot { r = r, index = i, hasColor = m.HasProperty(BaseColorId), hasSmooth = m.HasProperty(SmoothId) };
                    if (s.hasColor) s.baseColor = m.GetColor(BaseColorId);
                    if (s.hasSmooth) s.smooth = m.GetFloat(SmoothId);
                    if (s.hasColor || s.hasSmooth) _slots.Add(s);
                }
            }
        }

        void LateUpdate()
        {
            float w = _sv ? _sv.Wetness : 0f;
            if (Mathf.Abs(w - _applied) < 0.02f && !(w <= 0.001f && _applied > 0f)) return;
            _applied = w;
            foreach (var s in _slots)
            {
                if (!s.r) continue;
                if (w <= 0.001f) { s.r.SetPropertyBlock(null, s.index); continue; }
                _mpb.Clear();
                if (s.hasColor) _mpb.SetColor(BaseColorId, Color.Lerp(s.baseColor, s.baseColor * (1f - darken), w));
                if (s.hasSmooth) _mpb.SetFloat(SmoothId, Mathf.Min(0.92f, s.smooth + extraSmoothness * w));
                s.r.SetPropertyBlock(_mpb, s.index);
            }
            if (w <= 0.001f) _applied = 0f;
        }
    }
}
