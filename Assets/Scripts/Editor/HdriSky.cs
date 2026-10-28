using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools > Wash Square > HDRI Sky: turns the shared sky material (Scenes/WashSquareDaySky, every scene's skybox) from the
// procedural sky into a photographed one, Sky/*.hdr (Poly Haven, CC0: Kloofendal 48d partly cloudy, sky only so the park
// is the ground), spun round so the photo's sun sits where the open scene's sun light shines from. Ambient and
// reflections then come from the photo; Generate Lighting to rebake with it.
// The .hdr's sun is clamped to 20 (it was ~75,000): the bake took the photo's sun as fill light from the sky and lit
// every shadow with it (a second sun the shadows can't block), which read as overcast. The Light is the sun now.
// The original is Poly Haven's kloofendal_48d_partly_cloudy_puresky_4k.hdr, if it's ever needed again.
static class HdriSky
{
    const string Hdri = "Assets/Sky/kloofendal_48d_partly_cloudy_puresky_4k.hdr", Sky = "Assets/Scenes/WashSquareDaySky.mat";
    const float SunU = 0.5953f, SunElevation = 48f; // where the sun is in the photo (its brightest pixel), across and up

    [MenuItem("Tools/Wash Square/HDRI Sky")]
    internal static void Apply()
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(Hdri);
        importer.textureShape = TextureImporterShape.Texture2D;
        importer.mipmapEnabled = false; // mips draw a seam where the panorama wraps round
        importer.wrapModeU = TextureWrapMode.Repeat;
        importer.wrapModeV = TextureWrapMode.Clamp;
        importer.maxTextureSize = 4096;
        importer.textureCompression = TextureImporterCompression.CompressedHQ; // BC6H: keeps the HDR range
        importer.SaveAndReimport();

        var sky = AssetDatabase.LoadAssetAtPath<Material>(Sky);
        Undo.RecordObject(sky, "HDRI Sky");
        sky.shader = Shader.Find("Skybox/Panoramic");
        sky.shaderKeywords = new string[0]; // no _MAPPING_6_FRAMES_LAYOUT: latitude-longitude
        sky.SetFloat("_Mapping", 1);
        sky.SetFloat("_ImageType", 0);
        sky.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(Hdri));
        sky.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f)); // grey 0.5 = untinted
        sky.SetFloat("_Exposure", 1f);

        // Skybox/Panoramic samples u = 0.5 - atan2(z, x) / 2pi of the view direction turned _Rotation degrees about y
        Light sun = RenderSettings.sun;
        if (!sun)
            foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) { sun = l; break; }
        string note = "";
        if (sun)
        {
            Vector3 toSun = -sun.transform.forward;
            float rotation = (0.5f - SunU) * 360f - Mathf.Atan2(toSun.z, toSun.x) * Mathf.Rad2Deg;
            sky.SetFloat("_Rotation", Mathf.Repeat(rotation, 360f));
            float up = Mathf.Asin(Mathf.Clamp(toSun.y, -1, 1)) * Mathf.Rad2Deg;
            // ponytail: turns the photo to the sun, can't tilt it; set the sun to SunElevation if the gap shows
            note = " Sun is " + up.ToString("F0") + "° up, the photo's " + SunElevation + "°.";
        }
        else note = " No sun found, photo not turned.";
        EditorUtility.SetDirty(sky);
        AssetDatabase.SaveAssets();
        RenderSettings.skybox = sky;
        DynamicGI.UpdateEnvironment();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("HDRI Sky: " + Sky + " now shows " + Hdri + "." + note + " Generate Lighting to rebake.");
    }
}
