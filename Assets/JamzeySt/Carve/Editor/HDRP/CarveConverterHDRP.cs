using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using static JamzeySt.Carve.Editor.CarveConverter;

namespace JamzeySt.Carve.Editor.HDRP
{
    // HDRP/Lit <-> Carve Lit and HDRP/LayeredLit <-> Carve Layered Lit. Carve uses Lit's property names, so textures,
    // colors and tiling carry over with the shader switch; this maps the rest (POM depth and steps, smoothness and
    // metallic remaps, triplanar, layer heights and masks) and lists what has no Carve equivalent.
    static class CarveConverterHDRP
    {
        const string Lit = "HDRP/Lit", LayeredLit = "HDRP/LayeredLit";
        const string CarveLit = "Carve/HDRP/Carve Lit", CarveLayered = "Carve/HDRP/Carve Layered Lit";

        [InitializeOnLoadMethod]
        static void Register()
        {
            AddRule(FromLit);
            AddRule(FromLayeredLit);
            AddBackRule(BackToLit);
            AddBackRule(BackToLayeredLit);
        }

        static CarveConversion FromLit(Material m)
        {
            if (m.shader.name != Lit || Shader.Find(CarveLit) is not Shader target) return null;
            var c = new CarveConversion { source = m, from = "HDRP Lit", to = "Carve Lit" };
            bool mask = HasTex(m, "_MaskMap");
            float smooth = mask ? F(m, "_SmoothnessRemapMax", 1f) : F(m, "_Smoothness", 0.5f);
            float metal = mask ? F(m, "_MetallicRemapMax", 1f) : F(m, "_Metallic", 0f);
            var mapping = ReadMapping(m, "", c);
            float mpu = MetersPerUV(m, out int users);
            var pom = ReadDepth(m, "", mapping, mpu, users);
            if (pom.capped) c.lost.Add("relief deeper than 50 cm (capped)");
            c.carried.Add("maps, colors, tiling");
            c.carried.Add(pom.label);
            Lost(m, c, "");
            c.apply = t =>
            {
                t.shader = target;
                t.SetFloat("_Smoothness", smooth);
                t.SetFloat("_Metallic", metal);
                t.SetFloat("_CarveDepth", pom.depth);
                if (pom.steps.x > 0f)
                {
                    t.SetFloat("_CarveMinSteps", pom.steps.x);
                    t.SetFloat("_CarveMaxSteps", pom.steps.y);
                }
                ApplyMapping(t, mapping, "_BaseColorMap");
                CarveHDRPSetup.Fix(t);
            };
            return c;
        }

