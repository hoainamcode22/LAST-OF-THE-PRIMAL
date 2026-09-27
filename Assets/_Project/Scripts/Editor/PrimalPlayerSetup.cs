using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PrimalFrontier.Animation;
using PrimalFrontier.Player;

namespace PrimalFrontier.EditorTools
{
    /// <summary>Builds the gameplay player prefab (motor, animation driver, face) around the art prefab, and a controller test scene.</summary>
    public static class PrimalPlayerSetup
    {
        public const string ArtPrefab = "Assets/Art/Characters/Player/Prefab/PFB_Player_Survivor.prefab";
        public const string GameplayPrefab = "Assets/_Project/Prefabs/Player/PFB_Player.prefab";
        public const string TestScene = "Assets/_Project/Scenes/Test_PlayerController.unity";

        public static int EnsureLayer(string name)
        {
            int existing = LayerMask.NameToLayer(name);
            if (existing >= 0) return existing;
            var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tm.FindProperty("layers");
            for (int i = 8; i < 32; i++)
            {
                var p = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(p.stringValue)) { p.stringValue = name; tm.ApplyModifiedProperties(); return i; }
            }
            Debug.LogError("no free layer for " + name); return 0;
        }

        static void SetLayerRecursive(GameObject go, int layer)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }

        [MenuItem("Primal Frontier/Player/Build Gameplay Prefab")]
        public static GameObject BuildGameplayPrefab()
        {
            var art = AssetDatabase.LoadAssetAtPath<GameObject>(ArtPrefab);
            if (art == null) { Debug.LogError("[PrimalPlayerSetup] art prefab missing: " + ArtPrefab); return null; }
            int playerLayer = EnsureLayer("Player");
            System.IO.Directory.CreateDirectory("Assets/_Project/Prefabs/Player");
            var root = new GameObject("PFB_Player");
            try
            {
                root.tag = "Player";
                var cc = root.AddComponent<CharacterController>(); cc.height = 1.8f; cc.radius = 0.3f; cc.center = new Vector3(0, 0.9f, 0);
                var motor = root.AddComponent<PlayerMotor>();
                var drv = root.AddComponent<PlayerAnimationDriver>();
                var face = root.AddComponent<PlayerFacial>();
                root.AddComponent<PlayerHealth>();
                root.AddComponent<PlayerFeedback>();
                var model = (GameObject)PrefabUtility.InstantiatePrefab(art, root.transform);
                model.name = "Model"; model.transform.localPosition = Vector3.zero; model.transform.localRotation = Quaternion.identity;
                var childCC = model.GetComponent<CharacterController>();
                if (childCC != null) Object.DestroyImmediate(childCC, true);
                var anim = model.GetComponent<Animator>();
                anim.applyRootMotion = false; anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                drv.animator = anim; face.animator = anim;
                face.face = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.name.EndsWith("_LOD0"));
                SetLayerRecursive(root, playerLayer);
                var saved = PrefabUtility.SaveAsPrefabAsset(root, GameplayPrefab);
                Debug.Log("[PrimalPlayerSetup] saved " + GameplayPrefab);
                return saved;
            }
            finally { Object.DestroyImmediate(root); }
        }

        [MenuItem("Primal Frontier/Player/Build Controller Test Scene")]
        public static void BuildTestScene()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GameplayPrefab) ?? BuildGameplayPrefab();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            TestEnvironment.Build(null);
            var player = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            player.transform.position = new Vector3(0, 0.02f, 0);
            var input = new GameObject("PlayerInput"); input.AddComponent<PlayerInputReader>();
            var camGo = new GameObject("Main Camera"); camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>(); cam.nearClipPlane = 0.08f; camGo.AddComponent<AudioListener>();
            var tpc = camGo.AddComponent<ThirdPersonCamera>(); tpc.target = player.transform; tpc.ignoreMask = 1 << LayerMask.NameToLayer("Player");
            camGo.transform.position = new Vector3(0, 2.2f, -3.5f);
            EditorSceneManager.SaveScene(scene, TestScene);
            Debug.Log("[PrimalPlayerSetup] saved " + TestScene);
        }

        /// <summary>batch: -executeMethod PrimalFrontier.EditorTools.PrimalPlayerSetup.BuildAllFromCommandLine</summary>
        public static void BuildAllFromCommandLine()
        {
            int code = 0;
            try { if (BuildGameplayPrefab() == null) code = 2; else BuildTestScene(); AssetDatabase.SaveAssets(); }
            catch (System.Exception e) { Debug.LogError("[PrimalPlayerSetup] " + e); code = 1; }
            EditorApplication.Exit(code);
        }
    }
}
