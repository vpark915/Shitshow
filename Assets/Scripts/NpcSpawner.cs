using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Fills the park with people when the scene starts. Lives on the NPCs object, which must stand on a path: only places
// that can be walked to from here are used. The NavMesh comes from Tools > Wash Square > Bake NPC NavMesh.
public class NpcSpawner : MonoBehaviour
{
    public const float Radius = 0.3f, Height = 1.8f;
    const int PathsOnly = 1; // the built-in Walkable area: paving and pavements, not lawns or the road

    [Header("Who")]
    public GameObject[] civilians = { };
    public GameObject[] police = { };
    public RuntimeAnimatorController controller;

    [Header("How many")]
    public int walkers = 100;
    public int joggers = 10;
    public int idlers = 50;
    [Tooltip("Police who walk a beat")]
    public int officers = 8;
    [Tooltip("Police who stand at a post")]
    public int guards = 8;

    [Header("Where")]
    [Tooltip("Walkers and officers stroll between random spots on the paths and pavements inside this box")]
    public Bounds roamArea = new Bounds(Vector3.zero, new Vector3(340, 10, 190));
    [Tooltip("Idlers stand around these, facing them")]
    public Transform[] hangouts = { };
    [Tooltip("Joggers run laps through these points, in either direction")]
    public Transform[] jogRoute = { };

    [Header("Speeds (m/s)")]
    public float walkSpeed = 1.4f;
    public float jogSpeed = 3.5f;

    [Header("Gunfire")]
    [Tooltip("People within this many metres of a shot run from it")]
    public float panicRadius = 25f;
    public float fleeSpeed = 5f;
    [Tooltip("Seconds before they calm down and go back to what they were doing")]
    public float panicTime = 8f;

    [Header("Damage")]
    [Tooltip("A pigeon shot does 10 by default")]
    public float health = 30f;

    [Header("Police")]
    [Tooltip("Shown in an officer's hand once they draw")]
    public GameObject policeGun;
    public float policeGunScale = 0.22f;
    [Tooltip("Officers within this many metres of a shot go after whoever fired it")]
    public float policeHearing = 60f;
    [Tooltip("They start shooting once they have a clear line inside this distance")]
    public float policeRange = 25f;
    [Tooltip("They keep closing in while shooting until they're this close, then stand their ground")]
    public float policeShootingDistance = 10f;
    [Tooltip("Seconds after the last shot they heard before they holster and go back to work")]
    public float policeAttention = 12f;
    public float policeFireInterval = 0.7f;
    [Tooltip("How far a shot can stray, in degrees. A pigeon is a small target, so this is most of the difficulty")]
    public float policeSpread = 1.2f;
    public float policeDamage = 10f;
    [Tooltip("How fast their aim follows a moving target, in m/s. Move faster than this and the shots land behind you")]
    public float policeAimSpeed = 3f;
    [Tooltip("Left floating where an officer dies; the player collects it for an extra reload")]
    public GameObject magazineDrop;
    [Tooltip("Left floating where a civilian dies")]
    public GameObject walletDrop;

    [Header("Chatting")]
    [Tooltip("Every this many seconds a walker somewhere calls over the walkers near them for a chat")]
    public float chatEvery = 4f;
    [Tooltip("They come from up to this many metres away")]
    public float chatRadius = 8f;
    [Tooltip("Fewest and most people in a group")]
    public Vector2Int chatSize = new Vector2Int(2, 4);
    [Tooltip("Shortest and longest chat, in seconds, counted from when they set off to meet")]
    public Vector2 chatTime = new Vector2(20, 50);

    [Header("Chess tables")]
    [Tooltip("Parent of the tables people sit at to talk; each has a bench along both long sides")]
    public Transform tables;
    [Tooltip("Chance a chat is two people sitting across a free table instead of a group standing")]
    [Range(0, 1)] public float sitChance = 0.5f;
    [Tooltip("Walkers come to a table from up to this many metres away")]
    public float tableReach = 30f;
    [Tooltip("Metres from the middle of the table out to where someone sits on the bench")]
    public float seatOut = 0.62f;

    float nextChat;
    float[] tableBusyUntil;
    readonly List<Vector3> roamPoints = new List<Vector3>();
    Vector3 anchor;

    void Start()
    {
        if (!NavMesh.SamplePosition(transform.position, out NavMeshHit here, 5f, PathsOnly))
        {
            Debug.LogError("NpcSpawner: no NavMesh path under " + name + ". Bake it, or move this object onto a path.", this);
            return;
        }
        anchor = here.position;
        NavMesh.pathfindingIterationsPerFrame = 500; // the default 100 leaves a whole crowd standing for seconds waiting for routes
        for (int i = 0; i < 600; i++)
        {
            Vector3 p = roamArea.center + Vector3.Scale(roamArea.extents, new Vector3(Random.Range(-1f, 1f), 0, Random.Range(-1f, 1f)));
            if (OnPath(ref p)) roamPoints.Add(p);
        }

        for (int i = 0; i < walkers; i++) Spawn(Npc.Kind.Walker, civilians, RoamPoint(), walkSpeed);
        for (int i = 0; i < officers; i++) Spawn(Npc.Kind.Walker, police, RoamPoint(), walkSpeed * 0.8f);
        for (int i = 0; i < guards; i++) Spawn(Npc.Kind.Guard, police, RoamPoint(), walkSpeed); // ponytail: posts are random spots on the paths; add a posts list if they should guard particular places
        for (int i = 0; i < idlers; i++) Spawn(Npc.Kind.Idler, civilians, HangoutSpot(out Vector3 lookAt), walkSpeed).lookAt = lookAt;
        for (int i = 0; i < joggers && jogRoute.Length > 1; i++)
        {
            int leg = Random.Range(0, jogRoute.Length);
            Vector3 p = Vector3.Lerp(jogRoute[leg].position, jogRoute[(leg + 1) % jogRoute.Length].position, Random.value);
            if (OnPath(ref p)) Spawn(Npc.Kind.Jogger, civilians, p, jogSpeed).routeIndex = leg;
        }
    }

