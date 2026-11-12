using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

// The Pigeon's minimap (design doc, Tracking and Information). A top-down picture of the park, taken once at start,
// centred on the player and turned with the camera so up is the way you're looking. Police are red dots, food carts and trucks yellow squares. The target
// (the Seagull) is never a dot: it's a circle of Target Radius with them somewhere inside, and the circle only moves
// once they leave it, so the map says roughly where they are but never exactly. When it's off the map, an arrow on the
// map's edge points to it. The Seagull's unfinished tasks are blue diamonds (HuntMatch moves each to where its current
// step can be done), with a blue arrow on the map's edge for one off the map; while guiding (the Seagull), a big blue
// arrow at the top of the screen points to the nearest and says how far. Every place that matters to a task's current
// step gets a hovering yellow ring, which bursts green once it's done. Landmarks (the arch, fountain, chess tables) are
// pixel icons hovering over their spot. Put this on the HUD canvas.
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
    [Tooltip("The map picture, taken in the editor (Tools > Wash Square > Bake Minimap). Empty: photographed at start, which in a build can catch the first frame before the lighting and textures have loaded")]
    public Texture2D bakedPicture;

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
    [Tooltip("The arrow on the map's edge pointing to the target when it's off the map")]
    public Color offMapColor = Color.red;

    [Header("Seagull tasks")]
    [Tooltip("Where the Seagull's tasks are (set by HuntMatch); a diamond each until done")]
    public Transform[] taskSpots = { };
    [Tooltip("Bit i set: task i is done, its diamond goes")]
    public int tasksDone;
    public Color taskColor = new Color(0.15f, 0.45f, 1f);
    [Tooltip("Show the arrow on screen to the nearest task (HuntMatch: the Seagull, during a round)")]
    public bool guide;
    [Tooltip("Keep the target faintly lit and thinly outlined (HuntMatch: the Pigeon sees the Seagull, during a round)")]
    public bool marked;
    public Color markGlow = new Color(0.35f, 0.12f, 0.05f), markOutline = new Color(1f, 0.45f, 0.25f, 0.8f);
    public float markWidth = 2f;
    [Tooltip("Places that matter to the tasks' current steps (filled by HuntMatch each frame): a hovering ring each; one that drops out bursts green")]
    public readonly HashSet<Transform> taskPlaces = new HashSet<Transform>();
    public Color placeColor = new Color(1f, 0.85f, 0.1f);
    public Color placeDoneColor = new Color(0.25f, 1f, 0.35f);
    public float placeSize = 34f;
    [Tooltip("Seconds a done place's green burst lasts")]
    public float burstTime = 0.7f;

    [Header("Landmarks")]
    [Tooltip("A pixel icon on the map for each. Icon: a picture in Resources/MinimapIcons. At: its scene objects, named as tasks name them (alternatives split by |, \"ChessTables/\" for each child); one icon over each bunch of them (Bunch metres apart at most)")]
    public Landmark[] landmarks =
    {
        new Landmark { icon = "wsp_arch", at = "WSP_Arch|Arch" },
        new Landmark { icon = "wsp_fountain", at = "WSP_Fountain|Fountain" },
        new Landmark { icon = "wsp_chess", at = "ChessTables/|WSP_Prop_ChessTable" },
    };
    public float iconSize = 30f;
    [Tooltip("Metres: landmark objects closer than this share one icon (the two corners of chess tables get one each)")]
    public float bunch = 30f;
    [Tooltip("Pixels the icons and rings bob up and down")]
    public float hover = 3f;
    public float hoverSpeed = 2.5f;

    [System.Serializable]
    public class Landmark
    {
        public string icon;
        public string at;
    }

    Texture picture;
    Sprite circle;
    float scale; // canvas pixels per metre
    RectTransform pivot, world, zone, exact, offMap;
    float revealUntil;
    Outline glow;
    bool lit; // the target's Glow is on for marked
    Vector3 zoneCentre;
    bool zonePlaced;
    readonly List<RectTransform> taskMarks = new List<RectTransform>(), taskArrows = new List<RectTransform>();
    RectTransform guideArrow;
    Text guideText;
    readonly List<(Npc cop, RectTransform dot)> police = new List<(Npc, RectTransform)>();
    readonly List<(Vector3 at, RectTransform icon)> icons = new List<(Vector3, RectTransform)>();
    readonly Dictionary<Transform, RectTransform> rings = new Dictionary<Transform, RectTransform>();
    readonly List<(RectTransform ring, float start)> bursts = new List<(RectTransform, float)>();
    readonly List<Transform> gone = new List<Transform>();
    Sprite ring;

    // before anyone's Start, so the crowd and the birds aren't in the picture
    void Awake() => picture = bakedPicture ? bakedPicture : Photograph();

    IEnumerator Start()
    {
        circle = MakeCircle();
        ring = MakeRing();
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
        Landmarks();
        zone = Make("Target", world, Vector2.one * targetRadius * 2 * scale, targetColor, circle);
        exact = Make("Target Exact", world, Vector2.one * dotSize * 1.6f, revealColor, circle);
        Make("You", frame, Vector2.one * dotSize, Color.white, circle);
        offMap = Make("Target Off Map", frame, new Vector2(2.4f, 1.6f) * dotSize, offMapColor, DamageIndicator.MakeArrow());
        // the way to the nearest task, top middle of the screen, with how far under it
        guideArrow = Make("Task Arrow", transform, new Vector2(54, 40), taskColor, DamageIndicator.MakeArrow());
        guideArrow.anchorMin = guideArrow.anchorMax = new Vector2(0.5f, 1);
        guideArrow.anchoredPosition = new Vector2(0, -70);
        guideText = Make("Distance", transform, new Vector2(200, 30), null, null).gameObject.AddComponent<Text>();
        guideText.rectTransform.anchorMin = guideText.rectTransform.anchorMax = new Vector2(0.5f, 1);
        guideText.rectTransform.anchoredPosition = new Vector2(0, -112);
        guideText.font = SubwayGUI.Font;
        guideText.fontSize = 20;
        guideText.alignment = TextAnchor.MiddleCenter;
        guideText.color = taskColor;
        guideText.raycastTarget = false;
        guideText.gameObject.AddComponent<Shadow>();

        yield return null; // the NPC spawner fills the park in its own Start
        FindPolice();
    }

    // a dot for each officer (again after a new round's new crowd: RoundReset)
    public void FindPolice()
    {
        foreach ((Npc _, RectTransform dot) in police) if (dot) Destroy(dot.gameObject);
        police.Clear();
        foreach (Npc npc in FindObjectsByType<Npc>(FindObjectsSortMode.None))
            if (npc.officer && npc.gameObject.activeInHierarchy) police.Add((npc, Make("Police", world, Vector2.one * dotSize, policeColor, circle)));
    }

    void LateUpdate()
    {
        if (!world || !player) return;
        // map up = camera forward: turn the map by the camera's heading, and slide it so the player is in the middle
        pivot.localEulerAngles = new Vector3(0, 0, Camera.main.transform.eulerAngles.y);
        world.anchoredPosition = -OnMap(player.position);

        while (taskMarks.Count < taskSpots.Length)
        {
            RectTransform mark = Make("Task", world, Vector2.one * dotSize * 1.4f, taskColor, null);
            mark.localEulerAngles = new Vector3(0, 0, 45);
            taskMarks.Add(mark);
            taskArrows.Add(Make("Task Off Map", Frame(), new Vector2(2f, 1.4f) * dotSize, taskColor, DamageIndicator.MakeArrow()));
        }
        Transform nearest = null;
        float nearestDistance = float.MaxValue;
        for (int i = 0; i < taskMarks.Count; i++)
        {
            bool show = i < taskSpots.Length && taskSpots[i] && (tasksDone & (1 << i)) == 0;
            taskMarks[i].gameObject.SetActive(show);
            taskArrows[i].gameObject.SetActive(show && Edge(taskMarks[i], taskArrows[i]));
            if (!show) continue;
            taskMarks[i].anchoredPosition = OnMap(taskSpots[i].position);
            float d = Flat(taskSpots[i].position - player.position).magnitude;
            if (d < nearestDistance) { nearestDistance = d; nearest = taskSpots[i]; }
        }
        Guide(nearest, nearestDistance);
        foreach ((Vector3 at, RectTransform icon) in icons) Hover(icon, at);
        Rings();

        foreach ((Npc cop, RectTransform dot) in police)
        {
            bool alive = cop && cop.enabled; // a dead officer's Npc is switched off
            dot.gameObject.SetActive(alive);
            if (alive)
            {
                dot.anchoredPosition = OnMap(cop.transform.position);
                dot.rotation = transform.rotation; // upright on screen while the map turns under it
            }
        }

        bool revealed = Time.time < revealUntil && target;
        exact.gameObject.SetActive(revealed);
        if (glow && glow.transform != target) Destroy(glow); // the target changed (online: a new bird)
        bool marking = marked && target;
        if ((revealed || marking) && !glow) AddOutline();
        if (glow)
        {
            glow.enabled = revealed || marking;
            // revealed: the bold outline through walls; otherwise just a thin one where it's in sight
            Outline.Mode mode = revealed ? Outline.Mode.OutlineAll : Outline.Mode.OutlineVisible;
            if (glow.OutlineMode != mode) glow.OutlineMode = mode;
            float width = revealed ? 6 : markWidth;
            if (glow.OutlineWidth != width) glow.OutlineWidth = width;
            Color color = revealed ? revealColor : markOutline;
            if (glow.OutlineColor != color) glow.OutlineColor = color;
        }
        if (target && (marking || lit))
        {
            Glow.On(target).steady = marking ? markGlow : Color.black; // a slight glow from within
            lit = marking;
        }
        zone.gameObject.SetActive(target && !revealed);
        offMap.gameObject.SetActive(false);
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

        // off the map: an arrow on the map's edge, in the target's direction
        offMap.gameObject.SetActive(Edge(revealed ? exact : zone, offMap));
    }

    RectTransform Frame() => (RectTransform)pivot.parent;

    // an icon over each landmark found in the scene
    void Landmarks()
    {
        Transform[] scene = FindObjectsByType<Transform>(FindObjectsSortMode.None);
        foreach (Landmark landmark in landmarks)
        {
            var picture = Resources.Load<Texture2D>("MinimapIcons/" + landmark.icon);
            if (!picture) continue;
            picture.filterMode = FilterMode.Point; // pixel art stays pixels
            var sprite = Sprite.Create(picture, new Rect(0, 0, picture.width, picture.height), Vector2.one * 0.5f);
            foreach (Vector3 at in Middles(landmark.at, scene))
            {
                RectTransform icon = Hovering(landmark.icon, sprite, Color.white, iconSize);
                icon.GetChild(0).gameObject.AddComponent<Shadow>().effectDistance = new Vector2(1.5f, -1.5f);
                icons.Add((at, icon));
            }
        }
    }

    // the middle of each bunch of what `at` names (only the outermost of nested matches), by what's drawn
    List<Vector3> Middles(string at, Transform[] scene)
    {
        var found = new List<Transform>();
        foreach (string one in at.Split('|'))
            foreach (Transform t in scene)
            {
                if (one.EndsWith("/")) { if (t.name == one.TrimEnd('/')) foreach (Transform child in t) found.Add(child); }
                else if (one != "" && t.name.StartsWith(one)) found.Add(t);
            }
        found.RemoveAll(t => !t.gameObject.activeInHierarchy || found.Exists(other => other != t && t.IsChildOf(other)));
        var bunches = new List<Bounds>();
        foreach (Transform t in found)
        {
            Bounds box = new Bounds(t.position, Vector3.zero);
            bool drawn = false;
            foreach (Renderer r in t.GetComponentsInChildren<Renderer>())
                if (drawn) box.Encapsulate(r.bounds); else { box = r.bounds; drawn = true; }
            int near = bunches.FindIndex(b => Flat(b.center - box.center).magnitude < bunch);
            if (near < 0) { bunches.Add(box); continue; }
            Bounds grown = bunches[near];
            grown.Encapsulate(box);
            bunches[near] = grown;
        }
        return bunches.ConvertAll(b => b.center);
    }

    // a ring per task place; one that's dropped out of Task Places (done) bursts green and goes
    void Rings()
    {
        foreach (Transform place in taskPlaces)
            if (place && !rings.ContainsKey(place)) rings[place] = Hovering("Task Place", ring, placeColor, placeSize);
        gone.Clear();
        foreach (var (place, mark) in rings) if (!place || !taskPlaces.Contains(place)) gone.Add(place);
        foreach (Transform place in gone)
        {
            RectTransform mark = rings[place];
            rings.Remove(place);
            if (!place) { Destroy(mark.gameObject); continue; } // gone from the scene, not done
            Make("Flash", mark.GetChild(0), Vector2.one * placeSize * 0.8f, placeDoneColor, circle);
            bursts.Add((mark, Time.time));
        }
        float pulse = 1 + 0.08f * Mathf.Sin(Time.time * hoverSpeed * 2);
        foreach (var (place, mark) in rings)
        {
            Hover(mark, place.position);
            mark.GetChild(0).localScale = Vector3.one * pulse;
        }
        for (int i = bursts.Count - 1; i >= 0; i--)
        {
            (RectTransform mark, float start) = bursts[i];
            float t = (Time.time - start) / burstTime;
            if (t >= 1 || !mark) { if (mark) Destroy(mark.gameObject); bursts.RemoveAt(i); continue; }
            mark.rotation = transform.rotation;
            Transform picture = mark.GetChild(0);
            picture.localScale = Vector3.one * (1 + 1.6f * Mathf.Sqrt(t)); // pops out fast, then slows
            Color c = placeDoneColor;
            c.a = 1 - t;
            picture.GetComponent<Image>().color = c;
            c.a = (1 - t) * 0.6f;
            picture.GetChild(0).GetComponent<Image>().color = c;
        }
    }

    // drop every task ring at once, with no burst (a new round, or the round's over)
    public void ClearPlaces()
    {
        taskPlaces.Clear();
        foreach (RectTransform mark in rings.Values) if (mark) Destroy(mark.gameObject);
        rings.Clear();
    }

    // a holder at a spot on the map that stays upright on screen, with its picture inside bobbing up and down
    RectTransform Hovering(string name, Sprite sprite, Color color, float pixels)
    {
        RectTransform holder = Make(name, world, Vector2.zero, null, null);
        Make("Picture", holder, Vector2.one * pixels, color, sprite);
        return holder;
    }

    void Hover(RectTransform holder, Vector3 at)
    {
        holder.anchoredPosition = OnMap(at);
        holder.rotation = transform.rotation;
        float phase = (at.x + at.z) * 0.37f; // not all in step
        ((RectTransform)holder.GetChild(0)).anchoredPosition = Vector2.up * Mathf.Sin(Time.time * hoverSpeed + phase) * hover;
    }

    // `arrow` on the map's edge pointing at `mark` (on the map picture) if that's off the map; false if it's on it
    bool Edge(RectTransform mark, RectTransform arrow)
    {
        Vector2 shown = pivot.localRotation * (world.anchoredPosition + mark.anchoredPosition);
        float half = size / 2, far = Mathf.Max(Mathf.Abs(shown.x), Mathf.Abs(shown.y));
        if (far <= half) return false;
        arrow.anchoredPosition = shown * (half - dotSize) / far;
        arrow.localEulerAngles = new Vector3(0, 0, Vector2.SignedAngle(Vector2.up, shown));
        return true;
    }

    // the arrow on screen: which way `spot` is from where the camera looks, flat, and how far
    void Guide(Transform spot, float metres)
    {
        bool on = guide && spot && Camera.main;
        guideArrow.gameObject.SetActive(on);
        guideText.gameObject.SetActive(on);
        if (!on) return;
        Vector3 way = Camera.main.transform.InverseTransformDirection(spot.position - player.position);
        guideArrow.localEulerAngles = new Vector3(0, 0, Vector2.SignedAngle(Vector2.up, new Vector2(way.x, way.z)));
        guideText.text = metres < 3 ? "here" : Mathf.RoundToInt(metres) + " m";
    }

    // for a while, show exactly where the target is: a dot on the map instead of the circle, and an outline through walls
    public void Reveal(float seconds)
    {
        if (!target) return;
        revealUntil = Mathf.Max(revealUntil, Time.time + seconds);
        if (!glow) AddOutline();
    }

    // stop revealing now
    public void Hide() => revealUntil = 0;

    // a new round (HuntMatch): no reveal, and the target's circle dropped afresh
    public void ResetRound()
    {
        revealUntil = 0;
        zonePlaced = false;
        ClearPlaces();
    }

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

    // straight down from above, world +X to the right and +Z up, matching OnMap (Bake Minimap uses it in the editor too)
    public RenderTexture Photograph()
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
        // bare minimum: PC_RPAsset's second renderer (Minimap_Renderer: Forward+, no SSGI, GTAO or fog), no shadows,
        // post-processing, depth or colour copies
        UniversalAdditionalCameraData bare = cam.GetUniversalAdditionalCameraData();
        bare.SetRenderer(1);
        bare.renderShadows = false;
        bare.renderPostProcessing = false;
        bare.antialiasing = AntialiasingMode.None;
        bare.requiresDepthOption = CameraOverrideOption.Off;
        bare.requiresColorOption = CameraOverrideOption.Off;
        var shot = new RenderTexture(resolution, Mathf.RoundToInt(resolution * area.size.z / area.size.x), 24);
        cam.targetTexture = shot;
        cam.Render();
        cam.targetTexture = null;
        if (Application.isPlaying) Destroy(cam.gameObject); else DestroyImmediate(cam.gameObject);
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

    // a soft-edged white ring for task places
    static Sprite MakeRing()
    {
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), Vector2.one * n / 2) / (n / 2);
                tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01((0.12f - Mathf.Abs(d - 0.82f)) * n / 2)));
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), Vector2.one * 0.5f);
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
