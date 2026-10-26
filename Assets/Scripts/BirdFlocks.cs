using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Flocks of ordinary pigeons and seagulls. They peck around a spot on the paving and all take off for another one when
// startled: by a gunshot, or by someone the pigeon is riding running through them. The pigeon itself is one of the
// birds to them, and ordinary walkers step around them. Now and then a flock moves on by itself. One object runs every bird; the spots come from
// the NPC spawner, so the NPCs object has to be in the scene too.
public class BirdFlocks : MonoBehaviour
{
    [Header("Who")]
    public GameObject pigeon;
    public float pigeonScale = 0.37f;
    public GameObject seagull;
    public float seagullScale = 0.6f;

    [Header("How many")]
    public int pigeonFlocks = 5;
    public int seagullFlocks = 3;
    [Tooltip("Fewest and most birds in a flock")]
    public Vector2Int flockSize = new Vector2Int(8, 16);

    [Header("On the ground")]
    [Tooltip("How far from the middle of the flock a bird wanders, in metres")]
    public float flockRadius = 3f;
    public float walkSpeed = 0.5f;

    [Header("Startled")]
    [Tooltip("A shot within this many metres sends the flock up")]
    public float gunRadius = 30f;
    [Tooltip("So does a ridden person moving faster than Scare Speed within this many metres of the middle of the flock")]
    public float scareRadius = 4f;
    public float scareSpeed = 3f;
    [Tooltip("A bike or car faster than Scare Speed sends it up this many seconds before it would reach the flock")]
    public float vehicleWarning = 1.5f;
    [Tooltip("Fewest and most seconds a flock stays put before moving on by itself")]
    public Vector2 restlessness = new Vector2(40f, 120f);

    [Header("Flight")]
    public float flySpeed = 6f;
    [Tooltip("How high the flock climbs on the way to its next spot, in metres")]
    public float flyHeight = 8f;
    [Tooltip("How steeply the birds climb out, in degrees above the ground")]
    public float takeOffAngle = 55f;
    [Tooltip("How steeply they come back down")]
    public float landingAngle = 35f;
    [Tooltip("How far apart the birds burst on take-off before closing up again, in metres")]
    public float scatter = 5f;
    [Tooltip("A startled flock goes at least this far away")]
    public float flyDistance = 30f;

    [Header("Food")]
    [Tooltip("A flock sent after someone by bread catches up once they're this far from it, in metres")]
    public float followGap = 3f;

    class Flock
    {
        public Vector3 centre;
        public float landsAt, movesOnAt;
        public readonly List<Bird> birds = new List<Bird>();
    }

    class Bird
    {
        public Transform body;
        public Animator animator;
        public Collider hitbox; // off while flying, so a flock overhead doesn't block shots, jumps or the camera
        public string state;
        public Vector3 from, to, burst, hitFrom;
        public Health health;
        public float takeOff, landing, nextStroll, height;
        public bool flying;
    }

    readonly List<Flock> flocks = new List<Flock>();
    NpcSpawner spots;
    float nextScareCheck;
    Flock led; // following leader (bread) until leadUntil
    Transform leader;
    float leadUntil;

    IEnumerator Start()
    {
        yield return null; // the NPC spawner finds its walkable spots in its own Start
        spots = FindFirstObjectByType<NpcSpawner>();
        for (int i = 0; i < pigeonFlocks + seagullFlocks; i++)
        {
            bool isPigeon = i < pigeonFlocks;
            var flock = new Flock { centre = spots.RoamPoint(), movesOnAt = Time.time + Random.Range(restlessness.x, restlessness.y) };
            for (int n = Random.Range(flockSize.x, flockSize.y + 1); n > 0; n--)
            {
                GameObject body = Instantiate(isPigeon ? pigeon : seagull, Spot(flock), Quaternion.Euler(0, Random.Range(0f, 360f), 0), transform);
                body.transform.localScale *= isPigeon ? pigeonScale : seagullScale;
                var bird = new Bird { body = body.transform, animator = body.GetComponent<Animator>(), hitbox = body.GetComponentInChildren<Collider>(), to = body.transform.position };
                // shootable (and egg-blastable, and headshots go by its "head" bone): one hit and it's down
                bird.health = body.AddComponent<Health>();
                bird.health.maxHealth = 1;
                bird.health.Hurt += from => bird.hitFrom = from;
                bird.health.Died += () => Limp(bird, (Flat(bird.body.position - bird.hitFrom).normalized * 4 + Vector3.up * 2) * Random.Range(0.8f, 1.2f));
                flock.birds.Add(bird);
            }
            flocks.Add(flock);
        }
    }

    void OnEnable() => Weapon.Fired += OnShot;
    void OnDisable() => Weapon.Fired -= OnShot;

