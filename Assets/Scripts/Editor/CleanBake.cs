using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Tools > Wash Square > Clean Daylight Bake: puts the open scene's lighting back to a neutral, clean daylight and bakes
// it from scratch. Everything that tinted it is reset: the sky's tint (0.5 grey is "none" on Skybox/Panoramic, white
// is ~4.6x), the sun's bounce (above 1 it paints its colour into every lightmap), the grading's saturation, and the
// fog's colour, the bloom and the volumetric haze; a touch of warmth in the white balance takes the blue cast of the
// open sky out of the shade. Shadows reach 80 m in realtime and are baked (shadowmask) beyond.
// Then the bake settings (LvlLighting) are set for a clean result, the old bake cleared and a new one started
// (Window > Rendering > Lighting shows its progress; Cancel there stops it).
static class CleanBake
{
    const string Sky = "Assets/Scenes/WashSquareDaySky.mat";

    [MenuItem("Tools/Wash Square/Clean Daylight Bake")]
    static void Apply()
    {
        // ---- sun: daylight white (5600 K), soft full shadows, normal bounce
        Light sun = RenderSettings.sun;
        if (!sun)
            foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) { sun = l; break; }
        if (sun)
        {
            Undo.RecordObject(sun, "Clean Daylight Bake");
            Undo.RecordObject(sun.transform, "Clean Daylight Bake");
            // peak sunny afternoon (2-3 pm): 55° up in the south-south-west, so looking north into the park the light still
            // comes a little from the side (from the south-south-east everything north of the camera was front-lit, flat).
            // For early or late in the day: about 35° up, Euler(35, 45, 0), at 4800 K
            sun.transform.rotation = Quaternion.Euler(55f, 25f, 0f);
            sun.color = Color.white;
            sun.useColorTemperature = true;
            sun.colorTemperature = 5500f; // clear midday white
            sun.intensity = 2.5f; // 3 blew the pale paving and the arch out to white
            sun.bounceIntensity = 1f;
            sun.lightmapBakeType = LightmapBakeType.Mixed; // baked bounce and far shadows, realtime near shadows
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 1f;
        }

