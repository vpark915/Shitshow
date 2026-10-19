using System.Collections.Generic;
using StarterAssets;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Put on the pigeon. Eating a food (FoodItem, through PieceEater) does one of two things (design doc section 10, Food):
// - timed foods give their ability straight away for a set number of seconds; eating another replaces it.
// - stocked foods go in the pigeon's stash to be used later: ice cream (egg bomb key) and bread (flock key) on a
//   button, a bagel by itself on the next hit, a pizza by itself the next time the pigeon is riding someone.
// Dying ends the timed ability; the scene restarts, so the stash goes too.
public class FoodPowers : MonoBehaviour
{
    public enum Ability { None, Sprint, JitteryWings, HotSauceLunge, EggBomb, FullBelly, CrowdFavourite, BagelShield, SnitchFlock, LowProfile }

    [Tooltip("Shows the timed ability with its seconds left, and what's in the stash")]
    public Text timerText;
    [Tooltip("Most of any one stocked food the pigeon can carry")]
    public int maxStock = 5;
    public InputAction eggAction = new InputAction("Egg bomb", binding: "<Keyboard>/f");
    public InputAction flockAction = new InputAction("Snitch flock", binding: "<Keyboard>/g");

    [Header("Sprint (hot dog)")]
    [Tooltip("Ground speed multiplier; no help in the air")]
    public float sprintMultiplier = 1.8f;

    [Header("Hot-sauce lunge (burrito)")]
    [Tooltip("Forward speed each jump adds, in m/s")]
    public float lungeSpeed = 10f;
    [Tooltip("A lunge is this fraction of a normal jump's height: long and low")]
    public float lungeHeight = 0.35f;

    [Header("Egg bomb (ice cream)")]
    [Tooltip("How high the blast throws the pigeon, in metres")]
    public float eggJump = 15f;
    [Tooltip("Everyone else within this many metres takes the damage")]
    public float eggRadius = 6f;
    public float eggDamage = 15f;
    [Tooltip("Shot out the pigeon's rear, straight down, as it shoots up; the blast is where it lands (Prefabs/SpottedEgg: the spottedEgg model with a Rigidbody, collider and EggShell)")]
    public GameObject eggModel;
    [Tooltip("How fast the egg shoots down, in m/s")]
    public float eggThrow = 18f;
    [Tooltip("Hot streaks of the blast fade from this...")]
    public Color blastHot = new Color(1f, 0.95f, 0.6f);
    [Tooltip("...to this")]
    public Color blastCool = new Color(0.9f, 0.3f, 0.05f);
    [Tooltip("Bits of shell and yolk thrown out with it")]
    public Color shellColor = new Color(1f, 0.85f, 0.35f);
    public Material splatMaterial;
    public float splatLife = 10f;

    [Header("Bagel shield")]
    [Tooltip("The pigeon pulses this colour while it has a bagel shield")]
    public Color shieldGlow = new Color(0.1f, 0.4f, 1.5f);
    public float shieldPulse = 3f;

    [Header("Full belly (gyro)")]
    [Range(0, 1)] public float damageScale = 0.5f;
    [Range(0, 1)] public float fullJumpScale = 0.7f;

    [Header("Crowd favourite (pizza)")]
    [Tooltip("Speed multiplier for the person being ridden")]
    public float rideSpeedMultiplier = 1.6f;

    public Ability Current { get; private set; }

    ThirdPersonController controller;
    HeadRide ride;
    Health health;
    Weapon weapon;
    BirdFlocks birds;
    Minimap map;
    Glow glow;
    float until, rideLeft, moveSpeed, sprintSpeed, jumpHeight, rideWalk, rideSprint;
    int extraJumps;
    string label;
    readonly Dictionary<Ability, int> stock = new Dictionary<Ability, int>();
    readonly Dictionary<Ability, float> stockSeconds = new Dictionary<Ability, float>(); // pizza: ride time, bread: tail time

    void Start()
    {
        controller = GetComponent<ThirdPersonController>();
        TryGetComponent(out ride);
        TryGetComponent(out health);
        TryGetComponent(out weapon);
        birds = FindFirstObjectByType<BirdFlocks>();
        map = FindFirstObjectByType<Minimap>();
        glow = Glow.On(this);
        moveSpeed = controller.MoveSpeed;
        sprintSpeed = controller.SprintSpeed;
        jumpHeight = controller.JumpHeight;
        extraJumps = controller.ExtraJumps;
        if (ride) { rideWalk = ride.walkSpeed; rideSprint = ride.sprintSpeed; }
        controller.Jumped += OnJumped;
        if (health) health.Died += End;
        if (timerText) timerText.enabled = false;
    }

