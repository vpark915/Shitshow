using System.Collections.Generic;
using UnityEngine;

// The player's settings, kept between runs (PlayerPrefs): mouse sensitivity, and the volume of everything (Master, on
// the listener), the music (Music), the sound effects (Sfx) and the park's background sound (Ambience). Every menu has a
// Settings button that opens Panel, a sign of sliders beside the menu, with a button for fullscreen or windowed (Unity
// keeps that between runs itself), one for the resolution (Native, the display's own, or a smaller one) and one for the graphics, normal or potato (GraphicsQuality).
public class GameSettings : MonoBehaviour
{
    public static float Sensitivity = 1, Master = 1, MusicVolume = 1, Effects = 1, AmbienceVolume = 1;

    // the settings sign is up (beside whichever menu opened it); Music closes it when the menus go
    public static bool Showing;

    static float Load(string key, float fallback) => PlayerPrefs.GetFloat("settings." + key, fallback);
    static void Save(string key, float value) => PlayerPrefs.SetFloat("settings." + key, value);

    // each bird's own sensitivity, as its scene set it (2 or so): the setting multiplies it
    readonly Dictionary<StarterAssets.ThirdPersonController, float> baseSensitivity = new();
    float nextLook;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Begin()
    {
        // (read here, not in the fields' initializers: Unity doesn't allow PlayerPrefs there)
        Sensitivity = Load("sensitivity", 1); Master = Load("master", 1); MusicVolume = Load("music", 1);
        Effects = Load("effects", 1); AmbienceVolume = Load("ambience", 1);
        AudioListener.volume = Master;
        if (FindFirstObjectByType<GameSettings>()) return;
        // the picked resolution (Unity keeps the window between runs, but not one the display no longer offers)
        resolution = Mathf.Clamp(PlayerPrefs.GetInt("settings.resolution", 0), 0, Resolutions().Count - 1);
        if (PlayerPrefs.HasKey("settings.resolution") && !Application.isEditor) ApplyScreen(Screen.fullScreen);
        var go = new GameObject("Game Settings");
        DontDestroyOnLoad(go);
        go.AddComponent<GameSettings>();
    }

    void Update()
    {
        // birds come and go (a scene loads, the other player joins): look for new ones twice a second
        if (Time.unscaledTime < nextLook) return;
        nextLook = Time.unscaledTime + 0.5f;
        foreach (var bird in FindObjectsByType<StarterAssets.ThirdPersonController>(FindObjectsSortMode.None))
            if (!baseSensitivity.ContainsKey(bird)) baseSensitivity[bird] = bird.LookSensitivity;
        ApplySensitivity();
    }

    void ApplySensitivity()
    {
        var gone = new List<StarterAssets.ThirdPersonController>();
        foreach (var (bird, baseValue) in baseSensitivity)
            if (bird) bird.LookSensitivity = baseValue * Sensitivity;
            else gone.Add(bird);
        foreach (var bird in gone) baseSensitivity.Remove(bird);
    }

    // the resolutions to pick from: 0 is Native (the display's own, whatever its shape), then the 16:9 sizes below it, biggest first
    static List<Vector2Int> resolutions;
    static int resolution; // index into resolutions

    static List<Vector2Int> Resolutions()
    {
        if (resolutions != null) return resolutions;
        var native = new Vector2Int(Display.main.systemWidth, Display.main.systemHeight);
        resolutions = new List<Vector2Int> { native };
        // 16:9 only (within 1%, for the likes of 1366x768); the common ones always, in case the display lists few
        var sizes = new List<Vector2Int> { new(3840, 2160), new(2560, 1440), new(1920, 1080), new(1600, 900), new(1280, 720), new(960, 540) };
        foreach (Resolution r in Screen.resolutions) sizes.Add(new Vector2Int(r.width, r.height));
        foreach (Vector2Int size in sizes)
            if (size.x <= native.x && size.y <= native.y && size != native && size.x >= 640 && Mathf.Abs(size.x * 9f / (size.y * 16f) - 1) < 0.01f && !resolutions.Contains(size))
                resolutions.Add(size);
        resolutions.Sort(1, resolutions.Count - 1, Comparer<Vector2Int>.Create((a, b) => (b.x * b.y).CompareTo(a.x * a.y)));
        return resolutions;
    }

    static string ResolutionName()
    {
        Vector2Int size = Resolutions()[resolution];
        return resolution == 0 ? "Native (" + size.x + "x" + size.y + ")" : size.x + "x" + size.y;
    }

    // the picked resolution, fullscreen or windowed: fullscreen draws at it and scales to the display; a window is that
    // size, except Native windowed, which is a window two thirds of the display
    static void ApplyScreen(bool fullscreen)
    {
        Vector2Int size = Resolutions()[resolution];
        if (fullscreen) Screen.SetResolution(size.x, size.y, FullScreenMode.FullScreenWindow);
        else if (resolution == 0) Screen.SetResolution(size.x * 2 / 3, size.y * 2 / 3, FullScreenMode.Windowed);
        else Screen.SetResolution(size.x, size.y, FullScreenMode.Windowed);
    }

    // the settings sign, to the right of the menu that opened it (that menu's rect)
    public static void Panel(Rect menu)
    {
        if (!Showing) return;
        SubwayGUI.Begin(new Rect(menu.xMax + 20, menu.y, 380, 532)); // title 50, five sliders 52, four rows 44, padding 30
        SubwayGUI.Title("Settings");
        float was = Sensitivity;
        Sensitivity = SubwayGUI.Slider("Mouse sensitivity", Sensitivity, 0.2f, 3, Sensitivity.ToString("0.00") + "x");
        Master = SubwayGUI.Slider("Master volume", Master, 0, 1);
        MusicVolume = SubwayGUI.Slider("Music", MusicVolume, 0, 1);
        Effects = SubwayGUI.Slider("Sound effects", Effects, 0, 1);
        AmbienceVolume = SubwayGUI.Slider("Park ambience", AmbienceVolume, 0, 1);
        if (SubwayGUI.Button(Screen.fullScreen ? "Display: fullscreen" : "Display: windowed")) ApplyScreen(!Screen.fullScreen);
        // left goes lower, right higher, up to Native (the list runs biggest first)
        int step = SubwayGUI.Stepper("Resolution", ResolutionName(), resolution < Resolutions().Count - 1, resolution > 0);
        if (step != 0)
        {
            resolution -= step;
            PlayerPrefs.SetInt("settings.resolution", resolution);
            ApplyScreen(Screen.fullScreen);
        }
        if (SubwayGUI.Button(GraphicsQuality.Potato ? "Graphics: potato" : "Graphics: normal")) GraphicsQuality.Set(!GraphicsQuality.Potato);
        if (SubwayGUI.Button("Done")) Showing = false;
        SubwayGUI.End();

        AudioListener.volume = Master;
        // in memory as they change; to disk when the sign's closed (and by Unity on quit)
        Save("sensitivity", Sensitivity); Save("master", Master); Save("music", MusicVolume); Save("effects", Effects); Save("ambience", AmbienceVolume);
        if (!Showing) PlayerPrefs.Save();
        if (was != Sensitivity) { var s = FindFirstObjectByType<GameSettings>(); if (s) s.ApplySensitivity(); }
    }
}
