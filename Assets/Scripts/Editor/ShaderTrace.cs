using System.IO;
using UnityEditor;
using UnityEngine;

// The shader recordings ShaderWarmup compiles up front (behind the loading screen), so a build doesn't hitch the first
// time each shader shows up:
// - Ship Shader Trace: what builds on this Mac recorded while played (they trace themselves, into persistentDataPath,
//   the same folder the editor uses) goes into StreamingAssets, so the next build warms it all on a first run.
//   Play a build through (every map, shoot, egg bomb, ride, eat, drive) and quit before running it.
// - Record Shader Trace (Start / Save): the same, from play mode in the editor. A first pass; the build's own recording
//   is the better one (the editor draws a few things the build doesn't, and the other way round).
static class ShaderTrace
{
    [MenuItem("Tools/Wash Square/Ship Shader Trace")]
    static void Ship()
    {
        string from = ShaderWarmup.RecordedPath, to = ShaderWarmup.ShippedPath;
        if (!File.Exists(from)) { Debug.LogWarning("Ship Shader Trace: nothing recorded yet at " + from + " (play a build, then quit it)"); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(to));
        File.Copy(from, to, true);
        AssetDatabase.Refresh();
        Debug.Log("Ship Shader Trace: " + from + " -> " + to + " (" + new FileInfo(to).Length / 1024 + " KB)");
    }

    [MenuItem("Tools/Wash Square/Record Shader Trace/Start (in play mode)")]
    static void Start()
    {
        if (!Application.isPlaying) { Debug.LogWarning("Record Shader Trace: enter play mode first"); return; }
        ShaderWarmup.BeginEditorTrace();
        Debug.Log("Record Shader Trace: recording. Play through everything, then Record Shader Trace > Save");
    }

    [MenuItem("Tools/Wash Square/Record Shader Trace/Save")]
    static void Save()
    {
        bool saved = ShaderWarmup.SaveEditorTrace(ShaderWarmup.ShippedPath);
        AssetDatabase.Refresh();
        Debug.Log("Record Shader Trace: " + (saved ? ShaderWarmup.RecordedVariants + " variants, " + ShaderWarmup.RecordedStates + " states saved to " + ShaderWarmup.ShippedPath : "nothing to save"));
    }
}
