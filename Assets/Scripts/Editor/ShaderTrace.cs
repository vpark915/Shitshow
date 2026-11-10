using System.IO;
using UnityEditor;
using UnityEngine;

// The shader recordings ShaderWarmup compiles up front (behind the loading screen), so a build doesn't hitch the first
// time each shader shows up:
// - Ship Shader Trace: what builds on this Mac recorded while played (they trace themselves, into persistentDataPath,
//   the same folder the editor uses) goes into StreamingAssets, so the next build warms it all on a first run.
//   Play a build through (every map, shoot, egg bomb, ride, eat, drive) and quit before running it. Only a build's
//   recording will do: one from the editor is for the editor (a different platform to the build, which then can't use
//   most of it).
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
}
