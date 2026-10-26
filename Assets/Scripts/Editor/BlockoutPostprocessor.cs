using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Runs every time the Washington Square blockout is (re)imported from Blender, so the clean-up and colliders survive re-exports.
class BlockoutPostprocessor : AssetPostprocessor
{
    // name prefixes of flat, hollow or walk-through pieces, which need their real shape (WSP_Park covers the park's
    // ground pieces and fences); everything else is a building or a small prop and gets a box
    static readonly string[] MeshShaped = { "WSP_Road", "WSP_Sidewalk", "WSP_Park", "WSP_Fountain", "WSP_Arch", "WSP_Prop_LongBenches", "WSP_Prop_Mound" };

    // the ground: its textures are projected in world metres (the pieces come without usable UVs)
    static readonly string[] Flat = { "WSP_Park_Base", "WSP_Park_Paths", "WSP_Sidewalk", "WSP_Road", "WSP_Road_Cobble" };

    const float PavingTile = 3f; // metres covered by one repeat of the paving texture

    // the park paths, cut into a grid: the scene hides them (the terrain draws the paths), but the NPC NavMesh bake lays
    // their shape on the terrain point by point (NpcSetup.Draped), and its triangles run up to 300 m long uncut
    static readonly string[] Ground = { "WSP_Park_Paths" };
    const float GridCell = 4f; // metres

    // bump to force a reimport after changing this file
    public override uint GetVersion() => 33; // 33 rim jets flow; 32 lightmap UVs;24 arch tessellation cut (undone); 26 smoothness, granite, weathering; 27 _Glossiness; 28 parallax; 29 MatteImports; 30 displacement retired, deeper stone, granite parallax; 31 deeper brick

    // the meshes are rewritten on import (UVs, GridCut), and the NavMesh bake reads the paths', which needs them readable
    void OnPreprocessModel()
    {
        if (assetPath.EndsWith("washingtonSquareBlockout.blend")) ((ModelImporter)assetImporter).isReadable = true;
    }

    void OnPostprocessModel(GameObject root)
    {
        if (!assetPath.EndsWith("washingtonSquareBlockout.blend")) return;

        foreach (Transform child in root.transform.Cast<Transform>().ToArray())
        {
            // Blender's default cube and the empty camera / light nodes
            if (!child.GetComponent<MeshFilter>() || !child.name.StartsWith("WSP_"))
            {
                Object.DestroyImmediate(child.gameObject);
                continue;
            }

            child.gameObject.isStatic = true;
            // project every piece's textures in world space (1 UV = PavingTile metres), from above for the ground and
            // tops, from the side for walls and kerbs; the chess tables keep Blender's UVs for their board
            if (!child.name.StartsWith("WSP_Prop_ChessTable"))
                BoxUVs(child.GetComponent<MeshFilter>().sharedMesh, child, Flat.Contains(child.name));
            if (Ground.Contains(child.name)) GridCut(child.GetComponent<MeshFilter>().sharedMesh, child);
            // lightmap UVs, last (the rewrites above split vertices): the .blend has one UV set, and without a second the
            // bake falls back to the box UVs above, which tile every few metres, so every tile shared the same lightmap texels
            Unwrapping.GenerateSecondaryUVSet(child.GetComponent<MeshFilter>().sharedMesh);
            if (MeshShaped.Any(child.name.StartsWith)) child.gameObject.AddComponent<MeshCollider>();
            else child.gameObject.AddComponent<BoxCollider>();
        }

        // the materials, from the renderers: OnPostprocessMaterial isn't called for this importer's embedded
        // materials (a reimport with it came out byte for byte the same)
        int textured = 0;
        foreach (Material material in root.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m && !AssetDatabase.Contains(m)).Distinct())
            if (Surface(material)) textured++;
        Debug.Log("BlockoutPostprocessor: textured " + textured + " materials");

        // mist in the fountain: where its 24 rim jets land (8.3 m out, one every 15 degrees, as built in Blender)
        // and wherever anything wades through it. No assets set here: depending on other new assets from this import
        // looped (Unity gave up half way and the park vanished), so the component loads its material itself
        Transform water = root.transform.Find("WSP_Fountain/WSP_Fountain_Water");
        if (water)
        {
            WaterSplashes splashes = water.gameObject.AddComponent<WaterSplashes>();
            splashes.jets = 24;
            splashes.jetRadius = 8.3f;
        }
        else Debug.LogWarning("BlockoutPostprocessor: no WSP_Fountain/WSP_Fountain_Water, so no fountain splashes");

