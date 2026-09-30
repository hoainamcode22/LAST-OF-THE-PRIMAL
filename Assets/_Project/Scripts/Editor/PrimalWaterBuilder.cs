using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using PrimalFrontier.World;
using Object = UnityEngine.Object;

namespace PrimalFrontier.EditorTools
{
    /// <summary>
    /// The new water: PF/Ocean (the sea), PF/Ocean Shore (foam lines, swash, wet sand) and PF/Water Flow (stream, pond).
    /// Build (bridge PrimalWaterBuilder.Build, menu Tools/Water: Build):
    ///  1. checks the three shaders compile (nothing changes otherwise) and sets the import settings of the procedural
    ///     textures in Art/Water (tools/water_textures.py: normal maps as Normal map, data textures linear);
    ///  2. bakes the shore map from the terrain (height above sea, smoothed distance to the shoreline, direction to the
    ///     beach) into Art/Water/Generated/T_Water_ShoreMap.asset;
    ///  3. generates the ocean mesh (fine grid over the island, stretched to 1.5 km, crack free) and the shore sheet
    ///     (terrain band -2.6 m .. +1.2 m around sea level, 64 m chunks so it culls);
    ///  4. bakes one flow map per fresh water body (stream: downstream along the channel, faster where steep, banks /
    ///     rocks / depth; pond: calm, inflow where the stream enters; the stream fades out inside the pond);
    ///  5. creates / updates the materials in Art/Water/Materials (numbers are kept on a re-run, arg "reset" restores
    ///     the defaults), adds Water/PF_Ocean + Water/PF_OceanShore, disables the old ENV_Ocean renderer, puts the flow
    ///     materials on the pond / stream renderers, adds WaterGlobals to the Water root, saves the scene.
    /// Every replaced renderer state (materials, enabled) is recorded first in Art/Materials/_Backup/water_originals.json
    /// (the first record is kept on re-runs). Idempotent. Gameplay (OceanShore, WaterSource) is not touched.
    /// Revert: restores the recorded renderers, deletes PF_Ocean / PF_OceanShore / WaterGlobals (arg "purge" also
    /// deletes the generated assets and materials).
    /// Preview: edit-mode captures of the beach (three moments of the swash cycle), the stream, the pond and the pond in
    /// the rain into Documentation/Screenshots/Water/. Args (';' separated): hour ("15"), only=pond+stream (name prefixes),
    /// debug=N (1 depth / scene depth / bank, 2 reflection, 3 alpha, 4 speed / foam / fresnel, 5 body, 6 composite,
    /// 7 reflection x fresnel, 8 fresnel / ndv / edge), dt=0.6 (shift the water clock: motion checks), suffix=_v2, keep
    /// (leave the hour set). In edit mode the procedural sky follows the sun and Unity rebuilds the sky reflection over
    /// the next editor frames, so the first capture after an hour change, a scene save or a script reload can show a
    /// dim reflection: run Preview twice and use the second (e.g. "15;keep;only=none", then "15").
    /// Inspect: lighting / reflection set-up, water renderers, flow map statistics.
    /// Build args (optional, ';' separated): reset | shoreStep=1 | oceanStep=5 | mapRes=1024 | flowTexel=0.25 | noObstacles
    /// Textures (Art/Water): T_Water_Normal_Waves, T_Water_Normal_Ripples, T_Water_Foam, T_Water_RainRipple, T_Water_FlowStreaks.
    /// </summary>
    public static class PrimalWaterBuilder
    {
        const string ArtRoot = "Assets/_Project/Art/Water", GenDir = ArtRoot + "/Generated", MatDir = ArtRoot + "/Materials";
        const string BackupDir = "Assets/_Project/Art/Materials/_Backup", RecordPath = BackupDir + "/water_originals.json";
        const string IslandScene = "Assets/_Project/Scenes/Island_VerticalSlice.unity", ShotDir = "Documentation/Screenshots/Water";
        const string OceanShader = "PF/Ocean", ShoreShader = "PF/Ocean Shore", FlowShader = "PF/Water Flow";
        const string OceanObject = "PF_Ocean", ShoreObject = "PF_OceanShore";
        const string TexWaves = "T_Water_Normal_Waves.png", TexRipples = "T_Water_Normal_Ripples.png", TexFoam = "T_Water_Foam.png", TexRain = "T_Water_RainRipple.png",
                     TexStreaks = "T_Water_FlowStreaks.png";
        const float ShoreBandLow = -2.6f, ShoreBandHigh = 1.2f, SdfRange = 48f, HeightRange = 16f, BankRange = 3f, DepthRange = 2f;

        static readonly StringBuilder Log = new StringBuilder();
        static void L(string s) { Log.AppendLine(s); Debug.Log("[PrimalWater] " + s); }
        static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        [MenuItem("Primal Frontier/Tools/Water: Build (sea, shore, stream, pond)")]
        static void MenuBuild() => EditorUtility.DisplayDialog("Primal Frontier", Build(""), "OK");
        [MenuItem("Primal Frontier/Tools/Water: Preview captures")]
        static void MenuPreview() => EditorUtility.DisplayDialog("Primal Frontier", Preview(""), "OK");
        [MenuItem("Primal Frontier/Tools/Water: Revert")]
        static void MenuRevert() => EditorUtility.DisplayDialog("Primal Frontier", Revert(""), "OK");

        // ------------------------------------------------------------------ records
        [Serializable] class RendererRecord { public string path; public string[] materials; public bool enabled; }
        [Serializable] class WaterRecord { public List<RendererRecord> renderers = new List<RendererRecord>(); public bool addedGlobals; public string builtAt; }

        static WaterRecord LoadRecord()
        {
            try { if (File.Exists(RecordPath)) return JsonUtility.FromJson<WaterRecord>(File.ReadAllText(RecordPath)) ?? new WaterRecord(); }
            catch (Exception e) { L("could not read " + RecordPath + ": " + e.Message); }
            return new WaterRecord();
        }

        static void SaveRecord(WaterRecord r)
        {
            Directory.CreateDirectory(BackupDir);
            File.WriteAllText(RecordPath, JsonUtility.ToJson(r, true));
            AssetDatabase.ImportAsset(RecordPath);
        }

        /// <summary>"path" for a .mat asset, "path#name" for a material inside a model file</summary>
        static string MatKey(Material m)
        {
            if (!m) return "";
            string p = AssetDatabase.GetAssetPath(m);
            return string.IsNullOrEmpty(p) || AssetDatabase.IsMainAsset(m) ? p : p + "#" + m.name;
        }

        static Material LoadMat(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            int i = key.IndexOf('#');
            if (i < 0) return AssetDatabase.LoadAssetAtPath<Material>(key);
            string path = key.Substring(0, i), name = key.Substring(i + 1);
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().FirstOrDefault(x => x.name == name);
        }

        static string PathOf(Transform t)
        {
            var parts = new List<string>();
            for (var x = t; x; x = x.parent) parts.Insert(0, x.name);
            return string.Join("/", parts);
        }

