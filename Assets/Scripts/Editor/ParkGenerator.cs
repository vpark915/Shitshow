using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Rebuilds the "ParkTerrain" lawn of the Washington Square blockout: hills, ground textures, trees, shrubs and grass.
// The terrain only exists over the blockout's lawns and planting beds (WSP_Park_Lawn, WSP_Park_PlantingBeds; it is cut
// away everywhere else), and nothing is planted on or right next to paths, fences or props. The lawns' middles are left
// open (OpenLawn), trees ring them along the paths, and a street tree stands in each sidewalk tree pit (StreetTrees).
// Tools > Wash Square > Regenerate Park. Tweak the numbers below and run it again; the same seed gives the same park.
public static class ParkGenerator
{
    // ---- tunables
    const int Seed = 1826;
    const float HillHeight = 3f;        // tallest a lawn mound can get, in metres
    const float HillRamp = 16f;         // metres from a path before a mound reaches full height; paths themselves stay flat
    // the real park: big shade trees ring the lawns along the paths, the lawns' middles open for sunbathing and frisbee,
    // shrubs in the planting beds and lawn edges
    const int BigTrees = 220, YoungTrees = 30, LooseBushes = 200, ShrubClusters = 50;
    const float BigTreeSpacing = 7.5f;
    const float OpenLawn = 8f;          // lawn this many metres or more from a path is left open: grass only
    const float TrunkThicken = 1.3f;    // big trees are widened by this much (trunk and crown) without getting taller
    const int GrassMin = 5, GrassMax = 9; // grass tufts per 0.16 m² cell, in thinner and lusher patches
    const float GrassDistance = 80f;     // metres from the camera that grass is drawn
    const float GrassFade = 20f;        // the shade plants fade out over the last this-many metres of that distance
    // the grass tufts' tint (times Textures/LawnBlades.png, already green), lush and thinner patches: mown-lawn green
    static readonly Color GrassHealthy = new Color(0.4f, 0.58f, 0.3f), GrassDry = new Color(0.47f, 0.56f, 0.28f); // brighter went neon lime in the sun
    const string Blades = "Assets/Textures/LawnBlades.png", LawnLayer = "Assets/Textures/Lawn.terrainlayer";
    // olive, not lime: the pack's leaf textures are already green, and the brighter tints read as neon in daylight
    static readonly Color FoliageHealthy = new Color(0.58f, 0.74f, 0.4f), FoliageDry = new Color(0.66f, 0.72f, 0.42f);

    // ---- park rectangle; Y0 sits the lawn just above the blockout's flat surface polygons (0.22-0.24)
    const float W = 294f, D = 139f, X0 = -147f, Z0 = -69.5f, Y0 = 0.28f, MaxHeight = 10f;
    const int GX = 2048, GZ = 1024, HR = 2049; // lawn grid cells (0.14 m), heightmap resolution
    const string Pack = "Assets/NatureManufacture Assets/Forest Environment Dynamic Nature/";
    const string DataPath = "Assets/Scenes/WashSquareParkTerrain.asset";

    static float[,] dist; // metres to the nearest paving / built thing, indexed [x, z]
    static bool[,] lawn;  // true where the blockout has grass or a planting bed, indexed [x, z]
    static bool[,] bed;   // true where it's a planting bed

    static float Dist(float wx, float wz)
    {
        int xi = Mathf.Clamp((int)((wx - X0) / W * GX), 0, GX - 1), zi = Mathf.Clamp((int)((wz - Z0) / D * GZ), 0, GZ - 1);
        float edge = Mathf.Min(Mathf.Min(wx - X0, X0 + W - wx), Mathf.Min(wz - Z0, Z0 + D - wz));
        return Mathf.Min(dist[xi, zi], edge);
    }

    static bool Bed(Vector2 p) => bed[Mathf.Clamp((int)((p.x - X0) / W * GX), 0, GX - 1), Mathf.Clamp((int)((p.y - Z0) / D * GZ), 0, GZ - 1)];
    static bool Open(Vector2 p) => !Bed(p) && Dist(p.x, p.y) >= OpenLawn; // a lawn's open middle