        static CarveConversion FromLayeredLit(Material m)
        {
            if (m.shader.name != LayeredLit || Shader.Find(CarveLayered) is not Shader target) return null;
            var c = new CarveConversion { source = m, from = "HDRP LayeredLit", to = "Carve Layered Lit" };
            int count = Mathf.Clamp(I(m, "_LayerCount", 2), 2, 4);
            var smooth = new float[4];
            var metal = new float[4];
            for (int i = 0; i < count; i++)
            {
                bool mask = HasTex(m, "_MaskMap" + i);
                smooth[i] = mask ? F(m, "_SmoothnessRemapMax" + i, 1f) : F(m, "_Smoothness" + i, 0.5f);
                metal[i] = mask ? F(m, "_MetallicRemapMax" + i, 1f) : F(m, "_Metallic" + i, 0f);
            }

            // LayeredLit layer height in meters: (h - center) * amplitude (POM carves down from the top: center 1).
            // Carve stores one depth and per-layer scale/offset in it, so normalize all layers into one range.
            bool pixel = I(m, "_DisplacementMode") == 2;
            var mapping = ReadMapping(m, "0", c);
            float mpu = MetersPerUV(m, out int users);
            float zMin = 0f, zMax = 0f;
            var amp = new float[4];
            var center = new float[4];
            for (int i = 0; i < count; i++)
            {
                if (!HasTex(m, "_HeightMap" + i)) continue;
                amp[i] = pixel ? F(m, "_HeightPoMAmplitude" + i, 2f) * 0.01f * PomToMeters(m, i.ToString(), mapping, mpu) : Mathf.Abs(F(m, "_HeightAmplitude" + i, 0.02f));
                center[i] = pixel ? 1f : F(m, "_HeightCenter" + i, 0.5f);
                zMin = Mathf.Min(zMin, -center[i] * amp[i]);
                zMax = Mathf.Max(zMax, (1f - center[i]) * amp[i]);
            }
            float depth = zMax - zMin;
            if (depth < 1e-4f) depth = 0.03f;
            if (depth > 0.5f)
            {
                for (int i = 0; i < 4; i++) amp[i] *= 0.5f / depth;
                zMin *= 0.5f / depth;
                depth = 0.5f;
                c.lost.Add("relief deeper than 50 cm (capped)");
            }
            var scale = new float[4];
            var offset = new float[4];
            for (int i = 0; i < 4; i++)
            {
                scale[i] = Mathf.Clamp01(amp[i] / depth);
                // A layer without a height map is flat at the reference level (z = 0).
                offset[i] = Mathf.Clamp((amp[i] > 0f ? -center[i] * amp[i] - zMin : -zMin) / depth, -1f, 1f);
            }

            int vertexMode = I(m, "_VertexColorMode");
            bool hasMask = HasTex(m, "_LayerMaskMap");
            int maskSource = vertexMode == 0 ? 0 : (hasMask ? 2 : 1);
            if (vertexMode == 2) c.lost.Add("vertex color Add/Subtract mode (uses multiply)");
            if (vertexMode == 0 && !hasMask) c.lost.Add("no layer mask: only layer 0 shows until one is painted");
            bool heightBlend = I(m, "_UseHeightBasedBlend") == 1;
            float softness = Mathf.Clamp(F(m, "_HeightTransition", 0.15f), 0.05f, 0.5f);
            if (I(m, "_UseMainLayerInfluence") == 1) c.lost.Add("main layer influence");
            c.carried.Add($"{count} layers: maps, colors, tiling, mask");
            c.carried.Add($"relief {depth * 100f:0.#} cm{DepthSource(pixel, mapping, mpu, users)}" + (heightBlend ? ", height blend" : ""));
            Lost(m, c, "0");

            c.apply = t =>
            {
                t.shader = target;
                CarveLayers.SetLayerCount(t, count);
                t.SetFloat("_CarveLayerMaskSource", maskSource);
                t.SetFloat("_CarveHeightBlend", heightBlend ? 1f : 0f);
                t.SetFloat("_CarveLayerBlendSoftness", softness);
                t.SetFloat("_CarveDepth", depth);
                for (int i = 0; i < count; i++)
                {
                    t.SetFloat("_Smoothness" + i, smooth[i]);
                    t.SetFloat("_Metallic" + i, metal[i]);
                    t.SetFloat("_CarveLayerHeight" + i, amp[i] > 0f ? scale[i] : 0f);
                    t.SetFloat("_CarveLayerOffset" + i, offset[i]);
                    ApplyMapping(t, mapping, "_BaseColorMap" + i);
                }
                CarveHDRPSetup.Fix(t);
            };
            return c;
        }

        static CarveConversion BackToLit(Material m)
        {
            if (m.shader.name != CarveLit || Shader.Find(Lit) is not Shader target) return null;
            var c = new CarveConversion { source = m, from = "Carve Lit", to = "HDRP Lit" };
            float depth = F(m, "_CarveDepth", 0.05f), smooth = F(m, "_Smoothness", 0.5f), metal = F(m, "_Metallic", 0f);
            float minSteps = F(m, "_CarveMinSteps", 8f), maxSteps = F(m, "_CarveMaxSteps", 32f);
            int mapping = I(m, "_CarveMapping");
            bool height = HasTex(m, "_HeightMap"), mask = HasTex(m, "_MaskMap");
            c.carried.Add("maps, colors, tiling, POM depth and steps");
            c.lost.Add("silhouettes, self-shadowing, relief occlusion and the other Carve features");
            c.apply = t =>
            {
                t.shader = target;
                if (mask)
                {
                    t.SetFloat("_SmoothnessRemapMin", 0f);
                    t.SetFloat("_SmoothnessRemapMax", smooth);
                    t.SetFloat("_MetallicRemapMin", 0f);
                    t.SetFloat("_MetallicRemapMax", metal);
                }
                BackMapping(t, mapping, "");
                BackPom(t, height, minSteps, maxSteps);
                t.SetFloat("_InvTilingScale", InvTiling(t, "", ReadMapping(t, "", null)));
                SetPomAmplitude(t, "", depth / PomToMeters(t, "", ReadMapping(t, "", null), MetersPerUV(t, out _)));
                HDMaterial.ValidateMaterial(t);
            };
            return c;
        }

