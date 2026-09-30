using UnityEngine;
using UnityEngine.Rendering;
using PrimalFrontier.Core;

namespace PrimalFrontier.VFX
{
    /// <summary>
    /// Weather clouds over the procedural sky (the sky itself cannot show clouds): a camera-centred dome with the
    /// PF/Cloud Veil material. Cover, drift, storm darkness and lightning come from the global values WeatherManager sets
    /// (_PF_Overcast, _PF_Wind, _PF_StormK, _PF_Flash); the colour follows the fog colour of the hour. The renderer is off
    /// under a clear sky. Placed by PrimalAtmosphereBuilder under [Atmosphere]/Sky; disable it to switch the clouds off.
    /// </summary>
    public class CloudVeil : MonoBehaviour
    {
        [Tooltip("PF/Cloud Veil material (M_CloudVeil); empty = made at run time if the shader is in the build")]
        public Material material;
        MeshRenderer _r; Mesh _mesh; bool _ok;
        static readonly int OvercastId = Shader.PropertyToID("_PF_Overcast");

        void Start()
        {
            if (!material)
            {
                var sh = Shader.Find("PF/Cloud Veil");
                if (sh) material = new Material(sh) { name = "M_CloudVeil (runtime)" };
            }
            if (!material) { Debug.LogWarning("[CloudVeil] no PF/Cloud Veil material: clouds off"); enabled = false; return; }
            var go = new GameObject("CloudDome");
            go.transform.SetParent(transform, false);
            _mesh = NightSky.BuildDome(32, 16); _mesh.name = "PF_CloudDome";
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
            var wm = WeatherManager.Instance;
            float cover = wm ? wm.Overcast : Shader.GetGlobalFloat(OvercastId);
            bool on = cover > 0.01f;
            if (_r.enabled != on) _r.enabled = on;
            if (!on) return;
            var cam = Camera.main;
            if (cam) _r.transform.position = cam.transform.position;
        }
    }
}
