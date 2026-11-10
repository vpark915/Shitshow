using System;
using System.Collections.Generic;
using UnityEngine;

// Every sound effect in the game, by name (Resources/SoundBank; Tools > Wash Square > Sound Bank selects it). Code asks
// Sfx to play a name; the entry says which clips (one is picked at random each time), how loud, how much the pitch
// wanders, and how far it carries. A name with no clips plays nothing, so a hook can go in before its sound is
// imported: drop the clip into its slot and it's heard. Clips should start on their first sample (no lead-in silence).
[CreateAssetMenu(menuName = "Shitshow/Sound Bank")]
public class SoundBank : ScriptableObject
{
    [Serializable]
    public class Sound
    {
        public string name;
        public AudioClip[] clips = new AudioClip[0];
        [Range(0, 1)] public float volume = 1;
        [Tooltip("Each play's pitch is picked from this range")]
        public Vector2 pitch = new Vector2(0.95f, 1.05f);
        [Tooltip("Metres: full volume inside the first, silent past the second. 0 for both: flat, not placed in the world (UI)")]
        public Vector2 distance = new Vector2(2, 60);
        [Tooltip("Plays at most once in this many seconds (several hits in one frame stay one sound)")]
        public float cooldown = 0.03f;
        [Tooltip("Background: a few at once at most (footsteps, chatter), and never cuts off another sound to play")]
        public bool background;
    }

    public List<Sound> sounds = new();

    Dictionary<string, Sound> byName;

    public Sound Find(string name)
    {
        if (byName == null || byName.Count != sounds.Count)
        {
            byName = new Dictionary<string, Sound>();
            foreach (Sound s in sounds) if (!string.IsNullOrEmpty(s.name)) byName[s.name] = s;
        }
        return byName.TryGetValue(name, out Sound found) ? found : null;
    }
}
