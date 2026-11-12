using UnityEngine;

namespace JamzeySt.Carve
{
    public enum CarveDebugView { Off, Curvature, Steps, Silhouette, SelfShadow, Occlusion, Cliff, SpecularOcclusion, TriplanarBlend, Layers }

    // Relief occlusion directions: Auto = 8 on large rings, 4 (alternating per pixel, TAA blends them) on small ones.
    public enum CarveOcclusionSamples { Auto, Four, Eight }

    // Scene-wide overrides read by every Carve material (published before each frame renders).
    public static class CarveGlobals
    {
        static readonly int GlobalsId = Shader.PropertyToID("_CarveGlobals");
        static readonly int FeaturesId = Shader.PropertyToID("_CarveGlobalFeatures");
        static readonly int QualityId = Shader.PropertyToID("_CarveGlobalQuality");

        public static float DepthScale { get; set; } = 1f;
        public static float StepScale { get; set; } = 1f;
        public static CarveDebugView DebugView { get; set; } = CarveDebugView.Off;

        // Scene-wide feature switches (0..1, multiplied with each material's own strength): scale Carve down on slower
        // hardware, or switch features off one at a time to see what each costs.
        public static float SelfShadow { get; set; } = 1f;
        public static float Occlusion { get; set; } = 1f;
        public static float CliffAndSpecular { get; set; } = 1f;
        public static float AntiTiling { get; set; } = 1f;

        // Scene-wide quality scales (CarveQualityManager sets them): relief distance scales every material's Fade Start / End,
        // shadow steps scale the self-shadow and reflection rays, extras distance ends self-shadow, occlusion and cliff
        // shading at that many meters (0 = no limit).
        public static float DistanceScale { get; set; } = 1f;
        public static float ShadowStepScale { get; set; } = 1f;
        public static float ExtrasDistance { get; set; } = 0f;
        public static CarveOcclusionSamples OcclusionSamples { get; set; } = CarveOcclusionSamples.Auto;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            DepthScale = 1f;
            DebugView = CarveDebugView.Off;
            ResetQuality();
        }

        // Back to every material's own settings.
        public static void ResetQuality()
        {
            StepScale = 1f;
            SelfShadow = Occlusion = CliffAndSpecular = AntiTiling = 1f;
            DistanceScale = ShadowStepScale = 1f;
            ExtrasDistance = 0f;
            OcclusionSamples = CarveOcclusionSamples.Auto;
        }

        internal static void Publish()
        {
            Shader.SetGlobalVector(GlobalsId, new Vector4(Mathf.Max(DepthScale, 0f), Mathf.Max(StepScale, 0.1f), (float)DebugView, 1f));
            Shader.SetGlobalVector(FeaturesId, new Vector4(Mathf.Clamp01(SelfShadow), Mathf.Clamp01(Occlusion), Mathf.Clamp01(CliffAndSpecular), Mathf.Clamp01(AntiTiling)));
            Shader.SetGlobalVector(QualityId, new Vector4(Mathf.Max(DistanceScale, 0.05f), Mathf.Max(ShadowStepScale, 0.1f), Mathf.Max(ExtrasDistance, 0f), (float)OcclusionSamples));
        }
    }
}
