using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// The HUD as NYC subway signs (made in Figma, file "Subway Signage"; exports in Resources/SubwayHUD): every number is a
// row of line bullets. Ammo and jumps bottom right, health and cash top right, and while the police hunt this bird an NYPD
// sign bottom middle. Each digit takes the colour of the real subway line it names (4 is green like the 4/5/6). Firing kicks the signs, a headshot bursts the Headshot sign over the crosshair
// (a kill bursts a Kill sign there instead, in the same style), and when nothing's going
// on the signs sway a little so the HUD never sits dead still.
// The scene's old Text fields are kept for their font and for the ride prompt; the number texts are hidden.
public class WeaponHUD : MonoBehaviour
{
    public Weapon weapon;
    public Text ammoText;
    public Text reloadsText;
    public StarterAssets.ThirdPersonController player;
    public Text jumpsText;
    public HeadRide ride;
    [Tooltip("Shown only while riding a person")]
    public Text rideText;
    public Text healthText;
    public Text moneyText;

    [Header("Signs")]
    [Tooltip("Bullet diameter, canvas pixels (1920x1080 reference)")]
    public float bulletSize = 48;
    [Tooltip("How hard a shot kicks the ammo sign (the others get a third)")]
    public float fireKick = 1f;
    [Tooltip("Seconds without shooting before the signs start to sway")]
    public float idleAfter = 1.5f;
    [Tooltip("Sway angle, degrees")]
    public float swayAngle = 1.5f;

    static readonly Color Black = Color.black, White = Color.white, Red = Hex(0xEE352E),
        Navy = Hex(0x00003A), Gold = Hex(0xF6A623);

    // MTA line colours by digit: 1 2 3 red (7th Ave), 4 5 6 green (Lexington), 7 purple (Flushing), 9 red (the old 1/9
    // skip-stop). 0 and 8 have no line, so they take the S shuttle's grey.
    static readonly Color[] LineColor =
    {
        Hex(0x808183), Hex(0xEE352E), Hex(0xEE352E), Hex(0xEE352E), Hex(0x00933C),
        Hex(0x00933C), Hex(0x00933C), Hex(0xB933AD), Hex(0x808183), Hex(0xEE352E),
    };

    Sign ammo, life, jumps, cash, wanted;
    Sign[] signs;
    Sign kill; // built like the Headshot sign, red X bullet and all: "Kill", "Double kill", "Triple kill"
    RawImage headshot;
    float headshotAt = -10, killAt = -10, lastShot = -10, markerUntil, markerStart;
    int shownJumps = -1, shownHealth = -1;
    RectTransform marker; // the hitmarker: four short strokes in an X round the crosshair
    Image[] strokes;
    Health health;

    static Color Hex(int rgb) => new Color((rgb >> 16 & 255) / 255f, (rgb >> 8 & 255) / 255f, (rgb & 255) / 255f);

    void OnEnable() { weapon.Changed += Refresh; weapon.Hit += OnHit; Weapon.Fired += OnFired; Weapon.Did += OnDid; }
    void OnDisable() { weapon.Changed -= Refresh; weapon.Hit -= OnHit; Weapon.Fired -= OnFired; Weapon.Did -= OnDid; }

    // every kind of kill (shot, roadkill, egg, silent) is a "kill" deed; a streak's deed follows it in the same frame
    void OnDid(Weapon who, string deed)
    {
        if (who != weapon || kill == null) return;
        if (deed == "assassinate") SubwayGUI.Burst(rideText); // E on the ride prompt's Silent kill
        // the snare on every kill; a streak adds the low tom over it (higher for a triple)
        if (deed == "kill") { killAt = Time.time; headshotAt = -10; kill.Show("Kill", "x"); Sfx.Play("kill"); } // a headshot kill shows only Kill
        else if (deed == "double kill") { kill.Show("Double kill", "x"); Sfx.Play("double kill"); }
        else if (deed == "triple kill") { kill.Show("Triple kill", "x"); Sfx.Play("triple kill"); }
    }

    void OnFired(Weapon who)
    {
        if (who != weapon || signs == null) return;
        lastShot = Time.time;
        foreach (Sign s in signs) s.Kick(s == ammo ? fireKick : fireKick / 3);
    }

    void OnHit(bool head)
    {
        if (head) { headshotAt = Time.time; killAt = -10; } // they share the spot: the newest sign wins
        markerStart = Time.time;
        markerUntil = Time.time + (head ? 0.35f : 0.2f);
        if (strokes != null) foreach (Image stroke in strokes) stroke.color = head ? new Color(1f, 0.2f, 0.15f) : Color.white;
    }