    void OnShot(Weapon shooter)
    {
        foreach (Flock flock in flocks)
            if (Vector3.Distance(shooter.transform.position, flock.centre) < gunRadius) Startle(flock);
    }

    // Vehicle: a bike or car hit a bird too slow to get out of the way. Returns whether hitbox was one of the birds
    public bool RunOver(Collider hitbox, Vector3 push)
    {
        foreach (Flock flock in flocks)
            foreach (Bird bird in flock.birds)
                if (bird.hitbox == hitbox)
                {
                    Limp(bird, push);
                    return true;
                }
        return false;
    }

    // (bone, sphere radius as a share of the bird's capsule radius); each joins the body
    static readonly (string bone, float radius)[] Parts = { ("body", 0.9f), ("head", 0.5f), ("wing.L", 0.4f), ("wing.R", 0.4f), ("tail.01", 0.35f) };

    // Dead (shot, blown up, run over): out of its flock and a ragdoll of spheres on its bones, all moving at velocity.
    // Built only now, so the living flocks don't pay for one each.
    // ponytail: bodies stay where they fall and flocks aren't topped up; fade and respawn if rounds run long
    void Limp(Bird bird, Vector3 velocity)
    {
        foreach (Flock flock in flocks) flock.birds.Remove(bird);
        bird.animator.enabled = false;
        if (bird.health) Destroy(bird.health); // the body takes no more hits as a living bird (a kill counts once)
        var capsule = bird.hitbox as CapsuleCollider;
        float size = capsule ? capsule.radius * capsule.transform.lossyScale.x : 0.07f; // world metres
        Vector3 middle = capsule ? capsule.transform.TransformPoint(capsule.center) : bird.body.position;
        if (bird.hitbox) bird.hitbox.enabled = false;

        Rigidbody chest = null;
        var made = new List<Collider>();
        foreach ((string name, float share) in Parts)
        {
            Transform bone = null;
            foreach (Transform t in bird.body.GetComponentsInChildren<Transform>()) if (t.name == name) { bone = t; break; }
            if (!bone) continue;
            float radius = size * share;
            Vector3 at = name == "body" ? middle : name == "head" ? bone.position + Vector3.up * radius * 0.6f : bone.position; // the head bone is at the neck
            var ball = bone.gameObject.AddComponent<SphereCollider>();
            ball.center = bone.InverseTransformPoint(at);
            ball.radius = radius / bone.lossyScale.x;
            foreach (Collider other in made) Physics.IgnoreCollision(ball, other); // they overlap
            made.Add(ball);

            var part = bone.gameObject.AddComponent<Rigidbody>();
            part.mass = name == "body" ? 0.3f : 0.03f;
            part.interpolation = RigidbodyInterpolation.Interpolate;
            part.linearVelocity = velocity;
            part.angularVelocity = Random.insideUnitSphere * 10f;
            if (chest) bone.gameObject.AddComponent<CharacterJoint>().connectedBody = chest;
            else chest = part; // "body" comes first
        }
    }

    // somewhere on the ground within the flock's patch
    Vector3 Spot(Flock flock)
    {
        Vector2 offset = Random.insideUnitCircle * flockRadius;
        Vector3 p = flock.centre + new Vector3(offset.x, 0, offset.y);
        return NavMesh.SamplePosition(p, out NavMeshHit hit, 1f, NavMesh.AllAreas) ? hit.position : flock.centre;
    }

    void Startle(Flock flock)
    {
        if (Time.time < flock.landsAt || flock == led) return; // already in the air; a flock sent by bread doesn't scare
        Vector3 away = flock.centre;
        for (int tries = 0; tries < 10 && Vector3.Distance(away, flock.centre) < flyDistance; tries++) away = spots.RoamPoint();
        FlyTo(flock, away);
    }

    // bread (FoodPowers): the flock nearest someone tails them for a while
    public void Follow(Transform who, float seconds)
    {
        leader = who;
        leadUntil = Time.time + seconds;
        led = Nearest(who.position);
    }

    public void StopFollowing() => led = null;

    Flock Nearest(Vector3 p)
    {
        Flock best = null;
        foreach (Flock flock in flocks)
            if (best == null || Vector3.Distance(p, flock.centre) < Vector3.Distance(p, best.centre)) best = flock;
        return best;
    }