        static CarveConversion BackToLayeredLit(Material m)
        {
            if (m.shader.name != CarveLayered || Shader.Find(LayeredLit) is not Shader target) return null;
            var c = new CarveConversion { source = m, from = "Carve Layered Lit", to = "HDRP LayeredLit" };
            int count = CarveProject.LayerCount(m);
            float depth = F(m, "_CarveDepth", 0.05f), minSteps = F(m, "_CarveMinSteps", 8f), maxSteps = F(m, "_CarveMaxSteps", 32f);
            int mapping = I(m, "_CarveMapping"), maskSource = I(m, "_CarveLayerMaskSource");
            float heightBlend = F(m, "_CarveHeightBlend", 1f), softness = F(m, "_CarveLayerBlendSoftness", 0.15f);
            var smooth = new float[4];
            var metal = new float[4];
            var mask = new bool[4];
            var height = new bool[4];
            var scale = new float[4];
            for (int i = 0; i < count; i++)
            {
                smooth[i] = F(m, "_Smoothness" + i, 0.5f);
                metal[i] = F(m, "_Metallic" + i, 0f);
                mask[i] = HasTex(m, "_MaskMap" + i);
                height[i] = HasTex(m, "_HeightMap" + i);
                scale[i] = F(m, "_CarveLayerHeight" + i, 1f);
            }
            c.carried.Add($"{count} layers: maps, colors, tiling, mask, POM depth");
            c.lost.Add("height offsets, silhouettes, self-shadowing and the other Carve features");
            c.apply = t =>
            {
                t.shader = target;
                CarveLayers.ClearKeyword(t);
                t.SetFloat("_LayerCount", count);
                t.SetFloat("_VertexColorMode", maskSource == 0 ? 0f : 1f);
                t.SetFloat("_UseHeightBasedBlend", heightBlend > 0.5f ? 1f : 0f);
                t.SetFloat("_HeightTransition", softness);
                for (int i = 0; i < count; i++)
                {
                    if (mask[i])
                    {
                        t.SetFloat("_SmoothnessRemapMin" + i, 0f);
                        t.SetFloat("_SmoothnessRemapMax" + i, smooth[i]);
                        t.SetFloat("_MetallicRemapMin" + i, 0f);
                        t.SetFloat("_MetallicRemapMax" + i, metal[i]);
                    }
                }
                BackMapping(t, mapping, "0");
                BackPom(t, System.Array.Exists(height, h => h), minSteps, maxSteps);
                var map = ReadMapping(t, "0", null);
                float mpu = MetersPerUV(t, out _);
                for (int i = 0; i < count; i++)
                {
                    t.SetFloat("_InvTilingScale" + i, InvTiling(t, i.ToString(), map));
                    SetPomAmplitude(t, i.ToString(), depth * scale[i] / PomToMeters(t, i.ToString(), map, mpu));
                }
                HDMaterial.ValidateMaterial(t);
            };
            return c;
        }

        struct Depth
        {
            public float depth;
            public Vector2 steps;
            public string label;
            public bool capped;
        }

