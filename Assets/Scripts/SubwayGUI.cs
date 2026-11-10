using UnityEngine;
using UnityEngine.UI;

// The menus as NYC subway signs, to match the HUD (WeaponHUD): a black sign under a thin white rule, white Inter, and
// every choice a row led by a line bullet numbered 1, 2, 3... in the colour of the subway line that number names.
// IMGUI, on the 1280x720 canvas the menus were already laid out on: Begin, then Title / Button / Slider / Field / Note, then End.
// Signs float (a slow sway and bob, like the HUD's idle), pop in when they appear, and a chosen item bursts the whole
// sign away before its button reports the click, so the burst is seen before the menu acts.
public static class SubwayGUI
{
    public static readonly Color Black = Color.black, White = Color.white, Red = Hex(0xEE352E), Grey = Hex(0xA7A9AC);

    // MTA line colours by digit: 1 2 3 red (7th Ave), 4 5 6 green (Lexington), 7 purple (Flushing), 9 red (the old 1/9
    // skip-stop). 0 and 8 have no line, so they take the S shuttle's grey.
    public static readonly Color[] LineColor =
    {
        Hex(0x808183), Hex(0xEE352E), Hex(0xEE352E), Hex(0xEE352E), Hex(0x00933C),
        Hex(0x00933C), Hex(0x00933C), Hex(0xB933AD), Hex(0x808183), Hex(0xEE352E),
    };

    public static Color Hex(int rgb) => new Color((rgb >> 16 & 255) / 255f, (rgb >> 8 & 255) / 255f, (rgb & 255) / 255f);

    static Font font;
    static Texture2D circle;
    static GUIStyle title, label, bullet, note, field, body, sliderLabel, sliderValue, smallTitle, smallBody;
    static int number; // the next button's bullet
    static string hovered; // the button the mouse was last over, and the frame it was
    static int hoveredFrame = -10;
    static float hoverStart = -10; // when the mouse came onto it: the row's little burst
    static Matrix4x4 baseMatrix = Matrix4x4.identity;

    // per sign (keyed by where it sits): when it appeared, and a burst under way
    class SignState { public float appear, burst = -1; public int lastFrame = -10, burstButton = -1; public bool fire, opened; }
    static readonly System.Collections.Generic.Dictionary<string, SignState> states = new();
    static SignState current;
    static int menuFrame = -10; // the last frame a menu (Begin/End) was drawn

    // a menu is up (drawn this frame or the last): CursorLock frees the cursor for it, and locks it again once it's gone
    public static bool MenuOpen => Time.frameCount - menuFrame <= 1;

    const float Pop = 0.05f, Settle = 0.12f, BurstUp = 0.06f, BurstOut = 0.16f, HoverUp = 0.04f, HoverBack = 0.14f;

    public static Font Font => font ? font : font = Resources.Load<Font>("Fonts/Inter-Regular");

    static void Styles()
    {
        if (title != null && title.font) return;
        circle = Resources.Load<Texture2D>("SubwayHUD/hud_circle");
        GUIStyle Make(int size, FontStyle style, Color color) => new GUIStyle
        {
            font = Font, fontSize = size, fontStyle = style, normal = { textColor = color }, richText = true,
            alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Overflow,
        };
        title = Make(30, FontStyle.Bold, White);
        label = Make(22, FontStyle.Bold, White);
        bullet = Make(20, FontStyle.Bold, White);
        bullet.alignment = TextAnchor.MiddleCenter;
        note = Make(15, FontStyle.Normal, Grey);
        note.wordWrap = true;
        body = Make(18, FontStyle.Normal, White);
        body.wordWrap = true;
        body.alignment = TextAnchor.UpperLeft;
        smallTitle = Make(20, FontStyle.Bold, White);
        smallBody = Make(14, FontStyle.Normal, White);
        smallBody.wordWrap = true;
        smallBody.alignment = TextAnchor.UpperLeft;
        sliderLabel = Make(18, FontStyle.Bold, White);
        sliderValue = Make(18, FontStyle.Normal, Grey);
        sliderValue.alignment = TextAnchor.MiddleRight;
        field = Make(22, FontStyle.Bold, White);
        field.padding = new RectOffset(12, 12, 0, 0);
    }

    // scales IMGUI to the 1280x720 layout; call first in OnGUI
    public static void Scale()
    {
        float s = Screen.height / 720f;
        GUI.matrix = baseMatrix = Matrix4x4.Scale(new Vector3(s, s, 1));
    }

