using System.Collections.Generic;
using UnityEngine;

// The player's settings, kept between runs (PlayerPrefs): mouse sensitivity, and the volume of everything (Master, on
// the listener), the music (Music), the sound effects (Sfx) and the park's background sound (Ambience). Every menu has a
// Settings button that opens Panel, a sign of sliders beside the menu.
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

    // the settings sign, to the right of the menu that opened it (that menu's rect)
    public static void Panel(Rect menu)
    {
        if (!Showing) return;
        SubwayGUI.Begin(new Rect(menu.xMax + 20, menu.y, 380, 400)); // title 50, five sliders 52, Done 44, padding 30
        SubwayGUI.Title("Settings");
        float was = Sensitivity;
        Sensitivity = SubwayGUI.Slider("Mouse sensitivity", Sensitivity, 0.2f, 3, Sensitivity.ToString("0.00") + "x");
        Master = SubwayGUI.Slider("Master volume", Master, 0, 1);
        MusicVolume = SubwayGUI.Slider("Music", MusicVolume, 0, 1);
        Effects = SubwayGUI.Slider("Sound effects", Effects, 0, 1);
        AmbienceVolume = SubwayGUI.Slider("Park ambience", AmbienceVolume, 0, 1);
        if (SubwayGUI.Button("Done")) Showing = false;
        SubwayGUI.End();

        AudioListener.volume = Master;
        // in memory as they change; to disk when the sign's closed (and by Unity on quit)
        Save("sensitivity", Sensitivity); Save("master", Master); Save("music", MusicVolume); Save("effects", Effects); Save("ambience", AmbienceVolume);
        if (!Showing) PlayerPrefs.Save();
        if (was != Sensitivity) { var s = FindFirstObjectByType<GameSettings>(); if (s) s.ApplySensitivity(); }
    }
}
