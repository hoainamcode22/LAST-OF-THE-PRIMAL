using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PrimalFrontier.Tests
{
    /// <summary>Island tests leave the island scene (camera, GameManager, a second input reader) loaded; this swaps in an empty scene
    /// so the next test class starts clean (the test runner object lives in DontDestroyOnLoad).</summary>
    public static class TestScenes
    {
        public static IEnumerator Clear()
        {
            var empty = SceneManager.CreateScene("PF_TestEmpty_" + Time.frameCount);
            SceneManager.SetActiveScene(empty);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s != empty && s.isLoaded) yield return SceneManager.UnloadSceneAsync(s);
            }
            yield return null;
        }

        /// <summary>Only clears when a gameplay scene is still around (keeps the plain player tests fast).</summary>
        public static IEnumerator ClearIfGameplayLeft()
        {
            if (Object.FindFirstObjectByType<PrimalFrontier.Core.GameManager>() != null || Object.FindFirstObjectByType<Terrain>() != null) yield return Clear();
        }
    }
}
