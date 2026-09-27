using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PrimalFrontier.EditorTools
{
    /// <summary>marks an editor method the bridge may run (static, no or one string argument)</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class PrimalBridgeCommandAttribute : Attribute { }

    /// <summary>
    /// Lets the pipeline run this project's own build steps (re-import a character, rebuild the Animator, bake the
    /// scene) inside the open editor instead of a second Unity instance. It only runs methods marked
    /// [PrimalBridgeCommand], only while the editor is idle (never in Play mode or while compiling), one at a time.
    /// Protocol: write Library/PrimalBridge/command.json {"id":"...","method":"Type.Method","arg":"..."} ->
    /// result_&lt;id&gt;.json with ok / error / log. Library is not in git.
    /// </summary>
    [InitializeOnLoad]
    public static class PrimalEditorBridge
    {
        public const string Dir = "Library/PrimalBridge";
        [Serializable] class Cmd { public string id; public string method; public string arg; }
        [Serializable] class Res { public string id, method, startedAt, finishedAt, result, error, log; public bool ok; }
        static double _next;
        static bool _busy;

        static PrimalEditorBridge()
        {
            if (Application.isBatchMode) return;
            EditorApplication.update += Poll;
        }

        static void Poll()
        {
            if (_busy || EditorApplication.timeSinceStartup < _next) return;
            _next = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            string cmdPath = Path.Combine(Dir, "command.json");
            if (!File.Exists(cmdPath)) return;
            Cmd c;
            try { c = JsonUtility.FromJson<Cmd>(File.ReadAllText(cmdPath)); File.Delete(cmdPath); }
            catch (Exception e) { Debug.LogWarning("[PrimalBridge] bad command: " + e.Message); try { File.Delete(cmdPath); } catch { } return; }
            if (c == null || string.IsNullOrEmpty(c.method)) return;
            _busy = true;
            var res = new Res { id = c.id, method = c.method, startedAt = DateTime.Now.ToString("s") };
            var log = new StringBuilder();
            Application.LogCallback cb = (m, st, t) =>
            {
                if (t == LogType.Log && !m.StartsWith("[Primal")) return;
                log.AppendLine($"[{t}] {m}");
                if (t == LogType.Exception || t == LogType.Error) log.AppendLine(st);
            };
            Application.logMessageReceived += cb;
            try
            {
                Debug.Log($"[PrimalBridge] running {c.method}");
                var mi = Find(c.method) ?? throw new Exception("not found or not marked [PrimalBridgeCommand]: " + c.method);
                var ps = mi.GetParameters();
                object r = mi.Invoke(null, ps.Length == 1 ? new object[] { c.arg } : null);
                res.ok = !(r is bool b) || b; res.result = r?.ToString();
            }
            catch (Exception e) { res.ok = false; res.error = (e is TargetInvocationException && e.InnerException != null ? e.InnerException : e).ToString(); }
            finally
            {
                Application.logMessageReceived -= cb;
                res.log = log.ToString(); res.finishedAt = DateTime.Now.ToString("s");
                Directory.CreateDirectory(Dir);
                File.WriteAllText(Path.Combine(Dir, $"result_{c.id}.json"), JsonUtility.ToJson(res, true));
                _busy = false;
            }
        }

        static MethodInfo Find(string full)
        {
            int dot = full.LastIndexOf('.'); if (dot <= 0) return null;
            string typeName = full.Substring(0, dot), method = full.Substring(dot + 1);
            if (!typeName.Contains('.')) typeName = "PrimalFrontier.EditorTools." + typeName;
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(typeName)).FirstOrDefault(t => t != null);
            var mi = type?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).FirstOrDefault(m => m.Name == method && m.GetParameters().Length <= 1);
            return mi != null && mi.GetCustomAttribute<PrimalBridgeCommandAttribute>() != null ? mi : null;
        }

        // ------------------------------------------------------------------ small commands
        [PrimalBridgeCommand] static string Refresh() { AssetDatabase.Refresh(); return "refreshed"; }
        [PrimalBridgeCommand] static string Ping() => $"editor {Application.unityVersion}, scene {UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path}";
    }
}
