using StarterAssets;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

// Put on the pigeon. Land on a person's head to ride them: move, sprint and jump drive the person, the camera pulls
// back to frame them, and the pigeon can still look around and shoot. The dismount key hops off.
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

    public Npc Mount { get; private set; }

    ThirdPersonController controller;
    StarterAssetsInputs input;
    CharacterController body;
    Health health;
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
        follow = FindFirstObjectByType<CinemachineThirdPersonFollow>();
        cameraTarget = controller.CinemachineCameraTarget.transform;
        targetRest = cameraTarget.localPosition;
        distanceRest = follow.CameraDistance;
        shoulderRest = follow.ShoulderOffset;
    }

    void OnEnable() => dismountAction.Enable();
    void OnDisable() => dismountAction.Disable();

    float Yaw => cameraTarget.eulerAngles.y;

    void Update()
    {
        if (Mount)
        {
            // off on the key, or thrown off when either of them dies
            if (dismountAction.WasPressedThisFrame() || !Mount.enabled || (health && health.IsDead)) { Dismount(); return; }
            if (input.jump) Mount.Jump(jumpHeight);
            input.jump = false;
            Vector3 direction = Quaternion.Euler(0, Yaw, 0) * new Vector3(input.move.x, 0, input.move.y);
            Mount.Steer(direction.normalized * (input.sprint ? sprintSpeed : walkSpeed));
        }
        else if (Time.time >= nextMount && body.enabled && body.velocity.y <= 0.1f) // alive, and coming down or standing, not jumping up past a head
            foreach (Collider near in Physics.OverlapSphere(transform.position, landRadius + 0.3f))
            {
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
        follow.CameraDistance = Mathf.Lerp(follow.CameraDistance, Mount ? cameraDistance : distanceRest, 1f - Mathf.Exp(-8f * Time.deltaTime));
        follow.ShoulderOffset = Vector3.Lerp(shoulderRest, cameraShoulderOffset, Mathf.InverseLerp(distanceRest, cameraDistance, follow.CameraDistance));
        if (!Mount) return;

        // after the Animator, so the pigeon rides the head as it bobs; the camera frames the steadier body
        transform.SetPositionAndRotation(head.position + Vector3.up * perchHeight, Quaternion.Euler(0, Yaw, 0));
        cameraTarget.position = Mount.transform.position + Vector3.up * cameraHeight;
    }

    void Ride(Npc npc)
    {
        Mount = npc;
        head = npc.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head);
        npc.SetRidden(true);
        Carry(true);
        // on the pigeon's layer the person is skipped by its own shots, the same way the pigeon is
        mountLayer = npc.gameObject.layer;
        npc.gameObject.layer = gameObject.layer;
        controller.Riding = true;
        input.jump = false;
    }

    // the pigeon sits inside the top of the person's body: stop the two blocking each other while it's carried
    void Carry(bool on)
    {
        var mountBody = Mount.GetComponent<CharacterController>();
        if (!mountBody.enabled) return; // died under the pigeon
        foreach (Collider mine in GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(mine, mountBody, on);
    }

    public void Dismount()
    {
        Carry(false);
        Mount.gameObject.layer = mountLayer;
        Mount.SetRidden(false);
        Mount = null;
        nextMount = Time.time + remountDelay;
        cameraTarget.localPosition = targetRest;
        Physics.SyncTransforms(); // the CharacterController was carried here without Move, so tell it where it is
        controller.Riding = false;
        controller.Jump();
        input.jump = false;
    }
}
