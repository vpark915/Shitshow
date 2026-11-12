using UnityEngine;

namespace JamzeySt.Carve
{
    // One per scene: sets how much work every Carve material does. Pick a preset or tune each setting (Custom); Quality Scale
    // (or Adaptive, in Play mode) scales the steps and distances down together to hold a frame rate.
    [ExecuteAlways, DisallowMultipleComponent]
    [AddComponentMenu("JamzeySt/Carve/Carve Quality Manager")]
    public class CarveQualityManager : MonoBehaviour
    {
        [Header("Quality")]
        public CarveQualityPreset preset = CarveQualityPreset.High;
        public CarveQualitySettings custom = CarveQualitySettings.For(CarveQualityPreset.High);

        [Header("Scale")]
        [Tooltip("Scales march steps, shadow steps, relief distance and extras distance together (1 = the settings above).")]
        [Range(0.1f, 1f)] public float qualityScale = 1f;

        [Header("Adaptive (Play mode)")]
        [Tooltip("Lower Quality Scale while frames run over the target, raise it again when there is room.")]
        public bool adaptive;
        [Min(10f)] public float targetFrameRate = 60f;
        [Tooltip("Adaptive never scales below this.")]
        [Range(0.1f, 1f)] public float minimumScale = 0.4f;
        [Tooltip("Scale change per second at full error. Raising is three times slower than lowering, so it settles instead of pumping.")]
        [Range(0.05f, 2f)] public float responseSpeed = 0.5f;

        public static CarveQualityManager Active { get; private set; }

        // The settings in use: the preset's, or the custom ones.
        public CarveQualitySettings Settings => preset == CarveQualityPreset.Custom ? custom : CarveQualitySettings.For(preset);

        // Play mode: the scale in use and the smoothed frame time (GPU time when available), measured with or without adaptive.
        public float CurrentScale { get; private set; } = 1f;
        public float MeasuredFrameMs { get; private set; }
        public bool UsingGpuTime { get; private set; }
        public float FrameBudgetMs => 1000f / Mathf.Max(targetFrameRate, 1f);

        // Optional GPU frame time (ms) and frame clock (s) for tools that drive frames themselves (benchmarks, video capture):
        // when set they replace FrameTimingManager and Time.unscaledDeltaTime. A clock of 0 holds the measurement and scale.
        public static System.Func<float> GpuTimeSource { get; set; }
        public static System.Func<float> DeltaTimeSource { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            GpuTimeSource = null;
            DeltaTimeSource = null;
        }

        readonly FrameTiming[] m_Timings = new FrameTiming[1];

        // Switch preset from code (menus, settings screens): Custom keeps the custom settings.
        public void SetPreset(CarveQualityPreset p)
        {
            preset = p;
            Apply();
        }

        // ---------------- Lifecycle

        void OnEnable()
        {
            if (Active != null && Active != this)
                Debug.LogWarning($"Carve: more than one Carve Quality Manager is enabled ({Active.name}, {name}); the last one enabled wins.", this);
            Active = this;
            CurrentScale = qualityScale;
            MeasuredFrameMs = 0f;
            Apply();
        }

        void OnDisable()
        {
            if (Active != this) return;
            Active = null;
            CarveGlobals.ResetQuality();
        }

        void OnValidate()
        {
            if (!adaptive || !Application.isPlaying) CurrentScale = qualityScale;
            if (isActiveAndEnabled && Active == this) Apply();
        }

        void Update()
        {
            if (Active != this) return;
            if (!Application.isPlaying) CurrentScale = qualityScale;
            else
            {
                float frame = DeltaTimeSource != null ? DeltaTimeSource() : Time.unscaledDeltaTime;
                if (frame > 0f)
                {
                    Measure(frame);
                    if (adaptive) Adapt(Mathf.Min(frame, 0.1f));
                }
                if (!adaptive) CurrentScale = qualityScale;
            }
            Apply();
        }

        // ---------------- Frame time and adaptive scale

        void Measure(float frame)
        {
            float ms = frame * 1000f;
            float source = GpuTimeSource != null ? GpuTimeSource() : 0f;
            if (source > 0f)
            {
                UsingGpuTime = true;
                ms = source;
            }
            else
            {
                FrameTimingManager.CaptureFrameTimings();
                UsingGpuTime = FrameTimingManager.GetLatestTimings(1, m_Timings) > 0 && m_Timings[0].gpuFrameTime > 0.0;
                if (UsingGpuTime) ms = (float)m_Timings[0].gpuFrameTime;
            }
            float dt = Mathf.Min(frame, 0.1f);
            MeasuredFrameMs = MeasuredFrameMs <= 0f ? ms : Mathf.Lerp(MeasuredFrameMs, ms, 1f - Mathf.Exp(-dt * 4f));
        }

        void Adapt(float dt)
        {
            // Hold the frame between 90% and 100% of the budget: over it lower the scale, well under it raise it.
            float error = MeasuredFrameMs / FrameBudgetMs;
            float step = 0f;
            if (error > 1f) step = -Mathf.Clamp01((error - 1f) * 4f);
            else if (error < 0.9f) step = Mathf.Clamp01((0.9f - error) * 4f) / 3f;
            CurrentScale = Mathf.Clamp(CurrentScale + step * responseSpeed * dt, Mathf.Min(minimumScale, 1f), 1f);
        }

        // ---------------- Publish to every Carve material

        void Apply()
        {
            var s = Settings;
            float k = Mathf.Clamp(CurrentScale, 0.1f, 1f);
            CarveGlobals.StepScale = s.marchSteps * k;
            CarveGlobals.DistanceScale = s.reliefDistance * k;
            CarveGlobals.ShadowStepScale = s.shadowSteps * k;
            CarveGlobals.ExtrasDistance = s.extrasDistance * k;
            CarveGlobals.OcclusionSamples = s.occlusionSamples;
            CarveGlobals.SelfShadow = s.selfShadow;
            CarveGlobals.Occlusion = s.reliefOcclusion;
            CarveGlobals.CliffAndSpecular = s.cliffAndSpecular;
            CarveGlobals.AntiTiling = s.antiTiling;
        }
    }
}
