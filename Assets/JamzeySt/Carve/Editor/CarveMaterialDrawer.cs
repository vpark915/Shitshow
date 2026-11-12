using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace JamzeySt.Carve.Editor
{
    // Pipeline-neutral inspector body for Carve materials; each pipeline's ShaderGUI calls Draw().
    public static class CarveMaterialDrawer
    {
        static class Styles
        {
            public static readonly GUIContent BaseMap = new GUIContent("Base Map", "Albedo (RGB). Tiling and offset apply to every map.");
            public static readonly GUIContent NormalMap = new GUIContent("Normal Map", "Tangent-space normal map and its strength.");
            public static readonly GUIContent MaskMap = new GUIContent("Mask Map", "Packed mask: R metallic, G ambient occlusion, A smoothness (HDRP layout).");
            public static readonly GUIContent Metallic = new GUIContent("Metallic");
            public static readonly GUIContent Smoothness = new GUIContent("Smoothness", "Multiplies the mask map's alpha.");
            public static readonly GUIContent HeightMap = new GUIContent("Height Map", "Linear grayscale: white = top, black = deepest. Use 16-bit (R16) for smooth relief.");
            public static readonly GUIContent Depth = new GUIContent("Depth (cm)", "Real depth of the relief in centimeters, independent of UV scale and tiling.");
            public static readonly GUIContent Steps = new GUIContent("Steps (min / max)", "Ray-march steps: min when looking straight on, max at grazing angles.");
            public static readonly GUIContent Refine = new GUIContent("Refine Steps", "Extra secant steps that sharpen the hit point.");
            public static readonly GUIContent Fade = new GUIContent("Distance Fade (m)", "Relief fades out between these distances to save cost far away.");
            public static readonly GUIContent Silhouette = new GUIContent("Edge Silhouettes", "Cut the relief into the outline of faces.\nUV Tile: every 0-1 UV tile is a face (Unity cube, many modular kits).\nBaked: face outlines baked into the mesh (Tools/Carve/Bake Silhouette Data).");
            public static readonly GUIContent Curvature = new GUIContent("Curved Silhouettes", "Bumpy outlines on curved meshes (rocks, columns, pipes).\nAuto: measured on the fly, no setup.\nBaked: per-vertex curvature from the mesh baker, smoothest result.");
            public static readonly GUIContent CurvatureScale = new GUIContent("Curvature Scale", "Exaggerate or soften curved silhouettes.");
            public static readonly GUIContent SelfShadow = new GUIContent("Self Shadow", "Relief casts soft shadows onto itself from the sun.");
            public static readonly GUIContent Softness = new GUIContent("Shadow Softness (°)", "Angular size of the light: larger = softer penumbra.");
            public static readonly GUIContent ShadowSteps = new GUIContent("Shadow Steps", "Samples along the shadow ray.");
            public static readonly GUIContent Occlusion = new GUIContent("Relief Occlusion", "Darkens deep, enclosed gaps in ambient light.");
            public static readonly GUIContent OcclusionRadius = new GUIContent("Occlusion Radius (cm)", "How far around each point the relief is checked.");
            public static readonly GUIContent SpecOcclusion = new GUIContent("Specular Occlusion", "Blocks reflections that would leak out of cracks: traces the reflection direction through the relief.");
            public static readonly GUIContent Stability = new GUIContent("Stability", "Less shimmer without TAA (Scene view, VR, MSAA): marches a slightly coarser height and softens tiny shadows. 0 = sharpest.");
            public static readonly GUIContent Cliff = new GUIContent("Cliff Shading", "Shades steep walls of the relief like real geometry and hides texture stretching.");
            public static readonly GUIContent CliffSlope = new GUIContent("Cliff Threshold", "Slope where cliff shading starts (lower = more walls treated).");
            public static readonly GUIContent Tracing = new GUIContent("Tracing", "Linear: fixed steps along the ray.\nPyramid: marches only between the lowest and highest relief under each pixel (baked min/max pyramid): fewer steps, never coarser. Baked automatically.");
            public static readonly GUIContent Pyramid = new GUIContent("Height Pyramid", "Generated from the height map (Assets/Carve Generated/Pyramids). Rebuilt automatically when the height map changes.");
            public static readonly GUIContent CasterSteps = new GUIContent("Cast Shadow Steps", "Fraction of the view steps used when rendering shadow maps. Shadow maps are lower resolution, so half is usually enough.");
            public static readonly GUIContent AutoLOD = new GUIContent("Auto LOD", "Caps the steps at this many per screen pixel the ray travels. Distant and shallow relief then costs a few steps; up close nothing changes. 0 = off.");
            public static readonly GUIContent Generate = new GUIContent("Generate Height Map", "Builds a height map from this material's normal map (plus a little albedo detail), saves it next to the albedo as a 16-bit PNG and assigns it.");
            public static readonly GUIContent GenerateOptions = new GUIContent("▾", "Height map generator options");
            public static readonly GUIContent Mapping = new GUIContent("Mapping", "UV: the mesh's own UVs.\nTriplanar Object: projected from three sides in the object's space; ignores UVs, so it works on rocks, spheres and scans with bad or no UVs. Moves with the object.\nTriplanar World: same in world space; textures stay continuous across neighbouring objects.");
            public static readonly GUIContent BlendSharpness = new GUIContent("Blend Sharpness", "How quickly the three projections hand over. Higher = narrower blend zones (sharper, less smearing).");
            public static readonly GUIContent[] MappingOptions = { new GUIContent("UV"), new GUIContent("Triplanar Object"), new GUIContent("Triplanar World") };
            public static readonly GUIContent AntiTiling = new GUIContent("Anti-Tiling", "Breaks up visible repetition: every hex cell gets its own offset and rotation of the texture, blended by height so seams fall into the gaps. The relief follows too. 0 = off; costs 3 height samples per step.");
            public static readonly GUIContent AntiTilingCell = new GUIContent("Cell Size (tiles)", "Size of the hex cells in texture tiles. Larger = fewer seams, less variation.");
            public static readonly GUIContent Detail = new GUIContent("Detail Relief", "A second, finer relief (pores, cracks, grain) tiled on top of the main one. It has real depth, so it self-shadows and catches light. Costs one more height sample per step.");
            public static readonly GUIContent DetailHeight = new GUIContent("Detail Height", "Linear grayscale height of the fine relief.");
            public static readonly GUIContent DetailNormal = new GUIContent("Detail Normal", "Normal map of the fine relief and its strength.");
            public static readonly GUIContent DetailTiling = new GUIContent("Detail Tiling", "Detail tiles per base tile.");
            public static readonly GUIContent DetailDepth = new GUIContent("Detail Depth (cm)", "Real depth of the fine relief, added to the main depth.");
            public static readonly GUIContent GenerateDetail = new GUIContent("Generate from Detail Normal", "Builds the detail height map from the detail normal map and assigns it.");
            public static readonly GUIContent LayerCount = new GUIContent("Layers", "How many surfaces to blend. Layer 0 is the base; the mask paints layers 1-3 over it.");
            public static readonly GUIContent[] LayerCountOptions = { new GUIContent("2"), new GUIContent("3"), new GUIContent("4") };
            public static readonly GUIContent MaskSource = new GUIContent("Mask Source", "Where layers 1-3 are painted (R, G, B).\nMask Map: a texture over the mesh's UVs.\nVertex Color: painted on the mesh (Polybrush or any vertex paint tool).\nBoth: multiplied.");
            public static readonly GUIContent[] MaskSourceOptions = { new GUIContent("Mask Map"), new GUIContent("Vertex Color"), new GUIContent("Both") };
            public static readonly GUIContent LayerMask = new GUIContent("Layer Mask", "R, G, B = coverage of layers 1, 2, 3 (mesh UV0). Black = base layer only.");
            public static readonly GUIContent MaskUV = new GUIContent("Mask UV", "UV0: the mesh's main UVs (the mask follows the relief exactly).\nUV1: lightmap UVs, for meshes whose UV0 tiles (walls, floors): every spot gets its own paint.");
            public static readonly GUIContent[] MaskUVOptions = { new GUIContent("UV0"), new GUIContent("UV1 (lightmap UVs)") };
            public static readonly GUIContent Paint = new GUIContent("Paint Layers in Scene View", "Brush the layers onto the selected objects, like terrain painting. 1-4 pick a layer, Shift erases, [ ] change the size.");
            public static readonly GUIContent PaintStop = new GUIContent("Stop Painting");
            public static readonly GUIContent HeightBlend = new GUIContent("Height Blend", "How much the relief decides the boundary. 1 = the taller surface wins (moss in the gaps, stones poking through sand); 0 = plain cross-fade by the mask.");
            public static readonly GUIContent BlendSoftness = new GUIContent("Blend Softness", "Width of the transition between layers, in relief height. Lower = crisper edges.");
            public static readonly GUIContent LayerHeightMap = new GUIContent("Height Map", "This layer's relief. Empty = a flat layer at its Height Offset + Height Scale.");
            public static readonly GUIContent LayerHeight = new GUIContent("Height Scale", "This layer's relief as a share of Depth.");
            public static readonly GUIContent LayerOffset = new GUIContent("Height Offset", "Raises (+) or sinks (-) the whole layer, as a share of Depth. Raise a flat sand or water layer to fill the gaps of a rock layer.");
            public static readonly GUIContent[] LayerTitles = { new GUIContent("Layer 0 · Base"), new GUIContent("Layer 1 · Mask R"), new GUIContent("Layer 2 · Mask G"), new GUIContent("Layer 3 · Mask B") };
            public static readonly GUIContent[] TracingOptions = { new GUIContent("Linear"), new GUIContent("Pyramid") };
            public static readonly GUIContent DebugView = new GUIContent("Debug View", "Visualize one part of Carve on this material.");
            public static readonly GUIContent[] SilhouetteOptions = { new GUIContent("Off"), new GUIContent("UV Tile"), new GUIContent("Baked") };
            public static readonly GUIContent[] CurvatureOptions = { new GUIContent("Off"), new GUIContent("Auto"), new GUIContent("Baked") };
            public static readonly GUIContent[] DebugOptions = { new GUIContent("Off"), new GUIContent("Curvature"), new GUIContent("Steps"), new GUIContent("Silhouette"), new GUIContent("Self Shadow"), new GUIContent("Occlusion"), new GUIContent("Cliff"), new GUIContent("Specular Occlusion"), new GUIContent("Triplanar Blend"), new GUIContent("Layers") };
        }

        // Pipeline hooks: what is wrong with a material's surface setup (null = fine) and how to fix it.
        public static System.Func<Material, string> SetupProblem;
        public static System.Action<Material> FixSetup;

        static readonly string[] Sections = { "Surface", "Relief", "Silhouettes", "Shadows & Occlusion", "Debug", "Detail Relief" };

        public static void Draw(MaterialEditor editor, MaterialProperty[] props)
        {
            var mats = editor.targets.OfType<Material>().ToArray();
            P p = new P(props);

            string problem = SetupProblem == null ? null : mats.Select(SetupProblem).FirstOrDefault(x => x != null);
            if (problem != null && HelpWithButton($"{problem}: the relief has no real depth and outlines are not cut. (Common after switching a Lit material to Carve.)", MessageType.Error, "Fix"))
                foreach (var m in mats) FixSetup(m);

            bool triplanar = p["_CarveMapping"] != null && (p["_CarveMapping"].hasMixedValue || p["_CarveMapping"].floatValue > 0.5f);
            if (Section(0))
            {
                Popup(editor, p["_CarveMapping"], Styles.Mapping, Styles.MappingOptions);
                if (triplanar)
                {
                    EditorGUI.indentLevel++;
                    Prop(editor, p["_CarveBlendSharpness"], Styles.BlendSharpness);
                    EditorGUILayout.LabelField(" ", "Tiling below = tiles per meter.", EditorStyles.miniLabel);
                    EditorGUI.indentLevel--;
                }
                if (p["_LayerCount"] != null)
                    Layers(editor, p, mats);
                else
                {
                    if (p["_BaseColorMap"] != null) editor.TexturePropertySingleLine(Styles.BaseMap, p["_BaseColorMap"], p["_BaseColor"]);
                    if (p["_NormalMap"] != null) editor.TexturePropertySingleLine(Styles.NormalMap, p["_NormalMap"], p["_NormalScale"]);
                    if (p["_MaskMap"] != null) editor.TexturePropertySingleLine(Styles.MaskMap, p["_MaskMap"]);
                    Prop(editor, p["_Metallic"], Styles.Metallic);
                    Prop(editor, p["_Smoothness"], Styles.Smoothness);
                    if (p["_BaseColorMap"] != null) editor.TextureScaleOffsetProperty(p["_BaseColorMap"]);
                }
                End();
            }

            if (Section(1))
            {
                if (p["_HeightMap"] != null)
                    HeightMapRow(editor, p["_HeightMap"], Styles.HeightMap, mats, -1);
                Scaled(editor, p["_CarveDepth"], Styles.Depth, 100f, 0f, 50f);
                MinMax(editor, p["_CarveMinSteps"], p["_CarveMaxSteps"], Styles.Steps, 1f, 128f);
                Prop(editor, p["_CarveAutoLOD"], Styles.AutoLOD);
                Prop(editor, p["_CarveAntiTiling"], Styles.AntiTiling);
                if (Enabled(p["_CarveAntiTiling"]))
                {
                    EditorGUI.indentLevel++;
                    Prop(editor, p["_CarveAntiTilingCell"], Styles.AntiTilingCell);
                    EditorGUI.indentLevel--;
                }
                Prop(editor, p["_CarveRefineSteps"], Styles.Refine);
                Prop(editor, p["_CarveStability"], Styles.Stability);
                if (p["_CarveTracing"] != null)
                {
                    EditorGUI.BeginChangeCheck();
                    Popup(editor, p["_CarveTracing"], Styles.Tracing, Styles.TracingOptions);
                    if (EditorGUI.EndChangeCheck()) CarvePyramidBaker.Assign(mats.Where(CarvePyramidBaker.UsesPyramid));
                    PyramidStatus(editor, p["_CarvePyramid"], mats);
                }
                MinMax(editor, p["_CarveFadeStart"], p["_CarveFadeEnd"], Styles.Fade, 0f, 200f);
                End();
            }

            if (p["_CarveDetail"] != null && Section(5))
            {
                Prop(editor, p["_CarveDetail"], Styles.Detail);
                if (Enabled(p["_CarveDetail"]))
                {
                    Rect line = editor.TexturePropertySingleLine(Styles.DetailHeight, p["_CarveDetailHeight"]);
                    if (mats.Any(CarveHeightGenerator.CanGenerateDetail))
                    {
                        float h = EditorGUIUtility.singleLineHeight;
                        if (GUI.Button(new Rect(line.xMax - 172f, line.y + (line.height - h) * 0.5f, 172f, h), Styles.GenerateDetail, EditorStyles.miniButton)
                            && CarveHeightGenerator.GenerateDetailFor(mats) > 0)
                            GUIUtility.ExitGUI();
                    }
                    editor.TexturePropertySingleLine(Styles.DetailNormal, p["_CarveDetailNormal"], p["_CarveDetailNormalScale"]);
                    Prop(editor, p["_CarveDetailTiling"], Styles.DetailTiling);
                    Scaled(editor, p["_CarveDetailDepth"], Styles.DetailDepth, 100f, 0f, 5f);
                }
                End();
            }

            if (Section(2))
            {
                EditorGUI.BeginChangeCheck();
                if (triplanar)
                    EditorGUILayout.HelpBox("Triplanar has no UV faces, so Edge Silhouettes are off; Curved Silhouettes still carve the outline (Baked curvature is measured as Auto).", MessageType.None);
                else
                    Popup(editor, p["_CarveSilhouette"], Styles.Silhouette, Styles.SilhouetteOptions);
                Popup(editor, p["_CarveCurvature"], Styles.Curvature, Styles.CurvatureOptions);
                if (EditorGUI.EndChangeCheck() && FixSetup != null)
                    foreach (var m in mats) if (SetupProblem(m) != null) FixSetup(m);
                if (p["_CarveCurvature"] != null && (p["_CarveCurvature"].hasMixedValue || p["_CarveCurvature"].floatValue > 0.5f))
                {
                    EditorGUI.indentLevel++;
                    Prop(editor, p["_CarveCurvatureScale"], Styles.CurvatureScale);
                    EditorGUI.indentLevel--;
                }
                BakeStatus(mats);
                End();
            }

            if (Section(3))
            {
                Prop(editor, p["_CarveSelfShadow"], Styles.SelfShadow);
                if (Enabled(p["_CarveSelfShadow"]))
                {
                    EditorGUI.indentLevel++;
                    Prop(editor, p["_CarveShadowSoftness"], Styles.Softness);
                    Prop(editor, p["_CarveShadowSteps"], Styles.ShadowSteps);
                    EditorGUI.indentLevel--;
                }
                Prop(editor, p["_CarveCasterSteps"], Styles.CasterSteps);
                Prop(editor, p["_CarveOcclusion"], Styles.Occlusion);
                if (Enabled(p["_CarveOcclusion"]))
                {
                    EditorGUI.indentLevel++;
                    Scaled(editor, p["_CarveOcclusionRadius"], Styles.OcclusionRadius, 100f, 0.5f, 25f);
                    EditorGUI.indentLevel--;
                }
                Prop(editor, p["_CarveSpecOcclusion"], Styles.SpecOcclusion);
                Prop(editor, p["_CarveCliff"], Styles.Cliff);
                if (Enabled(p["_CarveCliff"]))
                {
                    EditorGUI.indentLevel++;
                    Prop(editor, p["_CarveCliffSlope"], Styles.CliffSlope);
                    EditorGUI.indentLevel--;
                }
                if (CarveSun.Current == null)
                    EditorGUILayout.HelpBox("No directional light found: self-shadowing is off until the scene has a sun.", MessageType.Info);
                End();
            }

            if (Section(4))
            {
                Popup(editor, p["_CarveDebug"], Styles.DebugView, Styles.DebugOptions);
                if (CarveGlobals.DebugView != CarveDebugView.Off)
                    EditorGUILayout.HelpBox($"The Carve Hub's global debug view ({CarveGlobals.DebugView}) is overriding all Carve materials.", MessageType.Info);
                End();
            }

            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Open Carve Hub", GUILayout.Width(140))) CarveHub.Open();
            }
        }

        class P
        {
            readonly Dictionary<string, MaterialProperty> m_Map = new Dictionary<string, MaterialProperty>();
            public P(MaterialProperty[] props) { foreach (var x in props) m_Map[x.name] = x; }
            public MaterialProperty this[string name] => m_Map.TryGetValue(name, out var v) ? v : null;
        }

        static bool Section(int i)
        {
            string key = "JamzeySt.Carve.Material.Section." + i;
            bool open = EditorPrefs.GetBool(key, i < 4);
            bool now = CoreEditorUtils.DrawHeaderFoldout(Sections[i], open);
            if (now != open) EditorPrefs.SetBool(key, now);
            if (now) EditorGUI.indentLevel++;
            return now;
        }

        static void End()
        {
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(6);
        }

        static bool Enabled(MaterialProperty p) => p != null && (p.hasMixedValue || p.floatValue > 0f);

        static void Prop(MaterialEditor editor, MaterialProperty p, GUIContent label)
        {
            if (p != null) editor.ShaderProperty(p, label);
        }

        static void Popup(MaterialEditor editor, MaterialProperty p, GUIContent label, GUIContent[] options, int first = 0)
        {
            if (p == null) return;
            EditorGUI.showMixedValue = p.hasMixedValue;
            EditorGUI.BeginChangeCheck();
            int v = EditorGUILayout.Popup(label, Mathf.Clamp(Mathf.RoundToInt(p.floatValue) - first, 0, options.Length - 1), options);
            if (EditorGUI.EndChangeCheck())
            {
                editor.RegisterPropertyChangeUndo(label.text);
                p.floatValue = v + first;
            }
            EditorGUI.showMixedValue = false;
        }

        static void Scaled(MaterialEditor editor, MaterialProperty p, GUIContent label, float scale, float min, float max)
        {
            if (p == null) return;
            EditorGUI.showMixedValue = p.hasMixedValue;
            EditorGUI.BeginChangeCheck();
            float v = EditorGUILayout.Slider(label, p.floatValue * scale, min, max);
            if (EditorGUI.EndChangeCheck())
            {
                editor.RegisterPropertyChangeUndo(label.text);
                p.floatValue = v / scale;
            }
            EditorGUI.showMixedValue = false;
        }

        static void MinMax(MaterialEditor editor, MaterialProperty lo, MaterialProperty hi, GUIContent label, float min, float max)
        {
            if (lo == null || hi == null) return;
            EditorGUI.showMixedValue = lo.hasMixedValue || hi.hasMixedValue;
            float a = lo.floatValue, b = hi.floatValue;
            EditorGUI.BeginChangeCheck();
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(label);
                int indent = EditorGUI.indentLevel;
                EditorGUI.indentLevel = 0;
                a = EditorGUILayout.FloatField(a, GUILayout.Width(44));
                EditorGUILayout.MinMaxSlider(ref a, ref b, min, max);
                b = EditorGUILayout.FloatField(b, GUILayout.Width(44));
                EditorGUI.indentLevel = indent;
            }
            if (EditorGUI.EndChangeCheck())
            {
                editor.RegisterPropertyChangeUndo(label.text);
                a = Mathf.Clamp(Mathf.Round(a * 10f) / 10f, min, max);
                b = Mathf.Clamp(Mathf.Round(b * 10f) / 10f, a, max);
                lo.floatValue = a;
                hi.floatValue = b;
            }
            EditorGUI.showMixedValue = false;
        }

        // Height map slot with its Generate button and import checks; layer = -1 for Carve Lit's single height map.
        static void HeightMapRow(MaterialEditor editor, MaterialProperty prop, GUIContent label, Material[] mats, int layer)
        {
            EditorGUI.BeginChangeCheck();
            Rect line = editor.TexturePropertySingleLine(label, prop);
            if (EditorGUI.EndChangeCheck()) CarvePyramidBaker.Assign(mats.Where(CarvePyramidBaker.UsesPyramid));
            bool canGenerate = mats.Any(m => CarveProject.HeightSlots(m).Any(s => s.layer == layer && CarveHeightGenerator.CanGenerate(m, s)));
            if (canGenerate) GenerateButton(line, mats, layer);
            HeightMapChecks(mats, prop.name, layer < 0);
        }

        static void Layers(MaterialEditor editor, P p, Material[] mats)
        {
            Popup(editor, p["_LayerCount"], Styles.LayerCount, Styles.LayerCountOptions, 2);
            Popup(editor, p["_CarveLayerMaskSource"], Styles.MaskSource, Styles.MaskSourceOptions);
            if (p["_CarveLayerMaskSource"].hasMixedValue || p["_CarveLayerMaskSource"].floatValue < 0.5f || p["_CarveLayerMaskSource"].floatValue > 1.5f)
            {
                editor.TexturePropertySingleLine(Styles.LayerMask, p["_LayerMaskMap"]);
                EditorGUI.indentLevel++;
                Popup(editor, p["_CarveLayerMaskUV"], Styles.MaskUV, Styles.MaskUVOptions);
                if (!mats.All(m => CarveLayerPainter.IsPaintMask(m.GetTexture("_LayerMaskMap"))))
                    editor.TextureScaleOffsetProperty(p["_LayerMaskMap"]);
                EditorGUI.indentLevel--;
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(" ");
                bool active = CarveLayerPaintTool.Active;
                if (GUILayout.Button(active ? Styles.PaintStop : Styles.Paint))
                {
                    if (active) UnityEditor.Tools.current = Tool.Move;
                    else if (Selection.gameObjects.Any(g => g.GetComponentsInChildren<Renderer>().Any(r => r.sharedMaterials.Intersect(mats).Any())))
                        CarveLayerPaintTool.Activate();
                    else
                        EditorUtility.DisplayDialog("Carve Layer Paint", "Select an object in the scene that uses this material, then press Paint Layers.", "OK");
                }
            }
            Prop(editor, p["_CarveHeightBlend"], Styles.HeightBlend);
            Prop(editor, p["_CarveLayerBlendSoftness"], Styles.BlendSoftness);
            int count = p["_LayerCount"].hasMixedValue ? 4 : Mathf.Clamp(Mathf.RoundToInt(p["_LayerCount"].floatValue), 2, 4);
            for (int i = 0; i < count; i++)
            {
                string key = "JamzeySt.Carve.Material.Layer." + i;
                bool open = EditorPrefs.GetBool(key, i == 0);
                bool now = EditorGUILayout.Foldout(open, Styles.LayerTitles[i], true, EditorStyles.foldoutHeader);
                if (now != open) EditorPrefs.SetBool(key, now);
                if (!now) continue;
                EditorGUI.indentLevel++;
                editor.TexturePropertySingleLine(Styles.BaseMap, p["_BaseColorMap" + i], p["_BaseColor" + i]);
                editor.TexturePropertySingleLine(Styles.NormalMap, p["_NormalMap" + i], p["_NormalScale" + i]);
                editor.TexturePropertySingleLine(Styles.MaskMap, p["_MaskMap" + i]);
                Prop(editor, p["_Metallic" + i], Styles.Metallic);
                Prop(editor, p["_Smoothness" + i], Styles.Smoothness);
                HeightMapRow(editor, p["_HeightMap" + i], Styles.LayerHeightMap, mats, i);
                Prop(editor, p["_CarveLayerHeight" + i], Styles.LayerHeight);
                Prop(editor, p["_CarveLayerOffset" + i], Styles.LayerOffset);
                editor.TextureScaleOffsetProperty(p["_BaseColorMap" + i]);
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(2);
            }
        }

        static void HeightMapChecks(Material[] mats, string prop, bool warnMissing)
        {
            var fix = new List<TextureImporter>();
            bool missing = false;
            foreach (var m in mats)
            {
                var tex = m.GetTexture(prop);
                if (tex == null) { missing = true; continue; }
                if (AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(tex)) is TextureImporter ti && (ti.sRGBTexture || !ti.mipmapEnabled) && !fix.Contains(ti))
                    fix.Add(ti);
            }
            if (missing && warnMissing)
                EditorGUILayout.HelpBox("No height map: the surface stays flat.", MessageType.Warning);
            if (fix.Count > 0 && HelpWithButton("Height map should be linear (sRGB off) with mip maps on.", MessageType.Warning, "Fix Import"))
            {
                foreach (var ti in fix)
                {
                    ti.sRGBTexture = false;
                    ti.mipmapEnabled = true;
                    ti.SaveAndReimport();
                }
            }
        }

        static void GenerateButton(Rect line, Material[] mats, int layer)
        {
            float h = EditorGUIUtility.singleLineHeight;
            var main = new Rect(line.xMax - 172f, line.y + (line.height - h) * 0.5f, 150f, h);
            var more = new Rect(main.xMax + 2f, main.y, 20f, h);
            if (GUI.Button(main, Styles.Generate, EditorStyles.miniButtonLeft))
            {
                int n = CarveHeightGenerator.GenerateFor(mats, layer);
                if (n > 0) GUIUtility.ExitGUI();
            }
            if (GUI.Button(more, Styles.GenerateOptions, EditorStyles.miniButtonRight))
                PopupWindow.Show(more, new GeneratorOptions());
        }

        class GeneratorOptions : PopupWindowContent
        {
            public override Vector2 GetWindowSize() => new Vector2(280f, 126f);

            public override void OnGUI(Rect rect)
            {
                EditorGUILayout.LabelField("Height Map Generator", EditorStyles.boldLabel);
                float w = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 150f;
                CarveHeightGenerator.Smoothing = EditorGUILayout.Slider(new GUIContent("Smoothing", "Removes the normal map's fine grain, which would turn into tiny pits. 0 = keep everything."), CarveHeightGenerator.Smoothing, 0f, 1f);
                CarveHeightGenerator.AlbedoDetail = EditorGUILayout.Slider(new GUIContent("Albedo detail", "Adds fine detail from the albedo (dark = lower). Usually best at 0; raise it for weak or blurry normal maps."), CarveHeightGenerator.AlbedoDetail, 0f, 1f);
                CarveHeightGenerator.Green = (CarveHeightGenerator.GreenChannel)EditorGUILayout.EnumPopup(new GUIContent("Normal map green", "Auto detects DirectX (Y-) normal maps. Unity's own convention is OpenGL (Y+)."), CarveHeightGenerator.Green);
                CarveHeightGenerator.Invert = EditorGUILayout.Toggle(new GUIContent("Invert", "Swap high and low."), CarveHeightGenerator.Invert);
                EditorGUIUtility.labelWidth = w;
            }
        }

        static void PyramidStatus(MaterialEditor editor, MaterialProperty pyramid, Material[] mats)
        {
            if (!mats.Any(CarvePyramidBaker.UsesPyramid)) return;
            EditorGUI.indentLevel++;
            using (new EditorGUI.DisabledScope(true))
                if (pyramid != null) editor.TexturePropertySingleLine(Styles.Pyramid, pyramid);
            EditorGUI.indentLevel--;
            int missing = mats.Count(m => CarvePyramidBaker.GetStatus(m) == CarvePyramidBaker.Status.Missing);
            int stale = mats.Count(m => CarvePyramidBaker.GetStatus(m) == CarvePyramidBaker.Status.Stale);
            if (missing + stale > 0 && HelpWithButton(missing > 0 ? "No height pyramid yet: tracing runs Linear until it is baked." : "The height map changed since the pyramid was baked.",
                                                      MessageType.Warning, missing > 0 ? "Bake" : "Rebake"))
                CarvePyramidBaker.Assign(mats.Where(CarvePyramidBaker.UsesPyramid), stale > 0);
        }

        static void BakeStatus(Material[] mats)
        {
            if (!mats.Any(CarveProject.NeedsBakedData)) return;
            var unbaked = CarveProject.UnbakedFilters(mats);
            if (unbaked.Count == 0)
                EditorGUILayout.HelpBox("Baked mode: every mesh in the open scenes using this material has its silhouette data.", MessageType.None);
            else if (HelpWithButton($"Baked mode: {unbaked.Count} mesh(es) in the open scenes still need silhouette data.", MessageType.Warning, "Bake Now"))
                CarveProject.BakeFilters(unbaked);
        }

        static bool HelpWithButton(string text, MessageType type, string button)
        {
            bool pressed;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.HelpBox(text, type);
                pressed = GUILayout.Button(button, GUILayout.Width(90), GUILayout.Height(38));
            }
            return pressed;
        }
    }
}
