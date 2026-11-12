using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace JamzeySt.Carve.Editor
{
    // Bakes the min/max height pyramid used by Pyramid tracing: mip l texel = lowest and highest relief of that cell and its
    // 8 neighbours, so a ray only marches the height band it can actually hit. Rebakes itself when the source height map changes.
    public static class CarvePyramidBaker
    {
        public const string DefaultFolder = "Assets/Carve Generated/Pyramids";
        const string Suffix = "_CarvePyramid";
        const string StampPrefix = "carve-pyramid;";

        public enum Status { NotNeeded, Ready, Missing, Stale }

        [MenuItem("Tools/Carve/Bake Height Pyramids (Selected Materials)")]
        static void BakeSelected()
        {
            var mats = new List<Material>();
            foreach (var o in Selection.objects)
                if (o is Material m && CarveProject.IsCarve(m)) mats.Add(m);
            int n = Assign(mats, true);
            Debug.Log(mats.Count == 0 ? "[Carve] Select Carve materials first." : $"[Carve] Baked {n} height pyramid(s) for {mats.Count} material(s).");
        }

        // For your own Shader Graphs (Carve POM node): bake a height map's pyramid, then plug it into Height Pyramid.
        [MenuItem("Assets/Carve/Bake Height Pyramid", false, 1200)]
        static void BakeSelectedTextures()
        {
            var made = new List<Object>();
            foreach (var o in Selection.objects)
                if (o is Texture2D t && Stamp(t) == null)
                {
                    var p = Find(t);
                    made.Add(p != null && IsCurrent(p, t) ? p : BakeToAsset(t));
                }
            if (made.Count > 0) Selection.objects = made.ToArray();
            Debug.Log($"[Carve] {made.Count} height pyramid(s) ready in {DefaultFolder}.");
        }

        [MenuItem("Assets/Carve/Bake Height Pyramid", true)]
        static bool CanBakeSelectedTextures()
        {
            foreach (var o in Selection.objects)
                if (o is Texture2D t && Stamp(t) == null) return true;
            return false;
        }

        [InitializeOnLoadMethod]
        static void RegisterCheck()
        {
            CarveProject.AddCheck(() =>
            {
                var need = new List<Material>();
                foreach (var r in CarveProject.SceneRenderers())
                    foreach (var m in r.sharedMaterials)
                        if (!need.Contains(m) && (GetStatus(m) == Status.Missing || GetStatus(m) == Status.Stale)) need.Add(m);
                return need.Count == 0
                    ? CarveCheck.Ok("Height pyramids up to date", "Materials on Pyramid tracing have a current pyramid.")
                    : new CarveCheck { level = CarveCheckLevel.Warning, title = $"{need.Count} material(s) need a height pyramid", detail = "They use Pyramid tracing but run Linear until the pyramid is baked.", fixLabel = "Bake now", fix = () => Assign(need) };
            });
        }

        public static bool UsesPyramid(Material m)
        {
            if (!CarveProject.IsCarve(m) || !m.HasProperty("_CarveTracing") || m.GetFloat("_CarveTracing") < 0.5f) return false;
            foreach (var slot in CarveProject.HeightSlots(m))
                if (m.HasProperty(slot.pyramid) && m.GetTexture(slot.height) != null) return true;
            return false;
        }

        // Worst state over the material's height maps (layered materials have one pyramid per layer).
        public static Status GetStatus(Material m)
        {
            if (!UsesPyramid(m)) return Status.NotNeeded;
            var status = Status.Ready;
            foreach (var slot in CarveProject.HeightSlots(m))
            {
                if (!m.HasProperty(slot.pyramid) || !(m.GetTexture(slot.height) is Texture2D height)) continue;
                var pyramid = m.GetTexture(slot.pyramid) as Texture2D;
                if (pyramid == null) return Status.Missing;
                if (!IsCurrent(pyramid, height)) status = Status.Stale;
            }
            return status;
        }

        // Points every material at an up-to-date pyramid of its height map, baking only what is missing or stale.
        public static int Assign(IEnumerable<Material> mats, bool force = false)
        {
            var done = new Dictionary<Texture2D, Texture2D>();
            int baked = 0;
            foreach (var m in mats)
            {
                if (m == null) continue;
                foreach (var slot in CarveProject.HeightSlots(m))
                {
                    if (!m.HasProperty(slot.pyramid)) continue;
                    var height = m.GetTexture(slot.height) as Texture2D;
                    Texture2D pyramid = null;
                    if (height != null && !done.TryGetValue(height, out pyramid))
                    {
                        pyramid = Find(height);
                        if (force || pyramid == null || !IsCurrent(pyramid, height))
                        {
                            pyramid = BakeToAsset(height);
                            baked++;
                        }
                        done[height] = pyramid;
                    }
                    if (m.GetTexture(slot.pyramid) != pyramid)
                    {
                        Undo.RecordObject(m, "Assign Carve Height Pyramid");
                        m.SetTexture(slot.pyramid, pyramid);
                        EditorUtility.SetDirty(m);
                    }
                }
            }
            return baked;
        }

        public static Texture2D BakeToAsset(Texture2D height, string folder = DefaultFolder)
        {
            EnsureFolder(folder);
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(height));
            string path = $"{folder}/{height.name}{Suffix}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null && SourceGuid(existing) != guid)
            {
                path = $"{folder}/{height.name}_{guid.Substring(0, 6)}{Suffix}.asset";
                existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            var tex = Build(height);
            tex.name = Path.GetFileNameWithoutExtension(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(tex, existing);
                Object.DestroyImmediate(tex);
                tex = existing;
            }
            else
                AssetDatabase.CreateAsset(tex, path);
            var so = new SerializedObject(tex);
            so.FindProperty("m_IsReadable").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            var importer = AssetImporter.GetAtPath(path);
            importer.userData = $"{StampPrefix}v5;src={guid};hash={height.imageContentsHash}";
            EditorUtility.SetDirty(tex);
            AssetDatabase.WriteImportSettingsIfDirty(path);
            AssetDatabase.SaveAssets();
            return tex;
        }

        // RG = (max, min) of each cell and its 8 neighbours, per mip, at half the height map's resolution;
        // 8-bit, rounded outward so the range stays conservative (2K height map -> ~2.8 MB).
        public static Texture2D Build(Texture2D height)
        {
            int w = Mathf.NextPowerOfTwo(Mathf.Max(height.width, 2)), h = Mathf.NextPowerOfTwo(Mathf.Max(height.height, 2));
            float[] full = ReadHeight(height, w, h);
            float[] hi = Reduce(full, w, h, true, out int lw, out int lh);
            float[] lo = Reduce(full, w, h, false, out lw, out lh);
            int levels = 1 + Mathf.FloorToInt(Mathf.Log(Mathf.Max(lw, lh), 2f));
            var tex = new Texture2D(lw, lh, TextureFormat.RG16, levels, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
            for (int l = 0; l < levels; l++)
            {
                tex.SetPixelData(Dilate(hi, lo, lw, lh), l);
                if (l == levels - 1) break;
                hi = Reduce(hi, lw, lh, true, out int nw, out int nh);
                lo = Reduce(lo, lw, lh, false, out nw, out nh);
                lw = nw;
                lh = nh;
            }
            tex.Apply(false, false);
            return tex;
        }

        // Linear heights of any texture at the given size (GPU blit, so the source needs no Read/Write).
        public static float[] ReadHeight(Texture tex, int w, int h)
        {
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
            var prev = RenderTexture.active;
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            var read = new Texture2D(w, h, TextureFormat.RFloat, false, true);
            read.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            read.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var data = read.GetPixelData<float>(0).ToArray();
            Object.DestroyImmediate(read);
            return data;
        }

        static byte[] Dilate(float[] hi, float[] lo, int w, int h)
        {
            var o = new byte[w * h * 2];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float mx = 0f, mn = 1f;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int row = (y + dy + h) % h * w;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int i = row + (x + dx + w) % w;
                            mx = Mathf.Max(mx, hi[i]);
                            mn = Mathf.Min(mn, lo[i]);
                        }
                    }
                    int k = (y * w + x) * 2;
                    o[k] = (byte)Mathf.Clamp(Mathf.CeilToInt(mx * 255f - 1e-3f), 0, 255);
                    o[k + 1] = (byte)Mathf.Clamp(Mathf.FloorToInt(mn * 255f + 1e-3f), 0, 255);
                }
            return o;
        }

        static float[] Reduce(float[] m, int w, int h, bool max, out int nw, out int nh)
        {
            nw = Mathf.Max(w / 2, 1);
            nh = Mathf.Max(h / 2, 1);
            var o = new float[nw * nh];
            for (int y = 0; y < nh; y++)
            {
                int ya = Mathf.Min(y * 2, h - 1) * w, yb = Mathf.Min(y * 2 + 1, h - 1) * w;
                for (int x = 0; x < nw; x++)
                {
                    int xa = Mathf.Min(x * 2, w - 1), xb = Mathf.Min(x * 2 + 1, w - 1);
                    o[y * nw + x] = max ? Mathf.Max(Mathf.Max(m[ya + xa], m[ya + xb]), Mathf.Max(m[yb + xa], m[yb + xb]))
                                        : Mathf.Min(Mathf.Min(m[ya + xa], m[ya + xb]), Mathf.Min(m[yb + xa], m[yb + xb]));
                }
            }
            return o;
        }

        static string Stamp(Texture2D pyramid)
        {
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(pyramid));
            return importer != null && importer.userData.StartsWith(StampPrefix) ? importer.userData : null;
        }

        static string StampField(string stamp, string key)
        {
            if (stamp == null) return null;
            foreach (var part in stamp.Split(';'))
                if (part.StartsWith(key + "=")) return part.Substring(key.Length + 1);
            return null;
        }

        static string SourceGuid(Texture2D pyramid) => StampField(Stamp(pyramid), "src");

        static bool IsCurrent(Texture2D pyramid, Texture2D height)
        {
            if (pyramid == null || height == null) return false;
            string stamp = Stamp(pyramid);
            return stamp != null && stamp.StartsWith(StampPrefix + "v5;") && StampField(stamp, "src") == AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(height))
                && StampField(stamp, "hash") == height.imageContentsHash.ToString();
        }

        static Texture2D Find(Texture2D height)
        {
            if (!AssetDatabase.IsValidFolder(DefaultFolder)) return null;
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(height));
            foreach (var g in AssetDatabase.FindAssets("t:Texture2D", new[] { DefaultFolder }))
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(g));
                if (tex != null && SourceGuid(tex) == guid) return tex;
            }
            return null;
        }

        public static List<Texture2D> AllPyramids()
        {
            var list = new List<Texture2D>();
            if (!AssetDatabase.IsValidFolder(DefaultFolder)) return list;
            foreach (var g in AssetDatabase.FindAssets("t:Texture2D", new[] { DefaultFolder }))
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(g));
                if (tex != null && Stamp(tex) != null) list.Add(tex);
            }
            return list;
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        // A reimported height map makes its pyramid stale: rebake it right away.
        class Watcher : AssetPostprocessor
        {
            static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                if (imported.Length == 0 || !AssetDatabase.IsValidFolder(DefaultFolder)) return;
                var sources = new List<string>();
                foreach (var p in imported)
                    if (!p.StartsWith(DefaultFolder) && AssetImporter.GetAtPath(p) is TextureImporter) sources.Add(AssetDatabase.AssetPathToGUID(p));
                if (sources.Count == 0) return;
                EditorApplication.delayCall += () =>
                {
                    foreach (var pyramid in AllPyramids())
                    {
                        string src = SourceGuid(pyramid);
                        if (!sources.Contains(src)) continue;
                        var height = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(src));
                        if (height != null && !IsCurrent(pyramid, height)) BakeToAsset(height);
                    }
                };
            }
        }
    }
}
