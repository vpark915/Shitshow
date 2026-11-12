using System.IO;
using JamzeySt.Carve.Editor;
using UnityEditor;
using UnityEngine;

// The parallax surfaces on Carve (JamzeySt/Carve: Carve/URP/Carve Lit, real silhouette parallax with self-shadowing)
// (it replaced the project's own Shitshow/Parallax Lit). Carve has no height black and white points and wants metallic, occlusion and
// smoothness in one mask map, so Tools > Wash Square > Make Carve Maps bakes each set's own (next to its maps):
// - <set>_CarveHeight.png: the displacement stretched between its black and white points
// - <set>_CarveMask.png: R 1 (the Metallic slider passes through), G ambient occlusion, A smoothness
// - its height pyramid (Carve's fast tracing), in Carve Generated/Pyramids
// then reimports the blockout (BlockoutPostprocessor puts Carve on its relief materials once the maps are there).
// ponytail: Carve has no detail map, so these materials lose the weathering grime layer (the building decals stay); the
// trash cans are on URP Lit, not Carve, since Carve has no emission and Glow lights them up when they're looked at
static class CarveMaps
{
    // Carve's relief catches more light than the old parallax shader did (its normals follow the carved surface, and every pit's rim
    // glints), so the same smoothness read wet: these surfaces get this much of it
    internal const float SmoothnessScale = 0.7f;

    internal const string ShaderPath = "Assets/JamzeySt/Carve/Shaders/URP/Carve Lit.shader";
    const string Blockout = "Assets/Blender/WashingtonSquare/washingtonSquareBlockout.blend";


    // standalone Carve materials whose relief (height and normal) comes from another set than their colour: the
    // sidewalks carve as poured concrete flags (ambientCG Concrete031, CC0: a 4 x 4 grid of slabs per repeat, 1.5 m
    // each at the 6 m repeat, with their joints and pits), not Concrete047A's fine grain. (The park paths are gravel:
    // Materials/ParkPathGravel, a copy of the blockout's WSP_gravel, set on WSP_Park_Paths in the scene)
    static readonly (string material, string set, float black, float white, float depth)[] Relief =
    {
        ("Assets/Materials/Concrete047A_2K-JPG/Concrete047A.mat", "Assets/Materials/Concrete031_2K-JPG/Concrete031_2K-JPG_", 0.54f, 0.76f, 0.025f),
    };

    internal static string Pyramid(Texture height) => $"{CarvePyramidBaker.DefaultFolder}/{height.name}_CarvePyramid.asset";

    // one material onto Carve Lit; depth in metres, tiling as on the base map
    internal static void Apply(Material m, Shader carve, Texture color, Vector2 tiling, Texture normal, float normalScale,
        Texture mask, float metallic, float smoothness, Texture height, Texture pyramid, float depth)
    {
        m.shader = carve;
        m.SetTexture("_BaseColorMap", color);
        m.SetTextureScale("_BaseColorMap", tiling);
        m.SetTextureOffset("_BaseColorMap", Vector2.zero);
        m.SetColor("_BaseColor", Color.white);
        m.SetTexture("_NormalMap", normal);
        m.SetFloat("_NormalScale", normalScale);
        m.SetTexture("_MaskMap", mask);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Smoothness", smoothness);
        m.SetTexture("_HeightMap", height);
        m.SetTexture("_CarvePyramid", pyramid);
        m.SetFloat("_CarveTracing", pyramid ? 1 : 0);
        m.SetFloat("_CarveDepth", depth);
        Performance(m);
    }

    // profiled at ~6 ms of a ~22 ms frame. Drawn out to 25-60 m (10-25 cut it off too close), so each pixel is made
    // cheaper instead: fewer march and refine steps, fewer self-shadow steps, and Auto LOD dropping steps sooner with
    // distance; the height maps themselves are 1024 (Write), so every step reads a quarter of the texels
    internal static void Performance(Material m)
    {
        m.SetFloat("_CarveFadeStart", 25);
        m.SetFloat("_CarveFadeEnd", 60);
        m.SetFloat("_CarveMinSteps", 6);
        m.SetFloat("_CarveMaxSteps", 16);
        m.SetFloat("_CarveRefineSteps", 2);
        m.SetFloat("_CarveShadowSteps", 8);
        m.SetFloat("_CarveAutoLOD", 0.6f);
    }

    [MenuItem("Tools/Wash Square/Make Carve Maps")]
    static void Make()
    {
        try
        {
            int i = 0;
            foreach (var (set, range) in BlockoutPostprocessor.HeightRange)
            {
                EditorUtility.DisplayProgressBar("Carve maps", set, (float)i++ / BlockoutPostprocessor.HeightRange.Count);
                string prefix = BlockoutPostprocessor.Textures + set + "_2K-JPG_";
                var displacement = AssetDatabase.LoadAssetAtPath<Texture2D>(prefix + "Displacement.jpg");
                if (!displacement) { Debug.LogWarning("CarveMaps: no displacement for " + set); continue; }
                Texture2D height = Height(displacement, range.black, range.white, prefix + "CarveHeight.png");
                CarvePyramidBaker.BakeToAsset(height);
                Mask(AssetDatabase.LoadAssetAtPath<Texture2D>(prefix + "MetallicSmoothness.png"),
                    AssetDatabase.LoadAssetAtPath<Texture2D>(prefix + "AmbientOcclusion.jpg"), prefix + "CarveMask.png");
            }
            foreach (var relief in Relief) Borrow(relief);
        }
        finally { EditorUtility.ClearProgressBar(); }
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(Blockout, ImportAssetOptions.ForceUpdate);
        Debug.Log("CarveMaps: made the Carve maps and reimported the blockout");
    }

