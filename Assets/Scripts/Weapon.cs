using System;
using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

// Put on the player root. Hitscan: instantly hits whatever is under the screen-centre crosshair and draws a tracer from the gun.
public class Weapon : MonoBehaviour
{
    [Header("Ammo")]
    public int magazineSize = 12;
    public int reloadsLeft = 3;
    public float reloadTime = 1f;

    [Header("Shot")]
    public float damage = 10f;
    [Tooltip("Furthest distance a shot can hit")]
    public float range = 200f;
    [Tooltip("Seconds between shots while fire is held")]
    public float fireInterval = 0.15f;
    [Tooltip("Speed in m/s given to a physics body that gets hit (e.g. a ragdoll bone)")]
    public float hitKnockback = 3f;

    [Header("Tracer")]
    [Tooltip("Leave empty for a plain unlit line")]
    public Material tracerMaterial;
    public Color tracerColor = new Color(1f, 0.9f, 0.4f);
    public float tracerWidth = 0.05f;
    [Tooltip("Length of the streak in metres")]
    public float tracerLength = 4f;
    [Tooltip("How fast the streak travels from the barrel to the hit point, in m/s")]
    public float tracerSpeed = 200f;

    [Header("Gun")]
    [Tooltip("Gun model the tracer comes out of; its local +Z is the barrel direction. Falls back to this object if empty")]
    public Transform gun;
    [Tooltip("Barrel tip, in the gun's local space")]
    public Vector3 muzzleOffset = new Vector3(0f, 0.5f, 0f);
    [Tooltip("Object at the barrel tip that is shown briefly on each shot")]
    public GameObject muzzleFlash;
    public float flashTime = 0.05f;
    [Tooltip("Seconds the gun keeps pointing at the crosshair after a shot")]
    public float aimHoldTime = 0.5f;
    [Tooltip("Degrees the barrel kicks up on each shot (visual only, shots are unaffected)")]
    public float recoilAngle = 15f;
    [Tooltip("Seconds for the barrel to settle back down")]
    public float recoilRecovery = 0.15f;

    [Header("Animation")]
    [Tooltip("Played on the child Animator for each shot; needs a state named after the clip")]
    public AnimationClip shootAnimation;

    [Header("Scope")]
    [Tooltip("Camera field of view while the scope button is held")]
    public float scopedFov = 30f;
    [Tooltip("How quickly the camera eases in and out of the scope; higher is snappier")]
    public float scopeSpeed = 12f;

    [Header("Input")]
    public InputAction fireAction = new InputAction("Fire", binding: "<Mouse>/leftButton");
    public InputAction reloadAction = new InputAction("Reload", binding: "<Keyboard>/r");
    public InputAction scopeAction = new InputAction("Scope", binding: "<Mouse>/rightButton");

    public int Ammo { get; private set; }
    public bool IsReloading { get; private set; }

    // raised whenever ammo, reloads or reload state changes
    public event Action Changed;
    // every shot from any weapon, with who fired it; park NPCs, police and birds listen for this
    public static event Action<Weapon> Fired;

    Camera cam;
    Animator animator;
    CinemachineCamera followCamera;
    float normalFov;
    Quaternion gunRestRotation;
    float nextShotTime;
    float aimUntil;
    float recoil; // 1 right after a shot, eases back to 0

    void Awake()
    {
        Ammo = magazineSize;
        if (!tracerMaterial) tracerMaterial = new Material(Shader.Find("Sprites/Default"));
        cam = Camera.main;
        animator = GetComponentInChildren<Animator>();
        followCamera = FindFirstObjectByType<CinemachineCamera>();
        if (followCamera) normalFov = followCamera.Lens.FieldOfView;
        if (gun) gunRestRotation = gun.localRotation;
        if (muzzleFlash) muzzleFlash.SetActive(false);
    }

    void OnEnable()
    {
        fireAction.Enable();
        reloadAction.Enable();
        scopeAction.Enable();
    }

    void OnDisable()
    {
        fireAction.Disable();
        reloadAction.Disable();
        scopeAction.Disable();
    }

