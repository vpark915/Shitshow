using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Tools > Wash Square > Punchy Lighting: the open scene's share of the UE5-style look (the rest lives in the shared
// render assets: HDR grading, the volume profile, SSAO). What made it flat: shadows only 77% dark, a full-strength sky
// fill evening everything out, and no distance haze, so nothing read as near or far.
// - sun: full-strength soft shadows, a little brighter, so light and shade are clearly apart
// - sky fill (ambient) a touch up from default: full-strength shadows plus SSAO otherwise crush the shade
// - exponential height-less haze (UE5's exponential height fog / atmosphere stand-in) for depth
// Ctrl+Z undoes the sun; the rest is in Window > Rendering > Lighting > Environment. Then rebake the lighting
// (Lighting > Generate Lighting) so the baked shadows match.
static class PunchyLighting
{
    [MenuItem("Tools/Wash Square/Punchy Lighting (open scene)")]
    static void Apply()
    {
        Light sun = RenderSettings.sun;
        if (!sun)
            foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) { sun = l; break; }
        if (sun)
        {
            Undo.RecordObject(sun, "Punchy Lighting");
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 1f;
            sun.intensity = Mathf.Max(sun.intensity, 3.2f);
            sun.color = new Color(1f, 0.94f, 0.85f); // warm sun against the blue sky fill: the warm/cool split UE5 daylight has
        }
        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.ambientIntensity = 1.1f;
        // the "saran wrap": with no reflection probes every surface mirrors the bright open sky, shade or not
        RenderSettings.reflectionIntensity = 0.85f; // the probes reflect the real surroundings now, so this can come back up
        bool any = Area(out Bounds area);
        if (any) Probes(area);
        if (any) Fog(area);
        BounceProbes();
        // edges: TAA (UE5 smooths with its TSR, a temporal AA too); the cameras had none at all
        foreach (Camera cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            if (cam.TryGetComponent(out UnityEngine.Rendering.Universal.UniversalAdditionalCameraData data))
            {
                Undo.RecordObject(data, "Punchy Lighting");
                data.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.TemporalAntiAliasing;
                data.antialiasingQuality = UnityEngine.Rendering.Universal.AntialiasingQuality.High;
            }
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.0018f; // a hint of haze: ~3% at 100 m, ~12% at 200 m, ~25% at 300 m
        RenderSettings.fogColor = new Color(0.66f, 0.74f, 0.84f);
        DynamicGI.UpdateEnvironment();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("Punchy Lighting applied to " + EditorSceneManager.GetActiveScene().name + (sun ? "" : " (no sun found)") + ". Save the scene, then Generate Lighting to rebake.");
    }

    // bounce light for what moves: the lightmaps give it to the static park only, so people, birds and cars got one flat
    // sky colour and looked pasted on. An Adaptive Probe Volume (the render asset's Light Probe System is set to it) bakes
    // bounce light into a 3D grid with Generate Lighting, which everything that moves picks up per pixel.
    static void BounceProbes()
    {
        if (Object.FindAnyObjectByType<ProbeVolume>()) return;
        var volume = new GameObject("Adaptive Probe Volume").AddComponent<ProbeVolume>();
        Undo.RegisterCreatedObjectUndo(volume.gameObject, "Punchy Lighting");
        volume.mode = ProbeVolume.Mode.Global;
        // ponytail: probes no closer than 3 m and small props ignored, to keep the bake short over a 600 m park;
        // drop lowestSubdivLevelOverride to 0 (1 m) for finer bounce light round benches and lamps
        volume.overridesSubdivLevels = true;
        volume.lowestSubdivLevelOverride = 1;
        volume.overrideRendererFilters = true;
        volume.minRendererVolumeSize = 1f;
    }

    const float ProbeSpacing = 80f; // metres between reflection probes

    // a grid of baked, box-projected reflection probes over everything that renders, so surfaces reflect the
    // buildings, trees and ground around them instead of the sky (Generate Lighting bakes them). Rerunning replaces it.
    // ponytail: even grid at 6 m up; hand-placed probes in tight spots (under the arch, between buildings) if it shows
    static void Probes(Bounds area)
    {
        GameObject old = GameObject.Find("Reflection Probes");
        if (old) Undo.DestroyObjectImmediate(old);
        var root = new GameObject("Reflection Probes");
        Undo.RegisterCreatedObjectUndo(root, "Punchy Lighting");
        int nx = Mathf.Max(1, Mathf.CeilToInt(area.size.x / ProbeSpacing)), nz = Mathf.Max(1, Mathf.CeilToInt(area.size.z / ProbeSpacing));
        Vector3 cell = new Vector3(area.size.x / nx, Mathf.Min(area.size.y, 60f), area.size.z / nz);
        for (int x = 0; x < nx; x++)
            for (int z = 0; z < nz; z++)
            {
                var probe = new GameObject("Probe " + x + "," + z).AddComponent<ReflectionProbe>();
                probe.transform.SetParent(root.transform);
                Vector3 centre = new Vector3(area.min.x + cell.x * (x + 0.5f), area.min.y + cell.y / 2, area.min.z + cell.z * (z + 0.5f));
                probe.transform.position = new Vector3(centre.x, area.min.y + 6f, centre.z); // capture from about eye height
                probe.center = centre - probe.transform.position;
                probe.size = cell + Vector3.one * 10f; // overlap so neighbours blend
                probe.mode = ReflectionProbeMode.Baked;
                probe.boxProjection = true;
                probe.blendDistance = 5f;
                probe.resolution = 128;
            }
    }

    // everything static that renders, and the terrain
    static bool Area(out Bounds area)
    {
        area = new Bounds();
        bool any = false;
        foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            if (r.enabled && r.gameObject.isStatic) { if (any) area.Encapsulate(r.bounds); else area = r.bounds; any = true; }
        foreach (Terrain t in Terrain.activeTerrains)
        {
            var b = new Bounds(t.transform.position + t.terrainData.size / 2, t.terrainData.size);
            if (any) area.Encapsulate(b); else area = b; any = true;
        }
        return any;
    }

    // the haze sunlight shows up in as shafts through the trees (the Fog Volumes package; its render feature is on
    // PC_Renderer, the look in Assets/Settings/ParkFog.asset): one box over the whole park, from the ground up
    // FogHeight, fading out towards the top. Rerunning replaces it.
    const float FogHeight = 30f;
    static void Fog(Bounds area)
    {
        GameObject old = GameObject.Find("Park Fog");
        if (old) Undo.DestroyObjectImmediate(old);
        var profile = AssetDatabase.LoadAssetAtPath<Sinnwrig.FogVolumes.FogVolumeProfile>("Assets/Settings/ParkFog.asset");
        if (!profile) { Debug.LogWarning("Punchy Lighting: Assets/Settings/ParkFog.asset is missing, no fog"); return; }
        var fog = new GameObject("Park Fog").AddComponent<Sinnwrig.FogVolumes.FogVolume>();
        Undo.RegisterCreatedObjectUndo(fog.gameObject, "Punchy Lighting");
        fog.transform.position = new Vector3(area.center.x, area.min.y + FogHeight / 2, area.center.z);
        fog.transform.localScale = new Vector3(area.size.x, FogHeight, area.size.z);
        fog.volumeType = Sinnwrig.FogVolumes.VolumeType.Cube;
        fog.edgeFade = new Vector3(0.05f, 0.5f, 0.05f); // soft at the top, all but hard at the park's sides
        fog.maxDistance = 120f;
        fog.distanceFade = 0.7f;
        fog.sharedProfile = profile;
    }
}
