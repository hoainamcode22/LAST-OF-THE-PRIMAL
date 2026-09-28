using UnityEngine;
using UnityEngine.Rendering;
using PrimalFrontier.Core;

namespace PrimalFrontier.VFX
{
    /// <summary>
    /// Stars and the moon at night: a camera-centred dome with the PF/Night Sky material, drawn behind everything
    /// (see the shader). Fades with TimeManager.NightFactor (_PF_NightFactor) and the cloud cover; the renderer is off
    /// whenever the night factor is 0, so the day sky is never touched. The moon disc sits where the night light
    /// (TimeManager.sun at night) comes from. Added by bridge PrimalShaderBuilder.NightSky (arg "remove" deletes it);
    /// disabling this component or its object switches the stars off.
    /// </summary>
    public class NightSky : MonoBehaviour
    {
        [Tooltip("PF/Night Sky material (M_NightSky); empty = made at run time if the shader is in the build")]
        public Material material;
        [Tooltip("in-game hours for one full turn of the stars")] public float hoursPerTurn = 24f;
        public bool moon = true;

        MeshRenderer _r; Mesh _mesh; bool _ok;
        static readonly int RotId = Shader.PropertyToID("_PF_StarRotation"), MoonId = Shader.PropertyToID("_PF_MoonDir");

        void Start()
        {
            if (!material)
            {
                var sh = Shader.Find("PF/Night Sky");
                if (sh) material = new Material(sh) { name = "M_NightSky (runtime)" };
            }
            if (!material) { Debug.LogWarning("[NightSky] no PF/Night Sky material: stars off"); enabled = false; return; }
            var go = new GameObject("StarDome");
            go.transform.SetParent(transform, false);
            _mesh = BuildDome(32, 16);
            go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _r = go.AddComponent<MeshRenderer>();
            _r.sharedMaterial = material;
            _r.shadowCastingMode = ShadowCastingMode.Off; _r.receiveShadows = false;
            _r.lightProbeUsage = LightProbeUsage.Off; _r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion; _r.allowOcclusionWhenDynamic = false;
            _r.enabled = false;
            _ok = true;
        }

        void OnDisable() { if (_r) _r.enabled = false; }
        void OnDestroy() { if (_mesh) Destroy(_mesh); }

        void LateUpdate()
        {
            if (!_ok || !_r) return;
            var tm = TimeManager.Instance;
            float nf = tm ? tm.NightFactor : 0f;
            bool on = nf > 0.001f;
            if (_r.enabled != on) _r.enabled = on;
            if (!on) return;
            var cam = Camera.main;
            if (cam) _r.transform.position = cam.transform.position;      // bounds stay around the camera: never culled
            float turns = Mathf.Repeat((tm.day * 24f + tm.hour) / Mathf.Max(1f, hoursPerTurn), 1f);
            Shader.SetGlobalFloat(RotId, turns * Mathf.PI * 2f);
            // at night TimeManager turns the sun light into the moon light (lit from the moon's side)
            bool lightIsMoon = tm.hour < tm.sunriseHour || tm.hour > tm.sunsetHour;
            Vector4 md = Vector4.zero;
            if (moon && lightIsMoon && tm.sun)
            {
                Vector3 f = -tm.sun.transform.forward;
                md = new Vector4(f.x, f.y, f.z, 1f);
            }
            Shader.SetGlobalVector(MoonId, md);
        }

        /// <summary>unit UV sphere (only directions matter: the shader places it on the far plane)</summary>
        public static Mesh BuildDome(int seg, int rings)
        {
            var v = new Vector3[(seg + 1) * (rings + 1)];
            for (int r = 0, k = 0; r <= rings; r++)
            {
                float a = Mathf.PI * r / rings, y = Mathf.Cos(a), s = Mathf.Sin(a);
                for (int i = 0; i <= seg; i++, k++) { float b = Mathf.PI * 2f * i / seg; v[k] = new Vector3(Mathf.Cos(b) * s, y, Mathf.Sin(b) * s); }
            }
            var t = new int[seg * rings * 6];
            for (int r = 0, n = 0; r < rings; r++)
                for (int i = 0; i < seg; i++)
                {
                    int a = r * (seg + 1) + i, b = a + seg + 1;
                    t[n++] = a; t[n++] = b; t[n++] = a + 1;
                    t[n++] = a + 1; t[n++] = b; t[n++] = b + 1;
                }
            var m = new Mesh { name = "PF_StarDome", vertices = v, triangles = t };
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 20f);
            return m;
        }
    }
}
