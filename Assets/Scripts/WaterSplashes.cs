using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Splashes and mist on a flat water surface (this object's renderer: its top is the surface, its width the pool): a
// flipbook ring laid on the surface plus a few mist puffs where the fountain's jets land, round the foot of the centre
// spout, and wherever something moves through the water — a pigeon wading, someone's feet, a bird or an egg dropping
// in (a bigger one); mist also hangs along the jets. Visual only: every client runs its own off what it sees.
// ponytail: no droplets (too much overdraw round the fountain); round pools only (a box test, then the distance from
// the centre), add a polygon test for other shapes
[RequireComponent(typeof(Renderer))]
public class WaterSplashes : MonoBehaviour
{
    [Tooltip("Jets landing evenly spaced round a ring about the centre (0 for none)")]
    public int jets;
    public float jetRadius;
    [Tooltip("Seconds between splashes at each jet")]
    public float jetEvery = 0.35f;
    [Tooltip("Seconds between splashes for something wading at walking pace (faster, more often)")]
    public float wadeEvery = 0.3f;
    [Tooltip("Where the jets leave the rim (metres from the centre) and how high their arc rises over the surface: mist is shed along it")]
    public float jetNozzleRadius = 10.5f, jetArcHeight = 1.4f;
    [Tooltip("Mist puffs shed per second along each jet")]
    public float jetMistRate = 2;
    [Tooltip("Splashes and mist round the centre spout, where its sheet falls into the water (0 per second for none)")]
    public float spoutMistRadius = 1.5f, spoutMistRate = 6;
    public Material splashMaterial; // the 4x4 flipbook; empty, Resources/WaterSplash.mat
    public Material mistMaterial; // soft puff, additive; empty, Resources/WaterMist.mat

    ParticleSystem splashes, mist;
    Vector3 centre;
    float surface, reach;
    float[] jetNext;
    float jetMistDue, spoutMistDue;
    readonly Collider[] near = new Collider[64];
    Dictionary<Collider, (Vector3 at, float next, bool wet)> movers = new Dictionary<Collider, (Vector3, float, bool)>(), seen = new Dictionary<Collider, (Vector3, float, bool)>();

    void Awake()
    {
        Bounds pool = GetComponent<Renderer>().bounds;
        centre = pool.center;
        surface = pool.max.y;
        reach = Mathf.Max(pool.extents.x, pool.extents.z);
        // loaded here, not set by the park's import: an import that depends on other new assets looped (Unity gave up
        // half way and the park vanished)
        splashes = Rings(splashMaterial ? splashMaterial : Resources.Load<Material>("WaterSplash"));
        mist = Mist(mistMaterial ? mistMaterial : Resources.Load<Material>("WaterMist"));

        jetNext = new float[jets];
        for (int i = 0; i < jets; i++) jetNext[i] = Random.value * jetEvery; // out of step
    }

    void Update()
    {
        float now = Time.time;
        for (int i = 0; i < jets; i++)
            if (now >= jetNext[i])
            {
                jetNext[i] = now + jetEvery * Random.Range(0.8f, 1.2f);
                float angle = 2 * Mathf.PI * i / jets;
                Splash(centre + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * jetRadius, Random.Range(0.5f, 0.8f));
            }

        // mist shed along the jets' arcs, carried on the way the water's going
        jetMistDue += jets * jetMistRate * Time.deltaTime;
        for (; jetMistDue >= 1; jetMistDue--)
        {
            float angle = 2 * Mathf.PI * Random.Range(0, jets) / jets, t = Random.value;
            var outward = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
            float r = Mathf.Lerp(jetNozzleRadius, jetRadius, t);
            Vector3 at = centre + outward * r + Vector3.up * (surface - centre.y + 4 * jetArcHeight * t * (1 - t));
            Vector3 along = -outward * (jetNozzleRadius - jetRadius) + Vector3.up * 4 * jetArcHeight * (1 - 2 * t); // the arc's direction, per unit t
            mist.Emit(new ParticleSystem.EmitParams { position = at, velocity = along * 0.35f + Random.insideUnitSphere * 0.3f, startSize = Random.Range(0.8f, 1.6f) }, 1);
        }

        // splashes where the centre spout's sheet falls in, mist rolling outward off them
        spoutMistDue += spoutMistRate * Time.deltaTime;
        for (; spoutMistDue >= 1; spoutMistDue--)
        {
            float angle = Random.value * 2 * Mathf.PI;
            var outward = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
            Vector3 at = new Vector3(centre.x, surface, centre.z) + outward * spoutMistRadius * Random.Range(0.6f, 1f);
            Ring(at, Random.Range(0.5f, 0.9f));
            mist.Emit(new ParticleSystem.EmitParams
            {
                position = at + Vector3.up * 0.2f,
                velocity = outward * Random.Range(0.2f, 0.5f) + Vector3.up * Random.Range(0.2f, 0.6f),
                startSize = Random.Range(1.2f, 2f),
            }, 1);
        }

        // anything that moves (a Rigidbody, ragdoll limbs included, or a CharacterController) touching the surface
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        seen.Clear();
        int count = Physics.OverlapBoxNonAlloc(new Vector3(centre.x, surface, centre.z), new Vector3(reach, 0.5f, reach), near, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider thing = near[i];
            if (!thing.attachedRigidbody && !(thing is CharacterController)) continue; // the basin, the ground
            Bounds b = thing.bounds;
            Vector3 fromCentre = b.center - centre;
            fromCentre.y = 0;
            if (fromCentre.magnitude > reach) continue; // in the box round the pool, not the pool
            bool wet = b.min.y < surface + 0.05f && b.max.y > surface - 0.6f; // down at the surface, not deep under it
            float width = Mathf.Max(b.size.x, b.size.z);
            var state = (at: b.center, next: now, wet);
            if (movers.TryGetValue(thing, out var was))
            {
                state.next = was.next;
                Vector3 moved = b.center - was.at;
                float falling = -moved.y / dt;
                moved.y = 0;
                float speed = moved.magnitude / dt;
                if (wet && !was.wet && falling > 1f) Splash(b.center, width * 3 + 0.8f); // dropped in
                else if (wet && speed > 0.3f && now >= was.next)
                {
                    Splash(b.center, width * 2 + 0.3f);
                    state.next = now + wadeEvery * Mathf.Clamp(1.4f / speed, 0.4f, 2f);
                }
            }
            seen[thing] = state;
        }
        (movers, seen) = (seen, movers); // forget whatever left the water
    }

    void Ring(Vector3 at, float size) => splashes.Emit(new ParticleSystem.EmitParams
    {
        position = new Vector3(at.x, surface + 0.01f, at.z), // just over the surface, no flicker
        startSize = size,
        rotation = Random.Range(0f, 360f),
        applyShapeToPosition = false,
    }, 1);

    void Splash(Vector3 at, float size)
    {
        Ring(at, size);
        for (int i = Mathf.CeilToInt(size); i > 0; i--)
            mist.Emit(new ParticleSystem.EmitParams
            {
                position = new Vector3(at.x, surface + 0.2f, at.z) + Random.insideUnitSphere * size * 0.3f,
                velocity = Vector3.up * Random.Range(0.3f, 0.9f) + Random.insideUnitSphere * 0.4f,
                startSize = size * Random.Range(1.5f, 2.5f),
            }, 1);
    }

    // soft puffs that rise, swell, drift on a slow turbulence and fade: the mist and foam hanging round the jets and
    // where they land. ponytail: additive (the puff texture has no alpha), so it brightens what's behind; on dark water at
    // night it'd glow, a texture with alpha and alpha blending would fix that
    ParticleSystem Mist(Material material)
    {
        var holder = new GameObject("Splash Mist");
        holder.transform.SetParent(transform, false);
        var puffs = holder.AddComponent<ParticleSystem>();
        puffs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = puffs.main;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 3.2f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0, 2 * Mathf.PI);
        main.gravityModifier = -0.03f; // lifts a little
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 400;
        ParticleSystem.EmissionModule emission = puffs.emission;
        emission.enabled = false;
        ParticleSystem.ShapeModule shape = puffs.shape;
        shape.enabled = false;
        ParticleSystem.LimitVelocityOverLifetimeModule slow = puffs.limitVelocityOverLifetime;
        slow.enabled = true;
        slow.limit = 0.4f;
        slow.dampen = 0.08f; // thrown off fast, then hangs
        ParticleSystem.NoiseModule noise = puffs.noise;
        noise.enabled = true;
        noise.strength = 0.5f;
        noise.frequency = 0.4f;
        noise.scrollSpeed = 0.25f;
        noise.quality = ParticleSystemNoiseQuality.Low; // 1D noise: slow puffs, the difference doesn't show
        ParticleSystem.RotationOverLifetimeModule spin = puffs.rotationOverLifetime;
        spin.enabled = true;
        spin.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
        ParticleSystem.SizeOverLifetimeModule swell = puffs.sizeOverLifetime;
        swell.enabled = true;
        swell.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.EaseInOut(0, 0.6f, 1, 2f));
        ParticleSystem.ColorOverLifetimeModule fade = puffs.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
            new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(0.45f, 0.15f), new GradientAlphaKey(0.25f, 0.6f), new GradientAlphaKey(0, 1) });
        fade.color = gradient;
        var look = holder.GetComponent<ParticleSystemRenderer>();
        look.sharedMaterial = material;
        look.shadowCastingMode = ShadowCastingMode.Off;
        look.receiveShadows = false;
        // the fps dip at the fountain: at 2 (twice the screen) every puff near the camera filled the screen, hundreds of
        // layers of overdraw. A third of the screen at most
        look.maxParticleSize = 0.35f;
        puffs.Play();
        return puffs;
    }

    // the flipbook rings, laid flat on the surface, its 16 frames played once over each one's life
    ParticleSystem Rings(Material material)
    {
        var holder = new GameObject("Splashes");
        holder.transform.SetParent(transform, false);
        var rings = holder.AddComponent<ParticleSystem>();
        rings.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = rings.main;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = 0.8f;
        main.startSpeed = 0;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 128;
        ParticleSystem.EmissionModule emission = rings.emission;
        emission.enabled = false;
        ParticleSystem.ShapeModule shape = rings.shape;
        shape.enabled = false;
        ParticleSystem.TextureSheetAnimationModule sheet = rings.textureSheetAnimation;
        sheet.enabled = true;
        sheet.numTilesX = 4;
        sheet.numTilesY = 4;
        var look = holder.GetComponent<ParticleSystemRenderer>();
        look.sharedMaterial = material;
        look.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        look.shadowCastingMode = ShadowCastingMode.Off;
        look.receiveShadows = false;
        rings.Play();
        return rings;
    }
}
