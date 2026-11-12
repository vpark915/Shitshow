using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace JamzeySt.Carve.Editor
{
    // Bakes silhouette data into a mesh copy: UV2 = UV rect of each face (island), UV3 = principal curvature (kT, kB, kTB).
    public static class CarveMeshBaker
    {
        public const int BoundsChannel = 2;
        public const int CurvatureChannel = 3;
        public const string DefaultFolder = "Assets/Carve Generated/Meshes";
        const float Open = 1e5f;

        [MenuItem("Tools/Carve/Bake Silhouette Data (Selected)")]
        static void BakeSelected()
        {
            var done = new Dictionary<Mesh, Mesh>();
            foreach (var go in Selection.gameObjects)
            {
                var mf = go.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                if (!done.TryGetValue(mf.sharedMesh, out var baked))
                {
                    baked = BakeToAsset(mf.sharedMesh);
                    done[mf.sharedMesh] = baked;
                }
                Undo.RecordObject(mf, "Bake Carve Silhouette Data");
                mf.sharedMesh = baked;
            }
            Debug.Log($"[Carve] Baked {done.Count} mesh(es) into {DefaultFolder}.");
        }

        [MenuItem("Tools/Carve/Bake Silhouette Data (Selected)", true)]
        static bool BakeSelectedValid()
        {
            foreach (var go in Selection.gameObjects)
                if (go.GetComponent<MeshFilter>() != null) return true;
            return false;
        }

        public static Mesh BakeToAsset(Mesh source, string folder = DefaultFolder)
        {
            string name = source.name.EndsWith(" (Carve)") ? source.name : source.name + " (Carve)";
            string path = $"{folder}/{name}.asset";
            var mesh = Object.Instantiate(source);
            mesh.name = name;
            Bake(mesh);
            Directory.CreateDirectory(folder);
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            if (existing == source)
            {
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(mesh);
                Bake(existing);
                return existing;
            }
            EditorUtility.CopySerialized(mesh, existing);
            Object.DestroyImmediate(mesh);
            return existing;
        }

        public static void Bake(Mesh mesh)
        {
            if (mesh.tangents.Length != mesh.vertexCount) mesh.RecalculateTangents();
            var pos = mesh.vertices;
            var nrm = mesh.normals;
            var tan = mesh.tangents;
            var uv = new List<Vector2>();
            mesh.GetUVs(0, uv);
            while (uv.Count < mesh.vertexCount) uv.Add(Vector2.zero);
            var tris = mesh.triangles;
            int n = pos.Length;

            var group = WeldGroups(pos, nrm, out int groupCount);
            mesh.SetUVs(BoundsChannel, IslandBounds(tris, pos, uv, group, n));
            mesh.SetUVs(CurvatureChannel, Curvature(tris, pos, nrm, tan, group, groupCount));
        }

        // Vertices sharing position and normal form one group (welds UV seams, keeps hard edges apart).
        static int[] WeldGroups(Vector3[] pos, Vector3[] nrm, out int count)
        {
            var map = new Dictionary<(int, int, int, int, int, int), int>();
            var group = new int[pos.Length];
            for (int i = 0; i < pos.Length; i++)
            {
                var p = pos[i] * 1e4f;
                var q = nrm[i] * 100f;
                var key = (Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y), Mathf.RoundToInt(p.z), Mathf.RoundToInt(q.x), Mathf.RoundToInt(q.y), Mathf.RoundToInt(q.z));
                if (!map.TryGetValue(key, out int g)) map[key] = g = map.Count;
                group[i] = g;
            }
            count = map.Count;
            return group;
        }

        static int Find(int[] parent, int i)
        {
            while (parent[i] != i) i = parent[i] = parent[parent[i]];
            return i;
        }

        static List<Vector4> IslandBounds(int[] tris, Vector3[] pos, List<Vector2> uv, int[] group, int n)
        {
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            for (int t = 0; t < tris.Length; t += 3)
            {
                int a = Find(parent, tris[t]);
                parent[Find(parent, tris[t + 1])] = a;
                parent[Find(parent, tris[t + 2])] = a;
            }
            var sameUV = new Dictionary<(Vector3Int, Vector2Int), int>();
            for (int i = 0; i < n; i++)
            {
                var key = (Vector3Int.RoundToInt(pos[i] * 1e4f), Vector2Int.RoundToInt(uv[i] * 1e4f));
                if (sameUV.TryGetValue(key, out int j)) parent[Find(parent, i)] = Find(parent, j);
                else sameUV[key] = i;
            }
            var min = new Dictionary<int, Vector2>();
            var max = new Dictionary<int, Vector2>();
            for (int i = 0; i < n; i++)
            {
                int r = Find(parent, i);
                min[r] = min.TryGetValue(r, out var m0) ? Vector2.Min(m0, uv[i]) : uv[i];
                max[r] = max.TryGetValue(r, out var m1) ? Vector2.Max(m1, uv[i]) : uv[i];
            }

            // A side of an island is a seam, not an edge, when a vertex on it has a twin with the same position and normal but other UVs.
            var mates = new Dictionary<int, List<int>>();
            for (int i = 0; i < n; i++)
            {
                if (!mates.TryGetValue(group[i], out var list)) mates[group[i]] = list = new List<int>();
                list.Add(i);
            }
            var open = new Dictionary<int, int>();
            for (int i = 0; i < n; i++)
            {
                bool diffU = false, diffV = false;
                foreach (int j in mates[group[i]])
                {
                    diffU |= Mathf.Abs(uv[j].x - uv[i].x) > 1e-4f;
                    diffV |= Mathf.Abs(uv[j].y - uv[i].y) > 1e-4f;
                }
                if (!diffU && !diffV) continue;
                int r = Find(parent, i);
                Vector2 lo = min[r], hi = max[r];
                float eps = Mathf.Max((hi - lo).magnitude * 1e-3f, 1e-6f);
                int u = (Mathf.Abs(uv[i].x - lo.x) < eps ? 1 : 0) | (Mathf.Abs(uv[i].x - hi.x) < eps ? 2 : 0);
                int v = (Mathf.Abs(uv[i].y - lo.y) < eps ? 4 : 0) | (Mathf.Abs(uv[i].y - hi.y) < eps ? 8 : 0);
                // At a corner, only the side across which the UVs jump is a seam.
                if (u != 0 && v != 0)
                {
                    if (!diffU) u = 0;
                    if (!diffV) v = 0;
                }
                open[r] = (open.TryGetValue(r, out int o) ? o : 0) | u | v;
            }

            var result = new List<Vector4>(n);
            for (int i = 0; i < n; i++)
            {
                int r = Find(parent, i);
                Vector2 lo = min[r], hi = max[r];
                int sides = open.TryGetValue(r, out int o) ? o : 0;
                if ((sides & 1) != 0) lo.x = -Open;
                if ((sides & 2) != 0) hi.x = Open;
                if ((sides & 4) != 0) lo.y = -Open;
                if ((sides & 8) != 0) hi.y = Open;
                result.Add(new Vector4(lo.x, lo.y, hi.x, hi.y));
            }
            return result;
        }

        // Least-squares fit of the normal curvature k(dir) = kT x^2 + 2 kTB x y + kB y^2 to the 1-ring chords (Meyer et al.).
        static List<Vector4> Curvature(int[] tris, Vector3[] pos, Vector3[] nrm, Vector4[] tan, int[] group, int groupCount)
        {
            var ring = new HashSet<int>[groupCount];
            var groupPos = new Vector3[groupCount];
            for (int i = 0; i < pos.Length; i++) groupPos[group[i]] = pos[i];
            for (int t = 0; t < tris.Length; t += 3)
            {
                for (int e = 0; e < 3; e++)
                {
                    int a = group[tris[t + e]];
                    (ring[a] ??= new HashSet<int>()).Add(group[tris[t + (e + 1) % 3]]);
                    ring[a].Add(group[tris[t + (e + 2) % 3]]);
                }
            }

            var result = new List<Vector4>(pos.Length);
            for (int i = 0; i < pos.Length; i++)
            {
                Vector3 nI = nrm[i].normalized;
                Vector3 tI = ((Vector3)tan[i] - nI * Vector3.Dot(nI, tan[i])).normalized;
                Vector3 bI = Vector3.Cross(nI, tI) * (tan[i].w < 0f ? -1f : 1f);
                double s40 = 0, s31 = 0, s22 = 0, s13 = 0, s04 = 0, r20 = 0, r11 = 0, r02 = 0, kSum = 0;
                int count = 0;
                var neighbours = ring[group[i]];
                if (neighbours != null)
                {
                    foreach (int g in neighbours)
                    {
                        Vector3 e = groupPos[g] - pos[i];
                        float len2 = e.sqrMagnitude;
                        if (len2 < 1e-12f) continue;
                        float k = 2f * Vector3.Dot(nI, -e) / len2;
                        Vector3 et = e - nI * Vector3.Dot(nI, e);
                        float m = et.magnitude;
                        if (m < 1e-6f) continue;
                        double x = Vector3.Dot(et, tI) / m, y = Vector3.Dot(et, bI) / m;
                        double xx = x * x, xy = x * y, yy = y * y;
                        s40 += xx * xx; s31 += xx * xy; s22 += xx * yy; s13 += xy * yy; s04 += yy * yy;
                        r20 += xx * k; r11 += xy * k; r02 += yy * k;
                        kSum += k;
                        count++;
                    }
                }
                result.Add(count == 0 ? Vector4.zero : Solve(s40, s31, s22, s13, s04, r20, r11, r02, kSum / count));
            }
            return result;
        }

        // Normal equations for (kT, kTB, kB) with rows [x^2, 2xy, y^2]; falls back to the mean (isotropic) when ill-conditioned.
        static Vector4 Solve(double s40, double s31, double s22, double s13, double s04, double r20, double r11, double r02, double mean)
        {
            double a00 = s40, a01 = 2 * s31, a02 = s22;
            double a11 = 4 * s22, a12 = 2 * s13, a22 = s04;
            double b0 = r20, b1 = 2 * r11, b2 = r02;
            double det = a00 * (a11 * a22 - a12 * a12) - a01 * (a01 * a22 - a12 * a02) + a02 * (a01 * a12 - a11 * a02);
            if (System.Math.Abs(det) < 1e-9) return new Vector4((float)mean, (float)mean, 0f, 0f);
            double kT = (b0 * (a11 * a22 - a12 * a12) - a01 * (b1 * a22 - a12 * b2) + a02 * (b1 * a12 - a11 * b2)) / det;
            double kTB = (a00 * (b1 * a22 - b2 * a12) - b0 * (a01 * a22 - a12 * a02) + a02 * (a01 * b2 - b1 * a02)) / det;
            double kB = (a00 * (a11 * b2 - a12 * b1) - a01 * (a01 * b2 - b1 * a02) + b0 * (a01 * a12 - a11 * a02)) / det;
            return new Vector4((float)kT, (float)kB, (float)kTB, 0f);
        }
    }
}