        // Lit's relief: pixel displacement (POM) amplitude and samples, else the tessellation/vertex height range.
        static Depth ReadDepth(Material m, string suffix, Mapping map, float mpu, int users)
        {
            int mode = I(m, "_DisplacementMode");
            bool height = HasTex(m, "_HeightMap" + suffix);
            var d = new Depth { depth = 0.03f };
            if (mode == 2)
            {
                float amp = F(m, "_HeightPoMAmplitude" + suffix, 2f);
                d.depth = amp * 0.01f * PomToMeters(m, suffix, map, mpu);
                d.steps = new Vector2(Mathf.Clamp(F(m, "_PPDMinSamples", 5f), 1f, 64f), Mathf.Clamp(F(m, "_PPDMaxSamples", 15f), 1f, 128f));
                d.capped = d.depth > 0.5f;
                d.label = $"POM {amp:0.#} cm ({Mathf.Min(d.depth, 0.5f) * 100f:0.#} cm real{DepthSource(true, map, mpu, users)}), {d.steps.x:0}-{d.steps.y:0} steps";
            }
            else if (height && mode != 0)
            {
                d.depth = I(m, "_HeightMapParametrization") == 1 ? F(m, "_HeightTessAmplitude" + suffix, 2f) * 0.01f
                                                                 : Mathf.Abs(F(m, "_HeightMax" + suffix, 1f) - F(m, "_HeightMin" + suffix, -1f)) * 0.01f;
                d.label = $"displacement {d.depth * 100f:0.#} cm as relief";
            }
            else
                d.label = height ? "height map (relief 3 cm, no displacement set)" : "relief 3 cm";
            d.depth = Mathf.Clamp(d.depth, 0.002f, 0.5f);
            return d;
        }

        // HDRP's POM amplitude is in UV space: it assumes one UV unit spans Primitive Length meters, and with Lock
        // Tiling Scale (on by default) it divides by the tiling. Carve's depth is real meters, so convert with how many
        // meters one UV unit really covers on the meshes using the material (LitDataDisplacement.hlsl, ApplyPerPixelDisplacement).
        static float PomToMeters(Material m, string layer, Mapping map, float mpu)
        {
            // The shader reads the stored _InvTilingScale (kept in sync by HDRP's inspector), so use it as rendered.
            float invTiling = F(m, "_DisplacementLockTilingScale", 1f) > 0.5f ? F(m, "_InvTilingScale" + layer, InvTiling(m, layer, map)) : 1f;
            if (map.mode > 0)
                return invTiling;
            Vector4 inv = m.HasProperty("_InvPrimScale") ? m.GetVector("_InvPrimScale") : Vector4.one;
            float invPrim = Mathf.Max((Mathf.Abs(inv.x) + Mathf.Abs(inv.y)) * 0.5f, 1e-4f);
            return invTiling * mpu * invPrim;
        }

        // What HDRP's inspector stores in _InvTilingScale: 2 / (|tiling.x| + |tiling.y|), divided by the world scale for planar/triplanar.
        static float InvTiling(Material m, string layer, Mapping map)
        {
            string tex = m.HasProperty("_BaseColorMap" + layer) ? "_BaseColorMap" + layer : "_BaseColorMap";
            Vector2 st = m.HasProperty(tex) ? m.GetTextureScale(tex) : Vector2.one;
            float inv = 2f / Mathf.Max(Mathf.Abs(st.x) + Mathf.Abs(st.y), 1e-4f);
            return map.mode > 0 ? inv / Mathf.Max(map.worldScale, 1e-4f) : inv;
        }

        static string DepthSource(bool pom, Mapping map, float mpu, int users)
        {
            if (!pom || map.mode > 0) return "";
            return users > 0 ? " on the meshes in the open scenes" : ", not in open scenes: 1 m per UV unit assumed";
        }

        // How many meters one UV0 unit covers on the meshes using m in the open scenes (median); 1 if unused.
        // HDRP also divides the tangent view vector by the object scale (y only with Lock Object Scale), so that is folded in.
        static float MetersPerUV(Material m, out int users)
        {
            bool lockObject = F(m, "_DisplacementLockObjectScale", 1f) > 0.5f;
            var ratios = new List<float>();
            foreach (var r in CarveFind.All<Renderer>())
            {
                var mats = r.sharedMaterials;
                int sub = System.Array.IndexOf(mats, m);
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
                Vector3 s = r.transform.lossyScale;
                float sxz = Mathf.Max((Mathf.Abs(s.x) + Mathf.Abs(s.z)) * 0.5f, 1e-4f);
                float objectFactor = lockObject ? Mathf.Abs(s.y) / sxz : 1f / sxz;
                if (uvArea > 1e-9) ratios.Add((float)System.Math.Sqrt(world / uvArea) * objectFactor);
            }
            users = ratios.Count;
            if (users == 0) return 1f;
            ratios.Sort();
            return ratios[users / 2];
        }

