using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using static JamzeySt.Carve.Editor.CarveConverter;

namespace JamzeySt.Carve.Editor.URP
{
    // URP Lit <-> Carve Lit. Maps and colors move to Carve's names; URP's metallic/smoothness and occlusion maps are
    // packed into one mask map (R metallic, G occlusion, A smoothness); URP's parallax height and amount become a real
    // relief depth measured on the meshes using the material. Anything with no Carve equivalent is listed.
    static class CarveConverterURP
    {
        const string Lit = "Universal Render Pipeline/Lit", CarveLit = "Carve/URP/Carve Lit";

        [InitializeOnLoadMethod]
        static void Register()
        {
            AddRule(FromLit);
            AddBackRule(BackToLit);
        }

        static Texture Tex(Material m, string prop) => m.HasProperty(prop) ? m.GetTexture(prop) : null;

        static CarveConversion FromLit(Material m)
        {
            if (m.shader.name != Lit || Shader.Find(CarveLit) is not Shader target) return null;
            var c = new CarveConversion { source = m, from = "URP Lit", to = "Carve Lit" };
            var baseMap = Tex(m, "_BaseMap");
            var normal = Tex(m, "_BumpMap");
            var metalMap = Tex(m, "_MetallicGlossMap");
            var occlusion = Tex(m, "_OcclusionMap");
            var height = Tex(m, "_ParallaxMap");
            Vector2 tiling = m.HasProperty("_BaseMap") ? m.GetTextureScale("_BaseMap") : Vector2.one;
            Vector2 offset = m.HasProperty("_BaseMap") ? m.GetTextureOffset("_BaseMap") : Vector2.zero;
            Color color = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white;
            float bump = F(m, "_BumpScale", 1f), metal = F(m, "_Metallic", 0f), smooth = F(m, "_Smoothness", 0.5f);
            float occStrength = F(m, "_OcclusionStrength", 1f);
            bool smoothFromAlbedo = I(m, "_SmoothnessTextureChannel") == 1;

            // URP's parallax offsets the tiled UV by up to _Parallax: real depth = that times the meters one tiled UV unit spans.
            float mpu = MetersPerUV(m, out int users) / Mathf.Max((Mathf.Abs(tiling.x) + Mathf.Abs(tiling.y)) * 0.5f, 1e-4f);
            float depth = height != null ? Mathf.Clamp(F(m, "_Parallax", 0.005f) * mpu, 0.002f, 0.5f) : 0.03f;
            string where = users > 0 ? " on the meshes in the open scenes" : ", not in open scenes: 1 m per UV unit assumed";

            c.carried.Add("maps, colors, tiling");
            if (metalMap != null || occlusion != null) c.carried.Add("metallic, smoothness and occlusion packed into a mask map");
            c.carried.Add(height != null ? $"parallax {F(m, "_Parallax", 0.005f):0.###} ({depth * 100f:0.#} cm real{where})" : "relief 3 cm (no height map)");
            if (I(m, "_WorkflowMode", 1) == 0) c.lost.Add("specular workflow (converted as metallic 0)");
            if (I(m, "_Surface") == 1) c.lost.Add("transparency (Carve is opaque)");
            if (I(m, "_AlphaClip") == 1) c.lost.Add("alpha cutout (Carve uses alpha clipping for silhouettes)");
            if (Tex(m, "_EmissionMap") != null || (m.IsKeywordEnabled("_EMISSION") && m.HasProperty("_EmissionColor") && m.GetColor("_EmissionColor").maxColorComponent > 0.001f)) c.lost.Add("emission");
            if (Tex(m, "_DetailAlbedoMap") != null || Tex(m, "_DetailNormalMap") != null) c.lost.Add("detail maps (use Carve's Detail Relief)");
            if (I(m, "_Cull", 2) != 2) c.lost.Add("render face (Carve renders front faces; set Render Face after converting)");

            c.apply = t =>
            {
                Texture mask = metalMap != null || occlusion != null ? PackMask(m, metalMap, occlusion, smoothFromAlbedo ? baseMap : null, occStrength) : null;
                t.shader = target;
                t.SetTexture("_BaseColorMap", baseMap);
                t.SetTextureScale("_BaseColorMap", tiling);
                t.SetTextureOffset("_BaseColorMap", offset);
                t.SetColor("_BaseColor", color);
                t.SetTexture("_NormalMap", normal);
                t.SetFloat("_NormalScale", bump);
                t.SetTexture("_MaskMap", mask);
                // With a metallic map URP takes metallic from it alone; the mask holds it, so the slider passes it through.
                t.SetFloat("_Metallic", metalMap != null ? 1f : (I(m, "_WorkflowMode", 1) == 0 ? 0f : metal));
                t.SetFloat("_Smoothness", smooth);
                t.SetTexture("_HeightMap", height);
                t.SetFloat("_CarveDepth", depth);
                CarveLayers.SyncKeyword(t);
            };
            return c;
        }

