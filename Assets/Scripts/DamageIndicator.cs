using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Being hit: the screen's edges flash red, and an arrow round the middle of the screen points the way the hit came
// from, turning as you turn, until it fades. Put this on the HUD canvas.
public class DamageIndicator : MonoBehaviour
{
    [Tooltip("Whose hits to show; empty finds the player's pigeon")]
    public Health player;
    public Color color = new Color(0.85f, 0f, 0f, 0.55f);
    [Tooltip("Seconds the red edges and the arrow take to fade")]
    public float fade = 1.2f;
    [Tooltip("How far from the middle of the screen the arrows sit, in canvas pixels")]
    public float arrowDistance = 170f;
    public float arrowSize = 56f;

    Image vignette;
    float hitAt = -99f;
    readonly List<(Vector3 from, Image arrow, float at)> arrows = new List<(Vector3, Image, float)>();
    Sprite arrowLook;

    void Start()
    {
        if (!player)
        {
            FoodPowers pigeon = FindFirstObjectByType<FoodPowers>();
            if (pigeon) pigeon.TryGetComponent(out player);
        }
        if (player) player.Hurt += OnHurt;

        vignette = Make("Damage Vignette", MakeVignette());
        var full = vignette.rectTransform;
        full.anchorMin = Vector2.zero;
        full.anchorMax = Vector2.one;
        full.sizeDelta = Vector2.zero;
        vignette.color = Color.clear;
        arrowLook = MakeArrow();
    }

    void OnDestroy()
    {
        if (player) player.Hurt -= OnHurt;
    }

    void OnHurt(Vector3 from)
    {
        hitAt = Time.time;
        Vector3 away = from - player.transform.position;
        if (new Vector2(away.x, away.z).magnitude < 0.5f) return; // no way to point (a blast right on top of you)
        // the same shooter again: that arrow lights up again rather than a new one stacking on it
        for (int i = 0; i < arrows.Count; i++)
            if ((arrows[i].from - from).magnitude < 2f) { arrows[i] = (from, arrows[i].arrow, Time.time); return; }
        Image arrow = Make("Damage Arrow", arrowLook);
        arrow.rectTransform.sizeDelta = new Vector2(arrowSize, arrowSize * 0.6f);
        arrows.Add((from, arrow, Time.time));
    }

    void Update()
    {
        float left = Mathf.Clamp01(1 - (Time.time - hitAt) / fade);
        vignette.color = new Color(color.r, color.g, color.b, color.a * left);

        Camera view = Camera.main;
        for (int i = arrows.Count - 1; i >= 0; i--)
        {
            (Vector3 from, Image arrow, float at) = arrows[i];
            float life = 1 - (Time.time - at) / fade;
            if (life <= 0 || !player || !view) { Destroy(arrow.gameObject); arrows.RemoveAt(i); continue; }
            // which way the hit came from, relative to where the camera is looking: 0 straight ahead, 90 to the right
            Vector3 to = from - player.transform.position, ahead = view.transform.forward;
            float angle = Vector2.SignedAngle(new Vector2(to.x, to.z), new Vector2(ahead.x, ahead.z));
            arrow.rectTransform.localEulerAngles = new Vector3(0, 0, -angle);
            arrow.rectTransform.anchoredPosition = Quaternion.Euler(0, 0, -angle) * Vector2.up * arrowDistance;
            arrow.color = new Color(color.r, color.g, color.b, Mathf.Clamp01(life * 1.5f));
        }
    }

    Image Make(string name, Sprite look)
    {
        var image = new GameObject(name, typeof(RectTransform)).AddComponent<Image>();
        image.rectTransform.SetParent(transform, false);
        image.sprite = look;
        image.raycastTarget = false;
        return image;
    }

    // clear in the middle, red-ready towards the edges (tinted by the Image's colour)
    static Sprite MakeVignette()
    {
        const int n = 128;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), Vector2.one * n / 2) / (n / 2);
                tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Pow(Mathf.Clamp01((d - 0.55f) / 0.6f), 1.5f)));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), Vector2.one * 0.5f);
    }

    // a white triangle pointing up (the minimap uses it too)
    public static Sprite MakeArrow()
    {
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float half = (1 - (y + 0.5f) / n) * n / 2; // wide at the bottom, a point at the top
                float inside = half - Mathf.Abs(x + 0.5f - n / 2f);
                tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(inside)));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), Vector2.one * 0.5f);
    }
}