    static float Smooth(float from, float to, float value) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, value));

    [MenuItem("Tools/Wash Square/Regenerate Park")]
    public static string Generate()
    {
        Transform blockout = GameObject.Find("washingtonSquareBlockout").transform;
        Physics.SyncTransforms();
        Collider[] props = blockout.Cast<Transform>().Where(t => t.name.StartsWith("WSP_Prop_")).Select(t => t.GetComponent<Collider>()).ToArray();
        BuildDistanceField(blockout.Find("WSP_Park_Lawn").GetComponent<Collider>(), blockout.Find("WSP_Park_PlantingBeds").GetComponent<Collider>(), props);

        TerrainData data = AssetDatabase.LoadAssetAtPath<TerrainData>(DataPath);
        if (!data) { data = new TerrainData(); AssetDatabase.CreateAsset(data, DataPath); }
        data.heightmapResolution = HR;
        data.size = new Vector3(W, MaxHeight, D);
        data.alphamapResolution = 1024;
        data.SetDetailResolution(512, 32);

        // ---- hills: mounds in the lawns, flat next to anything paved so nothing pokes through the paths
        var heights = new float[HR, HR];
        for (int z = 0; z < HR; z++)
            for (int x = 0; x < HR; x++)
            {
                float wx = X0 + x / (HR - 1f) * W, wz = Z0 + z / (HR - 1f) * D;
                float noise = 0.65f * Mathf.PerlinNoise(wx / 45f + 57.3f, wz / 45f + 11.9f) + 0.35f * Mathf.PerlinNoise(wx / 17f + 5.1f, wz / 17f + 73.4f);
                heights[z, x] = Smooth(1f, HillRamp, Dist(wx, wz)) * HillHeight * Mathf.Lerp(0.25f, 1f, noise) / MaxHeight;
            }
        data.SetHeights(0, 0, heights);

        // ---- cut the terrain away wherever the blockout isn't grass or a planting bed
        int holes = data.holesResolution;
        var surface = new bool[holes, holes];
        for (int z = 0; z < holes; z++)
            for (int x = 0; x < holes; x++)
                surface[z, x] = lawn[x * GX / holes, z * GZ / holes];
        data.SetHoles(0, 0, surface);

        // ---- trees, bushes and shrubs (all terrain "trees")
        Random.InitState(Seed);
        // the pack's thickest trunks (0.4 m radius and up); 02, 03 and 08 are the spindly ones
        string[] big = { "beech_tree_04", "beech_tree_05", "beech_tree_06", "beech_tree_07", "beech_tree_09" };
        string[] young = { "beech_tree_00_C", "beech_tree_00_D", "beech_tree_01" };
        string[] bush = { "Bushes/Prefabs/Prefab_Forest_black_cherry_02", "Bushes/Prefabs/Prefab_Forest_black_cherry_03", "Bushes/Prefabs/Prefab_Forest_black_cherry_05", "Beech Trees/Prefabs/prefab_beech_plant_02", "Beech Trees/Prefabs/prefab_beech_plant_03" };
        // shrubs are the bushiest small prefabs, scaled up to head height
        string[] shrub = { "Bushes/Prefabs/Prefab_Forest_black_cherry_03", "Bushes/Prefabs/Prefab_Forest_black_cherry_05", "Beech Trees/Prefabs/prefab_beech_plant_03", "Beech Trees/Prefabs/prefab_beech_tree_00_A", "Beech Trees/Prefabs/prefab_beech_tree_00_B" };
        float[] shrubScale = { 2.2f, 2.2f, 1.2f, 0.9f, 0.8f };
        string[] prototypePaths = big.Concat(young).Select(n => "Beech Trees/Prefabs/prefab_" + n).Concat(bush).Concat(shrub).ToArray();
        data.SetTreeInstances(new TreeInstance[0], false); // drop the old planting first, or Unity logs an error per orphaned tree type
        data.treePrototypes = prototypePaths.Select(p => new TreePrototype { prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Pack + p + ".prefab") }).ToArray();
        int bushStart = big.Length + young.Length, shrubStart = bushStart + bush.Length;

        var trees = new List<TreeInstance>();
        var bigAt = new List<Vector2>(); var youngAt = new List<Vector2>(); var bushAt = new List<Vector2>();
        void Add(Vector2 p, int prototype, float scaleMin, float scaleMax, float widen = 1f)
        {
            float s = Random.Range(scaleMin, scaleMax);
            trees.Add(new TreeInstance
            {
                position = new Vector3((p.x - X0) / W, 0f, (p.y - Z0) / D), prototypeIndex = prototype,
                widthScale = s * widen, heightScale = s * Random.Range(0.95f, 1.05f), rotation = Random.Range(0f, Mathf.PI * 2f),
                color = Color.white, lightmapColor = Color.white
            });
        }
        Vector2 Anywhere() => new Vector2(Random.Range(X0 + 2f, X0 + W - 2f), Random.Range(Z0 + 2f, Z0 + D - 2f));
        bool Clear(Vector2 p, List<Vector2> others, float radius) { foreach (Vector2 o in others) if ((o - p).sqrMagnitude < radius * radius) return false; return true; }
        bool NearArch(Vector2 p) => Mathf.Abs(p.x) < 18f && p.y < -50f; // keep the view of the arch open

        for (int i = 0; i < 60000 && bigAt.Count < BigTrees; i++)
        {
            Vector2 p = Anywhere(); float d = Dist(p.x, p.y);
            if (d < 1.8f || NearArch(p) || Open(p) || !Clear(p, bigAt, BigTreeSpacing)) continue;
            if (d > 4.5f && Random.value > 0.25f) continue; // mostly a row along the paths
            bigAt.Add(p); Add(p, Random.Range(0, big.Length), 0.85f, 1.15f, TrunkThicken);
        }
        for (int i = 0; i < 30000 && youngAt.Count < YoungTrees; i++)
        {
            Vector2 p = Anywhere();
            if (Dist(p.x, p.y) < 1.5f || NearArch(p) || Open(p) || !Clear(p, bigAt, 4.5f) || !Clear(p, youngAt, 4f)) continue;
            youngAt.Add(p); Add(p, big.Length + Random.Range(0, young.Length), 0.9f, 1.2f);
        }
        // shrubbery: tight clumps beside paths and around trunks
        int shrubs = 0;
        for (int c = 0, tries = 0; c < ShrubClusters && tries < 20000; tries++)
        {
            Vector2 centre = Anywhere(); float d = Dist(centre.x, centre.y);
            if (d < 1.5f || NearArch(centre) || (d > 4f && !Bed(centre))) continue; // in the beds, or hugging a path
            c++;
            int members = Random.Range(4, 9);
            for (int m = 0, t = 0; m < members && t < 60; t++)
            {
                Vector2 p = centre + Random.insideUnitCircle * 3.5f;
                if (Dist(p.x, p.y) < 1.2f || Open(p) || !Clear(p, bigAt, 1.5f) || !Clear(p, youngAt, 1.5f) || !Clear(p, bushAt, 1.3f)) continue;
                int kind = Random.Range(0, shrub.Length);
                bushAt.Add(p); Add(p, shrubStart + kind, shrubScale[kind] * 0.8f, shrubScale[kind] * 1.25f);
                m++; shrubs++;
            }
        }
        int loose = 0;
        for (int i = 0; i < 80000 && loose < LooseBushes; i++)
        {
            Vector2 p = Anywhere(); float d = Dist(p.x, p.y);
            if (d < 0.8f || NearArch(p) || Open(p) || !Clear(p, bigAt, 1.5f) || !Clear(p, youngAt, 1.5f) || !Clear(p, bushAt, 1.6f)) continue;
            bool nearSomething = d < 3f || !Clear(p, bigAt, 4f);
            if (!nearSomething && !Bed(p)) continue; // small bushes hug path edges and tree bases, or fill the beds
            bushAt.Add(p); Add(p, bushStart + Random.Range(0, bush.Length), 0.9f, 1.5f);
            loose++;
        }
        data.SetTreeInstances(trees.ToArray(), true);

        // ---- ground textures: mown lawn (ambientCG Grass004) with mossier patches, a thin line of soil along path edges, leaf litter under trees
        data.terrainLayers = new[] { "Terrain_Layer6_Moss", null, "Terrain_Layer2_Soil", "Terrain_Layer5_Leaves" }
            .Select(n => n == null ? Lawn() : AssetDatabase.LoadAssetAtPath<TerrainLayer>(Pack + "Terrain Layers/" + n + ".terrainlayer")).ToArray();
        const int A = 1024;
        var shade = new float[A, A]; // 0..1 closeness to a big tree trunk, indexed [z, x]
        foreach (Vector2 t in bigAt)
        {
            const float r = 3.5f;
            int x0 = Mathf.Max(0, (int)((t.x - r - X0) / W * A)), x1 = Mathf.Min(A - 1, (int)((t.x + r - X0) / W * A));
            int z0 = Mathf.Max(0, (int)((t.y - r - Z0) / D * A)), z1 = Mathf.Min(A - 1, (int)((t.y + r - Z0) / D * A));
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    float away = Vector2.Distance(t, new Vector2(X0 + (x + 0.5f) / A * W, Z0 + (z + 0.5f) / A * D));
                    shade[z, x] = Mathf.Max(shade[z, x], 1f - Mathf.Clamp01(away / r));
                }
        }
        var alpha = new float[A, A, 4];
        for (int z = 0; z < A; z++)
            for (int x = 0; x < A; x++)
            {
                float wx = X0 + (x + 0.5f) / A * W, wz = Z0 + (z + 0.5f) / A * D;
                float soil = Mathf.Max(1f - Smooth(0.1f, 0.6f, Dist(wx, wz)), shade[z, x] * shade[z, x] * 0.35f);
                float leaves = Mathf.Max(Smooth(0.7f, 0.85f, Mathf.PerlinNoise(wx / 18f + 3.1f, wz / 18f + 8.7f)) * 0.15f, shade[z, x] * 0.45f) * (1f - soil);
                float lawn = 1f - soil - leaves;
                float grassy = 0.8f + 0.2f * Smooth(0.3f, 0.6f, Mathf.PerlinNoise(wx / 12f + 40.2f, wz / 12f + 19.6f));
                alpha[z, x, 0] = lawn * (1f - grassy); alpha[z, x, 1] = lawn * grassy; alpha[z, x, 2] = soil; alpha[z, x, 3] = leaves;
            }
        data.SetAlphamaps(0, 0, alpha);

        // ---- grass and ground plants (terrain details)
        // grass: Unity's own terrain grass, short tufts of blades (one textured quad each, 2 triangles, swaying in the
        // wind); the pack's 1-2 m forest-grass clumps read as heavy meadow, not a mown lawn
        // the shade plants under trees stay the pack's meshes, drawn instanced with its own materials
        var blades = (TextureImporter)AssetImporter.GetAtPath(Blades);
        if (!blades.alphaIsTransparency || blades.wrapMode != TextureWrapMode.Clamp)
        {
            blades.alphaIsTransparency = true; // see-through edges stay green when mipped, not dark
            blades.wrapMode = TextureWrapMode.Clamp;
            blades.mipmapEnabled = true;
            blades.SaveAndReimport();
        }
        string[] shadePlants = { "fern_01_4", "lily_valley_01_1", "plant_01_1" };
        var grassTuft = new DetailPrototype
        {
            prototypeTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Blades), usePrototypeMesh = false, renderMode = DetailRenderMode.Grass,
            minWidth = 0.25f, maxWidth = 0.4f, minHeight = 0.14f, maxHeight = 0.26f, noiseSeed = 100, noiseSpread = 0.15f,
            healthyColor = GrassHealthy, dryColor = GrassDry, useDensityScaling = false
        };
        const int grass = 1; // detail layer 0 is the grass, the shade plants after it
        data.detailPrototypes = new[] { grassTuft }.Concat(shadePlants.Select((n, i) => new DetailPrototype
        {
            prototype = AssetDatabase.LoadAssetAtPath<GameObject>(Pack + "Foliage and Grass/Unity Terrain Prefabs/prefab_Unity_Terrain_" + n + ".prefab"),
            usePrototypeMesh = true, useInstancing = true, renderMode = DetailRenderMode.VertexLit,
            minWidth = 1.1f, maxWidth = 1.8f, minHeight = 0.8f, maxHeight = 1.4f, noiseSeed = 101 + i, noiseSpread = 0.3f,
            healthyColor = Color.white, dryColor = Color.white, useDensityScaling = false
        })).ToArray();
        data.wavingGrassTint = Color.white; // the sway's own tint; white leaves the grass colours alone
        data.wavingGrassStrength = 0.25f;
        data.wavingGrassAmount = 0.3f;
        data.wavingGrassSpeed = 0.4f;
        data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
        const int DR = 512;
        int[][,] layers = data.detailPrototypes.Select(_ => new int[DR, DR]).ToArray();
        int clumps = 0;
        for (int z = 0; z < DR; z++)
            for (int x = 0; x < DR; x++)
            {
                float wx = X0 + (x + 0.5f) / DR * W, wz = Z0 + (z + 0.5f) / DR * D;
                if (Dist(wx, wz) < 0.4f) continue;
                float lush = Smooth(0.35f, 0.7f, Mathf.PerlinNoise(wx / 25f + 31.7f, wz / 25f + 90.2f));
                int tufts = Mathf.RoundToInt(Mathf.Lerp(GrassMin, GrassMax, lush) * (1f - 0.5f * shade[z * 2, x * 2])); // thinner in a tree's shade
                layers[0][z, x] = tufts; clumps += tufts;
                if (Random.value < shade[z * 2, x * 2] * 0.4f) { layers[grass + Random.Range(0, shadePlants.Length)][z, x]++; clumps++; }
            }
        for (int i = 0; i < layers.Length; i++) data.SetDetailLayer(0, 0, i, layers[i]);

        // ---- greener leaves and grass: the pack's foliage materials multiply their texture by these two tints
        // ponytail: edits the pack's own materials in place (they were near-white); reimport the pack to get the originals back
        var materials = data.treePrototypes.Select(p => p.prefab).Concat(data.detailPrototypes.Where(p => p.usePrototypeMesh).Select(p => p.prototype))
            .SelectMany(p => p.GetComponentsInChildren<Renderer>(true)).SelectMany(r => r.sharedMaterials).Where(m => m && m.HasProperty("_HealthyColor")).Distinct();
        foreach (Material m in materials)
        {
            m.SetColor("_HealthyColor", FoliageHealthy);
            m.SetColor("_DryColor", FoliageDry);
            EditorUtility.SetDirty(m);
        }

        // the far LOD's cross-card materials have no foliage tint, so they stood out bright green against the tinted
        // leaves: give them the leaves' tint (in their base colour, the pack's 0.85 grey times the tint)
        foreach (Material m in data.treePrototypes.SelectMany(p => p.prefab.GetComponentsInChildren<Renderer>(true)).SelectMany(r => r.sharedMaterials)
            .Where(m => m && m.name.EndsWith("_Cross") && m.HasProperty("_BaseColor")).Distinct())
        {
            m.SetColor("_BaseColor", Color.Lerp(FoliageHealthy, FoliageDry, 0.5f) * 0.85f);
            EditorUtility.SetDirty(m);
        }

        // the pack's LODs turned trees into cross cards below 30% of the screen's height (a few dozen metres) and kept
        // full detail only nearer than the tree is tall; now full detail out to ~20 m, the 2,600-triangle LOD2 out to ~200 m,
        // cards past that (the street trees are the same prefabs)
        // ponytail: edits the pack's prefabs in place, as the materials above
        foreach (GameObject prefab in data.treePrototypes.Select(p => p.prefab).Distinct())
        {
            if (!prefab.TryGetComponent(out LODGroup group)) continue;
            LOD[] lods = group.GetLODs();
            float[] at = lods.Length == 4 ? new[] { 0.6f, 0.3f, 0.06f, 0.012f } : lods.Length == 3 ? new[] { 0.3f, 0.1f, 0.012f } : null;
            if (at == null) continue;
            for (int i = 0; i < lods.Length; i++) lods[i].screenRelativeTransitionHeight = at[i];
            group.SetLODs(lods);
            PrefabUtility.SavePrefabAsset(prefab);
        }

        // the pack's grass shader fades itself out 25-45 m from the camera whatever the terrain says; move that fade to the end of the grass distance
        foreach (Material m in data.detailPrototypes.Where(p => p.usePrototypeMesh).SelectMany(p => p.prototype.GetComponentsInChildren<Renderer>(true)).SelectMany(r => r.sharedMaterials).Where(m => m && m.HasProperty("_CullFarStart")).Distinct())
        {
            m.SetFloat("_CullFarStart", GrassDistance - GrassFade);
            m.SetFloat("_CullFarDistance", GrassFade);
            EditorUtility.SetDirty(m);
        }

        // ---- terrain object
        GameObject go = GameObject.Find("ParkTerrain");
        if (!go) { go = Terrain.CreateTerrainGameObject(data); go.name = "ParkTerrain"; }
        go.transform.position = new Vector3(X0, Y0, Z0);
        go.isStatic = true;
        Terrain terrain = go.GetComponent<Terrain>();
        terrain.drawInstanced = true;
        terrain.treeDistance = 300f; // profiled: trees out to 600 m cost ~1,900 draw calls a frame
        terrain.treeBillboardDistance = 30f; // full trees nearer than this, flat billboards beyond
        terrain.detailObjectDistance = GrassDistance;
        terrain.detailObjectDensity = 1f;
        terrain.heightmapPixelError = 3f;
        terrain.basemapDistance = 300f;
        data.RefreshPrototypes(); // or the editor keeps drawing the old grass colours and texture
        terrain.Flush();
        EditorUtility.SetDirty(data);
        EditorSceneManager.MarkSceneDirty(go.scene);

        int street = StreetTrees(blockout, big.Select(n => "Beech Trees/Prefabs/prefab_" + n).ToArray());

        float top = 0f; foreach (float h in heights) top = Mathf.Max(top, h * MaxHeight);
        string summary = $"Park regenerated: {bigAt.Count} big trees, {youngAt.Count} young trees, {shrubs} shrubs, {loose} small bushes, {clumps} grass tufts and plants, {street} street trees, tallest mound {top:F1} m";
        Debug.Log(summary);
        return summary;
    }

    // NYC street trees: one in each of the blockout's sidewalk tree pits (WSP_TreePit_*: granite kerb, soil, iron
    // guard, drawn in Blender). Real tree objects (the terrain only covers the park), under "StreetTrees"; rerunning
    // replaces them. Lit by the probes, not the lightmaps (a hundred trees would double the bake). Each pit is carved
    // out of the people's NavMesh (its guard is in the way), without re-baking.
    static int StreetTrees(Transform blockout, string[] kinds)
    {
        GameObject root = GameObject.Find("StreetTrees");
        if (!root) root = new GameObject("StreetTrees");
        for (int i = root.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
        int planted = 0;
        foreach (Transform pit in blockout)
        {
            if (!pit.name.StartsWith("WSP_TreePit") || !pit.TryGetComponent(out MeshFilter shape)) continue;
            Bounds local = shape.sharedMesh.bounds;
            Vector3 at = pit.TransformPoint(local.center);
            at.y = pit.GetComponent<Renderer>().bounds.min.y; // the soil, at the pit's bottom
            var tree = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Pack + kinds[Random.Range(0, kinds.Length)] + ".prefab"), root.transform);
            tree.transform.SetPositionAndRotation(at, Quaternion.Euler(0, Random.Range(0f, 360f), 0));
            tree.transform.localScale = Vector3.one * Random.Range(0.7f, 0.85f); // younger and smaller than the park's
            foreach (Transform t in tree.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);

            if (!pit.TryGetComponent(out UnityEngine.AI.NavMeshObstacle hole)) hole = pit.gameObject.AddComponent<UnityEngine.AI.NavMeshObstacle>();
            hole.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
            hole.center = local.center;
            hole.size = local.size;
            hole.carving = true;
            hole.carveOnlyStationary = true;
            planted++;
        }
        if (planted == 0) Debug.LogWarning("ParkGenerator: no WSP_TreePit_* in the blockout, no street trees");
        return planted;
    }

    // rasterises the lawn polygons and the props standing on them into a grid, then spreads distances outwards (two-pass chamfer)
    // Textures/Lawn.terrainlayer: ambientCG Grass004 (mown lawn) tiled every 2 m
    static TerrainLayer Lawn()
    {
        var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(LawnLayer);
        if (!layer) { layer = new TerrainLayer(); AssetDatabase.CreateAsset(layer, LawnLayer); }
        const string set = "Assets/Blender/Textures/WashingtonSquareTextures/Grass004_2K-JPG_";
        layer.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(set + "Color.jpg");
        layer.normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(set + "NormalGL.jpg");
        layer.tileSize = new Vector2(2f, 2f);
        layer.normalScale = 0.6f;
        layer.smoothness = 0.1f;
        layer.diffuseRemapMax = new Vector4(0.8f, 1f, 0.75f, 1f); // the photo's olive, a little greener (seen past the grass distance)
        EditorUtility.SetDirty(layer);
        return layer;
    }

    static void BuildDistanceField(Collider grass, Collider beds, Collider[] props)
    {
        float cx = W / GX, cz = D / GZ, cd = Mathf.Sqrt(cx * cx + cz * cz);
        Ray Down(int x, int z) => new Ray(new Vector3(X0 + (x + 0.5f) * cx, 50f, Z0 + (z + 0.5f) * cz), Vector3.down);

        dist = new float[GX, GZ];
        lawn = new bool[GX, GZ];
        bed = new bool[GX, GZ];
        for (int x = 0; x < GX; x++)
            for (int z = 0; z < GZ; z++)
            {
                bed[x, z] = beds.Raycast(Down(x, z), out _, 60f);
                lawn[x, z] = bed[x, z] || grass.Raycast(Down(x, z), out _, 60f);
                dist[x, z] = lawn[x, z] ? 1e6f : 0f;
            }
        // benches, lamps, statues: the lawn stays under them but nothing is planted there
        foreach (Collider prop in props)
        {
            Bounds b = prop.bounds;
            int x0 = Mathf.Max(0, (int)((b.min.x - X0) / cx)), x1 = Mathf.Min(GX - 1, (int)((b.max.x - X0) / cx));
            int z0 = Mathf.Max(0, (int)((b.min.z - Z0) / cz)), z1 = Mathf.Min(GZ - 1, (int)((b.max.z - Z0) / cz));
            for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                    if (prop.Raycast(Down(x, z), out _, 60f)) dist[x, z] = 0f;
        }
        for (int x = 0; x < GX; x++)
            for (int z = 0; z < GZ; z++)
            {
                float d = dist[x, z];
                if (x > 0) d = Mathf.Min(d, dist[x - 1, z] + cx);
                if (z > 0) d = Mathf.Min(d, dist[x, z - 1] + cz);
                if (x > 0 && z > 0) d = Mathf.Min(d, dist[x - 1, z - 1] + cd);
                if (x < GX - 1 && z > 0) d = Mathf.Min(d, dist[x + 1, z - 1] + cd);
                dist[x, z] = d;
            }
        for (int x = GX - 1; x >= 0; x--)
            for (int z = GZ - 1; z >= 0; z--)
            {
                float d = dist[x, z];
                if (x < GX - 1) d = Mathf.Min(d, dist[x + 1, z] + cx);
                if (z < GZ - 1) d = Mathf.Min(d, dist[x, z + 1] + cz);
                if (x < GX - 1 && z < GZ - 1) d = Mathf.Min(d, dist[x + 1, z + 1] + cd);
                if (x > 0 && z < GZ - 1) d = Mathf.Min(d, dist[x - 1, z + 1] + cd);
                dist[x, z] = d;
            }
    }
}
