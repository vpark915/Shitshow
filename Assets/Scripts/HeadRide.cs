using StarterAssets;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

// Put on the pigeon. Land on a person's head to ride them: move, sprint and jump drive the person, the camera pulls
// back to frame them, and the pigeon can still look around and shoot. Landing on a Vehicle's perch (a Citi Bike's
// saddle) drives it the same way. The dismount key hops off. Out of ammo on someone's head (a police officer or
// anyone else), the assassinate key kills them silently: they drop as a ragdoll and no one hears a thing.
[DefaultExecutionOrder(-1)] // pose the pigeon before ThirdPersonController aims the camera target, which is its child
public class HeadRide : MonoBehaviour
{
    [Tooltip("How far from the middle of a head, in metres, still counts as landing on it")]
    public float landRadius = 0.35f;
    [Tooltip("Seconds after hopping off before another head can be landed on")]
    public float remountDelay = 0.5f;

    [Header("Riding (m/s)")]
    public float walkSpeed = 3f;
    public float sprintSpeed = 6.5f;
    [Tooltip("How high the person jumps, in metres")]
    public float jumpHeight = 1.2f;
    [Tooltip("Pigeon's feet above the person's head bone, in metres")]
    public float perchHeight = 0.12f;

    [Header("Camera while riding")]
    public float cameraDistance = 4f;
    [Tooltip("Height above the person's feet that the camera looks at")]
    public float cameraHeight = 1.5f;
    [Tooltip("Sideways shift of the camera, so the person stands beside the crosshair instead of under it")]
    public Vector3 cameraShoulderOffset = new Vector3(-0.8f, 0f, 0f);

    [Header("Input")]
    public InputAction dismountAction = new InputAction("Dismount", binding: "<Keyboard>/q");
    [Tooltip("Silent kill on the person being ridden; only with no ammo left (the eat key: PieceEater leaves it alone then)")]
    public InputAction assassinateAction = new InputAction("Assassinate", binding: "<Keyboard>/e");

    public Npc Mount { get; private set; }
    public Vehicle Driving { get; private set; }
    public bool Riding => Mount || Driving;
    // on someone's head with nothing left to shoot
    public bool CanAssassinate => Mount && weapon && weapon.OutOfAmmo && !(health && health.IsDead);

    ThirdPersonController controller;
    StarterAssetsInputs input;
    CharacterController body;
    Health health;
    Weapon weapon;
    CinemachineThirdPersonFollow follow;
    Transform cameraTarget, head;
    Vector3 targetRest, shoulderRest;
    float distanceRest, nextMount;
    int mountLayer;

    void Start()
    {
        controller = GetComponent<ThirdPersonController>();
        input = GetComponent<StarterAssetsInputs>();
        body = GetComponent<CharacterController>();
        TryGetComponent(out health);
        TryGetComponent(out weapon);
        follow = FindFirstObjectByType<CinemachineThirdPersonFollow>();
        cameraTarget = controller.CinemachineCameraTarget.transform;
        targetRest = cameraTarget.localPosition;
        distanceRest = follow.CameraDistance;
        shoulderRest = follow.ShoulderOffset;
    }

    void OnEnable() { dismountAction.Enable(); assassinateAction.Enable(); }
    void OnDisable() { dismountAction.Disable(); assassinateAction.Disable(); }

    float Yaw => cameraTarget.eulerAngles.y;

    void Update()
    {
        if (Driving)
        {
            if (dismountAction.WasPressedThisFrame() || (health && health.IsDead)) { Dismount(); return; }
            if (input.jump) Driving.Hop();
            input.jump = false;
            Driving.Drive(input.move, input.sprint);
        }
        else if (Mount)
        {
            // off on the key, or thrown off when either of them dies
            if (dismountAction.WasPressedThisFrame() || !Mount.enabled || (health && health.IsDead)) { Dismount(); return; }
            if (CanAssassinate && assassinateAction.WasPressedThisFrame()) { Assassinate(); return; }
            if (input.jump) Mount.Jump(jumpHeight);
            input.jump = false;
            Vector3 direction = Quaternion.Euler(0, Yaw, 0) * new Vector3(input.move.x, 0, input.move.y);
            Mount.Steer(direction.normalized * (input.sprint ? sprintSpeed : walkSpeed));
        }
        else if (Time.time >= nextMount && body.enabled && body.velocity.y <= 0.1f) // alive, and coming down or standing, not jumping up past a head
            foreach (Collider near in Physics.OverlapSphere(transform.position, 0.65f))
            {
                if (near.TryGetComponent(out Vehicle vehicle) && !vehicle.Tumbling)
                {
                    Vector3 fromPerch = transform.position - vehicle.perch.position;
                    if (Mathf.Abs(fromPerch.y) > 0.3f || Vector3.ProjectOnPlane(fromPerch, Vector3.up).magnitude > vehicle.landRadius) continue;
                    Ride(vehicle);
                    break;
                }
                if (!near.TryGetComponent(out Npc npc)) continue;
                Vector3 fromHead = transform.position - (npc.transform.position + Vector3.up * NpcSpawner.Height);
                if (Mathf.Abs(fromHead.y) > 0.3f || Vector3.ProjectOnPlane(fromHead, Vector3.up).magnitude > landRadius) continue;
                Ride(npc);
                break;
            }
    }

