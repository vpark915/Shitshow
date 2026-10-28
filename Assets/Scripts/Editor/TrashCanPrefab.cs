using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

// Tools > Wash Square > Build Trash Can Prefab: makes Prefabs/TrashCan from Blender/Props/trashCan.blend (an NYC wire
// litter basket, its bin bag and a pile of trash), standing on y = 0, with the park's surface treatment
// (BlockoutPostprocessor's, and its strength knobs): the can's own colour maps plus the ambientCG sets' normal,
// smoothness and height maps under Parallax Lit, and the weathering grime layer, saved as Materials/TrashCan/*.mat.
// Also lightmap UVs, a box to look at and bump into, a carved NavMesh hole so people walk round it, and TrashCan
// stocking any of the food prefabs. Run it again after changing the .blend.
public static class TrashCanPrefab
{
    const string Model = "Assets/Blender/Props/trashCan.blend", Prefab = "Assets/Prefabs/TrashCan.prefab";
    const string Colours = "Assets/Blender/Textures/TrashCanTextures/", Materials = "Assets/Materials/TrashCan/";

    // each material in the .blend: the ambientCG set its normal map came from in Blender (and its folder), smoothness
    // (times the set's smoothness map where it has one) and relief (metres, by parallax; the .blend's UVs are metres)
    static readonly (string name, string set, string folder, float smoothness, float relief)[] Surfaces =
    {
        ("TC_PaintedMetal", "PaintedMetal004", BlockoutPostprocessor.Textures, 0.47f, 0.002f), // green paint, chipped (as the park's iron)
        ("TC_Paper", "Concrete034", BlockoutPostprocessor.Textures, 0.4f, 0.006f),             // crumpled: the concrete's pits and ridges
        // ponytail: no height map in the plastic set, so the bag has no parallax; make one (as PaintedMetal004's) if it shows
        ("TC_Liner", "Plastic012A", "Assets/Blender/Textures/FoodCartTextures/", 0.55f, 0),    // a bin bag: shiny
    };

    [MenuItem("Tools/Wash Square/Build Trash Can Prefab")]
    static void Build()
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(Model);
        if (!importer.generateSecondaryUV) { importer.generateSecondaryUV = true; importer.SaveAndReimport(); } // for the lightmap bake

        var root = new GameObject("TrashCan");
        var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Model));
        PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        model.name = "Model";
        model.transform.SetParent(root.transform, false);
        foreach (Renderer r in model.GetComponentsInChildren<Renderer>())
        {
            Material[] slots = r.sharedMaterials;
            for (int i = 0; i < slots.Length; i++) if (slots[i]) slots[i] = Surface(slots[i].name) is Material m && m ? m : slots[i];
            r.sharedMaterials = slots;
        }

        // standing on y = 0, centred
        Bounds all = Look(root);
        model.transform.position -= new Vector3(all.center.x, all.min.y, all.center.z);
        all = Look(root);

        var box = root.AddComponent<BoxCollider>();
        box.center = all.center;
        box.size = all.size;
        var hole = root.AddComponent<NavMeshObstacle>(); // people walk round it, without re-baking the scene
        hole.shape = NavMeshObstacleShape.Capsule;
        hole.radius = Mathf.Max(all.extents.x, all.extents.z);
        hole.height = all.size.y;
        hole.center = all.center;
        hole.carving = true;
        hole.carveOnlyStationary = true;

        var can = root.AddComponent<TrashCan>();
        foreach (Transform part in model.GetComponentsInChildren<Transform>()) if (part.name == "TrashCan_Trash") can.trash = part.gameObject;
        var foods = new System.Collections.Generic.List<FoodItem>();
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" }))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (prefab.transform.parent == null && prefab.TryGetComponent(out FoodItem food)) foods.Add(food);
        }
        can.foods = foods.ToArray();

        foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = true; // lightmapped
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // 24k triangles a can: not worth a second draw (CleanBake's Cheaper Shadows)
        PrefabUtility.SaveAsPrefabAsset(root, Prefab);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets(); // the materials' settings, or the .mat files keep the defaults they were made with
        Debug.Log("TrashCanPrefab: made " + Prefab + " (" + all.size.ToString("F2") + " m, " + foods.Count + " foods it can hold)");
    }

    // the .blend material `name` remade as a Parallax Lit .mat, or null if it isn't one of Surfaces
    static Material Surface(string name)
    {
        int i = System.Array.FindIndex(Surfaces, s => s.name == name);
        if (i < 0) return null;
        var (_, set, folder, smoothness, relief) = Surfaces[i];
        string prefix = folder + set + "_2K-JPG_";
        if (!AssetDatabase.IsValidFolder(Materials.TrimEnd('/'))) AssetDatabase.CreateFolder("Assets/Materials", "TrashCan");
        string path = Materials + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader parallax = AssetDatabase.LoadAssetAtPath<Shader>(BlockoutPostprocessor.ParallaxShader);
        if (!material) { material = new Material(parallax); AssetDatabase.CreateAsset(material, path); }
        material.shader = parallax;

        material.SetTexture("_BaseMap", Load(Colours + name + "_Color.jpg"));
        material.SetColor("_BaseColor", Color.white);
        material.SetTexture("_BumpMap", Load(prefix + "NormalGL.jpg"));
        material.SetFloat("_BumpScale", BlockoutPostprocessor.NormalStrength);
        material.EnableKeyword("_NORMALMAP");

        Texture2D gloss = Load(prefix + "MetallicSmoothness.png");
        material.SetTexture("_MetallicGlossMap", gloss);
        if (gloss) material.EnableKeyword("_METALLICSPECGLOSSMAP");
        else material.DisableKeyword("_METALLICSPECGLOSSMAP");
        material.SetFloat("_SmoothnessTextureChannel", 0);
        material.SetFloat("_Smoothness", smoothness * BlockoutPostprocessor.SmoothnessScale);
        material.SetFloat("_Metallic", 0); // painted, paper, plastic

        Texture2D heights = Load(prefix + "Displacement.jpg");
        if (relief > 0 && heights && BlockoutPostprocessor.HeightRange.TryGetValue(set, out var range))
        {
            material.SetTexture("_ParallaxMap", heights);
            material.SetFloat("_Parallax", relief * BlockoutPostprocessor.ParallaxStrength); // UVs in metres: depth in metres
            material.SetFloat("_ParallaxMin", range.black);
            material.SetFloat("_ParallaxMax", range.white);
            material.EnableKeyword("_PARALLAXMAP");
        }
        else material.DisableKeyword("_PARALLAXMAP");

        // the park's grime, smaller (a can is under a metre: the park's 7 m stains would be one flat tint)
        material.SetTexture("_DetailAlbedoMap", Load(BlockoutPostprocessor.Textures + "WSP_Weathering_DetailX2.png"));
        material.SetTextureScale("_DetailAlbedoMap", Vector2.one / 1.5f);
        material.SetFloat("_DetailAlbedoMapScale", BlockoutPostprocessor.WeatheringStrength);
        material.EnableKeyword("_DETAIL_MULX2");
        EditorUtility.SetDirty(material);
        return material;
    }

    static Texture2D Load(string path) => AssetDatabase.LoadAssetAtPath<Texture2D>(path);

    static Bounds Look(GameObject thing)
    {
        Renderer[] looks = thing.GetComponentsInChildren<Renderer>();
        Bounds b = looks[0].bounds;
        foreach (Renderer r in looks) b.Encapsulate(r.bounds);
        return b;
    }
}