    // sways and bobs the sign about its middle, pops it in when it appears, bursts it away when an item is chosen (or
    // from burstAt, for a sign the caller retires itself). A new key is a new sign, which pops in. Unscaled time: the
    // pause menu runs with the game stopped.
    static SignState Float(Rect r, string key = null, float burstAt = -1)
    {
        float t = Time.unscaledTime;
        key ??= r.ToString();
        if (!states.TryGetValue(key, out SignState st)) states[key] = st = new SignState();
        // (re)appeared: not drawn last frame. Counted in frames, not seconds: a slow frame (a camera cut, a hitch) isn't
        // the sign going away
        st.opened = Time.frameCount - st.lastFrame > 1;
        if (st.opened) { st.appear = t; st.burst = -1; st.fire = false; }
        st.lastFrame = Time.frameCount;

        float size;
        if (burstAt >= 0)
        {
            float b = t - burstAt;
            size = b < BurstUp ? Mathf.Lerp(1f, 1.12f, b / BurstUp) : Mathf.Lerp(1.12f, 0f, (b - BurstUp) / BurstOut);
        }
        else if (st.burst >= 0)
        {
            float b = t - st.burst;
            size = b < BurstUp ? Mathf.Lerp(1f, 1.12f, b / BurstUp) : Mathf.Lerp(1.12f, 0f, (b - BurstUp) / BurstOut);
            // fire on a repaint, so the menu changes between frames and not between IMGUI's layout and repaint passes
            if (b >= BurstUp + BurstOut && Event.current.type == EventType.Repaint) { st.fire = true; st.burst = -1; st.appear = t; size = 0; }
        }
        else
        {
            float a = t - st.appear;
            size = a < Pop ? Mathf.Lerp(0.3f, 1.08f, a / Pop) : a < Settle ? Mathf.Lerp(1.08f, 1f, (a - Pop) / (Settle - Pop)) : 1f;
        }

        float phase = r.x * 0.013f + r.y * 0.007f;
        float angle = 1.2f * Mathf.Sin(t * 0.9f + phase);
        Vector3 middle = new Vector3(r.center.x, r.center.y + 4 * Mathf.Sin(t * 1.3f + phase), 0);
        GUI.matrix = baseMatrix * Matrix4x4.TRS(middle, Quaternion.Euler(0, 0, angle), Vector3.one * Mathf.Max(size, 0.001f))
                     * Matrix4x4.Translate(-(Vector3)r.center);
        return st;
    }


    static void Fill(Rect r, Color c)
    {
        Color was = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = was;
    }

    // the sign itself: black, with the white rule a little below its top edge
    public static void Sign(Rect r)
    {
        Fill(r, Black);
        Fill(new Rect(r.x, r.y + 8, r.width, 3), White);
    }

    // a sign sized to its contents is hard in IMGUI, so the caller gives the height
    public static void Begin(Rect r)
    {
        Styles();
        menuFrame = Time.frameCount;
        current = Float(r);
        if (current.opened) Sfx.Play("menu open"); // the sidestick as a menu comes up (not as it pops back after a choice)
        Sign(r);
        number = 1;
        GUILayout.BeginArea(new Rect(r.x + 18, r.y + 20, r.width - 36, r.height - 30));
    }

    public static void End()
    {
        GUILayout.EndArea();
        GUI.matrix = baseMatrix;
        current = null;
    }

    public static void Title(string text)
    {
        GUILayout.Label(text, title, GUILayout.Height(44));
        GUILayout.Space(6);
    }