    void OnEnable() { eggAction.Enable(); flockAction.Enable(); }
    void OnDisable() { eggAction.Disable(); flockAction.Disable(); }

    public static string Name(Ability a) => a switch
    {
        Ability.Sprint => "Sprint",
        Ability.JitteryWings => "Jittery wings",
        Ability.HotSauceLunge => "Hot-sauce lunge",
        Ability.EggBomb => "Egg bomb",
        Ability.FullBelly => "Full belly",
        Ability.CrowdFavourite => "Crowd favourite",
        Ability.BagelShield => "Bagel shield",
        Ability.SnitchFlock => "Snitch flock",
        Ability.LowProfile => "Low profile",
        _ => "",
    };

    public static bool Stocked(Ability a) => a is Ability.EggBomb or Ability.CrowdFavourite or Ability.BagelShield or Ability.SnitchFlock;

    // what the eat prompt says about a food
    public static string Describe(FoodItem food) =>
        Name(food.ability) + (Stocked(food.ability) ? ", keep for later" : ", " + food.seconds + "s");

    public void Give(Ability ability, float seconds, string food)
    {
        if (Stocked(ability))
        {
            if (ability == Ability.BagelShield) { if (health) health.shields = Mathf.Min(health.shields + 1, maxStock); }
            else { stock[ability] = Mathf.Min(Count(ability) + 1, maxStock); stockSeconds[ability] = seconds; }
            return;
        }
        End();
        Current = ability;
        until = Time.time + seconds;
        label = food;
        switch (ability)
        {
            case Ability.JitteryWings: controller.ExtraJumps = extraJumps + 1; break;
            case Ability.FullBelly:
                if (health) health.damageScale = damageScale;
                controller.JumpHeight = jumpHeight * fullJumpScale;
                break;
            case Ability.LowProfile: if (weapon) weapon.unseen = true; break;
        }
    }

    public int Count(Ability a) => a == Ability.BagelShield ? (health ? health.shields : 0) : stock.TryGetValue(a, out int n) ? n : 0;

    bool Take(Ability a)
    {
        if (Count(a) == 0) return false;
        stock[a]--;
        return true;
    }

    // a fresh life (online respawn): no ability running and nothing in the stash
    public void Clear()
    {
        End();
        stock.Clear();
        rideLeft = 0;
    }

    // timed ability back to normal
    void End()
    {
        Current = Ability.None;
        controller.MoveSpeed = moveSpeed;
        controller.SprintSpeed = sprintSpeed;
        controller.JumpHeight = jumpHeight;
        controller.ExtraJumps = extraJumps;
        if (health) health.damageScale = 1f;
        if (weapon) weapon.unseen = false;
    }

    void Update()
    {
        if (Current != Ability.None && Time.time >= until) End();

        if (Current == Ability.Sprint)
        {
            float m = controller.Grounded ? sprintMultiplier : 1f;
            controller.MoveSpeed = moveSpeed * m;
            controller.SprintSpeed = sprintSpeed * m;
        }

        bool dead = health && health.IsDead;
        glow.steady = Count(Ability.BagelShield) > 0 && !dead ? shieldGlow * (0.35f + 0.65f * (0.5f + 0.5f * Mathf.Sin(Time.time * shieldPulse * Mathf.PI))) : Color.black;
        if (!dead && eggAction.WasPressedThisFrame() && Take(Ability.EggBomb)) EggBomb();
        if (!dead && flockAction.WasPressedThisFrame() && map && map.target && Count(Ability.SnitchFlock) > 0)
        {
            Take(Ability.SnitchFlock);
            float s = stockSeconds[Ability.SnitchFlock];
            if (birds) birds.Follow(map.target, s);
            map.Reveal(s);
        }

        // pizza: the clock only runs while riding someone, and the next slice starts when one runs out
        if (ride)
        {
            bool riding = ride.Mount;
            if (riding && rideLeft <= 0 && Take(Ability.CrowdFavourite)) rideLeft = stockSeconds[Ability.CrowdFavourite];
            if (riding) rideLeft -= Time.deltaTime;
            float m = rideLeft > 0 ? rideSpeedMultiplier : 1f;
            ride.walkSpeed = rideWalk * m;
            ride.sprintSpeed = rideSprint * m;
        }

        if (timerText) Show();
    }

