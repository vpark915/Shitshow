using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace JamzeySt.Carve.Editor
{
    public enum CarveCheckLevel { Ok, Info, Warning, Error }

    public struct CarveCheck
    {
        public CarveCheckLevel level;
        public string title;
        public string detail;
        public string fixLabel;
        public Action fix;

        public static CarveCheck Ok(string title, string detail = "") => new CarveCheck { level = CarveCheckLevel.Ok, title = title, detail = detail };
    }

    // One height map of a material with the maps it is generated from and its pyramid (Carve Lit: one; Layered: one per layer).
    public struct CarveHeightSlot
    {
        public string label, height, pyramid, albedo, normal;
        public int layer;
    }

    // Project/scene queries shared by the material inspector and the Carve Hub.
    public static class CarveProject
    {
        public const string ShaderPrefix = "Carve/";
        static readonly List<Func<CarveCheck>> s_Checks = new List<Func<CarveCheck>>();
        static double s_ScanTime = -1;
        static List<Renderer> s_SceneRenderers = new List<Renderer>();

        public static IReadOnlyList<Func<CarveCheck>> Checks => s_Checks;

        public static void AddCheck(Func<CarveCheck> check)
        {
            if (!s_Checks.Contains(check)) s_Checks.Add(check);
        }

        public static string PipelineName
        {
            get
            {
                var rp = GraphicsSettings.currentRenderPipeline;
                if (rp == null) return "Built-in";
                string n = rp.GetType().Name;
                if (n.Contains("HDRenderPipeline")) return "HDRP";
                if (n.Contains("Universal")) return "URP";
                return n;
            }
        }

        // Carve materials of the active pipeline (the package ships HDRP and URP shaders side by side).
        public static bool IsCarve(Material m)
        {
            if (m == null || m.shader == null || !m.shader.name.StartsWith(ShaderPrefix)) return false;
            string other = PipelineName == "URP" ? ShaderPrefix + "HDRP/" : PipelineName == "HDRP" ? ShaderPrefix + "URP/" : null;
            return other == null || !m.shader.name.StartsWith(other);
        }

        public static bool IsLayered(Material m) => IsCarve(m) && m.HasProperty("_LayerCount");

        public static int LayerCount(Material m) => IsLayered(m) ? Mathf.Clamp(Mathf.RoundToInt(m.GetFloat("_LayerCount")), 2, 4) : 1;

        public static List<CarveHeightSlot> HeightSlots(Material m)
        {
            var list = new List<CarveHeightSlot>();
            if (!IsCarve(m)) return list;
            if (!IsLayered(m))
            {
                if (m.HasProperty("_HeightMap"))
                    list.Add(new CarveHeightSlot { label = "Height Map", height = "_HeightMap", pyramid = "_CarvePyramid", albedo = "_BaseColorMap", normal = "_NormalMap", layer = -1 });
                return list;
            }
            for (int i = 0; i < LayerCount(m); i++)
                list.Add(new CarveHeightSlot { label = $"Layer {i} height", height = "_HeightMap" + i, pyramid = "_CarvePyramid" + i, albedo = "_BaseColorMap" + i, normal = "_NormalMap" + i, layer = i });
            return list;
        }

        public static List<Material> ProjectMaterials()
        {
            var list = new List<Material>();
            foreach (var guid in AssetDatabase.FindAssets("t:Material"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/")) continue;
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (IsCarve(m)) list.Add(m);
            }
            list.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        // Renderers in the open scenes that use at least one Carve material (cached for a second).
        public static List<Renderer> SceneRenderers()
        {
            if (EditorApplication.timeSinceStartup - s_ScanTime < 1.0) return s_SceneRenderers;
            s_ScanTime = EditorApplication.timeSinceStartup;
            s_SceneRenderers = new List<Renderer>();
            foreach (var r in CarveFind.All<Renderer>())
            {
                foreach (var m in r.sharedMaterials)
                    if (IsCarve(m)) { s_SceneRenderers.Add(r); break; }
            }
            return s_SceneRenderers;
        }

        public static void InvalidateScan() => s_ScanTime = -1;

        public static bool NeedsBakedData(Material m) =>
            IsCarve(m) && ((m.HasProperty("_CarveSilhouette") && Mathf.RoundToInt(m.GetFloat("_CarveSilhouette")) == 2) ||
                           (m.HasProperty("_CarveCurvature") && Mathf.RoundToInt(m.GetFloat("_CarveCurvature")) == 2));

        public static bool HasBakedData(Mesh mesh) =>
            mesh != null && mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord2) && mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord3);

        // Mesh filters whose material asks for baked silhouette data but whose mesh has none; optionally only those using `only`.
        public static List<MeshFilter> UnbakedFilters(ICollection<Material> only = null)
        {
            var list = new List<MeshFilter>();
            foreach (var r in SceneRenderers())
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || HasBakedData(mf.sharedMesh)) continue;
                foreach (var m in r.sharedMaterials)
                {
                    if (!NeedsBakedData(m) || (only != null && !only.Contains(m))) continue;
                    list.Add(mf);
                    break;
                }
            }
            return list;
        }

        public static int BakeFilters(IEnumerable<MeshFilter> filters)
        {
            var done = new Dictionary<Mesh, Mesh>();
            foreach (var mf in filters)
            {
                if (mf == null || mf.sharedMesh == null) continue;
                if (!done.TryGetValue(mf.sharedMesh, out var baked))
                {
                    baked = CarveMeshBaker.BakeToAsset(mf.sharedMesh);
                    done[mf.sharedMesh] = baked;
                }
                Undo.RecordObject(mf, "Bake Carve Silhouette Data");
                mf.sharedMesh = baked;
            }
            InvalidateScan();
            return done.Count;
        }

        [InitializeOnLoadMethod]
        static void RegisterCommonChecks()
        {
            AddCheck(() =>
            {
                string rp = PipelineName;
                return rp == "HDRP" || rp == "URP"
                    ? CarveCheck.Ok($"Render pipeline: {rp}")
                    : new CarveCheck { level = CarveCheckLevel.Error, title = $"Render pipeline: {rp}", detail = "Carve needs HDRP 17+ or URP 17+ (Unity 6.0 LTS or newer)." };
            });
            AddCheck(() =>
            {
                var sun = CarveSun.Current;
                return sun != null
                    ? CarveCheck.Ok("Sun found for self-shadowing", sun.name)
                    : new CarveCheck { level = CarveCheckLevel.Warning, title = "No directional light found", detail = "Self-shadowing needs a sun. Add a directional light or assign Lighting > Sun Source." };
            });
            AddCheck(() =>
            {
                var stale = ProjectMaterials().Where(m => !CarveLayers.IsKeywordInSync(m)).ToList();
                return stale.Count == 0
                    ? CarveCheck.Ok("Layered materials compiled for their layer count", "Only the layers each material uses are compiled in.")
                    : new CarveCheck { level = CarveCheckLevel.Warning, title = $"{stale.Count} layered material(s) not compiled for their layer count", detail = "They still read the layer count at runtime, which costs up to a third more GPU time.", fixLabel = "Fix", fix = () =>
                    {
                        Undo.RecordObjects(stale.ToArray(), "Compile Carve Layer Count");
                        foreach (var m in stale) { CarveLayers.SyncKeyword(m); EditorUtility.SetDirty(m); }
                    } };
            });
            AddCheck(() =>
            {
                var unbaked = UnbakedFilters();
                return unbaked.Count == 0
                    ? CarveCheck.Ok("Silhouette data baked", "Every mesh using Baked silhouettes or curvature has its data.")
                    : new CarveCheck { level = CarveCheckLevel.Warning, title = $"{unbaked.Count} mesh(es) need silhouette data", detail = "Their materials use Baked silhouettes or curvature, but the meshes were never baked.", fixLabel = "Bake now", fix = () => BakeFilters(unbaked) };
            });
            AddCheck(() =>
            {
                int missing = 0;
                var canGenerate = new List<Material>();
                var seen = new HashSet<Material>();
                foreach (var r in SceneRenderers())
                    foreach (var m in r.sharedMaterials)
                    {
                        if (!IsCarve(m) || !seen.Add(m)) continue;
                        // A layer without a height map is a valid flat layer; only flag it when one can be generated.
                        foreach (var slot in HeightSlots(m))
                        {
                            if (m.GetTexture(slot.height) != null) continue;
                            bool can = CarveHeightGenerator.CanGenerate(m, slot);
                            if (slot.layer >= 0 && !can) continue;
                            missing++;
                            if (can && !canGenerate.Contains(m)) canGenerate.Add(m);
                        }
                    }
                if (missing == 0) return CarveCheck.Ok("Height maps assigned");
                var check = new CarveCheck { level = CarveCheckLevel.Warning, title = $"{missing} Carve material slot(s) without a height map", detail = "Without a height map the surface stays flat." };
                if (canGenerate.Count > 0)
                {
                    check.detail += $" {canGenerate.Count} can be generated from their normal maps.";
                    check.fixLabel = "Generate";
                    check.fix = () => CarveHeightGenerator.GenerateFor(canGenerate.ToArray());
                }
                return check;
            });
        }
    }
}
