using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine.Profiling;

namespace JamzeySt.Carve.Editor
{
    // Measures what Carve costs in the open views: GPU time of the pipeline's passes with Carve on vs. flat
    // (global depth scale 0), alternating every few frames so clock changes hit both sides equally.
    public static class CarveCostMeter
    {
        // (profiler marker, label), registered by each pipeline's editor code.
        public static readonly List<(string marker, string label)> Markers = new List<(string, string)>();

        const int PhaseFrames = 8, SkipFrames = 3, Phases = 16;
        static List<Recorder> s_Recs;
        static List<float>[,] s_Samples;
        static int s_Frame;
        static float s_Depth0;

        public static bool Running { get; private set; }
        public static float Progress => Running ? (float)s_Frame / (PhaseFrames * Phases) : 0f;
        public static string Result { get; private set; }
        public static event Action Changed;

        public static void Start()
        {
            if (Running || Markers.Count == 0) return;
            s_Recs = new List<Recorder>();
            foreach (var (marker, _) in Markers)
            {
                var r = Recorder.Get(marker);
                r.enabled = true;
                s_Recs.Add(r);
            }
            s_Samples = new List<float>[2, Markers.Count];
            for (int c = 0; c < 2; c++)
                for (int k = 0; k < Markers.Count; k++) s_Samples[c, k] = new List<float>();
            s_Frame = 0;
            s_Depth0 = CarveGlobals.DepthScale;
            Running = true;
            EditorApplication.update += Tick;
            Changed?.Invoke();
        }

        static void Tick()
        {
            int phase = s_Frame / PhaseFrames, inPhase = s_Frame % PhaseFrames;
            bool on = phase % 2 == 0;
            if (inPhase >= SkipFrames && s_Frame > 0)
                for (int k = 0; k < s_Recs.Count; k++)
                    if (s_Recs[k].gpuSampleBlockCount > 0) s_Samples[on ? 0 : 1, k].Add(s_Recs[k].gpuElapsedNanoseconds * 1e-6f);
            s_Frame++;
            if (s_Frame >= PhaseFrames * Phases)
            {
                Finish();
                return;
            }
            CarveGlobals.DepthScale = (s_Frame / PhaseFrames) % 2 == 0 ? s_Depth0 : 0f;
            SceneView.RepaintAll();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        static void Finish()
        {
            EditorApplication.update -= Tick;
            CarveGlobals.DepthScale = s_Depth0;
            Running = false;
            var sb = new StringBuilder();
            float total = 0f;
            for (int k = 0; k < Markers.Count; k++)
            {
                float d = Median(s_Samples[0, k]) - Median(s_Samples[1, k]);
                total += d;
                sb.Append(Markers[k].label).Append(": ").Append(d.ToString("F2")).Append(" ms   ");
            }
            Result = s_Samples[0, 0].Count == 0
                ? "No frames were rendered while measuring. Keep a Scene or Game view visible and try again."
                : $"Carve costs about {total:F2} ms of GPU time per frame in the open views.\n{sb.ToString().TrimEnd()}";
            SceneView.RepaintAll();
            Changed?.Invoke();
        }

        static float Median(List<float> v)
        {
            if (v.Count == 0) return 0f;
            v.Sort();
            return v[v.Count / 2];
        }
    }
}
