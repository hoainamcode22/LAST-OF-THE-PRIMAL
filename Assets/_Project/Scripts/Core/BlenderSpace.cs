using UnityEngine;

namespace PrimalFrontier.Core
{
    /// <summary>
    /// Single source of truth for Blender (Z-up, right-handed, metres) to Unity (Y-up, left-handed) conversion.
    /// Blender FBX export uses forward -Z, up Y, "apply transform"; Unity's importer then flips X for handedness.
    /// Net mapping for points: U = (-Bx, Bz, -By). For transforms: T_u = M T_b M^T with
    /// M = [[-1,0,0],[0,0,1],[0,-1,0]]. See ADR-002 in .claude/docs/technical-preferences.md.
    /// </summary>
    public static class BlenderSpace
    {
        public static Vector3 ToUnityPosition(float x, float y, float z) => new Vector3(-x, z, -y);

        public static Vector3 ToUnityPosition(Vector3 b) => new Vector3(-b.x, b.z, -b.y);

        /// <summary>Decompose a Blender row-major 4x4 world matrix (matrix_world rows) into Unity TRS.</summary>
        public static void Decompose(float[][] m, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            position = ToUnityPosition(m[0][3], m[1][3], m[2][3]);

            // Basis columns of the Blender matrix (scaled)
            var c0 = new Vector3(m[0][0], m[1][0], m[2][0]);
            var c1 = new Vector3(m[0][1], m[1][1], m[2][1]);
            var c2 = new Vector3(m[0][2], m[1][2], m[2][2]);
            float sx = c0.magnitude, sy = c1.magnitude, sz = c2.magnitude;
            if (sx > 1e-6f) c0 /= sx;
            if (sy > 1e-6f) c1 /= sy;
            if (sz > 1e-6f) c2 /= sz;

            // R_b as rows
            float[,] rb =
            {
                { c0.x, c1.x, c2.x },
                { c0.y, c1.y, c2.y },
                { c0.z, c1.z, c2.z }
            };
            float[,] mm = { { -1, 0, 0 }, { 0, 0, 1 }, { 0, -1, 0 } };
            float[,] ru = Mul(Mul(mm, rb), Transpose(mm));

            var forward = new Vector3(ru[0, 2], ru[1, 2], ru[2, 2]);
            var up = new Vector3(ru[0, 1], ru[1, 1], ru[2, 1]);
            rotation = Quaternion.LookRotation(forward, up);
            // S_u = M S_b M^T = diag(sx, sz, sy)
            scale = new Vector3(sx, sz, sy);
        }

        /// <summary>Unity yaw (radians, around +Y) for a Blender matrix; used for terrain TreeInstances.</summary>
        public static float YawRadians(float[][] m)
        {
            Decompose(m, out _, out var rot, out _);
            Vector3 f = rot * Vector3.forward;
            return Mathf.Atan2(f.x, f.z);
        }

        static float[,] Mul(float[,] a, float[,] b)
        {
            var r = new float[3, 3];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                {
                    float s = 0f;
                    for (int k = 0; k < 3; k++) s += a[i, k] * b[k, j];
                    r[i, j] = s;
                }
            return r;
        }

        static float[,] Transpose(float[,] a)
        {
            var r = new float[3, 3];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    r[i, j] = a[j, i];
            return r;
        }
    }
}
