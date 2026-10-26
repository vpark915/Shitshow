using UnityEngine;

// A car: land on the roof to drive it (Vehicle has the controls). The body is a box raised to kerb height: each frame
// it's swept along its move and stops at whatever's in the way, then set down on the ground under its middle, so it
// climbs kerbs and drops off edges. The wheels roll with the speed and the front ones steer.
// ponytail: turning isn't swept, so a car turning against a wall can swing its corner into it; and it stops for
// anything solid, even a bagel. Sweep the turn, or a layer mask, if either shows
[RequireComponent(typeof(BoxCollider))]
public class Car : Vehicle
{
    [Header("Parts (from the model)")]
    public Transform[] rearWheels = { }, frontWheels = { };
    public float wheelRadius = 0.38f;
    [Tooltip("Highest kerb or step it drives up, metres; the box starts just above this")]
    public float stepHeight = 0.25f;

    [Header("Off the road")]
    [Tooltip("Name the road's objects start with (WSP_Road, WSP_Road_Cobble); driven anywhere else (pavement, the park) the driver is wanted. None in the scene (the Hunt Gym): anywhere goes")]
    public string road = "WSP_Road";
    [Tooltip("Police this close, metres, come after a driver off the road (big: the whole map hears about it)")]
    public float offRoadCopRadius = 1000f;
    [Tooltip("Seconds the driver stays wanted after last being off the road; the police keep hunting till it's up")]
    public float wantedTime = 30f;

    const float Skin = 0.02f;

    BoxCollider box;
    Transform[] wheels;
    Quaternion[] wheelRest;
    Vector3[] axles;
    float fallSpeed, steer, roll, nextReport;
    bool hasRoad;

    void Awake()
    {
        box = GetComponent<BoxCollider>();
        hasRoad = GameObject.Find(road);
        BlockPeople();
        wheels = new Transform[rearWheels.Length + frontWheels.Length];
        rearWheels.CopyTo(wheels, 0);
        frontWheels.CopyTo(wheels, rearWheels.Length);
        wheelRest = new Quaternion[wheels.Length];
        axles = new Vector3[wheels.Length];
        for (int i = 0; i < wheels.Length; i++)
        {
            wheelRest[i] = wheels[i].localRotation;
            axles[i] = wheels[i].InverseTransformDirection(transform.right);
        }
    }

    void Update()
    {
        float dt = Time.deltaTime;
        float turn = Throttle(dt, out float wantSteer, out _);
        // a car pivots about its back axle, so reversing turns the other way
        transform.Rotate(0, Speed < 0 ? -turn : turn, 0);

        // into walls: sweep the box along the move (a cast ignores what it starts inside, so not this car itself)
        Vector3 move = transform.forward * Speed * dt;
        Vector3 centre = transform.TransformPoint(box.center), half = Vector3.Scale(box.size, transform.lossyScale) / 2 - Vector3.one * Skin; // a hair smaller: not stopped by the rider on the roof
        // ponytail: only the first thing hit counts, so a wall right behind someone run over gets driven into for a frame
        if (move.sqrMagnitude > 0 && Physics.BoxCast(centre, half, move.normalized, out RaycastHit wall, transform.rotation, move.magnitude + Skin, ~0, QueryTriggerInteraction.Ignore)
            && !RunOver(wall.collider))
        {
            move = move.normalized * Mathf.Max(0, wall.distance - Skin);
            Speed = 0;
        }
        transform.position += move;

        // onto the ground under the middle: up a kerb, or falling off an edge
        fallSpeed += Physics.gravity.y * dt;
        float drop = Mathf.Max(0, -fallSpeed * dt);
        Vector3 from = transform.position + Vector3.up * stepHeight;
        if (Physics.Raycast(from, Vector3.down, out RaycastHit ground, stepHeight + drop + 0.05f, ~0, QueryTriggerInteraction.Ignore))
        {
            transform.position = new Vector3(transform.position.x, ground.point.y, transform.position.z);
            fallSpeed = 0;
            // a bird driving on the pavement or through the park: the police come, and keep coming while it stays off the road
            bool onRoad = ground.collider.name.StartsWith(road);
            if (rider && hasRoad && !onRoad && Time.time >= nextReport)
            {
                nextReport = Time.time + 1f;
                Npc.Report(rider, transform.position, offRoadCopRadius, null, wantedTime);
            }
        }
        else transform.position += Vector3.up * fallSpeed * dt;

        // the wheels
        roll += Speed / wheelRadius * Mathf.Rad2Deg * dt;
        steer = Mathf.Lerp(steer, wantSteer, 1f - Mathf.Exp(-8f * dt));
        for (int i = 0; i < wheels.Length; i++)
        {
            Quaternion turned = i >= rearWheels.Length ? Quaternion.AngleAxis(steer, wheels[i].parent.InverseTransformDirection(transform.up)) : Quaternion.identity;
            wheels[i].localRotation = turned * wheelRest[i] * Quaternion.AngleAxis(roll, axles[i]);
        }
    }
}
