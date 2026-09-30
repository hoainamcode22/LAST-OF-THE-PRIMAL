using UnityEngine;

namespace PrimalFrontier.Core
{
    public enum BloodLevel { Off = 0, Reduced = 1, Normal = 2 }

    /// <summary>Player preferences (PlayerPrefs): volumes, mouse, quality preset, resolution, fullscreen, VSync, hints.</summary>
    public static class GameSettings
    {
        public static float Master { get => PlayerPrefs.GetFloat("pf_master", 0.9f); set { PlayerPrefs.SetFloat("pf_master", value); Apply(); } }
        public static float Music { get => PlayerPrefs.GetFloat("pf_music", 0.7f); set { PlayerPrefs.SetFloat("pf_music", value); Apply(); } }
        public static float Sfx { get => PlayerPrefs.GetFloat("pf_sfx", 1f); set { PlayerPrefs.SetFloat("pf_sfx", value); Apply(); } }
        public static float Ambience { get => PlayerPrefs.GetFloat("pf_amb", 0.9f); set { PlayerPrefs.SetFloat("pf_amb", value); Apply(); } }
        public static float MouseSensitivity { get => PlayerPrefs.GetFloat("pf_mouse", 0.12f); set { PlayerPrefs.SetFloat("pf_mouse", value); Apply(); } }
        public static bool InvertY { get => PlayerPrefs.GetInt("pf_invy", 0) == 1; set { PlayerPrefs.SetInt("pf_invy", value ? 1 : 0); Apply(); } }
        public static int Quality { get => PlayerPrefs.GetInt("pf_quality", Application.isMobilePlatform ? 0 : 2); set { PlayerPrefs.SetInt("pf_quality", value); ApplyQuality(); } }
        public static bool VSync { get => PlayerPrefs.GetInt("pf_vsync", 1) == 1; set { PlayerPrefs.SetInt("pf_vsync", value ? 1 : 0); ApplyQuality(); } }
        public static bool Fullscreen { get => UnityEngine.Screen.fullScreen; set { UnityEngine.Screen.fullScreen = value; } }
        public static bool Hints { get => PlayerPrefs.GetInt("pf_hints", 1) == 1; set => PlayerPrefs.SetInt("pf_hints", value ? 1 : 0); }
        /// <summary>blood effects: Off (dust puffs instead), Reduced (small sprays, no pools / trails), Normal</summary>
        /// <summary>on-screen touch controls, mobile builds only (MobileHUD.Supported): 0 automatic (touch devices), 1 always, 2 never. No Settings row on PC.</summary>
        public static int TouchControls { get => PlayerPrefs.GetInt("pf_touch", 0); set => PlayerPrefs.SetInt("pf_touch", Mathf.Clamp(value, 0, 2)); }
        public static readonly string[] TouchNames = { "AUTO", "ON", "OFF" };
        /// <summary>stealth indicator (eye / noise ring / threat markers): 0 automatic (crouched or a predator near), 1 always, 2 off</summary>
        public static int StealthHud { get => PlayerPrefs.GetInt("pf_stealthhud", 0); set => PlayerPrefs.SetInt("pf_stealthhud", Mathf.Clamp(value, 0, 2)); }
        public static readonly string[] StealthHudNames = { "AUTO", "ALWAYS", "OFF" };
        public static BloodLevel Blood { get => (BloodLevel)Mathf.Clamp(PlayerPrefs.GetInt("pf_blood", (int)BloodLevel.Normal), 0, 2); set => PlayerPrefs.SetInt("pf_blood", (int)value); }
        public static readonly string[] BloodNames = { "OFF", "REDUCED", "NORMAL" };
        public static readonly string[] QualityNames = { "Low", "Medium", "High", "Ultra" };

        public static void Apply()
        {
            AudioListener.volume = Master;
            AudioBus.Sfx = Sfx; AudioBus.Ambience = Ambience; AudioBus.Music = Music;
            var input = Player.PlayerInputReader.Instance;
            if (input) { input.mouseSensitivity = MouseSensitivity; input.invertY = InvertY; }
        }

        /// <summary>maps Low/Medium/High/Ultra onto the project's quality levels (whatever count it has)</summary>
        public static void ApplyQuality()
        {
            int n = QualitySettings.names.Length;
            int lvl = Mathf.Clamp(Mathf.RoundToInt(Quality / 3f * (n - 1)), 0, n - 1);
            QualitySettings.SetQualityLevel(lvl, true);
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            Application.targetFrameRate = Application.isMobilePlatform ? (Quality >= 2 ? 60 : 30) : VSync ? -1 : 144;
            TerrainQuality.Apply(Quality);
            ApplyRenderPreset(Mathf.Clamp(Quality, 0, 3));
        }

        // ---- per preset render cost (Low / Medium / High / Ultra) on top of the Mobile / PC quality level
        static readonly float[] RenderScale = { 0.75f, 0.9f, 1f, 1f };
        static readonly float[] ShadowDistance = { 30f, 45f, 70f, 110f };
        static readonly int[] Msaa = { 1, 2, 4, 4 };
        static readonly int[] Cascades = { 1, 2, 2, 4 };
        static readonly float[] LodBias = { 0.7f, 1.0f, 1.5f, 2.0f };
        static readonly int[] MaxEffects = { 24, 40, 64, 96 };
        /// <summary>how many pooled particle effects may play at once for the current preset</summary>
        public static int MaxVfx => MaxEffects[Mathf.Clamp(Quality, 0, 3)];
        // one private copy of the pipeline asset per quality level (made once, changed on every apply)
        static readonly System.Collections.Generic.Dictionary<int, UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset> _rpCopy =
            new System.Collections.Generic.Dictionary<int, UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset>();

        /// <summary>
        /// Render scale, shadow distance / cascades, MSAA and LOD bias per preset. Builds only: in the editor these
        /// would be written into the project's pipeline / quality assets, so the editor keeps the asset values.
        /// A private copy of the pipeline asset is changed, never the asset on disk.
        /// </summary>
        static void ApplyRenderPreset(int q)
        {
            if (Application.isEditor) return;
            QualitySettings.lodBias = LodBias[q];
            int lvl = QualitySettings.GetQualityLevel();
            if (!_rpCopy.TryGetValue(lvl, out var copy) || !copy)
            {
                var src = QualitySettings.renderPipeline != null ? QualitySettings.renderPipeline : UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;
                if (!(src is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp)) return;
                copy = Object.Instantiate(urp); copy.name = urp.name + " (runtime)";
                _rpCopy[lvl] = copy;
            }
            copy.renderScale = RenderScale[q];
            copy.shadowDistance = ShadowDistance[q];
            copy.shadowCascadeCount = Cascades[q];
            copy.msaaSampleCount = Msaa[q];
            if (QualitySettings.renderPipeline != copy) QualitySettings.renderPipeline = copy;
        }

        public static void CycleResolution()
        {
            var rs = UnityEngine.Screen.resolutions; if (rs.Length == 0) return;
            var cur = UnityEngine.Screen.currentResolution; int idx = 0;
            for (int i = 0; i < rs.Length; i++) if (rs[i].width == UnityEngine.Screen.width && rs[i].height == UnityEngine.Screen.height) idx = i;
            var r = rs[(idx + 1) % rs.Length];
            UnityEngine.Screen.SetResolution(r.width, r.height, UnityEngine.Screen.fullScreenMode);
        }
    }
}
