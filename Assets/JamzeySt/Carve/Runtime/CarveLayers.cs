using UnityEngine;

namespace JamzeySt.Carve
{
    // Carve Layered Lit compiles only the layers a material uses (keyword _CARVE_LAYERS_L2/L3/L4), which is much faster than
    // reading the count at runtime. Change the layer count from scripts through SetLayerCount so the keyword follows.
    public static class CarveLayers
    {
        public const string CountProperty = "_LayerCount";
        const string KeywordProperty = "_CARVE_LAYERS";
        static readonly string[] s_Keywords = { "_CARVE_LAYERS_AUTO", "_CARVE_LAYERS_L2", "_CARVE_LAYERS_L3", "_CARVE_LAYERS_L4" };

        public static bool IsLayered(Material m) => m != null && m.HasProperty(CountProperty) && m.HasProperty(KeywordProperty);

        public static int GetLayerCount(Material m) => Mathf.Clamp(Mathf.RoundToInt(m.GetFloat(CountProperty)), 2, 4);

        public static void SetLayerCount(Material m, int count)
        {
            if (!IsLayered(m)) return;
            m.SetFloat(CountProperty, Mathf.Clamp(count, 2, 4));
            SyncKeyword(m);
        }

        // True when the compiled layer count matches Layer Count.
        public static bool IsKeywordInSync(Material m) => !IsLayered(m) || m.IsKeywordEnabled(s_Keywords[GetLayerCount(m) - 1]);

        public static void SyncKeyword(Material m)
        {
            if (!IsLayered(m)) return;
            int index = GetLayerCount(m) - 1;
            m.SetFloat(KeywordProperty, index);
            for (int i = 0; i < s_Keywords.Length; i++)
            {
                if (i == index) m.EnableKeyword(s_Keywords[i]);
                else m.DisableKeyword(s_Keywords[i]);
            }
        }

        // For materials leaving Carve Layered Lit.
        public static void ClearKeyword(Material m)
        {
            foreach (var k in s_Keywords) m.DisableKeyword(k);
        }
    }
}
