using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// The Tutorial scene (a copy of the Mechanics Gym, Tools > Wash Square > Build Tutorial): walks through every mechanic
// one step at a time on a floating subway sign at the top of the screen (SubwayGUI). Do what it says and the sign bursts
// away and the next one pops in; Tab skips a step. Steps are checked from the player's deeds (Weapon.Did, the same
// deeds Seagull tasks count) or from what the bird is doing. Progress survives a death (the scene reloads).
public class Tutorial : MonoBehaviour
{
    class Step
    {
        public string title, how;
        public int station; // the gym station to go to, 0 for anywhere
        public Func<bool> done;
    }

    static int reached; // the step to pick up at after a death reloads the scene

    // from the start menu: a fresh run, not picking up where the last one left off
    public static void Begin() => reached = 0;

    readonly List<Step> steps = new();
    readonly HashSet<string> deeds = new(); // done since the current step began
    int index;
    float burstAt = -1, look, sprinting, scoping, bathing, onStep;
    Vector3 start;
    Weapon weapon;
    StarterAssets.ThirdPersonController player;
    StarterAssets.StarterAssetsInputs input;
    Transform water;

    const float Width = 600;

    void Start()
    {
        var hud = FindFirstObjectByType<WeaponHUD>();
        if (!hud || !hud.weapon || !hud.player) { Debug.LogError("Tutorial: no WeaponHUD with a weapon and player in the scene", this); enabled = false; return; }
        weapon = hud.weapon;
        player = hud.player;
        input = player.GetComponent<StarterAssets.StarterAssetsInputs>();
        start = player.transform.position;
        foreach (Transform t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
            if (t.name == "Water" && t.parent && t.parent.name == "East Wing") water = t;
        Weapon.Did += OnDid;
        Steps();
        index = Mathf.Min(reached, steps.Count);
    }

    void OnDestroy() => Weapon.Did -= OnDid;

    void OnDid(Weapon who, string deed)
    {
        if (who == weapon) deeds.Add(deed);
    }

    Func<bool> Did(params string[] any) => () => Array.Exists(any, deeds.Contains);

    void Add(string title, string how, int station, Func<bool> done) => steps.Add(new Step { title = title, how = how, station = station, done = done });

    void Steps()
    {
        Add("Who's who", "You're the Pigeon: the detective in the deerstalker. In a match you hunt the Seagull, the one in the red headband (round 2, you swap).", 0, () => onStep > 7);
        Add("The clock", "A round starts at 5:00. The Seagull has to finish its tasks by 0:00, and each one it finishes puts 2:00 back on the clock: more time for it, and more for you to catch it.", 0, () => onStep > 8);
        Add("Look around", "Move the mouse to look around.", 0, () => look > 400);
        Add("Walk", "WASD to walk.", 0, () => Vector3.Distance(player.transform.position, start) > 5);
        Add("Sprint", "Hold Shift while you move to sprint.", 0, () => sprinting > 1.5f);
        Add("Jump", "Space to jump.", 0, () => !player.Grounded);
        Add("Jump chain", "Keep pressing Space in the air: you get " + player.ExtraJumps + " more jumps up there. Landing on anything gives them back.", 1, () => player.ExtraJumpsLeft == 0 && !player.Grounded);
        Add("Umbrella bounce", "Land on top of a cart umbrella: it throws you about 12 m up.", 2, () => player.transform.position.y > 9);
        Add("Shoot", "Left click to shoot.", 0, Did("shoot"));
        Add("Scope", "Hold right click to scope in.", 0, () => scoping > 0.5f);
        Add("Reload", "R reloads. Spare magazines are few: officers drop more.", 0, Did("reload"));
        Add("Headshot", "Shoot someone in the head: the Headshot sign bursts over your crosshair. Station 16's people stand still for it.", 16, Did("headshot", "headshot kill"));
        Add("Kill", "Kill someone and a Kill sign pops up.", 16, Did("kill"));
        Add("Double kill", "Two kills under 3 s apart: Double kill. Three: Triple kill.", 16, Did("double kill"));
        Add("Eat", "Look at a food close up and press E: it goes in your stash. Press X to use the picked food (Z picks the next); its power lasts a while and the HUD counts it down.", 3, Did("eat"));
        Add("Buy", "Carts and trucks keep food on the counter. Look at it and press E to buy it with your cash.", 4, Did("buy"));
        Add("Chess piece", "Eat a chess piece (E): for 10 s the minimap shows exactly where the Seagull is.", 5, Did("eat chess piece"));
        Add("Drops", "People drop a wallet (cash) when they die, officers a magazine. Walk into one to pick it up.", 6, Did("wallet", "magazine"));
        Add("Ride someone", "Land on someone's head to ride them: move, sprint and jump steer them. Q hops off.", 7, Did("ride person"));
        Add("Silent kill", "Out of ammo with no reloads, on someone's head, press E to kill them silently.", 7, Did("assassinate"));
        Add("Egg bomb", "Eat an ice cream to stash an egg bomb, then press F: it blasts you 15 m up with all your air jumps back, kills anyone close and hurts everyone within 6 m.", 3, Did("egg bomb"));
        Add("Snitch flock", "Eat bread to stash a snitch flock, then press G: a flock tails the Seagull and gives it away.", 3, Did("snitch flock"));
        Add("Drive", "Land on a car's roof to drive it: W/S drive and brake, A/D steer, Shift faster, Q hops off.", 10, Did("drive car"));
        Add("Citi Bike", "Land on a bike's saddle to ride it. Space hops, Q gets off.", 11, Did("ride bike"));
        Add("Trash can", "Look at a trash can and press E to rummage: food, cash, a magazine, or just trash.", 12, Did("search trash"));
        Add("Take a bath", "Stand in the fountain for 2 s. \"Take a bath\" tasks are done this way.", 13, () => bathing > 2);
        Add("Wanted", "Fire a shot: you're wanted, and police in earshot come after you. Lose them, or use a soft pretzel so they ignore you.", 14, () => Npc.IsWanted(weapon)); // still wanted from earlier shots counts
        Add("Hijack", "Land on the roof of a car that's driving round to take it over.", 15, Did("hijack"));
        Add("Run someone over", "Drive into someone: roadkill. In the park, that gets you wanted.", 15, Did("run over"));
    }

    void Update()
    {
        if (index >= steps.Count || Time.timeScale == 0) return;
        if (burstAt >= 0)
        {
            if (Time.unscaledTime - burstAt < 0.25f) return; // the sign's burst plays out
            burstAt = -1;
            reached = ++index;
            deeds.Clear();
            look = sprinting = scoping = bathing = onStep = 0;
            start = player.transform.position;
            return;
        }

        onStep += Time.deltaTime;
        if (Mouse.current != null) look += Mouse.current.delta.ReadValue().magnitude;
        if (input && input.sprint && input.move.sqrMagnitude > 0.1f) sprinting += Time.deltaTime;
        if (weapon.scopeAction.IsPressed()) scoping += Time.deltaTime;
        if (water)
        {
            Vector3 d = player.transform.position - water.position;
            bool inside = new Vector2(d.x, d.z).magnitude < water.lossyScale.x / 2 && player.transform.position.y < water.position.y + 1.2f;
            bathing = inside ? bathing + Time.deltaTime : 0;
        }

        bool skip = Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame;
        if (skip || steps[index].done()) burstAt = Time.unscaledTime;
        if (burstAt >= 0 && !skip) Sfx.Play("objective done"); // the sax for a step done (not one skipped)
    }

    void OnGUI()
    {
        if (Time.timeScale == 0 || !weapon) return; // the Escape menu is up
        SubwayGUI.Scale();
        if (index >= steps.Count)
        {
            string done = "You've seen every mechanic in the game. Esc for the menu, or keep playing.";
            SubwayGUI.Board(new Rect((1280 - Width) / 2, 20, Width, 80 + SubwayGUI.BodyHeight(done, Width)), "That's everything", done, "tutorial done");
            return;
        }
        Step step = steps[index];
        string text = step.how + "\n" + (step.station > 0 ? "Station " + step.station + " · " : "") + "Step " + (index + 1) + " of " + steps.Count + " · Tab skips";
        SubwayGUI.Board(new Rect((1280 - Width) / 2, 20, Width, 80 + SubwayGUI.BodyHeight(text, Width)), step.title, text, "tutorial " + index, index + 1, burstAt);
    }
}
