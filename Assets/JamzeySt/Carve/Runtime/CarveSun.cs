using UnityEngine;
using UnityEngine.Rendering;

namespace JamzeySt.Carve
{
    // Publishes the sun direction for Carve's self-shadowing: RenderSettings.sun, else the brightest active directional light.
    public static class CarveSun
    {
        static readonly int SunDirId = Shader.PropertyToID("_CarveSunDirWS");
        static Light s_Sun;
        static float s_NextSearch;
        static bool s_Hooked;

        public static Light Override { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Sun = null;
            s_NextSearch = 0f;
            Override = null;
            Hook();
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        static void Hook()
        {
            if (s_Hooked) return;
            s_Hooked = true;
            RenderPipelineManager.beginContextRendering += (ctx, cams) => Publish();
        }

        public static Light Current => Override != null ? Override : Find();

        static void Publish()
        {
            CarveGlobals.Publish();
            var sun = Current;
            if (sun == null || !sun.isActiveAndEnabled)
            {
                Shader.SetGlobalVector(SunDirId, Vector4.zero);
                return;
            }
            Vector3 d = -sun.transform.forward;
            Shader.SetGlobalVector(SunDirId, new Vector4(d.x, d.y, d.z, 1f));
        }

        static Light Find()
        {
            if (RenderSettings.sun != null && RenderSettings.sun.isActiveAndEnabled) return RenderSettings.sun;
            if (s_Sun != null && s_Sun.isActiveAndEnabled && Time.realtimeSinceStartup < s_NextSearch) return s_Sun;
            s_NextSearch = Time.realtimeSinceStartup + 1f;
            s_Sun = null;
            float best = -1f;
            foreach (var l in CarveFind.All<Light>())
            {
                if (l.type != LightType.Directional || !l.isActiveAndEnabled) continue;
                float b = l.intensity * l.color.maxColorComponent;
                if (b > best) { best = b; s_Sun = l; }
            }
            return s_Sun;
        }
    }
}
