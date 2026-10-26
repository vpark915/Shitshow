using UnityEngine;

// A Citi Bike: land on the saddle to ride it (Vehicle has the controls). Jump hops. The wheels roll with the speed, the
// cranks turn while pedalling, the bars steer and the bike leans into turns.
// ponytail: the body is one upright capsule in the middle, so the front and back wheels can poke into walls; Car's
// box sweep if that shows
[RequireComponent(typeof(CharacterController))]
public class CitiBike : Vehicle
{
    [Tooltip("How high a hop goes, in metres")]
    public float hopHeight = 0.5f;

    [Header("Parts (from the model)")]
    public Transform rearWheel, frontWheel, crank;
    [Tooltip("The handlebars and fork; the front wheel is their child so it steers with them")]
    public Transform bars;
    [Tooltip("The model, tilted into turns")]
    public Transform lean;
    public float wheelRadius = 0.335f;
    [Tooltip("Crank turns per wheel turn")]
    public float crankRatio = 0.4f;
    [Tooltip("Furthest it leans into a turn, degrees")]
    public float maxLean = 25f;

    CharacterController body;
    Vector3 rearAxle, frontAxle, crankAxle, steerAxis;
    Quaternion barsRest, leanRest;
    float fallSpeed, steer, yawRate;
    bool struck; // this frame's move hit a person, not a wall

    void Awake()
    {
        body = GetComponent<CharacterController>();
        BlockPeople();
        body.minMoveDistance = 0; // pulling away from a stop moves a fraction of a millimetre a frame
        // each part turns about its own local version of the bike's sideways axis, so a steered front wheel still rolls
        rearAxle = rearWheel.InverseTransformDirection(transform.right);
        frontAxle = frontWheel.InverseTransformDirection(transform.right);
        crankAxle = crank.InverseTransformDirection(transform.right);
        // the fork's head tube: from the bars' pivot down to the front axle
        steerAxis = bars.parent.InverseTransformDirection(bars.position - frontWheel.position).normalized;
        barsRest = bars.localRotation;
        leanRest = lean.localRotation;
    }

    // ran into a person: they go flying and the bike rides on through them (and their flailing limbs)
    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (!RunOver(hit.collider)) return;
        struck = true;
        // light things (limbs, birds) it rides over; heavier ones it shoved are moving off anyway
        if (hit.collider.attachedRigidbody && hit.collider.attachedRigidbody.mass < 10) Physics.IgnoreCollision(body, hit.collider);
    }

    public override void Hop()
    {
        if (body.isGrounded) fallSpeed = Mathf.Sqrt(2 * -Physics.gravity.y * hopHeight);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        float turn = Throttle(dt, out float wantSteer, out bool pedalling);
        transform.Rotate(0, turn, 0);
        yawRate = Mathf.Lerp(yawRate, dt > 0 ? turn / dt : 0, 1f - Mathf.Exp(-6f * dt));

        if (body.isGrounded && fallSpeed < 0) fallSpeed = -2f;
        fallSpeed += Physics.gravity.y * dt;
        struck = false;
        CollisionFlags hit = body.Move((transform.forward * Speed + Vector3.up * fallSpeed) * dt);
        // a wall takes the speed off (only on a hit: a slow start moves less than the body notices, and reads as stopped)
        if ((hit & CollisionFlags.Sides) != 0 && !struck) Speed = Mathf.Clamp(Vector3.Dot(body.velocity, transform.forward), Mathf.Min(0, Speed), Mathf.Max(0, Speed));

        // the parts
        float roll = Speed / wheelRadius * Mathf.Rad2Deg * dt;
        rearWheel.Rotate(rearAxle, roll, Space.Self);
        frontWheel.Rotate(frontAxle, roll, Space.Self);
        if (pedalling) crank.Rotate(crankAxle, roll * crankRatio, Space.Self);
        steer = Mathf.Lerp(steer, wantSteer, 1f - Mathf.Exp(-8f * dt));
        bars.localRotation = Quaternion.AngleAxis(steer, steerAxis) * barsRest;
        // a real bike leans by atan(speed * turn rate / gravity)
        float tilt = Mathf.Atan(Speed * yawRate * Mathf.Deg2Rad / -Physics.gravity.y) * Mathf.Rad2Deg;
        lean.localRotation = Quaternion.Euler(0, 0, -Mathf.Clamp(tilt, -maxLean, maxLean)) * leanRest;
    }
}