        static CarveConversion BackToLit(Material m)
        {
            if (m.shader.name != CarveLit || Shader.Find(Lit) is not Shader target) return null;
            var c = new CarveConversion { source = m, from = "Carve Lit", to = "URP Lit" };
            var baseMap = Tex(m, "_BaseColorMap");
            var normal = Tex(m, "_NormalMap");
            var mask = Tex(m, "_MaskMap");
            var height = Tex(m, "_HeightMap");
            Vector2 tiling = m.GetTextureScale("_BaseColorMap"), offset = m.GetTextureOffset("_BaseColorMap");
            Color color = m.GetColor("_BaseColor");
            float bump = F(m, "_NormalScale", 1f), metal = F(m, "_Metallic", 0f), smooth = F(m, "_Smoothness", 0.5f), depth = F(m, "_CarveDepth", 0.05f);
            float mpu = MetersPerUV(m, out _) / Mathf.Max((Mathf.Abs(tiling.x) + Mathf.Abs(tiling.y)) * 0.5f, 1e-4f);
            c.carried.Add("maps, colors, tiling, mask (as metallic and occlusion maps), height as parallax");
            if (I(m, "_CarveMapping") > 0) c.lost.Add("triplanar mapping (URP Lit uses UV0)");
            c.lost.Add("silhouettes, real depth, self-shadowing, relief occlusion and the other Carve features");
            c.apply = t =>
            {
                t.shader = target;
                t.SetTexture("_BaseMap", baseMap);
                t.SetTextureScale("_BaseMap", tiling);
                t.SetTextureOffset("_BaseMap", offset);
                t.SetColor("_BaseColor", color);
                t.SetTexture("_BumpMap", normal);
                t.SetFloat("_BumpScale", bump);
                // Carve's mask is R metallic, G occlusion, A smoothness: URP reads the same channels from these two maps.
                t.SetTexture("_MetallicGlossMap", mask);
                t.SetTexture("_OcclusionMap", mask);
                t.SetFloat("_Metallic", metal);
                t.SetFloat("_Smoothness", smooth);
                t.SetTexture("_ParallaxMap", height);
                t.SetFloat("_Parallax", Mathf.Clamp(depth / Mathf.Max(mpu, 1e-4f), 0.005f, 0.08f));
                SetKeyword(t, "_NORMALMAP", normal != null);
                SetKeyword(t, "_METALLICSPECGLOSSMAP", mask != null);
                SetKeyword(t, "_OCCLUSIONMAP", mask != null);
                SetKeyword(t, "_PARALLAXMAP", height != null);
            };
            return c;
        }

        static void SetKeyword(Material m, string k, bool on)
        {
            if (on) m.EnableKeyword(k);
            else m.DisableKeyword(k);
        }

        // R metallic and A smoothness from the metallic map (or the albedo's alpha), G occlusion; written next to the source
        // material as a linear PNG.
        static Texture2D PackMask(Material m, Texture metal, Texture occlusion, Texture smoothFromAlbedo, float occStrength)
        {
            int w = 4, h = 4;
            foreach (var t in new[] { metal, occlusion })
                if (t != null) { w = Mathf.Max(w, t.width); h = Mathf.Max(h, t.height); }
            Color[] Read(Texture t, Color fill)
            {
                if (t == null) { var c = new Color[w * h]; for (int i = 0; i < c.Length; i++) c[i] = fill; return c; }
                var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                Graphics.Blit(t, rt);
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                var px = tex.GetPixels();
                Object.DestroyImmediate(tex);
                return px;
            }
            var mPx = Read(metal, Color.white);
            var oPx = Read(occlusion, Color.white);
            var aPx = smoothFromAlbedo != null ? Read(smoothFromAlbedo, Color.white) : null;
            var outPx = new Color[w * h];
            for (int i = 0; i < outPx.Length; i++)
                outPx[i] = new Color(metal != null ? mPx[i].r : 1f, Mathf.Lerp(1f, oPx[i].g, occStrength), 0f, aPx != null ? aPx[i].a : (metal != null ? mPx[i].a : 1f));
            var mask = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            mask.SetPixels(outPx);
            mask.Apply();
            string dir = Path.GetDirectoryName(AssetDatabase.GetAssetPath(m)).Replace('\\', '/');
            string path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{m.name}_CarveMask.png");
            File.WriteAllBytes(path, mask.EncodeToPNG());
            Object.DestroyImmediate(mask);
            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter ti)
            {
                ti.sRGBTexture = false;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // How many meters one UV0 unit covers on the meshes using m in the open scenes (median); 1 if unused.
        static float MetersPerUV(Material m, out int users)
        {
            var ratios = new List<float>();
            foreach (var r in CarveFind.All<Renderer>())
            {
                int sub = System.Array.IndexOf(r.sharedMaterials, m);
                if (sub < 0) continue;
                Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null || !mesh.isReadable || sub >= mesh.subMeshCount) continue;
                var v = mesh.vertices;
                var uv = mesh.uv;
                if (uv.Length != v.Length) continue;
                var l2w = r.transform.localToWorldMatrix;
                var tri = mesh.GetTriangles(sub);
                double world = 0, uvArea = 0;
                for (int i = 0; i < tri.Length; i += 3)
                {
                    Vector3 a = l2w.MultiplyPoint3x4(v[tri[i]]), b = l2w.MultiplyPoint3x4(v[tri[i + 1]]), c = l2w.MultiplyPoint3x4(v[tri[i + 2]]);
                    Vector2 ua = uv[tri[i]], ub = uv[tri[i + 1]], uc = uv[tri[i + 2]];
                    world += Vector3.Cross(b - a, c - a).magnitude;
                    uvArea += Mathf.Abs((ub.x - ua.x) * (uc.y - ua.y) - (uc.x - ua.x) * (ub.y - ua.y));
                }
                if (uvArea > 1e-9) ratios.Add((float)System.Math.Sqrt(world / uvArea));
            }
            users = ratios.Count;
            if (users == 0) return 1f;
            ratios.Sort();
            return ratios[users / 2];
        }
    }
}
