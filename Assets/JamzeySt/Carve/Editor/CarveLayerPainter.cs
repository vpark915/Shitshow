using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.Rendering;

namespace JamzeySt.Carve.Editor
{
    // Scene-view brush for Carve Layered Lit, like Unity's terrain paint: pick a layer, paint it onto the object.
    [EditorTool("Carve Layer Paint")]
    public class CarveLayerPaintTool : EditorTool
    {
        GUIContent m_Icon;

        public override GUIContent toolbarIcon => m_Icon ??= new GUIContent(EditorGUIUtility.IconContent("TerrainInspector.TerrainToolSplat").image, "Carve Layer Paint");

        public static void Activate() => ToolManager.SetActiveTool<CarveLayerPaintTool>();

        public static bool Active => ToolManager.activeToolType == typeof(CarveLayerPaintTool);

        public override void OnActivated()
        {
            CarveLayerPainter.Begin();
            Selection.selectionChanged += CarveLayerPainter.Begin;
        }

        public override void OnWillBeDeactivated()
        {
            Selection.selectionChanged -= CarveLayerPainter.Begin;
            CarveLayerPainter.End();
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (window is SceneView view) CarveLayerPainter.OnSceneGUI(view);
        }
    }

    public static class CarveLayerPainter
    {
        public enum Mode { Paint, Erase, Smooth }

        const string Prefs = "JamzeySt.Carve.Paint.";
        const string MaskFolder = "Assets/Carve Generated/Masks";
        const string MaskLabel = "CarveLayerMask";
        const string MeshFolder = "Assets/Carve Generated/Meshes";
        public static readonly int[] Resolutions = { 512, 1024, 2048, 4096 };
        public static readonly Color[] LayerColors = { new Color(0.75f, 0.75f, 0.75f), new Color(1f, 0.35f, 0.3f), new Color(0.35f, 0.9f, 0.3f), new Color(0.3f, 0.55f, 1f) };

        public static int Layer { get => EditorPrefs.GetInt(Prefs + "Layer", 1); set => EditorPrefs.SetInt(Prefs + "Layer", Mathf.Clamp(value, 0, 3)); }
        public static Mode BrushMode { get => (Mode)EditorPrefs.GetInt(Prefs + "Mode", 0); set => EditorPrefs.SetInt(Prefs + "Mode", (int)value); }
        public static float Size { get => EditorPrefs.GetFloat(Prefs + "Size", 0.5f); set => EditorPrefs.SetFloat(Prefs + "Size", Mathf.Clamp(value, 0.02f, 20f)); }
        public static float Strength { get => EditorPrefs.GetFloat(Prefs + "Strength", 0.5f); set => EditorPrefs.SetFloat(Prefs + "Strength", Mathf.Clamp(value, 0.01f, 1f)); }
        public static float Hardness { get => EditorPrefs.GetFloat(Prefs + "Hardness", 0.3f); set => EditorPrefs.SetFloat(Prefs + "Hardness", Mathf.Clamp01(value)); }
        public static bool FrontOnly { get => EditorPrefs.GetBool(Prefs + "FrontOnly", true); set => EditorPrefs.SetBool(Prefs + "FrontOnly", value); }
        public static int Resolution { get => EditorPrefs.GetInt(Prefs + "Resolution", 1024); set => EditorPrefs.SetInt(Prefs + "Resolution", value); }

        class Session
        {
            public Material material;
            public Texture2D mask;
            public RenderTexture rt, prev, coverage;
            public int uvChannel;
            public Vector4 maskST = new Vector4(1f, 1f, 0f, 0f);
            public bool dirty;
        }

        class Target
        {
            public MeshRenderer renderer;
            public Mesh mesh;
            public Session session;
            public int[] submeshes;
        }

        static readonly Dictionary<Material, Session> s_Sessions = new Dictionary<Material, Session>();
        static readonly List<Target> s_Targets = new List<Target>();
        static readonly List<string> s_Warnings = new List<string>();
        static readonly List<MeshFilter> s_SharedUV = new List<MeshFilter>();
        static Material s_PaintMat;
        static bool s_Hit, s_Painting;
        static RaycastHit s_HitInfo;
        static Vector3 s_LastStamp;
        static readonly HashSet<Session> s_Touched = new HashSet<Session>();

        public static IReadOnlyList<string> Warnings => s_Warnings;
        public static bool BrushOverTarget => s_Hit;
        public static bool HasSharedUVs => s_SharedUV.Count > 0;
        public static Material CurrentMaterial => s_Targets.Count > 0 ? s_Targets[0].session.material : null;
        public static int TargetCount => s_Targets.Select(t => t.renderer).Distinct().Count();
        public static string TargetNames => string.Join(", ", s_Targets.Select(t => t.renderer.name).Distinct());