    // a Carve material's height and normal from another set, the height stretched between its black and white points
    static void Borrow((string material, string set, float black, float white, float depth) relief)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(relief.material);
        var source = AssetDatabase.LoadAssetAtPath<Texture2D>(relief.set + "Displacement.jpg");
        if (!m || m.shader.name != "Carve/URP/Carve Lit" || !source) { Debug.LogWarning("CarveMaps: can't borrow relief for " + relief.material); return; }
        var data = (TextureImporter)AssetImporter.GetAtPath(relief.set + "Displacement.jpg");
        if (data.sRGBTexture) { data.sRGBTexture = false; data.SaveAndReimport(); } // numbers, not colour
        var normals = (TextureImporter)AssetImporter.GetAtPath(relief.set + "NormalGL.jpg");
        if (normals.textureType != TextureImporterType.NormalMap) { normals.textureType = TextureImporterType.NormalMap; normals.SaveAndReimport(); }
        Texture2D height = Height(source, relief.black, relief.white, relief.set + "CarveHeight.png");
        Texture2D pyramid = CarvePyramidBaker.BakeToAsset(height);
        Undo.RecordObject(m, "Borrow Carve relief");
        m.SetTexture("_HeightMap", height);
        m.SetTexture("_CarvePyramid", pyramid);
        m.SetFloat("_CarveTracing", 1);
        m.SetTexture("_NormalMap", AssetDatabase.LoadAssetAtPath<Texture2D>(relief.set + "NormalGL.jpg"));
        m.SetFloat("_CarveDepth", relief.depth);
        Performance(m);
        EditorUtility.SetDirty(m);
        Debug.Log($"CarveMaps: {m.name} carves with {Path.GetFileName(relief.set.TrimEnd('_'))}'s relief, {relief.depth * 100:0.#} cm deep");
    }

    // the height map stretched between its black and white points, as a linear single-channel PNG
    static Texture2D Height(Texture2D source, float black, float white, string path)
    {
        Color[] px = Read(source);
        float range = Mathf.Max(white - black, 1e-3f);
        for (int i = 0; i < px.Length; i++)
        {
            float h = Mathf.Clamp01((px[i].g - black) / range);
            px[i] = new Color(h, h, h, 1);
        }
        return Write(px, source.width, source.height, path, true);
    }

    // R 1, G occlusion, A smoothness (the metallic map's alpha); none when there's neither map
    static Texture2D Mask(Texture2D smoothness, Texture2D occlusion, string path)
    {
        if (!smoothness && !occlusion) return null;
        Texture2D size = smoothness ? smoothness : occlusion;
        Color[] s = smoothness ? Read(smoothness, size.width, size.height) : null;
        Color[] o = occlusion ? Read(occlusion, size.width, size.height) : null;
        var px = new Color[size.width * size.height];
        for (int i = 0; i < px.Length; i++) px[i] = new Color(1, o != null ? o[i].g : 1, 0, s != null ? s[i].a : 1);
        return Write(px, size.width, size.height, path, false);
    }

    static Color[] Read(Texture2D t) => Read(t, t.width, t.height);

    // any texture's pixels, readable or not, as stored (linear)
    static Color[] Read(Texture2D t, int w, int h)
    {
        var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        Graphics.Blit(t, rt);
        var was = RenderTexture.active;
        RenderTexture.active = rt;
        var copy = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
        copy.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        RenderTexture.active = was;
        RenderTexture.ReleaseTemporary(rt);
        Color[] px = copy.GetPixels();
        Object.DestroyImmediate(copy);
        return px;
    }

    static Texture2D Write(Color[] px, int w, int h, string path, bool single)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
        tex.SetPixels(px);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.sRGBTexture = false;
        importer.alphaSource = single ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput;
        if (single)
        {
            importer.maxTextureSize = 1024; // relief at half the colour's resolution: a quarter of the reads per step
            // heights banded by compression read as terraces: keep all 8 bits of the one channel Carve reads
            var standalone = importer.GetPlatformTextureSettings("Standalone");
            standalone.maxTextureSize = 1024;
            standalone.overridden = true;
            standalone.format = TextureImporterFormat.R8;
            importer.SetPlatformTextureSettings(standalone);
        }
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}

// The blockout falls back to plain URP Lit when its import can't load the Carve shader or maps, which happens when it's
// reimported in the middle of a bigger refresh (a package install, the editor starting, a big import) before they are:
// the park then quietly lost Carve. After every refresh that touched the blockout, if its materials came out without
// Carve while the Carve maps are on disk, it's reimported once more, now that everything has loaded.
class CarveFallbackCheck : AssetPostprocessor
{
    const string Blockout = "Assets/Blender/WashingtonSquare/washingtonSquareBlockout.blend";
    static int retries; // a reimport that still falls back (the shader really broken) isn't repeated forever

    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        if (System.Array.IndexOf(imported, Blockout) < 0) return;
        EditorApplication.delayCall += () =>
        {
            bool fellBack = false;
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(Blockout))
                if (o is Material m && m.name == "WSP_cobble") fellBack = m.shader.name != "Carve/URP/Carve Lit";
            bool mapsMade = File.Exists(BlockoutPostprocessor.Textures + "PavingStones046_2K-JPG_CarveHeight.png");
            if (!fellBack || !mapsMade) { retries = 0; return; }
            if (++retries > 2) { Debug.LogError("CarveFallbackCheck: the blockout keeps coming out without Carve; is the Carve shader compiling?"); return; }
            Debug.LogWarning("CarveFallbackCheck: the blockout imported before Carve had loaded; reimporting it");
            AssetDatabase.ImportAsset(Blockout, ImportAssetOptions.ForceUpdate);
        };
    }
}