    void LateUpdate()
    {
        // ease the camera out to person size and back
        float riding = Driving ? Driving.cameraDistance : cameraDistance;
        follow.CameraDistance = Mathf.Lerp(follow.CameraDistance, Riding ? riding : distanceRest, 1f - Mathf.Exp(-8f * Time.deltaTime));
        follow.ShoulderOffset = Vector3.Lerp(shoulderRest, cameraShoulderOffset, Mathf.InverseLerp(distanceRest, riding, follow.CameraDistance));
        if (Driving)
        {
            transform.SetPositionAndRotation(Driving.perch.position, Quaternion.Euler(0, Driving.transform.eulerAngles.y, 0)); // faces the road, not the camera
            cameraTarget.position = Driving.transform.position + Vector3.up * cameraHeight;
            return;
        }
        if (!Mount) return;

        // after the Animator, so the pigeon rides the head as it bobs; the camera frames the steadier body
        transform.SetPositionAndRotation(head.position + Vector3.up * perchHeight, Quaternion.Euler(0, Yaw, 0));
        cameraTarget.position = Mount.transform.position + Vector3.up * cameraHeight;
    }

    // no shot, so nobody hears it (police only come for gunfire, being run over, or a car off the road); they go limp
    // and fall off the head, and the next Update sees them dead and hops the pigeon off
    void Assassinate()
    {
        if (!Mount.TryGetComponent(out Health victim) || victim.IsDead) return;
        string kind = Weapon.Kind(Mount);
        victim.TakeDamage(victim.Current + 1, transform.position);
        Weapon.Done(weapon, "assassinate", "assassinate " + kind);
        Weapon.Killed(weapon, victim, true, "silent kill");
    }

    void Ride(Npc npc)
    {
        Mount = npc;
        head = npc.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head);
        npc.SetRidden(true);
        Mounted(npc.gameObject);
        Weapon.Done(weapon, "ride person", "ride " + Weapon.Kind(npc));
    }

    void Ride(Vehicle vehicle)
    {
        Driving = vehicle;
        TryGetComponent(out vehicle.rider);
        Mounted(vehicle.gameObject);
        Weapon.Done(weapon, vehicle is Car ? "drive car" : "ride bike");
        if (vehicle.TryGetComponent(out TrafficCar _)) Weapon.Done(weapon, "hijack");
    }

    void Mounted(GameObject mount)
    {
        Carry(mount, true);
        // on the pigeon's layer the mount is skipped by its own shots, the same way the pigeon is
        mountLayer = mount.layer;
        mount.layer = gameObject.layer;
        controller.Riding = true;
        input.jump = false;
    }

    // the pigeon sits inside the mount's body: stop the two blocking each other while it's carried
    void Carry(GameObject mount, bool on)
    {
        // a person's walking body, a vehicle's only collider
        Collider mountBody = mount.TryGetComponent(out CharacterController walker) ? walker : mount.GetComponent<Collider>();
        if (!mountBody.enabled) return; // died under the pigeon
        foreach (Collider mine in GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(mine, mountBody, on);
    }

    public void Dismount()
    {
        GameObject mount = Driving ? Driving.gameObject : Mount.gameObject;
        Carry(mount, false);
        mount.layer = mountLayer;
        if (Mount) Mount.SetRidden(false);
        if (Driving) Driving.rider = null;
        Mount = null;
        Driving = null;
        nextMount = Time.time + remountDelay;
        cameraTarget.localPosition = targetRest;
        Physics.SyncTransforms(); // the CharacterController was carried here without Move, so tell it where it is
        controller.Riding = false;
        controller.Jump();
        input.jump = false;
    }
}
