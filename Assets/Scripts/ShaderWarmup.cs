using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

// No shader hitches mid-game: Metal compiles each shader (and each render state it's drawn with) the first time it's
// on screen, which stalls a frame. A GraphicsStateCollection records every one the game draws; at start and on each
// scene change, behind the loading screen (Loading), everything recorded is compiled up front.
// - Shipped: StreamingAssets/ShaderWarmup/<API>.graphicsstate (Tools > Wash Square > Ship Shader Trace copies this
//   machine's recording there, so the next build warms it all on a first run).
// - Recorded: builds trace as they're played and save to persistentDataPath on quit and scene change, merged with what
//   was recorded before, so even states the shipped file missed are warm from the next launch on.
public static class ShaderWarmup
{
    const int PerFrame = 24; // states compiled each frame while warming: the loading pigeon keeps moving

    static GraphicsStateCollection shipped, recorded;
    static bool tracing;

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
        Load();
        foreach (GraphicsStateCollection c in new[] { shipped, recorded })
        {
            if (!c || c.isWarmedUp || c.totalGraphicsStateCount == 0) continue;
            while (!c.isWarmedUp)
            {
                c.WarmUpProgressively(PerFrame).Complete();
                yield return null;
            }
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

#if UNITY_EDITOR
    // for Tools > Wash Square > Record Shader Trace: trace while playing in the editor, save when asked
    public static void BeginEditorTrace()
    {
        recorded ??= new GraphicsStateCollection();
        tracing = recorded.BeginTrace();
    }

    public static bool SaveEditorTrace(string path)
    {
        if (recorded == null) return false;
        recorded.EndTrace();
        tracing = false;
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        return recorded.SaveToFile(path);
    }

    public static int RecordedVariants => recorded != null ? recorded.variantCount : 0;
    public static int RecordedStates => recorded != null ? recorded.totalGraphicsStateCount : 0;
#endif
}
