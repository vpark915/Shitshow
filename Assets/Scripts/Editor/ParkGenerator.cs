using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Rebuilds the "ParkTerrain" lawn of the Washington Square blockout: hills, ground textures, trees, shrubs and grass.
// The terrain only exists over the blockout's lawns and planting beds (WSP_Park_Lawn, WSP_Park_PlantingBeds; it is cut
// away everywhere else), and nothing is planted on or right next to paths, fences or props.
// Tools > Wash Square > Regenerate Park. Tweak the numbers below and run it again; the same seed gives the same park.
public static class ParkGenerator
{
    // ---- tunables
    const int Seed = 1826;
    const float HillHeight = 3f;        // tallest a lawn mound can get, in metres
    const float HillRamp = 16f;         // metres from a path before a mound reaches full height; paths themselves stay flat
    const int BigTrees = 340, YoungTrees = 60, LooseBushes = 380, ShrubClusters = 80;
    const float BigTreeSpacing = 6f;
    const float TrunkThicken = 1.3f;    // big trees are widened by this much (trunk and crown) without getting taller
    const float GrassMin = 0.4f, GrassMax = 0.85f; // chance of a grass clump per 0.16 m² cell, in sparse and lush patches
    const float GrassDistance = 120f;    // metres from the camera that grass is drawn
    const float GrassFade = 20f;        // grass fades out over the last this-many metres of that distance
    static readonly Color FoliageHealthy = new Color(0.66f, 0.95f, 0.48f), FoliageDry = new Color(0.72f, 0.90f, 0.50f);

    // ---- park rectangle; Y0 sits the lawn just above the blockout's flat surface polygons (0.22-0.24)
    const float W = 294f, D = 139f, X0 = -147f, Z0 = -69.5f, Y0 = 0.28f, MaxHeight = 10f;
    const int GX = 2048, GZ = 1024, HR = 2049; // lawn grid cells (0.14 m), heightmap resolution
    const string Pack = "Assets/NatureManufacture Assets/Forest Environment Dynamic Nature/";
    const string DataPath = "Assets/Scenes/WashSquareParkTerrain.asset";

    static float[,] dist; // metres to the nearest paving / built thing, indexed [x, z]
    static bool[,] lawn;  // true where the blockout has grass or a planting bed, indexed [x, z]

    static float Dist(float wx, float wz)
    {
        int xi = Mathf.Clamp((int)((wx - X0) / W * GX), 0, GX - 1), zi = Mathf.Clamp((int)((wz - Z0) / D * GZ), 0, GZ - 1);
        float edge = Mathf.Min(Mathf.Min(wx - X0, X0 + W - wx), Mathf.Min(wz - Z0, Z0 + D - wz));
        return Mathf.Min(dist[xi, zi], edge);
    }

