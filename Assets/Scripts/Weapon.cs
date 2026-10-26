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
    [Tooltip("A hit on anyone with Health sprays a quick burst of droplets out of the wound in this colour")]
    public Color bloodColor = new Color(0.55f, 0f, 0.02f);
    [Tooltip("Fewest and most droplets in a spurt")]
    public Vector2Int bloodDroplets = new Vector2Int(14, 24);
    [Tooltip("Slowest and fastest droplet, in m/s")]
    public Vector2 bloodSpeed = new Vector2(5f, 11f);
    [Tooltip("How much the spurt angles towards the camera; 0 sprays it straight across the view")]
    public float bloodTowardCamera = 0.6f;
    [Tooltip("Seconds the gun keeps pointing at the crosshair after a shot")]
    public float aimHoldTime = 0.5f;
    [Tooltip("Degrees the barrel kicks up on each shot (visual only, shots are unaffected)")]
    public float recoilAngle = 15f;
    [Tooltip("Seconds for the barrel to settle back down")]
    public float recoilRecovery = 0.15f;

    [Header("Camera kick")]
    [Tooltip("How hard the camera shakes on each shot, in metres")]
    public float shake = 0.12f;
    [Tooltip("Degrees the camera zooms in on each shot before easing back")]
    public float shotZoom = 3f;

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
    // this weapon's shot just hurt someone: true through the head (WeaponHUD shows a hitmarker, red and HEADSHOT for the head)
    public event Action<bool> Hit;
    // something the player did that a Seagull task can count (HuntMatch): "kill officer", "eat hot dog"... The whole
    // list of deeds is in HuntTasks
    public static event Action<Weapon, string> Did;
    public static void Done(Weapon who, params string[] deeds)
    {
        if (who) foreach (string deed in deeds) if (deed != "") Did?.Invoke(who, deed);
    }
    // what kind of someone this is, for deeds: "officer", "guard", "civilian", "bird", or "" for anything else
    public static string Kind(Component someone)
    {
        Npc npc = someone.GetComponentInParent<Npc>();
        if (npc) return npc.officer ? "officer" : npc.kind == Npc.Kind.Guard ? "guard" : "civilian";
        return someone.GetComponentInParent<NetBird>() ? "bird" : "";
    }
    // `victim` was alive before `who` hurt it: the kill deeds if it's dead now ("kill", "kill officer", "kill jogger",
    // plus `how`, e.g. "roadkill"), and "double kill" / "triple kill" for kills under 3 s apart
    public static void Killed(Weapon who, Health victim, bool wasAlive, params string[] how)
    {
        if (!who || !victim || !wasAlive || !victim.IsDead) return;
        if (victim.GetComponentInParent<BirdFlocks>()) { Done(who, "kill flock bird"); return; } // not a "kill": tasks count people
        string kind = Kind(victim);
        Done(who, "kill");
        if (kind != "") Done(who, "kill " + kind);
        Npc npc = victim.GetComponentInParent<Npc>();
        if (npc && !npc.officer && npc.kind != Npc.Kind.Guard) Done(who, "kill " + npc.kind.ToString().ToLowerInvariant()); // walker, jogger, idler
        Done(who, how);
        who.streak = Time.time - who.lastKill <= 3f ? who.streak + 1 : 1;
        who.lastKill = Time.time;
        if (who.streak == 2) Done(who, "double kill");
        if (who.streak == 3) Done(who, "triple kill");
    }
    int streak;
    float lastKill = -10;
    // nothing in the gun and nothing to reload: time for a silent kill from a head (HeadRide)
    public bool OutOfAmmo => Ammo == 0 && reloadsLeft == 0 && !IsReloading;

    [HideInInspector] public bool unseen; // a food ability (Low profile): people and police pay no attention to its shots

    Camera cam;
    Animator animator;
    CinemachineCamera followCamera;
    CinemachineImpulseSource impulse;
    static Material bloodMaterial; // plain blended, so dark red shows (an additive material can only brighten)
    float normalFov;
    Quaternion gunRestRotation;
    float nextShotTime;
    float aimUntil;
    float recoil; // 1 right after a shot, eases back to 0
    HeadRide ride; // no shooting while driving a Vehicle

    void Awake()
    {
        Money = startingMoney;
        Ammo = magazineSize;
        startReloads = reloadsLeft;
        if (gun) { gunHome = gun.parent; gunAt = gun.localPosition; gunTurn = gun.localRotation; gunSize = gun.localScale; }
        if (!tracerMaterial) tracerMaterial = new Material(Shader.Find("Sprites/Default"));
        cam = Camera.main;
        TryGetComponent(out ride);
        animator = GetComponentInChildren<Animator>();
        followCamera = FindFirstObjectByType<CinemachineCamera>();
        if (followCamera)
        {
            normalFov = followCamera.Lens.FieldOfView;
            // the shot shake: a short recoil-shaped impulse that the follow camera listens for
            if (!followCamera.TryGetComponent(out CinemachineImpulseListener _)) followCamera.gameObject.AddComponent<CinemachineImpulseListener>();
            impulse = gameObject.AddComponent<CinemachineImpulseSource>();
            impulse.ImpulseDefinition.ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Recoil;
            impulse.ImpulseDefinition.ImpulseDuration = 0.15f;
        }
        if (gun) gunRestRotation = gun.localRotation;
        if (muzzleFlash) muzzleFlash.SetActive(false);
        if (TryGetComponent(out Health health)) health.Died += DropGun;
    }

    // on death the gun comes loose and falls on its own, away from the ragdoll
    Transform gunHome, dropped;
    Vector3 gunAt;
    Quaternion gunTurn;
    Vector3 gunSize;
    int startReloads;

    void DropGun()
    {
        if (!gun) return;
        if (muzzleFlash) muzzleFlash.SetActive(false);
        gun.SetParent(null, true);
        var box = gun.gameObject.AddComponent<BoxCollider>(); // sized to the mesh
        foreach (Collider part in GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(box, part);
        var body = gun.gameObject.AddComponent<Rigidbody>();
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic; // small and quick, it would fall through the floor
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.linearVelocity = (TryGetComponent(out CharacterController mover) ? mover.velocity : Vector3.zero) + Vector3.up * 1.5f;
        body.angularVelocity = UnityEngine.Random.insideUnitSphere * 6f;
        dropped = gun;
        gun = null; // stop aiming it
    }

    // gun back in hand and a full load (online respawn and new rounds)
    public void Rearm()
    {
        if (dropped)
        {
            Destroy(dropped.GetComponent<Rigidbody>());
            Destroy(dropped.GetComponent<BoxCollider>());
            dropped.SetParent(gunHome, false);
            dropped.SetLocalPositionAndRotation(gunAt, gunTurn);
            dropped.localScale = gunSize; // dropping kept its world size as its local scale; under the scaled-down bird it shrank
            gun = dropped;
            dropped = null;
        }
        Ammo = magazineSize;
        reloadsLeft = startReloads;
        Changed?.Invoke();
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
        if (IsReloading || Ammo <= 0 || Time.time < nextShotTime || (ride && ride.Driving)) return false; // hands on the bars
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

        if (impulse)
        {
            impulse.GenerateImpulseWithVelocity((UnityEngine.Random.insideUnitSphere + Vector3.up) * shake);
            followCamera.Lens.FieldOfView -= shotZoom; // Update eases it back out
        }

        // hitscan: the crosshair ray already found what was hit, so damage lands instantly
        if (hit.collider)
        {
            Health health = hit.collider.GetComponentInParent<Health>();
            float dealt = damage;
            bool alive = health && !health.IsDead, head = false;
            if (alive)
            {
                Bleed(hit.point, hit.normal);
                head = health.Headshot(cam.ViewportPointToRay(new Vector3(0.5f, 0.5f)));
                if (head) dealt *= health.headshotMultiplier;
                Hit?.Invoke(head);
                string kind = Kind(health);
                Done(this, "hit", head ? "headshot" : "body shot");
                if (kind != "") Done(this, "hit " + kind);
            }
            if (health) health.TakeDamage(dealt, origin);
            float far = Vector3.Distance(transform.position, hit.point);
            Killed(this, health, alive, head ? "headshot kill" : "shot kill", far >= 40 ? "long shot kill" : far <= 3 ? "point blank kill" : "");
            if (hit.rigidbody) hit.rigidbody.AddForceAtPosition((target - origin).normalized * hitKnockback, hit.point, ForceMode.VelocityChange);
        }

        StartCoroutine(Tracer(origin, target));

        Fired?.Invoke(this);
        Done(this, "shoot");
        Changed?.Invoke();
        return true;
    }

    void HideFlash() => muzzleFlash.SetActive(false);

    // a fast spray of droplets out of the wound; the police use it for their hits too
    public void Bleed(Vector3 at, Vector3 normal)
    {
        // straight out of the wound points it at the camera, where it's only a small star: spray it off to the side
        // instead, a little up and towards the camera, so the player sees the whole streak
        Vector3 toCamera = cam ? (cam.transform.position - at).normalized : normal;
        Vector3 side = Vector3.ProjectOnPlane(normal, toCamera);
        if (side.sqrMagnitude < 0.01f) side = cam ? cam.transform.right * (UnityEngine.Random.value < 0.5f ? -1f : 1f) : Vector3.right;
        Vector3 spray = side.normalized + toCamera * bloodTowardCamera + Vector3.up * 0.4f;

        Spray("Blood", at, Quaternion.LookRotation(spray), bloodColor, bloodDroplets, bloodSpeed, new Vector2(0.12f, 0.3f), new Vector2(0.02f, 0.06f), 2f, 18f);
    }

    // a fast burst of shrinking streaks: blood, and the egg bomb's blast (FoodPowers). A cone of 90 or more is a
    // hemisphere. Facing points the cone.
    // ponytail: builds a particle system per burst; pool them if lots at once ever shows up in the profiler
    public static ParticleSystem Spray(string name, Vector3 at, Quaternion facing, Color color, Vector2Int count, Vector2 speed, Vector2 life, Vector2 size, float gravity, float cone)
    {
        var spurt = new GameObject(name);
        spurt.SetActive(false); // set it all up before it plays on waking
        spurt.transform.SetPositionAndRotation(at, facing);
        var particles = spurt.AddComponent<ParticleSystem>();
        var main = particles.main;
        main.duration = 0.05f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
        main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
        main.startColor = color;
        main.gravityModifier = gravity;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = particles.emission;
        emission.rateOverTime = 0;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count.x, (short)count.y) });
        var shape = particles.shape;
        shape.shapeType = cone >= 90f ? ParticleSystemShapeType.Hemisphere : ParticleSystemShapeType.Cone;
        shape.angle = cone;
        shape.radius = 0.02f;
        var shrink = particles.sizeOverLifetime; // shrink away instead of popping out
        shrink.enabled = true;
        shrink.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
        var look = spurt.GetComponent<ParticleSystemRenderer>();
        if (!bloodMaterial) bloodMaterial = new Material(Shader.Find("Sprites/Default"));
        look.sharedMaterial = bloodMaterial;
        look.renderMode = ParticleSystemRenderMode.Stretch; // streaks that read as fast
        look.velocityScale = 0.03f;
        look.lengthScale = 1f;
        look.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        spurt.SetActive(true);
        Destroy(spurt, life.y + 0.2f);
        return particles;
    }

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
        Done(this, "reload");
        Changed?.Invoke();
        Invoke(nameof(FinishReload), reloadTime);
    }

    // picked up a magazine (Pickup)
    public void AddMagazine(int count)
    {
        reloadsLeft += count;
        Done(this, "magazine");
        Changed?.Invoke();
    }

    // ponytail: the player's cash lives here because Pickup and the HUD already talk to the Weapon; move it if money gets a use
    [Tooltip("Cash the player starts with")]
    public int startingMoney;
    public int Money { get; private set; }

    // picked up a wallet (Pickup)
    public void AddMoney(int amount)
    {
        Money += amount;
        Done(this, "wallet");
        Changed?.Invoke();
    }

    // bought something (PieceEater, at a food vendor); false and nothing spent if there isn't enough
    public bool Spend(int amount)
    {
        if (amount > Money) return false;
        Money -= amount;
        Changed?.Invoke();
        return true;
    }

    void FinishReload()
    {
        Ammo = magazineSize;
        IsReloading = false;
        Changed?.Invoke();
    }
}
