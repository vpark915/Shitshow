using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The Pigeon's minimap (design doc, Tracking and Information). A top-down picture of the park, taken once at start,
// centred on the player and turned with the camera so up is the way you're looking. Police are red dots, food carts and trucks yellow squares. The target
// (the Seagull) is never a dot: it's a circle of Target Radius with them somewhere inside, and the circle only moves
// once they leave it, so the map says roughly where they are but never exactly. Put this on the HUD canvas.
public class Minimap : MonoBehaviour
{
    [Header("Map")]
    public Transform player;
    [Tooltip("Part of the world the map picture covers; keep it square")]
    public Bounds area = new Bounds(Vector3.zero, new Vector3(340, 10, 340));
    [Tooltip("Width of the map on screen, in canvas pixels")]
    public float size = 300f;
    [Tooltip("Metres from the middle of the map to its edge")]
    public float range = 60f;
    [Tooltip("Pixels in the map picture")]
    public int resolution = 1024;

    [Header("Police")]
    public Color policeColor = Color.red;
    public float dotSize = 10f;

    [Header("Food")]
    [Tooltip("Parent of the food carts and trucks; each child gets a square on the map")]
    public Transform vendors;
    public Color vendorColor = new Color(1f, 0.8f, 0.1f);

    [Header("Target")]
    [Tooltip("Who you're chasing")]
    public Transform target;
    [Tooltip("Radius in metres of the circle the target is somewhere inside")]
    public float targetRadius = 25f;
    public Color targetColor = new Color(1f, 0.55f, 0f, 0.4f);
    [Tooltip("While revealed (a chess piece eaten), the target's exact dot on the map and their outline through walls")]
    public Color revealColor = new Color(1f, 0.2f, 0.1f);

    Texture picture;
    Sprite circle;
    float scale; // canvas pixels per metre
    RectTransform pivot, world, zone, exact;
    float revealUntil;
    Outline glow;
    Vector3 zoneCentre;
    bool zonePlaced;
    readonly List<(Npc cop, RectTransform dot)> police = new List<(Npc, RectTransform)>();

    // before anyone's Start, so the crowd and the birds aren't in the picture
    void Awake() => picture = Photograph();

    IEnumerator Start()
    {
        circle = MakeCircle();
        scale = size / 2 / range;

        RectTransform frame = Make("Minimap", transform, Vector2.one * size, new Color(0.1f, 0.1f, 0.1f, 0.8f), null);
        frame.anchorMin = frame.anchorMax = frame.pivot = Vector2.zero;
        frame.anchoredPosition = new Vector2(32, 32);
        frame.gameObject.AddComponent<RectMask2D>();
        pivot = Make("Pivot", frame, Vector2.zero, null, null);
        world = Make("World", pivot, new Vector2(area.size.x, area.size.z) * scale, null, null);
        world.gameObject.AddComponent<RawImage>().texture = picture;
        if (vendors) // they never move, so placed once
            foreach (Transform vendor in vendors)
                Make("Food", world, Vector2.one * dotSize, vendorColor, null).anchoredPosition = OnMap(vendor.position);
        zone = Make("Target", world, Vector2.one * targetRadius * 2 * scale, targetColor, circle);
        exact = Make("Target Exact", world, Vector2.one * dotSize * 1.6f, revealColor, circle);
        Make("You", frame, Vector2.one * dotSize, Color.white, circle);

        yield return null; // the NPC spawner fills the park in its own Start
        foreach (Npc npc in FindObjectsByType<Npc>(FindObjectsSortMode.None))
            if (npc.officer) police.Add((npc, Make("Police", world, Vector2.one * dotSize, policeColor, circle)));
    }

    void LateUpdate()
    {
        if (!world || !player) return;
        // map up = camera forward: turn the map by the camera's heading, and slide it so the player is in the middle
        pivot.localEulerAngles = new Vector3(0, 0, Camera.main.transform.eulerAngles.y);
        world.anchoredPosition = -OnMap(player.position);

        foreach ((Npc cop, RectTransform dot) in police)
        {
            bool alive = cop && cop.enabled; // a dead officer's Npc is switched off
            dot.gameObject.SetActive(alive);
            if (alive) dot.anchoredPosition = OnMap(cop.transform.position);
        }

        bool revealed = Time.time < revealUntil && target;
        exact.gameObject.SetActive(revealed);
        if (glow && glow.transform != target) Destroy(glow); // the target changed (online: a new bird)
        if (revealed && !glow) AddOutline();
        if (glow) glow.enabled = revealed;
        zone.gameObject.SetActive(target && !revealed);
        if (!target) return;
        if (revealed) exact.anchoredPosition = OnMap(target.position);
        // keep the circle still until the target walks out of it, then drop a new one somewhere around them
        if (!zonePlaced || Flat(target.position - zoneCentre).magnitude > targetRadius)
        {
            Vector2 off = Random.insideUnitCircle * targetRadius * 0.8f;
            zoneCentre = target.position + new Vector3(off.x, 0, off.y);
            zonePlaced = true;
        }
        zone.anchoredPosition = OnMap(zoneCentre);
    }

    // for a while, show exactly where the target is: a dot on the map instead of the circle, and an outline through walls
    public void Reveal(float seconds)
    {
        if (!target) return;
        revealUntil = Mathf.Max(revealUntil, Time.time + seconds);
        if (!glow) AddOutline();
    }

    // stop revealing now (online: a new round)
    public void Hide() => revealUntil = 0;

    // the outline takes the target's renderers as it's added, so a target that changes its looks (NetBird) removes it
    // and it's added again here
    void AddOutline()
    {
        glow = target.gameObject.AddComponent<Outline>();
        glow.OutlineMode = Outline.Mode.OutlineAll;
        glow.OutlineColor = revealColor;
        glow.OutlineWidth = 6;
    }

    Vector2 OnMap(Vector3 p) => Flat(p - area.center) * scale;
    static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);

    // straight down from above, world +X to the right and +Z up, matching OnMap
    Texture Photograph()
    {
        var cam = new GameObject("Minimap Camera").AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = area.extents.z;
        cam.aspect = area.size.x / area.size.z;
        cam.transform.SetPositionAndRotation(area.center + Vector3.up * 200, Quaternion.Euler(90, 0, 0));
        cam.nearClipPlane = 1;
        cam.farClipPlane = 400;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.2f, 0.2f, 0.2f);
        var shot = new RenderTexture(resolution, Mathf.RoundToInt(resolution * area.size.z / area.size.x), 24);
        cam.targetTexture = shot;
        cam.Render();
        cam.targetTexture = null;
        Destroy(cam.gameObject);
        return shot;
    }

    static RectTransform Make(string name, Transform parent, Vector2 size, Color? color, Sprite sprite)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.sizeDelta = size;
        if (color is Color c)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = c;
            image.sprite = sprite;
            image.raycastTarget = false;
        }
        return rect;
    }

    // a soft-edged white disc for dots and the target circle
    static Sprite MakeCircle()
    {
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), Vector2.one * n / 2) / (n / 2);
                tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01((1 - d) * n / 2)));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), Vector2.one * 0.5f);
    }
}
