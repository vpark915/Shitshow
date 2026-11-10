using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

// No shader hitches mid-game: Metal compiles each shader (and each render state it's drawn with) the first time it's
// on screen, which stalls a frame. A GraphicsStateCollection records every one the game draws; at start and on each
// scene change, behind the loading screen (Loading), everything recorded is compiled up front.
// - Shipped: StreamingAssets/ShaderWarmup/<API>.graphicsstate (Tools > Wash Square > Ship Shader Trace copies this
//   machine's recording from a build there, so the next build warms it all on a first run).
// - A recorded state the build can't compile (its variant stripped, or the pipeline changed since) is given up on, and
//   the whole warm-up on at MaxSeconds, so the loading screen can't hang on it.
// - Recorded: builds trace as they're played and save to persistentDataPath on quit and scene change, merged with what
//   was recorded before, so even states the shipped file missed are warm from the next launch on.
public static class ShaderWarmup
{
    const int PerFrame = 24; // states compiled each frame while warming: the loading pigeon keeps moving
    const float MaxSeconds = 8; // the loading screen never waits longer than this on it

    static GraphicsStateCollection shipped, recorded;
    static bool tracing, warmed;

    static string FileName => SystemInfo.graphicsDeviceType + ".graphicsstate";
    public static string ShippedPath => Path.Combine(Application.streamingAssetsPath, "ShaderWarmup", FileName);
    public static string RecordedPath => Path.Combine(Application.persistentDataPath, "ShaderWarmup", FileName);

    // the shipped file and this machine's recording, compiled a few states a frame; yields until all done
    public static IEnumerator WarmUp()
    {
#if UNITY_EDITOR
        // the editor compiles shaders in the background with a stand-in meanwhile (no hitch), and from source, so warming
        // there takes most of a minute: builds only
        yield break;
#else
        if (warmed) yield break; // once a launch: compiled stays compiled
        warmed = true;
        Load();
        float giveUpAt = Time.realtimeSinceStartup + MaxSeconds;
        foreach (GraphicsStateCollection c in new[] { shipped, recorded })
        {
            if (!c || c.isWarmedUp || c.totalGraphicsStateCount == 0) continue;
            // a recorded state whose shader variant isn't in this build (stripped, or the pipeline's settings changed
            // since) never completes: stop when a few frames go by with nothing more done, or at the time limit
            int last = -1, stuck = 0;
            while (!c.isWarmedUp && stuck < 5 && Time.realtimeSinceStartup < giveUpAt)
            {
                c.WarmUpProgressively(PerFrame).Complete();
                stuck = c.completedWarmupCount == last ? stuck + 1 : 0;
                last = c.completedWarmupCount;
                yield return null;
            }
            Debug.Log("ShaderWarmup: " + c.completedWarmupCount + "/" + c.totalGraphicsStateCount + " states compiled" + (c.isWarmedUp ? "" : " (gave up on the rest)"));
        }
#endif
    }

    public static float Progress
    {
        get
        {
            int done = 0, all = 0;
            foreach (GraphicsStateCollection c in new[] { shipped, recorded })
                if (c) { done += c.completedWarmupCount; all += c.totalGraphicsStateCount; }
            return all == 0 ? 1 : (float)done / all;
        }
    }

    static void Load()
    {
        if (shipped || recorded) return;
        shipped = Read(ShippedPath);
        // the recording goes on in the same collection: what this run draws is added to what earlier runs drew
        recorded = Read(RecordedPath) ?? new GraphicsStateCollection();
#if !UNITY_EDITOR
        tracing = recorded.BeginTrace();
        Application.quitting += Save;
        UnityEngine.SceneManagement.SceneManager.sceneUnloaded += _ => Save();
#endif
    }

    static GraphicsStateCollection Read(string path)
    {
        if (!File.Exists(path)) return null;
        var c = new GraphicsStateCollection();
        return c.LoadFromFile(path) ? c : null;
    }

    // what's been recorded so far, to disk (tracing stops to save and starts again)
    static void Save()
    {
        if (!tracing || recorded == null) return;
        recorded.EndTrace();
        Directory.CreateDirectory(Path.GetDirectoryName(RecordedPath));
        recorded.SaveToFile(RecordedPath);
        tracing = recorded.BeginTrace();
    }

}
