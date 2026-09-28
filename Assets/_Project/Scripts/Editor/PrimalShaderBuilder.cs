using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PrimalFrontier.EditorTools
{
    /// <summary>
    /// Switches materials to the PRIMAL FRONTIER shaders (Assets/_Project/Shaders) after checking they compile.
    /// Every material is copied to Art/Materials/_Backup first (the original URP Lit set-up), so "restore" puts it back.
    /// Water: PF/Water on M_Ocean (the sea) with a generated tiling wave normal map; the pond / stream keep URP Lit.
    /// Tree / bush wind (PF/Foliage Wind) and the night sky: PrimalShaderBuilder.Wind.cs.
    /// </summary>
    public static partial class PrimalShaderBuilder
    {
        const string Mats = "Assets/_Project/Art/Materials", Backup = "Assets/_Project/Art/Materials/_Backup", Tex = "Assets/_Project/Art/Textures";
        static readonly StringBuilder Log = new StringBuilder();
        static void L(string s) { Log.AppendLine(s); Debug.Log("[PrimalShaders] " + s); }

        [MenuItem("Primal Frontier/Tools/Shaders: Water")] static void MenuWater() => EditorUtility.DisplayDialog("Primal Frontier", Water(""), "OK");
        [MenuItem("Primal Frontier/Tools/Shaders: Foliage Wind (experimental)")]
        static void MenuFoliage()
        {
            if (!EditorUtility.DisplayDialog("Primal Frontier", "Experimental: the editor crashed once right after switching the tree materials to PF/Foliage (cause not confirmed). Save the scene first. 'Shaders: Restore Backups' puts URP Lit back.", "Switch", "Cancel")) return;
            EditorUtility.DisplayDialog("Primal Frontier", Foliage("confirm"), "OK");
        }
        [MenuItem("Primal Frontier/Tools/Shaders: Restore Backups")] static void MenuRestore() => EditorUtility.DisplayDialog("Primal Frontier", Restore(""), "OK");

        static bool Ok(string shader, out Shader sh)
        {
            sh = Shader.Find(shader);
            if (!sh) { L(shader + ": not found"); return false; }
            var errs = ShaderUtil.GetShaderMessages(sh).Where(m => m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error).ToArray();
            if (errs.Length > 0 || ShaderUtil.ShaderHasError(sh)) { L($"{shader}: {errs.Length} compile errors, materials NOT switched: " + string.Join(" | ", errs.Take(3).Select(e => e.message + " line " + e.line))); return false; }
            return true;
        }

        static Material Keep(string name)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>($"{Mats}/{name}.mat");
            if (!m) { L("missing material " + name); return null; }
            Directory.CreateDirectory(Backup);
            string b = $"{Backup}/{name}.mat";
            if (!AssetDatabase.LoadAssetAtPath<Material>(b)) { AssetDatabase.CopyAsset($"{Mats}/{name}.mat", b); L($"backup {b}"); }
            return m;
        }

        [PrimalBridgeCommand]
        public static string Restore(string arg)
        {
            Log.Clear();
            if (!Directory.Exists(Backup)) return "no backups";
            foreach (var f in Directory.GetFiles(Backup, "*.mat"))
            {
                string name = Path.GetFileNameWithoutExtension(f);
                if (!string.IsNullOrEmpty(arg) && !arg.Split(',').Contains(name)) continue;
                var src = AssetDatabase.LoadAssetAtPath<Material>(f.Replace('\\', '/')); var dst = AssetDatabase.LoadAssetAtPath<Material>($"{Mats}/{name}.mat");
                if (src && dst) { dst.shader = src.shader; dst.CopyPropertiesFromMaterial(src); dst.shaderKeywords = src.shaderKeywords; dst.renderQueue = src.renderQueue; EditorUtility.SetDirty(dst); L("restored " + name); }
            }
            AssetDatabase.SaveAssets();
            return Log.ToString();
        }

        [PrimalBridgeCommand]
        public static string Water(string arg)
        {
            Log.Clear();
            if (!Ok("PF/Water", out var sh)) return Log.ToString();
            var n = WaveNormal();
            void Set(string name, Color shallow, Color deep, float tiling, float speed, float strength, float glint, float reflection)
            {
                var m = Keep(name); if (!m) return;
                m.shader = sh; m.shaderKeywords = new string[0]; m.renderQueue = -1;
                m.SetColor("_ShallowColor", shallow); m.SetColor("_DeepColor", deep); m.SetTexture("_NormalMap", n);
                m.SetFloat("_Tiling", tiling); m.SetFloat("_Speed", speed); m.SetFloat("_NormalScale", strength);
                m.SetFloat("_Smoothness", 0.92f); m.SetFloat("_FresnelPower", name == "M_Ocean" ? 4f : 5f); m.SetFloat("_ReflectionStrength", reflection); m.SetFloat("_SpecStrength", glint); m.SetFloat("_RainRipples", 0.6f);
                EditorUtility.SetDirty(m); L($"{name}: PF/Water");
            }
            Set("M_Ocean", new Color(0.07f, 0.3f, 0.34f, 0.7f), new Color(0.02f, 0.12f, 0.18f, 0.94f), 12f, 0.02f, 0.28f, 0.8f, 0.85f);
            // pond / stream: the URP Lit set-up reads better at close range (the sky-only reflection turns a small pond
            // grey-white at grazing angles); arg "fresh" switches it too
            if (arg == "fresh") Set("M_FreshWater", new Color(0.08f, 0.2f, 0.17f, 0.78f), new Color(0.03f, 0.09f, 0.08f, 0.94f), 4f, 0.015f, 0.16f, 0.6f, 0.45f);
            AssetDatabase.SaveAssets();
            return Log.ToString();
        }

        /// <summary>trees, palms, ferns (M_Foliage) and bark (M_Bark): PF/Foliage (URP Lit look + wind + rain wetness)</summary>
        [PrimalBridgeCommand]
        public static string Foliage(string arg)
        {
            Log.Clear();
            if (arg != "confirm") return "experimental: call with arg \"confirm\" (the editor crashed once right after this switch)";
            if (!Ok("PF/Foliage", out var sh)) return Log.ToString();
            void Set(string name, float sway, float height, float flutter, float wetDarken)
            {
                var m = Keep(name); if (!m) return;
                var kw = m.shaderKeywords; int q = m.renderQueue; float clip = m.HasProperty("_AlphaClip") ? m.GetFloat("_AlphaClip") : 0f; float cull = m.HasProperty("_Cull") ? m.GetFloat("_Cull") : 2f;
                m.shader = sh;
                m.shaderKeywords = kw; m.renderQueue = q;
                m.SetFloat("_AlphaClip", clip); m.SetFloat("_Cull", cull);
                if (clip > 0.5f) m.EnableKeyword("_ALPHATEST_ON"); else m.DisableKeyword("_ALPHATEST_ON");
                if (m.GetTexture("_BumpMap")) m.EnableKeyword("_NORMALMAP");
                m.SetFloat("_WindSway", sway); m.SetFloat("_WindHeight", height); m.SetFloat("_Flutter", flutter);
                m.SetFloat("_WetDarken", wetDarken); m.SetFloat("_WetShine", 0.5f);
                EditorUtility.SetDirty(m); L($"{name}: PF/Foliage (sway {sway} m, flutter {flutter} m)");
            }
            Set("M_Foliage", 0.28f, 9f, 0.045f, 0.3f);
            Set("M_Bark", 0.28f, 9f, 0f, 0.35f);
            AssetDatabase.SaveAssets();
            return Log.ToString();
        }

        /// <summary>tiling wave normal map: a sum of waves with whole-number frequencies (so it repeats seamlessly)</summary>
        static Texture2D WaveNormal()
        {
            string p = $"{Tex}/T_WaterNormal.png";
            const int N = 256;
            // ~48 waves in random directions, amplitude falling with frequency: reads as natural chop, no visible rows
            var rnd = new System.Random(1234);
            var list = new System.Collections.Generic.List<(int kx, int ky, float a, float ph)>();
            while (list.Count < 48)
            {
                int kx = rnd.Next(-14, 15), ky = rnd.Next(-14, 15); float k = Mathf.Sqrt(kx * kx + ky * ky);
                if (k < 2f || k > 14f) continue;
                list.Add((kx, ky, 1f / Mathf.Pow(k, 1.3f), (float)rnd.NextDouble() * 6.283f));
            }
            var waves = list.ToArray();
            float H(float u, float v) { float h = 0f; foreach (var w in waves) h += w.a * Mathf.Sin(2f * Mathf.PI * (w.kx * u + w.ky * v) + w.ph); return h; }
            var tex = new Texture2D(N, N, TextureFormat.RGB24, false); var px = new Color[N * N];
            float e = 1f / N, s = 6f;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (float)x / N, v = (float)y / N;
                    float dx = (H(u + e, v) - H(u - e, v)) / (2f * e), dy = (H(u, v + e) - H(u, v - e)) / (2f * e);
                    var nn = new Vector3(-dx * 0.06f, -dy * 0.06f, 1f).normalized;
                    px[y * N + x] = new Color(nn.x * 0.5f + 0.5f, nn.y * 0.5f + 0.5f, nn.z * 0.5f + 0.5f);
                }
            tex.SetPixels(px); tex.Apply();
            File.WriteAllBytes(p, tex.EncodeToPNG()); Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
            var ti = (TextureImporter)AssetImporter.GetAtPath(p);
            ti.textureType = TextureImporterType.NormalMap; ti.wrapMode = TextureWrapMode.Repeat; ti.mipmapEnabled = true; ti.maxTextureSize = 256; ti.SaveAndReimport();
            L("texture " + p);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
        }
    }
}
