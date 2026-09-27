using UnityEngine;

namespace PrimalFrontier.Core
{
    /// <summary>Player preferences (PlayerPrefs): volumes, mouse, quality preset, resolution, fullscreen, VSync, hints.</summary>
    public static class GameSettings
    {
        public static float Master { get => PlayerPrefs.GetFloat("pf_master", 0.9f); set { PlayerPrefs.SetFloat("pf_master", value); Apply(); } }
        public static float Music { get => PlayerPrefs.GetFloat("pf_music", 0.7f); set { PlayerPrefs.SetFloat("pf_music", value); Apply(); } }
        public static float Sfx { get => PlayerPrefs.GetFloat("pf_sfx", 1f); set { PlayerPrefs.SetFloat("pf_sfx", value); Apply(); } }
        public static float Ambience { get => PlayerPrefs.GetFloat("pf_amb", 0.9f); set { PlayerPrefs.SetFloat("pf_amb", value); Apply(); } }
        public static float MouseSensitivity { get => PlayerPrefs.GetFloat("pf_mouse", 0.12f); set { PlayerPrefs.SetFloat("pf_mouse", value); Apply(); } }
        public static bool InvertY { get => PlayerPrefs.GetInt("pf_invy", 0) == 1; set { PlayerPrefs.SetInt("pf_invy", value ? 1 : 0); Apply(); } }
        public static int Quality { get => PlayerPrefs.GetInt("pf_quality", Mathf.Clamp(QualitySettings.GetQualityLevel(), 0, 3)); set { PlayerPrefs.SetInt("pf_quality", value); ApplyQuality(); } }
        public static bool VSync { get => PlayerPrefs.GetInt("pf_vsync", 1) == 1; set { PlayerPrefs.SetInt("pf_vsync", value ? 1 : 0); ApplyQuality(); } }
        public static bool Fullscreen { get => UnityEngine.Screen.fullScreen; set { UnityEngine.Screen.fullScreen = value; } }
        public static bool Hints { get => PlayerPrefs.GetInt("pf_hints", 1) == 1; set => PlayerPrefs.SetInt("pf_hints", value ? 1 : 0); }
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
            Application.targetFrameRate = VSync ? -1 : 144;
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