    void Update()
    {
        if (Time.time < nextChat || transform.childCount == 0) return;
        nextChat = Time.time + chatEvery;
        if (tables && tables.childCount > 0 && Random.value < sitChance && SitAtTable()) return;
        // ponytail: looks around one person picked at random and gives up if fewer than two walkers there are free
        var group = new List<Npc>();
        int size = Random.Range(chatSize.x, chatSize.y + 1);
        foreach (Collider near in Physics.OverlapSphere(transform.GetChild(Random.Range(0, transform.childCount)).position, chatRadius))
            if (group.Count < size && near.TryGetComponent(out Npc npc) && npc.CanChat) group.Add(npc);
        if (group.Count < 2) return;

        // in a ring around where the first of them is standing, facing in
        Vector3 centre = group[0].transform.position;
        float seconds = Random.Range(chatTime.x, chatTime.y);
        for (int i = 0; i < group.Count; i++)
        {
            Vector3 spot = centre + Quaternion.Euler(0, 360f * i / group.Count, 0) * Vector3.forward * 0.7f;
            group[i].Chat(NavMesh.SamplePosition(spot, out NavMeshHit hit, 1f, NavMesh.AllAreas) ? hit.position : centre, centre, seconds);
        }
    }

    // two free walkers near a free table sit down across it from each other
    bool SitAtTable()
    {
        if (tableBusyUntil == null || tableBusyUntil.Length != tables.childCount) tableBusyUntil = new float[tables.childCount];
        int i = Random.Range(0, tables.childCount);
        if (Time.time < tableBusyUntil[i]) return false;
        Transform table = tables.GetChild(i);
        var pair = new List<Npc>();
        foreach (Collider near in Physics.OverlapSphere(table.position, tableReach))
            if (pair.Count < 2 && near.TryGetComponent(out Npc npc) && npc.CanChat) pair.Add(npc);
        if (pair.Count < 2) return false;

        float seconds = Random.Range(chatTime.x, chatTime.y);
        // ponytail: the table stays booked for the whole chat even if they're scared off early
        tableBusyUntil[i] = Time.time + seconds;
        for (int side = 0; side < 2; side++)
            pair[side].Chat(table.position + table.forward * (side == 0 ? seatOut : -seatOut), table.position, seconds, true);
        return true;
    }

    Npc Spawn(Npc.Kind kind, GameObject[] models, Vector3 position, float speed)
    {
        GameObject person = Instantiate(models[Random.Range(0, models.Length)], position, Quaternion.Euler(0, Random.Range(0f, 360f), 0), transform);
        var animator = person.GetComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.fireEvents = false; // the Starter Assets run clip calls OnFootstep, which only the player handles
        animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms; // nobody off-screen needs posing; a crowd of 500 makes this a few ms

        // solid to the player and to bullets
        var body = person.AddComponent<CapsuleCollider>();
        body.center = Vector3.up * Height / 2;
        body.height = Height;
        body.radius = Radius;
        person.AddComponent<Rigidbody>().isKinematic = true;

        var agent = person.AddComponent<NavMeshAgent>();
        agent.radius = Radius;
        agent.height = Height;
        agent.speed = speed * Random.Range(0.85f, 1.15f);
        agent.angularSpeed = 240;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
        agent.avoidancePriority = Random.Range(20, 80);

        person.AddComponent<Health>().maxHealth = health;

        var npc = person.AddComponent<Npc>();
        npc.kind = kind;
        npc.officer = models == police;
        npc.spawner = this;
        return npc;
    }

    public Vector3 RoamPoint() => roamPoints[Random.Range(0, roamPoints.Count)];

    // somewhere to stand a few steps away from one of the hangouts
    public Vector3 HangoutSpot(out Vector3 lookAt)
    {
        for (int tries = 0; tries < 10 && hangouts.Length > 0; tries++)
        {
            Transform hangout = hangouts[Random.Range(0, hangouts.Length)];
            Bounds size = hangout.TryGetComponent(out Collider shape) ? shape.bounds : new Bounds(hangout.position, Vector3.zero);
            Vector2 away = Random.insideUnitCircle.normalized * (Mathf.Max(size.extents.x, size.extents.z) + Random.Range(1f, 4f));
            lookAt = size.center;
            Vector3 p = lookAt + new Vector3(away.x, 0, away.y);
            if (OnPath(ref p)) return p;
        }
        return lookAt = RoamPoint();
    }

    // snaps p onto a path or pavement that can be reached on foot from the spawner
    public bool OnPath(ref Vector3 p)
    {
        p.y = anchor.y;
        var path = new NavMeshPath();
        if (!NavMesh.SamplePosition(p, out NavMeshHit hit, 2f, PathsOnly)) return false;
        if (!NavMesh.CalculatePath(anchor, hit.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) return false;
        p = hit.position;
        return true;
    }
}
