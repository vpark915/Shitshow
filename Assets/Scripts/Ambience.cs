using UnityEngine;

// The park's background sound: the SoundBank's "park ambience" looping under everything, flat (not placed in the
// world), from the first scene on. Its volume is the bank entry's times the Ambience setting; it fades in over a couple
// of seconds at start.
public class Ambience : MonoBehaviour
{
    public string sound = "park ambience";
    public float fadeIn = 2.5f;

    AudioSource source;
    float target;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Begin()
    {
        if (FindFirstObjectByType<Ambience>()) return;
        var go = new GameObject("Ambience");
        DontDestroyOnLoad(go);
        go.AddComponent<Ambience>();
    }

    void Start()
    {
        var bank = Resources.Load<SoundBank>("SoundBank");
        SoundBank.Sound entry = bank ? bank.Find(sound) : null;
        if (entry == null || entry.clips.Length == 0 || !entry.clips[0]) { enabled = false; return; }
        source = gameObject.AddComponent<AudioSource>();
        source.clip = entry.clips[Random.Range(0, entry.clips.Length)];
        source.loop = true;
        source.spatialBlend = 0;
        source.volume = 0;
        source.time = Random.Range(0, source.clip.length); // somewhere in the recording, not always its start
        target = entry.volume;
        source.Play();
    }

    void Update()
    {
        // towards the bank's volume times the Ambience setting (which can move either way while the settings are open)
        if (source) source.volume = Mathf.MoveTowards(source.volume, target * GameSettings.AmbienceVolume, target / fadeIn * Time.unscaledDeltaTime);
    }
}
