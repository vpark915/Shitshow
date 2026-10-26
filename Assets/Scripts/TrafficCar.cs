using UnityEngine;

// Drives a Car round the streets by itself, through the same controls a rider uses: it heads for the next corner of its
// route (turning off for the one after once it's within Look Ahead, which rounds the corners), keeps to Cruise, slows
// for a sharp turn, and brakes for anything in front of it: the car ahead, a person, a bird, a bike. Land on it and
// it's yours; hop off and it carries on to the next corner from wherever it was left.
// ponytail: one lane, one way round, no lights or junctions, and each game runs its own traffic (not synced online);
// two-way streets, signals and syncing are what to add if the streets need to matter more
[RequireComponent(typeof(Car))]
public class TrafficCar : MonoBehaviour
{
    [Tooltip("The corners of the loop it drives, in order (world positions on the road)")]
    public Vector3[] route = { };
    [Tooltip("Speed it keeps to, m/s")]
    public float cruise = 8f;
    [Tooltip("How far from a corner it starts turning for the next one, metres")]
    public float lookAhead = 9f;
    [Tooltip("Gap it keeps to whatever's ahead: 3 m plus this many seconds at its speed")]
    public float gap = 1.2f;
    [Tooltip("Seconds it waits for something in the way that isn't traffic (someone standing in the road, a dumped car) before pushing through it")]
    public float patience = 4f;

    Car car;
    BoxCollider box;
    int next = -1;
    float waited, pushUntil;

    // driving itself round, not taken over or knocked over: worth queueing behind
    public bool Driving => route.Length > 1 && !car.rider && !car.Tumbling;

    void Awake()
    {
        car = GetComponent<Car>();
        box = GetComponent<BoxCollider>();
    }

    void Update()
    {
        if (route.Length < 2 || car.rider || car.Tumbling) { next = -1; return; } // someone else is driving, or it's been knocked over
        if (next < 0) next = Upcoming();
        if (Flat(route[next] - transform.position) < lookAhead) next = (next + 1) % route.Length;

        float off = Vector3.SignedAngle(transform.forward, Flat3(route[next] - transform.position), Vector3.up);
        float steer = Mathf.Clamp(off / 30f, -1, 1);
        float pace = Mathf.Abs(off) > 30 ? cruise * 0.5f : cruise;
        bool blocked = Blocked(out bool queue);
        // it queues behind traffic for as long as it takes; for anything else it waits a while, then pushes through
        // slowly (the car shoves a dumped car aside and knocks a person down, as it would anything it hits)
        waited = blocked && !queue ? waited + Time.deltaTime : 0;
        if (waited > patience) pushUntil = Time.time + 2f;
        if (Time.time < pushUntil && !queue) { blocked = false; pace = Mathf.Min(pace, 3f); }
        float throttle = blocked ? (car.Speed > 0.3f ? -1 : 0) // brake, but don't back up
            : car.Speed < pace ? 1 : 0;
        car.Drive(new Vector2(steer, throttle), false);
    }

    // something in the lane ahead within stopping room worth stopping for: another vehicle, someone on their feet, or
    // anything fixed. Not park birds, bodies, food or other loose things: it drives on through those (Vehicle.RunOver),
    // or one bird on the road would hold up the whole loop. queue: it's traffic, so wait for it however long
    bool Blocked(out bool queue)
    {
        queue = false;
        bool blocked = false;
        Vector3 half = Vector3.Scale(box.size, transform.lossyScale) / 2;
        half.x *= 0.8f; // a parked car's mirror at the lane's edge doesn't count
        float room = 3f + Mathf.Max(0, car.Speed) * gap;
        // the box is raised above kerb height, so not the road
        foreach (RaycastHit hit in Physics.BoxCastAll(transform.TransformPoint(box.center), half, transform.forward, transform.rotation, room, ~0, QueryTriggerInteraction.Ignore))
        {
            Collider thing = hit.collider;
            if (thing.transform.IsChildOf(transform) || thing.GetComponentInParent<BirdFlocks>()) continue;
            if (thing.attachedRigidbody && !thing.attachedRigidbody.isKinematic) continue; // ragdolls, dead birds, tumbling things
            if (thing.GetComponentInParent<FoodItem>() || thing.GetComponentInParent<Pickup>()) continue;
            TrafficCar other = thing.GetComponentInParent<TrafficCar>();
            if (other && other.Driving) queue = true;
            blocked = true;
        }
        return blocked;
    }

    // the corner it should head for from here: the next one round from the corner nearest, so it never turns back
    int Upcoming()
    {
        int nearest = 0;
        for (int i = 1; i < route.Length; i++)
            if (Flat(route[i] - transform.position) < Flat(route[nearest] - transform.position)) nearest = i;
        int after = (nearest + 1) % route.Length;
        // already out along the road leaving the nearest corner: on to the one after
        return Vector3.Dot(transform.position - route[nearest], route[after] - route[nearest]) > 0 ? after : nearest;
    }

    static Vector3 Flat3(Vector3 v) => new Vector3(v.x, 0, v.z);
    static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;
}