        static Transform FindPath(Scene scene, string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            int slash = path.IndexOf('/');
            string rootName = slash < 0 ? path : path.Substring(0, slash);
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name != rootName) continue;
                if (slash < 0) return root.transform;
                var t = root.transform.Find(path.Substring(slash + 1));
                if (t) return t;
            }
            // moved: look it up by its own name
            string leaf = path.Substring(path.LastIndexOf('/') + 1);
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == leaf) return t;
            return null;
        }

        // ------------------------------------------------------------------ scene helpers
        static bool IslandOpen(out Scene scene, out bool wasDirty)
        {
            scene = EditorSceneManager.GetActiveScene(); wasDirty = scene.isDirty;
            if (scene.path == IslandScene) return true;
            if (scene.isDirty) { L($"the open scene ({scene.name}) has unsaved changes: save it or open {IslandScene}, then run again"); return false; }
            if (!File.Exists(IslandScene)) { L("scene missing: " + IslandScene); return false; }
            scene = EditorSceneManager.OpenScene(IslandScene, OpenSceneMode.Single); wasDirty = false;
            return true;
        }

        static void SaveScene(Scene scene, bool wasDirty)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            if (wasDirty) { L("the scene had other unsaved changes: NOT saved, save it yourself"); return; }
            if (string.IsNullOrEmpty(scene.path)) return;
            EditorSceneManager.SaveScene(scene);
            L("scene saved: " + scene.path);
        }

        static bool ShaderOk(string name, out Shader sh)
        {
            sh = Shader.Find(name);
            if (!sh) { L(name + ": shader not found (is Assets/_Project/Shaders up to date?)"); return false; }
            var errs = ShaderUtil.GetShaderMessages(sh).Where(m => m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error).ToArray();
            if (errs.Length > 0 || ShaderUtil.ShaderHasError(sh))
            {
                L($"{name}: {errs.Length} compile error(s): " + string.Join(" | ", errs.Take(4).Select(e => e.message + " (line " + e.line + ")")));
                return false;
            }
            return true;
        }

        static Dictionary<string, string> Args(string arg)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tok in (arg ?? "").Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = tok.IndexOf('=');
                if (eq < 0) d[tok.Trim()] = "1"; else d[tok.Substring(0, eq).Trim()] = tok.Substring(eq + 1).Trim();
            }
            return d;
        }

        static float ArgF(Dictionary<string, string> a, string key, float def, float min, float max)
        {
            if (a.TryGetValue(key, out var s) && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return Mathf.Clamp(v, min, max);
            return def;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        // ------------------------------------------------------------------ terrain
        static Terrain[] _terrains = new Terrain[0];

        static void CollectTerrains()
        {
            var list = Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(t => t && t.terrainData).ToList();
            if (Terrain.activeTerrain && list.Remove(Terrain.activeTerrain)) list.Insert(0, Terrain.activeTerrain);
            _terrains = list.ToArray();
        }

        static bool TerrainY(float x, float z, out float y)
        {
            foreach (var t in _terrains)
            {
                var p = t.transform.position; var s = t.terrainData.size;
                if (x < p.x || z < p.z || x > p.x + s.x || z > p.z + s.z) continue;
                y = t.SampleHeight(new Vector3(x, 0f, z)) + p.y;
                return true;
            }
            y = float.NaN;
            return false;
        }

        static Rect TerrainRect()
        {
            bool any = false; Rect r = default;
            foreach (var t in _terrains)
            {
                var p = t.transform.position; var s = t.terrainData.size;
                var tr = new Rect(p.x, p.z, s.x, s.z);
                if (!any) { r = tr; any = true; }
                else r = Rect.MinMaxRect(Mathf.Min(r.xMin, tr.xMin), Mathf.Min(r.yMin, tr.yMin), Mathf.Max(r.xMax, tr.xMax), Mathf.Max(r.yMax, tr.yMax));
            }
            return r;
        }

        // ------------------------------------------------------------------ distance transform (Felzenszwalb, exact)
        const double Inf = 1e20;

        static void Edt1D(double[] f, int n, double[] d, int[] v, double[] z)
        {
            int k = 0; v[0] = 0; z[0] = -Inf; z[1] = Inf;
            for (int q = 1; q < n; q++)
            {
                double s = ((f[q] + (double)q * q) - (f[v[k]] + (double)v[k] * v[k])) / (2.0 * q - 2.0 * v[k]);
                while (s <= z[k])
                {
                    k--;
                    s = ((f[q] + (double)q * q) - (f[v[k]] + (double)v[k] * v[k])) / (2.0 * q - 2.0 * v[k]);
                }
                k++; v[k] = q; z[k] = s; z[k + 1] = Inf;
            }
            k = 0;
            for (int q = 0; q < n; q++)
            {
                while (z[k + 1] < q) k++;
                d[q] = ((double)q - v[k]) * ((double)q - v[k]) + f[v[k]];
            }
        }

        /// <summary>distance (texels) from every cell to the nearest cell where feature[i] is true</summary>
        static float[] Edt(bool[] feature, int w, int h)
        {
            int n = Mathf.Max(w, h);
            var f = new double[n]; var d = new double[n]; var v = new int[n]; var z = new double[n + 1];
            var grid = new double[w * h];
            for (int i = 0; i < grid.Length; i++) grid[i] = feature[i] ? 0.0 : Inf;
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++) f[y] = grid[y * w + x];
                Edt1D(f, h, d, v, z);
                for (int y = 0; y < h; y++) grid[y * w + x] = d[y];
            }
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++) f[x] = grid[y * w + x];
                Edt1D(f, w, d, v, z);
                for (int x = 0; x < w; x++) grid[y * w + x] = d[x];
            }
            var outp = new float[w * h];
            for (int i = 0; i < outp.Length; i++) outp[i] = grid[i] >= Inf * 0.5 ? 1e6f : (float)Math.Sqrt(grid[i]);
            return outp;
        }

        /// <summary>separable box blur, 'passes' times (3 passes ~ gaussian)</summary>
        static float[] Blur(float[] src, int w, int h, int r, int passes)
        {
            var a = (float[])src.Clone(); var b = new float[a.Length];
            for (int p = 0; p < passes; p++)
            {
                for (int y = 0; y < h; y++)
                {
                    double sum = 0; int row = y * w;
                    for (int x = -r; x <= r; x++) sum += a[row + Mathf.Clamp(x, 0, w - 1)];
                    for (int x = 0; x < w; x++)
                    {
                        b[row + x] = (float)(sum / (2 * r + 1));
                        sum += a[row + Mathf.Min(x + r + 1, w - 1)] - a[row + Mathf.Max(x - r, 0)];
                    }
                }
                for (int x = 0; x < w; x++)
                {
                    double sum = 0;
                    for (int y = -r; y <= r; y++) sum += b[Mathf.Clamp(y, 0, h - 1) * w + x];
                    for (int y = 0; y < h; y++)
                    {
                        a[y * w + x] = (float)(sum / (2 * r + 1));
                        sum += b[Mathf.Min(y + r + 1, h - 1) * w + x] - b[Mathf.Max(y - r, 0) * w + x];
                    }
                }
            }
            return a;
        }

        static byte B01(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);

        /// <summary>a linear RGBA32 data texture saved as a .asset (kept in place on re-runs so references hold)</summary>
        static Texture2D SaveDataTexture(string path, int w, int h, Color32[] px)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            bool created = false;
            if (!tex) { tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true); created = true; }
            else if (tex.width != w || tex.height != h || tex.format != TextureFormat.RGBA32 || tex.mipmapCount != 1) tex.Reinitialize(w, h, TextureFormat.RGBA32, false);
            tex.name = Path.GetFileNameWithoutExtension(path);
            tex.wrapMode = TextureWrapMode.Clamp; tex.filterMode = FilterMode.Bilinear; tex.anisoLevel = 0;
            tex.SetPixels32(px); tex.Apply(false, false);
            if (created) AssetDatabase.CreateAsset(tex, path); else EditorUtility.SetDirty(tex);
            return tex;
        }

        // ------------------------------------------------------------------ 1. shore map
        class ShoreMap { public Texture2D tex; public Vector4 rect; public int w, h; public float texel; public float[] height; }

        static ShoreMap BakeShoreMap(float seaLevel, int res)
        {
            var r = TerrainRect();
            int w = res, h = Mathf.Max(8, Mathf.RoundToInt(res * r.height / Mathf.Max(1f, r.width)));
            float tx = r.width / w, tz = r.height / h;
            var height = new float[w * h];
            var land = new bool[w * h]; var water = new bool[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    float hy = TerrainY(r.xMin + (x + 0.5f) * tx, r.yMin + (y + 0.5f) * tz, out var ty) ? ty - seaLevel : -HeightRange;
                    height[i] = hy; land[i] = hy > 0f; water[i] = !land[i];
                }
            float texel = (tx + tz) * 0.5f;
            var toLand = Edt(land, w, h); var toWater = Edt(water, w, h);
            var sdf = new float[w * h];
            bool anyLand = land.Any(b => b);
            for (int i = 0; i < sdf.Length; i++)
                sdf[i] = !anyLand ? SdfRange * 2f : land[i] ? -toWater[i] * texel : toLand[i] * texel;
            for (int i = 0; i < sdf.Length; i++) sdf[i] = Mathf.Clamp(sdf[i], -SdfRange * 2f, SdfRange * 2f);
            // smooth so the crest lines bend gently around coves and headlands (no cusps on the medial axis)
            var smooth = Blur(sdf, w, h, Mathf.Max(1, Mathf.RoundToInt(2.5f / texel)), 3);
            var px = new Color32[w * h];
            int shoreTexels = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    float gx = (smooth[y * w + Mathf.Min(x + 1, w - 1)] - smooth[y * w + Mathf.Max(x - 1, 0)]) / (2f * tx);
                    float gz = (smooth[Mathf.Min(y + 1, h - 1) * w + x] - smooth[Mathf.Max(y - 1, 0) * w + x]) / (2f * tz);
                    float gl = Mathf.Sqrt(gx * gx + gz * gz);
                    Vector2 dir = gl > 0.05f ? new Vector2(-gx, -gz) / gl : Vector2.zero;    // towards the shore
                    float hv = height[i];
                    float e = Mathf.Sign(hv) * Mathf.Sqrt(Mathf.Min(Mathf.Abs(hv), HeightRange) / HeightRange);
                    px[i] = new Color32(B01(0.5f + 0.5f * e), B01(0.5f + Mathf.Clamp(smooth[i], -SdfRange, SdfRange) / (2f * SdfRange)),
                                        B01(dir.x * 0.5f + 0.5f), B01(dir.y * 0.5f + 0.5f));
                    if (hv > ShoreBandLow && hv < ShoreBandHigh) shoreTexels++;
                }
            var tex = SaveDataTexture(GenDir + "/T_Water_ShoreMap.asset", w, h, px);
            L($"shore map {w}x{h} ({F(texel)} m/texel) over terrain {F(r.width)} x {F(r.height)} m, sea level {F(seaLevel)}, shore band {F(shoreTexels * tx * tz)} m2");
            return new ShoreMap { tex = tex, rect = new Vector4(r.xMin, r.yMin, 1f / r.width, 1f / r.height), w = w, h = h, texel = texel, height = height };
        }

        // ------------------------------------------------------------------ 2. meshes
        static List<float> Axis(float lo, float hi, float step, float centre, float far)
        {
            var inner = new List<float>();
            int n = Mathf.Max(1, Mathf.CeilToInt((hi - lo) / step));
            for (int i = 0; i <= n; i++) inner.Add(Mathf.Lerp(lo, hi, i / (float)n));
            var left = new List<float>(); float x = lo, s = step;
            while (x > centre - far) { s *= 1.25f; x = Mathf.Max(x - s, centre - far); left.Add(x); }
            var right = new List<float>(); x = hi; s = step;
            while (x < centre + far) { s *= 1.25f; x = Mathf.Min(x + s, centre + far); right.Add(x); }
            left.Reverse();
            var all = new List<float>(left); all.AddRange(inner); all.AddRange(right);
            return all;
        }

        /// <summary>fine grid over the island, stretched outwards to 'far' metres: one crack-free mesh (object at sea level)</summary>
        static Mesh BuildOceanMesh(Rect island, float step, float far)
        {
            var xs = Axis(island.xMin - 40f, island.xMax + 40f, step, island.center.x, far);
            var zs = Axis(island.yMin - 40f, island.yMax + 40f, step, island.center.y, far);
            int nx = xs.Count, nz = zs.Count;
            var verts = new Vector3[nx * nz]; var normals = new Vector3[nx * nz];
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++) { verts[j * nx + i] = new Vector3(xs[i], 0f, zs[j]); normals[j * nx + i] = Vector3.up; }
            var tris = new int[(nx - 1) * (nz - 1) * 6]; int k = 0;
            for (int j = 0; j < nz - 1; j++)
                for (int i = 0; i < nx - 1; i++)
                {
                    int v00 = j * nx + i, v10 = v00 + 1, v01 = v00 + nx, v11 = v01 + 1;
                    tris[k++] = v00; tris[k++] = v01; tris[k++] = v11;
                    tris[k++] = v00; tris[k++] = v11; tris[k++] = v10;
                }
            string path = GenDir + "/ME_Ocean.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool created = !mesh;
            if (created) mesh = new Mesh();
            mesh.Clear();
            mesh.name = "ME_Ocean";
            mesh.indexFormat = verts.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.vertices = verts; mesh.normals = normals; mesh.triangles = tris;
            mesh.RecalculateBounds();
            var b = mesh.bounds; b.Expand(new Vector3(0f, 4f, 0f)); mesh.bounds = b;           // room for the swell
            if (created) AssetDatabase.CreateAsset(mesh, path); else EditorUtility.SetDirty(mesh);
            L($"ocean mesh {nx}x{nz} = {verts.Length} vertices, {tris.Length / 3} triangles, {F(step)} m over the island, out to {F(far)} m");
            return mesh;
        }

        /// <summary>the shoreline band as 64-cell chunks lying on the sand (or on the water where the sand is under it)</summary>
        static List<Mesh> BuildShoreMeshes(float seaLevel, float step)
        {
            var r = TerrainRect();
            int nx = Mathf.FloorToInt(r.width / step) + 1, nz = Mathf.FloorToInt(r.height / step) + 1;
            var hgt = new float[nx * nz];
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                    hgt[j * nx + i] = TerrainY(r.xMin + i * step, r.yMin + j * step, out var y) ? y - seaLevel : -HeightRange;
            string path = GenDir + "/ME_OceanShore.asset";
            if (AssetDatabase.LoadMainAssetAtPath(path)) AssetDatabase.DeleteAsset(path);
            var meshes = new List<Mesh>();
            const int C = 64;
            int quads = 0;
            var map = new int[(C + 1) * (C + 1)];
            for (int cj = 0; cj < nz - 1; cj += C)
                for (int ci = 0; ci < nx - 1; ci += C)
                {
                    var verts = new List<Vector3>(); var tris = new List<int>();
                    for (int m = 0; m < map.Length; m++) map[m] = -1;
                    int V(int i, int j)
                    {
                        int li = (j - cj) * (C + 1) + (i - ci);
                        if (map[li] >= 0) return map[li];
                        float hv = hgt[j * nx + i];
                        float y = Mathf.Max(hv + 0.03f, 0.02f);                   // on the sand, or just above the sea
                        map[li] = verts.Count; verts.Add(new Vector3(r.xMin + i * step, y, r.yMin + j * step));
                        return map[li];
                    }
                    for (int j = cj; j < Mathf.Min(cj + C, nz - 1); j++)
                        for (int i = ci; i < Mathf.Min(ci + C, nx - 1); i++)
                        {
                            float a = hgt[j * nx + i], b = hgt[j * nx + i + 1], c = hgt[(j + 1) * nx + i], d = hgt[(j + 1) * nx + i + 1];
                            float lo = Mathf.Min(Mathf.Min(a, b), Mathf.Min(c, d)), hi = Mathf.Max(Mathf.Max(a, b), Mathf.Max(c, d));
                            if (hi < ShoreBandLow || lo > ShoreBandHigh) continue;
                            int v00 = V(i, j), v10 = V(i + 1, j), v01 = V(i, j + 1), v11 = V(i + 1, j + 1);
                            tris.Add(v00); tris.Add(v01); tris.Add(v11);
                            tris.Add(v00); tris.Add(v11); tris.Add(v10);
                            quads++;
                        }
                    if (tris.Count == 0) continue;
                    var mesh = new Mesh { name = $"ME_OceanShore_{ci / C:00}_{cj / C:00}" };
                    mesh.SetVertices(verts);
                    mesh.SetNormals(Enumerable.Repeat(Vector3.up, verts.Count).ToList());
                    mesh.SetTriangles(tris, 0);
                    mesh.RecalculateBounds();
                    var bb = mesh.bounds; bb.Expand(new Vector3(0.5f, 0.5f, 0.5f)); mesh.bounds = bb;
                    if (meshes.Count == 0) AssetDatabase.CreateAsset(mesh, path); else AssetDatabase.AddObjectToAsset(mesh, path);
                    meshes.Add(mesh);
                }
            L($"shore sheet: {meshes.Count} chunk(s), {quads} quads at {F(step)} m (terrain {F(ShoreBandLow)} .. +{F(ShoreBandHigh)} m around the sea)");
            return meshes;
        }

        // ------------------------------------------------------------------ 3. flow maps
        class Body
        {
            public Renderer renderer; public MeshFilter filter; public string key; public bool calm;
            public float ox, oz, texel; public int w, h;
            public bool[] inside, wet, handover; public float[] surfY, depth, bank, calmFade; public Vector2[] flow;
            public float minY, maxY; public Texture2D tex; public Vector4 rect;
            public bool Cell(float x, float z, out int i)
            {
                int ix = Mathf.FloorToInt((x - ox) / texel), iz = Mathf.FloorToInt((z - oz) / texel);
                i = iz * w + ix;
                return ix >= 0 && iz >= 0 && ix < w && iz < h;
            }
            public Vector3 World(int i) => new Vector3(ox + (i % w + 0.5f) * texel, surfY[i], oz + (i / w + 0.5f) * texel);
        }

        static string BodyKey(string name)
        {
            string k = name.Replace("ENV_", "").Replace("_Water", "").Replace("Water", "").Trim('_', ' ');
            return string.IsNullOrEmpty(k) ? name : k;
        }

        static Body Rasterize(Renderer r, MeshFilter mf, float texelWanted, bool obstacles)
        {
            var mesh = mf.sharedMesh;
            if (!mesh) { L($"{r.name}: no mesh"); return null; }
            if (!mesh.isReadable) { L($"{r.name}: mesh {mesh.name} is not readable (enable Read/Write in its model import settings): skipped"); return null; }
            var lv = mesh.vertices; var tri = mesh.triangles;
            var wv = new Vector3[lv.Length];
            for (int i = 0; i < lv.Length; i++) wv[i] = mf.transform.TransformPoint(lv[i]);
            float minX = wv.Min(v => v.x) - 1f, maxX = wv.Max(v => v.x) + 1f, minZ = wv.Min(v => v.z) - 1f, maxZ = wv.Max(v => v.z) + 1f;
            float texel = Mathf.Max(texelWanted, Mathf.Max(maxX - minX, maxZ - minZ) / 1024f);
            var b = new Body { renderer = r, filter = mf, key = BodyKey(r.name), ox = minX, oz = minZ, texel = texel };
            b.w = Mathf.CeilToInt((maxX - minX) / texel); b.h = Mathf.CeilToInt((maxZ - minZ) / texel);
            int n = b.w * b.h;
            b.inside = new bool[n]; b.surfY = new float[n]; b.depth = new float[n]; b.wet = new bool[n]; b.handover = new bool[n]; b.calmFade = new float[n];
            for (int i = 0; i < n; i++) { b.surfY[i] = float.NegativeInfinity; b.calmFade[i] = 1f; }
            for (int t = 0; t + 2 < tri.Length; t += 3)
            {
                Vector3 A = wv[tri[t]], B = wv[tri[t + 1]], C = wv[tri[t + 2]];
                float area = (B.x - A.x) * (C.z - A.z) - (B.z - A.z) * (C.x - A.x);
                if (Mathf.Abs(area) < 1e-8f) continue;
                int x0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(A.x, Mathf.Min(B.x, C.x)) - minX) / texel - 0.5f));
                int x1 = Mathf.Min(b.w - 1, Mathf.CeilToInt((Mathf.Max(A.x, Mathf.Max(B.x, C.x)) - minX) / texel - 0.5f));
                int z0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(A.z, Mathf.Min(B.z, C.z)) - minZ) / texel - 0.5f));
                int z1 = Mathf.Min(b.h - 1, Mathf.CeilToInt((Mathf.Max(A.z, Mathf.Max(B.z, C.z)) - minZ) / texel - 0.5f));
                for (int iz = z0; iz <= z1; iz++)
                    for (int ix = x0; ix <= x1; ix++)
                    {
                        float px = minX + (ix + 0.5f) * texel, pz = minZ + (iz + 0.5f) * texel;
                        float w0 = ((C.x - B.x) * (pz - B.z) - (C.z - B.z) * (px - B.x)) / area;
                        float w1 = ((A.x - C.x) * (pz - C.z) - (A.z - C.z) * (px - C.x)) / area;
                        float w2 = 1f - w0 - w1;
                        if (w0 < -1e-4f || w1 < -1e-4f || w2 < -1e-4f) continue;
                        int i = iz * b.w + ix;
                        float y = w0 * A.y + w1 * B.y + w2 * C.y;
                        b.inside[i] = true; b.surfY[i] = Mathf.Max(b.surfY[i], y);
                    }
            }
            if (obstacles) Physics.SyncTransforms();
            int wetCount = 0, rocks = 0;
            b.minY = float.MaxValue; b.maxY = float.MinValue;
            var hits = new RaycastHit[16];
            for (int i = 0; i < n; i++)
            {
                if (!b.inside[i]) continue;
                var p = b.World(i);
                b.minY = Mathf.Min(b.minY, p.y); b.maxY = Mathf.Max(b.maxY, p.y);
                float d = TerrainY(p.x, p.z, out var ty) ? p.y - ty : 1f;
                if (obstacles && d > 0.02f)
                {
                    int hc = Physics.RaycastNonAlloc(new Ray(new Vector3(p.x, p.y + 4f, p.z), Vector3.down), hits, 4.3f, ~0, QueryTriggerInteraction.Ignore);
                    for (int k = 0; k < hc; k++)
                    {
                        var c = hits[k].collider;
                        if (!c || c is TerrainCollider || c.attachedRigidbody || c is CharacterController || c.GetComponentInParent<Animator>()) continue;
                        if (c.transform.IsChildOf(r.transform)) continue;
                        if (hits[k].point.y > p.y - 0.15f) { d = 0f; rocks++; break; }
                    }
                }
                b.depth[i] = Mathf.Max(0f, d);
                b.wet[i] = d > 0.02f;
                if (b.wet[i]) wetCount++;
            }
            string lower = r.name.ToLowerInvariant();
            b.calm = lower.Contains("pond") || lower.Contains("lake") || (!lower.Contains("stream") && !lower.Contains("river") && b.maxY - b.minY < 0.3f);
            L($"{r.name}: flow grid {b.w}x{b.h} at {F(texel)} m, {wetCount} wet texels ({F(wetCount * texel * texel)} m2), surface {F(b.minY)} .. {F(b.maxY)} m, {(b.calm ? "calm" : "flowing")}" + (rocks > 0 ? $", {rocks} texels blocked by colliders (rocks)" : ""));
            return b;
        }

        static void BankDistance(Body b, List<Body> flowing)
        {
            int n = b.w * b.h;
            var dry = new bool[n];
            for (int i = 0; i < n; i++)
            {
                dry[i] = !b.wet[i];
                // a calm pond has no bank where a stream keeps flowing into it
                if (dry[i] && b.calm && !b.inside[i])
                {
                    var p = b.World(i);
                    foreach (var f in flowing) if (f.Cell(p.x, p.z, out int j) && f.wet[j]) { dry[i] = false; break; }
                }
            }
            var d = Edt(dry, b.w, b.h);
            b.bank = new float[n];
            for (int i = 0; i < n; i++) b.bank[i] = d[i] * b.texel;
        }

        // simple binary heap for Dijkstra
        class Heap
        {
            readonly List<(float k, int v)> a = new List<(float, int)>();
            public int Count => a.Count;
            public void Push(float k, int v)
            {
                a.Add((k, v)); int i = a.Count - 1;
                while (i > 0) { int p = (i - 1) / 2; if (a[p].k <= a[i].k) break; (a[p], a[i]) = (a[i], a[p]); i = p; }
            }
            public (float k, int v) Pop()
            {
                var top = a[0]; var last = a[a.Count - 1]; a.RemoveAt(a.Count - 1);
                if (a.Count > 0)
                {
                    a[0] = last; int i = 0;
                    while (true)
                    {
                        int l = i * 2 + 1, r = l + 1, m = i;
                        if (l < a.Count && a[l].k < a[m].k) m = l;
                        if (r < a.Count && a[r].k < a[m].k) m = r;
                        if (m == i) break;
                        (a[m], a[i]) = (a[i], a[m]); i = m;
                    }
                }
                return top;
            }
        }

        /// <summary>downstream direction = gradient of the distance along the channel from the highest water; speed from the slope.
        /// Runs over the whole mesh footprint (not only the wet texels): a bump of bank above the water must not cut the stream in two.</summary>
        static void FlowField(Body b)
        {
            int n = b.w * b.h, w = b.w, h = b.h;
            var chan = b.inside;
            b.flow = new Vector2[n];
            var g = new float[n];
            for (int i = 0; i < n; i++) g[i] = float.MaxValue;
            var heap = new Heap();
            int sources = 0;
            for (int i = 0; i < n; i++) if (chan[i] && b.surfY[i] >= b.maxY - 0.05f) { g[i] = 0f; heap.Push(0f, i); sources++; }
            int[] dx = { 1, -1, 0, 0, 1, 1, -1, -1 }, dz = { 0, 0, 1, -1, 1, -1, 1, -1 };
            float[] cost = { 1f, 1f, 1f, 1f, 1.4142f, 1.4142f, 1.4142f, 1.4142f };
            while (heap.Count > 0)
            {
                var (k, v) = heap.Pop();
                if (k > g[v]) continue;
                int x = v % w, z = v / w;
                for (int d = 0; d < 8; d++)
                {
                    int nx = x + dx[d], nz = z + dz[d];
                    if (nx < 0 || nz < 0 || nx >= w || nz >= h) continue;
                    int u = nz * w + nx;
                    if (!chan[u]) continue;
                    float nk = k + cost[d];
                    if (nk < g[u]) { g[u] = nk; heap.Push(nk, u); }
                }
            }
            float G(int x, int z, int fx, int fz)
            {
                if (x < 0 || z < 0 || x >= w || z >= h) return g[fz * w + fx];
                int i = z * w + x;
                if (!chan[i] || g[i] == float.MaxValue) return g[fz * w + fx];
                return g[i];
            }
            var dir = new Vector2[n];
            int reached = 0;
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                {
                    int i = z * w + x;
                    if (!chan[i] || g[i] == float.MaxValue) continue;
                    if (b.wet[i]) reached++;
                    var gr = new Vector2(G(x + 1, z, x, z) - G(x - 1, z, x, z), G(x, z + 1, x, z) - G(x, z - 1, x, z));
                    dir[i] = gr.sqrMagnitude > 1e-6f ? gr.normalized : Vector2.zero;
                }
            // smooth the directions over the channel, then renormalise
            int rad = Mathf.Max(1, Mathf.RoundToInt(0.8f / b.texel));
            for (int pass = 0; pass < 2; pass++)
            {
                var nd = new Vector2[n];
                for (int z = 0; z < h; z++)
                    for (int x = 0; x < w; x++)
                    {
                        int i = z * w + x; if (!chan[i]) continue;
                        Vector2 s = Vector2.zero;
                        for (int oz = -rad; oz <= rad; oz++)
                            for (int ox = -rad; ox <= rad; ox++)
                            {
                                int xx = x + ox, zz = z + oz;
                                if (xx < 0 || zz < 0 || xx >= w || zz >= h) continue;
                                int j = zz * w + xx; if (chan[j]) s += dir[j];
                            }
                        nd[i] = s.sqrMagnitude > 1e-6f ? s.normalized : dir[i];
                    }
                dir = nd;
            }
            // speed: 0.35 on the flat, full on a ~15% slope, slower along the banks
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                {
                    int i = z * w + x; if (!chan[i]) continue;
                    int xa = Mathf.Min(x + 1, w - 1), xb = Mathf.Max(x - 1, 0), za = Mathf.Min(z + 1, h - 1), zb = Mathf.Max(z - 1, 0);
                    float sy = b.surfY[i];
                    float gx = ((chan[z * w + xa] ? b.surfY[z * w + xa] : sy) - (chan[z * w + xb] ? b.surfY[z * w + xb] : sy)) / ((xa - xb) * b.texel + 1e-5f);
                    float gz = ((chan[za * w + x] ? b.surfY[za * w + x] : sy) - (chan[zb * w + x] ? b.surfY[zb * w + x] : sy)) / ((za - zb) * b.texel + 1e-5f);
                    float downhill = -(gx * dir[i].x + gz * dir[i].y);
                    float speed = Mathf.Clamp01(0.35f + Mathf.Max(0f, downhill) * 4.5f) * Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(b.bank[i] / 1.5f));
                    b.flow[i] = dir[i] * speed;
                }
            int wet = b.wet.Count(x => x);
            L($"{b.renderer.name}: flow from {sources} source texels at the top, {reached} of {wet} wet texels reached" + (reached < wet * 0.9f ? " (WARNING: part of the water is not connected to the top of the mesh)" : ""));
        }

        /// <summary>calm water: still, except a fan of inflow where a stream enters</summary>
        static void CalmFlow(Body b, List<Body> flowing)
        {
            int n = b.w * b.h;
            b.flow = new Vector2[n];
            Vector3 sumP = Vector3.zero; Vector2 sumD = Vector2.zero; int cnt = 0;
            foreach (var f in flowing)
                for (int j = 0; j < f.w * f.h; j++)
                {
                    if (!f.wet[j]) continue;
                    var p = f.World(j);
                    if (b.Cell(p.x, p.z, out int i) && b.wet[i]) { sumP += p; sumD += f.flow[j]; cnt++; }
                }
            if (cnt == 0) { L($"{b.renderer.name}: calm, no inflow"); return; }
            var inlet = sumP / cnt; var inDir = sumD.sqrMagnitude > 1e-6f ? sumD.normalized : Vector2.zero;
            for (int i = 0; i < n; i++)
            {
                if (!b.wet[i]) continue;
                var p = b.World(i);
                var off = new Vector2(p.x - inlet.x, p.z - inlet.z); float dist = off.magnitude;
                var d = (inDir * 0.6f + (dist > 0.01f ? off / dist : inDir) * 0.4f).normalized;
                b.flow[i] = d * (0.45f * Mathf.Exp(-dist / 7f));
            }
            L($"{b.renderer.name}: calm with inflow at {inlet} from {cnt} stream texels");
        }

        /// <summary>stream texels inside a calm pond fade out over 1.5 m, so the pond draws the water there</summary>
        static void Handover(Body f, List<Body> calm)
        {
            int n = f.w * f.h, cnt = 0;
            for (int i = 0; i < n; i++)
            {
                if (!f.wet[i]) continue;
                var p = f.World(i);
                foreach (var c in calm)
                    if (c.Cell(p.x, p.z, out int j) && c.wet[j])
                    {
                        f.handover[i] = true; f.calmFade[i] = Mathf.Min(f.calmFade[i], 1f - Mathf.Clamp01(c.bank[j] / 1.5f)); cnt++;
                        break;
                    }
            }
            if (cnt > 0) L($"{f.renderer.name}: {F(cnt * f.texel * f.texel)} m2 overlap a calm body and fade out there");
        }

        static void EncodeFlow(Body b)
        {
            int n = b.w * b.h;
            var px = new Color32[n];
            for (int i = 0; i < n; i++)
            {
                if (!b.inside[i] && !b.wet[i]) { px[i] = new Color32(128, 128, 0, 0); continue; }
                var fl = b.flow != null ? b.flow[i] : Vector2.zero;
                float depth = b.wet[i] ? b.depth[i] * b.calmFade[i] : 0f;
                px[i] = new Color32(B01(fl.x * 0.5f + 0.5f), B01(fl.y * 0.5f + 0.5f), B01(b.bank[i] / BankRange), B01(depth / DepthRange));
            }
            b.tex = SaveDataTexture($"{GenDir}/T_Water_Flow_{b.key}.asset", b.w, b.h, px);
            b.rect = new Vector4(b.ox, b.oz, 1f / (b.w * b.texel), 1f / (b.h * b.texel));
        }

        // ------------------------------------------------------------------ textures + materials
        static Texture2D ImportTexture(string file, bool normal, bool mips, bool compressed)
        {
            string path = $"{ArtRoot}/{file}";
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (!ti) { L($"MISSING {path}: copy it from src_assets/Water (tools/water_textures.py)"); return null; }
            bool dirty = false;
            void Set<T>(T now, T want, Action<T> apply) { if (!EqualityComparer<T>.Default.Equals(now, want)) { apply(want); dirty = true; } }
            Set(ti.textureType, normal ? TextureImporterType.NormalMap : TextureImporterType.Default, v => ti.textureType = v);
            Set(ti.sRGBTexture, false, v => ti.sRGBTexture = v);
            Set(ti.wrapMode, TextureWrapMode.Repeat, v => ti.wrapMode = v);
            Set(ti.mipmapEnabled, mips, v => ti.mipmapEnabled = v);
            Set(ti.filterMode, FilterMode.Bilinear, v => ti.filterMode = v);
            Set(ti.anisoLevel, mips ? 4 : 0, v => ti.anisoLevel = v);
            Set(ti.textureCompression, compressed ? TextureImporterCompression.CompressedHQ : TextureImporterCompression.Uncompressed, v => ti.textureCompression = v);
            Set(ti.maxTextureSize, 512, v => ti.maxTextureSize = v);
            if (!normal)
            {
                Set(ti.alphaSource, TextureImporterAlphaSource.FromInput, v => ti.alphaSource = v);
                Set(ti.alphaIsTransparency, false, v => ti.alphaIsTransparency = v);
            }
            if (dirty) { ti.SaveAndReimport(); L($"import settings: {file} ({(normal ? "normal map" : "linear data")}, {(mips ? "mips" : "no mips")}, {(compressed ? "compressed" : "uncompressed")})"); }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material Mat(string name, Shader sh, bool reset, out bool fresh)
        {
            string path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            fresh = false;
            if (!m) { m = new Material(sh) { name = name }; AssetDatabase.CreateAsset(m, path); fresh = true; L("material " + path); }
            else if (m.shader != sh || reset)
            {
                m.shader = sh;
                if (reset) { var d = new Material(sh); m.CopyPropertiesFromMaterial(d); Object.DestroyImmediate(d); fresh = true; L(name + ": numbers reset to the defaults"); }
            }
            m.renderQueue = -1; m.shaderKeywords = new string[0]; m.enableInstancing = true;
            return m;
        }

        static void SetTex(Material m, string prop, Texture t) { if (t && m.HasProperty(prop)) m.SetTexture(prop, t); }

        static void Configure(Renderer r)
        {
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = true;
            r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }

        static void DestroyNamed(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true).Where(x => x.name == name).ToArray())
                    if (t) Object.DestroyImmediate(t.gameObject);
        }

        // ------------------------------------------------------------------ Build
        [PrimalBridgeCommand]
        public static string Build(string arg)
        {
            Log.Clear();
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "stop Play mode first";
            var a = Args(arg);
            bool reset = a.ContainsKey("reset"), obstacles = !a.ContainsKey("noObstacles");
            float shoreStep = ArgF(a, "shoreStep", 1f, 0.5f, 4f), oceanStep = ArgF(a, "oceanStep", 5f, 2f, 20f), flowTexel = ArgF(a, "flowTexel", 0.25f, 0.1f, 2f);
            int mapRes = Mathf.RoundToInt(ArgF(a, "mapRes", 1024f, 128f, 2048f));
            var clock = System.Diagnostics.Stopwatch.StartNew();

            if (!ShaderOk(OceanShader, out var shOcean) | !ShaderOk(ShoreShader, out var shShore) | !ShaderOk(FlowShader, out var shFlow))
            { L("nothing changed: fix the shader errors first"); return Log.ToString(); }
            if (!IslandOpen(out var scene, out bool wasDirty)) return Log.ToString();
            EnsureFolder(ArtRoot); EnsureFolder(GenDir); EnsureFolder(MatDir);
            var tWaves = ImportTexture(TexWaves, true, true, true);
            var tRipples = ImportTexture(TexRipples, true, true, true);
            var tFoam = ImportTexture(TexFoam, false, true, true);
            var tRain = ImportTexture(TexRain, false, false, false);
            var tStreaks = ImportTexture(TexStreaks, false, true, true);
            if (!tWaves || !tRipples || !tFoam || !tRain || !tStreaks) { L("nothing changed: textures missing"); return Log.ToString(); }

            CollectTerrains();
            if (_terrains.Length == 0) { L("no terrain in the scene: nothing changed"); return Log.ToString(); }

            // ---- find the water
            var oceanGo = GameObject.Find("ENV_Ocean");
            var oceanRenderer = oceanGo ? oceanGo.GetComponent<MeshRenderer>() : null;
            if (!oceanRenderer)
                oceanRenderer = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .FirstOrDefault(r => r.sharedMaterial && r.sharedMaterial.name == "M_Ocean" && r.name != OceanObject);
            var shore = Object.FindFirstObjectByType<OceanShore>(FindObjectsInactive.Include);
            float seaLevel = shore ? shore.seaLevel : oceanRenderer ? oceanRenderer.transform.position.y : 0f;
            Transform waterRoot = oceanRenderer ? oceanRenderer.transform.parent : null;
            if (!waterRoot) { var w = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Water"); waterRoot = w ? w.transform : new GameObject("Water").transform; }

            var fresh = new List<(Renderer r, MeshFilter mf)>();
            foreach (var ws in Object.FindObjectsByType<WaterSource>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!ws.fresh || !ws.isActiveAndEnabled) continue;                  // switched-off bodies (the old stream) are skipped
                var mf = ws.surface ? ws.surface : ws.GetComponentInChildren<MeshFilter>();
                var r = mf ? mf.GetComponent<Renderer>() : null;
                if (r && !fresh.Any(f => f.r == r)) fresh.Add((r, mf));
            }
            foreach (var n in new[] { "ENV_Pond_Water", "ENV_Stream_Water" })
            {
                var go = GameObject.Find(n); if (!go) continue;
                var mf = go.GetComponentInChildren<MeshFilter>(); var r = mf ? mf.GetComponent<Renderer>() : null;
                if (r && !fresh.Any(f => f.r == r)) fresh.Add((r, mf));
            }
            L($"sea level {F(seaLevel)} m; old sea: {(oceanRenderer ? PathOf(oceanRenderer.transform) : "none")}; fresh water: {string.Join(", ", fresh.Select(f => f.r.name))}");

            // ---- record the originals BEFORE changing anything (first record wins)
            var rec = LoadRecord();
            void Remember(Renderer r)
            {
                string p = PathOf(r.transform);
                if (rec.renderers.Any(x => x.path == p)) return;
                rec.renderers.Add(new RendererRecord { path = p, materials = r.sharedMaterials.Select(MatKey).ToArray(), enabled = r.enabled });
            }
            if (oceanRenderer) Remember(oceanRenderer);
            foreach (var f in fresh) Remember(f.r);
            if (!waterRoot.GetComponent<VFX.WaterGlobals>()) rec.addedGlobals = true;
            rec.builtAt = DateTime.Now.ToString("s");
            SaveRecord(rec);

            // ---- bake + generate
            var map = BakeShoreMap(seaLevel, mapRes);
            var island = TerrainRect();
            var oceanMesh = BuildOceanMesh(island, oceanStep, 1500f);
            DestroyNamed(scene, ShoreObject);                                  // before its meshes are replaced
            var shoreMeshes = BuildShoreMeshes(seaLevel, shoreStep);

            var bodies = new List<Body>();
            foreach (var f in fresh) { var b = Rasterize(f.r, f.mf, flowTexel, obstacles); if (b != null) bodies.Add(b); }
            var calm = bodies.Where(b => b.calm).ToList(); var flowing = bodies.Where(b => !b.calm).ToList();
            foreach (var b in calm) BankDistance(b, flowing);
            foreach (var b in flowing) BankDistance(b, flowing);
            foreach (var b in flowing) { FlowField(b); Handover(b, calm); }
            foreach (var b in calm) CalmFlow(b, flowing);
            foreach (var b in bodies) EncodeFlow(b);

            // ---- materials
            var mOcean = Mat("M_Water_Ocean", shOcean, reset, out _);
            SetTex(mOcean, "_ShoreMap", map.tex); mOcean.SetVector("_ShoreMapRect", map.rect); mOcean.SetFloat("_SeaLevel", seaLevel);
            SetTex(mOcean, "_WaveNormal", tWaves); SetTex(mOcean, "_RippleNormal", tRipples); SetTex(mOcean, "_FoamTex", tFoam); SetTex(mOcean, "_RainRippleTex", tRain);
            EditorUtility.SetDirty(mOcean);
            var mShore = Mat("M_Water_Shore", shShore, reset, out _);
            SetTex(mShore, "_ShoreMap", map.tex); mShore.SetVector("_ShoreMapRect", map.rect);
            SetTex(mShore, "_FoamTex", tFoam); SetTex(mShore, "_RippleNormal", tRipples);
            mShore.SetFloat("_ShoreWaveLength", mOcean.GetFloat("_ShoreWaveLength"));                // the foam must ride the ocean's waves
            mShore.SetFloat("_ShoreWavePeriod", mOcean.GetFloat("_ShoreWavePeriod"));
            EditorUtility.SetDirty(mShore);
            var flowMats = new Dictionary<Renderer, Material>();
            foreach (var b in bodies)
            {
                var m = Mat("M_Water_" + b.key, shFlow, reset, out bool isNew);
                if (isNew && b.calm)
                {
                    // calm pond: a near mirror with slow broad swell, soft ripples, no white water, strong rain rings
                    m.SetFloat("_FlowSpeed", 0.4f); m.SetFloat("_WindDrift", 0.06f); m.SetFloat("_Stretch", 1.4f);
                    m.SetFloat("_StreakStrength", 0.1f); m.SetFloat("_RippleStrength", 0.06f); m.SetFloat("_WaveStrength", 0.06f);
                    m.SetFloat("_BankFoam", 0f); m.SetFloat("_RapidsFoam", 0f); m.SetFloat("_ShallowFoam", 0f);
                    m.SetFloat("_RainStrength", 1f); m.SetFloat("_RainTiling", 2.2f); m.SetFloat("_Smoothness", 0.96f); m.SetFloat("_CausticsStrength", 0.35f);
                    m.SetFloat("_ReflectionStrength", 1f);
                    m.SetVector("_AbsorbColor", new Vector4(0.75f, 0.3f, 0.24f, 0f)); m.SetColor("_ScatterColor", new Color(0.055f, 0.12f, 0.11f, 1f));
                }
                else if (isNew)
                {
                    m.SetFloat("_RainStrength", 0.5f);
                }
                SetTex(m, "_FlowMap", b.tex); m.SetVector("_FlowMapRect", b.rect);
                m.SetFloat("_BankRange", BankRange); m.SetFloat("_DepthRange", DepthRange);
                SetTex(m, "_WaveNormal", tWaves); SetTex(m, "_RippleNormal", tRipples); SetTex(m, "_FoamTex", tFoam); SetTex(m, "_RainRippleTex", tRain);
                SetTex(m, "_StreakTex", tStreaks);
                EditorUtility.SetDirty(m);
                flowMats[b.renderer] = m;
            }

            // ---- scene
            DestroyNamed(scene, OceanObject);
            var ocean = new GameObject(OceanObject);
            ocean.transform.SetParent(waterRoot, false);
            ocean.transform.position = new Vector3(0f, seaLevel, 0f);
            ocean.AddComponent<MeshFilter>().sharedMesh = oceanMesh;
            var or = ocean.AddComponent<MeshRenderer>(); or.sharedMaterial = mOcean; Configure(or); or.allowOcclusionWhenDynamic = false;
            if (oceanRenderer) { oceanRenderer.enabled = false; L($"{oceanRenderer.name}: renderer off (recorded; Revert turns it back on)"); }

            var shoreRoot = new GameObject(ShoreObject);
            shoreRoot.transform.SetParent(waterRoot, false);
            shoreRoot.transform.position = new Vector3(0f, seaLevel, 0f);
            foreach (var mesh in shoreMeshes)
            {
                var go = new GameObject(mesh.name.Replace("ME_", "PF_"));
                go.transform.SetParent(shoreRoot.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mShore; Configure(r);
            }
            foreach (var kv in flowMats)
            {
                var mats = kv.Key.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = kv.Value;
                kv.Key.sharedMaterials = mats;                                   // only the materials: Revert restores exactly this
                kv.Key.enabled = true;
                L($"{kv.Key.name}: {kv.Value.name}");
            }
            if (!waterRoot.GetComponent<VFX.WaterGlobals>()) { waterRoot.gameObject.AddComponent<VFX.WaterGlobals>(); L($"{waterRoot.name}: WaterGlobals added (rain rings, PC depth / refraction)"); }

            AssetDatabase.SaveAssets();
            SaveScene(scene, wasDirty);
            L($"done in {F((float)clock.Elapsed.TotalSeconds)} s. Check: PrimalWaterBuilder.Preview; undo: PrimalWaterBuilder.Revert");
            return Log.ToString();
        }

        // ------------------------------------------------------------------ Revert
        [PrimalBridgeCommand]
        public static string Revert(string arg)
        {
            Log.Clear();
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "stop Play mode first";
            if (!IslandOpen(out var scene, out bool wasDirty)) return Log.ToString();
            var rec = LoadRecord();
            int restored = 0;
            foreach (var r in rec.renderers)
            {
                var t = FindPath(scene, r.path);
                var ren = t ? t.GetComponent<Renderer>() : null;
                if (!ren) { L("not found: " + r.path); continue; }
                var mats = (r.materials ?? new string[0]).Select(LoadMat).ToArray();
                if (mats.Length > 0 && mats.All(m => m)) ren.sharedMaterials = mats;
                else if (mats.Length > 0) L($"{r.path}: a recorded material is missing, materials left as they are");
                ren.enabled = r.enabled;
                restored++;
                L($"{r.path}: restored ({string.Join(", ", mats.Select(m => m ? m.name : "missing"))}, {(r.enabled ? "on" : "off")})");
            }
            DestroyNamed(scene, OceanObject); DestroyNamed(scene, ShoreObject);
            L($"removed {OceanObject} / {ShoreObject}");
            if (rec.addedGlobals || !File.Exists(RecordPath))
                foreach (var g in Object.FindObjectsByType<VFX.WaterGlobals>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    L($"{g.name}: WaterGlobals removed");
                    if (g.gameObject.GetComponents<Component>().Length <= 2 && g.transform.childCount == 0 && g.gameObject.name == "WaterGlobals") Object.DestroyImmediate(g.gameObject);
                    else Object.DestroyImmediate(g);
                }
            if (File.Exists(RecordPath)) AssetDatabase.DeleteAsset(RecordPath);
            if (Args(arg).ContainsKey("purge"))
            {
                if (AssetDatabase.IsValidFolder(GenDir)) AssetDatabase.DeleteAsset(GenDir);
                if (AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.DeleteAsset(MatDir);
                L("purged " + GenDir + " and " + MatDir);
            }
            AssetDatabase.SaveAssets();
            SaveScene(scene, wasDirty);
            L($"{restored} renderer(s) restored");
            return Log.ToString();
        }

        // ------------------------------------------------------------------ Preview
        static readonly int WaterTimeId = Shader.PropertyToID("_PF_WaterTime"), RainId = Shader.PropertyToID("_PF_Rain"), WetId = Shader.PropertyToID("_PF_Wetness"),
                            DebugId = Shader.PropertyToID("_PF_WaterDebug"),
                            DepthFlagId = Shader.PropertyToID("_PF_WaterSceneDepth"), RefractFlagId = Shader.PropertyToID("_PF_WaterRefraction");

        struct Shot { public string name; public Vector3 eye, look; public float fov, time, rain; }

        /// <summary>arg (';' separated, all optional): hour of day ("15" or "hour=15"), "only=pond+stream" (shot name
        /// prefixes), "debug=N" (shader debug view: 1 depth / scene depth / bank, 2 reflection, 3 alpha, 4 speed / foam / fresnel,
        /// 5 body colour; files get a _dbgN suffix), "suffix=_v2"; writes Documentation/Screenshots/Water/*.png</summary>
        [PrimalBridgeCommand]
        public static string Preview(string arg)
        {
            Log.Clear();
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "stop Play mode first";
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != IslandScene)
            {
                if (scene.isDirty) return "active scene has unsaved changes; open Island_VerticalSlice first";
                scene = EditorSceneManager.OpenScene(IslandScene, OpenSceneMode.Single);
            }
            CollectTerrains();
            var shots = new List<Shot>();
            var shore = Object.FindFirstObjectByType<OceanShore>(FindObjectsInactive.Include);
            float sea = shore ? shore.seaLevel : 0f;
            var mOcean = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_Water_Ocean.mat");
            float period = mOcean && mOcean.HasProperty("_ShoreWavePeriod") ? mOcean.GetFloat("_ShoreWavePeriod") : 6.5f;

            float Ground(Vector3 p) => TerrainY(p.x, p.z, out var y) ? y : sea;
            Vector3 Eye(Vector3 p, float above) { p.y = Mathf.Max(Ground(p), sea) + above; return p; }

            // beach: the waterline closest to the player spawn
            var spawn = GameObject.Find("ZONE_PlayerSpawn"); var cam0 = Camera.main;
            Vector3 origin = spawn ? spawn.transform.position : cam0 ? cam0.transform.position : Vector3.zero;
            if (FindWaterline(origin, sea, out var wl, out var seaward))
            {
                var along = new Vector3(-seaward.z, 0f, seaward.x);
                for (int k = 0; k < 3; k++)
                    shots.Add(new Shot { name = $"beach_close_{k}", eye = Eye(wl - seaward * 9f + along * 2f, 2.3f), look = new Vector3(wl.x, sea, wl.z) + seaward * 5f, fov = 55f, time = 100f + k * period / 3f });
                shots.Add(new Shot { name = "beach_low", eye = Eye(wl - seaward * 4.5f, 1.7f), look = new Vector3(wl.x, sea + 0.2f, wl.z) + along * 14f + seaward * 1.5f, fov = 60f, time = 100f + period * 0.2f });
                shots.Add(new Shot { name = "beach_wide", eye = Eye(wl - seaward * 22f - along * 12f, 8f), look = new Vector3(wl.x, sea, wl.z) + seaward * 12f + along * 6f, fov = 55f, time = 100f });
                L($"beach: waterline {wl}, seaward {seaward}");
            }
            else L("beach: no waterline found within 160 m of the spawn");

            // stream: the fastest part of its flow map; pond: from the side
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                var m = r.sharedMaterial;
                if (!m || !m.shader || m.shader.name != FlowShader || !m.HasProperty("_FlowMap")) continue;
                var fm = m.GetTexture("_FlowMap") as Texture2D; var rect = m.GetVector("_FlowMapRect");
                var bounds = r.bounds;
                string key = BodyKey(r.name).ToLowerInvariant();
                bool isCalm = m.GetFloat("_FlowSpeed") < 0.8f;
                if (!isCalm && fm && fm.isReadable && rect.z > 0f)
                {
                    var px = fm.GetPixels32(); int best = -1; float bs = -1f;
                    for (int i = 0; i < px.Length; i++)
                    {
                        if (px[i].a < 60 || px[i].b < 110) continue;               // deep enough, away from the bank
                        float fx = px[i].r / 127.5f - 1f, fz = px[i].g / 127.5f - 1f, s = fx * fx + fz * fz;
                        if (s > bs) { bs = s; best = i; }
                    }
                    if (best >= 0)
                    {
                        float x = rect.x + (best % fm.width + 0.5f) / fm.width / rect.z, z = rect.y + (best / fm.width + 0.5f) / fm.height / rect.w;
                        var dir = new Vector3(px[best].r / 127.5f - 1f, 0f, px[best].g / 127.5f - 1f).normalized;
                        var side = new Vector3(-dir.z, 0f, dir.x);
                        float y = SurfaceY(r, x, z, bounds.center.y);
                        var c = new Vector3(x, y, z);
                        shots.Add(new Shot { name = $"{key}_rapids", eye = Eye(c + side * 6f - dir * 3f, 3f), look = c + dir * 2f, fov = 55f, time = 100f });
                        shots.Add(new Shot { name = $"{key}_downstream", eye = Eye(c - dir * 9f + side * 1.5f, 2.2f), look = c + dir * 5f, fov = 60f, time = 100f });
                        continue;
                    }
                }
                var ctr = bounds.center; float rad = Mathf.Max(bounds.extents.x, bounds.extents.z);
                var from = ctr + new Vector3(-0.7f, 0f, -0.7f) * rad * 1.2f;
                shots.Add(new Shot { name = key, eye = Eye(from, 4f), look = ctr, fov = 55f, time = 100f });
                if (isCalm) shots.Add(new Shot { name = key + "_rain", eye = Eye(ctr + new Vector3(-0.4f, 0f, -0.4f) * rad, 2.5f), look = ctr, fov = 55f, time = 100f, rain = 1f });
            }
            if (shots.Count == 0) { L("nothing to capture (run PrimalWaterBuilder.Build first)"); return Log.ToString(); }

            // render
            var tm = Object.FindFirstObjectByType<Core.TimeManager>(); float oldHour = tm ? tm.hour : 0f;
            float hour = -1f;
            foreach (var tok in (arg ?? "").Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                if (float.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out var hv)) hour = hv;
            var pa = Args(arg);
            hour = ArgF(pa, "hour", hour, -1f, 24f);
            int debug = Mathf.RoundToInt(ArgF(pa, "debug", 0f, 0f, 9f));
            float dt = ArgF(pa, "dt", 0f, -1000f, 1000f);                                     // shift the water clock (motion checks)
            if (dt != 0f) shots = shots.Select(sh => { sh.time += dt; return sh; }).ToList();
            string suffix = pa.TryGetValue("suffix", out var sfx) ? sfx : debug > 0 ? "_dbg" + debug : "";
            if (pa.TryGetValue("only", out var only) && !string.IsNullOrEmpty(only))
            {
                var keep = only.Split('+');
                shots = shots.Where(sh => keep.Any(k => sh.name.StartsWith(k, StringComparison.OrdinalIgnoreCase))).ToList();
            }
            float oldDebug = Shader.GetGlobalFloat(DebugId);
            // "keep": leave the hour set afterwards. In edit mode the procedural sky follows the sun and Unity rebuilds the
            // sky reflection over the next editor frames, so for stable captures run Preview "15;keep" once, then again.
            // Put the scene hour back with Preview "9;keep;only=none" (or TimeManager) before saving the scene.
            bool keepHour = pa.ContainsKey("keep");
            var urp = GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            float oldRain = Shader.GetGlobalFloat(RainId), oldWet = Shader.GetGlobalFloat(WetId), oldDepth = Shader.GetGlobalFloat(DepthFlagId), oldRefr = Shader.GetGlobalFloat(RefractFlagId);
            const int W = 1280, H = 720;
            var go = new GameObject("_WaterPreviewCam") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            if (cam0) cam.CopyFrom(cam0);
            cam.enabled = false; cam.nearClipPlane = 0.1f; cam.farClipPlane = 2000f;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            Directory.CreateDirectory(ShotDir);
            bool asyncWas = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
            try
            {
                if (tm && hour >= 0f) { tm.hour = hour; tm.Apply(); }
                Shader.SetGlobalFloat(DepthFlagId, urp && urp.supportsCameraDepthTexture ? 1f : 0f);
                Shader.SetGlobalFloat(RefractFlagId, urp && urp.supportsCameraOpaqueTexture ? 1f : 0f);
                Shader.SetGlobalFloat(DebugId, debug);
                // warm-up: the first renders after a shader (re)compile can miss the water, so render every view once first
                foreach (var s in shots)
                {
                    Shader.SetGlobalFloat(WaterTimeId, s.time); Shader.SetGlobalFloat(RainId, s.rain);
                    cam.transform.SetPositionAndRotation(s.eye, Quaternion.LookRotation((s.look - s.eye).normalized));
                    cam.fieldOfView = s.fov; cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
                }
                foreach (var s in shots)
                {
                    Shader.SetGlobalFloat(WaterTimeId, s.time);
                    Shader.SetGlobalFloat(RainId, s.rain);
                    Shader.SetGlobalFloat(WetId, s.rain > 0f ? 1f : oldWet);
                    cam.transform.SetPositionAndRotation(s.eye, Quaternion.LookRotation((s.look - s.eye).normalized));
                    cam.fieldOfView = s.fov;
                    cam.targetTexture = rt; cam.Render(); cam.Render();
                    var prev = RenderTexture.active; RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
                    RenderTexture.active = prev; cam.targetTexture = null;
                    File.WriteAllBytes($"{ShotDir}/{s.name}{suffix}.png", tex.EncodeToPNG());
                    L($"{s.name}{suffix}.png: eye {s.eye}, look {s.look}, water time {F(s.time)} s{(s.rain > 0f ? ", rain" : "")}");
                }
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = asyncWas;
                Shader.SetGlobalFloat(WaterTimeId, 0f); Shader.SetGlobalFloat(RainId, oldRain); Shader.SetGlobalFloat(WetId, oldWet); Shader.SetGlobalFloat(DebugId, 0f);
                Shader.SetGlobalFloat(DepthFlagId, oldDepth); Shader.SetGlobalFloat(RefractFlagId, oldRefr);
                if (tm && hour >= 0f && !keepHour) { tm.hour = oldHour; tm.Apply(); }
                cam.targetTexture = null; RenderTexture.active = null;
                Object.DestroyImmediate(go); Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt);
            }
            L($"{shots.Count} capture(s) in {ShotDir}");
            return Log.ToString();
        }

        /// <summary>diagnostics: lighting / reflection set-up and, per water body, material, flow map statistics and depth checks</summary>
        [PrimalBridgeCommand]
        public static string Inspect(string arg)
        {
            Log.Clear();
            CollectTerrains();
            var dr = ReflectionProbe.defaultTexture;
            L($"ambient {RenderSettings.ambientMode} x{F(RenderSettings.ambientIntensity)} sky {RenderSettings.ambientSkyColor} equator {RenderSettings.ambientEquatorColor} ground {RenderSettings.ambientGroundColor}");
            L($"reflection mode {RenderSettings.defaultReflectionMode}, intensity {F(RenderSettings.reflectionIntensity)}, bounces {RenderSettings.reflectionBounces}, custom {(RenderSettings.customReflectionTexture ? RenderSettings.customReflectionTexture.name : "none")}, default probe texture {(dr ? dr.name + " " + dr.width + "px" : "NONE")}");
            L($"skybox {(RenderSettings.skybox ? RenderSettings.skybox.name + " (" + RenderSettings.skybox.shader.name + ")" : "none")}, fog {RenderSettings.fog} {RenderSettings.fogMode} {RenderSettings.fogColor}, lighting data {(Lightmapping.lightingDataAsset ? Lightmapping.lightingDataAsset.name : "none")}");
            L($"reflection probes in scene: {Object.FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length}");
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var m = r.sharedMaterial;
                if (!m || !m.shader || !m.shader.name.StartsWith("PF/") || !(m.shader.name.Contains("Water") || m.shader.name.Contains("Ocean"))) continue;
                if (r.name.StartsWith("PF_OceanShore_") && r.name != "PF_OceanShore_00_00") continue;
                var b = r.bounds;
                L($"{PathOf(r.transform)}: {m.name} ({m.shader.name}) enabled {r.enabled}/{r.gameObject.activeInHierarchy}, probes {r.reflectionProbeUsage}, light probes {r.lightProbeUsage}, bounds {b.center} size {b.size}, queue {m.renderQueue}");
                if (!m.HasProperty("_FlowMap")) continue;
                var fm = m.GetTexture("_FlowMap") as Texture2D; var rect = m.GetVector("_FlowMapRect");
                if (!fm || !fm.isReadable) { L("  flow map missing / unreadable"); continue; }
                var px = fm.GetPixels32(); int wet = 0; double dep = 0, bank = 0, spd = 0; int dmax = 0;
                foreach (var c in px) { if (c.a == 0) continue; wet++; dep += c.a; bank += c.b; dmax = Mathf.Max(dmax, c.a); float fx = c.r / 127.5f - 1f, fz = c.g / 127.5f - 1f; spd += Mathf.Sqrt(fx * fx + fz * fz); }
                L($"  flow map {fm.width}x{fm.height} rect {rect}: {wet} texels with water, mean depth {F((float)(dep / Mathf.Max(1, wet) / 255.0 * DepthRange))} m (max {F(dmax / 255f * DepthRange)}), mean bank {F((float)(bank / Mathf.Max(1, wet) / 255.0 * BankRange))} m, mean speed {F((float)(spd / Mathf.Max(1, wet)))}");
                var ctr = b.center; float sy = SurfaceY(r, ctr.x, ctr.z, ctr.y);
                if (TerrainY(ctr.x, ctr.z, out var ty)) L($"  at the bounds centre {ctr}: surface {F(sy)} m, terrain {F(ty)} m, depth {F(sy - ty)} m");
                foreach (var k in new[] { "_FlowSpeed", "_ShallowAlpha", "_DeepAlpha", "_DepthScale", "_EdgeSoftness", "_ReflectionStrength", "_RapidsFoam", "_BankFoam", "_ShallowFoam" })
                    if (m.HasProperty(k)) Log.Append($"  {k}={F(m.GetFloat(k))}");
                Log.AppendLine();
            }
            return Log.ToString();
        }

        /// <summary>nearest point where the terrain dips under the sea, marching out from 'from' in 32 directions</summary>
        static bool FindWaterline(Vector3 from, float sea, out Vector3 point, out Vector3 seaward)
        {
            point = from; seaward = Vector3.forward; float best = float.MaxValue; bool found = false;
            for (int d = 0; d < 32; d++)
            {
                float ang = d * Mathf.PI * 2f / 32f; var dir = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang));
                float prevR = 0f;
                for (float r = 0.5f; r < 160f; r += 0.5f)
                {
                    var p = from + dir * r;
                    if (!TerrainY(p.x, p.z, out var y)) break;
                    if (y < sea)
                    {
                        float lo = prevR, hi = r;                                          // refine the crossing
                        for (int k = 0; k < 12; k++) { float m = (lo + hi) * 0.5f; var q = from + dir * m; if (TerrainY(q.x, q.z, out var qy) && qy < sea) hi = m; else lo = m; }
                        if (hi < best) { best = hi; point = from + dir * hi; point.y = sea; found = true; }
                        break;
                    }
                    prevR = r;
                }
            }
            if (!found) return false;
            // seaward = downhill direction of the terrain around the waterline
            TerrainY(point.x + 2f, point.z, out var ex); TerrainY(point.x - 2f, point.z, out var wx);
            TerrainY(point.x, point.z + 2f, out var nz); TerrainY(point.x, point.z - 2f, out var sz);
            var g = new Vector3(ex - wx, 0f, nz - sz);
            seaward = g.sqrMagnitude > 1e-6f ? -g.normalized : (point - from).normalized;
            return true;
        }

        /// <summary>water surface height under (x, z): a ray against the mesh bounds centre as fallback</summary>
        static float SurfaceY(Renderer r, float x, float z, float fallback)
        {
            var mf = r.GetComponent<MeshFilter>();
            if (!mf || !mf.sharedMesh || !mf.sharedMesh.isReadable) return fallback;
            var v = mf.sharedMesh.vertices; float best = float.MaxValue, y = fallback;
            foreach (var lv in v)
            {
                var w = mf.transform.TransformPoint(lv);
                float d = (w.x - x) * (w.x - x) + (w.z - z) * (w.z - z);
                if (d < best) { best = d; y = w.y; }
            }
            return y;
        }
    }
}