    // a menu choice: the numbered bullet, then the words. Hover lifts the row out of the black. A click starts the
    // sign's burst; this returns true once the burst is over.
    public static bool Button(string text, bool enabled = true)
    {
        Rect r = GUILayoutUtility.GetRect(0, 44, GUILayout.ExpandWidth(true));
        int n = number++;
        SignState st = current;
        if (st != null && st.fire && st.burstButton == n) { st.fire = false; return true; }
        bool chosen = st != null && st.burst >= 0 && st.burstButton == n;
        bool hot = enabled && (chosen || st?.burst < 0 && r.Contains(Event.current.mousePosition));
        // the sidestick each time the mouse comes onto a button (once, not every frame it stays there), and a little burst
        if (enabled && (st == null || st.burst < 0) && Event.current.type == EventType.Repaint && r.Contains(Event.current.mousePosition))
        {
            if (hovered != text || Time.frameCount > hoveredFrame + 1) { Sfx.Play("menu hover"); hoverStart = Time.unscaledTime; }
            hovered = text;
            hoveredFrame = Time.frameCount;
        }
        // the burst: the row swells a little and its highlight flashes, then both settle
        float h = hot && !chosen && hovered == text ? Time.unscaledTime - hoverStart : -1;
        float swell = h < 0 ? 0 : h < HoverUp ? h / HoverUp : h < HoverUp + HoverBack ? 1 - (h - HoverUp) / HoverBack : 0;
        Matrix4x4 rowMatrix = GUI.matrix;
        if (swell > 0) GUIUtility.ScaleAroundPivot(Vector2.one * (1 + 0.07f * swell), r.center); // (the pivot from inside the layout area)
        if (hot) Fill(new Rect(r.x - 8, r.y, r.width + 16, r.height), new Color(1, 1, 1, chosen ? 0.35f : 0.14f + 0.2f * swell));
        Color was = GUI.color;
        if (!enabled) GUI.color = new Color(1, 1, 1, 0.35f);
        Rect disc = new Rect(r.x, r.y + 4, 36, 36);
        Color c = GUI.color;
        GUI.color = LineColor[n % 10] * c;
        GUI.DrawTexture(disc, circle);
        GUI.color = c;
        GUI.Label(disc, (n % 10).ToString(), bullet);
        GUI.Label(new Rect(r.x + 50, r.y, r.width - 50, r.height), text, label);
        GUI.color = was;
        GUI.matrix = rowMatrix; // clicks land on the row where it sits, not where the burst has it
        if (!enabled || !GUI.Button(r, GUIContent.none, GUIStyle.none)) return false;
        if (st == null) { Sfx.Play("menu click"); return true; } // outside Begin/End: no burst, act now
        if (st.burst < 0) { st.burst = Time.unscaledTime; st.burstButton = n; Sfx.Play("menu click"); }
        return false;
    }

    // a setting: its name and value on one line, a track under them filled in red up to a white knob; click or drag
    // anywhere along it. shown: the value as text (a percentage if not given)
    public static float Slider(string text, float value, float min, float max, string shown = null)
    {
        Rect r = GUILayoutUtility.GetRect(0, 52, GUILayout.ExpandWidth(true));
        GUI.Label(new Rect(r.x, r.y, r.width, 26), text, sliderLabel);
        GUI.Label(new Rect(r.x, r.y, r.width, 26), shown ?? Mathf.RoundToInt(Mathf.InverseLerp(min, max, value) * 100) + "%", sliderValue);
        Rect track = new Rect(r.x + 10, r.y + 36, r.width - 20, 4), hit = new Rect(r.x, r.y + 24, r.width, 26);
        int id = GUIUtility.GetControlID(FocusType.Passive);
        Event e = Event.current;
        float At(Vector2 mouse) => Mathf.Lerp(min, max, Mathf.InverseLerp(track.x, track.xMax, mouse.x));
        switch (e.GetTypeForControl(id))
        {
            case EventType.MouseDown when e.button == 0 && hit.Contains(e.mousePosition):
                GUIUtility.hotControl = id;
                value = At(e.mousePosition);
                e.Use();
                break;
            case EventType.MouseDrag when GUIUtility.hotControl == id:
                value = At(e.mousePosition);
                e.Use();
                break;
            case EventType.MouseUp when GUIUtility.hotControl == id:
                GUIUtility.hotControl = 0;
                e.Use();
                break;
        }
        float k = Mathf.InverseLerp(min, max, value);
        Fill(track, new Color(1, 1, 1, 0.25f));
        Fill(new Rect(track.x, track.y, track.width * k, track.height), Red);
        bool held = GUIUtility.hotControl == id, over = hit.Contains(e.mousePosition);
        float knob = held ? 22 : over ? 20 : 18;
        GUI.DrawTexture(new Rect(track.x + track.width * k - knob / 2, track.center.y - knob / 2, knob, knob), circle);
        return value;
    }

    // a text box: white type over dark grey, underlined in white
    public static string Field(string text, int maxLength, string placeholder = "")
    {
        Rect r = GUILayoutUtility.GetRect(0, 44, GUILayout.ExpandWidth(true));
        Fill(r, new Color(0.16f, 0.16f, 0.16f));
        Fill(new Rect(r.x, r.yMax - 3, r.width, 3), White);
        if (text.Length == 0) GUI.Label(r, placeholder, new GUIStyle(field) { normal = { textColor = Grey }, fontStyle = FontStyle.Normal });
        return GUI.TextField(r, text, maxLength, field);
    }

