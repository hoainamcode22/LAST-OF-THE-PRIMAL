using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using PrimalFrontier.Audio;

namespace PrimalFrontier.EditorTools
{
    /// <summary>Imports the synthesized WAVs (Tools/Audio/sfx_synth.py) with mobile-friendly settings and builds Resources/SfxLibrary.</summary>
    public static class PrimalAudioBuilder
    {
        const string SfxDir = "Assets/_Project/Audio/SFX", AmbDir = "Assets/_Project/Audio/Ambience";

        static readonly Dictionary<SfxId, (float vol, float jitter, float dist)> Mix = new Dictionary<SfxId, (float, float, float)>
        {
            { SfxId.FootSand, (0.45f, 0.08f, 12f) }, { SfxId.FootDirt, (0.45f, 0.08f, 12f) }, { SfxId.FootRock, (0.4f, 0.1f, 12f) }, { SfxId.FootMud, (0.5f, 0.08f, 12f) },
            { SfxId.FootWater, (0.5f, 0.1f, 14f) }, { SfxId.Land, (0.7f, 0.05f, 16f) }, { SfxId.HitFlesh, (0.9f, 0.08f, 20f) }, { SfxId.HitHeavy, (1f, 0.06f, 25f) },
            { SfxId.HurtGrunt, (0.7f, 0.05f, 18f) }, { SfxId.Death, (0.8f, 0f, 20f) }, { SfxId.WoodChop, (0.85f, 0.08f, 30f) }, { SfxId.StoneHit, (0.8f, 0.08f, 30f) },
            { SfxId.LeafRustle, (0.55f, 0.1f, 15f) }, { SfxId.Pickup, (0.5f, 0.1f, 10f) }, { SfxId.Craft, (0.6f, 0.08f, 12f) }, { SfxId.CraftKnap, (0.6f, 0.1f, 15f) },
            { SfxId.Build, (0.8f, 0.08f, 25f) }, { SfxId.Eat, (0.6f, 0.08f, 8f) }, { SfxId.Drink, (0.6f, 0.05f, 8f) }, { SfxId.WaterSplash, (0.7f, 0.08f, 20f) },
            { SfxId.FireIgnite, (0.8f, 0.03f, 20f) }, { SfxId.FireExtinguish, (0.8f, 0.03f, 20f) }, { SfxId.Sizzle, (0.5f, 0.05f, 10f) },
            { SfxId.SpearWhoosh, (0.6f, 0.1f, 15f) }, { SfxId.SpearImpact, (0.9f, 0.08f, 25f) }, { SfxId.BowDraw, (0.6f, 0.03f, 10f) }, { SfxId.BowRelease, (0.8f, 0.05f, 20f) },
            { SfxId.ArrowImpact, (0.8f, 0.08f, 25f) }, { SfxId.UiClick, (0.5f, 0.05f, 5f) }, { SfxId.UiObjective, (0.55f, 0f, 5f) }, { SfxId.UiRecipe, (0.55f, 0f, 5f) },
            { SfxId.DinoStep, (1f, 0.06f, 60f) }, { SfxId.DinoStepHeavy, (1f, 0.05f, 90f) },
            // phase 3 / 3.5 (sfx_synth.py phase3): subtle, short range; the punch hit is the loudest of the set
            { SfxId.PunchWhoosh, (0.5f, 0.1f, 12f) }, { SfxId.PunchHit, (0.85f, 0.08f, 18f) }, { SfxId.PunchHeavyHit, (0.95f, 0.06f, 22f) },
            { SfxId.PlayerGrunt, (0.55f, 0.06f, 12f) }, { SfxId.WaterFill, (0.55f, 0.05f, 10f) }, { SfxId.WaterBoil, (0.45f, 0.02f, 10f) },
            { SfxId.BandageWrap, (0.5f, 0.06f, 8f) }, { SfxId.ToolBreak, (0.8f, 0.05f, 15f) }, { SfxId.FireHiss, (0.6f, 0.05f, 18f) },
            { SfxId.BranchSnap, (0.7f, 0.1f, 18f) }, { SfxId.StoneGatherHand, (0.6f, 0.1f, 12f) },
            // PC phase (sfx_synth.py pcphase): quiet body sounds, footsteps as loud as the other surfaces
            { SfxId.FootWood, (0.45f, 0.08f, 12f) }, { SfxId.FootGrass, (0.4f, 0.1f, 10f) }, { SfxId.Chew, (0.4f, 0.08f, 5f) },
            { SfxId.WaterScoop, (0.55f, 0.08f, 10f) }, { SfxId.ClimbGrab, (0.55f, 0.1f, 10f) }, { SfxId.ClimbScrape, (0.45f, 0.1f, 10f) },
        };

