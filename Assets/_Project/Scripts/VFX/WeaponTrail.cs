using UnityEngine;

namespace PrimalFrontier.VFX
{
    /// <summary>
    /// Short swing trail on the tip of a held weapon (WeaponData.trail). Emits only while the attack's hitbox is live
    /// (Emit(true / false) from the melee weapon), so it shows the real active window. Tapers 0.06 -> 0 m over 0.12 s.
    /// </summary>
    [RequireComponent(typeof(TrailRenderer))]
    public class WeaponTrail : MonoBehaviour
    {
        public float width = 0.06f;
        public float time = 0.12f;
        TrailRenderer _tr; bool _on;
        static Material _fallback;

        /// <summary>adds (or reuses) a "Trail" child at localTip of the model</summary>
        public static WeaponTrail Attach(Transform model, Vector3 localTip, Color color, Material material)
        {
            if (!model) return null;
            var t = model.Find("Trail");
            if (!t) { t = new GameObject("Trail").transform; t.SetParent(model, false); }
            t.localPosition = localTip; t.localRotation = Quaternion.identity;
            t.gameObject.layer = model.gameObject.layer;
            var trail = t.gameObject.GetOrAdd<WeaponTrail>();
            trail.Configure(color, material);
            return trail;
        }

        void Awake() { _tr = GetComponent<TrailRenderer>(); _tr.emitting = false; }

        public void Configure(Color color, Material material)
        {
            if (!_tr) _tr = GetComponent<TrailRenderer>();
            _tr.time = time;
            _tr.minVertexDistance = 0.02f;
            _tr.widthCurve = new AnimationCurve(new Keyframe(0f, width), new Keyframe(1f, 0f));
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                      new[] { new GradientAlphaKey(color.a, 0f), new GradientAlphaKey(0f, 1f) });
            _tr.colorGradient = g;
            _tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _tr.receiveShadows = false;
            _tr.alignment = LineAlignment.View;
            _tr.numCapVertices = 0;
            _tr.sharedMaterial = material ? material : Fallback();
            _tr.emitting = false; _on = false;
            _tr.Clear();
        }

        static Material Fallback()
        {
            if (_fallback) return _fallback;
            var sh = Shader.Find("Sprites/Default");
            if (!sh) sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            _fallback = sh ? new Material(sh) { name = "M_WeaponTrail_Runtime" } : null;
            return _fallback;
        }

        /// <summary>start / stop leaving the trail (a new swing starts clean, not joined to the last one)</summary>
        public void Emit(bool on)
        {
            if (!_tr || on == _on) return;
            _on = on;
            if (on) _tr.Clear();
            _tr.emitting = on;
        }

        void OnDisable() { _on = false; if (_tr) { _tr.emitting = false; _tr.Clear(); } }
    }
}