    public static void Note(string text)
    {
        GUILayout.Space(4);
        GUILayout.Label(text, note);
    }

    // a sign with a title and running text, for match info and tutorial steps: no buttons, so no layout area. Keyed by
    // where it sits (not its height, which changes as tasks get done) unless given a key; badge > 0 leads the heading
    // with that numbered line bullet; burstAt bursts it away from that time (unscaled).
    public static void Board(Rect r, string heading, string text, string key = null, int badge = 0, float burstAt = -1)
    {
        Styles();
        BoardIn(r, heading, text, key, badge, burstAt, title, body, 18, 62);
    }

    // the same, smaller: for a board that stays up while playing (HuntMatch's tasks), out of the way in the corner
    public static void SmallBoard(Rect r, string heading, string text, string key = null)
    {
        Styles();
        BoardIn(r, heading, text, key, 0, -1, smallTitle, smallBody, 14, 44);
    }

    public static float SmallBodyHeight(string text, float width)
    {
        Styles();
        return smallBody.CalcHeight(new GUIContent(text), width - 28);
    }

    static void BoardIn(Rect r, string heading, string text, string key, int badge, float burstAt, GUIStyle titleStyle, GUIStyle bodyStyle, float titleY, float bodyY)
    {
        Float(new Rect(r.x, r.y, r.width, 0), key, burstAt);
        Sign(r);
        float x = r.x + 18;
        if (badge > 0)
        {
            Rect disc = new Rect(x, r.y + 20, 38, 38);
            Color was = GUI.color;
            GUI.color = LineColor[badge % 10];
            GUI.DrawTexture(disc, circle);
            GUI.color = was;
            GUI.Label(disc, badge.ToString(), new GUIStyle(bullet) { fontSize = badge > 9 ? 16 : 20 });
            x += 50;
        }
        float pad = bodyStyle == body ? 18 : 14;
        if (badge == 0) x = r.x + pad;
        GUI.Label(new Rect(x, r.y + titleY, r.xMax - pad - x, titleStyle.fontSize + 10), heading, titleStyle);
        GUI.Label(new Rect(r.x + pad, r.y + bodyY, r.width - pad * 2, r.height - bodyY - 8), text, bodyStyle);
        GUI.matrix = baseMatrix;
    }

    public static float BodyHeight(string text, float width)
    {
        Styles();
        return body.CalcHeight(new GUIContent(text), width - 36);
    }

    // one line of big centred type on a sign, mid screen ("Respawning...")
    public static void Banner(float y, string text)
    {
        Styles();
        float w = title.CalcSize(new GUIContent(text)).x + 48;
        Rect r = new Rect(640 - w / 2, y, w, 62);
        Sign(r);
        var centred = new GUIStyle(title) { alignment = TextAnchor.MiddleCenter };
        GUI.Label(new Rect(r.x, r.y + 10, r.width, r.height - 10), text, centred);
    }

    // uGUI prompts (eat, ride, food timers): a black sign with the white rule behind the text, sized to it each frame.
    // Like the menus it pops in when it appears (looking at something) or changes to something else, and Burst sends
    // it off with the menus' burst (pressing E on it).
    public static void Back(Text text)
    {
        if (!text || text.GetComponent<Backing>()) return;
        text.font = Font ? Font : text.font;
        text.color = White;
        if (text.TryGetComponent(out Outline o)) o.enabled = false;
        if (text.TryGetComponent(out Shadow s)) s.enabled = false;
        text.gameObject.AddComponent<Backing>();
    }

    // the prompt was acted on: a copy of it bursts away where it stood, and the prompt itself pops back in a moment
    // later if it's still wanted (a cart looked at after buying from it)
    public static void Burst(Text text)
    {
        if (text && text.TryGetComponent(out Backing backing)) backing.Burst();
    }

    class Backing : MonoBehaviour
    {
        Text text;
        RectTransform sign;
        Image rule;
        const float Pad = 14;

        Vector3 baseScale;
        Vector2 baseAt;
        string shown; // what it said last frame, digits aside: new words pop it in again
        float appear = -10, hideUntil, burstFrom = -1; // burstFrom >= 0: this is a copy bursting away

