using System.Collections.Generic;
using UnityEngine;

// Lights up everything under an object: a steady colour (hovered food, a bagel shield) plus a short flash that fades
// (taking a hit). Added on first use with Glow.On. Works by turning on emission in its own copies of the materials,
// which URP Lit and FlatKit both have.
// ponytail: relies on the _EMISSION shader variant being in builds; if a build doesn't glow, add an emissive material
// of each shader to the project so the variant isn't stripped.
public class Glow : MonoBehaviour
{
    static readonly int Emission = Shader.PropertyToID("_EmissionColor");

    [HideInInspector] public Color steady = Color.black;

    Material[] materials;
    Color[] rest;
    Color flash, shown = Color.black;
    float flashFrom, flashTime;

    public static Glow On(Component on) => on.TryGetComponent(out Glow glow) ? glow : on.gameObject.AddComponent<Glow>();

    void Awake()
    {
        var list = new List<Material>();
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            if (r is MeshRenderer || r is SkinnedMeshRenderer) // not sprites, lines or particles (muzzle flash, tracers)
                foreach (Material m in r.materials)
                    if (m.HasProperty(Emission)) list.Add(m);
        materials = list.ToArray();
        rest = new Color[materials.Length];
        for (int i = 0; i < materials.Length; i++)
        {
            // a material with emission switched off can still hold a colour (the pigeon's is white): that isn't its glow
            rest[i] = materials[i].IsKeywordEnabled("_EMISSION") ? materials[i].GetColor(Emission) : Color.black;
            materials[i].EnableKeyword("_EMISSION");
            materials[i].SetColor(Emission, rest[i]);
        }
    }

    public void Flash(Color color, float seconds)
    {
        flash = color;
        flashFrom = Time.time;
        flashTime = seconds;
    }

    void LateUpdate()
    {
        float fade = flashTime > 0 ? Mathf.Clamp01(1 - (Time.time - flashFrom) / flashTime) : 0;
        Color now = steady + flash * fade;
        if (now == shown) return;
        shown = now;
        for (int i = 0; i < materials.Length; i++) materials[i].SetColor(Emission, rest[i] + now);
    }
}
