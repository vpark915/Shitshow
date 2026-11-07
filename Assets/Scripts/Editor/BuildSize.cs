using System.Linq;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

// Tools > Wash Square > Shrink Build: the 1.7 GB build was ~1 GB of textures (the forest pack's at full 4K) and ~0.5 GB
// of shader variants, all stored uncompressed (compression "Default" = none).
// - every build profile compresses with LZ4HC: shader code shrinks several times over, the game loads as fast
// - textures above 2K are capped at 2K (a quarter of the size; not visible at game distances), except the sky photo,
//   which fills the whole screen; normal/mask/smoothness/AO maps at 1K (detail, not colour)
// - only the two maps ship, Wash Square first (the other gyms are test scenes: they stay in the project)
// - the blockout model (71 of the build's 105 MB of meshes) is stored at medium mesh compression (~5 mm precision)
static class BuildSize
{
    const int MaxTexture = 2048, MaxDataTexture = 1024;
    // normal, mask, smoothness, AO and height maps: fine detail, not colour, and 369 of the 630 MB of textures at 2K
    static readonly System.Text.RegularExpressions.Regex DataMap = new System.Text.RegularExpressions.Regex(@"(_N\.|Normal|Mask|Metallic|_MT|AO|Roughness|Displacement|Height|_H_|_SM\.)");
    const string Sky = "Assets/Sky/", GameScene = "Assets/Scenes/WashSquareHunt.unity", GymScene = "Assets/Scenes/MechanicsGym.unity";
    const string Blockout = "Assets/Blender/WashingtonSquare/washingtonSquareBlockout.blend";

    [MenuItem("Tools/Wash Square/Shrink Build")]
    static void Shrink()
    {
        // ponytail: the compression setting isn't public API: set the profiles' serialized m_CompressionType (inside their
        // platform settings) to the internal UnityEditor.Compression enum's Lz4HC
        System.Type type = typeof(BuildProfile).Assembly.GetType("UnityEditor.Compression");
        int lz4hc = type != null ? (int)System.Enum.Parse(type, "Lz4HC") : -1;
        int profiles = 0;
        BuildProfile[] all = Resources.FindObjectsOfTypeAll<BuildProfile>();
        foreach (BuildProfile profile in all)
        {
            if (lz4hc < 0) break;
            var so = new SerializedObject(profile);
            SerializedProperty it = so.GetIterator();
            bool found = false;
            while (it.Next(true))
                if (it.name == "m_CompressionType") { if (it.propertyType == SerializedPropertyType.Enum) it.enumValueFlag = lz4hc; else it.intValue = lz4hc; found = true; }
            if (!found) continue;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(profile);
            profiles++;
        }

        // Wash Square first (a build starts in scene 0, which was SampleGym; its menu opens the Mechanics Gym)
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(GameScene, true), new EditorBuildSettingsScene(GymScene, true) };

        var blockout = (ModelImporter)AssetImporter.GetAtPath(Blockout);
        if (blockout.meshCompression != ModelImporterMeshCompression.Medium)
        {
            blockout.meshCompression = ModelImporterMeshCompression.Medium;
            blockout.SaveAndReimport(); // ~3 min: BlockoutPostprocessor re-textures it as before
        }

        int capped = 0;
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (string path in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath).Distinct())
            {
                int max = DataMap.IsMatch(System.IO.Path.GetFileName(path)) ? MaxDataTexture : MaxTexture;
                if (path.StartsWith(Sky) || path.Contains("/Lightmap-") || !(AssetImporter.GetAtPath(path) is TextureImporter importer) || importer.maxTextureSize <= max) continue;
                importer.maxTextureSize = max;
                foreach (var platform in new[] { "Standalone" })
                {
                    TextureImporterPlatformSettings s = importer.GetPlatformTextureSettings(platform);
                    if (s.overridden && s.maxTextureSize > max) { s.maxTextureSize = max; importer.SetPlatformTextureSettings(s); }
                }
                importer.SaveAndReimport();
                capped++;
            }
        }
        finally { AssetDatabase.StopAssetEditing(); }
        AssetDatabase.SaveAssets();
        Debug.Log("Shrink Build: " + profiles + " build profiles LZ4HC (value " + lz4hc + "), " + capped + " textures capped (colour " + MaxTexture + ", data maps " + MaxDataTexture + ")");
    }

    // Tools > Wash Square > Fewer Shader Variants: the PC pipeline's features nothing uses, each doubling every lit
    // shader's variants (the terrain's 3,072 took over an hour to build): light layers, extra (point/spot) lights and
    // their shadows. The game's only light is the sun. Turn them back on here if a scene gets lamps that light up.
    [MenuItem("Tools/Wash Square/Fewer Shader Variants")]
    static void FewerVariants()
    {
        var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
        var so = new SerializedObject(asset);
        so.FindProperty("m_SupportsLightLayers").boolValue = false;
        so.FindProperty("m_AdditionalLightShadowsSupported").boolValue = false;
        so.FindProperty("m_AdditionalLightsRenderingMode").intValue = 0; // disabled
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        Debug.Log("Fewer Shader Variants: light layers, additional lights and their shadows off in PC_RPAsset");
    }

    // Tools > Wash Square > Test Build Size: a macOS build of the build scenes, LZ4HC, into Temp/SizeBuild (not shipped), and its size
    [MenuItem("Tools/Wash Square/Test Build Size")]
    static void TestBuild()
    {
        string path = System.IO.Path.GetFullPath("Temp/SizeBuild/Game.app");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
            locationPathName = path, target = BuildTarget.StandaloneOSX, options = BuildOptions.CompressWithLz4HC
        });
        Debug.Log("Test Build Size: " + report.summary.result + ", " + (report.summary.totalSize / 1e6).ToString("F0") + " MB at " + path);
    }
}
