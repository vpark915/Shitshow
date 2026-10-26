using UnityEngine;
using UnityEngine.AI;

// Something the pigeon can land on and drive (HeadRide does the landing, the camera and hopping off), like a Citi Bike.
// Forward drives, back brakes and then backs it up, left/right steer (turning like a vehicle rather than on the spot),
// sprint goes faster and jump hops (if it can). Left alone it rolls to a stop. The rider can't shoot (Weapon checks).
public abstract class Vehicle : MonoBehaviour
{
    [Header("Driving (m/s)")]
    public float speed = 6f;
    public float sprintSpeed = 9f;
    [Tooltip("Speed gained per second under power, m/s")]
    public float acceleration = 3f;
    [Tooltip("Backing up, m/s")]
    public float backSpeed = 1.2f;
    [Tooltip("Speed lost per second while braking, m/s")]
    public float braking = 8f;
    [Tooltip("Speed lost per second with no power, m/s")]
    public float coasting = 1f;
    [Tooltip("Fastest turn, degrees per second")]
    public float turnRate = 100f;
    [Tooltip("At this speed and above it turns at the full rate; slower, it turns slower")]
    public float fullTurnSpeed = 6f;
    [Tooltip("Share of the full turn rate it still has standing still (so a stopped bike can still be pointed)")]
    [Range(0, 1)] public float standingTurn = 0.25f;
    [Tooltip("Furthest the front wheels turn, degrees")]
    public float maxSteer = 35f;

    [Header("Riding")]
    [Tooltip("Where the rider sits")]
    public Transform perch;
    [Tooltip("How far from the perch, in metres, still counts as landing on it")]
    public float landRadius = 0.35f;
    [Tooltip("How far back the camera sits while driving it")]
    public float cameraDistance = 4f;

    [Header("Running people over")]
    [Tooltip("Slower than this, m/s, a person just stops it like a wall")]
    public float knockSpeed = 2f;
    public float knockDamage = 5f;
    [Tooltip("Police this close, metres, come after the rider")]
    public float copRadius = 40f;

    public float Speed { get; protected set; }
    [HideInInspector] public Weapon rider; // HeadRide, while someone's driving

    [Tooltip("kg, for what happens when it hits something loose or gets hit")]
    public float mass = 25f;

    // knocked over by another vehicle: tumbling as a rigidbody, not drivable, till it comes to rest
    public bool Tumbling { get; private set; }

    // Something in the way. A bird or person is knocked flying, and anything loose (a ragdoll, a dead bird, a parked
    // bike or car) is shoved like a real collision, by mass: v = 2·m1/(m1+m2)·speed, and this slows by the same share.
    // Returns whether it drives on; anything else stops it like a wall.
    protected bool RunOver(Collider hit)
    {
        Vector3 push = transform.forward * Speed + Vector3.up * Mathf.Abs(Speed) * 0.4f;
        BirdFlocks flocks = hit.GetComponentInParent<BirdFlocks>();
        if (flocks && flocks.RunOver(hit, push)) { Weapon.Done(rider, "run over bird"); return true; }
        Rigidbody loose = hit.attachedRigidbody;
        if (loose && !loose.isKinematic) { Shove(loose.mass, v => loose.AddForce(v, ForceMode.VelocityChange)); return true; }
        Npc person = hit.GetComponentInParent<Npc>();
        if (person) return Mathf.Abs(Speed) >= knockSpeed && person.RunOver(push, knockDamage, transform.position, rider, copRadius);
        Vehicle parked = hit.GetComponentInParent<Vehicle>();
        if (!parked || parked == this || parked.rider) return false; // ponytail: a ridden one is a wall; throw its rider off if that's wanted
        if (!parked.Tumbling) Weapon.Done(rider, "crash");
        Shove(parked.mass, parked.Knock);
        return true;
    }

    void Shove(float theirMass, System.Action<Vector3> give)
    {
        Vector3 v = transform.forward * Speed;
        give(v * (2 * mass / (mass + theirMass)) + Vector3.up * 0.2f * Mathf.Abs(Speed));
        Speed *= (mass - theirMass) / (mass + theirMass); // much lighter than what it hit: bounces back
    }

    // hit by another vehicle: hand it to physics, and set it back on its wheels once it's still
    public void Knock(Vector3 velocity)
    {
        if (!TryGetComponent(out Rigidbody body)) body = gameObject.AddComponent<Rigidbody>();
        if (!Tumbling)
        {
            Tumbling = true;
            enabled = false; // stops the driving Update
            Speed = 0;
            if (TryGetComponent(out CharacterController walker)) { walker.enabled = false; Shell().enabled = true; }
            body.isKinematic = false;
            body.mass = mass;
            StartCoroutine(Settle(body));
        }
        body.AddForce(velocity, ForceMode.VelocityChange);
    }