        void Awake()
        {
            text = GetComponent<Text>();
            baseScale = text.rectTransform.localScale;
            baseAt = text.rectTransform.anchoredPosition;
            var go = new GameObject(name + " Sign", typeof(RectTransform), typeof(Image));
            sign = go.GetComponent<RectTransform>();
            sign.SetParent(transform.parent, false);
            sign.SetSiblingIndex(transform.GetSiblingIndex()); // just before the text, so drawn behind it
            var back = go.GetComponent<Image>();
            back.color = Black;
            back.raycastTarget = false;
            rule = new GameObject("Rule", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            rule.rectTransform.SetParent(sign, false);
            rule.rectTransform.anchorMin = new Vector2(0, 1);
            rule.rectTransform.anchorMax = new Vector2(1, 1);
            rule.rectTransform.pivot = new Vector2(0.5f, 1);
            rule.rectTransform.anchoredPosition = new Vector2(0, -5);
            rule.rectTransform.sizeDelta = new Vector2(0, 2);
            rule.color = White;
            rule.raycastTarget = false;
        }

        public void Burst()
        {
            if (burstFrom >= 0 || !sign.gameObject.activeSelf || Time.unscaledTime < hideUntil) return;
            // the copy keeps the words as they were; nothing else holds it, so it stays up till its burst is done
            var copy = Instantiate(gameObject, transform.parent).GetComponent<Backing>();
            copy.name = name + " (burst)";
            copy.transform.SetSiblingIndex(transform.GetSiblingIndex() + 1);
            copy.baseScale = baseScale;
            copy.baseAt = baseAt;
            copy.burstFrom = Time.unscaledTime;
            copy.text.enabled = true;
            hideUntil = Time.unscaledTime + BurstUp + BurstOut;
        }

        void LateUpdate()
        {
            float now = Time.unscaledTime;
            bool on = text.isActiveAndEnabled && text.text.Length > 0;
            if (sign.gameObject.activeSelf != on) sign.gameObject.SetActive(on);
            RectTransform t = text.rectTransform;
            if (!on) { shown = null; t.localScale = baseScale; t.anchoredPosition = baseAt; return; }
            // looked at something, or something else; a countdown ticking (the food timers) isn't new words
            string words = System.Text.RegularExpressions.Regex.Replace(text.text, "[0-9]", "");
            if (words != shown) { shown = words; appear = now; }

            // where the text actually sits inside its rect, from its alignment
            Vector2 size = new Vector2(Mathf.Min(text.preferredWidth, t.rect.width + 2000), text.preferredHeight);
            Vector2 align = new Vector2((int)text.alignment % 3 / 2f, 1 - (int)text.alignment / 3 / 2f);
            if (text.horizontalOverflow == HorizontalWrapMode.Wrap) size.x = Mathf.Min(size.x, t.rect.width);
            Vector2 corner = t.rect.min + Vector2.Scale(t.rect.size - size, align); // bottom left of the text, in the text's space

            float k;
            if (burstFrom >= 0)
            {
                float b = now - burstFrom;
                if (b >= BurstUp + BurstOut) { Destroy(gameObject); return; }
                k = b < BurstUp ? Mathf.Lerp(1f, 1.12f, b / BurstUp) : Mathf.Lerp(1.12f, 0f, (b - BurstUp) / BurstOut);
            }
            else
            {
                float a = now - Mathf.Max(appear, hideUntil);
                k = now < hideUntil ? 0 : a < Pop ? Mathf.Lerp(0.3f, 1.08f, a / Pop) : a < Settle ? Mathf.Lerp(1.08f, 1f, (a - Pop) / (Settle - Pop)) : 1f;
            }
            // scaled about the middle of the words, not the text box's pivot
            k = Mathf.Max(k, 0.001f);
            Vector2 middle = corner + size / 2;
            t.localScale = baseScale * k;
            t.anchoredPosition = baseAt + Vector2.Scale(middle * (1 - k), baseScale);

            sign.anchorMin = sign.anchorMax = t.anchorMin == t.anchorMax ? t.anchorMin : new Vector2(0.5f, 0.5f);
            sign.pivot = new Vector2(0, 0);
            sign.localRotation = t.localRotation;
            sign.localScale = t.localScale;
            sign.position = t.TransformPoint(corner - new Vector2(Pad, Pad));
            sign.sizeDelta = size + new Vector2(Pad * 2, Pad * 2 + 6); // the extra 6: room for the rule above the words
        }

        void OnDestroy()
        {
            if (sign) Destroy(sign.gameObject);
        }
    }
}
