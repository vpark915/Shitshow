using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// Weathering decals to drag round the park: grime on the ground and at wall bases, water streaks down walls under
// ledges, moss on stone. Makes their textures (noise, drawn here: no texture set had transparent grime), a URP Decal
// material and a ready-to-place DecalProjector prefab each into Assets/Decals, and adds the Decal feature to PC_Renderer
// (screen space: no depth-normals prepass, so cheaper than DBuffer). Run once; again to remake the textures.
// ponytail: noise-drawn stains, not photo-scanned; swap a material's Base Map for a scanned decal to go further
static class WeatheringDecals
{
    const string Folder = "Assets/Decals", Renderer = "Assets/Settings/PC_Renderer.asset";

    [MenuItem("Tools/Wash Square/Make Weathering Decals")]
    static void Make()
    {
        Directory.CreateDirectory(Folder);
        // name, colour, alpha at (u, v), projector size (x, y, depth), laid flat (pointing down) or on a wall
        Make("Grime", new Color(0.16f, 0.13f, 0.1f), (u, v) => Blob(u, v, 3) * 0.85f, new Vector3(3, 3, 1), true);
        Make("Moss", new Color(0.2f, 0.27f, 0.09f), (u, v) => Mathf.Clamp01((Fbm(u * 6, v * 6, 11) - 0.4f) * 6) * Mathf.Sqrt(Blob(u, v, 7)), new Vector3(2, 2, 1), true);
        Make("Leaks", new Color(0.11f, 0.1f, 0.08f), Leak, new Vector3(2, 3, 0.6f), false);
        AddDecalFeature();
        AssetDatabase.SaveAssets();
        Debug.Log("WeatheringDecals: made Grime, Moss and Leaks in " + Folder);
    }

    static void Make(string name, Color colour, System.Func<float, float, float> alpha, Vector3 size, bool ground)
    {
        const int Size = 512;
        var pixels = new Color[Size * Size];
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float u = (x + 0.5f) / Size, v = (y + 0.5f) / Size, shade = 0.8f + 0.4f * Fbm(u * 9, v * 9, 3);
                pixels[y * Size + x] = new Color(colour.r * shade, colour.g * shade, colour.b * shade, alpha(u, v));
            }
        var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
        texture.SetPixels(pixels);
        string png = Folder + "/" + name + ".png";
        File.WriteAllBytes(png, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(png);
        var importer = (TextureImporter)AssetImporter.GetAtPath(png);
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.SaveAndReimport();

        string matPath = Folder + "/" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (!material)
        {
            material = new Material(Shader.Find("Shader Graphs/Decal")) { enableInstancing = true };
            AssetDatabase.CreateAsset(material, matPath);
        }
        material.SetTexture("Base_Map", AssetDatabase.LoadAssetAtPath<Texture2D>(png));
        EditorUtility.SetDirty(material);

        var holder = new GameObject("Decal_" + name);
        if (ground) holder.transform.rotation = Quaternion.Euler(90, 0, 0); // projects along its z: down onto the ground
        DecalProjector projector = holder.AddComponent<DecalProjector>();
        projector.material = material;
        projector.size = size;
        projector.pivot = new Vector3(0, 0, size.z / 2);
        projector.drawDistance = 60;
        PrefabUtility.SaveAsPrefabAsset(holder, Folder + "/Decal_" + name + ".prefab");
        Object.DestroyImmediate(holder);
    }

    static void AddDecalFeature()
    {
        var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(Renderer);
        if (!data || data.rendererFeatures.Any(f => f is DecalRendererFeature)) return;
        var feature = ScriptableObject.CreateInstance<DecalRendererFeature>();
        feature.name = "Decal";
        var settings = new SerializedObject(feature);
        settings.FindProperty("m_Settings.technique").enumValueIndex = 2; // ScreenSpace
        settings.FindProperty("m_Settings.maxDrawDistance").floatValue = 60;
        settings.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.AddObjectToAsset(feature, data);
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long id);
        var so = new SerializedObject(data);
        SerializedProperty features = so.FindProperty("m_RendererFeatures"), map = so.FindProperty("m_RendererFeatureMap");
        features.arraySize++;
        features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
        map.arraySize++;
        map.GetArrayElementAtIndex(map.arraySize - 1).longValue = id;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(data);
    }

    // a soft-edged irregular patch: noise, faded out toward the square's edges so the decal box never shows
    static float Blob(float u, float v, int seed)
    {
        float edge = Mathf.Clamp01((0.5f - new Vector2(u - 0.5f, v - 0.5f).magnitude) * 2.6f);
        return Mathf.Clamp01((Fbm(u * 4, v * 4, seed) - 0.35f) * 2.2f) * edge * edge;
    }

    // dark streaks running down from the top edge (v = 1 is up), each column its own length
    static float Leak(float u, float v)
    {
        float column = Mathf.PerlinNoise(u * 28, 4.2f), length = 0.25f + 0.75f * Mathf.PerlinNoise(u * 9, 17.3f);
        float down = 1 - v, fade = Mathf.Clamp01(1 - down / length);
        float sides = Mathf.Clamp01(Mathf.Min(u, 1 - u) * 8);
        return Mathf.Clamp01((column - 0.35f) * 2.5f) * fade * fade * sides * (0.6f + 0.4f * Fbm(u * 3, v * 12, 5));
    }

    static float Fbm(float x, float y, int seed)
    {
        float sum = 0, amp = 0.5f;
        for (int i = 0; i < 4; i++, x *= 2, y *= 2, amp *= 0.5f) sum += amp * Mathf.PerlinNoise(x + seed * 13.1f, y + seed * 7.7f);
        return sum / 0.9375f;
    }
}
