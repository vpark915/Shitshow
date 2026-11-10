using UnityEngine;

// On Resources/EggSplat (Blender/Effects/eggSplat): the fried egg an egg bomb leaves. Shows one of its shapes at random,
// drops each bone of that shape's rig onto whatever ground is under it (so the white drapes over slopes, kerbs and
// steps instead of floating or sinking), pops in with an overshoot, sits, then shrinks away. Only a look: the blast's
// damage all lands on the first frame (FoodPowers.Burst).
public class EggSplat : MonoBehaviour
{
    public float growTime = 0.22f;
    public float shrinkTime = 0.5f;
    [Tooltip("Bones look for ground this far above and below where they rest, metres (before scaling)")]
    public float reach = 0.6f;

    float size, life, born;

    // after Instantiate: size is the scale (the blast's radius), life the seconds before it's gone
    public void Splat(float size, float life, int ignoreLayer)
    {
        this.size = size;
        this.life = life;
        born = Time.time;
        int pick = Random.Range(0, transform.childCount);
        for (int i = 0; i < transform.childCount; i++) transform.GetChild(i).gameObject.SetActive(i == pick);
        transform.localScale = Vector3.one * size;
        Drape(transform.GetChild(pick), ~(1 << ignoreLayer));
        transform.localScale = Vector3.one * 0.001f; // grows from nothing in Update
    }

    // at full size: each bone down (or up) onto the ground beneath it, along the splat's own up
    void Drape(Transform shape, int mask)
    {
        var skin = shape.GetComponentInChildren<SkinnedMeshRenderer>();
        if (!skin) return;
        Vector3 up = transform.up;
        float span = reach * size;
        foreach (Transform bone in skin.bones)
        {
            Vector3 from = bone.position + up * span;
            float best = float.MaxValue;
            Vector3 ground = bone.position;
            foreach (RaycastHit hit in Physics.RaycastAll(from, -up, span * 2, mask, QueryTriggerInteraction.Ignore))
            {
                // ground only: not people, birds, cars or anything else that moves
                if (hit.rigidbody || hit.collider.GetComponentInParent<Health>()) continue;
                // the surface nearest where the bone rests: under a bench or a branch, the ground, not the bench top
                float off = Mathf.Abs(hit.distance - span);
                if (off < best) { best = off; ground = hit.point; }
            }
            if (best < float.MaxValue) bone.position = ground + up * 0.01f;
        }
        skin.updateWhenOffscreen = true; // its bounds moved with the bones
    }

    void Update()
    {
        float t = Time.time - born;
        float s;
        if (t < growTime)
        {
            // out past full size, then back: a splat, not a slide
            float k = t / growTime;
            s = k < 0.7f ? Mathf.Lerp(0, 1.12f, Mathf.Sin(k / 0.7f * Mathf.PI / 2)) : Mathf.Lerp(1.12f, 1, (k - 0.7f) / 0.3f);
        }
        else if (t > life - shrinkTime)
        {
            float k = Mathf.Clamp01((life - t) / shrinkTime);
            s = k * k * (3 - 2 * k); // eases out to nothing
            if (t >= life) { Destroy(gameObject); return; }
        }
        else s = 1;
        transform.localScale = Vector3.one * Mathf.Max(size * s, 0.001f);
    }
}