    // the whole flock takes off and lands around a new centre
    void FlyTo(Flock flock, Vector3 centre)
    {
        Vector3 old = flock.centre;
        flock.centre = centre;
        foreach (Bird bird in flock.birds)
        {
            bird.from = bird.body.position;
            bird.to = Spot(flock);
            // each bird breaks outwards from the middle of the flock, at its own height, then they close up to land
            Vector3 outwards = Vector3.ProjectOnPlane(bird.from - old, Vector3.up).normalized;
            Vector2 any = Random.insideUnitCircle;
            bird.burst = (outwards + new Vector3(any.x, 0, any.y)) * (scatter / 2);
            bird.height = flyHeight * Random.Range(0.7f, 1.3f);
            bird.takeOff = Time.time + Random.Range(0f, 0.8f); // not all on the same frame
            bird.landing = bird.takeOff + Vector3.Distance(bird.from, bird.to) / (flySpeed * Random.Range(0.9f, 1.1f));
            bird.flying = true;
            if (bird.hitbox) bird.hitbox.enabled = false;
            flock.landsAt = Mathf.Max(flock.landsAt, bird.landing);
        }
    }

    void Update()
    {
        if (led != null && (!leader || Time.time >= leadUntil)) led = null;
        if (led != null && Time.time >= led.landsAt && Vector3.Distance(Flat(leader.position), Flat(led.centre)) > followGap)
            FlyTo(led, leader.position);

        if (Time.time >= nextScareCheck)
        {
            nextScareCheck = Time.time + 0.5f;
            foreach (Flock flock in flocks)
            {
                if (Time.time >= flock.movesOnAt)
                {
                    flock.movesOnAt = Time.time + Random.Range(restlessness.x, restlessness.y);
                    Startle(flock);
                    continue;
                }
                // a bike or car is checked further out the faster it goes, so the flock is up before it arrives
                foreach (Collider near in Physics.OverlapSphere(flock.centre, scareRadius + vehicleWarning * 20f))
                    if ((near is CharacterController body && near.TryGetComponent(out Npc person) && person.Ridden && body.velocity.magnitude > scareSpeed
                            && Vector3.Distance(near.transform.position, flock.centre) < scareRadius)
                        || (near.TryGetComponent(out Vehicle ride) && Mathf.Abs(ride.Speed) > scareSpeed
                            && Vector3.Distance(near.transform.position, flock.centre) < scareRadius + Mathf.Abs(ride.Speed) * vehicleWarning))
                    {
                        Startle(flock);
                        break;
                    }
            }
        }

        foreach (Flock flock in flocks)
            foreach (Bird bird in flock.birds)
            {
                if (bird.flying && Time.time >= bird.takeOff) Fly(bird);
                else if (!bird.flying) Potter(flock, bird);
            }
    }

    void Fly(Bird bird)
    {
        float t = Mathf.InverseLerp(bird.takeOff, bird.landing, Time.time);
        t = t * t * (3 - 2 * t); // ease away from the ground and back onto it instead of leaving at full speed
        // straight up the take-off angle to cruising height, level, then down the landing angle
        float length = Vector3.Distance(bird.from, bird.to);
        float height = Mathf.Min(bird.height, t * length * Mathf.Tan(takeOffAngle * Mathf.Deg2Rad), (1 - t) * length * Mathf.Tan(landingAngle * Mathf.Deg2Rad));
        Vector3 next = Vector3.Lerp(bird.from, bird.to, t) + Vector3.up * height + bird.burst * Mathf.Sin(t * Mathf.PI); // widest half way there
        Face(bird, next - bird.body.position, 720);
        bird.body.position = next;
        Play(bird, "Fly");
        if (t < 1) return;
        bird.flying = false;
        if (bird.hitbox) bird.hitbox.enabled = true;
    }

    // peck, and now and then stroll to another spot in the patch
    void Potter(Flock flock, Bird bird)
    {
        if (Time.time >= bird.nextStroll)
        {
            bird.to = Spot(flock);
            bird.nextStroll = Time.time + Random.Range(3f, 12f);
        }
        Vector3 step = bird.to - bird.body.position;
        if (step.magnitude < 0.05f) { Play(bird, bird.nextStroll - Time.time > 2f ? "Eat" : "Idle_A"); return; }
        Face(bird, Vector3.ProjectOnPlane(step, Vector3.up), 360);
        bird.body.position = Vector3.MoveTowards(bird.body.position, bird.to, walkSpeed * Time.deltaTime);
        Play(bird, "Walk");
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);

    static void Face(Bird bird, Vector3 direction, float degreesPerSecond)
    {
        if (direction.sqrMagnitude > 1e-6f)
            bird.body.rotation = Quaternion.RotateTowards(bird.body.rotation, Quaternion.LookRotation(direction), degreesPerSecond * Time.deltaTime);
    }

    // both birds' Animator controllers have these state names on their base layer
    static void Play(Bird bird, string state)
    {
        if (bird.state == state) return;
        bird.state = state;
        bird.animator.CrossFadeInFixedTime(state, 0.2f, 0);
    }
}