    // a CharacterController isn't a collider a rigidbody can tumble on, so a bike gets a box round its model for it
    BoxCollider shell;
    BoxCollider Shell()
    {
        if (shell) return shell;
        Bounds local = LocalBounds();
        shell = gameObject.AddComponent<BoxCollider>();
        shell.center = local.center;
        shell.size = local.size;
        return shell;
    }

    // People walk the NavMesh, which knows nothing of colliders, so without this they'd walk straight through a vehicle
    // that wasn't there when it was baked (traffic, or one driven somewhere new): standing still it cuts itself out of
    // the walkable area, moving they step round it. Car and CitiBike call this when they wake.
    protected void BlockPeople()
    {
        Bounds local = LocalBounds();
        var block = gameObject.AddComponent<NavMeshObstacle>();
        block.shape = NavMeshObstacleShape.Box;
        block.center = new Vector3(local.center.x, local.max.y / 2, local.center.z); // from the ground up, under the body too
        block.size = new Vector3(local.size.x, local.max.y, local.size.z);
        block.carving = true;
        block.carveOnlyStationary = true;
    }

    // the box round all of its model, in its own space
    Bounds LocalBounds()
    {
        Bounds local = new Bounds();
        bool first = true;
        foreach (MeshFilter part in GetComponentsInChildren<MeshFilter>())
        {
            if (!part.sharedMesh) continue;
            Bounds b = part.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = transform.InverseTransformPoint(part.transform.TransformPoint(b.center + Vector3.Scale(b.extents, new Vector3(i & 1, i >> 1 & 1, i >> 2 & 1) * 2 - Vector3.one)));
                if (first) local = new Bounds(corner, Vector3.zero); else local.Encapsulate(corner);
                first = false;
            }
        }
        return local;
    }

    // ponytail: a car's box starts at kerb height, so while tumbling it sits that much into the ground
    System.Collections.IEnumerator Settle(Rigidbody body)
    {
        yield return new WaitForSeconds(1f);
        while (body.linearVelocity.sqrMagnitude > 0.05f || body.angularVelocity.sqrMagnitude > 0.05f) yield return null;
        yield return new WaitForSeconds(1f);
        body.isKinematic = true;
        transform.rotation = Quaternion.Euler(0, transform.eulerAngles.y, 0);
        // back onto the ground under it, skipping its own colliders
        float best = float.MaxValue;
        foreach (RaycastHit ground in Physics.RaycastAll(transform.position + Vector3.up * 3, Vector3.down, 10f, ~0, QueryTriggerInteraction.Ignore))
            if (!ground.transform.IsChildOf(transform) && ground.distance < best) { best = ground.distance; transform.position = ground.point; }
        if (TryGetComponent(out CharacterController walker)) { shell.enabled = false; walker.enabled = true; }
        Physics.SyncTransforms();
        Tumbling = false;
        enabled = true;
    }

    Vector2 wish;
    bool sprint;

    // the rider calls this every frame with the move keys (y: forward/back, x: steer) and whether to sprint. Where the
    // camera looks doesn't matter, so the rider can look around while driving
    public void Drive(Vector2 move, bool hard)
    {
        wish = move;
        sprint = hard;
    }

    public virtual void Hop() { }

    // speed and heading for this frame from what the rider wants: returns how far to turn (degrees), with the front
    // wheels' angle wanted and whether it's under power going forward
    protected float Throttle(float dt, out float wantSteer, out bool powered)
    {
        float target = 0, rate = coasting;
        if (wish.y > 0.1f)
        {
            target = sprint ? sprintSpeed : speed;
            rate = Speed < target ? acceleration : coasting;
        }
        else if (wish.y < -0.1f) // back: brake, then back up
        {
            target = -backSpeed;
            rate = Speed > 0 ? braking : acceleration;
        }
        float steerInput = Mathf.Clamp(wish.x, -1, 1);
        wantSteer = steerInput * maxSteer;
        float turn = steerInput * turnRate * Mathf.Max(standingTurn, Mathf.Clamp01(Mathf.Abs(Speed) / fullTurnSpeed)) * dt;
        wish = Vector2.zero; // only while someone keeps driving
        powered = target > 0;
        Speed = Mathf.MoveTowards(Speed, target, rate * dt);
        return turn;
    }
}