    void Update()
    {
        if (reloadAction.WasPressedThisFrame() || (Ammo == 0 && fireAction.WasPressedThisFrame())) Reload();
        if (fireAction.IsPressed()) TryFire();

        // scope: ease the camera's field of view in while held, back out on release, and keep the gun on the crosshair
        bool scoped = scopeAction.IsPressed();
        if (scoped) aimUntil = Mathf.Max(aimUntil, Time.time + 0.1f);
        if (followCamera)
            followCamera.Lens.FieldOfView = Mathf.Lerp(followCamera.Lens.FieldOfView, scoped ? scopedFov : normalFov,
                1f - Mathf.Exp(-scopeSpeed * Time.deltaTime));
    }

    // after the Animator has posed the wing: keep the barrel on the crosshair while shooting, otherwise let it ride the bone
    void LateUpdate()
    {
        if (!gun) return;
        if (Time.time < aimUntil) gun.rotation = Quaternion.LookRotation(AimPoint(out _) - gun.position);
        else gun.localRotation = gunRestRotation;

        recoil = Mathf.MoveTowards(recoil, 0f, Time.deltaTime / recoilRecovery);
        gun.rotation *= Quaternion.Euler(-recoilAngle * recoil, 0f, 0f);
    }

    // whatever is under the crosshair, ignoring the shooter's own layer
    Vector3 AimPoint(out RaycastHit hit)
    {
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f));
        return Physics.Raycast(ray, out hit, range, ~(1 << gameObject.layer), QueryTriggerInteraction.Ignore)
            ? hit.point
            : ray.GetPoint(range);
    }

    public bool TryFire()
    {
        if (IsReloading || Ammo <= 0 || Time.time < nextShotTime) return false;
        nextShotTime = Time.time + fireInterval;
        Ammo--;

        // point the gun at the shot before reading the muzzle position
        Vector3 target = AimPoint(out RaycastHit hit);
        aimUntil = Time.time + aimHoldTime;
        recoil = 1f;
        if (gun) gun.rotation = Quaternion.LookRotation(target - gun.position);
        Vector3 origin = (gun ? gun : transform).TransformPoint(muzzleOffset);

        if (shootAnimation && animator && animator.isActiveAndEnabled)
            animator.CrossFadeInFixedTime(shootAnimation.name, 0.05f, 0, 0f);

        if (muzzleFlash)
        {
            muzzleFlash.transform.localRotation = Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(0f, 360f));
            muzzleFlash.SetActive(true);
            CancelInvoke(nameof(HideFlash));
            Invoke(nameof(HideFlash), flashTime);
        }

        // hitscan: the crosshair ray already found what was hit, so damage lands instantly
        if (hit.collider)
        {
            Health health = hit.collider.GetComponentInParent<Health>();
            if (health) health.TakeDamage(damage);
            if (hit.rigidbody) hit.rigidbody.AddForceAtPosition((target - origin).normalized * hitKnockback, hit.point, ForceMode.VelocityChange);
        }

        StartCoroutine(Tracer(origin, target));

        Fired?.Invoke(this);
        Changed?.Invoke();
        return true;
    }

    void HideFlash() => muzzleFlash.SetActive(false);

    // ponytail: the police borrow this weapon's tracer for their own shots; give them their own when the two should look different
    public void DrawTracer(Vector3 from, Vector3 to) => StartCoroutine(Tracer(from, to));

    // a short streak that flies from the barrel to the hit point, then disappears
    IEnumerator Tracer(Vector3 from, Vector3 to)
    {
        LineRenderer line = new GameObject("Tracer").AddComponent<LineRenderer>();
        line.sharedMaterial = tracerMaterial;
        line.startColor = line.endColor = tracerColor;
        line.startWidth = 0f; // tail tapers to a point
        line.endWidth = tracerWidth;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        float distance = Vector3.Distance(from, to);
        Vector3 direction = (to - from) / distance;
        // start one step out so the streak is already visible on the frame of the shot
        for (float head = tracerSpeed * Time.deltaTime; head < distance + tracerLength; head += tracerSpeed * Time.deltaTime)
        {
            line.SetPosition(0, from + direction * Mathf.Clamp(head - tracerLength, 0f, distance));
            line.SetPosition(1, from + direction * Mathf.Min(head, distance));
            yield return null;
        }

        Destroy(line.gameObject);
    }

    public void Reload()
    {
        if (IsReloading || reloadsLeft <= 0 || Ammo == magazineSize) return;
        IsReloading = true;
        reloadsLeft--;
        Changed?.Invoke();
        Invoke(nameof(FinishReload), reloadTime);
    }

    void FinishReload()
    {
        Ammo = magazineSize;
        IsReloading = false;
        Changed?.Invoke();
    }
}