        static void SetPomAmplitude(Material t, string layer, float meters)
        {
            t.SetFloat("_HeightPoMAmplitude" + layer, meters * 100f);
            t.SetFloat("_HeightAmplitude" + layer, meters);
        }

        // 0 = UV, 1 = triplanar object, 2 = triplanar world; worldScale multiplies the tiling (Lit triplanar: tiles per meter).
        struct Mapping
        {
            public int mode;
            public float worldScale;
        }

        static Mapping ReadMapping(Material m, string suffix, CarveConversion c)
        {
            int uv = I(m, "_UVBase" + suffix);
            var map = new Mapping { worldScale = 1f };
            if (uv == 5)
            {
                map.mode = I(m, "_ObjectSpaceUVMapping") == 1 ? 1 : 2;
                map.worldScale = F(m, "_TexWorldScale" + suffix, 1f);
                c?.carried.Add(map.mode == 1 ? "triplanar (object)" : "triplanar (world)");
            }
            else if (uv == 4)
                c?.lost.Add("planar mapping (uses UV0)");
            else if (uv > 0)
                c?.lost.Add($"UV{uv} mapping (uses UV0)");
            return map;
        }

        static void ApplyMapping(Material t, Mapping map, string tex)
        {
            t.SetFloat("_CarveMapping", map.mode);
            if (map.mode > 0 && t.HasProperty(tex))
                t.SetTextureScale(tex, t.GetTextureScale(tex) * map.worldScale);
        }

        static void BackPom(Material t, bool height, float minSteps, float maxSteps)
        {
            t.SetFloat("_DisplacementMode", height ? 2f : 0f);
            t.SetFloat("_PPDMinSamples", Mathf.Clamp(minSteps, 1f, 64f));
            t.SetFloat("_PPDMaxSamples", Mathf.Clamp(maxSteps, 1f, 64f));
            // Carve turns alpha clipping on for its silhouettes; Lit would cut the albedo's alpha instead.
            t.SetFloat("_AlphaCutoffEnable", 0f);
        }

        static void BackMapping(Material t, int mapping, string suffix)
        {
            t.SetFloat("_UVBase" + suffix, mapping > 0 ? 5f : 0f);
            if (mapping > 0)
            {
                t.SetFloat("_ObjectSpaceUVMapping", mapping == 1 ? 1f : 0f);
                t.SetFloat("_TexWorldScale" + suffix, 1f);
            }
        }

        // Lit features Carve has no equivalent for (listed in the converter, never silently dropped).
        static void Lost(Material m, CarveConversion c, string suffix)
        {
            if (I(m, "_SurfaceType") == 1) c.lost.Add("transparency (Carve is opaque)");
            if (HasTex(m, "_EmissiveColorMap") || (m.HasProperty("_EmissiveColor") && m.GetColor("_EmissiveColor").maxColorComponent > 0.001f)) c.lost.Add("emission");
            if (HasTex(m, "_DetailMap" + suffix) || HasTex(m, "_DetailMap")) c.lost.Add("detail map (use Carve's Detail Relief)");
            int id = I(m, "_MaterialID", 1);
            if (id != 1) c.lost.Add(new[] { "subsurface scattering", "", "anisotropy", "iridescence", "specular color", "translucency" }[Mathf.Clamp(id, 0, 5)]);
            if (HasTex(m, "_BentNormalMap")) c.lost.Add("bent normal map (Carve computes its own)");
            if (F(m, "_CoatMask", 0f) > 0f) c.lost.Add("clear coat");
            if (I(m, "_AlphaCutoffEnable") == 1) c.lost.Add("alpha cutout (Carve uses alpha clipping for silhouettes)");
        }
    }
}
