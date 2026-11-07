using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools > Wash Square > Bake Minimap: takes the open scene's minimap picture now, in the editor, saves it as
// Textures/Minimap_<scene>.png and gives it to the scene's Minimap. A build used to photograph the park on its first
// frame, before the lighting data and textures had streamed in: objects went missing and shading came out wrong.
// Run it again after changing the park.
static class MinimapBake
{
    [MenuItem("Tools/Wash Square/Bake Minimap")]
    internal static void Bake()
    {
        var map = Object.FindAnyObjectByType<Minimap>(FindObjectsInactive.Include);
        if (!map) { Debug.LogWarning("Bake Minimap: no Minimap in the open scene"); return; }
        RenderTexture shot = map.Photograph();
        var read = new Texture2D(shot.width, shot.height, TextureFormat.RGB24, false);
        RenderTexture.active = shot;
        read.ReadPixels(new Rect(0, 0, shot.width, shot.height), 0, 0);
        RenderTexture.active = null;
        shot.Release();
        string path = "Assets/Textures/Minimap_" + map.gameObject.scene.name + ".png";
        System.IO.File.WriteAllBytes(path, read.EncodeToPNG());
        Object.DestroyImmediate(read);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Default;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.maxTextureSize = 2048;
        importer.SaveAndReimport();

        Undo.RecordObject(map, "Bake Minimap");
        map.bakedPicture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
        EditorSceneManager.SaveScene(map.gameObject.scene);
        Debug.Log("Bake Minimap: " + path + " (" + shot.width + "x" + shot.height + ") on " + map.name);
    }
}