        // ---- sky, ambient, reflections, haze
        HdriSky.Apply(); // turns the photo's sun to where the sun now is
        var sky = AssetDatabase.LoadAssetAtPath<Material>(Sky);
        if (sky)
        {
            sky.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f));
            sky.SetFloat("_Exposure", 1f);
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;
        }
        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.ambientIntensity = 1f;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        RenderSettings.reflectionIntensity = 1f;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.0015f;
        RenderSettings.fogColor = new Color(0.76f, 0.79f, 0.82f); // grey daylight haze, barely blue

        Grade();

        Shadows();

        // ---- bake settings: GPU, enough samples for no blotches, 2 bounces, baked contact shadows, denoised
        LightingSettings bake = Lightmapping.lightingSettings;
        Undo.RecordObject(bake, "Clean Daylight Bake");
        bake.bakedGI = true;
        bake.realtimeGI = false;
        bake.mixedBakeMode = MixedLightingMode.Shadowmask; // past the realtime shadow distance, baked shadows instead of none
        bake.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
        bake.directSampleCount = 32;
        bake.indirectSampleCount = 256;
        bake.environmentSampleCount = 256;
        bake.maxBounces = 2;
        bake.lightProbeSampleCountMultiplier = 1f;
        bake.lightmapResolution = 4f;
        bake.lightmapMaxSize = 4096;
        bake.lightmapPadding = 4;
        bake.lightmapCompression = LightmapCompression.NormalQuality;
        bake.directionalityMode = LightmapsMode.CombinedDirectional;
        bake.indirectScale = 1f;
        bake.albedoBoost = 1f;
        bake.ao = true;
        bake.aoMaxDistance = 1f;
        bake.aoExponentIndirect = 1f;
        bake.aoExponentDirect = 0f;
        bake.filteringMode = LightingSettings.FilterMode.Advanced;
        bake.denoiserTypeDirect = bake.denoiserTypeIndirect = bake.denoiserTypeAO = LightingSettings.DenoiserType.OpenImage; // OptiX is NVIDIA-only
        EditorUtility.SetDirty(bake);

        DynamicGI.UpdateEnvironment();
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        // (the probe volume's baking set has Virtual Offset off: it rebuilds the 2049² terrain as one mesh for the GPU
        // ray tracer, which is past its 8.9 GB buffer limit, and the probe bake failed with it on)
        Lightmapping.Cancel();
        Lightmapping.Clear();
        Lightmapping.BakeAsync();
        Debug.Log("Clean Daylight Bake: lighting reset" + (sun ? "" : " (no sun found)") + ", old bake cleared, new bake started");
    }

    // Tools > Wash Square > Cheaper Shadows (no bake needed): realtime sun shadows out to 80 m in 3 cascades, baked
    // (shadowmask) beyond; 150 m in 4 cascades drew every object a second time and cost ~3 ms of CPU a frame. And the
    // small props stop casting: benches, lamps, bins, bikes and the fountain's spray were ~1,000 of the 3,750 draws a frame
    // ponytail: their baked shadows go too (the bake skips non-casters); turn a prefab's back on if its shadow is missed
    static readonly string[] Shadowless = { "Assets/Prefabs/ParkBench.prefab", "Assets/Prefabs/CitiBike.prefab", "Assets/Prefabs/TrashCan.prefab" };

    [MenuItem("Tools/Wash Square/Cheaper Shadows")]
    static void Shadows()
    {
        var urp = new SerializedObject(GraphicsSettings.currentRenderPipeline);
        urp.FindProperty("m_ShadowDistance").floatValue = 80f;
        urp.FindProperty("m_ShadowCascadeCount").intValue = 3;
        urp.FindProperty("m_Cascade3Split").vector2Value = new Vector2(0.1f, 0.35f);
        urp.FindProperty("m_MainLightShadowmapResolution").intValue = 4096;
        urp.ApplyModifiedPropertiesWithoutUndo();

        int props = 0;
        foreach (string path in Shadowless)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true)) { r.shadowCastingMode = ShadowCastingMode.Off; props++; }
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
        }
        GameObject blockout = GameObject.Find("washingtonSquareBlockout");
        if (blockout)
            foreach (Renderer r in blockout.GetComponentsInChildren<Renderer>(true))
                if (r.name.StartsWith("WSP_Prop_Lamp") || r.name.StartsWith("WSP_Fountain_Spray")) { r.shadowCastingMode = ShadowCastingMode.Off; props++; }
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("Cheaper Shadows: 80 m, 3 cascades; " + props + " prop renderers no longer cast");
    }

    // Tools > Wash Square > Daylight Grade: just the colour grading and haze (no bake): rich but natural daylight colour,
    // ACES contrast, warm highlights against slightly cool shade (the warm/cool split real sunlight has)
    [MenuItem("Tools/Wash Square/Daylight Grade")]
    static void Grade()
    {
        var volume = Object.FindAnyObjectByType<Volume>();
        VolumeProfile profile = volume ? volume.sharedProfile : null;
        if (profile)
        {
            if (profile.TryGet(out ColorAdjustments colour))
            {
                colour.postExposure.Override(0.15f);
                colour.contrast.Override(22f);
                colour.saturation.Override(24f); // ACES greys colours out; this puts them back
                colour.colorFilter.Override(Color.white);
                colour.hueShift.Override(0f);
            }
            if (!profile.TryGet(out WhiteBalance balance)) balance = profile.Add<WhiteBalance>();
            balance.active = true;
            balance.temperature.Override(6f);
            balance.tint.Override(0f);
            if (!profile.TryGet(out LiftGammaGain split)) split = profile.Add<LiftGammaGain>();
            split.active = true;
            split.lift.Override(new Vector4(0.97f, 0.99f, 1.04f, 0f)); // shade a touch blue
            split.gamma.Override(new Vector4(1f, 1f, 1f, 0f));
            split.gain.Override(new Vector4(1.04f, 1.01f, 0.95f, 0f)); // sunlight a touch gold
            if (!profile.TryGet(out Tonemapping tone)) tone = profile.Add<Tonemapping>();
            tone.mode.Override(TonemappingMode.ACES);
            if (profile.TryGet(out Bloom bloom)) // only what's really bright glows, not the sunlit paving
            {
                bloom.threshold.Override(1.2f);
                bloom.intensity.Override(0.3f);
            }
            // the volumetric haze (its component lives in a package, so set by name): a thin layer near the ground; at
            // half density and 178 m high it milked out everything past the first trees
            VolumeComponent haze = profile.components.Find(c => c.GetType().Name == "VolumetricFogVolumeComponent");
            if (haze)
            {
                void Set(string field, float value) => ((VolumeParameter<float>)haze.GetType().GetField(field).GetValue(haze)).Override(value);
                Set("density", 0.08f);
                Set("distance", 80f);
                Set("baseHeight", 0f);
                Set("maximumHeight", 30f);
                ((VolumeParameter)haze.GetType().GetField("groundHeight").GetValue(haze)).overrideState = false;
            }
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
        }
    }
}
