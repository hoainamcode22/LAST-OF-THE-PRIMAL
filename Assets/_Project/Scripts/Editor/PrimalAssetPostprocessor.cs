using UnityEditor;
using UnityEngine;

namespace PrimalFrontier.EditorTools
{
    /// <summary>
    /// Import rules for everything that comes out of the Blender pipeline (E:\Model game khủng long\scripts\pf_export.py).
    /// Models: metre scale, no cameras/lights/animation, keep Blender custom normals, material remap by name (M_*).
    /// Textures: _D sRGB albedo, _N normal map, _M linear mask (R metal, G AO, B height, A smoothness).
    /// </summary>
    public class PrimalAssetPostprocessor : AssetPostprocessor
    {
        const string ModelsRoot = "Assets/_Project/Art/Models/";
        const string TexturesRoot = "Assets/_Project/Art/Textures/";
        const string TerrainRoot = "Assets/_Project/Art/Terrain/";
        public const string UiRoot = "Assets/_Project/Resources/UI/";

        /// <summary>9-slice border (px) of each UI texture, the same numbers UIStyle uses</summary>
        public static int UiBorder(string file)
        {
            switch (file)
            {
                case "ui_panel": return 28;
                case "ui_leather": case "ui_paper": return 40;
                case "ui_wood": case "ui_stone": return 20;
                case "ui_slot": case "ui_slot_active": return 14;
                case "ui_button": return 16;
                case "ui_bar_back": return 6;
                case "ui_bar_fill": return 3;
                default: return 0;
            }
        }

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(ModelsRoot)) return;
            var mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.bakeAxisConversion = false;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importVisibility = false;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.importBlendShapes = false;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
            mi.materialName = ModelImporterMaterialName.BasedOnMaterialName;
            mi.materialSearch = ModelImporterMaterialSearch.Everywhere;
            mi.addCollider = false;
            mi.isReadable = assetPath.Contains("/Water/");
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.optimizeMeshPolygons = true;
            mi.optimizeMeshVertices = true;
            mi.generateSecondaryUV = false;
        }

        void OnPreprocessTexture()
        {
            var ti = (TextureImporter)assetImporter;
            if (assetPath.StartsWith(TexturesRoot))
            {
                string file = System.IO.Path.GetFileNameWithoutExtension(assetPath);
                ti.mipmapEnabled = true;
                ti.anisoLevel = 4;
                ti.wrapMode = TextureWrapMode.Repeat;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.maxTextureSize = file.Contains("FoliageAtlas") ? 2048 : 1024;
                if (file.EndsWith("_N"))
                {
                    ti.textureType = TextureImporterType.NormalMap;
                    ti.sRGBTexture = false;
                }
                else if (file.EndsWith("_M"))
                {
                    ti.textureType = TextureImporterType.Default;
                    ti.sRGBTexture = false;
                    ti.alphaSource = TextureImporterAlphaSource.FromInput;
                }
                else
                {
                    ti.textureType = TextureImporterType.Default;
                    ti.sRGBTexture = true;
                    ti.alphaSource = TextureImporterAlphaSource.FromInput;
                    ti.alphaIsTransparency = file.Contains("FoliageAtlas");
                    if (file.Contains("FoliageAtlas")) ti.mipMapsPreserveCoverage = true;
                    if (file.Contains("FoliageAtlas")) ti.alphaTestReferenceValue = 0.5f;
                }
            }
            else if (assetPath.StartsWith(UiRoot))
            {
                // UI textures are sprites so the UI saved in the scene can reference them (UIStyle loads them as Sprite)
                string file = System.IO.Path.GetFileNameWithoutExtension(assetPath);
                int b = UiBorder(file);
                ti.textureType = TextureImporterType.Sprite;
                var st = new TextureImporterSettings(); ti.ReadTextureSettings(st);
                st.spriteMode = (int)SpriteImportMode.Single; st.spriteMeshType = SpriteMeshType.FullRect; st.spriteAlignment = (int)SpriteAlignment.Center;
                st.spritePixelsPerUnit = 100f; st.spriteBorder = new Vector4(b, b, b, b); st.spriteGenerateFallbackPhysicsShape = false;
                st.mipmapEnabled = false; st.alphaIsTransparency = true; st.wrapMode = TextureWrapMode.Clamp;
                ti.SetTextureSettings(st);
            }
            else if (assetPath.StartsWith(TerrainRoot))
            {
                // Splat / detail density maps are read byte-exact by PrimalWorldBuilder; keep the imported copy light.
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = false;
                ti.mipmapEnabled = false;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.npotScale = TextureImporterNPOTScale.None;
                ti.maxTextureSize = 2048;
            }
        }
    }
}