    void Show()
    {
        var lines = new List<string>();
        if (Current != Ability.None) lines.Add(label + ": " + Name(Current) + " " + Mathf.CeilToInt(until - Time.time) + "s");
        if (rideLeft > 0) lines.Add("Pizza: " + Name(Ability.CrowdFavourite) + " " + Mathf.CeilToInt(rideLeft) + "s" + (ride.Mount ? "" : " (paused till you ride)"));
        var stash = new List<string>();
        if (Count(Ability.EggBomb) > 0) stash.Add("Egg bomb x" + Count(Ability.EggBomb) + " [" + eggAction.GetBindingDisplayString() + "]");
        if (Count(Ability.SnitchFlock) > 0) stash.Add("Snitch flock x" + Count(Ability.SnitchFlock) + " [" + flockAction.GetBindingDisplayString() + "]");
        if (Count(Ability.BagelShield) > 0) stash.Add("Bagel shield x" + Count(Ability.BagelShield));
        if (Count(Ability.CrowdFavourite) > 0) stash.Add("Pizza x" + Count(Ability.CrowdFavourite) + " (on mount)");
        if (stash.Count > 0) lines.Add(string.Join("   ", stash));
        timerText.enabled = lines.Count > 0;
        timerText.text = string.Join("\n", lines);
    }

    void OnJumped()
    {
        if (Current != Ability.HotSauceLunge) return;
        controller.Launch(controller.JumpHeight * lungeHeight);
        controller.Push = transform.forward * lungeSpeed; // the pigeon always faces where the camera looks
    }

    // Angry Birds style: the egg shoots out the pigeon's rear straight down as the pigeon rockets sky high; it bursts on
    // whatever it hits, hitting everyone else close by
    void EggBomb()
    {
        Npc mount = ride ? ride.Mount : null;
        if (mount) ride.Dismount();
        controller.Launch(eggJump);
        if (!eggModel) { Burst(transform.position, mount); return; }
        GameObject egg = Instantiate(eggModel, transform.position + Vector3.up * 0.2f - transform.forward * 0.15f, Random.rotation);
        foreach (Collider shell in egg.GetComponentsInChildren<Collider>())
            foreach (Collider mine in GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(shell, mine); // never bursts on the pigeon
        var body = egg.GetComponent<Rigidbody>();
        body.linearVelocity = Vector3.down * eggThrow;
        body.angularVelocity = Random.insideUnitSphere * 8f;
        egg.GetComponent<EggShell>().Burst = at => Burst(at, mount);
    }

    void Burst(Vector3 at, Npc spare)
    {
        Blast(at);
        var hit = new HashSet<Health>();
        foreach (Collider c in Physics.OverlapSphere(at, eggRadius, ~0, QueryTriggerInteraction.Ignore))
            if (c.GetComponentInParent<Health>() is Health h && h && h != health && (!spare || h.gameObject != spare.gameObject) && hit.Add(h))
                h.TakeDamage(eggDamage);
        if (Physics.Raycast(at + Vector3.up * 0.3f, Vector3.down, out RaycastHit floor, 3f, ~(1 << gameObject.layer), QueryTriggerInteraction.Ignore))
            Splat(floor.point);
    }

    // the same quick streaks as blood (Weapon.Spray): a dome of hot sparks, and slower bits of shell and yolk that fall
    void Blast(Vector3 at)
    {
        Quaternion up = Quaternion.LookRotation(Vector3.up);
        ParticleSystem fire = Weapon.Spray("Egg Blast", at, up, blastHot, new Vector2Int(50, 70), new Vector2(eggRadius * 1.5f, eggRadius * 3f), new Vector2(0.12f, 0.3f), new Vector2(0.06f, 0.16f), 0.5f, 90f);
        var fade = fire.colorOverLifetime;
        fade.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(blastCool, 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.6f, 1f) });
        fade.color = g; // tints the hot start colour on its way out
        Weapon.Spray("Egg Shell", at, up, shellColor, new Vector2Int(14, 22), new Vector2(4f, 9f), new Vector2(0.3f, 0.6f), new Vector2(0.04f, 0.09f), 3f, 60f);
    }

    void Splat(Vector3 at)
    {
        GameObject look = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(look.GetComponent<Collider>());
        look.name = "Egg Splat";
        look.transform.SetPositionAndRotation(at + Vector3.up * 0.004f, Quaternion.identity);
        look.transform.localScale = new Vector3(eggRadius * 2, 0.002f, eggRadius * 2);
        if (splatMaterial) look.GetComponent<Renderer>().sharedMaterial = splatMaterial;
        Destroy(look, splatLife);
    }
}
