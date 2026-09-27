using UnityEngine;

namespace PrimalFrontier.Player
{
    /// <summary>Greybox course used by the controller test scene and the play-mode tests: flat ground, 20 deg ramp,
    /// 55 deg slope (not walkable), steps and a wall for camera collision.</summary>
    public static class TestEnvironment
    {
        public static GameObject Build(Transform parent)
        {
            var root = new GameObject("TestCourse"); if (parent) root.transform.SetParent(parent, false);
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { color = new Color(0.55f, 0.52f, 0.46f) };
            GameObject Box(string n, Vector3 pos, Vector3 size, Vector3 euler)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = n; g.transform.SetParent(root.transform, false);
                g.transform.localPosition = pos; g.transform.localRotation = Quaternion.Euler(euler); g.transform.localScale = size;
                g.GetComponent<Renderer>().sharedMaterial = mat; return g;
            }
            Box("Ground", new Vector3(0, -0.5f, 0), new Vector3(120, 1, 120), Vector3.zero);
            // 20 deg ramp rising along +X, starting at x = 6
            float L = 8f, a = 20f; float h = Mathf.Sin(a * Mathf.Deg2Rad) * L;
            Box("Ramp20", new Vector3(6 + Mathf.Cos(a * Mathf.Deg2Rad) * L / 2f, h / 2f - 0.25f, 0), new Vector3(L, 0.5f, 4), new Vector3(0, 0, a));
            // 55 deg slope rising along -X, starting at x = -6
            float a2 = 55f;
            Box("Slope55", new Vector3(-6 - Mathf.Cos(a2 * Mathf.Deg2Rad) * 3f, Mathf.Sin(a2 * Mathf.Deg2Rad) * 3f - 0.25f, 0), new Vector3(6, 0.5f, 4), new Vector3(0, 0, -a2));
            for (int i = 0; i < 6; i++) Box("Step" + i, new Vector3(0, (i + 1) * 0.1f, 8 + i * 0.35f + 0.175f), new Vector3(3, (i + 1) * 0.2f, 0.35f), Vector3.zero);
            Box("Wall", new Vector3(0, 2, -12), new Vector3(10, 4, 0.5f), Vector3.zero);
            var sun = new GameObject("Sun"); sun.transform.SetParent(root.transform, false);
            var l = sun.AddComponent<Light>(); l.type = LightType.Directional; l.intensity = 1.2f; sun.transform.rotation = Quaternion.Euler(45, 30, 0);
            return root;
        }
    }
}
