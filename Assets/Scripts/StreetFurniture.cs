using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// NYC street signs from the Figma file "Wash Square Street Signs" (exports in Resources/StreetSigns): street name blades,
// the Greenwich Village Historic District blades, guide and regulatory signs, and the white DOT panel the gym's words go
// on. Each sign is a plate as tall as asked and as wide as its art. A sign faces its transform's forward: stand in
// front of it, on the +forward side, to read it.
public static class StreetFurniture
{
    static readonly Dictionary<string, Material> materials = new();
    static Material metal;

    public static Texture2D Art(string name) => Resources.Load<Texture2D>("StreetSigns/" + name);

    // the art's size as drawn in Figma, for the sign's proportions: the imported texture is scaled to a power of two (so
    // it can be compressed with its mipmaps), which would stretch them. Signs are built by editor tools, which read the
    // source image; in a player it's the texture's own size
    public static Vector2 Size(Texture2D tex)
    {
#if UNITY_EDITOR
        if (UnityEditor.AssetImporter.GetAtPath(UnityEditor.AssetDatabase.GetAssetPath(tex)) is UnityEditor.TextureImporter importer)
        {
            importer.GetSourceTextureWidthAndHeight(out int w, out int h);
            return new Vector2(w, h);
        }
#endif
        return new Vector2(tex.width, tex.height);
    }

    // the art on a plate: cut out round its rounded corners, a little sheen like the reflective sheeting
    static Material Face(string art)
    {
        if (materials.TryGetValue(art, out Material m) && m) return m;
        Texture2D tex = Art(art);
        m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Sign " + art, mainTexture = tex };
        m.SetTexture("_BaseMap", tex);
        m.SetFloat("_AlphaClip", 1);
        m.SetFloat("_Cutoff", 0.5f);
        m.EnableKeyword("_ALPHATEST_ON");
        m.SetFloat("_Smoothness", 0.45f);
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
        return materials[art] = m;
    }

    // the bare aluminium back and edges
    static Material Metal()
    {
        if (metal) return metal;
        metal = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Sign Back", color = new Color(0.62f, 0.64f, 0.66f) };
        metal.SetFloat("_Metallic", 0.6f);
        metal.SetFloat("_Smoothness", 0.4f);
        return metal;
    }

    // a sign `height` metres tall at this spot; twoSided (street blades) carries the art on its back too
    public static Transform Sign(string art, Transform parent, Vector3 position, Quaternion rotation, float height, bool twoSided = false)
    {
        Texture2D tex = Art(art);
        if (!tex) { Debug.LogWarning("StreetFurniture: no sign art named " + art); return null; }
        Vector2 size = Size(tex);
        float width = height * size.x / size.y;
        var sign = new GameObject("Sign " + art).transform;
        sign.SetParent(parent, false);
        sign.SetPositionAndRotation(position, rotation);

        // a Quad faces -Z, so the front one turns round to face the reader on +Z
        Quad(sign, "Front", Face(art), new Vector3(0, 0, 0.002f), Quaternion.Euler(0, 180, 0), width, height);
        if (twoSided) Quad(sign, "Back", Face(art), new Vector3(0, 0, -0.002f), Quaternion.identity, width, height);
        // the plate between them, inset a touch so the art's rounded corners hide its square ones
        var plate = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
        Object.DestroyImmediate(plate.GetComponent<Collider>());
        plate.name = "Plate";
        plate.SetParent(sign, false);
        plate.localScale = new Vector3(width * 0.97f, height * 0.92f, 0.003f);
        plate.GetComponent<Renderer>().sharedMaterial = Metal();
        return sign;
    }

    static void Quad(Transform sign, string name, Material material, Vector3 at, Quaternion turn, float width, float height)
    {
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad).transform;
        Object.DestroyImmediate(quad.GetComponent<Collider>());
        quad.name = name;
        quad.SetParent(sign, false);
        quad.localPosition = at;
        quad.localRotation = turn;
        quad.localScale = new Vector3(width, height, 1);
        quad.GetComponent<Renderer>().sharedMaterial = material;
    }

    // the white DOT panel with words on it (Inter, black, sized to fit), for the gym and tutorial
    public static Transform Panel(Transform parent, Vector3 position, Quaternion rotation, float height, string words)
    {
        Transform sign = Sign("plate_info_panel", parent, position, rotation, height);
        Texture2D tex = Art("plate_info_panel");
        var canvas = new GameObject("Words", typeof(RectTransform), typeof(Canvas)).GetComponent<RectTransform>();
        canvas.SetParent(sign, false);
        canvas.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        canvas.sizeDelta = Size(tex);
        canvas.localScale = Vector3.one * (height / Size(tex).y);
        canvas.localPosition = new Vector3(0, 0, 0.004f);
        canvas.localRotation = Quaternion.Euler(0, 180, 0); // a world canvas reads from -Z, like a Quad
        var text = new GameObject("Text", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
        RectTransform r = text.rectTransform;
        r.SetParent(canvas, false);
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = new Vector2(70, 60);
        r.offsetMax = new Vector2(-70, -60); // inside the panel's inset line
        text.font = SubwayGUI.Font;
        text.color = new Color(0.07f, 0.07f, 0.07f);
        text.alignment = TextAnchor.MiddleLeft;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = 18;
        text.resizeTextMaxSize = 64;
        text.lineSpacing = 1.05f;
        text.text = words;
        text.raycastTarget = false;
        return sign;
    }
}