        static Material PaintMaterial
        {
            get
            {
                if (s_PaintMat == null)
                {
                    var shader = Shader.Find("Hidden/Carve/LayerPaint");
                    if (shader != null) s_PaintMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                }
                return s_PaintMat;
            }
        }

        static bool IsLayered(Material m) => CarveProject.IsLayered(m);

        // ---------------- Session setup

        public static void Begin()
        {
            End();
            s_MeshCache.Clear();
            s_Warnings.Clear();
            s_SharedUV.Clear();
            var renderers = Selection.gameObjects.SelectMany(g => g.GetComponentsInChildren<MeshRenderer>()).Distinct().ToList();
            var asked = new Dictionary<Material, Material>();
            foreach (var r in renderers)
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (!IsLayered(m)) continue;
                    if (!asked.TryGetValue(m, out var use))
                    {
                        use = DecideMaterial(m, r, renderers);
                        asked[m] = use;
                    }
                    if (use == null) continue;
                    if (use != m) { mats[i] = use; changed = true; }
                }
                if (changed)
                {
                    Undo.RecordObject(r, "Unique Carve Material");
                    r.sharedMaterials = mats;
                }
                foreach (var m in r.sharedMaterials.Distinct())
                {
                    if (!IsLayered(m)) continue;
                    var s = GetSession(m);
                    if (s == null) continue;
                    var subs = Enumerable.Range(0, Mathf.Min(r.sharedMaterials.Length, mf.sharedMesh.subMeshCount)).Where(k => r.sharedMaterials[k] == m).ToArray();
                    s_Targets.Add(new Target { renderer = r, mesh = mf.sharedMesh, session = s, submeshes = subs });
                }
            }
            foreach (var s in s_Sessions.Values)
            {
                FitMaskToUVs(s);
                BuildCoverage(s);
            }
            SceneView.RepaintAll();
        }

        public static void End()
        {
            foreach (var s in s_Sessions.Values)
            {
                if (s.dirty) Commit(s);
                if (s.dirty || EditorUtility.IsDirty(s.mask)) AssetDatabase.SaveAssetIfDirty(s.mask);
                Release(s);
            }
            s_Sessions.Clear();
            s_Targets.Clear();
            s_Painting = false;
        }

        // A material shared with objects that aren't being painted would be painted everywhere: offer a copy for these objects.
        static Material DecideMaterial(Material m, MeshRenderer r, List<MeshRenderer> painting)
        {
            int others = CarveFind.All<Renderer>().Count(x => !painting.Contains(x as MeshRenderer) && x.sharedMaterials.Contains(m));
            if (others == 0) return m;
            int choice = EditorUtility.DisplayDialogComplex("Carve Layer Paint",
                $"\"{m.name}\" is also used by {others} other object(s) in the open scenes. Painting changes the mask on all of them.\n\nMake a copy of the material (and its mask) just for the selected objects?",
                "Make Unique Copy", "Cancel", "Paint Shared");
            if (choice == 1) return null;
            if (choice == 2) return m;
            string src = AssetDatabase.GetAssetPath(m);
            string dir = string.IsNullOrEmpty(src) ? "Assets" : Path.GetDirectoryName(src).Replace('\\', '/');
            string path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{m.name}_{r.name}.mat");
            var copy = new Material(m);
            AssetDatabase.CreateAsset(copy, path);
            Undo.RegisterCreatedObjectUndo(copy, "Unique Carve Material");
            var mask = copy.GetTexture("_LayerMaskMap");
            if (mask != null && AssetDatabase.GetLabels(mask).Contains(MaskLabel))
                copy.SetTexture("_LayerMaskMap", CreateMask(copy, mask.width, mask));
            return copy;
        }

        static Session GetSession(Material m)
        {
            if (s_Sessions.TryGetValue(m, out var s)) return s;
            if (PaintMaterial == null) { s_Warnings.Add("Paint shader missing (Hidden/Carve/LayerPaint)."); return null; }
            var mask = m.GetTexture("_LayerMaskMap") as Texture2D;
            if (!IsPaintMask(mask))
            {
                Undo.RecordObject(m, "Carve Layer Mask");
                mask = CreateMask(m, Resolution, mask);
                m.SetTexture("_LayerMaskMap", mask);
                m.SetTextureScale("_LayerMaskMap", Vector2.one);
                m.SetTextureOffset("_LayerMaskMap", Vector2.zero);
                EditorUtility.SetDirty(m);
            }
            if (Mathf.RoundToInt(m.GetFloat("_CarveLayerMaskSource")) == 1)
            {
                Undo.RecordObject(m, "Carve Layer Mask");
                m.SetFloat("_CarveLayerMaskSource", 2f);
                s_Warnings.Add($"{m.name}: Mask Source was Vertex Color; now Both (vertex colors x painted mask).");
            }
            s = new Session { material = m, mask = mask, uvChannel = m.GetFloat("_CarveLayerMaskUV") > 0.5f ? 1 : 0 };
            s.rt = NewRT(mask.width, RenderTextureFormat.ARGB32);
            s.prev = NewRT(mask.width, RenderTextureFormat.ARGB32);
            s.coverage = NewRT(mask.width, RenderTextureFormat.R8);
            Graphics.Blit(mask, s.rt);
            CarvePaintHistory.Remember(mask);
            s_Sessions[m] = s;
            return s;
        }

        static RenderTexture NewRT(int size, RenderTextureFormat format)
        {
            var rt = new RenderTexture(size, size, 0, format, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            rt.Create();
            return rt;
        }

        static void Release(Session s)
        {
            foreach (var rt in new[] { s.rt, s.prev, s.coverage })
            {
                if (rt == null) continue;
                if (RenderTexture.active == rt) RenderTexture.active = null;
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }

        public static bool IsPaintMask(Texture tex) =>
            tex is Texture2D t && AssetDatabase.Contains(t) && t.isReadable && t.format == TextureFormat.RGBA32 && AssetDatabase.GetLabels(t).Contains(MaskLabel);

        // Readable RGBA32 texture asset (linear, mipmapped), so strokes update it in place without reimporting.
        static Texture2D CreateMask(Material m, int size, Texture source)
        {
            EnsureFolder(MaskFolder);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            if (source != null) Graphics.Blit(source, rt);
            else Graphics.Blit(Texture2D.blackTexture, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, size, size), 0, 0, false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = tex.GetPixels32(0);
            for (int i = 0; i < px.Length; i++) px[i].a = 255;
            tex.SetPixels32(px, 0);
            tex.Apply(true, false);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{MaskFolder}/{m.name}_LayerMask.asset");
            tex.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(tex, path);
            AssetDatabase.SetLabels(tex, new[] { MaskLabel });
            return tex;
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        // Meshes whose UV0 is scaled past 0-1 (tiled floors, walls) get the mask stretched once over their whole UV range
        // through the mask's tiling/offset, so every spot has its own paint. The shader and the brush use the same mapping.
        static void FitMaskToUVs(Session s)
        {
            var m = s.material;
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = -min;
            var uvs = new List<Vector2>();
            foreach (var t in s_Targets)
            {
                if (t.session != s) continue;
                uvs.Clear();
                try { t.mesh.GetUVs(s.uvChannel, uvs); }
                catch { continue; }
                if (uvs.Count == 0)
                {
                    s_Warnings.Add($"{t.renderer.name}: the mesh has no UV{s.uvChannel}" + (s.uvChannel == 1 ? " (enable Generate Lightmap UVs on the model, or use Generate Unique UVs)." : "."));
                    continue;
                }
                Vector2 tMin = uvs[0], tMax = uvs[0];
                foreach (var uv in uvs) { tMin = Vector2.Min(tMin, uv); tMax = Vector2.Max(tMax, uv); }
                min = Vector2.Min(min, tMin);
                max = Vector2.Max(max, tMax);
                // Faces that share UV space (box sides, mirrored halves) cover more UV area than the UV bounds hold.
                double area = 0;
                foreach (int sub in t.submeshes)
                {
                    var tri = t.mesh.GetTriangles(sub);
                    for (int i = 0; i < tri.Length; i += 3)
                    {
                        Vector2 p0 = uvs[tri[i]], p1 = uvs[tri[i + 1]], p2 = uvs[tri[i + 2]];
                        area += Mathf.Abs((p1.x - p0.x) * (p2.y - p0.y) - (p2.x - p0.x) * (p1.y - p0.y)) * 0.5;
                    }
                }
                var size = tMax - tMin;
                var mf = t.renderer.GetComponent<MeshFilter>();
                if (area > size.x * size.y * 1.02 && !s_SharedUV.Contains(mf))
                {
                    s_SharedUV.Add(mf);
                    s_Warnings.Add($"{t.renderer.name}: several faces share the same UV{s.uvChannel} space, so paint on one shows on the others. Generate Unique UVs gives every face its own.");
                }
            }
            if (min.x > max.x) return;
            Vector2 st = m.GetTextureScale("_LayerMaskMap"), off = m.GetTextureOffset("_LayerMaskMap");
            bool identity = st == Vector2.one && off == Vector2.zero;
            bool inside = min.x >= -0.01f && min.y >= -0.01f && max.x <= 1.01f && max.y <= 1.01f;
            if (identity && !inside)
            {
                Vector2 size = Vector2.Max(max - min, new Vector2(1e-4f, 1e-4f));
                st = new Vector2(1f / size.x, 1f / size.y);
                off = new Vector2(-min.x * st.x, -min.y * st.y);
                Undo.RecordObject(m, "Fit Carve Layer Mask");
                m.SetTextureScale("_LayerMaskMap", st);
                m.SetTextureOffset("_LayerMaskMap", off);
                EditorUtility.SetDirty(m);
            }
            s.maskST = new Vector4(st.x, st.y, off.x, off.y);
            Vector2 a = Vector2.Scale(min, st) + off, b = Vector2.Scale(max, st) + off;
            if (a.x < -0.01f || a.y < -0.01f || b.x > 1.01f || b.y > 1.01f)
                s_Warnings.Add($"{m.name}: part of the mesh's UV{s.uvChannel} falls outside the painted mask; those areas can't be painted. Clear the mask's tiling or use a new mask.");
        }

        // Copies each mesh with faces sharing UV space, unwraps it into UV1 (Unity's lightmap unwrap: every face unique),
        // and points the materials' masks at UV1. Carve's baked UV2/UV3 data is kept.
        public static void GenerateUniqueUVs()
        {
            var filters = s_SharedUV.Where(f => f != null && f.sharedMesh != null).ToList();
            var mats = s_Targets.Where(t => filters.Contains(t.renderer.GetComponent<MeshFilter>())).Select(t => t.session.material).Distinct().ToList();
            End();
            EnsureFolder(MeshFolder);
            var done = new Dictionary<Mesh, Mesh>();
            foreach (var mf in filters)
            {
                var src = mf.sharedMesh;
                if (!done.TryGetValue(src, out var copy))
                {
                    copy = Object.Instantiate(src);
                    copy.name = src.name + " (Carve UV1)";
                    Unwrapping.GenerateSecondaryUVSet(copy);
                    AssetDatabase.CreateAsset(copy, AssetDatabase.GenerateUniqueAssetPath($"{MeshFolder}/{src.name}_UV1.asset"));
                    done[src] = copy;
                }
                Undo.RecordObject(mf, "Generate Unique UVs");
                mf.sharedMesh = copy;
            }
            foreach (var m in mats)
            {
                Undo.RecordObject(m, "Generate Unique UVs");
                m.SetFloat("_CarveLayerMaskUV", 1f);
                m.SetTextureScale("_LayerMaskMap", Vector2.one);
                m.SetTextureOffset("_LayerMaskMap", Vector2.zero);
                EditorUtility.SetDirty(m);
            }
            AssetDatabase.SaveAssets();
            CarveProject.InvalidateScan();
            Begin();
        }

        // ---------------- Painting

        static void Draw(Session s, int pass, Target only = null, MaterialPropertyBlock extra = null)
        {
            var cmd = new CommandBuffer { name = "Carve Layer Paint" };
            cmd.SetRenderTarget(pass == 1 ? s.coverage : s.rt);
            var props = new MaterialPropertyBlock();
            foreach (var t in s_Targets)
            {
                if (t.session != s || (only != null && t != only)) continue;
                props.Clear();
                props.SetMatrix("_PaintObjectToWorld", t.renderer.localToWorldMatrix);
                props.SetFloat("_UVChannel", s.uvChannel);
                props.SetFloat("_FlipY", SystemInfo.graphicsUVStartsAtTop ? -1f : 1f);
                props.SetVector("_MaskST", s.maskST);
                props.SetTexture("_Prev", s.prev);
                foreach (var sub in t.submeshes) cmd.DrawMesh(t.mesh, Matrix4x4.identity, PaintMaterial, sub, pass, props);
            }
            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Release();
        }

        static void BuildCoverage(Session s)
        {
            var cmd = new CommandBuffer();
            cmd.SetRenderTarget(s.coverage);
            cmd.ClearRenderTarget(false, true, Color.clear);
            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Release();
            Draw(s, 1);
        }

        static void SetBrush(Vector3 center, Vector3 normal, float radius, float strength, Mode mode, int layer)
        {
            var m = PaintMaterial;
            m.SetVector("_Brush", new Vector4(center.x, center.y, center.z, radius));
            m.SetVector("_BrushNormal", new Vector4(normal.x, normal.y, normal.z, FrontOnly ? 1f : 0f));
            m.SetFloat("_Strength", strength);
            m.SetFloat("_Hardness", Hardness);
            m.SetFloat("_Mode", mode == Mode.Smooth ? 1f : 0f);
            // Layer k (1-3) lives in mask channel k-1 and covers the layers below it: keep lower channels, raise its own,
            // clear the ones above. Layer 0 (or Erase) clears everything.
            int k = mode == Mode.Erase ? 0 : layer;
            var keep = Vector4.zero;
            var set = Vector4.zero;
            for (int c = 0; c < 3; c++)
            {
                keep[c] = c < k - 1 ? 1f : 0f;
                set[c] = c == k - 1 ? 1f : 0f;
            }
            keep.w = 1f;
            m.SetVector("_Keep", keep);
            m.SetVector("_Set", set);
        }

        static void Stamp(Vector3 center, Vector3 normal, float pressure)
        {
            float radius = Size * 0.5f;
            SetBrush(center, normal, radius, Strength * pressure, Event.current != null && Event.current.shift ? Mode.Erase : BrushMode, Layer);
            foreach (var s in s_Sessions.Values)
            {
                bool any = false;
                foreach (var t in s_Targets)
                {
                    if (t.session != s || t.renderer.bounds.SqrDistance(center) > radius * radius) continue;
                    if (!any)
                    {
                        Graphics.CopyTexture(s.rt, s.prev);
                        any = true;
                    }
                    Draw(s, 0, t);
                }
                if (!any) continue;
                Graphics.CopyTexture(s.rt, 0, 0, s.mask, 0, 0);
                s.dirty = true;
                s_Touched.Add(s);
            }
        }

        // Scripting: one undoable stroke through world points on the painted objects, with the current brush settings.
        public static void Stroke(IList<Vector3> points, Vector3 normal, int layer, Mode mode = Mode.Paint)
        {
            if (s_Targets.Count == 0 || points.Count == 0) return;
            int l0 = Layer;
            var m0 = BrushMode;
            Layer = layer;
            BrushMode = mode;
            BeginStroke();
            Stamp(points[0], normal, 1f);
            float spacing = Mathf.Max(Size * 0.15f, 0.002f);
            for (int i = 1; i < points.Count; i++)
            {
                int n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(points[i - 1], points[i]) / spacing));
                for (int k = 1; k <= n; k++) Stamp(Vector3.Lerp(points[i - 1], points[i], (float)k / n), normal, 1f);
            }
            EndStroke("Paint Carve Layers");
            Layer = l0;
            BrushMode = m0;
        }

        // Whole objects at once (Fill / Clear): a brush big enough to cover every target.
        public static void FillAll(int layer)
        {
            BeginStroke();
            foreach (var s in s_Sessions.Values)
            {
                Graphics.CopyTexture(s.rt, s.prev);
                foreach (var t in s_Targets)
                {
                    if (t.session != s) continue;
                    var b = t.renderer.bounds;
                    SetBrush(b.center, Vector3.up, b.extents.magnitude * 2f + 1f, 1f, layer == 0 ? Mode.Erase : Mode.Paint, layer);
                    PaintMaterial.SetVector("_BrushNormal", Vector4.zero);
                    Draw(s, 0, t);
                    Graphics.CopyTexture(s.rt, s.prev);
                }
                s.dirty = true;
                s_Touched.Add(s);
            }
            EndStroke(layer == 0 ? "Clear Carve Layers" : "Fill Carve Layer");
        }

        static void BeginStroke()
        {
            foreach (var s in s_Sessions.Values) CarvePaintHistory.Remember(s.mask);
            s_Touched.Clear();
            s_Painting = true;
        }

        static void EndStroke(string undoName)
        {
            s_Painting = false;
            foreach (var s in s_Touched) Commit(s);
            CarvePaintHistory.Push(s_Touched.Select(s => s.mask), undoName);
            s_Touched.Clear();
        }

        // Fills the UV gutters, then writes the result into the mask asset (mips rebuilt).
        static void Commit(Session s)
        {
            var tmp = RenderTexture.GetTemporary(s.rt.width, s.rt.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            PaintMaterial.SetTexture("_Prev", s.rt);
            PaintMaterial.SetTexture("_Coverage", s.coverage);
            Graphics.Blit(s.rt, tmp, PaintMaterial, 2);
            Graphics.Blit(tmp, s.rt);
            var prev = RenderTexture.active;
            RenderTexture.active = tmp;
            s.mask.ReadPixels(new Rect(0, 0, tmp.width, tmp.height), 0, 0, false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(tmp);
            s.mask.Apply(true, false);
            EditorUtility.SetDirty(s.mask);
            s.dirty = false;
        }

        // Called by the history after Undo/Redo rewrote a mask's pixels.
        internal static void Reload(Texture2D mask)
        {
            foreach (var s in s_Sessions.Values)
                if (s.mask == mask) Graphics.Blit(mask, s.rt);
        }

        public static void Resize(int size)
        {
            Resolution = size;
            var mats = s_Sessions.Keys.ToList();
            End();
            foreach (var m in mats)
            {
                var old = m.GetTexture("_LayerMaskMap") as Texture2D;
                if (old == null || old.width == size) continue;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, true) { wrapMode = old.wrapMode, filterMode = old.filterMode };
                var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                Graphics.Blit(old, rt);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, size, size), 0, 0, false);
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                tex.Apply(true, false);
                tex.name = old.name;
                EditorUtility.CopySerialized(tex, old);
                Object.DestroyImmediate(tex);
                EditorUtility.SetDirty(old);
                AssetDatabase.SaveAssetIfDirty(old);
            }
            Begin();
        }

        public static void ExportPNG()
        {
            var m = CurrentMaterial;
            var mask = m != null ? m.GetTexture("_LayerMaskMap") as Texture2D : null;
            if (mask == null) return;
            string path = EditorUtility.SaveFilePanelInProject("Export Layer Mask", mask.name, "png", "Save the painted layer mask as a PNG.");
            if (string.IsNullOrEmpty(path)) return;
            File.WriteAllBytes(path, mask.EncodeToPNG());
            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter ti) { ti.sRGBTexture = false; ti.SaveAndReimport(); }
        }

        // ---------------- Scene view

        class MeshCache
        {
            public Vector3[] vertices, normals;
            public int[] triangles;
        }

        static readonly Dictionary<Mesh, MeshCache> s_MeshCache = new Dictionary<Mesh, MeshCache>();

        static MeshCache GetCache(Mesh mesh)
        {
            if (s_MeshCache.TryGetValue(mesh, out var c)) return c;
            c = new MeshCache();
            try
            {
                c.vertices = mesh.vertices;
                c.normals = mesh.normals;
                c.triangles = mesh.triangles;
            }
            catch
            {
                c.vertices = new Vector3[0];
                c.triangles = new int[0];
            }
            s_MeshCache[mesh] = c;
            return c;
        }

        // Closest hit of the mouse ray on the painted objects.
        static bool Raycast(Ray ray, out RaycastHit best)
        {
            best = default;
            bool found = false;
            foreach (var t in s_Targets.GroupBy(t => t.renderer).Select(g => g.First()))
            {
                if (!t.renderer.bounds.IntersectRay(ray)) continue;
                if (RaycastMesh(t.mesh, t.renderer.localToWorldMatrix, ray, out var hit) && (!found || hit.distance < best.distance))
                {
                    best = hit;
                    found = true;
                }
            }
            return found;
        }

        // Two-sided ray/mesh test in the mesh's local space (no colliders needed); the normal faces the ray.
        public static bool RaycastMesh(Mesh mesh, Matrix4x4 localToWorld, Ray ray, out RaycastHit hit)
        {
            hit = default;
            var c = GetCache(mesh);
            var w2l = localToWorld.inverse;
            Vector3 o = w2l.MultiplyPoint3x4(ray.origin), d = w2l.MultiplyVector(ray.direction);
            var v = c.vertices;
            var tri = c.triangles;
            float tBest = float.MaxValue, uBest = 0f, vBest = 0f;
            int iBest = -1;
            for (int i = 0; i < tri.Length; i += 3)
            {
                Vector3 a = v[tri[i]], e1 = v[tri[i + 1]] - a, e2 = v[tri[i + 2]] - a;
                Vector3 p = Vector3.Cross(d, e2);
                float det = Vector3.Dot(e1, p);
                if (det > -1e-12f && det < 1e-12f) continue;
                float inv = 1f / det;
                Vector3 s = o - a;
                float u = Vector3.Dot(s, p) * inv;
                if (u < 0f || u > 1f) continue;
                Vector3 q = Vector3.Cross(s, e1);
                float w = Vector3.Dot(d, q) * inv;
                if (w < 0f || u + w > 1f) continue;
                float tt = Vector3.Dot(e2, q) * inv;
                if (tt > 0f && tt < tBest) { tBest = tt; iBest = i; uBest = u; vBest = w; }
            }
            if (iBest < 0) return false;
            Vector3 point = localToWorld.MultiplyPoint3x4(o + d * tBest);
            Vector3 n = c.normals != null && c.normals.Length == v.Length
                ? c.normals[tri[iBest]] * (1f - uBest - vBest) + c.normals[tri[iBest + 1]] * uBest + c.normals[tri[iBest + 2]] * vBest
                : Vector3.Cross(v[tri[iBest + 1]] - v[tri[iBest]], v[tri[iBest + 2]] - v[tri[iBest]]);
            n = w2l.transpose.MultiplyVector(n).normalized;
            if (Vector3.Dot(n, ray.direction) > 0f) n = -n;
            hit = new RaycastHit { point = point, normal = n, distance = Vector3.Distance(ray.origin, point) };
            return true;
        }

        public static void OnSceneGUI(SceneView view)
        {
            var e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            if (s_Targets.Count == 0) return;
            // Only take the mouse while it is over a paintable object, so clicking elsewhere still selects.
            if (e.type == EventType.Layout && (s_Hit || s_Painting)) HandleUtility.AddDefaultControl(id);

            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode >= KeyCode.Alpha1 && e.keyCode <= KeyCode.Alpha4 && !e.control && !e.alt)
                {
                    int l = e.keyCode - KeyCode.Alpha1;
                    if (l < CarveProject.LayerCount(CurrentMaterial)) Layer = l;
                    e.Use();
                }
                else if (e.keyCode == KeyCode.LeftBracket) { Size /= 1.15f; e.Use(); }
                else if (e.keyCode == KeyCode.RightBracket) { Size *= 1.15f; e.Use(); }
            }

            if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag || e.type == EventType.MouseDown || e.type == EventType.Repaint)
                s_Hit = !e.alt && Raycast(HandleUtility.GUIPointToWorldRay(e.mousePosition), out s_HitInfo);

            float pressure = e.pointerType == PointerType.Pen ? Mathf.Max(e.pressure, 0.05f) : 1f;
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt && s_Hit)
            {
                GUIUtility.hotControl = id;
                BeginStroke();
                Stamp(s_HitInfo.point, s_HitInfo.normal, pressure);
                s_LastStamp = s_HitInfo.point;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && GUIUtility.hotControl == id && s_Painting)
            {
                if (s_Hit)
                {
                    // Stamps every 15% of the brush along the drag, so fast strokes stay continuous.
                    float spacing = Mathf.Max(Size * 0.15f, 0.002f);
                    float dist = Vector3.Distance(s_LastStamp, s_HitInfo.point);
                    int n = Mathf.Min(Mathf.FloorToInt(dist / spacing), 64);
                    for (int i = 1; i <= n; i++)
                        Stamp(Vector3.Lerp(s_LastStamp, s_HitInfo.point, (float)i / n), s_HitInfo.normal, pressure);
                    if (n > 0) s_LastStamp = s_HitInfo.point;
                }
                e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 0 && GUIUtility.hotControl == id)
            {
                GUIUtility.hotControl = 0;
                if (s_Painting) EndStroke("Paint Carve Layers");
                e.Use();
            }

            if (e.type == EventType.Repaint && s_Hit)
            {
                bool erase = e.shift || BrushMode == Mode.Erase;
                var c = BrushMode == Mode.Smooth ? Color.white : LayerColors[erase ? 0 : Layer];
                float r = Size * 0.5f;
                Handles.color = c;
                Handles.DrawWireDisc(s_HitInfo.point, s_HitInfo.normal, r, 2f);
                Handles.color = new Color(c.r, c.g, c.b, 0.5f);
                Handles.DrawWireDisc(s_HitInfo.point, s_HitInfo.normal, r * Hardness);
                Handles.color = new Color(c.r, c.g, c.b, 0.12f);
                Handles.DrawSolidDisc(s_HitInfo.point, s_HitInfo.normal, r);
            }
            if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag) view.Repaint();
        }

        // Projects are saved with the mask's current pixels (a stroke in progress is finished first).
        class SaveHook : AssetModificationProcessor
        {
            static string[] OnWillSaveAssets(string[] paths)
            {
                foreach (var s in s_Sessions.Values)
                    if (s.dirty) Commit(s);
                return paths;
            }
        }

        [InitializeOnLoadMethod]
        static void Hooks()
        {
            AssemblyReloadEvents.beforeAssemblyReload += () => { foreach (var s in s_Sessions.Values) if (s.dirty) Commit(s); };
        }
    }

    [Overlay(typeof(SceneView), "carve-layer-paint", "Carve Layer Paint")]
    class CarveLayerPaintOverlay : IMGUIOverlay, ITransientOverlay
    {
        static readonly string[] Modes = { "Paint", "Erase", "Smooth" };
        bool m_MaskFold;

        public bool visible => CarveLayerPaintTool.Active;

        // Dark, 60% see-through panel so the surface being painted stays visible behind it.
        public override void OnCreated()
        {
            var root = rootVisualElement;
            root.style.backgroundColor = new Color(0.07f, 0.07f, 0.07f, 0.6f);
            root.schedule.Execute(() =>
            {
                foreach (var e in UnityEngine.UIElements.UQueryExtensions.Query<UnityEngine.UIElements.VisualElement>(root).ToList())
                    if (e != root && e.resolvedStyle.backgroundColor.a > 0f) e.style.backgroundColor = Color.clear;
            });
        }

        public override void OnGUI()
        {
            var m = CarveLayerPainter.CurrentMaterial;
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(260)))
            {
                if (m == null)
                {
                    EditorGUILayout.HelpBox("Select objects that use a Carve Layered Lit material.", MessageType.Info);
                    return;
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(new GUIContent($"{CarveLayerPainter.TargetNames}", m.name), EditorStyles.boldLabel);
                    if (GUILayout.Button(new GUIContent("Done", "Stop painting (back to the Move tool)."), GUILayout.Width(50))) UnityEditor.Tools.current = Tool.Move;
                }

                int count = CarveProject.LayerCount(m);
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int i = 0; i < count; i++)
                    {
                        var rect = GUILayoutUtility.GetRect(56, 56, GUILayout.Width(56), GUILayout.Height(56));
                        bool selected = CarveLayerPainter.Layer == i && CarveLayerPainter.BrushMode == CarveLayerPainter.Mode.Paint;
                        if (selected) EditorGUI.DrawRect(rect, CarveLayerPainter.LayerColors[i]);
                        var inner = new Rect(rect.x + 3, rect.y + 3, rect.width - 6, rect.height - 6);
                        var tex = m.GetTexture("_BaseColorMap" + i);
                        if (tex != null) GUI.DrawTexture(inner, tex, ScaleMode.ScaleAndCrop, false, 0, m.GetColor("_BaseColor" + i), 0, 0);
                        else EditorGUI.DrawRect(inner, m.GetColor("_BaseColor" + i));
                        GUI.Label(new Rect(inner.x + 2, inner.y + 1, 30, 16), (i + 1).ToString(), EditorStyles.whiteBoldLabel);
                        if (GUI.Button(rect, new GUIContent("", i == 0 ? "Layer 0 (base). Key 1." : $"Layer {i}. Key {i + 1}."), GUIStyle.none))
                        {
                            CarveLayerPainter.Layer = i;
                            CarveLayerPainter.BrushMode = CarveLayerPainter.Mode.Paint;
                        }
                    }
                }

                CarveLayerPainter.BrushMode = (CarveLayerPainter.Mode)GUILayout.Toolbar((int)CarveLayerPainter.BrushMode, Modes);
                float lw = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 80;
                CarveLayerPainter.Size = EditorGUILayout.Slider(new GUIContent("Size (m)", "Brush diameter in meters. [ and ] change it."), CarveLayerPainter.Size, 0.02f, 10f);
                CarveLayerPainter.Strength = EditorGUILayout.Slider(new GUIContent("Strength", "How much each dab moves toward the layer. Pen pressure scales it."), CarveLayerPainter.Strength, 0.01f, 1f);
                CarveLayerPainter.Hardness = EditorGUILayout.Slider(new GUIContent("Hardness", "0 = soft edge (the relief decides the boundary), 1 = hard edge."), CarveLayerPainter.Hardness, 0f, 1f);
                CarveLayerPainter.FrontOnly = EditorGUILayout.Toggle(new GUIContent("Front faces", "Only paint surfaces facing the brush, not the far side of thin objects."), CarveLayerPainter.FrontOnly);
                EditorGUIUtility.labelWidth = lw;

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent("Fill", "Fill the selected objects with the selected layer."))) CarveLayerPainter.FillAll(CarveLayerPainter.Layer);
                    if (GUILayout.Button(new GUIContent("Clear", "Back to layer 0 everywhere."))) CarveLayerPainter.FillAll(0);
                    bool layersView = CarveGlobals.DebugView == CarveDebugView.Layers;
                    bool now = GUILayout.Toggle(layersView, new GUIContent("Layers View", "Show each layer as a flat color while painting."), "Button");
                    if (now != layersView) CarveGlobals.DebugView = now ? CarveDebugView.Layers : CarveDebugView.Off;
                }

                m_MaskFold = EditorGUILayout.Foldout(m_MaskFold, "Mask", true);
                if (m_MaskFold)
                {
                    var mask = m.GetTexture("_LayerMaskMap");
                    EditorGUILayout.ObjectField(mask, typeof(Texture2D), false);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        int cur = mask != null ? mask.width : CarveLayerPainter.Resolution;
                        int pick = EditorGUILayout.IntPopup(cur, CarveLayerPainter.Resolutions.Select(r => r.ToString()).ToArray(), CarveLayerPainter.Resolutions);
                        if (pick != cur) CarveLayerPainter.Resize(pick);
                        if (GUILayout.Button("Export PNG")) CarveLayerPainter.ExportPNG();
                    }
                }

                foreach (var w in CarveLayerPainter.Warnings) EditorGUILayout.HelpBox(w, MessageType.Warning);
                if (CarveLayerPainter.HasSharedUVs && GUILayout.Button(new GUIContent("Generate Unique UVs", "Copies the mesh, unwraps it into UV1 so every face has its own paint, and switches the mask to UV1. Paint again afterwards.")))
                    CarveLayerPainter.GenerateUniqueUVs();
                if (!CarveLayerPainter.BrushOverTarget)
                    EditorGUILayout.LabelField("Move the brush over a selected object to paint.", EditorStyles.centeredGreyMiniLabel);
                EditorGUILayout.LabelField("Drag to paint · Shift erase · 1-4 layer · [ ] size", EditorStyles.centeredGreyMiniLabel);
            }
        }
    }
}
