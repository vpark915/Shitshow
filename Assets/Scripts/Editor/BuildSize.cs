using System.Linq;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

// Tools > Wash Square > Shrink Build: the 1.7 GB build was ~1 GB of textures (the forest pack's at full 4K) and ~0.5 GB
// of shader variants, all stored uncompressed (compression "Default" = none).
// - every build profile compresses with LZ4HC: shader code shrinks several times over, the game loads as fast
// - textures above 2K are capped at 2K (a quarter of the size; not visible at game distances), except the sky photo,
//   which fills the whole screen; normal/mask/smoothness/AO maps at 1K (detail, not colour)
// - only the game's scenes ship: Wash Square first, then the Mechanics Gym and the Tutorial (both opened from its
//   menu); the other gyms are test scenes and stay in the project
// - the blockout model (71 of the build's 105 MB of meshes) is stored at medium mesh compression (~5 mm precision)
static class BuildSize
{
    const int MaxTexture = 2048, MaxDataTexture = 1024;
    // normal, mask, smoothness, AO and height maps: fine detail, not colour, and 369 of the 630 MB of textures at 2K
    static readonly System.Text.RegularExpressions.Regex DataMap = new System.Text.RegularExpressions.Regex(@"(_N\.|Normal|Mask|Metallic|_MT|AO|Roughness|Displacement|Height|_H_|_SM\.|_disp_)");
    const string Sky = "Assets/Sky/", GameScene = "Assets/Scenes/WashSquareHunt.unity", GymScene = "Assets/Scenes/MechanicsGym.unity",
        TutorialScene = "Assets/Scenes/Tutorial.unity";
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
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(GameScene, true), new EditorBuildSettingsScene(GymScene, true), new EditorBuildSettingsScene(TutorialScene, true) };

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

    // Tools > Wash Square > Shrink Sign Art: the Figma sign exports (Resources/StreetSigns) are odd sizes, and Unity can't
    // compress a texture with mipmaps unless its sides are powers of two, so all 40 shipped uncompressed (55 MB). Scaled
    // to the nearest power of two they compress (DXT5) and keep their mipmaps; StreetFurniture.Size gives the tools the
    // Figma proportions, so signs aren't stretched
    [MenuItem("Tools/Wash Square/Shrink Sign Art")]
    static void SignArt()
    {
        int n = 0;
        foreach (string path in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Resources/StreetSigns" }).Select(AssetDatabase.GUIDToAssetPath))
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer) || importer.npotScale == TextureImporterNPOTScale.ToNearest) continue;
            importer.npotScale = TextureImporterNPOTScale.ToNearest;
            importer.SaveAndReimport();
            n++;
        }
        Debug.Log("Shrink Sign Art: " + n + " sign textures scaled to powers of two, so they compress");
    }

    // Tools > Wash Square > Crunch Textures: every compressed (DXT) texture the build ships, crunched: a second, lossy
    // compression on disk, unpacked as it loads, usually half the size or less. Colour at quality 50, normal maps at 75
    // (they show crunch's blockiness more). Not lightmaps (rebaked often, and smooth gradients block badly), HDR, UI or
    // the packages' own. Slow: the crunching happens as each reimports
    [MenuItem("Tools/Wash Square/Crunch Textures")]
    static void Crunch()
    {
        string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        var paths = AssetDatabase.GetDependencies(scenes, true)
            .Concat(AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Resources" }).Select(AssetDatabase.GUIDToAssetPath))
            .Where(p => p.StartsWith("Assets/") && !p.Contains("/Lightmap-") && !p.EndsWith(".exr") && !p.EndsWith(".hdr") && !p.StartsWith(Sky))
            .Distinct().ToList();
        int crunched = 0;
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (string path in paths)
            {
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer) || importer.crunchedCompression) continue;
                if (importer.textureType is not (TextureImporterType.Default or TextureImporterType.NormalMap)) continue;
                if (importer.textureCompression == TextureImporterCompression.Uncompressed) continue;
                TextureImporterPlatformSettings standalone = importer.GetPlatformTextureSettings("Standalone");
                if (standalone.overridden) continue; // set by hand for a reason
                importer.crunchedCompression = true;
                importer.compressionQuality = importer.textureType == TextureImporterType.NormalMap ? 75 : 50;
                importer.SaveAndReimport();
                crunched++;
            }
        }
        finally { AssetDatabase.StopAssetEditing(); }
        Debug.Log("Crunch Textures: " + crunched + " textures crunched");
    }

    // Tools > Wash Square > Trim Render Features: features of the PC pipeline the game doesn't use, each of which doubles
    // some shaders' variants in the build: lens flares (none in the scenes; the screen-space one is at intensity 0),
    // terrain holes (the park has none), and the GPU Resident Drawer, which adds an instanced copy of every lit shader
    // (and keeps the BatchRendererGroup variants: strip those too). Turn the drawer back on if the foliage gets slow
    [MenuItem("Tools/Wash Square/Trim Render Features")]
    static void TrimFeatures()
    {
        var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
        var so = new SerializedObject(asset);
        so.FindProperty("m_SupportDataDrivenLensFlare").boolValue = false;
        so.FindProperty("m_SupportScreenSpaceLensFlare").boolValue = false;
        bool holes = false;
        foreach (string guid in AssetDatabase.FindAssets("t:TerrainData", new[] { "Assets/Scenes" }))
        {
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(AssetDatabase.GUIDToAssetPath(guid));
            bool[,] solid = data.GetHoles(0, 0, data.holesResolution, data.holesResolution);
            foreach (bool s in solid) if (!s) { holes = true; break; }
        }
        if (!holes) so.FindProperty("m_SupportsTerrainHoles").boolValue = false;
        so.FindProperty("m_GPUResidentDrawerMode").intValue = 0; // disabled
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(asset);
        var graphics = new SerializedObject(AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/GraphicsSettings.asset"));
        graphics.FindProperty("m_BrgStripping").intValue = 0; // keep only if Entities Graphics is in (it isn't)
        graphics.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();
        Debug.Log("Trim Render Features: lens flares off, terrain holes " + (holes ? "kept (the terrain has some)" : "off") + ", GPU Resident Drawer off, BRG variants stripped");
    }

    // Tools > Wash Square > Compress Builds: builds from the Build window compress with LZ4HC. Without it the data ships raw
    // (sharedassets*.assets, 967 MB on 10 Nov) instead of packed (data.unity3d, about 650 MB). The setting isn't public
    // API: the editor's internal per-platform compression, and each build profile's m_CompressionType (as Shrink sets)
    [MenuItem("Tools/Wash Square/Compress Builds")]
    static void Compress()
    {
        System.Type type = typeof(BuildProfile).Assembly.GetType("UnityEditor.Compression");
        object lz4hc = System.Enum.Parse(type, "Lz4HC");
        const System.Reflection.BindingFlags any = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
        var get = typeof(EditorUserBuildSettings).GetMethod("GetCompressionType", any, null, new[] { typeof(BuildTargetGroup) }, null);
        var set = typeof(EditorUserBuildSettings).GetMethod("SetCompressionType", any, null, new[] { typeof(BuildTargetGroup), type }, null);
        string before = get != null ? get.Invoke(null, new object[] { BuildTargetGroup.Standalone }).ToString() : "?";
        set?.Invoke(null, new[] { BuildTargetGroup.Standalone, lz4hc });
        string after = get != null ? get.Invoke(null, new object[] { BuildTargetGroup.Standalone }).ToString() : "?";
        int profiles = 0;
        foreach (BuildProfile profile in Resources.FindObjectsOfTypeAll<BuildProfile>())
        {
            var so = new SerializedObject(profile);
            SerializedProperty it = so.GetIterator();
            while (it.Next(true))
                if (it.name == "m_CompressionType") { it.intValue = (int)lz4hc; profiles++; }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(profile);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("Compress Builds: Standalone compression " + before + " -> " + after + (set == null ? " (no SetCompressionType in this Unity)" : "") + "; " + profiles + " build profiles LZ4HC");
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
