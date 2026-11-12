using System;
using UnityEngine;

namespace JamzeySt.Carve
{
    public enum CarveQualityPreset { Low, Medium, High, Ultra, Custom }

    // Scene-wide Carve quality. Steps and distances are relative to each material's own settings (1 = as authored).
    [Serializable]
    public struct CarveQualitySettings
    {
        [Header("Relief")]
        [Tooltip("Relief march steps (and Auto LOD), relative to each material's own.")]
        [Range(0.25f, 2f)] public float marchSteps;
        [Tooltip("How far the relief reaches: scales every material's Fade Start and Fade End. Lower flattens it closer to the camera.")]
        [Range(0.1f, 2f)] public float reliefDistance;

        [Header("Lighting")]
        [Tooltip("Self-shadow and reflection ray steps, relative to each material's own.")]
        [Range(0.25f, 2f)] public float shadowSteps;
        [Tooltip("Self-shadow, relief occlusion and cliff shading end at this many meters (0 = no limit). The relief itself stays.")]
        [Min(0f)] public float extrasDistance;
        [Tooltip("Relief occlusion directions per pixel. Auto: 8 on large rings, 4 on small ones (TAA blends alternating sets).")]
        public CarveOcclusionSamples occlusionSamples;

        [Header("Features")]
        [Range(0f, 1f)] public float selfShadow;
        [Range(0f, 1f)] public float reliefOcclusion;
        [Range(0f, 1f)] public float cliffAndSpecular;
        [Range(0f, 1f)] public float antiTiling;

        public static CarveQualitySettings For(CarveQualityPreset preset)
        {
            var s = new CarveQualitySettings
            {
                marchSteps = 1f, reliefDistance = 1f,
                shadowSteps = 1f, extrasDistance = 0f, occlusionSamples = CarveOcclusionSamples.Auto,
                selfShadow = 1f, reliefOcclusion = 1f, cliffAndSpecular = 1f, antiTiling = 1f,
            };
            switch (preset)
            {
                case CarveQualityPreset.Low:
                    s.marchSteps = 0.6f; s.reliefDistance = 0.5f;
                    s.shadowSteps = 0.5f; s.extrasDistance = 20f; s.occlusionSamples = CarveOcclusionSamples.Four;
                    s.reliefOcclusion = 0f; s.cliffAndSpecular = 0f;
                    break;
                case CarveQualityPreset.Medium:
                    s.marchSteps = 0.8f; s.reliefDistance = 0.75f;
                    s.shadowSteps = 0.75f; s.extrasDistance = 40f; s.occlusionSamples = CarveOcclusionSamples.Four;
                    break;
                case CarveQualityPreset.Ultra:
                    s.marchSteps = 1.25f; s.reliefDistance = 1.5f;
                    s.shadowSteps = 1.5f; s.occlusionSamples = CarveOcclusionSamples.Eight;
                    break;
            }
            return s;
        }

        public static string Describe(CarveQualityPreset preset)
        {
            switch (preset)
            {
                case CarveQualityPreset.Low: return "Fewer steps, relief to half distance, shading extras within 20 m, no relief occlusion or cliff shading. For slower GPUs and huge scenes.";
                case CarveQualityPreset.Medium: return "Slightly fewer steps, relief to 3/4 distance, shading extras within 40 m. Every feature stays on.";
                case CarveQualityPreset.High: return "Every material exactly as authored. The default.";
                case CarveQualityPreset.Ultra: return "More march and shadow steps, relief reaching 1.5x further, 8 occlusion directions everywhere. For screenshots and fast GPUs.";
                default: return "Your own settings below.";
            }
        }
    }
}
