using UnityEngine;
using UnityEngine.Profiling;

namespace JamzeySt.Carve.Demo
{
    // On-screen controls for the demo scenes: Carve on/off for before/after, debug views, sun sweep, and the station guide.
    public class CarveDemoHUD : MonoBehaviour
    {
        public Transform player;
        public Light sun;
        public bool visible = true;
        [Tooltip("Degrees per second while the sun sweep is on.")]
        public float sunSweepSpeed = 12f;

        bool m_Sweep;
        float m_SunYaw;
        float m_SunPitch;
        float m_FrameMs = 16f;
        bool m_ShowGpu;
        int m_Profile;

        // F2 cycles these to show what each Carve feature costs (read the G panel in each). With a Carve Quality Manager in the
        // scene F2 cycles its presets instead and F3 switches its adaptive scaling.
        static readonly string[] k_Profiles = { "Full", "No self-shadow", "No relief occlusion", "No cliff / specular occlusion", "No anti-tiling", "Half march steps", "No extras" };
        Recorder[] m_Recorders;
        float[] m_GpuMs;

        // HDRP passes for the live GPU breakdown (G). Passes this HDRP version doesn't record are left out.
        static readonly (string marker, string label)[] k_Passes =
        {
            ("HDRenderPipelineRenderCamera", "Whole camera"),
            ("DepthPrepass", "Depth prepass"),
            ("DeferredDepthPrepass", "Depth prepass (deferred)"),
            ("GBuffer", "GBuffer"),
            ("RenderShadowMaps", "Shadow maps"),
            ("RenderSSAO", "SSAO"),
            ("ContactShadows", "Contact shadows"),
            ("DeferredLighting", "Deferred lighting"),
            ("ForwardOpaque", "Forward opaque"),
            ("PostProcessing", "Post processing"),
        };
        GUIStyle m_Box, m_Label, m_Title;
        CarveDemoFlyCamera m_Fly;

        void Start()
        {
            if (player != null) m_Fly = player.GetComponent<CarveDemoFlyCamera>();
            if (sun != null)
            {
                var e = sun.transform.eulerAngles;
                m_SunPitch = e.x;
                m_SunYaw = e.y;
            }
            CarveGlobals.DepthScale = 1f;
            CarveGlobals.DebugView = CarveDebugView.Off;
        }

        void SetGpu(bool on)
        {
            m_ShowGpu = on;
            if (m_Recorders == null)
            {
                m_Recorders = new Recorder[k_Passes.Length];
                m_GpuMs = new float[k_Passes.Length];
                for (int i = 0; i < k_Passes.Length; i++) m_Recorders[i] = Recorder.Get(k_Passes[i].marker);
            }
            foreach (var r in m_Recorders) if (r.isValid) r.enabled = on;
        }

        void SetProfile(int p)
        {
            m_Profile = p;
            if (CarveQualityManager.Active != null) return;
            CarveGlobals.SelfShadow = p == 1 || p == 6 ? 0f : 1f;
            CarveGlobals.Occlusion = p == 2 || p == 6 ? 0f : 1f;
            CarveGlobals.CliffAndSpecular = p == 3 || p == 6 ? 0f : 1f;
            CarveGlobals.AntiTiling = p == 4 ? 0f : 1f;
            CarveGlobals.StepScale = p == 5 ? 0.5f : 1f;
        }

        void OnDisable()
        {
            SetProfile(0);
            if (m_Recorders != null) SetGpu(false);
            CarveGlobals.DepthScale = 1f;
            CarveGlobals.DebugView = CarveDebugView.Off;
        }

        void Update()
        {
            m_FrameMs = Mathf.Lerp(m_FrameMs, Time.unscaledDeltaTime * 1000f, 0.05f);
            if (CarveDemoInput.TogglePressed(KeyCode.F1)) visible = !visible;
            if (CarveDemoInput.TogglePressed(KeyCode.C)) CarveGlobals.DepthScale = CarveGlobals.DepthScale > 0f ? 0f : 1f;
            if (CarveDemoInput.TogglePressed(KeyCode.V)) CarveGlobals.DebugView = NextView(CarveGlobals.DebugView);
            if (CarveDemoInput.TogglePressed(KeyCode.L)) m_Sweep = !m_Sweep;
            if (CarveDemoInput.TogglePressed(KeyCode.G)) SetGpu(!m_ShowGpu);
            var quality = CarveQualityManager.Active;
            if (CarveDemoInput.TogglePressed(KeyCode.F2))
            {
                if (quality != null) quality.SetPreset((CarveQualityPreset)(((int)quality.preset + 1) % (int)CarveQualityPreset.Custom));
                else SetProfile((m_Profile + 1) % k_Profiles.Length);
            }
            if (quality != null && CarveDemoInput.TogglePressed(KeyCode.F3)) quality.adaptive = !quality.adaptive;
            if (m_ShowGpu)
                for (int i = 0; i < m_Recorders.Length; i++)
                    if (m_Recorders[i].isValid) m_GpuMs[i] = Mathf.Lerp(m_GpuMs[i], m_Recorders[i].gpuElapsedNanoseconds * 1e-6f, 0.1f);
            if (m_Sweep && sun != null)
            {
                m_SunYaw += sunSweepSpeed * Time.deltaTime;
                sun.transform.rotation = Quaternion.Euler(m_SunPitch, m_SunYaw, 0f);
            }
        }

