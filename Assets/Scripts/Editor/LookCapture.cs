using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// Tools > Wash Square > Capture Look Shots: renders the open scene, with post-processing, from the Scene view and a few
// fixed spots in the park into Temp/LookShots/*.png (the project's Temp folder, not imported), to judge the lighting.
static class LookCapture
{
    static readonly (string name, Vector3 at, Vector3 lookAt)[] Spots =
    {
        ("fountain", new Vector3(0, 1.8f, -32), new Vector3(0, 3, 0)),           // eye level, the fountain and the arch behind
        ("overview", new Vector3(-70, 45, -120), new Vector3(0, 0, 0)),           // over the park's south-west corner
        ("lawn", new Vector3(-95, 1.8f, 35), new Vector3(-40, 2, 30)),            // eye level across a lawn
        ("street", new Vector3(-150, 1.8f, -78), new Vector3(-100, 3, -76)),      // along the south sidewalk
        ("sunward", new Vector3(25, 2.5f, 30), new Vector3(0, 0, 5)),             // towards the sun (south-west): shadows come at the camera
    };

    [MenuItem("Tools/Wash Square/Capture Look Shots")]
    static void Capture()
    {
        string folder = System.IO.Path.Combine(Application.dataPath, "../Temp/LookShots");
        System.IO.Directory.CreateDirectory(folder);
        var go = new GameObject("LookCapture") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 1500f;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt;
            var shots = new System.Collections.Generic.List<(string, Vector3, Quaternion)>();
            SceneView view = SceneView.lastActiveSceneView;
            if (view) shots.Add(("sceneview", view.camera.transform.position, view.camera.transform.rotation));
            foreach (var (name, at, lookAt) in Spots) shots.Add((name, at, Quaternion.LookRotation(lookAt - at)));
            var read = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            foreach (var (name, at, rot) in shots)
            {
                go.transform.SetPositionAndRotation(at, rot);
                cam.Render();
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                RenderTexture.active = null;
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, name + ".png"), read.EncodeToPNG());
            }
            Object.DestroyImmediate(read);
            cam.targetTexture = null;
            rt.Release();
            Debug.Log("LookCapture: " + shots.Count + " shots in " + System.IO.Path.GetFullPath(folder));
        }
        finally { Object.DestroyImmediate(go); }
    }
}
