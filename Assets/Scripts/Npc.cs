using UnityEngine;
using UnityEngine.AI;

// One person in the park, added by NpcSpawner. The NavMeshAgent moves them and the animation follows its speed.
// Police are the same thing with officer ticked: instead of running from a gunshot they go after whoever fired it.
public class Npc : MonoBehaviour
{
    public enum Kind { Walker, Jogger, Idler, Guard }

    static readonly int Speed = Animator.StringToHash("Speed"), Hangout = Animator.StringToHash("Hangout"), Cadence = Animator.StringToHash("Cadence"),
        Talk = Animator.StringToHash("Talk"), TalkStyle = Animator.StringToHash("TalkStyle");
    const int TalkStyles = 1; // clips in the Talk blend tree in Npc.controller, at thresholds 0, 1, 2...
    const float WalkPace = 1.4f; // m/s the walk clip covers at normal playback; the same number is its threshold in Npc.controller

    public Kind kind;
    public NpcSpawner spawner;
    [HideInInspector] public bool officer;
    [HideInInspector] public int routeIndex; // joggers: the route point they're heading for
    [HideInInspector] public Vector3 lookAt; // idlers: what they stand facing

    NavMeshAgent agent;
    Animator animator;
    CharacterController body; // only while ridden
    float waitUntil, fleeUntil, chatUntil, calmSpeed, fallSpeed;
    int routeStep;
    Vector3 post; // guards: where they stand
    Weapon suspect; // officers: who they're shooting at
    float calmAt, nextAct;
    bool aiming;
    Vector3 aimPoint;
    Transform arm, hand, gun;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();
        if (TryGetComponent(out Health health)) health.Died += Die;
    }

    void OnEnable() => Weapon.Fired += OnShot;
    void OnDisable() => Weapon.Fired -= OnShot;

    void Start()
    {
        post = transform.position;
        routeStep = Random.value < 0.5f ? 1 : -1;
        if (routeStep < 0) routeIndex++; // spawned between routeIndex and the next point, so running backwards starts by returning to it
        if (kind != Kind.Idler) Next();
    }

    void Update()
    {
        Vector3 velocity = Ridden ? Vector3.ProjectOnPlane(body.velocity, Vector3.up) : agent.velocity;
        animator.SetFloat(Speed, velocity.magnitude, 0.15f, Time.deltaTime);
        // slower than the walk clip: play it slower instead of sliding the feet
        animator.SetFloat(Cadence, Mathf.Clamp(animator.GetFloat(Speed) / WalkPace, 0.5f, 1f));
        if (Ridden) return;
        // The NavMesh floats a few centimetres over the real ground (up to 20 on slopes and kerbs), so stand on what's under the feet.
        // ponytail: the ray starts at knee height and takes whatever it meets first, so a pigeon walking between someone's feet lifts them briefly
        if (Physics.Raycast(transform.position + Vector3.up * 0.5f, Vector3.down, out RaycastHit floor, 1f, ~0, QueryTriggerInteraction.Ignore))
            agent.baseOffset = Mathf.MoveTowards(agent.baseOffset, agent.baseOffset + floor.point.y - transform.position.y, Time.deltaTime);
        if (suspect) { Pursue(); return; }
        if (fleeUntil > 0)
        {
            animator.SetBool(Hangout, false);
            animator.SetBool(Talk, false);
            chatUntil = 0;
            if (Time.time < fleeUntil) return;
            fleeUntil = 0;
            agent.speed = calmSpeed;
            Next();
            return;
        }
        bool arrived = !agent.pathPending && agent.remainingDistance <= (kind == Kind.Jogger ? 3f : 0.5f);
        if (chatUntil > 0)
        {
            animator.SetBool(Talk, arrived);
            if (arrived) Face(lookAt);
            if (Time.time < chatUntil) return;
            chatUntil = 0;
            animator.SetBool(Talk, false);
            Next();
            return;
        }
        animator.SetBool(Hangout, arrived && kind == Kind.Idler);
        if (!arrived) return;

        if (kind == Kind.Jogger) { Next(); return; }
        if (kind == Kind.Guard) return;
        if (kind == Kind.Idler) Face(lookAt);
        // walkers stop for a moment and move on; idlers stay put for a while before trying somewhere else
        if (waitUntil == 0) waitUntil = Time.time + (kind == Kind.Idler ? Random.Range(20f, 90f) : Random.Range(0f, 6f));
        else if (Time.time >= waitUntil) Next();
    }

    void Face(Vector3 point)
    {
        Vector3 facing = Vector3.ProjectOnPlane(point - transform.position, Vector3.up);
        if (facing != Vector3.zero) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(facing), 180 * Time.deltaTime);
    }

    // NpcSpawner: walkers with nothing better to do can be called over for a chat
    public bool CanChat => enabled && kind == Kind.Walker && !officer && !Ridden && fleeUntil == 0 && chatUntil == 0;

    // walk to spot, then stand talking towards centre until the time is up
    public void Chat(Vector3 spot, Vector3 centre, float seconds)
    {
        agent.SetDestination(spot);
        lookAt = centre;
        chatUntil = Time.time + seconds;
        animator.SetFloat(TalkStyle, Random.Range(0, TalkStyles));
    }

    // a gunshot close by: sprint straight away from it, then calm down and carry on
    void OnShot(Weapon shooter)
    {
        if (Ridden) return;
        Vector3 away = Vector3.ProjectOnPlane(transform.position - shooter.transform.position, Vector3.up);
        if (officer)
        {
            if (away.magnitude > spawner.policeHearing) return;
            if (!suspect)
            {
                calmSpeed = agent.speed;
                agent.speed = spawner.jogSpeed;
                nextAct = Time.time + Random.Range(0.6f, 1.2f); // time to react and draw
                if (!gun)
                {
                    arm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                    hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                    gun = Instantiate(spawner.policeGun, hand).transform;
                    gun.localPosition = Vector3.zero;
                    gun.localScale = Vector3.one * spawner.policeGunScale / hand.lossyScale.x;
                }
                gun.gameObject.SetActive(true);
            }
            suspect = shooter;
            calmAt = Time.time + spawner.policeAttention;
            return;
        }
        if (away.magnitude > spawner.panicRadius) return;
        if (fleeUntil == 0) calmSpeed = agent.speed;
        fleeUntil = Time.time + spawner.panicTime * Random.Range(0.8f, 1.2f);
        agent.speed = spawner.fleeSpeed * Random.Range(0.85f, 1.15f);
        // anywhere will do in a panic, lawns and the road included
        if (NavMesh.SamplePosition(transform.position + away.normalized * spawner.panicRadius * 1.5f, out NavMeshHit hit, 10f, NavMesh.AllAreas))
            agent.SetDestination(hit.position);
    }

    // An officer with a suspect: run until there's a clear shot inside range, then stand and fire.
    void Pursue()
    {
        bool wasAiming = aiming;
        aiming = false;
        suspect.TryGetComponent(out Health prey);
        if (Time.time > calmAt || (prey && prey.IsDead))
        {
            suspect = null;
            gun.gameObject.SetActive(false);
            agent.speed = calmSpeed;
            agent.isStopped = false;
            Next();
            return;
        }

        Vector3 eye = transform.position + Vector3.up * 1.4f;
        Vector3 centre = suspect.TryGetComponent(out Collider shape) && shape.enabled ? shape.bounds.center : suspect.transform.position;
        Vector3 to = centre - eye;
        // clear if the first thing in the way is the suspect, or the person the suspect is riding
        bool clear = Physics.Raycast(eye, to, out RaycastHit seen, spawner.policeRange, ~0, QueryTriggerInteraction.Ignore)
            && seen.collider.GetComponentInParent<Health>() is Health first && first
            && (first == prey || (first.TryGetComponent(out Npc person) && person.Ridden));
        agent.isStopped = clear;
        if (!clear)
        {
            if (Time.time < nextAct) return;
            nextAct = Time.time + 0.5f;
            agent.SetDestination(suspect.transform.position);
            return;
        }

        aiming = true;
        // their aim trails a moving target, so keeping moving is how you don't get hit
        aimPoint = wasAiming ? Vector3.MoveTowards(aimPoint, centre, spawner.policeAimSpeed * Time.deltaTime) : centre;
        to = aimPoint - eye;
        Vector3 flat = Vector3.ProjectOnPlane(to, Vector3.up);
        if (flat != Vector3.zero) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(flat), agent.angularSpeed * Time.deltaTime);
        if (Time.time < nextAct) return;
        nextAct = Time.time + spawner.policeFireInterval * Random.Range(0.8f, 1.2f);

        Vector2 stray = Random.insideUnitCircle * spawner.policeSpread;
        Vector3 shot = Quaternion.LookRotation(to) * Quaternion.Euler(stray.x, stray.y, 0) * Vector3.forward;
        Vector3 end = eye + shot * spawner.policeRange * 2;
        // whatever the bullet meets takes it: the suspect, their mount, or a bystander
        if (Physics.Raycast(eye, shot, out RaycastHit hit, spawner.policeRange * 2, ~0, QueryTriggerInteraction.Ignore))
        {
            end = hit.point;
            Health struck = hit.collider.GetComponentInParent<Health>();
            if (struck) struck.TakeDamage(spawner.policeDamage);
        }
        suspect.DrawTracer(gun.position, end);
    }

    // There's no aiming animation: after the Animator has posed the officer, swing the right arm onto the target.
    void LateUpdate()
    {
        if (!aiming || Ridden) return;
        arm.rotation = Quaternion.FromToRotation(hand.position - arm.position, aimPoint - arm.position) * arm.rotation;
        gun.rotation = Quaternion.LookRotation(aimPoint - hand.position);
    }

    // HeadRide: while a pigeon is perched on this person's head it does the thinking, through Steer and Jump. A ridden
    // person is a free-moving body like the player, not a NavMesh agent, so they can go over kerbs, benches and walls.
    public bool Ridden { get; private set; }

    public void SetRidden(bool on)
    {
        if (!enabled) return; // dead
        Ridden = on;
        animator.SetBool(Hangout, false);
        animator.SetBool(Talk, false);
        chatUntil = 0;
        if (!body)
        {
            body = gameObject.AddComponent<CharacterController>();
            body.height = NpcSpawner.Height;
            body.radius = NpcSpawner.Radius;
            body.skinWidth = 0.03f;
            body.center = Vector3.up * (NpcSpawner.Height / 2 + body.skinWidth); // a CharacterController rests its skin width above the ground
            body.stepOffset = 0.4f;
        }
        // ponytail: hopping off somewhere people can't walk (a bench, a roof) drops them to the nearest spot they can
        if (!on && NavMesh.SamplePosition(transform.position, out NavMeshHit ground, 100f, NavMesh.AllAreas)) transform.position = ground.position;
        GetComponent<CapsuleCollider>().enabled = agent.enabled = !on;
        body.enabled = on;
        fallSpeed = 0;
        if (!on) Next();
    }

    // call every frame while ridden, with the walking velocity wanted
    public void Steer(Vector3 velocity)
    {
        if (body.isGrounded && fallSpeed < 0) fallSpeed = -2f;
        fallSpeed += Physics.gravity.y * Time.deltaTime;
        body.Move((velocity + Vector3.up * fallSpeed) * Time.deltaTime);
        if (velocity != Vector3.zero)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(velocity), agent.angularSpeed * Time.deltaTime);
    }

    public void Jump(float height)
    {
        if (body.isGrounded) fallSpeed = Mathf.Sqrt(2 * -Physics.gravity.y * height);
    }

    void Next()
    {
        waitUntil = 0;
        if (kind == Kind.Walker) agent.SetDestination(spawner.RoamPoint());
        else if (kind == Kind.Idler) agent.SetDestination(spawner.HangoutSpot(out lookAt));
        else if (kind == Kind.Guard) agent.SetDestination(post);
        else
        {
            int count = spawner.jogRoute.Length;
            routeIndex = ((routeIndex + routeStep) % count + count) % count;
            // spread across the pavement instead of everyone running the same line
            agent.SetDestination(spawner.jogRoute[routeIndex].position + new Vector3(Random.Range(-1f, 1f), 0, Random.Range(-1f, 1f)));
        }
    }

    // (limb, the bone it runs to, radius in metres); the joint goes to the nearest limb above it in the skeleton
    static readonly (HumanBodyBones bone, HumanBodyBones end, float radius)[] Limbs =
    {
        (HumanBodyBones.Hips, HumanBodyBones.Spine, 0.14f), (HumanBodyBones.Spine, HumanBodyBones.Head, 0.15f), (HumanBodyBones.Head, HumanBodyBones.Head, 0.11f),
        (HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, 0.05f), (HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, 0.045f),
        (HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, 0.05f), (HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, 0.045f),
        (HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, 0.08f), (HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, 0.06f),
        (HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, 0.08f), (HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, 0.06f),
    };

    // Killed: stop thinking and go limp. The ragdoll is only built now, so the living crowd doesn't pay for one each.
    // ponytail: bodies stay where they fall and nobody replaces them; fade them out and respawn if rounds run long
    void Die()
    {
        Vector3 velocity = Ridden ? body.velocity : agent.velocity;
        enabled = agent.enabled = animator.enabled = false;
        foreach (Collider whole in GetComponents<Collider>()) whole.enabled = false;

        var parts = new System.Collections.Generic.List<Collider>();
        foreach ((HumanBodyBones bone, HumanBodyBones end, float radius) in Limbs)
        {
            Transform limb = animator.GetBoneTransform(bone);
            // the capsule lies along the bone, in the bone's own space and scale
            Vector3 along = bone == end ? limb.InverseTransformVector(Vector3.up * 0.12f) : limb.InverseTransformPoint(animator.GetBoneTransform(end).position);
            float scale = limb.lossyScale.x;
            var part = limb.gameObject.AddComponent<CapsuleCollider>();
            part.center = along / 2;
            part.direction = Mathf.Abs(along.x) > Mathf.Abs(along.y) ? (Mathf.Abs(along.x) > Mathf.Abs(along.z) ? 0 : 2) : (Mathf.Abs(along.y) > Mathf.Abs(along.z) ? 1 : 2);
            part.radius = radius / scale;
            part.height = along.magnitude + 2 * part.radius;
            foreach (Collider other in parts) Physics.IgnoreCollision(part, other); // they overlap at the joints
            parts.Add(part);

            var body = limb.gameObject.AddComponent<Rigidbody>();
            body.mass = 6;
            body.linearVelocity = velocity;
            Rigidbody above = limb.parent.GetComponentInParent<Rigidbody>();
            if (above.transform != transform) limb.gameObject.AddComponent<CharacterJoint>().connectedBody = above;
        }
    }
}
