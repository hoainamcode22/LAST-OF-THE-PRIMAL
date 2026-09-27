using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PrimalFrontier.EditorTools
{
    /// <summary>player builds: -executeMethod PrimalFrontier.EditorTools.PrimalBuild.WindowsFromCommandLine (Builds/Windows/PrimalFrontier.exe)</summary>
    public static class PrimalBuild
    {
        public static void WindowsFromCommandLine()
        {
            int code = 0;
            try { code = Windows() ? 0 : 2; }
            catch (Exception e) { Debug.LogError("[PrimalBuild] " + e); code = 1; }
            EditorApplication.Exit(code);
        }

        [MenuItem("Primal Frontier/Build/Windows")]
        public static bool Windows()
        {
            Directory.CreateDirectory("Builds/Windows");
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/_Project/Scenes/Island_VerticalSlice.unity" },
                locationPathName = "Builds/Windows/PrimalFrontier.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var r = BuildPipeline.BuildPlayer(opts);
            var s = r.summary;
            string line = $"[PrimalBuild] Windows: {s.result} size {s.totalSize / 1e6:F0} MB errors {s.totalErrors} warnings {s.totalWarnings} time {s.totalTime}";
            Debug.Log(line);
            Directory.CreateDirectory("Documentation");
            File.WriteAllText("Documentation/Build_Windows.md", $"# Windows build\n\n- result: {s.result}\n- output: Builds/Windows/PrimalFrontier.exe\n- size: {s.totalSize / 1e6:F0} MB\n- errors: {s.totalErrors}, warnings: {s.totalWarnings}\n- build time: {s.totalTime}\n");
            return s.result == BuildResult.Succeeded;
        }
    }
}
