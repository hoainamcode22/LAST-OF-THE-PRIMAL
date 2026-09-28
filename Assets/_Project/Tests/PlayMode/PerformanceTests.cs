using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using PrimalFrontier.AI;
using PrimalFrontier.Core;
using PrimalFrontier.Player;

namespace PrimalFrontier.Tests
{
    /// <summary>Phase 16: frame time, allocations and render counters on the island with every creature spawned, measured
    /// in the editor (not on a phone). Results -> Documentation/Performance_Island.md</summary>
    public class PerformanceTests
    {
        GameManager _gm;
        [TearDown] public void Cleanup() { GameManager.ForceShowTitle = null; GameManager.ForcePlayIntro = null; PlayerInputReader.Simulate = false; }
        [UnityTearDown] public IEnumerator Unload() { yield return TestScenes.Clear(); }

        [UnityTest] public IEnumerator Island_Frame_Budget_With_All_Creatures()
        {
            TestScenes.UseTestSaves(); GameManager.ForceShowTitle = false; GameManager.ForcePlayIntro = false; PlayerInputReader.Simulate = true;
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Project/Scenes/Island_VerticalSlice.unity", new LoadSceneParameters(LoadSceneMode.Single));
#endif
            float t = 0; while ((_gm = GameManager.Instance) == null || _gm.State != GameState.Playing) { t += Time.unscaledDeltaTime; if (t > 20f) break; yield return null; }
            Assert.IsNotNull(_gm);
            // stand where the nearest herd can be seen (worst case: creatures at full AI / LOD0 rate)
            var tri = DinosaurController.All.FirstOrDefault(d => d.def && d.def.id == "triceratops");
            if (tri) _gm.Player.GetComponent<PlayerMotor>().Warp(tri.transform.position + tri.transform.right * 35f + Vector3.up * 2f, Quaternion.LookRotation(-tri.transform.right));
            yield return new WaitForSeconds(3f);
            var mainT = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 300);
            var gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 300);
            var batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count", 300);
            var setpass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count", 300);
            var tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count", 300);
            int frames = 240; float sum = 0, worst = 0; long gcSum = 0; long bMax = 0, spMax = 0, trMax = 0;
            for (int i = 0; i < frames; i++)
            {
                yield return null;
                float dt = Time.unscaledDeltaTime; sum += dt; worst = Mathf.Max(worst, dt);
                if (gc.Valid) gcSum += gc.LastValue;
                if (batches.Valid) bMax = System.Math.Max(bMax, batches.LastValue);
                if (setpass.Valid) spMax = System.Math.Max(spMax, setpass.LastValue);
                if (tris.Valid) trMax = System.Math.Max(trMax, tris.LastValue);
            }
            double mainMs = mainT.Valid && mainT.Count > 0 ? Enumerable.Range(0, mainT.Count).Average(i => mainT.GetSample(i).Value) / 1e6 : -1;
            mainT.Dispose(); gc.Dispose(); batches.Dispose(); setpass.Dispose(); tris.Dispose();
            float avg = sum / frames;
            int dinos = DinosaurController.All.Count;
            var sb = new StringBuilder();
            sb.AppendLine("# Island performance (editor, batch mode)\n");
            sb.AppendLine("Measured inside the Unity editor on the development PC while running the PlayMode tests. Not a phone measurement.\n");
            sb.AppendLine($"- land creatures alive: {dinos}, ambient creatures: {Object.FindObjectsByType<AmbientCreature>(FindObjectsSortMode.None).Length}");
            sb.AppendLine($"- frames sampled: {frames}");
            sb.AppendLine($"- average frame: {avg * 1000f:F1} ms ({1f / avg:F0} fps), worst frame: {worst * 1000f:F1} ms");
            sb.AppendLine($"- main thread (profiler): {(mainMs >= 0 ? mainMs.ToString("F1") + " ms" : "n/a")}");
            sb.AppendLine($"- GC allocated per frame (avg): {(gcSum / (double)frames) / 1024.0:F1} KB");
            sb.AppendLine($"- render counters (max): batches {bMax}, SetPass {spMax}, triangles {trMax}");
            Directory.CreateDirectory("Documentation"); File.WriteAllText("Documentation/Performance_Island.md", sb.ToString());
            Debug.Log(sb.ToString());
            Assert.Less(avg, 0.1f, "editor frame budget (generous: batch-mode editor, all creatures active)");
        }
    }
}
