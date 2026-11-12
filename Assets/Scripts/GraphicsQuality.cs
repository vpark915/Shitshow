using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// The Graphics setting (GameSettings' sign).
// - Normal is the game as set up in its assets (PC_RPAsset, PC_Renderer, TestProfile, the terrain, the quality level):
//   no renderer features or bloom, one shadow cascade to 22.5 m, grass to 17.5 m, and Carve's relief (the parallax
//   surfaces) at its Low preset, which is the one part set here, as globals.
// - Potato is for weak machines, and looks it: no parallax at all (Carve flat), no sun shadows, no grass, trees half as
//   far, LODs dropping sooner, drawn at 75% resolution, no renderer features (HTrace GTAO and the rest), and people
//   without shadows and only out to 30 m (CrowdCulling).
// The pipeline asset and its renderers are assets: in the editor a change to them would be saved into the project, so each value is kept
// the first time Potato touches it and put back on Normal and when play stops. The rest (the sun, the terrain) are
// scene objects, whose changes in play mode don't last.
public static class GraphicsQuality
{
    public static bool Potato { get; private set; }

    const float RenderScale = 0.75f, TerrainScale = 0.5f, LodBias = 0.5f;

    static readonly Dictionary<Terrain, (float detail, float tree)> terrains = new();
    static readonly Dictionary<Light, LightShadows> suns = new();
    static readonly Dictionary<ScriptableRendererFeature, bool> features = new();
    static float renderScale = -1, lodBias = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Begin()
    {
        SceneManager.sceneLoaded += (_, _) => { CrossFade(); if (Potato) Apply(); };
        CrossFade();
        Application.quitting += () => Set(false, false);
        Set(PlayerPrefs.GetInt("settings.potato", 0) == 1, false);
    }

    public static void Set(bool potato, bool save = true)
    {
        Potato = potato;
        if (save) PlayerPrefs.SetInt("settings.potato", potato ? 1 : 0);
        Restore();
        CarveLow();
        if (potato) Apply();
    }

    // every LOD group fades between its levels and out at its last (URP's dithered cross-fade) rather than popping, in
    // whichever scene (the gym's and tutorial's props included)
    static void CrossFade()
    {
        foreach (LODGroup group in Object.FindObjectsByType<LODGroup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            group.fadeMode = LODFadeMode.CrossFade;
            group.animateCrossFading = true;
        }
    }

    // Normal's Carve: the Low preset (CarveQualitySettings)
    public static void CarveLow()
    {
        JamzeySt.Carve.CarveGlobals.DepthScale = 1f;
        JamzeySt.Carve.CarveGlobals.StepScale = 0.6f;
        JamzeySt.Carve.CarveGlobals.DistanceScale = 0.5f;
        JamzeySt.Carve.CarveGlobals.ShadowStepScale = 0.5f;
        JamzeySt.Carve.CarveGlobals.ExtrasDistance = 20f;
        JamzeySt.Carve.CarveGlobals.OcclusionSamples = JamzeySt.Carve.CarveOcclusionSamples.Four;
        JamzeySt.Carve.CarveGlobals.Occlusion = 0f;
        JamzeySt.Carve.CarveGlobals.CliffAndSpecular = 0f;
    }

    static void Apply()
    {
        // flat: no marching at all (profiled at ~6 ms), the surfaces lit by their normal maps alone
        JamzeySt.Carve.CarveGlobals.DepthScale = 0f;
        JamzeySt.Carve.CarveGlobals.SelfShadow = 0f;
        if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
        {
            if (renderScale < 0) renderScale = urp.renderScale;
            urp.renderScale = renderScale * RenderScale;
            foreach (ScriptableRendererData data in urp.rendererDataList)
            {
                if (!data) continue;
                foreach (ScriptableRendererFeature feature in data.rendererFeatures)
                {
                    if (!feature) continue;
                    features.TryAdd(feature, feature.isActive);
                    feature.SetActive(false);
                }
            }
            // HTrace AO leaves URP's screen-space occlusion keyword on with its last AO texture bound when it stops
            // running, which darkened the whole frame (measured: the darks ~45% down); off with it
            Shader.DisableKeyword("_SCREEN_SPACE_OCCLUSION");
        }
        foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (light.type != LightType.Directional) continue;
            suns.TryAdd(light, light.shadows);
            light.shadows = LightShadows.None;
        }
        foreach (Terrain terrain in Terrain.activeTerrains)
        {
            if (!terrains.ContainsKey(terrain)) terrains[terrain] = (terrain.detailObjectDistance, terrain.treeDistance);
            terrain.detailObjectDistance = 0;
            terrain.treeDistance = terrains[terrain].tree * TerrainScale;
        }
        if (lodBias < 0) lodBias = QualitySettings.lodBias;
        QualitySettings.lodBias = lodBias * LodBias;
    }

    static void Restore()
    {
        if (renderScale >= 0 && GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp) urp.renderScale = renderScale;
        foreach (var (light, shadows) in suns) if (light) light.shadows = shadows;
        foreach (var (feature, on) in features) if (feature) feature.SetActive(on);
        foreach (var (terrain, (detail, tree)) in terrains)
            if (terrain) { terrain.detailObjectDistance = detail; terrain.treeDistance = tree; }
        if (lodBias >= 0) QualitySettings.lodBias = lodBias;
        JamzeySt.Carve.CarveGlobals.ResetQuality();
        suns.Clear(); terrains.Clear(); features.Clear();
        renderScale = lodBias = -1;
    }
}
