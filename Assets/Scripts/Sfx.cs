using System.Collections.Generic;
using UnityEngine;

// Plays the SoundBank's sounds: Sfx.Play("shot", gunPosition), or Sfx.Play("menu select") for a flat UI sound. Sources
// come from a small pool kept across scenes, so a sound starts the frame it's asked for, with no object made on the spot.
public static class Sfx
{
    const int PoolSize = 24, MaxBackground = 8;

    static SoundBank bank;
    static readonly List<AudioSource> pool = new();
    static readonly Dictionary<string, float> lastPlayed = new();
    static readonly HashSet<AudioSource> background = new();
    static int next;
    static Transform holder;

    static SoundBank Bank => bank ? bank : bank = Resources.Load<SoundBank>("SoundBank");

    // each returns the source it's playing on (null if nothing played), so a caller can cut it short
    public static AudioSource Play(string name) => Play(name, null, 1);
    public static AudioSource Play(string name, Vector3 at, float volume = 1) => Play(name, (Vector3?)at, volume);

    static AudioSource Play(string name, Vector3? at, float volume)
    {
        SoundBank.Sound sound = Bank ? Bank.Find(name) : null;
        if (sound == null || sound.clips.Length == 0) return null;
        AudioClip clip = sound.clips[Random.Range(0, sound.clips.Length)];
        if (!clip) return null;
        if (lastPlayed.TryGetValue(name, out float last) && Time.unscaledTime - last < sound.cooldown) return null;
        AudioSource source = Source(sound.background);
        if (!source) return null;
        lastPlayed[name] = Time.unscaledTime;
        if (sound.background) background.Add(source); else background.Remove(source);
        bool placed = at.HasValue && sound.distance.y > 0;
        source.transform.position = placed ? at.Value : Vector3.zero;
        source.spatialBlend = placed ? 1 : 0;
        source.minDistance = Mathf.Max(0.01f, sound.distance.x);
        source.maxDistance = Mathf.Max(source.minDistance + 0.01f, sound.distance.y);
        source.pitch = Random.Range(sound.pitch.x, sound.pitch.y);
        source.volume = sound.volume * volume * GameSettings.Effects;
        source.clip = clip;
        source.Play();
        return source;
    }

    // the next source round the pool, preferring one that's finished; for a background sound, null if it would have
    // to cut something off or there are already enough background sounds playing
    static AudioSource Source(bool forBackground)
    {
        pool.RemoveAll(s => !s);
        if (forBackground)
        {
            int playing = 0;
            foreach (AudioSource s in background) if (s && s.isPlaying) playing++;
            if (playing >= MaxBackground) return null;
        }
        if (pool.Count < PoolSize)
        {
            if (!holder)
            {
                holder = new GameObject("Sfx").transform;
                Object.DontDestroyOnLoad(holder.gameObject);
            }
            var go = new GameObject("Sfx Source");
            go.transform.SetParent(holder);
            var made = go.AddComponent<AudioSource>();
            made.playOnAwake = false;
            made.rolloffMode = AudioRolloffMode.Linear; // silent at the far distance, unlike the default curve
            made.dopplerLevel = 0;
            pool.Add(made);
            return made;
        }
        for (int i = 0; i < pool.Count; i++)
        {
            AudioSource s = pool[(next + i) % pool.Count];
            if (!s.isPlaying) { next = (next + i + 1) % pool.Count; return s; }
        }
        if (forBackground) return null;
        AudioSource oldest = pool[next]; // all busy: cut the one started longest ago
        next = (next + 1) % pool.Count;
        return oldest;
    }
}
