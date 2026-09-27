using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PrimalFrontier.EditorTools
{
    /// <summary>
    /// Phase 16 performance pass, repeatable: phone texture overrides (ASTC, size caps per asset family), audio import
    /// (mono / Vorbis / load type by length), GPU instancing on every material, render pipeline shadow distance per
    /// quality level, build scene list, player settings for Android (IL2CPP ARM64, GPU skinning). Writes
    /// Documentation/Performance_Optimization.md.
    /// batch: -executeMethod PrimalFrontier.EditorTools.PrimalOptimizer.RunFromCommandLine
    /// </summary>
    public static class PrimalOptimizer
    {
        static readonly StringBuilder Log = new StringBuilder();
        static void L(string s) { Log.AppendLine(s); Debug.Log("[PrimalOptimizer] " + s); }

        public static void RunFromCommandLine()
        {
            int code = 0;
            try { Run(); }
            catch (Exception e) { Debug.LogError("[PrimalOptimizer] " + e); code = 1; }
            EditorApplication.Exit(code);
        }

        [MenuItem("Primal Frontier/Optimize/Run Performance Pass")]
        public static void Run()
        {
            Log.Clear();
            L($"# Performance optimization pass ({DateTime.Now:yyyy-MM-dd HH:mm})\n");
            AssetDatabase.StartAssetEditing();
            try { Textures(); Audio(); }
            finally { AssetDatabase.StopAssetEditing(); }
            Materials();
            Pipeline();
            BuildScenes();
            Player();
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Documentation");
            File.WriteAllText("Documentation/Performance_Optimization.md", Log.ToString());
        }

        // ------------------------------------------------------------------ textures
        static int MobileCap(string path, TextureImporter ti)
        {
            string p = path.Replace('\\', '/');
            if (p.Contains("/UI/") || ti.textureType == TextureImporterType.Sprite) return 1024;
            if (p.Contains("/VFX/")) return 256;
            if (p.Contains("_Eye_")) return 256;
            if (p.Contains("_Mouth_")) return 512;
            if (p.Contains("/Characters/")) return 1024;
            if (p.Contains("/Terrain/")) return 1024;
            if (p.Contains("/Icons/")) return 256;
            return 512;                                                   // props, rocks, plants
        }

        static void Textures()
        {
            int changed = 0, total = 0; long pcBytes = 0, mobileBytes = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Art", "Assets/_Project" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var ti = AssetImporter.GetAtPath(path) as TextureImporter; if (ti == null) continue;
                total++;
                int cap = Mathf.Min(MobileCap(path, ti), ti.maxTextureSize);
                var fmt = ti.textureType == TextureImporterType.Sprite || path.Contains("/UI/") ? TextureImporterFormat.ASTC_4x4 : TextureImporterFormat.ASTC_6x6;
                bool dirty = false;
                foreach (var plat in new[] { "Android", "iPhone" })
                {
                    var ps = ti.GetPlatformTextureSettings(plat);
                    if (!ps.overridden || ps.maxTextureSize != cap || ps.format != fmt)
                    { ps.overridden = true; ps.maxTextureSize = cap; ps.format = fmt; ps.compressionQuality = 50; ti.SetPlatformTextureSettings(ps); dirty = true; }
                }
                if (ti.textureType != TextureImporterType.Sprite && !ti.mipmapEnabled && !path.Contains("/UI/")) { ti.mipmapEnabled = true; dirty = true; }
                if (dirty) { ti.SaveAndReimport(); changed++; }
                int pc = ti.maxTextureSize; pcBytes += (long)pc * pc; mobileBytes += (long)cap * cap;
            }
            L($"## Textures\n- {total} textures, {changed} updated; phone overrides ASTC 6x6 (UI ASTC 4x4)");
            L($"- caps on phones: characters / terrain 1024, mouth 512, eyes / VFX / icons 256, props 512");
            L($"- texel budget: PC {pcBytes / 1e6:F0} Mpx max, phone {mobileBytes / 1e6:F0} Mpx max (before compression)\n");
        }

        // ------------------------------------------------------------------ audio
        static void Audio()
        {
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/_Project/Audio" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var ai = AssetImporter.GetAtPath(path) as AudioImporter; if (ai == null) continue;
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path); float len = clip ? clip.length : 1f;
                bool music = path.Contains("/Music/"), amb = path.Contains("/Ambience/") || len > 12f;
                var s = ai.defaultSampleSettings;
                s.compressionFormat = AudioCompressionFormat.Vorbis; s.quality = music || amb ? 0.5f : 0.4f;
                s.loadType = music ? AudioClipLoadType.Streaming : amb ? AudioClipLoadType.CompressedInMemory : len < 3f ? AudioClipLoadType.DecompressOnLoad : AudioClipLoadType.CompressedInMemory;
                s.preloadAudioData = !music;
                ai.defaultSampleSettings = s;
                ai.forceToMono = !(music || amb);
                ai.loadInBackground = music || amb;
                ai.SaveAndReimport(); n++;
            }
            L($"## Audio\n- {n} clips: Vorbis; effects mono + decompress on load (< 3 s) or compressed in memory; ambience compressed in memory; music streamed\n");
        }

        // ------------------------------------------------------------------ materials
        static void Materials()
        {
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/Art", "Assets/_Project" }))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (m == null || m.enableInstancing || m.shader == null || !m.shader.name.StartsWith("Universal Render Pipeline")) continue;
                m.enableInstancing = true; EditorUtility.SetDirty(m); n++;
            }
            L($"## Materials\n- GPU instancing enabled on {n} more URP materials (SRP batcher stays on)\n");
        }

        // ------------------------------------------------------------------ render pipeline per quality level
        static void Pipeline()
        {
            L("## Render pipeline");
            var names = QualitySettings.names;
            var seen = new HashSet<UniversalRenderPipelineAsset>();
            for (int i = 0; i < names.Length; i++)
            {
                var rp = QualitySettings.GetRenderPipelineAssetAt(i) as UniversalRenderPipelineAsset;
                if (rp == null || !seen.Add(rp)) continue;
                bool mobile = names[i].ToLowerInvariant().Contains("mobile") || rp.name.Contains("Mobile");
                rp.shadowDistance = mobile ? 45f : 120f;
                rp.shadowCascadeCount = mobile ? 1 : 3;
                rp.msaaSampleCount = mobile ? 1 : 4;
                rp.renderScale = mobile ? 0.9f : 1f;
                rp.supportsHDR = !mobile;
                EditorUtility.SetDirty(rp);
                L($"- {names[i]} ({rp.name}): shadows {rp.shadowDistance} m / {rp.shadowCascadeCount} cascade(s), MSAA x{rp.msaaSampleCount}, render scale {rp.renderScale}, HDR {(rp.supportsHDR ? "on" : "off")}");
            }
            // LOD bias: big creatures should keep their detailed mesh a little longer on PC
            var so = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
            var levels = so.FindProperty("m_QualitySettings");
            for (int i = 0; i < levels.arraySize; i++)
            {
                var lv = levels.GetArrayElementAtIndex(i); bool mobile = lv.FindPropertyRelative("name").stringValue.ToLowerInvariant().Contains("mobile");
                lv.FindPropertyRelative("lodBias").floatValue = mobile ? 0.9f : 1.6f;
                lv.FindPropertyRelative("skinWeights").intValue = mobile ? 2 : 4;
                lv.FindPropertyRelative("particleRaycastBudget").intValue = mobile ? 64 : 256;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            L("- LOD bias 0.9 phone / 1.6 PC, skin weights 2 / 4 bones, particle raycast budget 64 / 256\n");
        }

        static void BuildScenes()
        {
            const string island = "Assets/_Project/Scenes/Island_VerticalSlice.unity";
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(island, true) };
            L($"## Build\n- build scene list: {island} only (template sample scene removed from builds)");
        }

        static void Player()
        {
            PlayerSettings.gpuSkinning = true;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Low);
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Standalone, ManagedStrippingLevel.Disabled);
            L("- GPU skinning on; Android: IL2CPP, ARM64, managed stripping low\n");
        }
    }
}
