using System.Collections.Generic;
using StarterAssets;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Put on the pigeon. Eating a food (FoodItem, through PieceEater) puts it in the pigeon's stash, to use when you choose
// (design doc section 10, Food):
// - timed foods (hot dog, coffee, burrito, gyro, pretzel) and pizza: the use key uses the picked one, the next-food key
//   picks another. A timed food gives its ability for a set number of seconds; using another replaces it. Pizza only
//   goes while riding someone, and its clock only runs then.
// - ice cream (egg bomb key) and bread (flock key) have keys of their own; a bagel works by itself on the next hit.
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
    [Tooltip("Uses the picked food from the stash")]
    public InputAction useAction = new InputAction("Use food", binding: "<Keyboard>/x");
    [Tooltip("Picks the next food in the stash")]
    public InputAction nextAction = new InputAction("Next food", binding: "<Keyboard>/z");

    [Header("Sprint (hot dog)")]
    [Tooltip("Ground speed multiplier; no help in the air")]
    public float sprintMultiplier = 1.8f;

    [Header("Hot-sauce lunge (burrito)")]
    [Tooltip("Forward speed each jump adds, in m/s")]
    public float lungeSpeed = 10f;
    [Tooltip("A lunge is this fraction of a normal jump's height: long and low")]
    public float lungeHeight = 0.35f;

    [Header("Egg bomb (ice cream)")]
    [Tooltip("How high the blast throws the pigeon, in metres (and it gets all its air jumps back)")]
    public float eggJump = 15f;
    [Tooltip("Everyone else within this many metres takes the damage")]
    public float eggRadius = 6f;
    [Tooltip("Damage at the edge of the blast; it climbs to three times this toward the middle")]
    public float eggDamage = 15f;
    [Tooltip("People and birds this close to the burst are killed outright (a player takes the full three times Egg Damage instead)")]
    public float eggKillRadius = 3f;
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
    [Tooltip("Only if Resources/EggSplat (the fried egg left on the ground) is missing: a flat disc in this")]
    public Material splatMaterial;
    public float splatLife = 10f;

    [Header("Bagel shield")]
    [Tooltip("The pigeon pulses this colour while it has a bagel shield")]
    public Color shieldGlow = new Color(0.1f, 0.4f, 1.5f);
    public float shieldPulse = 3f;

    [Header("Full belly (gyro)")]
    [Tooltip("The pigeon shrinks to this size while full (0.5: half size, a smaller target)")]
    [Range(0.1f, 1)] public float fullSize = 0.5f;
    [Tooltip("Seconds to shrink, and to grow back")]
    public float resizeTime = 0.25f;

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
    // walking and running speed on top of the rig's own (HuntMatch makes the Pigeon faster)
    [System.NonSerialized] public float speedScale = 1f;
    Vector3 baseScale;
    int extraJumps;
    string label;
    Ability picked;
    readonly Dictionary<Ability, int> stock = new Dictionary<Ability, int>();
    readonly Dictionary<Ability, float> stockSeconds = new Dictionary<Ability, float>(); // how long it lasts (pizza: ride time, bread: tail time)
    readonly Dictionary<Ability, string> stockLabel = new Dictionary<Ability, string>();
    // what the use key goes through, in this order
    static readonly Ability[] Usable = { Ability.Sprint, Ability.JitteryWings, Ability.HotSauceLunge, Ability.FullBelly, Ability.LowProfile, Ability.CrowdFavourite };

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
        baseScale = transform.localScale;
        if (ride) { rideWalk = ride.walkSpeed; rideSprint = ride.sprintSpeed; }
        controller.Jumped += OnJumped;
        if (health) health.Died += End;
        if (timerText) timerText.enabled = false;
        SubwayGUI.Back(timerText); // on a subway sign, like the HUD
    }

    void OnEnable() { eggAction.Enable(); flockAction.Enable(); useAction.Enable(); nextAction.Enable(); }
    void OnDisable() { eggAction.Disable(); flockAction.Disable(); useAction.Disable(); nextAction.Disable(); }

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

    // abilities that run for a while once used
    public static bool Timed(Ability a) => a is Ability.Sprint or Ability.JitteryWings or Ability.HotSauceLunge or Ability.FullBelly or Ability.LowProfile;

    // what the eat prompt says about a food
    public static string Describe(FoodItem food) =>
        Name(food.ability) + (Timed(food.ability) || food.ability == Ability.CrowdFavourite ? " " + food.seconds + "s" : "") + ", keep for later";

    // eaten: into the stash
    public void Give(Ability ability, float seconds, string food)
    {
        if (ability == Ability.BagelShield) { if (health) health.shields = Mathf.Min(health.shields + 1, maxStock); return; }
        if (ability == Ability.None) return;
        stock[ability] = Mathf.Min(Count(ability) + 1, maxStock);
        stockSeconds[ability] = seconds;
        stockLabel[ability] = food;
        if (Count(picked) == 0 && System.Array.IndexOf(Usable, ability) >= 0) picked = ability;
    }

    // the use key: the picked food out of the stash (pizza only while riding)
    void Use(Ability ability)
    {
        if (ability == Ability.CrowdFavourite)
        {
            if (!ride || !ride.Mount || !Take(ability)) return;
            rideLeft += stockSeconds[ability];
        }
        else
        {
            if (!Take(ability)) return;
            End();
            Current = ability;
            until = Time.time + stockSeconds[ability];
            label = stockLabel[ability];
            switch (ability)
            {
                case Ability.JitteryWings: controller.ExtraJumps = extraJumps + 1; break;
                case Ability.LowProfile: if (weapon) weapon.unseen = true; break;
            }
        }
        if (Count(picked) == 0) Next();
    }

    // pick the next food in the stash the use key can use (none left: stays put)
    void Next()
    {
        int at = System.Array.IndexOf(Usable, picked);
        for (int i = 1; i <= Usable.Length; i++)
        {
            Ability a = Usable[(at + i + Usable.Length) % Usable.Length];
            if (Count(a) > 0) { picked = a; return; }
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
        controller.MoveSpeed = moveSpeed * speedScale;
        controller.SprintSpeed = sprintSpeed * speedScale;
        controller.JumpHeight = jumpHeight;
        controller.ExtraJumps = extraJumps;
        if (weapon) weapon.unseen = false;
    }

    void Update()
    {
        if (Current != Ability.None && Time.time >= until) End();

        // full belly: the whole bird (body, collider, gun) shrinks while it lasts, and grows back after. About its feet,
        // so it stays on the ground
        Vector3 size = Current == Ability.FullBelly ? baseScale * fullSize : baseScale;
        if (transform.localScale != size)
            transform.localScale = Vector3.MoveTowards(transform.localScale, size, (1 - fullSize) * baseScale.magnitude / resizeTime * Time.deltaTime);

        {
            float m = (Current == Ability.Sprint && controller.Grounded ? sprintMultiplier : 1f) * speedScale;
            controller.MoveSpeed = moveSpeed * m;
            controller.SprintSpeed = sprintSpeed * m;
        }

        bool dead = health && health.IsDead;
        glow.steady = Count(Ability.BagelShield) > 0 && !dead ? shieldGlow * (0.35f + 0.65f * (0.5f + 0.5f * Mathf.Sin(Time.time * shieldPulse * Mathf.PI))) : Color.black;
        if (!dead && eggAction.WasPressedThisFrame() && Take(Ability.EggBomb)) { EggBomb(); Weapon.Done(weapon, "egg bomb"); }
        if (!dead && flockAction.WasPressedThisFrame() && map && map.target && Count(Ability.SnitchFlock) > 0)
        {
            Take(Ability.SnitchFlock);
            Weapon.Done(weapon, "snitch flock");
            float s = stockSeconds[Ability.SnitchFlock];
            if (birds) birds.Follow(map.target, s);
            map.Reveal(s);
        }

        if (!dead && nextAction.WasPressedThisFrame()) Next();
        if (!dead && useAction.WasPressedThisFrame()) Use(picked);

        // pizza: the clock only runs while riding someone
        if (ride)
        {
            bool riding = ride.Mount;
            if (riding && rideLeft > 0) rideLeft -= Time.deltaTime;
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
        if (stash.Count > 0) lines.Add(string.Join("   ", stash));
        // the use key's foods, the picked one marked
        var foods = new List<string>();
        foreach (Ability a in Usable)
            if (Count(a) > 0)
            {
                string food = (stockLabel.TryGetValue(a, out string l) ? l : Name(a)) + " x" + Count(a);
                foods.Add(a == picked ? "> " + food + " [" + useAction.GetBindingDisplayString() + "]" : food);
            }
        if (foods.Count > 0)
            lines.Add(string.Join("   ", foods) + (foods.Count > 1 ? "   [" + nextAction.GetBindingDisplayString() + "] next" : "")
                      + (picked == Ability.CrowdFavourite && !(ride && ride.Mount) ? "  (pizza: ride someone first)" : ""));
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
        controller.RefillJumps(); // a fresh set of air jumps at the top of the blast
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
        foreach (Collider c in Physics.OverlapSphere(at, eggRadius, ~0, QueryTriggerInteraction.Collide)) // triggers too: birds in flight
            if (c.GetComponentInParent<Health>() is Health h && h && h != health && (!spare || h.gameObject != spare.gameObject) && hit.Add(h))
            {
                bool alive = !h.IsDead;
                if (alive) Weapon.Done(weapon, "egg hit", "egg hit " + Weapon.Kind(h));
                h.TakeDamage(EggDamageAt(Vector3.Distance(at, c.ClosestPoint(at)), h), at);
                Weapon.Killed(weapon, h, alive, "egg kill");
            }
        if (Physics.Raycast(at + Vector3.up * 0.3f, Vector3.down, out RaycastHit floor, 3f, ~(1 << gameObject.layer), QueryTriggerInteraction.Ignore))
            Splat(floor.point, floor.normal);
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

    // close in, a kill; further out it falls off to eggDamage at the edge. The other player is never one-shot: a
    // point-blank egg on a bird would end a round in one button
    float EggDamageAt(float distance, Health h)
    {
        bool player = h.GetComponent<Weapon>() || h.GetComponentInParent<NetBird>();
        if (distance <= eggKillRadius && !player) return h.maxHealth * 10; // a bagel still saves you
        float t = Mathf.InverseLerp(eggKillRadius, eggRadius, distance);
        return Mathf.Lerp(eggDamage * 3, eggDamage, t);
    }

    static GameObject splatModel;

    // a fried egg where it burst (Resources/EggSplat, from Blender/Effects/eggSplat; EggSplat drapes it over the
    // ground, pops it in and shrinks it away), lying on the ground's slope, turned at random, as wide as the blast
    void Splat(Vector3 at, Vector3 normal)
    {
        if (!splatModel) splatModel = Resources.Load<GameObject>("EggSplat");
        if (splatModel && splatModel.GetComponent<EggSplat>())
        {
            GameObject fried = Instantiate(splatModel, at + normal * 0.01f, Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0, Random.Range(0f, 360f), 0));
            fried.name = "Egg Splat";
            fried.GetComponent<EggSplat>().Splat(eggRadius * Random.Range(0.9f, 1.15f), splatLife, gameObject.layer);
            return;
        }
        GameObject look = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(look.GetComponent<Collider>());
        look.name = "Egg Splat";
        look.transform.SetPositionAndRotation(at + Vector3.up * 0.004f, Quaternion.identity);
        look.transform.localScale = new Vector3(eggRadius * 2, 0.002f, eggRadius * 2);
        if (splatMaterial) look.GetComponent<Renderer>().sharedMaterial = splatMaterial;
        Destroy(look, splatLife);
    }
}
