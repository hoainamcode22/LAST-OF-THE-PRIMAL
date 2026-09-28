using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using PrimalFrontier.Player;
using PrimalFrontier.UI;

namespace PrimalFrontier.Tests
{
    /// <summary>The in-game controls table (ControlsGuide, also docs/CONTROLS.md) shows the real PlayerInputReader bindings.</summary>
    public class ControlsGuideTests
    {
        static bool KeyboardOrMouse(InputBinding b) => !b.isComposite && b.path != null && (b.path.StartsWith("<Keyboard>") || b.path.StartsWith("<Mouse>"));

        [UnityTest] public IEnumerator Controls_Table_Matches_PlayerInputReader_Bindings()
        {
            yield return TestScenes.ClearIfGameplayLeft();
            var go = new GameObject("ControlsTestInput");
            var reader = go.AddComponent<PlayerInputReader>();
            try
            {
                // this reader's own actions (private fields), not whatever else is enabled
                var byName = new Dictionary<string, InputAction>();
                foreach (var f in typeof(PlayerInputReader).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
                {
                    var v = f.GetValue(reader);
                    if (v is InputAction a) byName[a.name] = a;
                    else if (v is InputAction[] arr) foreach (var x in arr) if (x != null) byName[x.name] = x;
                }
                Assert.Greater(byName.Count, 10, "PlayerInputReader actions found");
                var covered = new HashSet<string>();
                foreach (var row in ControlsGuide.Rows)
                {
                    if (row.IsHeader || row.actions == null) continue;
                    var got = new HashSet<string>();
                    foreach (var n in row.actions)
                    {
                        Assert.IsTrue(byName.TryGetValue(n, out var a), $"row '{row.label}': PlayerInputReader has no action '{n}'");
                        covered.Add(n);
                        foreach (var b in a.bindings) if (KeyboardOrMouse(b)) got.Add(b.path);
                    }
                    CollectionAssert.AreEquivalent(row.paths ?? new string[0], got, $"row '{row.label}' ({string.Join(", ", row.actions)})");
                }
                foreach (var kv in byName)
                    if (kv.Value.bindings.Any(KeyboardOrMouse))
                        Assert.IsTrue(covered.Contains(kv.Key), $"action '{kv.Key}' has a keyboard / mouse binding but no row in ControlsGuide");
            }
            finally { Object.Destroy(go); }
            yield return null;
        }
    }
}
