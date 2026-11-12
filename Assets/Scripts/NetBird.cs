using StarterAssets;
using Unity.Netcode;
using UnityEngine;

// Online stand-in for a player's bird, spawned by Netcode for each player (the NetworkManager's player prefab). Every
// game still has its own single-player rig (the scene's pigeon) that its player drives; the owner copies its rig's
// position, speed and death onto this, and NetworkTransform carries it to the other game, where it shows as a pigeon
// or seagull for its role. Shots that hit it there are sent to the owner's rig. The one-off animations (shooting,
// eating, a silent kill) are sent to every game, so both players see them on the shown bird.
// ponytail: only the bird is shared; each game has its own NPCs, food and police. A bird riding a person shares which
// look that person has, and the other game carries a stand-in of them under the bird (walking at the bird's speed);
// shots that hit the stand-in go to the real person in the rider's game. The real person in the other game stays
// wherever its own crowd has them. A bird driving shares which vehicle (RoundReset numbers them the same in both
// games), and the other game carries its own copy of that car or bike under the bird.
public class NetBird : NetworkBehaviour
{
    public static NetBird Local, Remote;

    public GameObject pigeonModel, seagullModel;
    [Tooltip("Ground speed, m/s, above which it walks and runs. Keep runSpeed between the rig's walk and sprint speeds " +
             "(2 and 4): on either one the speed jitters across it and the clip keeps restarting")]
    public float walkSpeed = 0.3f, runSpeed = 3f;

