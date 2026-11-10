using UnityEngine;

// The music: the SoundBank's "menu music" while a menu is up (the starting menu, or an Escape menu) and its "game music"
// while playing, each looping, flat and quiet, crossfading as the menus open and close. The menus set Open each frame;
// each track picks up where it left off. Their volumes are the bank entries', times the Music setting.
public class Music : MonoBehaviour
{
    // a menu is up. True till a menu says otherwise, so the game track doesn't blip in before the starting menu's first frame
    public static bool Open = true;

    public float fade = 0.75f;

    Track menu, game;

    class Track
    {
        public AudioSource source;
        public float volume;
        bool started;

        public Track(GameObject on, string sound)
        {
            var bank = Resources.Load<SoundBank>("SoundBank");
            SoundBank.Sound entry = bank ? bank.Find(sound) : null;
            if (entry == null || entry.clips.Length == 0 || !entry.clips[0]) return;
            source = on.AddComponent<AudioSource>();
            source.clip = entry.clips[0];
            source.loop = true;
            source.spatialBlend = 0;
            source.ignoreListenerPause = true;
            source.volume = 0;
            volume = entry.volume;
        }

        // towards full (times the setting) or silent; paused once silent, resumed where it was
        public void Fade(bool on, float seconds)
        {
            if (!source) return;
            float target = on ? volume * GameSettings.MusicVolume : 0;
            // the menus run with the game stopped (timeScale 0): unscaled time
            source.volume = Mathf.MoveTowards(source.volume, target, volume / seconds * Time.unscaledDeltaTime);
            if (on && !source.isPlaying)
            {
                if (started) source.UnPause(); else source.Play();
                started = true;
            }
            else if (!on && source.isPlaying && source.volume <= 0) source.Pause();
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Begin()
    {
        if (FindFirstObjectByType<Music>()) return;
        var go = new GameObject("Music");
        DontDestroyOnLoad(go);
        go.AddComponent<Music>();
    }

    void Start()
    {
        menu = new Track(gameObject, "menu music");
        game = new Track(gameObject, "game music");
    }

    void Update()
    {
        menu.Fade(Open, fade);
        game.Fade(!Open, fade);
        if (!Open) GameSettings.Showing = false; // the settings sign goes with the menu that opened it
    }
}
