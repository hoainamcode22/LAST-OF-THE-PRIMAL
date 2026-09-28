using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace PrimalFrontier.EditorTools
{
    /// <summary>
    /// Runs the PlayMode test suite from the editor bridge (the editor enters and leaves Play mode by itself) and writes
    /// a summary to Library/PrimalBridge/playmode_tests.txt plus the NUnit XML to Documentation/Tests/playmode_results.xml.
    /// arg: empty = all PlayMode tests, otherwise a class / group name regex (e.g. "SurvivalLoopTests|CombatPolishTests").
    /// The callbacks are registered again after every domain reload, so the result survives entering / leaving Play mode.
    /// </summary>
    [InitializeOnLoad]
    public static class PrimalTestRunner
    {
        const string Summary = "Library/PrimalBridge/playmode_tests.txt";
        const string Xml = "Documentation/Tests/playmode_results.xml";
        static readonly TestRunnerApi Api;

        static PrimalTestRunner()
        {
            Api = ScriptableObject.CreateInstance<TestRunnerApi>();
            Api.hideFlags = HideFlags.HideAndDontSave;
            Api.RegisterCallbacks(new Callbacks());
        }

        [PrimalBridgeCommand]
        public static string RunPlayMode(string arg)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "Play mode is running: stop it first";
            Directory.CreateDirectory(Path.GetDirectoryName(Summary));
            File.WriteAllText(Summary, "RUNNING " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + "\n");
            var filter = new Filter { testMode = TestMode.PlayMode };
            if (!string.IsNullOrWhiteSpace(arg)) filter.groupNames = new[] { arg.Trim() };
            Api.Execute(new ExecutionSettings(filter));
            return "PlayMode tests started (" + (string.IsNullOrWhiteSpace(arg) ? "all" : arg) + "): poll " + Summary;
        }

        class Callbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }

            public void RunFinished(ITestResultAdaptor result)
            {
                PrimalFrontier.Core.SaveSystem.FolderOverride = null;             // saves go back to the player's folder whatever a test left
                try
                {
                    var sb = new StringBuilder();
                    sb.AppendLine($"DONE {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    sb.AppendLine($"passed {result.PassCount}  failed {result.FailCount}  skipped {result.SkipCount}  inconclusive {result.InconclusiveCount}  time {result.Duration:F1}s");
                    Collect(result, sb);
                    Directory.CreateDirectory(Path.GetDirectoryName(Summary));
                    File.WriteAllText(Summary, sb.ToString());
                    Directory.CreateDirectory(Path.GetDirectoryName(Xml));
                    TestRunnerApi.SaveResultToFile(result, Xml);
                }
                catch (Exception e) { Debug.LogError("[PrimalTestRunner] " + e); }
            }

            static void Collect(ITestResultAdaptor r, StringBuilder sb)
            {
                if (r.HasChildren) { foreach (var c in r.Children) Collect(c, sb); return; }
                if (r.TestStatus == TestStatus.Passed) { sb.AppendLine("  ok   " + r.Test.FullName); return; }
                string msg = (r.Message ?? "").Replace("\r", " ").Replace("\n", " ");
                if (msg.Length > 400) msg = msg.Substring(0, 400);
                string st = r.TestStatus == TestStatus.Failed ? "FAILED" : r.TestStatus == TestStatus.Skipped ? "SKIPPED" : "INCONCLUSIVE";
                sb.AppendLine($"  {st} {r.Test.FullName}: {msg}");
            }
        }
    }
}