    readonly NetworkVariable<float> speed = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    readonly NetworkVariable<bool> airborne = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    readonly NetworkVariable<bool> dead = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    // the look (NpcSpawner.Model) of the person it's riding, -1 when it isn't
    readonly NetworkVariable<int> mount = new(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    // the number (RoundReset.Number) of the vehicle it's driving, -1 when it isn't
    readonly NetworkVariable<int> vehicle = new(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    Transform rig;
    CharacterController rigBody;
    ThirdPersonController rigMover;
    Health rigHealth;
    Weapon rigWeapon;
    HeadRide rigRide;
    NpcSpawner spawner;
    GameObject standIn; // the ridden person, in the other game
    int standInLook = -1;
    Animator standInLooks;
    Transform standInHead;
    Vector3 lastAt;
    float standInSpeed;
    Vehicle carried; // the vehicle it's driving, in the other game
    string playing;
    GameObject showing;
    float actingUntil; // a one-off animation is playing: hold off the walk/fly/idle ones

    // whether this bird is the seagull this round (before the first round everyone is a pigeon)
    public bool IsSeagull => HuntMatch.Instance && HuntMatch.Instance.Started && HuntMatch.Instance.Hunter != OwnerClientId;

    public override void OnNetworkSpawn()
    {
        if (IsOwner) Local = this; else Remote = this;
        FoodPowers powers = FindFirstObjectByType<FoodPowers>();
        rig = powers.transform;
        rigBody = rig.GetComponent<CharacterController>();
        rigMover = rig.GetComponent<ThirdPersonController>();
        rigHealth = rig.GetComponent<Health>();
        rigWeapon = rig.GetComponent<Weapon>();
        rigRide = rig.GetComponent<HeadRide>();
        spawner = FindFirstObjectByType<NpcSpawner>();
        lastAt = transform.position;
        if (IsOwner) Weapon.Did += Acted;
        // feet on the ground: it stands where the pigeon rig stands, so its models drop as far as the rig's did
        pigeonModel.transform.localPosition -= Vector3.up * BirdFeet.RigDrop;
        seagullModel.transform.localPosition -= Vector3.up * BirdFeet.RigDrop;
        BirdOutfit.Dress(pigeonModel, BirdOutfit.Look.DetectiveHat);
        BirdOutfit.Dress(seagullModel, BirdOutfit.Look.Headband);
        HeadRide.NeverCull(pigeonModel); // the other player never vanishes at a distance either
        HeadRide.NeverCull(seagullModel);
        foreach (GameObject model in new[] { pigeonModel, seagullModel })
        {
            foreach (Collider part in model.GetComponentsInChildren<Collider>(true)) part.enabled = false; // the root capsule is the hitbox
            model.SetActive(false); // Update shows the right one
        }
        GetComponent<Collider>().enabled = !IsOwner; // never in the way of its own player
        if (!IsOwner) GetComponent<Health>().forward = HitRpc;
        if (HuntMatch.Instance) HuntMatch.Instance.Refresh();
    }

    public override void OnNetworkDespawn()
    {
        if (standIn) Destroy(standIn);
        if (carried) Hold(carried, false);
        Weapon.Did -= Acted;
        if (Local == this) Local = null;
        if (Remote == this) Remote = null;
        if (HuntMatch.Instance) HuntMatch.Instance.Refresh();
    }

    [Rpc(SendTo.Owner)]
    void HitRpc(float amount, Vector3 from) => rigHealth.TakeDamage(amount, from);

    // the owner's bird did something with an animation of its own
    void Acted(Weapon who, string deed)
    {
        if (who != rigWeapon) return;
        string state = deed == "shoot" && who.shootAnimation ? who.shootAnimation.name
            : deed == "eat" || deed == "eat chess piece" ? "Eat" : deed == "assassinate" ? "Attack" : null;
        if (state != null) ActRpc(state);
        string sound = deed == "shoot" ? "shot" : deed == "reload" ? "reload" : deed == "assassinate" ? "neck snap" : null;
        if (sound != null) SoundRpc(sound, who.transform.position);
    }

    // the other player hears what this bird does (the owner's own game plays it through Weapon and HeadRide)
    [Rpc(SendTo.NotOwner)]
    void SoundRpc(string sound, Vector3 at) => Sfx.Play(sound, at);

    [Rpc(SendTo.Everyone)]
    void ActRpc(string state)
    {
        Animator look = showing ? showing.GetComponentInChildren<Animator>() : null;
        if (!look || !look.HasState(0, Animator.StringToHash(state))) return;
        look.CrossFadeInFixedTime(state, 0.05f, 0, 0f);
        playing = state;
        actingUntil = Time.time + 0.5f;
    }

    // the other bird's ridden person: a copy of their look, put on (or taken off) as the ride starts and ends
    void StandIn()
    {
        int look = mount.Value;
        if (look != standInLook)
        {
            standInLook = look;
            if (standIn) Destroy(standIn);
            standIn = null;
            GameObject model = spawner ? spawner.Model(look) : null;
            if (model)
            {
                standIn = Instantiate(model, transform.position, Quaternion.Euler(0, transform.eulerAngles.y, 0));
                standIn.name = "Ridden (stand-in)";
                foreach (Collider part in standIn.GetComponentsInChildren<Collider>(true)) part.enabled = false;
                foreach (Rigidbody part in standIn.GetComponentsInChildren<Rigidbody>(true)) part.isKinematic = true;
                // shootable like a person, with the hits sent to the real one in the rider's game
                var body = standIn.AddComponent<CapsuleCollider>();
                body.center = Vector3.up * NpcSpawner.Height / 2;
                body.height = NpcSpawner.Height;
                body.radius = NpcSpawner.Radius;
                standIn.AddComponent<Health>().forward = MountHitRpc;
                standInLooks = standIn.GetComponent<Animator>();
                standInLooks.runtimeAnimatorController = spawner.controller;
                standInLooks.applyRootMotion = false;
                standInLooks.fireEvents = false;
                standInHead = standInLooks.GetBoneTransform(HumanBodyBones.Head);
            }
            lastAt = transform.position;
        }
        // its pace and heading from how the bird on its head moves
        Vector3 moved = transform.position - lastAt;
        lastAt = transform.position;
        if (!standIn || Time.deltaTime <= 0) return;
        Vector3 flat = Vector3.ProjectOnPlane(moved, Vector3.up);
        standInSpeed = Mathf.Lerp(standInSpeed, flat.magnitude / Time.deltaTime, 1 - Mathf.Exp(-10 * Time.deltaTime));
        standInLooks.SetFloat("Speed", standInSpeed, 0.15f, Time.deltaTime);
        standInLooks.SetFloat("Cadence", Mathf.Clamp(standInSpeed / 1.4f, 0.5f, 1f)); // Npc's walk pace
        if (flat.magnitude > 0.005f)
            standIn.transform.rotation = Quaternion.RotateTowards(standIn.transform.rotation, Quaternion.LookRotation(flat), 540 * Time.deltaTime);
    }

    [Rpc(SendTo.Owner)]
    void MountHitRpc(float amount, Vector3 from)
    {
        if (rigRide && rigRide.Mount && rigRide.Mount.TryGetComponent(out Health person)) person.TakeDamage(amount, from);
    }

    // the other bird's vehicle in this game: its own driving (and traffic route) off while it's carried, back on after
    static void Hold(Vehicle ride, bool held)
    {
        if (!ride) return;
        ride.enabled = !held;
        if (ride.TryGetComponent(out TrafficCar traffic)) { traffic.enabled = !held; if (!held) traffic.Restart(); }
        if (ride.TryGetComponent(out CharacterController walker)) walker.enabled = !held;
        if (!held) Physics.SyncTransforms();
    }

    // after the Animator has posed the stand-in: slide it so its head is right under the bird's feet; and the carried
    // vehicle turned the bird's way with its perch under the bird
    void LateUpdate()
    {
        if (carried)
        {
            carried.transform.rotation = Quaternion.Euler(0, transform.eulerAngles.y, 0);
            carried.transform.position += transform.position - carried.perch.position;
        }
        if (!standIn) return;
        Vector3 head = standInHead ? standInHead.position : standIn.transform.position + Vector3.up * NpcSpawner.Height;
        Vector3 under = transform.position - Vector3.up * (rigRide ? rigRide.perchHeight : 0.12f);
        standIn.transform.position += under - head;
    }

    void Update()
    {
        if (!IsSpawned) return;
        if (IsOwner && rig)
        {
            transform.SetPositionAndRotation(rig.position, rig.rotation);
            transform.localScale = rig.localScale; // a gyro shrinks the bird (FoodPowers)
            Vector3 v = rigBody.velocity;
            speed.Value = new Vector2(v.x, v.z).magnitude;
            airborne.Value = rigMover && !rigMover.Grounded;
            dead.Value = rigHealth.IsDead;
            mount.Value = rigRide && rigRide.Mount ? rigRide.Mount.model : -1;
            vehicle.Value = rigRide && rigRide.Driving ? RoundReset.Number(rigRide.Driving) : -1;
        }
        else
        {
            StandIn();
            Vehicle now = RoundReset.Numbered(vehicle.Value);
            if (now != carried)
            {
                if (carried) Hold(carried, false);
                carried = now;
                if (carried) Hold(carried, true);
            }
        }

        // your own bird is shown by your rig, except as the seagull: the rig is a pigeon, so then it's shown here
        bool seagull = IsSeagull;
        bool shown = !IsOwner || seagull;
        GameObject model = shown ? seagull ? seagullModel : pigeonModel : null;
        if (model != showing)
        {
            pigeonModel.SetActive(model == pigeonModel);
            seagullModel.SetActive(model == seagullModel);
            showing = model;
            playing = null; // a model switched on starts in its controller's default state
            if (TryGetComponent(out Outline outline)) Destroy(outline); // made for the old model; the minimap adds it again
        }
        if (!model) return;

        string state = dead.Value ? "Death" : airborne.Value ? "Fly" : speed.Value > runSpeed ? "Run" : speed.Value > walkSpeed ? "Walk" : "Idle_A";
        Animator look = model.GetComponentInChildren<Animator>();
        if (look && state != playing && (Time.time >= actingUntil || state == "Death"))
        {
            look.CrossFadeInFixedTime(state, 0.15f);
            playing = state;
        }
    }
}