    static float Smooth(float from, float to, float value) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, value));

    [MenuItem("Tools/Wash Square/Regenerate Park")]
    public static string Generate()
    {
        Transform blockout = GameObject.Find("washingtonSquareBlockout").transform;
        Physics.SyncTransforms();
        Collider[] props = blockout.Cast<Transform>().Where(t => t.name.StartsWith("WSP_Prop_")).Select(t => t.GetComponent<Collider>()).ToArray();
        BuildDistanceField(new[] { "WSP_Park_Lawn", "WSP_Park_PlantingBeds" }.Select(n => blockout.Find(n).GetComponent<Collider>()).ToArray(), props);

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
            if (d < 1.8f || NearArch(p) || !Clear(p, bigAt, BigTreeSpacing)) continue;
            if (d > 6f && Random.value > 0.35f) continue; // mostly line the paths, leave some open lawn
            bigAt.Add(p); Add(p, Random.Range(0, big.Length), 0.85f, 1.15f, TrunkThicken);
        }
        for (int i = 0; i < 30000 && youngAt.Count < YoungTrees; i++)
        {
            Vector2 p = Anywhere();
            if (Dist(p.x, p.y) < 1.5f || NearArch(p) || !Clear(p, bigAt, 4.5f) || !Clear(p, youngAt, 4f)) continue;
            youngAt.Add(p); Add(p, big.Length + Random.Range(0, young.Length), 0.9f, 1.2f);
        }
        // shrubbery: tight clumps beside paths and around trunks
        int shrubs = 0;
        for (int c = 0, tries = 0; c < ShrubClusters && tries < 20000; tries++)
        {
            Vector2 centre = Anywhere(); float d = Dist(centre.x, centre.y);
            if (d < 1.5f || d > 7f || NearArch(centre)) continue;
            c++;
            int members = Random.Range(4, 9);
            for (int m = 0, t = 0; m < members && t < 60; t++)
            {
                Vector2 p = centre + Random.insideUnitCircle * 3.5f;
                if (Dist(p.x, p.y) < 1.2f || !Clear(p, bigAt, 1.5f) || !Clear(p, youngAt, 1.5f) || !Clear(p, bushAt, 1.3f)) continue;
                int kind = Random.Range(0, shrub.Length);
                bushAt.Add(p); Add(p, shrubStart + kind, shrubScale[kind] * 0.8f, shrubScale[kind] * 1.25f);
                m++; shrubs++;
            }
        }
        int loose = 0;
        for (int i = 0; i < 80000 && loose < LooseBushes; i++)
        {
            Vector2 p = Anywhere(); float d = Dist(p.x, p.y);
            if (d < 0.8f || NearArch(p) || !Clear(p, bigAt, 1.5f) || !Clear(p, youngAt, 1.5f) || !Clear(p, bushAt, 1.6f)) continue;
            bool nearSomething = d < 3f || !Clear(p, bigAt, 4f);
            if (!nearSomething && Random.value > 0.15f) continue; // small bushes hug path edges and tree bases
            bushAt.Add(p); Add(p, bushStart + Random.Range(0, bush.Length), 0.9f, 1.5f);
            loose++;
        }
        data.SetTreeInstances(trees.ToArray(), true);

        // ---- ground textures: mossy green lawn with grassier patches, bare soil along path edges, leaf litter under trees
        data.terrainLayers = new[] { "Terrain_Layer6_Moss", "Terrain_Layer4_Grass_Plants", "Terrain_Layer2_Soil", "Terrain_Layer5_Leaves" }
            .Select(n => AssetDatabase.LoadAssetAtPath<TerrainLayer>(Pack + "Terrain Layers/" + n + ".terrainlayer")).ToArray();
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
                float soil = Mathf.Max(1f - Smooth(0.2f, 1.2f, Dist(wx, wz)), shade[z, x] * shade[z, x] * 0.5f);
                float leaves = Mathf.Max(Smooth(0.65f, 0.8f, Mathf.PerlinNoise(wx / 18f + 3.1f, wz / 18f + 8.7f)) * 0.4f, shade[z, x] * 0.6f) * (1f - soil);
                float lawn = 1f - soil - leaves;
                float grassy = Smooth(0.4f, 0.7f, Mathf.PerlinNoise(wx / 12f + 40.2f, wz / 12f + 19.6f)) * 0.5f;
                alpha[z, x, 0] = lawn * (1f - grassy); alpha[z, x, 1] = lawn * grassy; alpha[z, x, 2] = soil; alpha[z, x, 3] = leaves;
            }
        data.SetAlphamaps(0, 0, alpha);

        // ---- grass and ground plants (terrain details, drawn instanced with the pack's own materials)
        // the pack's cheapest clumps (18-41 triangles each): grass is most of what the camera draws when it looks across
        // the park, and the 56-90 triangle clumps made turning the camera stutter
        string[] grass = { "grass_01_1", "grass_01_3", "grass_02_1", "grass_02_3", "grass_03_3", "plant_02_2" };
        string[] shadePlants = { "fern_01_4", "lily_valley_01_1", "plant_01_1" };
        string[] detailNames = grass.Concat(shadePlants).ToArray();
        data.detailPrototypes = detailNames.Select((n, i) => new DetailPrototype
        {
            prototype = AssetDatabase.LoadAssetAtPath<GameObject>(Pack + "Foliage and Grass/Unity Terrain Prefabs/prefab_Unity_Terrain_" + n + ".prefab"),
            usePrototypeMesh = true, useInstancing = true, renderMode = DetailRenderMode.VertexLit,
            minWidth = 1.1f, maxWidth = 1.8f, minHeight = 0.8f, maxHeight = 1.4f, noiseSeed = 100 + i, noiseSpread = 0.3f,
            healthyColor = Color.white, dryColor = Color.white, useDensityScaling = false
        }).ToArray();
        data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
        const int DR = 512;
        int[][,] layers = detailNames.Select(_ => new int[DR, DR]).ToArray();
        int clumps = 0;
        for (int z = 0; z < DR; z++)
            for (int x = 0; x < DR; x++)
            {
                float wx = X0 + (x + 0.5f) / DR * W, wz = Z0 + (z + 0.5f) / DR * D;
                if (Dist(wx, wz) < 0.4f) continue;
                float lush = Smooth(0.35f, 0.7f, Mathf.PerlinNoise(wx / 25f + 31.7f, wz / 25f + 90.2f));
                if (Random.value < Mathf.Lerp(GrassMin, GrassMax, lush)) { layers[Random.Range(0, grass.Length)][z, x]++; clumps++; }
                if (Random.value < shade[z * 2, x * 2] * 0.4f) { layers[grass.Length + Random.Range(0, shadePlants.Length)][z, x]++; clumps++; }
            }
        for (int i = 0; i < layers.Length; i++) data.SetDetailLayer(0, 0, i, layers[i]);

        // ---- greener leaves and grass: the pack's foliage materials multiply their texture by these two tints
        // ponytail: edits the pack's own materials in place (they were near-white); reimport the pack to get the originals back
        var materials = data.treePrototypes.Select(p => p.prefab).Concat(data.detailPrototypes.Select(p => p.prototype))
            .SelectMany(p => p.GetComponentsInChildren<Renderer>(true)).SelectMany(r => r.sharedMaterials).Where(m => m && m.HasProperty("_HealthyColor")).Distinct();
        foreach (Material m in materials)
        {
            m.SetColor("_HealthyColor", FoliageHealthy);
            m.SetColor("_DryColor", FoliageDry);
            EditorUtility.SetDirty(m);
        }

        // the pack's grass shader fades itself out 25-45 m from the camera whatever the terrain says; move that fade to the end of the grass distance
        foreach (Material m in data.detailPrototypes.SelectMany(p => p.prototype.GetComponentsInChildren<Renderer>(true)).SelectMany(r => r.sharedMaterials).Where(m => m && m.HasProperty("_CullFarStart")).Distinct())
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
        terrain.Flush();
        EditorUtility.SetDirty(data);
        EditorSceneManager.MarkSceneDirty(go.scene);

        float top = 0f; foreach (float h in heights) top = Mathf.Max(top, h * MaxHeight);
        string summary = $"Park regenerated: {bigAt.Count} big trees, {youngAt.Count} young trees, {shrubs} shrubs, {loose} small bushes, {clumps} grass clumps, tallest mound {top:F1} m";
        Debug.Log(summary);
        return summary;
    }

    // rasterises the lawn polygons and the props standing on them into a grid, then spreads distances outwards (two-pass chamfer)
    static void BuildDistanceField(Collider[] lawns, Collider[] props)
    {
        float cx = W / GX, cz = D / GZ, cd = Mathf.Sqrt(cx * cx + cz * cz);
        Ray Down(int x, int z) => new Ray(new Vector3(X0 + (x + 0.5f) * cx, 50f, Z0 + (z + 0.5f) * cz), Vector3.down);

        dist = new float[GX, GZ];
        lawn = new bool[GX, GZ];
        for (int x = 0; x < GX; x++)
            for (int z = 0; z < GZ; z++)
            {
                lawn[x, z] = lawns.Any(l => l.Raycast(Down(x, z), out _, 60f));
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
