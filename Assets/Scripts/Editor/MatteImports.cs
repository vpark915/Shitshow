using UnityEditor;
using UnityEngine;

// The "weird shine": Blender's default roughness (0.5) comes in as URP smoothness 0.5, so brick, wood, plastic and
// cloth all catch the sun like wet plastic. UE5 scenes read real because most things are rough (0.6-0.9 roughness).
// This caps the smoothness of every material imported from Assets/Blender (embedded in the .blend / .fbx), on every
// (re)import. The extracted .mat files elsewhere (Models/, Tolik_Cola benches) were capped once by hand the same way.
class MatteImports : AssetPostprocessor
{
    public override uint GetVersion() => 2;

    // from the renderers: OnPostprocessMaterial isn't called for these importers' embedded materials. Materials that
    // are .mat files already (the importer's remaps) are left alone: they're shared, and not this import's to change
    void OnPostprocessModel(GameObject root)
    {
        if (!assetPath.StartsWith("Assets/Blender/")) return;
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            foreach (Material material in r.sharedMaterials)
            {
                if (!material || AssetDatabase.Contains(material)) continue;
                // the blockout's surfaces get measured per-material smoothness from BlockoutPostprocessor (map x multiplier,
                // multipliers above this cap): capping them flattened brick to ~0.1 and marble to ~0.2
                if (assetPath.EndsWith("washingtonSquareBlockout.blend") && BlockoutPostprocessor.Surfaces.ContainsKey(material.name)) continue;
                float cap = Cap(material.name);
                foreach (string p in new[] { "_Smoothness", "_Glossiness" })
                    if (material.HasProperty(p) && material.GetFloat(p) > cap) material.SetFloat(p, cap);
            }
    }

    // ponytail: name guesses; a material that should stay glossy and isn't caught here gets its own word added
    public static float Cap(string name)
    {
        string n = name.ToLowerInvariant();
        if (Has(n, "foam", "steam")) return 0.25f;
        if (Has(n, "glass", "window", "water", "chrome", "mirror", "light", "lens", "screen")) return 1f;
        if (Has(n, "paint", "body", "car", "taxi")) return 0.55f; // car paint has a clear coat
        if (Has(n, "metal", "steel", "iron", "gold", "bronze", "alum", "rim", "hub")) return 0.5f;
        return 0.25f;
    }

    static bool Has(string n, params string[] words)
    {
        foreach (string w in words) if (n.Contains(w)) return true;
        return false;
    }
}