        static CarveDebugView NextView(CarveDebugView v) => (CarveDebugView)(((int)v + 1) % System.Enum.GetValues(typeof(CarveDebugView)).Length);

        void OnGUI()
        {
            if (!visible) return;
            m_Box ??= new GUIStyle(GUI.skin.box) { padding = new RectOffset(14, 14, 10, 12) };
            m_Label ??= new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true, fontSize = 13 };
            m_Title ??= new GUIStyle(m_Label) { fontSize = 17, fontStyle = FontStyle.Bold };

            GUILayout.BeginArea(new Rect(16, 16, 360, 330), m_Box);
            GUILayout.Label("<b>CARVE</b>  " + CarveInfo.Slogan, m_Label);
            GUILayout.Label((m_Fly != null ? "WASD fly  -  E / Q up / down  -  Shift fast  -  Right mouse look  -  Scroll speed" + (m_Fly.tour != null ? "  -  <b>T</b> tour" : "")
                                            : "WASD move  -  Shift run  -  Right mouse orbit  -  Scroll zoom") + "\n" +
                            "<b>C</b> Carve on/off  -  <b>V</b> debug view  -  <b>L</b> sweep the sun  -  <b>G</b> GPU timings (<b>F2</b> profile)  -  <b>F1</b> hide", m_Label);
            GUILayout.Space(4);
            string state = CarveGlobals.DepthScale > 0f ? "<color=#E8A04F><b>ON</b></color>" : "<color=#9AA3AE><b>OFF</b> (flat, normal maps only)</color>";
            GUILayout.Label($"Carve: {state}", m_Label);
            GUILayout.Label($"Frame: {m_FrameMs:0.0} ms ({1000f / Mathf.Max(m_FrameMs, 0.01f):0} fps)", m_Label);
            if (m_Fly != null && m_Fly.Touring) GUILayout.Label("<color=#E8A04F><b>Tour</b></color> (any movement key takes over)", m_Label);
            var q = CarveQualityManager.Active;
            if (q != null)
                GUILayout.Label($"Quality: <b>{q.preset}</b> (F2)  -  " + (q.adaptive ? $"adaptive <b>{q.CurrentScale:0.00}</b> for {q.targetFrameRate:0} fps (F3)" : "adaptive off (F3)"), m_Label);
            if (CarveGlobals.DebugView != CarveDebugView.Off) GUILayout.Label($"Debug view: <b>{CarveGlobals.DebugView}</b>", m_Label);
            if (sun != null)
            {
                GUILayout.Label("Sun direction", m_Label);
                float yaw = GUILayout.HorizontalSlider(Mathf.Repeat(m_SunYaw, 360f), 0f, 360f);
                if (!Mathf.Approximately(yaw, Mathf.Repeat(m_SunYaw, 360f)))
                {
                    m_SunYaw = yaw;
                    sun.transform.rotation = Quaternion.Euler(m_SunPitch, m_SunYaw, 0f);
                }
            }
            GUILayout.EndArea();

            if (m_ShowGpu)
            {
                GUILayout.BeginArea(new Rect(Screen.width - 296, 16, 280, 62 + 22 * k_Passes.Length), m_Box);
                GUILayout.Label("<b>GPU per pass</b> (ms, this frame's camera)", m_Label);
                GUILayout.Label(CarveQualityManager.Active != null ? $"Quality: <b>{CarveQualityManager.Active.preset}</b>" : $"F2 profile: <b>{k_Profiles[m_Profile]}</b>", m_Label);
                for (int i = 0; i < k_Passes.Length; i++)
                    if (m_Recorders[i].isValid && m_Recorders[i].gpuSampleBlockCount > 0)
                        GUILayout.Label($"{k_Passes[i].label}: <b>{m_GpuMs[i]:0.00}</b>", m_Label);
                GUILayout.EndArea();
            }

            var station = player != null ? CarveDemoStation.Nearest(player.position) : null;
            if (station != null)
            {
                float w = Mathf.Min(620f, Screen.width - 32f);
                GUILayout.BeginArea(new Rect((Screen.width - w) * 0.5f, Screen.height - 150f, w, 134f), m_Box);
                GUILayout.Label(station.title, m_Title);
                GUILayout.Label(station.description, m_Label);
                GUILayout.EndArea();
            }
        }
    }
}