        /// <summary>bridge: PrimalAudioBuilder.BuildLibrary (import settings for new WAVs + Resources/SfxLibrary)</summary>
        [PrimalBridgeCommand]
        public static string BuildLibrary()
        {
            Build();
            var lib = AssetDatabase.LoadAssetAtPath<SfxLibrary>("Assets/_Project/Resources/SfxLibrary.asset");
            if (!lib) return "no library";
            var missing = Enum.GetValues(typeof(SfxId)).Cast<SfxId>().Where(i => i != SfxId.None && !lib.entries.Any(e => e.id == i)).ToList();
            return $"{lib.entries.Count} ids, {lib.entries.Sum(e => e.clips.Length)} clips; missing: {(missing.Count == 0 ? "none" : string.Join(", ", missing))}";
        }

        public static void Build()
        {
            var files = Directory.GetFiles(SfxDir, "*.wav").Select(p => p.Replace('\\', '/')).ToList();
            foreach (var p in files) Configure(p, false);
            foreach (var p in Directory.GetFiles(AmbDir, "*.wav")) Configure(p.Replace('\\', '/'), true);
            var lib = ScriptableObject.CreateInstance<SfxLibrary>();
            foreach (SfxId id in Enum.GetValues(typeof(SfxId)))
            {
                if (id == SfxId.None) continue;
                var clips = files.Where(f => Path.GetFileNameWithoutExtension(f).StartsWith($"SFX_{id}_")).OrderBy(f => f)
                                 .Select(f => AssetDatabase.LoadAssetAtPath<AudioClip>(f)).Where(c => c != null).ToArray();
                if (clips.Length == 0) { Debug.LogWarning("[PrimalAudioBuilder] no clips for " + id); continue; }
                var m = Mix.TryGetValue(id, out var v) ? v : (0.8f, 0.05f, 25f);
                lib.entries.Add(new SfxLibrary.Entry { id = id, clips = clips, volume = m.Item1, pitchJitter = m.Item2, maxDistance = m.Item3 });
            }
            const string path = "Assets/_Project/Resources/SfxLibrary.asset";
            Directory.CreateDirectory("Assets/_Project/Resources");
            AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(lib, path); AssetDatabase.SaveAssets();
            Debug.Log($"[PrimalAudioBuilder] {lib.entries.Count} sfx ids, {files.Count} clips -> {path}");
        }

        static void Configure(string path, bool loop)
        {
            var ai = AssetImporter.GetAtPath(path) as AudioImporter;
            if (ai == null) { AssetDatabase.ImportAsset(path); ai = (AudioImporter)AssetImporter.GetAtPath(path); }
            var s = ai.defaultSampleSettings;
            var want = s;
            want.compressionFormat = AudioCompressionFormat.Vorbis; want.quality = loop ? 0.45f : 0.6f;
            want.loadType = loop ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            want.sampleRateSetting = AudioSampleRateSetting.OptimizeSampleRate;
            // already set up: no reimport (the library build stays quick with 100+ clips)
            if (ai.forceToMono && ai.loadInBackground == loop && s.compressionFormat == want.compressionFormat && Mathf.Approximately(s.quality, want.quality)
                && s.loadType == want.loadType && s.sampleRateSetting == want.sampleRateSetting) return;
            ai.forceToMono = true; ai.loadInBackground = loop;
            ai.defaultSampleSettings = want;
            ai.SaveAndReimport();
        }
    }

    /// <summary>batch: builds VFX + audio libraries and the gameplay player prefab in one Unity run</summary>
    public static class PrimalPresentationBuilder
    {
        public static void BuildFromCommandLine()
        {
            int code = 0;
            try
            {
                PrimalVfxBuilder.Build();
                PrimalAudioBuilder.Build();
                if (PrimalPlayerSetup.BuildGameplayPrefab() == null) code = 2;
                PrimalPlayerSetup.BuildTestScene();
                AssetDatabase.SaveAssets();
            }
            catch (Exception e) { Debug.LogError("[PrimalPresentationBuilder] " + e); code = 1; }
            EditorApplication.Exit(code);
        }
    }
}
