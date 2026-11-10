using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// The loading screen, for every scene change: Loading.Load(scene) instead of SceneManager.LoadScene. The scene loads in
// the background while the screen covers everything: the pigeon (Resources/Loading/pigeonLoading1 and 2) swapping
// between its two drawings, and "Loading" with its dots counting up, in the game's type. It stays a moment at least
// (no flash on a quick load) and fades once the new scene has drawn its first frame.
public class Loading : MonoBehaviour
{
    const float FrameTime = 0.15f, MinShow = 1f, FadeOut = 0.25f, DotTime = 0.35f, Tilt = 6f, TiltTime = 0.06f;
    static readonly Color Paper = Color.black, Ink = Color.white;

    static Loading instance;
    static Texture2D[] frames;
    static GUIStyle words;
    static Material trueColor; // draws the pigeon at its real colours (Resources/Shaders/GuiTrueColor)

    float shownAt, alpha;
    bool busy;

    public static bool Busy => instance && instance.busy;

    public static void Load(string scene) => Begin().StartCoroutine(instance.Run(() => SceneManager.LoadSceneAsync(scene)));

    // the game's first scene: no load (Unity's done it), but the screen stays up while the shaders warm (ShaderWarmup)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Startup() => Begin().StartCoroutine(instance.Run(null));
    public static void Load(int buildIndex) => Begin().StartCoroutine(instance.Run(() => SceneManager.LoadSceneAsync(buildIndex)));

    static Loading Begin()
    {
        if (!instance)
        {
            var go = new GameObject("Loading");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<Loading>();
        }
        return instance;
    }

    IEnumerator Run(System.Func<AsyncOperation> load)
    {
        if (busy) yield break; // one load at a time: a second click while loading does nothing
        busy = true;
        shownAt = Time.unscaledTime;
        alpha = 1;
        yield return null; // the screen is drawn before the load starts (the old scene can hitch as it goes)
        if (load != null) yield return LoadScene(load);
        // every shader the game's been seen to draw, compiled now rather than as each first shows up mid-game
        yield return ShaderWarmup.WarmUp();
        while (Time.unscaledTime - shownAt < MinShow) yield return null;
        yield return null; // the new scene's first frame (its Starts) under the screen
        yield return new WaitForEndOfFrame();
        for (float t = 0; t < FadeOut; t += Time.unscaledDeltaTime)
        {
            alpha = 1 - t / FadeOut;
            yield return null;
        }
        alpha = 0;
        busy = false;
    }

    // read the scene in the background with the pigeon animating, and only switch over once it's all in and the
    // screen's been up a moment: the switch (the old scene going, the new one's Awakes) freezes a beat whatever we
    // do, so it comes last rather than mid-animation
    IEnumerator LoadScene(System.Func<AsyncOperation> load)
    {
        Application.backgroundLoadingPriority = ThreadPriority.Low; // smaller steps per frame: smoother animation
        AsyncOperation op = load();
        op.allowSceneActivation = false;
        while (op.progress < 0.9f || Time.unscaledTime - shownAt < MinShow) yield return null;
        op.allowSceneActivation = true;
        while (!op.isDone) yield return null;
        Application.backgroundLoadingPriority = ThreadPriority.Normal;
    }

    void OnGUI()
    {
        if (alpha <= 0) return;
        GUI.depth = -1000; // over every menu and HUD
        frames ??= new[] { Resources.Load<Texture2D>("Loading/pigeonLoading1"), Resources.Load<Texture2D>("Loading/pigeonLoading2") };
        words ??= new GUIStyle { font = SubwayGUI.Font, fontSize = 34, fontStyle = FontStyle.Bold, normal = { textColor = Ink }, alignment = TextAnchor.MiddleLeft };

        SubwayGUI.Scale();
        Color was = GUI.color;
        float w = 720f * Screen.width / Screen.height; // the 720-high canvas, however wide the screen is
        Rect screen = new Rect(0, 0, w, 720);
        GUI.color = new Color(Paper.r, Paper.g, Paper.b, alpha);
        GUI.DrawTexture(screen, Texture2D.whiteTexture);
        GUI.color = new Color(1, 1, 1, alpha);

        float t = Time.unscaledTime - shownAt;
        int step = (int)(t / FrameTime);
        Texture2D frame = frames[step % 2];
        // each swap tips it the other way: a little left on one drawing, a little right on the other, swung over quickly
        float swing = Mathf.SmoothStep(0, 1, (t - step * FrameTime) / TiltTime);
        float tilt = step == 0 ? -Tilt * swing : Mathf.Lerp(step % 2 == 0 ? Tilt : -Tilt, step % 2 == 0 ? -Tilt : Tilt, swing);
        const float size = 340;
        if (!trueColor) { Shader shader = Resources.Load<Shader>("Shaders/GuiTrueColor"); if (shader) trueColor = new Material(shader); }
        Rect at = new Rect((w - size) / 2, 150, size, size);
        Matrix4x4 upright = GUI.matrix;
        Vector3 feet = new Vector3(at.center.x, at.yMax - 60, 0); // tips about its feet (canvas coordinates, like SubwayGUI's sway)
        GUI.matrix = upright * Matrix4x4.TRS(feet, Quaternion.Euler(0, 0, tilt), Vector3.one) * Matrix4x4.Translate(-feet);
        if (frame && trueColor && Event.current.type == EventType.Repaint)
        {
            trueColor.SetFloat("_Alpha", alpha);
            Graphics.DrawTexture(at, frame, trueColor);
        }
        else if (frame && !trueColor) GUI.DrawTexture(at, frame, ScaleMode.ScaleToFit);
        GUI.matrix = upright;

        // "Loading" stays put while the dots count up: laid out as if all three were there
        float full = words.CalcSize(new GUIContent("Loading...")).x;
        GUI.Label(new Rect((w - full) / 2, 500, full + 20, 50), "Loading" + new string('.', (int)(t / DotTime) % 4), words);
        GUI.color = was;

        // nothing under the screen gets clicks or keys
        if (Event.current.type != EventType.Repaint && Event.current.type != EventType.Layout) Event.current.Use();
    }
}