    void Start()
    {
        if (player) health = player.GetComponentInParent<Health>();
        if (rideText) rideText.verticalOverflow = VerticalWrapMode.Overflow; // room for the silent kill line
        SubwayGUI.Back(rideText);
        Transform canvas = healthText.transform.parent;
        Font font = healthText.font;
        foreach (Text old in new[] { ammoText, reloadsText, jumpsText, healthText, moneyText }) if (old) old.enabled = false;

        // jumps stack above ammo bottom right (the minimap has the bottom left corner); health sits left of cash, top right
        ammo = new Sign(canvas, new Vector2(1, 0), Black, White, font, bulletSize);
        life = new Sign(canvas, new Vector2(1, 1), Black, White, font, bulletSize);
        jumps = new Sign(canvas, new Vector2(1, 0), Black, White, font, bulletSize);
        cash = new Sign(canvas, new Vector2(1, 1), Black, White, font, bulletSize);
        wanted = new Sign(canvas, new Vector2(0.5f, 0), Navy, Gold, font, bulletSize, Resources.Load<Texture2D>("SubwayHUD/nypd_patch")); // bottom middle, where it's seen
        wanted.separator = Gold;
        signs = new[] { ammo, life, jumps, cash, wanted };
        life.root.gameObject.SetActive(health);
        jumps.root.gameObject.SetActive(player);

        var art = Resources.Load<Texture2D>("SubwayHUD/hud_headshot");
        headshot = new GameObject("Headshot Sign", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        headshot.texture = art;
        headshot.raycastTarget = false;
        RectTransform r = headshot.rectTransform;
        r.SetParent(canvas, false);
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = new Vector2(0, 110);
        r.sizeDelta = new Vector2(art.width, art.height) * (bulletSize / 96f) * 0.5f * 0.7f; // export is 2x the Figma sign, whose bullets are 96
        headshot.gameObject.SetActive(false);

        kill = new Sign(canvas, new Vector2(0.5f, 0.5f), Black, White, font, bulletSize * 0.7f); // the Headshot sign's scale
        kill.root.anchoredPosition = new Vector2(0, 110);
        kill.root.gameObject.SetActive(false);

        marker = new GameObject("HitMarker", typeof(RectTransform)).GetComponent<RectTransform>();
        marker.SetParent(canvas, false);
        marker.anchorMin = marker.anchorMax = new Vector2(0.5f, 0.5f);
        marker.anchoredPosition = Vector2.zero;
        strokes = new Image[4];
        for (int i = 0; i < 4; i++)
        {
            var stroke = new GameObject("Stroke", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            stroke.raycastTarget = false;
            RectTransform s = stroke.rectTransform;
            s.SetParent(marker, false);
            s.sizeDelta = new Vector2(3, 12);
            float angle = 45 + 90 * i;
            s.localRotation = Quaternion.Euler(0, 0, angle);
            s.anchoredPosition = Quaternion.Euler(0, 0, angle) * Vector2.up * 14; // out from the middle, along the stroke
            strokes[i] = stroke;
        }
        marker.gameObject.SetActive(false);
        Refresh();
    }

    void Update()
    {
        if (ride && rideText)
        {
            rideText.enabled = ride.Riding;
            rideText.text = ride.dismountAction.GetBindingDisplayString() + ": Dismount" +
                            (ride.CanAssassinate ? "\n" + ride.assassinateAction.GetBindingDisplayString() + ": Silent kill" : "");
        }
        if (marker)
        {
            marker.gameObject.SetActive(Time.time < markerUntil);
            marker.localScale = Vector3.one * Mathf.Lerp(1.4f, 1f, (Time.time - markerStart) / 0.1f); // pops in, then holds
        }
        Burst(headshot.rectTransform, Time.time - headshotAt);
        Burst(kill.root, Time.time - killAt, 0.7f);

        bool isWanted = Npc.IsWanted(weapon);
        wanted.root.gameObject.SetActive(isWanted);
        if (isWanted)
        {
            int left = Mathf.Max(0, Mathf.CeilToInt(Npc.WantedUntil - Time.time));
            wanted.Show("Wanted", left / 60 + ":" + (left % 60).ToString("00"));
            wanted.rule.color = Color.Lerp(Gold, Red, Mathf.PingPong(Time.time * 2, 1)); // the gold rule flashes red
        }
        if (player && player.ExtraJumpsLeft != shownJumps)
        {
            shownJumps = player.ExtraJumpsLeft;
            jumps.Show("Jumps", shownJumps.ToString());
            jumps.rule.color = shownJumps == 0 ? Red : White;
            jumps.Kick(fireKick / 2);
        }
        if (health)
        {
            int hp = Mathf.CeilToInt(Mathf.Max(health.Current, 0));
            if (hp != shownHealth)
            {
                shownHealth = hp;
                life.Show("Health", hp.ToString());
                life.Kick(fireKick / 2);
            }
            life.rule.color = health.Current <= health.maxHealth * 0.3f ? Color.Lerp(White, Red, Mathf.PingPong(Time.time * 3, 1)) : White;
        }
        life.home.x = -32 - cash.root.rect.width - 12;
        jumps.home.y = 32 + ammo.root.rect.height + 12;
        // the ride prompt stacks above jumps (its sign reaches the text's padding below the words)
        SubwayGUI.Raise(rideText, jumps.home.y + jumps.root.rect.height + 12 + SubwayGUI.PromptPad);

        float idle = Mathf.Clamp01((Time.time - lastShot - idleAfter) / 1f); // eases in over a second
        for (int i = 0; i < signs.Length; i++) signs[i].Animate(idle, swayAngle, i * 1.7f);
    }

    // pops in big, overshoots, settles, holds, then shrinks away (0.4 s all told at the default hold)
    static void Burst(RectTransform r, float t, float hold = 0.32f)
    {
        const float pop = 0.05f, settle = 0.11f, outTime = 0.08f;
        bool on = t < hold + outTime;
        if (r.gameObject.activeSelf != on) r.gameObject.SetActive(on);
        if (!on) return;
        float s = t < pop ? Mathf.Lerp(0.3f, 1.3f, t / pop)
                : t < settle ? Mathf.Lerp(1.3f, 1f, (t - pop) / (settle - pop))
                : t < hold ? 1f
                : Mathf.Lerp(1f, 0f, (t - hold) / outTime);
        r.localScale = Vector3.one * s;
        r.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(-6f, 0f, t / settle));
    }

    void Refresh()
    {
        if (ammo == null) return; // Changed before Start
        string reloads = weapon.reloadsLeft == 1 ? "1 reload" : weapon.reloadsLeft + " reloads";
        ammo.Show(weapon.IsReloading ? "Reloading" : "Ammo\n" + reloads, weapon.Ammo + "/" + weapon.magazineSize);
        ammo.rule.color = weapon.Ammo == 0 || weapon.IsReloading ? Red : White;
        cash.Show("Cash", "$" + weapon.Money);
    }

    // One station sign: a bar, a thin rule, the title, then the bullets. Anchored to a screen corner.
    class Sign
    {
        public RectTransform root;
        public Image rule;
        public Color separator = White;
        public Vector2 home; // where Animate puts the corner, before the sway
        readonly Text title;
        readonly RectTransform row;
        readonly float size;
        string shown;
        float kick, kickSign = 1;

        static Texture2D circle;
        static readonly System.Collections.Generic.Dictionary<char, Texture2D> glyphs = new();

        public Sign(Transform canvas, Vector2 corner, Color bar, Color ruleColor, Font font, float size, Texture2D patch = null)
        {
            this.size = size;
            root = Make("Subway Sign", canvas, typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            root.anchorMin = root.anchorMax = root.pivot = corner;
            // 32 in from the corner's edges; centred along an edge for a middle anchor
            root.anchoredPosition = home = new Vector2(corner.x == 0 ? 32 : corner.x == 1 ? -32 : 0, corner.y == 0 ? 32 : corner.y == 1 ? -32 : 0);
            Image back = root.GetComponent<Image>();
            back.color = bar;
            back.raycastTarget = false;
            var fit = root.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var stack = root.GetComponent<VerticalLayoutGroup>();
            stack.childControlWidth = stack.childControlHeight = true;
            stack.childForceExpandHeight = false;

            Make("Top", root, typeof(LayoutElement)).GetComponent<LayoutElement>().minHeight = size / 6;
            rule = Make("Rule", root, typeof(Image), typeof(LayoutElement)).GetComponent<Image>();
            rule.color = ruleColor;
            rule.raycastTarget = false;
            rule.GetComponent<LayoutElement>().minHeight = Mathf.Max(2, size / 24);

            RectTransform body = Make("Body", root, typeof(HorizontalLayoutGroup));
            var across = body.GetComponent<HorizontalLayoutGroup>();
            int pad = Mathf.RoundToInt(size / 6);
            across.padding = new RectOffset(pad, pad, pad, pad + pad / 2);
            across.spacing = pad * 1.5f;
            across.childAlignment = TextAnchor.MiddleLeft;
            across.childControlWidth = across.childControlHeight = true;
            across.childForceExpandWidth = across.childForceExpandHeight = false;

            if (patch)
            {
                var badge = Make("Patch", body, typeof(RawImage), typeof(LayoutElement)).GetComponent<RawImage>();
                badge.texture = patch;
                badge.raycastTarget = false;
                var e = badge.GetComponent<LayoutElement>();
                e.preferredHeight = size * 1.5f;
                e.preferredWidth = e.preferredHeight * patch.width / patch.height;
            }
            title = Make("Title", body, typeof(Text)).GetComponent<Text>();
            title.font = font;
            title.fontStyle = FontStyle.Bold;
            title.fontSize = Mathf.RoundToInt(size * 0.7f);
            title.lineSpacing = 0.9f;
            title.color = White;
            title.raycastTarget = false;
            title.horizontalOverflow = HorizontalWrapMode.Overflow;

            row = Make("Bullets", body, typeof(HorizontalLayoutGroup));
            var bullets = row.GetComponent<HorizontalLayoutGroup>();
            bullets.spacing = pad;
            bullets.childAlignment = TextAnchor.MiddleLeft;
            bullets.childControlWidth = bullets.childControlHeight = true;
            bullets.childForceExpandWidth = bullets.childForceExpandHeight = false;
        }

        static RectTransform Make(string name, Transform parent, params System.Type[] parts)
        {
            var go = new GameObject(name, parts);
            if (!go.TryGetComponent(out RectTransform r)) r = go.AddComponent<RectTransform>();
            r.SetParent(parent, false);
            return r;
        }

        static Texture2D Glyph(char c)
        {
            if (!glyphs.TryGetValue(c, out Texture2D t))
                glyphs[c] = t = Resources.Load<Texture2D>("SubwayHUD/glyph_" + (c == '$' ? "dollar" : c == ':' ? "colon" : c == '/' ? "slash" : c.ToString()));
            return t;
        }

        // digits become bullets in their line's colour, x a red cross bullet; $ : / sit between them bare
        public void Show(string label, string number)
        {
            title.text = label;
            if (number == shown) return;
            shown = number;
            if (!circle) circle = Resources.Load<Texture2D>("SubwayHUD/hud_circle");
            for (int i = row.childCount - 1; i >= 0; i--) Object.DestroyImmediate(row.GetChild(i).gameObject); // not Destroy: the old bullets would show for a frame beside the new
            foreach (char c in number)
            {
                bool cross = c == 'x', bare = !cross && (c < '0' || c > '9'); // x: the red cross bullet off the Headshot sign
                var spot = Make("Bullet " + c, row, typeof(RawImage), typeof(LayoutElement));
                var e = spot.GetComponent<LayoutElement>();
                e.preferredWidth = bare ? size * 0.45f : size;
                e.preferredHeight = size;
                var disc = spot.GetComponent<RawImage>();
                disc.texture = circle;
                if (!bare) disc.color = cross ? Red : LineColor[c - '0'];
                disc.enabled = !bare;
                disc.raycastTarget = false;
                var g = Make("Glyph", spot, typeof(RawImage)).GetComponent<RawImage>();
                g.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
                g.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                g.rectTransform.sizeDelta = Vector2.one * size; // the glyph art is square with the bullet, so a bare one overhangs its slot
                g.texture = Glyph(c);
                g.color = bare ? separator : White;
                g.raycastTarget = false;
            }
        }

        public void Kick(float amount)
        {
            kick = Mathf.Max(kick, amount);
            kickSign = Random.value < 0.5f ? -1 : 1;
        }

        // the shot kick snaps out and decays; underneath, an idle sway fades in once things go quiet
        public void Animate(float idle, float angle, float phase)
        {
            kick = Mathf.MoveTowards(kick, 0, Time.deltaTime * 6);
            float k = kick * kick; // eased: sharp start, soft end
            float t = Time.time;
            root.localScale = Vector3.one * (1 + k * 0.18f);
            root.localRotation = Quaternion.Euler(0, 0, kickSign * k * 5 + idle * angle * Mathf.Sin(t * 0.9f + phase));
            root.anchoredPosition = home + Vector2.up * (idle * 3 * Mathf.Sin(t * 1.3f + phase));
        }
    }
}
