using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;

// ponytail: temporary diagnostic, delete once the frame budget is sorted. Samples every profiler marker for 120 Play mode
// frames and logs the most expensive by CPU and GPU time (markers nest, so parents include their children)
static class FrameStats
{
    const int Frames = 120;
    static List<(string name, Recorder recorder)> recorders;
    static Dictionary<string, (double cpu, double gpu)> totals;
    static ProfilerRecorder mainThread, renderThread, gpuFrame, batches, setPass, triangles;
    static int frame;

    [MenuItem("Tools/Wash Square/Log Frame Stats (Play mode)")]
    static void Start()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("FrameStats: enter Play mode first"); return; }
        var names = new List<string>();
        Sampler.GetNames(names);
        recorders = new List<(string, Recorder)>();
        foreach (string name in names.Distinct())
        {
            Recorder recorder = Recorder.Get(name);
            if (!recorder.isValid) continue;
            recorder.enabled = true;
            recorders.Add((name, recorder));
        }
        totals = new Dictionary<string, (double, double)>();
        mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "CPU Main Thread Frame Time");
        renderThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "CPU Render Thread Frame Time");
        gpuFrame = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GPU Frame Time");
        batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
        setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
        triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
        frame = -10; // skip the hitch from enabling thousands of recorders
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Debug.Log("FrameStats: sampling " + recorders.Count + " markers for " + Frames + " frames");
    }

    // A/B: GPU and main thread time with each suspect switched off in turn, everything put back after. Play mode only
    // (scene changes revert on exit; the asset toggles are restored here)
    static readonly string[] Steps = { "baseline", "SSGI off", "GTAO off", "volumetric fog off", "sun shadows off", "terrain trees + grass off",
        "post-processing off", "render scale 0.5", "all characters hidden", "baseline again" };
    static int step, stepFrame;
    static double stepGpu, stepMain;
    static StringBuilder abLog;

    [MenuItem("Tools/Wash Square/Log Feature Costs (Play mode)")]
    static void StartAB()
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("FrameStats: enter Play mode first"); return; }
        mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "CPU Main Thread Frame Time");
        gpuFrame = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GPU Frame Time");
        step = 0; stepFrame = -30; stepGpu = stepMain = 0;
        abLog = new StringBuilder("FrameStats feature costs (ms per frame, 60 frames each)\n");
        Apply(Steps[0], true);
        EditorApplication.update -= TickAB;
        EditorApplication.update += TickAB;
    }

    static void TickAB()
    {
        if (!EditorApplication.isPlaying) { EditorApplication.update -= TickAB; return; }
        if (Time.frameCount == lastFrame) return;
        lastFrame = Time.frameCount;
        if (stepFrame++ < 0) return; // settle (history buffers, streaming)
        stepGpu += gpuFrame.LastValue / 1e6;
        stepMain += mainThread.LastValue / 1e6;
        if (stepFrame < 60) return;

        abLog.AppendFormat("{0,-28} GPU {1,6:F2}   main {2,6:F2}\n", Steps[step], stepGpu / 60, stepMain / 60);
        Apply(Steps[step], false);
        if (++step == Steps.Length)
        {
            EditorApplication.update -= TickAB;
            mainThread.Dispose(); gpuFrame.Dispose();
            Debug.Log(abLog.ToString());
            return;
        }
        Apply(Steps[step], true);
        stepFrame = -30; stepGpu = stepMain = 0;
    }

    static List<Renderer> hidden = new List<Renderer>();

    static void Apply(string what, bool off)
    {
        var pipeline = (UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset)UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
        var renderer = (UnityEngine.Rendering.Universal.UniversalRendererData)pipeline.rendererDataList[0];
        switch (what)
        {
            case "SSGI off": Feature(renderer, "ScreenSpaceGlobalIlluminationURP", off); break;
            case "GTAO off": Feature(renderer, "GroundTruthAmbientOcclusion", off); break;
            case "volumetric fog off": Feature(renderer, "VolumetricFogRendererFeature", off); break;
            case "sun shadows off":
                RenderSettings.sun.shadows = off ? LightShadows.None : LightShadows.Soft; break;
            case "terrain trees + grass off":
                foreach (Terrain t in Terrain.activeTerrains) t.drawTreesAndFoliage = !off; break;
            case "post-processing off":
                Camera.main.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>().renderPostProcessing = !off; break;
            case "render scale 0.5": pipeline.renderScale = off ? 0.5f : 1f; break;
            case "all characters hidden":
                if (off)
                {
                    hidden = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None).Where(r => r.enabled).Cast<Renderer>().ToList();
                    foreach (Renderer r in hidden) r.enabled = false;
                }
                else foreach (Renderer r in hidden) if (r) r.enabled = true;
                break;
        }
    }

    static void Feature(UnityEngine.Rendering.Universal.UniversalRendererData renderer, string name, bool off)
    {
        foreach (var feature in renderer.rendererFeatures)
            if (feature && feature.GetType().Name == name) feature.SetActive(!off);
    }

    // which meshes the triangles come from: visible renderers grouped by mesh, and terrain trees by prototype
    [MenuItem("Tools/Wash Square/Log Triangle Sources")]
    static void Triangles()
    {
        var byMesh = new Dictionary<string, (long tris, int count, int shadows)>();
        void Add(string key, long tris, bool shadows)
        {
            byMesh.TryGetValue(key, out var v);
            byMesh[key] = (v.tris + tris, v.count + 1, v.shadows + (shadows ? 1 : 0));
        }
        foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            Mesh mesh = r is SkinnedMeshRenderer s ? s.sharedMesh : r.TryGetComponent(out MeshFilter f) ? f.sharedMesh : null;
            if (!mesh) continue;
            LODGroup lod = r.GetComponentInParent<LODGroup>();
            string key = (r is SkinnedMeshRenderer ? "[skinned] " : "") + mesh.name + (lod ? " (LOD group)" : "");
            Add(key, (long)(mesh.triangles.Length / 3), r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off);
        }
        foreach (Terrain t in Terrain.activeTerrains)
        {
            TerrainData data = t.terrainData;
            var counts = new int[data.treePrototypes.Length];
            foreach (TreeInstance tree in data.treeInstances) counts[tree.prototypeIndex]++;
            for (int i = 0; i < counts.Length; i++)
            {
                GameObject prefab = data.treePrototypes[i].prefab;
                LODGroup lod = prefab ? prefab.GetComponent<LODGroup>() : null;
                long tris = prefab ? prefab.GetComponentsInChildren<MeshFilter>().Where(m => m.sharedMesh && (!lod || lod.GetLODs()[0].renderers.Contains(m.GetComponent<Renderer>()))).Sum(m => (long)m.sharedMesh.triangles.Length / 3) : 0;
                byMesh["[terrain tree] " + (prefab ? prefab.name : "?") + (lod ? " LODs:" + lod.lodCount : " no LOD") + " x" + counts[i] + " (LOD0 tris each " + tris + ")"] = (tris * counts[i], counts[i], counts[i]);
            }
            byMesh["[terrain] trees: distance " + t.treeDistance + " billboard " + t.treeBillboardDistance + " fullLOD max " + t.treeMaximumFullLODCount + "; detail distance " + t.detailObjectDistance + " density " + t.detailObjectDensity] = (0, 0, 0);
        }
        var log = new StringBuilder("Triangle sources (whole scene, LOD0, not view-culled)\n");
        foreach (var m in byMesh.OrderByDescending(m => m.Value.tris).Take(40))
            log.AppendFormat("{0,10:N0} tris  x{1,-5} shadow casters {2,-5} {3}\n", m.Value.tris, m.Value.count, m.Value.shadows, m.Key);
        log.AppendFormat("total {0:N0}\n", byMesh.Sum(m => m.Value.tris));
        Debug.Log(log.ToString());
    }

    // one-shot: project settings Unity holds in memory (editing their files on disk gets overwritten), set through its API.
    // Static batching off: the GPU Resident Drawer (and its occlusion culling) skips statically batched renderers. Keep All:
    // the drawer's shader variants, stripped from builds otherwise. LOD bias 2 kept trees at full detail twice as far
    [MenuItem("Tools/Wash Square/Apply Performance Settings")]
    static void Performance()
    {
        foreach (BuildTarget target in new[] { BuildTarget.StandaloneOSX, BuildTarget.StandaloneWindows64, BuildTarget.StandaloneLinux64 })
            PlayerSettings.SetStaticBatchingForPlatform(target, false);
        // read-only in the API: the serialized field, with the enum's value looked up rather than guessed
        System.Type mode = typeof(UnityEditor.Rendering.EditorGraphicsSettings).GetProperty("batchRendererGroupShaderStrippingMode").PropertyType;
        var graphics = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
        graphics.FindProperty("m_BrgStripping").intValue = (int)System.Enum.Parse(mode, "KeepAll");
        graphics.ApplyModifiedProperties();
        QualitySettings.lodBias = 1f;
        AssetDatabase.SaveAssets();
        Debug.Log("FrameStats: static batching off, BRG variants Keep All, LOD bias " + QualitySettings.lodBias);
    }

    static double main, render, gpu;
    static int lastFrame = -1;

    static void Tick()
    {
        if (!EditorApplication.isPlaying) { Stop(); return; }
        if (Time.frameCount == lastFrame) return; // the editor updates more often than the game renders
        lastFrame = Time.frameCount;
        if (frame++ < 0) { main = render = gpu = 0; return; }

        foreach ((string name, Recorder recorder) in recorders)
        {
            double cpu = recorder.elapsedNanoseconds / 1e6, gpuTime = recorder.gpuElapsedNanoseconds / 1e6;
            if (cpu <= 0 && gpuTime <= 0) continue;
            totals.TryGetValue(name, out var sum);
            totals[name] = (sum.cpu + cpu, sum.gpu + gpuTime);
        }
        main += mainThread.LastValue / 1e6;
        render += renderThread.LastValue / 1e6;
        gpu += gpuFrame.LastValue / 1e6;
        if (frame < Frames) return;

        var log = new StringBuilder("FrameStats over " + Frames + " frames (ms per frame)\n");
        log.AppendFormat("main thread {0:F2}  render thread {1:F2}  GPU frame {2:F2}  batches {3}  setpass {4}  tris {5}\n",
            main / Frames, render / Frames, gpu / Frames, batches.LastValue, setPass.LastValue, triangles.LastValue);
        log.AppendLine("-- top CPU --");
        foreach (var t in totals.Where(t => !t.Key.Contains("Memory") && !t.Key.Contains("Bytes") && !t.Key.Contains("Count") && !t.Key.StartsWith("GfxResource")).OrderByDescending(t => t.Value.cpu).Take(110))
            log.AppendFormat("{0,8:F3}  {1}\n", t.Value.cpu / Frames, t.Key);
        log.AppendLine("-- top GPU --");
        foreach (var t in totals.Where(t => t.Value.gpu > 0).OrderByDescending(t => t.Value.gpu).Take(35))
            log.AppendFormat("{0,8:F3}  {1}\n", t.Value.gpu / Frames, t.Key);
        Debug.Log(log.ToString());
        Stop();
    }

    static void Stop()
    {
        EditorApplication.update -= Tick;
        if (recorders != null) foreach ((_, Recorder recorder) in recorders) recorder.enabled = false;
        recorders = null;
        mainThread.Dispose(); renderThread.Dispose(); gpuFrame.Dispose(); batches.Dispose(); setPass.Dispose(); triangles.Dispose();
    }

    // which editor windows are open (each visible one repaints every Play mode frame) and whether a Scene view
    // re-renders the scene every frame
    [MenuItem("Tools/Wash Square/Log Editor Windows")]
    static void LogWindows()
    {
        var log = new StringBuilder("FrameStats windows:\n");
        foreach (EditorWindow w in Resources.FindObjectsOfTypeAll<EditorWindow>())
            log.AppendLine("  " + w.GetType().Name + " '" + w.titleContent.text + "' " + w.position.size + (w.hasFocus ? " focused" : "")
                + (w is SceneView s ? " alwaysRefresh " + s.sceneViewState.alwaysRefresh + " fx " + s.sceneViewState.fxEnabled : ""));
        Debug.Log(log.ToString());
    }
}
