using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using PrimalFrontier.Core;

namespace PrimalFrontier.VFX
{
    /// <summary>
    /// Global values for the water shaders (PF/Ocean, PF/Ocean Shore, PF/Water Flow), pushed every frame:
    /// _PF_Rain = WeatherManager rain intensity (rain rings; wetness lingers after the rain, so it is not used),
    /// _PF_WaterSceneDepth / _PF_WaterRefraction = 1 only when the active URP asset (and the main camera) produce the
    /// depth / opaque texture (PC quality). Without this component every value stays 0: no rain rings, and the shaders
    /// use only their baked maps, which is the safe mobile path. Runs in edit mode too so the Scene view matches.
    /// Added to the "Water" root by bridge PrimalWaterBuilder.Build, removed by PrimalWaterBuilder.Revert.
    /// </summary>
    [ExecuteAlways]
    public class WaterGlobals : MonoBehaviour
    {
        [Tooltip("use the camera depth texture when the pipeline has it (soft edges against rocks and props)")] public bool useSceneDepth = true;
        [Tooltip("use the camera opaque texture when the pipeline has it (refraction of the bed)")] public bool useRefraction = true;
        [Tooltip("scale of the rain rings (1 = follow the weather)")] [Range(0f, 2f)] public float rainScale = 1f;

        static readonly int RainId = Shader.PropertyToID("_PF_Rain"), DepthId = Shader.PropertyToID("_PF_WaterSceneDepth"),
                            RefractId = Shader.PropertyToID("_PF_WaterRefraction");

        void OnEnable() { Push(); }
        void Update() { Push(); }

        void OnDisable()
        {
            Shader.SetGlobalFloat(RainId, 0f); Shader.SetGlobalFloat(DepthId, 0f); Shader.SetGlobalFloat(RefractId, 0f);
        }

        void Push()
        {
            var wm = WeatherManager.Instance;
            float rain = Application.isPlaying && wm ? Mathf.Clamp01(wm.Intensity * rainScale) : 0f;
            bool depth = false, opaque = false;
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp) { depth = urp.supportsCameraDepthTexture; opaque = urp.supportsCameraOpaqueTexture; }
            var cam = Camera.main;
            if (urp && cam && cam.TryGetComponent<UniversalAdditionalCameraData>(out var data))
            {
                depth = data.requiresDepthTexture;
                opaque = data.requiresColorTexture;
            }
            // set every frame (three floats): an edit-mode preview may have changed them in between
            Shader.SetGlobalFloat(RainId, rain);
            Shader.SetGlobalFloat(DepthId, useSceneDepth && depth ? 1f : 0f);
            Shader.SetGlobalFloat(RefractId, useRefraction && opaque ? 1f : 0f);
        }
    }
}
