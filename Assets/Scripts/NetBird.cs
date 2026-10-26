using StarterAssets;
using Unity.Netcode;
using UnityEngine;

// Online stand-in for a player's bird, spawned by Netcode for each player (the NetworkManager's player prefab). Every
// game still has its own single-player rig (the scene's pigeon) that its player drives; the owner copies its rig's
// position, speed and death onto this, and NetworkTransform carries it to the other game, where it shows as a pigeon
// or seagull for its role. Shots that hit it there are sent to the owner's rig. The one-off animations (shooting,
// eating, a silent kill) are sent to every game, so both players see them on the shown bird.
// ponytail: only the bird is shared; each game has its own NPCs, food and police, so a bird riding a person shows on
// the other screen as a bird gliding along on its own. Share NPCs when that matters.
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

    Transform rig;
    CharacterController rigBody;
    ThirdPersonController rigMover;
    Health rigHealth;
    Weapon rigWeapon;
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
        if (IsOwner) Weapon.Did += Acted;
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
    }

    [Rpc(SendTo.Everyone)]
    void ActRpc(string state)
    {
        Animator look = showing ? showing.GetComponentInChildren<Animator>() : null;
        if (!look || !look.HasState(0, Animator.StringToHash(state))) return;
        look.CrossFadeInFixedTime(state, 0.05f, 0, 0f);
        playing = state;
        actingUntil = Time.time + 0.5f;
    }

    void Update()
    {
        if (!IsSpawned) return;
        if (IsOwner && rig)
        {
            transform.SetPositionAndRotation(rig.position, rig.rotation);
            Vector3 v = rigBody.velocity;
            speed.Value = new Vector2(v.x, v.z).magnitude;
            airborne.Value = rigMover && !rigMover.Grounded;
            dead.Value = rigHealth.IsDead;
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
