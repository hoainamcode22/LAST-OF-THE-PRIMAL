using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using PrimalFrontier.Core;
using PrimalFrontier.Player;

namespace PrimalFrontier.Tests
{
    /// <summary>
    /// Vegetation wind (PF/Foliage Wind): the wind materials have a real amplitude, the split left the shared materials
    /// (logs, ferns, nodes) on their own shader, and a tree prototype of the island really moves: rendered twice 0.6 s
    /// apart with a strong wind, its pixels differ, while the same tree with the sway zeroed (property block) does not.
    /// Ignored when no wind material exists (wind never applied, or reverted).
    /// </summary>
    public class WindTests
    {
        const string WindShader = "PF/Foliage Wind", CopyTag = "PF_WindCopyOf";
        static readonly int WindId = Shader.PropertyToID("_PF_Wind"), SwayId = Shader.PropertyToID("_WindSway"), FlutterId = Shader.PropertyToID("_Flutter");

        [TearDown] public void Cleanup() { GameManager.ForceShowTitle = null; GameManager.ForcePlayIntro = null; PlayerInputReader.Simulate = false; Time.timeScale = 1f; }
        [UnityTearDown] public IEnumerator Unload() { yield return TestScenes.ClearIfGameplayLeft(); }

#if UNITY_EDITOR
        static List<Material> WindMaterials() =>
            UnityEditor.AssetDatabase.FindAssets("t:Material", new[] { "Assets/_Project" }).Select(UnityEditor.AssetDatabase.GUIDToAssetPath)
                .Where(p => !p.Contains("/_WindTest/")).Select(UnityEditor.AssetDatabase.LoadAssetAtPath<Material>)
                .Where(m => m && m.shader && m.shader.name == WindShader).ToList();
#endif

        [UnityTest] public IEnumerator Wind_Materials_Have_Amplitude_And_A_Tree_Moves()
        {
#if !UNITY_EDITOR
            Assert.Ignore("editor only (reads the material assets)");
            yield break;
#else
            Assert.IsNotNull(Shader.Find(WindShader), WindShader + " shader");
            var wind = WindMaterials();
            if (wind.Count == 0) Assert.Ignore("no material on " + WindShader + ": wind not applied (bridge PrimalShaderBuilder.WindSplit)");
            foreach (var m in wind) Assert.Greater(m.GetFloat(SwayId), 0f, m.name + ": wind sway amplitude");
            // split: the source of every copy is back on its own shader (fallen logs, ferns, nodes stay still)
            foreach (var m in wind)
            {
                string src = m.GetTag(CopyTag, false, "");
                if (string.IsNullOrEmpty(src) || src.Contains("#")) continue;
                var s = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(src);
                if (s) Assert.AreNotEqual(WindShader, s.shader ? s.shader.name : "", $"{s.name} (shared) must not sway after the split");
            }

            TestScenes.UseTestSaves(); GameManager.ForceShowTitle = false; GameManager.ForcePlayIntro = false; PlayerInputReader.Simulate = true;
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Project/Scenes/Island_VerticalSlice.unity", new LoadSceneParameters(LoadSceneMode.Single));
            float t = 0; while ((GameManager.Instance == null || GameManager.Instance.State != GameState.Playing) && t < 20f) { t += Time.unscaledDeltaTime; yield return null; }
            var terrain = Terrain.activeTerrain;
            Assert.IsNotNull(terrain, "island terrain");
            var proto = terrain.terrainData.treePrototypes.Select(p => p.prefab)
                .FirstOrDefault(p => p && p.GetComponentsInChildren<Renderer>(true).Any(r => r.sharedMaterials.Any(m => m && m.shader && m.shader.name == WindShader)));
            Assert.IsNotNull(proto, "wind materials exist but no terrain tree prototype uses one (run WindSplit)");

            // the tree alone, high above the island, on a layer nothing else uses, seen by its own camera
            int layer = Enumerable.Range(8, 24).Reverse().FirstOrDefault(i => string.IsNullOrEmpty(LayerMask.LayerToName(i)));
            Assert.Greater(layer, 0, "a free layer");
            var tm = TimeManager.Instance; bool paused = tm && tm.paused; if (tm) tm.paused = true;     // steady light
            var tree = Object.Instantiate(proto, terrain.transform.position + new Vector3(terrain.terrainData.size.x * 0.5f, 400f, terrain.terrainData.size.z * 0.5f), Quaternion.identity);
            foreach (var tr in tree.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = layer;
            var rends = tree.GetComponentsInChildren<Renderer>(true);
            var b = rends[0].bounds; foreach (var r in rends) b.Encapsulate(r.bounds);
            var camGo = new GameObject("WindTestCam"); var cam = camGo.AddComponent<Camera>();
            cam.enabled = false; cam.cullingMask = 1 << layer; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
            cam.fieldOfView = 40f; cam.nearClipPlane = 0.1f; cam.farClipPlane = 200f;
            float dist = Mathf.Max(4f, b.extents.magnitude * 2.4f);
            camGo.transform.position = b.center + new Vector3(0.3f, 0.15f, -1f).normalized * dist;
            camGo.transform.LookAt(b.center);
            var rt = new RenderTexture(160, 160, 24, RenderTextureFormat.ARGB32); cam.targetTexture = rt;
            var read = new Texture2D(160, 160, TextureFormat.RGBA32, false);
            Color32[] Shot()
            {
                Shader.SetGlobalVector(WindId, new Vector4(0.8f, 0.6f, 1.4f, 1f));            // strong steady wind for both shots
                cam.Render();
                var prev = RenderTexture.active; RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, 160, 160), 0, 0); read.Apply();
                RenderTexture.active = prev;
                return read.GetPixels32();
            }
            static int Diff(Color32[] a, Color32[] c) { int n = 0; for (int i = 0; i < a.Length; i++) if (Mathf.Abs(a[i].r - c[i].r) + Mathf.Abs(a[i].g - c[i].g) + Mathf.Abs(a[i].b - c[i].b) > 24) n++; return n; }
            int moving, still, covered;
            try
            {
                Shot(); yield return null;                                                     // warm-up (shader / shadow variants)
                var a = Shot(); covered = a.Count(p => p.r + p.g + p.b > 12);
                yield return new WaitForSeconds(0.6f);
                var c = Shot(); moving = Diff(a, c);
                // control: the same tree with the sway zeroed must not change
                var mpb = new MaterialPropertyBlock(); mpb.SetFloat(SwayId, 0f); mpb.SetFloat(FlutterId, 0f);
                foreach (var r in rends) r.SetPropertyBlock(mpb);
                var d = Shot();
                yield return new WaitForSeconds(0.6f);
                var e = Shot(); still = Diff(d, e);
            }
            finally
            {
                if (tm) tm.paused = paused;
                cam.targetTexture = null; rt.Release(); Object.Destroy(rt); Object.Destroy(read); Object.Destroy(camGo); Object.Destroy(tree);
            }
            Assert.Greater(covered, 200, "the tree is in the picture");
            Assert.Greater(moving, 20, $"{proto.name} moves in the wind ({moving} pixels changed in 0.6 s)");
            Assert.Greater(moving, still * 3 + 5, $"the change comes from the wind ({moving} with wind, {still} with the sway zeroed)");
#endif
        }
    }
}