        Transform spray = root.transform.Find("WSP_Fountain/WSP_Fountain_Spray");
        if (spray && spray.TryGetComponent(out MeshFilter sprayShape)) SpoutUVs(sprayShape.sharedMesh, spray);
    }

    // The centre spout (a column up the middle that opens over the top into a sheet falling outward, round the vertical
    // axis) came with a flat front-on UV projection, which smears the running-water texture into streaks down its sides.
    // Re-laid round it: U round the spout (mirrored at the back, so no seam), V along the water's path: up the column
    // (y), over the cap and down the sheet (out and down). The river shader scrolls along a direction read from UV3
    // (mesh.uv4), which the .blend doesn't have: set to (0,-1) there, which moves the texture toward +V, downstream. Metres throughout,
    // matching the river material's tiling. The rim jets (farther out than SpoutReach) are laid along their arcs below.
    // ponytail: the path is a fixed formula for this spout's profile (column narrower than SpoutColumn, from y SpoutBase)
    const float SpoutReach = 2f, SpoutColumn = 0.33f;
    static void SpoutUVs(Mesh mesh, Transform part)
    {
        Vector3[] vertices = mesh.vertices;
        Vector2[] uvs = mesh.uv;
        if (uvs.Length != vertices.Length) uvs = new Vector2[vertices.Length];
        var flows = new Vector2[vertices.Length];
        Vector3 axis = part.TransformPoint(mesh.bounds.center);
        float bottom = float.MaxValue, top = float.MinValue, rim = 0;
        var world = new Vector3[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            world[i] = part.TransformPoint(vertices[i]);
            float r = Flat2(world[i] - axis);
            rim = Mathf.Max(rim, r);
            if (r > SpoutReach) continue;
            bottom = Mathf.Min(bottom, world[i].y);
            top = Mathf.Max(top, world[i].y);
        }
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 w = world[i] - axis;
            float r = Flat2(w);
            if (r > SpoutReach)
            {
                // a rim jet: from its nozzle at the rim, arcing inward to land about 8 m out. V inward from the rim,
                // U across the jet (arc length from the jet's own line, one every 15 degrees from +x, so no seam in one)
                // ponytail: V is horizontal distance only, so the water runs a little faster over the top of the arc
                float angle = Mathf.Atan2(w.z, w.x), step = Mathf.PI / 12;
                uvs[i] = new Vector2((angle - Mathf.Round(angle / step) * step) * r, (rim - r) * 1.3f);
            }
            else
            {
                float round = Mathf.Abs(Mathf.Atan2(w.z, w.x)) / Mathf.PI; // 0..1 front to back, the same both sides
                float along = r < SpoutColumn ? world[i].y - bottom // up the column
                    : (top - bottom) + (r - SpoutColumn) + (top - world[i].y); // then out and down the sheet
                uvs[i] = new Vector2(round * 2f, along);
            }
            flows[i] = new Vector2(0, -1); // the shader samples at uv + flow * t, so the picture moves along -flow
        }
        mesh.uv = uvs;
        mesh.uv4 = flows;
        mesh.RecalculateTangents();
    }

    static float Flat2(Vector3 v) => new Vector2(v.x, v.z).magnitude;

    // the blend's materials given the ambientCG sets beside it: a colour map (the set's own, or one tinted in Blender),
    // the set's normal and ambient occlusion maps, and the metres one repeat covers. The normal maps are the surface
    // relief that keeps brick, stone and grass from looking like flat paint (height maps, by parallax, were tried and
    // cost too many frames). The paving, pavements, road and fountain water use their own materials (the importer's
    // remaps), so aren't listed.
    // smoothness: with a set, the multiplier on its smoothness map (<set>_MetallicSmoothness.png: 1 - its roughness map,
    // made in Blender), picked so the average lands on a dry, weathered outdoor value (the comment), since ambientCG's
    // maps run glossy (grass 0.74, polished granite 0.97); without one, the value itself. metallic only for bare metal
    // (it takes constants: a map would override it). Too even a smoothness everywhere was the plastic look.
    const string Textures = "Assets/Blender/Textures/WashingtonSquareTextures/";
    internal static readonly Dictionary<string, (string color, string set, float metres, float smoothness, float metallic, float relief)> Surfaces =
        new Dictionary<string, (string, string, float, float, float, float)>
    {
        ["WSP_red_brick"] = ("WSP_brick_red_Color", "Bricks109A", 1.5f, 0.53f, 0, 0.035f),      // ~0.2; 3.5 cm: bricks well proud of the mortar
        ["WSP_brown_brick"] = ("WSP_brick_brown_Color", "Bricks109A", 1.5f, 0.53f, 0, 0.035f),
        ["WSP_buff_brick"] = ("WSP_brick_buff_Color", "Bricks109A", 1.5f, 0.53f, 0, 0.035f),
        ["WSP_tan_brick"] = ("WSP_brick_tan_Color", "Bricks109A", 1.5f, 0.53f, 0, 0.035f),
        ["WSP_white_brick"] = ("WSP_brick_white_Color", "Bricks109A", 1.5f, 0.53f, 0, 0.035f),
        ["WSP_granite"] = (null, "Granite003A", 2f, 0.31f, 0, 0.004f),  // ~0.3: flamed park granite, not the set's polished countertop; its flat height map, stretched, pits like flamed stone
        ["WSP_marble"] = (null, "Marble012", 2f, 0.48f, 0, 0.008f),                           // ~0.45: weathered, not polished
        ["WSP_limestone"] = ("WSP_limestone_Color", "Concrete034", 2f, 0.42f, 0, 0.006f),     // ~0.2
        ["WSP_sandstone"] = ("WSP_sandstone_Color", "Concrete034", 2f, 0.42f, 0, 0.006f),     // ~0.2
        ["WSP_slate"] = ("WSP_slate_Color", "Concrete034", 2f, 0.62f, 0, 0.006f),             // ~0.3
        ["WSP_concrete"] = (null, "Concrete034", 2f, 0.42f, 0, 0.006f),                       // ~0.2
        ["WSP_cobble"] = (null, "PavingStones046", 2f, 0.6f, 0, 0.025f),                      // ~0.25
        ["WSP_gravel"] = (null, "Gravel023", 2f, 0.28f, 0, 0.03f),                           // ~0.15
        ["WSP_sand"] = (null, "Ground054", 2f, 0.3f, 0, 0.015f),                              // ~0.1
        ["WSP_grass"] = (null, "Grass004", 2f, 0.34f, 0, 0.03f),                             // ~0.25
        ["WSP_turf"] = ("WSP_turf_Color", "Grass004", 2f, 0.4f, 0, 0.02f),                   // ~0.3: artificial
        ["WSP_planting"] = (null, "Ground078", 2f, 0.28f, 0, 0.025f),                         // ~0.12: soil
        ["WSP_wood"] = ("WSP_bench_wood_Color", "Wood051", 1.5f, 0.54f, 0, 0.003f),           // ~0.25: weathered slats
        ["WSP_iron"] = ("WSP_iron_Color", "PaintedMetal004", 1f, 0.47f, 0, 0),           // ~0.4: painted, so not metal
        ["WSP_dark"] = ("WSP_dark_Color", "PaintedMetal004", 1f, 0.47f, 0, 0),
        ["WSP_bronze"] = ("WSP_bronze_Color", "PaintedMetal004", 1f, 0.35f, 0.7f, 0),    // patinated bronze
        ["WSP_roof"] = (null, "RoofingTiles013A", 2f, 0.6f, 0, 0.02f),                       // ~0.3
        ["WSP_play_surface"] = ("WSP_rubber_surface_Color", null, 2f, 0.1f, 0, 0),       // rubber
        ["WSP_play_equipment"] = ("WSP_play_yellow_Color", null, 1f, 0.45f, 0, 0),       // powder-coated steel
        ["WSP_play_accent"] = ("WSP_play_green_Color", null, 1f, 0.45f, 0, 0),
    };

    // relief: metres of depth by parallax occlusion (Shitshow/Parallax Lit, from the set's <set>_Displacement.jpg):
    // the pebbles, mortar lines and grass get real-looking depth on flat meshes, for a few samples per pixel on screen.
    // 0 where it's under a millimetre (painted metal, plastic, rubber): all cost, nothing seen.
    // Each set's height map black and white points (2nd/98th percentiles, measured in Blender) stretch it to the full depth.
    static readonly Dictionary<string, (float black, float white)> HeightRange = new Dictionary<string, (float, float)>
    {
        ["Bricks109A"] = (0.16f, 0.73f), ["Granite003A"] = (0.49f, 0.51f), ["Concrete034"] = (0.39f, 0.85f), ["Grass004"] = (0.15f, 0.62f), ["Gravel023"] = (0.15f, 0.74f),
        ["Ground054"] = (0.40f, 0.85f), ["Ground078"] = (0.24f, 0.68f), ["Marble012"] = (0.64f, 0.98f), ["PavingStones046"] = (0.53f, 0.82f),
        ["RoofingTiles013A"] = (0.27f, 0.91f), ["Wood051"] = (0.32f, 0.65f),
    };
    const string ParallaxShader = "Assets/Parallax/ParallaxLit.shader";

    // Weathering on every listed material: URP Lit's detail map as a large, slightly warm grime layer (x2 detail: 0.5 = no
    // change, darker = dust and soot), from ambientCG's SurfaceImperfections013 stains (made in Blender). Tiled at
    // WeatheringMetres, out of step with the surface textures, so it also hides their repeats.
    const float WeatheringMetres = 7f;

    bool Surface(Material material)
    {
        if (!Surfaces.TryGetValue(material.name, out var surface)) return false;
        string set = surface.set == null ? null : surface.set + "_2K-JPG_";
        Texture2D color = Load(surface.color ?? set + "Color");
        if (set != null)
        {
            material.SetTexture("_BumpMap", Load(set + "NormalGL"));
            material.SetFloat("_BumpScale", 1f);
            material.EnableKeyword("_NORMALMAP");
            Texture2D occlusion = Load(set + "AmbientOcclusion"); // not every set has one
            if (occlusion)
            {
                material.SetTexture("_OcclusionMap", occlusion);
                material.EnableKeyword("_OCCLUSIONMAP");
            }
        }
        if (color)
        {
            material.SetTexture("_BaseMap", color);
            material.SetColor("_BaseColor", Color.white); // the texture carries the colour
            material.SetTextureScale("_BaseMap", Vector2.one * PavingTile / surface.metres); // tiles every map (they share _BaseMap_ST)
        }

        Texture2D smoothness = set != null && surface.metallic == 0 ? Load(set + "MetallicSmoothness", ".png") : null;
        material.SetTexture("_MetallicGlossMap", smoothness);
        if (smoothness) material.EnableKeyword("_METALLICSPECGLOSSMAP");
        else material.DisableKeyword("_METALLICSPECGLOSSMAP");
        material.SetFloat("_Smoothness", surface.smoothness);
        material.SetFloat("_Glossiness", surface.smoothness); // URP's MaterialPostprocessor copies this over _Smoothness after import
        material.SetFloat("_Metallic", surface.metallic);
        material.SetFloat("_SmoothnessTextureChannel", 0); // from the metallic map's alpha, not the colour map's

        Texture2D heights;
        if (surface.relief > 0 && HeightRange.TryGetValue(surface.set ?? "", out var range) && (heights = Load(set + "Displacement")))
        {
            // Lit's own Height Map slot, but Parallax Lit, whose parallax steps through it (Lit's is a single offset)
            context.DependsOnArtifact(ParallaxShader);
            Shader parallax = AssetDatabase.LoadAssetAtPath<Shader>(ParallaxShader);
            if (parallax) material.shader = parallax; // still Lit (one-step parallax) if the shader isn't there yet
            material.SetTexture("_ParallaxMap", heights);
            material.SetFloat("_Parallax", surface.relief / surface.metres); // depth in texture repeats
            material.SetFloat("_ParallaxMin", range.black);
            material.SetFloat("_ParallaxMax", range.white);
            material.EnableKeyword("_PARALLAXMAP");
        }

        material.SetTexture("_DetailAlbedoMap", Load("WSP_Weathering_DetailX2", ".png"));
        material.SetTextureScale("_DetailAlbedoMap", Vector2.one * PavingTile / WeatheringMetres); // the UVs are PavingTile metres
        material.SetFloat("_DetailAlbedoMapScale", 1f);
        material.EnableKeyword("_DETAIL_MULX2");
        return true;
    }

    // the sets' data maps are numbers, not colours: normal maps as normal maps, the rest linear (sRGB would skew them)
    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith("Assets/Blender/Textures/")) return;
        var importer = (TextureImporter)assetImporter;
        if (assetPath.Contains("_NormalGL")) importer.textureType = TextureImporterType.NormalMap;
        else if (assetPath.Contains("_Roughness") || assetPath.Contains("_AmbientOcclusion") || assetPath.Contains("_Displacement")
            || assetPath.Contains("_MetallicSmoothness") || assetPath.Contains("_DetailX2")) importer.sRGBTexture = false; // data, not colour (the x2 detail map's 0.5 must stay 0.5)
    }

    // null when the file isn't there (a set without an ambient occlusion map)
    Texture2D Load(string name, string extension = ".jpg")
    {
        string path = Textures + name + extension;
        if (!System.IO.File.Exists(path)) return null;
        context.DependsOnArtifact(path); // reimport (with it loaded) when the texture changes
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    struct Corner
    {
        public Vector3 position, normal; // position in world space while cutting
        public Vector4 tangent;
        public Vector2 uv, uv2;

        public static Corner Lerp(Corner a, Corner b, float t) => new Corner
        {
            position = Vector3.Lerp(a.position, b.position, t), normal = Vector3.Lerp(a.normal, b.normal, t),
            tangent = Vector4.Lerp(a.tangent, b.tangent, t), uv = Vector2.Lerp(a.uv, b.uv, t), uv2 = Vector2.Lerp(a.uv2, b.uv2, t),
        };
    }

    // Cuts every triangle along the same world grid of vertical planes (x and z every GridCell metres), the same points
    // on both triangles sharing an edge (no T-junctions). Each triangle becomes a few convex pieces per grid cell,
    // fanned back into triangles.
    // Box projection chosen per triangle, by its face normal. (It used to be per vertex, by the vertex normal: on
    // smooth-shaded curves, like the arch's soffit, one triangle's corners could pick different sides and the texture
    // smeared across it.) A vertex shared by triangles projected from different sides is split into one per side.
    static void BoxUVs(Mesh mesh, Transform part, bool flat)
    {
        Vector3[] vertices = mesh.vertices, normals = mesh.normals;
        Vector2[] uv2s = mesh.uv2;
        Vector3[] world = vertices.Select(part.TransformPoint).ToArray();
        var positions = new List<Vector3>();
        var newNormals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var newUv2s = new List<Vector2>();
        var split = new Dictionary<(int vertex, int side), int>();
        var submeshes = new int[mesh.subMeshCount][];
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            int[] triangles = mesh.GetTriangles(s);
            for (int t = 0; t < triangles.Length; t += 3)
            {
                Vector3 n = Vector3.Cross(world[triangles[t + 1]] - world[triangles[t]], world[triangles[t + 2]] - world[triangles[t]]);
                int side = Mathf.Abs(n.y) > (flat ? 0.5f * n.magnitude : Mathf.Max(Mathf.Abs(n.x), Mathf.Abs(n.z))) ? 1 : Mathf.Abs(n.x) > Mathf.Abs(n.z) ? 0 : 2;
                for (int k = t; k < t + 3; k++)
                {
                    int i = triangles[k];
                    if (!split.TryGetValue((i, side), out int index))
                    {
                        index = split[(i, side)] = positions.Count;
                        Vector3 w = world[i];
                        positions.Add(vertices[i]);
                        newNormals.Add(normals.Length > 0 ? normals[i] : Vector3.up);
                        uvs.Add((side == 1 ? new Vector2(w.x, w.z) : side == 0 ? new Vector2(w.z, w.y) : new Vector2(w.x, w.y)) / PavingTile);
                        if (uv2s.Length > 0) newUv2s.Add(uv2s[i]);
                    }
                    triangles[k] = index;
                }
            }
            submeshes[s] = triangles;
        }

        int count = mesh.subMeshCount;
        mesh.Clear();
        if (positions.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(positions);
        mesh.SetNormals(newNormals);
        mesh.SetUVs(0, uvs);
        if (newUv2s.Count > 0) mesh.SetUVs(1, newUv2s);
        mesh.subMeshCount = count;
        for (int s = 0; s < count; s++) mesh.SetTriangles(submeshes[s], s);
        mesh.RecalculateTangents(); // the normal maps go by the new UVs
        mesh.RecalculateBounds();
    }

    static void GridCut(Mesh mesh, Transform part)
    {
        // never clear a mesh that can't be written back (it would vanish)
        if (!mesh.isReadable) { Debug.LogWarning("BlockoutPostprocessor: " + part.name + " isn't readable, so it isn't cut into a grid"); return; }
        Vector3[] positions = mesh.vertices, normals = mesh.normals;
        Vector4[] tangents = mesh.tangents;
        Vector2[] uvs = mesh.uv, uv2s = mesh.uv2;
        Corner At(int i) => new Corner
        {
            position = part.TransformPoint(positions[i]), normal = normals.Length > 0 ? normals[i] : Vector3.up,
            tangent = tangents.Length > 0 ? tangents[i] : Vector4.zero, uv = uvs.Length > 0 ? uvs[i] : Vector2.zero, uv2 = uv2s.Length > 0 ? uv2s[i] : Vector2.zero,
        };

        var corners = new List<Corner>();
        var submeshes = new List<int>[mesh.subMeshCount];
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            submeshes[s] = new List<int>();
            int[] triangles = mesh.GetTriangles(s);
            for (int t = 0; t < triangles.Length; t += 3)
                foreach (List<Corner> strip in Slice(new List<Corner> { At(triangles[t]), At(triangles[t + 1]), At(triangles[t + 2]) }, 0))
                    foreach (List<Corner> piece in Slice(strip, 2))
                    {
                        int first = corners.Count;
                        corners.AddRange(piece);
                        for (int i = 1; i + 1 < piece.Count; i++) submeshes[s].AddRange(new[] { first, first + i, first + i + 1 });
                    }
        }

        mesh.Clear();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = corners.Select(c => part.InverseTransformPoint(c.position)).ToArray();
        mesh.normals = corners.Select(c => c.normal.normalized).ToArray();
        mesh.uv = corners.Select(c => c.uv).ToArray();
        if (uv2s.Length > 0) mesh.uv2 = corners.Select(c => c.uv2).ToArray(); // lightmap UVs
        mesh.subMeshCount = submeshes.Length;
        for (int s = 0; s < submeshes.Length; s++) mesh.SetTriangles(submeshes[s], s);
        mesh.RecalculateTangents(); // to match the UVs (the projected ones above aren't what Blender's tangents went by)
        mesh.RecalculateBounds();
    }

    // a convex polygon cut by the grid planes across one axis (0 = x, 2 = z), lowest piece first
    static IEnumerable<List<Corner>> Slice(List<Corner> polygon, int axis)
    {
        float low = polygon.Min(c => c.position[axis]), high = polygon.Max(c => c.position[axis]);
        for (float line = Mathf.Floor(low / GridCell + 1) * GridCell; line < high - 1e-4f; line += GridCell)
        {
            var below = new List<Corner>();
            var above = new List<Corner>();
            for (int i = 0; i < polygon.Count; i++)
            {
                Corner a = polygon[i], b = polygon[(i + 1) % polygon.Count];
                float da = a.position[axis] - line, db = b.position[axis] - line;
                if (da <= 1e-5f) below.Add(a);
                if (da >= -1e-5f) above.Add(a);
                if ((da < -1e-5f && db > 1e-5f) || (da > 1e-5f && db < -1e-5f)) // the edge crosses the line
                {
                    Corner cut = Corner.Lerp(a, b, da / (da - db));
                    below.Add(cut);
                    above.Add(cut);
                }
            }
            if (below.Count >= 3) yield return below;
            polygon = above;
            if (polygon.Count < 3) yield break;
        }
        yield return polygon;
    }
}
