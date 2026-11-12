using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace JamzeySt.Carve.Editor
{
    // Relief -> real geometry: subdivides a mesh until no edge is longer than the spacing, evaluates the Carve
    // material's relief at every vertex on the GPU (the shaders' own height code: layers, anti-tiling, detail,
    // triplanar), and pushes the vertices in. For colliders (physics on the carved surface), LODs and export.
    public static class CarveReliefBaker
    {
        public const string Folder = "Assets/Carve Generated/Relief";
        const string Prefs = "JamzeySt.Carve.Relief.";
        const int MaxVertices = 2000000;

        public static float MeshSpacing { get => EditorPrefs.GetFloat(Prefs + "Mesh", 0.02f); set => EditorPrefs.SetFloat(Prefs + "Mesh", Mathf.Clamp(value, 0.002f, 1f)); }
        public static float ColliderSpacing { get => EditorPrefs.GetFloat(Prefs + "Collider", 0.05f); set => EditorPrefs.SetFloat(Prefs + "Collider", Mathf.Clamp(value, 0.005f, 2f)); }

        public static bool CanBake(MeshRenderer r) =>
            r != null && r.GetComponent<MeshFilter>()?.sharedMesh != null && r.sharedMaterials.Any(CarveProject.IsCarve);

        [MenuItem("GameObject/Carve/Add Relief Collider", false, 30)]
        static void MenuCollider() => AddColliders(SelectedRenderers());

        [MenuItem("GameObject/Carve/Bake Relief Mesh", false, 31)]
        static void MenuMesh() => BakeMeshes(SelectedRenderers(), true);

        [MenuItem("GameObject/Carve/Add Relief Collider", true)]
        [MenuItem("GameObject/Carve/Bake Relief Mesh", true)]
        static bool MenuValid() => SelectedRenderers().Count > 0;

        public static List<MeshRenderer> SelectedRenderers() =>
            Selection.gameObjects.SelectMany(g => g.GetComponentsInChildren<MeshRenderer>()).Where(CanBake).Distinct().ToList();

        // A MeshCollider with the carved surface on each object (physics objects rest in the grooves).
        public static int AddColliders(IList<MeshRenderer> renderers)
        {
            int n = 0;
            foreach (var r in renderers)
            {
                var mesh = Bake(r, ColliderSpacing, "Collider", out string error);
                if (mesh == null) { Debug.LogWarning($"[Carve] {r.name}: {error}"); continue; }
                var col = r.GetComponent<MeshCollider>();
                if (col == null) col = Undo.AddComponent<MeshCollider>(r.gameObject);
                else Undo.RecordObject(col, "Carve Relief Collider");
                col.sharedMesh = mesh;
                foreach (var other in r.GetComponents<Collider>())
                    if (other != col && other.enabled) { Undo.RecordObject(other, "Carve Relief Collider"); other.enabled = false; }
                Debug.Log($"[Carve] {r.name}: relief collider, {mesh.vertexCount:N0} vertices ({AssetDatabase.GetAssetPath(mesh)}). Other colliders on it were disabled.");
                n++;
            }
            return n;
        }

        // A displaced mesh asset per object; optionally a sibling object showing it with a plain Lit copy of the material.
        public static int BakeMeshes(IList<MeshRenderer> renderers, bool createObjects)
        {
            int n = 0;
            foreach (var r in renderers)
            {
                var mesh = Bake(r, MeshSpacing, "Mesh", out string error);
                if (mesh == null) { Debug.LogWarning($"[Carve] {r.name}: {error}"); continue; }
                n++;
                Debug.Log($"[Carve] {r.name}: relief mesh, {mesh.vertexCount:N0} vertices ({AssetDatabase.GetAssetPath(mesh)}).");
                if (!createObjects) continue;
                var go = new GameObject(r.name + " (Relief Mesh)");
                Undo.RegisterCreatedObjectUndo(go, "Carve Relief Mesh");
                go.transform.SetParent(r.transform.parent, false);
                go.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation);
                go.transform.localScale = r.transform.localScale;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = r.sharedMaterials.Select(FlatCopy).ToArray();
                Undo.RecordObject(r.gameObject, "Carve Relief Mesh");
                r.gameObject.SetActive(false);
                Selection.activeGameObject = go;
            }
            return n;
        }

        // The relief is now geometry, so the copy keeps the material's own texturing (anti-tiling, layers, triplanar)
        // with depth 0 and the relief-only effects off: nothing is carved twice.
        static Material FlatCopy(Material m)
        {
            if (!CarveProject.IsCarve(m)) return m;
            EnsureFolder(Folder);
            string path = $"{Folder}/{m.name}_Baked.mat";
            var copy = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (copy == null)
            {
                copy = new Material(m);
                AssetDatabase.CreateAsset(copy, path);
            }
            else
                copy.CopyPropertiesFromMaterial(m);
            foreach (var p in new[] { "_CarveDepth", "_CarveDetailDepth", "_CarveSilhouette", "_CarveCurvature", "_CarveSelfShadow", "_CarveOcclusion", "_CarveSpecOcclusion", "_CarveCliff" })
                if (copy.HasProperty(p)) copy.SetFloat(p, 0f);
            CarveMaterialDrawer.FixSetup?.Invoke(copy);
            EditorUtility.SetDirty(copy);
            return copy;
        }

        public static Mesh Bake(MeshRenderer r, float spacing, string kind, out string error)
        {
            error = null;
            var src = r.GetComponent<MeshFilter>().sharedMesh;
            var l2w = r.transform.localToWorldMatrix;
            var mats = r.sharedMaterials;

            // Rough vertex estimate from the surface area, before doing any work.
            var v0 = src.vertices;
            var t0 = src.triangles;
            double area = 0;
            for (int i = 0; i < t0.Length; i += 3)
                area += Vector3.Cross(l2w.MultiplyPoint3x4(v0[t0[i + 1]]) - l2w.MultiplyPoint3x4(v0[t0[i]]), l2w.MultiplyPoint3x4(v0[t0[i + 2]]) - l2w.MultiplyPoint3x4(v0[t0[i]])).magnitude * 0.5;
            double estimate = area / (spacing * spacing * 0.433) * 0.5 + v0.Length;
            if (estimate > MaxVertices)
            {
                error = $"about {estimate:N0} vertices at {spacing * 100f:0.#} cm spacing (limit {MaxVertices:N0}). Use a larger spacing.";
                return null;
            }

            var mesh = new Subdivider(src, l2w, spacing).Run();
            var heights = new float[mesh.vertices.Count];
            var depths = new float[mesh.vertices.Count];
            for (int sub = 0; sub < mesh.submeshes.Count; sub++)
            {
                var m = sub < mats.Length ? mats[sub] : null;
                if (!CarveProject.IsCarve(m)) continue;
                var idx = mesh.submeshes[sub].Distinct().ToArray();
                var h = Evaluate(m, r.transform, mesh, idx, out error);
                if (h == null) return null;
                float depth = m.GetFloat("_CarveDepth") + (m.HasProperty("_CarveDetail") && m.GetFloat("_CarveDetail") > 0.5f ? m.GetFloat("_CarveDetailDepth") : 0f);
                for (int i = 0; i < idx.Length; i++)
                {
                    heights[idx[i]] = float.IsFinite(h[i]) ? Mathf.Clamp01(h[i]) : 1f;
                    depths[idx[i]] = depth;
                }
            }

            var w2l = l2w.inverse;
            var pos = new Vector3[mesh.vertices.Count];
            for (int i = 0; i < pos.Length; i++)
            {
                Vector3 p = mesh.vertices[i] - mesh.normals[i] * ((1f - heights[i]) * depths[i]);
                pos[i] = w2l.MultiplyPoint3x4(p);
            }
            // Re-bakes fill the existing asset through the Mesh API, so colliders and objects using it keep their
            // reference and the GPU copy is refreshed.
            EnsureFolder(Folder);
            string path = $"{Folder}/{r.name}_{src.name}_Relief{kind}.asset";
            var outMesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool created = outMesh == null;
            if (created) outMesh = new Mesh();
            outMesh.Clear();
            outMesh.name = Path.GetFileNameWithoutExtension(path);
            outMesh.indexFormat = pos.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            outMesh.vertices = pos;
            outMesh.SetUVs(0, mesh.uv0);
            outMesh.SetUVs(1, mesh.uv1);
            if (mesh.hasColor) outMesh.SetColors(mesh.colors);
            outMesh.subMeshCount = mesh.submeshes.Count;
            for (int s = 0; s < mesh.submeshes.Count; s++) outMesh.SetTriangles(mesh.submeshes[s], s);
            outMesh.RecalculateNormals();
            outMesh.RecalculateTangents();
            outMesh.RecalculateBounds();
            if (created) AssetDatabase.CreateAsset(outMesh, path);
            else EditorUtility.SetDirty(outMesh);
            AssetDatabase.SaveAssets();
            return outMesh;
        }

        // ---------------- GPU height evaluation

        static float[] Evaluate(Material m, Transform t, Subdivider.Result mesh, int[] idx, out string error)
        {
            error = null;
            bool layered = CarveProject.IsLayered(m);
            var cs = Find(layered ? "CarveReliefBakeLayered" : "CarveReliefBake");
            if (cs == null) { error = "relief bake compute shader not found."; return null; }
            int kernel = cs.FindKernel("Bake");
            if (!cs.IsSupported(kernel)) { error = $"{cs.name} failed to compile (see the Console)."; return null; }
            int n = idx.Length;
            var uv = new Vector4[n];
            var pos = new Vector4[n];
            var nrm = new Vector4[n];
            var col = new Vector4[n];
            for (int i = 0; i < n; i++)
            {
                int k = idx[i];
                uv[i] = new Vector4(mesh.uv0[k].x, mesh.uv0[k].y, mesh.uv1[k].x, mesh.uv1[k].y);
                pos[i] = mesh.vertices[k];
                nrm[i] = mesh.normals[k];
                col[i] = mesh.hasColor ? (Vector4)mesh.colors[k] : Vector4.one;
            }
            var buffers = new[] { new ComputeBuffer(n, 16), new ComputeBuffer(n, 16), new ComputeBuffer(n, 16), new ComputeBuffer(n, 16), new ComputeBuffer(n, 4) };
            try
            {
                buffers[0].SetData(uv);
                buffers[1].SetData(pos);
                buffers[2].SetData(nrm);
                buffers[3].SetData(col);
                cs.SetBuffer(kernel, "_BakeUV", buffers[0]);
                cs.SetBuffer(kernel, "_BakePos", buffers[1]);
                cs.SetBuffer(kernel, "_BakeNrm", buffers[2]);
                cs.SetBuffer(kernel, "_BakeCol", buffers[3]);
                cs.SetBuffer(kernel, "_BakeOut", buffers[4]);
                cs.SetInt("_BakeCount", n);
                // Triplanar Object projects in the object's rotation from its origin; World uses identity.
                bool objectSpace = m.HasProperty("_CarveMapping") && Mathf.RoundToInt(m.GetFloat("_CarveMapping")) == 1;
                cs.SetMatrix("_BakeObjRot", objectSpace ? Matrix4x4.Rotate(t.rotation) : Matrix4x4.identity);
                cs.SetVector("_BakeObjOrigin", objectSpace ? (Vector4)t.position : Vector4.zero);
                BindMaterial(cs, kernel, m, layered);
                cs.Dispatch(kernel, (n + 63) / 64, 1, 1);
                var h = new float[n];
                buffers[4].GetData(h);
                return h;
            }
            finally
            {
                foreach (var b in buffers) b.Release();
            }
        }

        // Every property the bake code reads, from the material (missing textures as Unity's white default, like the shaders).
        static void BindMaterial(ComputeShader cs, int kernel, Material m, bool layered)
        {
            var shader = m.shader;
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                string name = shader.GetPropertyName(i);
                switch (shader.GetPropertyType(i))
                {
                    case ShaderPropertyType.Texture:
                        var tex = m.GetTexture(name) ?? Texture2D.whiteTexture;
                        cs.SetTexture(kernel, name, tex);
                        cs.SetVector(name + "_TexelSize", new Vector4(1f / tex.width, 1f / tex.height, tex.width, tex.height));
                        Vector2 s = m.GetTextureScale(name), o = m.GetTextureOffset(name);
                        cs.SetVector(name + "_ST", new Vector4(s.x, s.y, o.x, o.y));
                        break;
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range:
                        cs.SetFloat(name, m.GetFloat(name));
                        break;
                    case ShaderPropertyType.Color:
                    case ShaderPropertyType.Vector:
                        cs.SetVector(name, m.GetVector(name));
                        break;
                }
            }
        }

        static ComputeShader Find(string name)
        {
            foreach (var guid in AssetDatabase.FindAssets(name + " t:ComputeShader"))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(p) == name) return AssetDatabase.LoadAssetAtPath<ComputeShader>(p);
            }
            return null;
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        // ---------------- Crack-free subdivision

        // Splits every edge longer than the spacing at its midpoint (shared through an edge cache), then re-triangulates
        // each triangle by how many of its edges were split; repeats until every edge fits. Whether an edge splits
        // depends only on the edge, so neighbouring triangles always agree and the displaced mesh has no cracks.
        class Subdivider
        {
            public class Result
            {
                public List<Vector3> vertices = new List<Vector3>(), normals = new List<Vector3>();
                public List<Vector2> uv0 = new List<Vector2>(), uv1 = new List<Vector2>();
                public List<Color> colors = new List<Color>();
                public bool hasColor;
                public List<List<int>> submeshes = new List<List<int>>();
            }

            readonly Result m_R = new Result();
            readonly float m_Spacing2;
            readonly Dictionary<long, int> m_Mid = new Dictionary<long, int>();

            public Subdivider(Mesh src, Matrix4x4 l2w, float spacing)
            {
                m_Spacing2 = spacing * spacing;
                var v = src.vertices;
                var n = src.normals;
                var uv0 = new List<Vector2>();
                var uv1 = new List<Vector2>();
                src.GetUVs(0, uv0);
                src.GetUVs(1, uv1);
                var col = src.colors;
                m_R.hasColor = col != null && col.Length == v.Length;
                var nm = l2w.inverse.transpose;
                for (int i = 0; i < v.Length; i++)
                {
                    m_R.vertices.Add(l2w.MultiplyPoint3x4(v[i]));
                    m_R.normals.Add(n.Length == v.Length ? nm.MultiplyVector(n[i]).normalized : Vector3.up);
                    m_R.uv0.Add(i < uv0.Count ? uv0[i] : Vector2.zero);
                    m_R.uv1.Add(i < uv1.Count ? uv1[i] : Vector2.zero);
                    m_R.colors.Add(m_R.hasColor ? col[i] : Color.white);
                }
                for (int s = 0; s < src.subMeshCount; s++) m_R.submeshes.Add(new List<int>(src.GetTriangles(s)));
            }

            public Result Run()
            {
                for (int pass = 0; pass < 24; pass++)
                {
                    bool any = false;
                    for (int s = 0; s < m_R.submeshes.Count; s++)
                    {
                        var tris = m_R.submeshes[s];
                        var next = new List<int>(tris.Count * 2);
                        for (int i = 0; i < tris.Count; i += 3)
                            any |= Split(tris[i], tris[i + 1], tris[i + 2], next);
                        m_R.submeshes[s] = next;
                    }
                    if (!any) break;
                }
                return m_R;
            }

            bool Long(int a, int b) => (m_R.vertices[a] - m_R.vertices[b]).sqrMagnitude > m_Spacing2;

            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (m_Mid.TryGetValue(key, out int m)) return m;
                m = m_R.vertices.Count;
                m_R.vertices.Add((m_R.vertices[a] + m_R.vertices[b]) * 0.5f);
                m_R.normals.Add((m_R.normals[a] + m_R.normals[b]).normalized);
                m_R.uv0.Add((m_R.uv0[a] + m_R.uv0[b]) * 0.5f);
                m_R.uv1.Add((m_R.uv1[a] + m_R.uv1[b]) * 0.5f);
                m_R.colors.Add((m_R.colors[a] + m_R.colors[b]) * 0.5f);
                m_Mid[key] = m;
                return m;
            }

            static void Add(List<int> o, int a, int b, int c) { o.Add(a); o.Add(b); o.Add(c); }

            bool Split(int a, int b, int c, List<int> o)
            {
                bool ab = Long(a, b), bc = Long(b, c), ca = Long(c, a);
                int count = (ab ? 1 : 0) + (bc ? 1 : 0) + (ca ? 1 : 0);
                if (count == 0) { Add(o, a, b, c); return false; }
                if (count == 3)
                {
                    int x = Mid(a, b), y = Mid(b, c), z = Mid(c, a);
                    Add(o, a, x, z); Add(o, x, b, y); Add(o, z, y, c); Add(o, x, y, z);
                    return true;
                }
                // Rotate so the pattern starts at a: one split edge (a-b), or two (a-b and b-c).
                if (count == 1)
                {
                    if (bc) { (a, b, c) = (b, c, a); }
                    else if (ca) { (a, b, c) = (c, a, b); }
                    int x = Mid(a, b);
                    Add(o, a, x, c); Add(o, x, b, c);
                    return true;
                }
                if (!ab) { (a, b, c) = (b, c, a); }
                else if (!bc) { (a, b, c) = (c, a, b); }
                int m1 = Mid(a, b), m2 = Mid(b, c);
                Add(o, m1, b, m2);
                // Split the remaining quad (a, m1, m2, c) along its shorter diagonal.
                if ((m_R.vertices[a] - m_R.vertices[m2]).sqrMagnitude < (m_R.vertices[m1] - m_R.vertices[c]).sqrMagnitude)
                { Add(o, a, m1, m2); Add(o, a, m2, c); }
                else
                { Add(o, a, m1, c); Add(o, m1, m2, c); }
                return true;
            }
        }
    }
}
