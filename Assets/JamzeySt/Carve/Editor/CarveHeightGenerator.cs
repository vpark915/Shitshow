using System;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace JamzeySt.Carve.Editor
{
    // Height map from a normal map (+ a little albedo detail): the normal map's slopes are integrated with a periodic
    // Poisson solve (Frankot-Chellappa, FFT), so the result tiles exactly like its sources.
    public static class CarveHeightGenerator
    {
        public const string DefaultFolder = "Assets/Carve Generated/Heights";
        const string Stamp = "carve-height-generator";
        const int MaxSize = 2048;

        public enum GreenChannel { Auto, OpenGL, DirectX }

        public static float AlbedoDetail
        {
            get => EditorPrefs.GetFloat("JamzeySt.Carve.HeightGen.AlbedoDetail", 0f);
            set => EditorPrefs.SetFloat("JamzeySt.Carve.HeightGen.AlbedoDetail", Mathf.Clamp01(value));
        }

        public static GreenChannel Green
        {
            get => (GreenChannel)EditorPrefs.GetInt("JamzeySt.Carve.HeightGen.Green", 0);
            set => EditorPrefs.SetInt("JamzeySt.Carve.HeightGen.Green", (int)value);
        }

        // The last Auto decision, for the log.
        public static bool LastWasDirectX { get; private set; }

        // 0 = keep every grain of the normal map, 1 = only the big shapes. Fine grain turns into pits under POM.
        public static float Smoothing
        {
            get => EditorPrefs.GetFloat("JamzeySt.Carve.HeightGen.Smoothing", 0.5f);
            set => EditorPrefs.SetFloat("JamzeySt.Carve.HeightGen.Smoothing", Mathf.Clamp01(value));
        }

        public static bool Invert
        {
            get => EditorPrefs.GetBool("JamzeySt.Carve.HeightGen.Invert", false);
            set => EditorPrefs.SetBool("JamzeySt.Carve.HeightGen.Invert", value);
        }

        public static bool CanGenerate(Material m)
        {
            foreach (var slot in CarveProject.HeightSlots(m))
                if (CanGenerate(m, slot)) return true;
            return false;
        }

        public static bool CanGenerate(Material m, CarveHeightSlot slot) =>
            m.GetTexture(slot.height) == null && m.GetTexture(slot.albedo) != null && m.GetTexture(slot.normal) != null;

        // Generates, saves and assigns a height map for every material that has albedo + normal but no height map.
        public static bool CanGenerateDetail(Material m) =>
            CarveProject.IsCarve(m) && m.HasProperty("_CarveDetailHeight") && m.GetFloat("_CarveDetail") > 0.5f
            && m.GetTexture("_CarveDetailHeight") == null && m.GetTexture("_CarveDetailNormal") != null;

        // Detail relief: height from the detail normal map alone.
        public static int GenerateDetailFor(Material[] mats)
        {
            int n = 0;
            try
            {
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (!CanGenerateDetail(m)) continue;
                    EditorUtility.DisplayProgressBar("Carve", $"Generating detail height for {m.name}", (float)i / mats.Length);
                    var height = GenerateAsset(null, m.GetTexture("_CarveDetailNormal"));
                    Undo.RecordObject(m, "Generate Carve Detail Height");
                    m.SetTexture("_CarveDetailHeight", height);
                    EditorUtility.SetDirty(m);
                    n++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            return n;
        }

        // layer >= 0 limits it to that layer of layered materials.
        public static int GenerateFor(Material[] mats, int layer = -1)
        {
            int n = 0;
            try
            {
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    foreach (var slot in CarveProject.HeightSlots(m))
                    {
                        if ((layer >= 0 && slot.layer != layer) || !CanGenerate(m, slot)) continue;
                        EditorUtility.DisplayProgressBar("Carve", $"Generating height map for {m.name}", (float)i / mats.Length);
                        var height = GenerateAsset(m.GetTexture(slot.albedo), m.GetTexture(slot.normal));
                        Undo.RecordObject(m, "Generate Carve Height Map");
                        m.SetTexture(slot.height, height);
                        EditorUtility.SetDirty(m);
                        n++;
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            CarvePyramidBaker.Assign(Array.FindAll(mats, CarvePyramidBaker.UsesPyramid));
            return n;
        }

        public static Texture2D GenerateAsset(Texture albedo, Texture normal)
        {
            int size = Mathf.Min(Mathf.NextPowerOfTwo(Mathf.Max(normal.width, normal.height)), MaxSize);
            float[] h = Generate(albedo, normal, size, size);
            var tex = new Texture2D(size, size, TextureFormat.R16, false, true);
            var px = new ushort[h.Length];
            for (int i = 0; i < h.Length; i++) px[i] = (ushort)Mathf.Clamp(Mathf.RoundToInt(h[i] * 65535f), 0, 65535);
            tex.SetPixelData(px, 0);
            tex.Apply(false, false);
            byte[] png = tex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(tex);

            string path = OutputPath(albedo != null ? albedo : normal);
            File.WriteAllBytes(path, png);
            AssetDatabase.ImportAsset(path);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = false;
            ti.mipmapEnabled = true;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.maxTextureSize = Mathf.Max(size, 32);
            var ps = ti.GetDefaultPlatformTextureSettings();
            ps.format = TextureImporterFormat.R16;
            ps.textureCompression = TextureImporterCompression.Uncompressed;
            ti.SetPlatformTextureSettings(ps);
            ti.userData = Stamp;
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // Next to the albedo when it lives in Assets (overwriting only our own earlier output), else in DefaultFolder.
        static string OutputPath(Texture albedo)
        {
            string src = AssetDatabase.GetAssetPath(albedo);
            string folder = src.StartsWith("Assets/") ? Path.GetDirectoryName(src).Replace('\\', '/') : DefaultFolder;
            if (!AssetDatabase.IsValidFolder(folder))
            {
                Directory.CreateDirectory(folder);
                AssetDatabase.Refresh();
            }
            string path = $"{folder}/{albedo.name}_CarveHeight.png";
            var existing = AssetImporter.GetAtPath(path);
            if (existing != null && existing.userData != Stamp) path = AssetDatabase.GenerateUniqueAssetPath(path);
            return path;
        }

        // Heights in 0..1, row 0 = bottom (v = 0).
        public static float[] Generate(Texture albedo, Texture normal, int w, int h)
        {
            int n = w * h;
            var nrm = ReadRGBA(normal, w, h);
            var re = new float[n];
            var im = new float[n];
            for (int i = 0; i < n; i++)
            {
                Color c = nrm[i];
                float nx = c.r * c.a * 2f - 1f, ny = c.g * 2f - 1f;
                float nz = Mathf.Sqrt(Mathf.Max(1f - nx * nx - ny * ny, 0.01f));
                // Pack both slopes into one complex FFT: z = p + i q, with p = dh/dx and q = dh/dy.
                re[i] = Mathf.Clamp(-nx / nz, -8f, 8f);
                im[i] = Mathf.Clamp(-ny / nz, -8f, 8f);
            }
            var green = Green;
            LastWasDirectX = green == GreenChannel.DirectX || (green == GreenChannel.Auto && LooksDirectX(re, im, w, h));
            if (LastWasDirectX)
                for (int i = 0; i < n; i++) im[i] = -im[i];
            CarveFFT.Transform2D(re, im, w, h, false);

            // Unpack P and Q from the packed spectrum, then H = -i (u P + v Q) / (u^2 + v^2), very low frequencies damped.
            var hr = new float[n];
            var hi = new float[n];
            float f0 = 1.5f / Mathf.Max(w, h);
            float fc = Mathf.Lerp(0.5f, 0.04f, Smoothing);
            for (int y = 0; y < h; y++)
            {
                int ym = (h - y) % h;
                float fy = (y <= h / 2 ? y : y - h) / (float)h;
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x, j = ym * w + (w - x) % w;
                    float fx = (x <= w / 2 ? x : x - w) / (float)w;
                    float pr = 0.5f * (re[i] + re[j]), pi = 0.5f * (im[i] - im[j]);
                    float qr = 0.5f * (im[i] + im[j]), qi = -0.5f * (re[i] - re[j]);
                    float u = 2f * Mathf.PI * fx, v = 2f * Mathf.PI * fy, d = u * u + v * v;
                    if (d <= 0f) continue;
                    float ar = u * pr + v * qr, ai = u * pi + v * qi;
                    float f2 = fx * fx + fy * fy;
                    float hp = (1f - Mathf.Exp(-f2 / (f0 * f0))) * Mathf.Exp(-f2 / (fc * fc));
                    hr[i] = ai / d * hp;
                    hi[i] = -ar / d * hp;
                }
            }
            CarveFFT.Transform2D(hr, hi, w, h, true);
            Normalize(hr);

            float detail = AlbedoDetail;
            if (detail > 0f && albedo != null)
            {
                var alb = ReadRGBA(albedo, w, h);
                var lum = new float[n];
                for (int i = 0; i < n; i++) lum[i] = alb[i].r * 0.2126f + alb[i].g * 0.7152f + alb[i].b * 0.0722f;
                var low = (float[])lum.Clone();
                int r = Mathf.Max(w / 128, 2);
                for (int pass = 0; pass < 2; pass++) BoxBlur(low, w, h, r);
                for (int i = 0; i < n; i++) lum[i] -= low[i];
                Normalize(lum);
                for (int i = 0; i < n; i++) hr[i] = Mathf.Lerp(hr[i], lum[i], detail);
                Normalize(hr);
            }
            if (Invert)
                for (int i = 0; i < n; i++) hr[i] = 1f - hr[i];
            return hr;
        }

        // Slopes of a real surface are curl-free (dp/dy = dq/dx). Read with the wrong green sign they aren't, so the
        // convention with less curl wins.
        static bool LooksDirectX(float[] p, float[] q, int w, int h)
        {
            double asGL = 0, asDX = 0;
            for (int y = 0; y < h; y++)
            {
                int y1 = (y + 1) % h;
                for (int x = 0; x < w; x++)
                {
                    int x1 = (x + 1) % w;
                    float dpdy = p[y1 * w + x] - p[y * w + x], dqdx = q[y * w + x1] - q[y * w + x];
                    asGL += (dpdy - dqdx) * (dpdy - dqdx);
                    asDX += (dpdy + dqdx) * (dpdy + dqdx);
                }
            }
            return asDX < asGL;
        }

        static Color[] ReadRGBA(Texture tex, int w, int h)
        {
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var prev = RenderTexture.active;
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            var read = new Texture2D(w, h, TextureFormat.RGBAFloat, false, true);
            read.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            read.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = read.GetPixels();
            UnityEngine.Object.DestroyImmediate(read);
            return px;
        }

        // Robust 0..1 range: 0.5th to 99.5th percentile.
        static void Normalize(float[] v)
        {
            var sorted = (float[])v.Clone();
            Array.Sort(sorted);
            float lo = sorted[(int)(sorted.Length * 0.005f)], hi = sorted[(int)(sorted.Length * 0.995f)];
            float k = hi > lo ? 1f / (hi - lo) : 0f;
            for (int i = 0; i < v.Length; i++) v[i] = Mathf.Clamp01((v[i] - lo) * k);
        }

        static void BoxBlur(float[] v, int w, int h, int r)
        {
            var tmp = new float[v.Length];
            float inv = 1f / (2 * r + 1);
            Parallel.For(0, h, y =>
            {
                int row = y * w;
                float sum = 0f;
                for (int k = -r; k <= r; k++) sum += v[row + (k % w + w) % w];
                for (int x = 0; x < w; x++)
                {
                    tmp[row + x] = sum * inv;
                    sum += v[row + (x + r + 1) % w] - v[row + (x - r + w) % w];
                }
            });
            Parallel.For(0, w, x =>
            {
                float sum = 0f;
                for (int k = -r; k <= r; k++) sum += tmp[((k % h + h) % h) * w + x];
                for (int y = 0; y < h; y++)
                {
                    v[y * w + x] = sum * inv;
                    sum += tmp[((y + r + 1) % h) * w + x] - tmp[((y - r + h) % h) * w + x];
                }
            });
        }
    }

    // Radix-2 complex FFT on separate real/imaginary arrays (power-of-two sizes).
    static class CarveFFT
    {
        public static void Transform2D(float[] re, float[] im, int w, int h, bool inverse)
        {
            Parallel.For(0, h, y =>
            {
                var r = new float[w];
                var i = new float[w];
                Array.Copy(re, y * w, r, 0, w);
                Array.Copy(im, y * w, i, 0, w);
                Transform(r, i, inverse);
                Array.Copy(r, 0, re, y * w, w);
                Array.Copy(i, 0, im, y * w, w);
            });
            Parallel.For(0, w, x =>
            {
                var r = new float[h];
                var i = new float[h];
                for (int y = 0; y < h; y++) { r[y] = re[y * w + x]; i[y] = im[y * w + x]; }
                Transform(r, i, inverse);
                for (int y = 0; y < h; y++) { re[y * w + x] = r[y]; im[y * w + x] = i[y]; }
            });
            if (inverse)
            {
                float s = 1f / (w * h);
                for (int k = 0; k < re.Length; k++) { re[k] *= s; im[k] *= s; }
            }
        }

        static void Transform(float[] re, float[] im, bool inverse)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j)
                {
                    (re[i], re[j]) = (re[j], re[i]);
                    (im[i], im[j]) = (im[j], im[i]);
                }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = 2.0 * Math.PI / len * (inverse ? 1 : -1);
                float wr = (float)Math.Cos(ang), wi = (float)Math.Sin(ang);
                for (int i = 0; i < n; i += len)
                {
                    float cr = 1f, ci = 0f;
                    for (int k = 0; k < len / 2; k++)
                    {
                        int a = i + k, b = a + len / 2;
                        float tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - tr;
                        im[b] = im[a] - ti;
                        re[a] += tr;
                        im[a] += ti;
                        float ncr = cr * wr - ci * wi;
                        ci = cr * wi + ci * wr;
                        cr = ncr;
                    }
                }
            }
        }
    }
}
