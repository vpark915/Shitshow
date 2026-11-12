using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

// The online hunter vs hunted match (design doc sections 3, 4, 13), connected through Unity Relay so neither player
// needs an open port. One player hosts and gets a join code; the other types it in. Then two rounds: the host is the
// Pigeon (hunter) first, then roles swap.
// - The Pigeon wins a round by killing the Seagull, or by the clock running out with Seagull tasks unfinished.
// - The Seagull wins by finishing every task and still being alive at 0:00. A Seagull that hasn't finished them by
//   then loses the whole match outright, whatever happens in the other round (both do: it's a draw). Each task it
//   finishes puts Task Bonus (2:00) back on the clock.
// - A dead Pigeon comes back at its spawn after a few seconds with its status (food) gone; the clock keeps running.
// - Match: otherwise the faster kill as Pigeon wins.
// The server keeps the clock and the score; each game watches its own bird for deaths and tasks and reports them.
// Each round's Seagull tasks are drawn from HuntTasks (plus this scene's landmark Tasks) with a seed the server picks,
// so both games draw the same ones; HuntTasks says how each kind of task is done. A long task (Chain) is several of
// them in order: only its current step counts, and it's ticked off when the last step is done.
// The map is the scene: the host's is told to the joining game, which loads it and joins again if it's on another one.
// After round 2 a scoreboard shows both rounds; a rematch starts once both players accept it.
// The starting menu also opens the Mechanics Gym, offline practice.
public class HuntMatch : NetworkBehaviour
{
    public static HuntMatch Instance { get; private set; }
    // a round has just begun in this game (trash cans restock)
    public static event Action RoundBegan;

    public enum Phase : byte { Waiting, Playing, Between, Over }

    // how a task is done; HuntTasks describes each and the fields it reads
    public enum Goal { Stay, StandOn, Deed, RidePerson, DriveCar, RideBike, Airborne, Altitude, Wanted, Money, Earn, Spend, Stock, Reloads, Survive, Chain }
    // what has to be true at the same time for a task to count (a kill while flying, a stay while wanted...)
    public enum When { Any, Flying, OnFoot, Riding, Driving, Wanted, Hurt, Hidden }

    [Serializable]
    public class Task
    {
        public string name;
        [Tooltip("What the Seagull is told to do (the Pigeon sees it too)")]
        public string description;
        [Tooltip("How it's done (HuntTasks lists what each reads)")]
        public Goal goal;
        [Tooltip("Stay: this exact spot (Radius and Height from it); empty: the objects named in At")]
        public Transform spot;
        [Tooltip("Stay: scene objects whose names start with this, | between alternatives, \"Group/\" for each child of Group. StandOn: the ground's name")]
        public string at;
        [Tooltip("Stay: Height counts from the object's top instead of its foot")]
        public bool onTop;
        [Tooltip("Stay: how close counts, flat metres from the spot (or from the object's footprint)")]
        public float radius = 3f;
        [Tooltip("Stay: height range that counts, metres above the spot (or the object's foot or top)")]
        public Vector2 height = new Vector2(-1f, 2f);
        [FormerlySerializedAs("seconds")]
        [Tooltip("Seconds (Stay, StandOn, RidePerson, Airborne, Wanted), metres (DriveCar, RideBike, Altitude) or dollars (Money)")]
        public float amount = 3f;
        [Tooltip("Deed: which one (HuntTasks lists them). Stock: which food power (EggBomb, BagelShield, CrowdFavourite, SnitchFlock)")]
        public string deed;
        [Tooltip("Deed: all Count of them within this many seconds; 0 = any time in the round")]
        public float within;
        [Tooltip("Only counts while this is also true")]
        public When when;
        [Tooltip("Deed: how many times. Stay: at how many different places. Stock: how many held. Reloads: how many spare magazines held")]
        public int count = 1;
        [Tooltip("Off: the seconds add up over tries instead of starting over")]
        public bool inOneGo = true;
        [Tooltip("Chain: the names of the tasks to do, in order, separated by >")]
        public string steps;
    }

    // the maps the starting menu offers: scene name, and what it's called
    const string Gym = "MechanicsGym", TutorialScene = "Tutorial";
    // a join code to use again once the host's map has loaded
    static string rejoin;

    public float roundLength = 300f;
    [Tooltip("Seconds added to the clock each time the Seagull finishes a task")]
    public float taskBonus = 120f;
    [Tooltip("Seconds between the two players being in and the first round, and between rounds")]
    public float breather = 5f;
    public float pigeonRespawn = 3f;
    [Tooltip("The Seagull's health each round (the Pigeon keeps the rig's own, 100): the hunted one gets more to survive the chase")]
    public float seagullHealth = 200f;
    [Tooltip("The Pigeon's walking and running speed each round, times the rig's own (the Seagull keeps the rig's)")]
    public float pigeonSpeed = 1.3f;
    [Tooltip("Seconds the Pigeon sees exactly where a finished task was done")]
    public float taskPing = 3f;
    public Transform hunterSpawn, huntedSpawn;
    [Tooltip("This scene's landmark tasks, drawn along with the pool's")]
    public Task[] tasks;
    [Tooltip("Every other task (empty: Resources/HuntTasks)")]
    public HuntTasks pool;
    [Tooltip("The offline stand-in seagull, hidden once the match is online")]
    public GameObject dummySeagull;

    readonly NetworkVariable<Phase> phase = new();
    readonly NetworkVariable<float> timeLeft = new();
    readonly NetworkVariable<int> round = new();
    readonly NetworkVariable<ulong> hunter = new();
    readonly NetworkVariable<int> tasksDone = new();
    readonly NetworkVariable<int> taskSeed = new();
    // seconds each round's Pigeon took to kill; -1 not played yet, a round the Seagull won is float.MaxValue, and
    // Unfinished a round the Seagull ran out of time on its tasks (below every kill time, so that Pigeon wins the match)
    const float Unfinished = -2;
    readonly NetworkVariable<float> kill1 = new(-1), kill2 = new(-1);
    // each round's Pigeon deaths, for the scoreboard
    readonly NetworkVariable<int> deaths1 = new(), deaths2 = new();
    // how many matches these two have played (a rematch is a new round 1), and who's accepted a rematch: 1 the host, 2 the other
    readonly NetworkVariable<int> match = new();
    readonly NetworkVariable<byte> ready = new();
    float elapsed; // server: seconds into the round (the clock itself goes back up with each task)

    public bool Playing => IsSpawned && phase.Value == Phase.Playing;
    public bool Started => IsSpawned && round.Value > 0;
    public ulong Hunter => hunter.Value;
    bool IAmHunter => hunter.Value == NetworkManager.LocalClientId;
    int AllTasks => (1 << active.Length) - 1;

    // this game's own bird
    GameObject rig;
    Health health;
    float pigeonHealth; // the rig's own max health, for the Pigeon and outside rounds
    Weapon weapon;
    FoodPowers powers;
    HeadRide ride;
    Minimap map;
    PlayerInput input;
    StarterAssets.StarterAssetsInputs moves;
    InputAction[] keys; // the rig's own keys and buttons (fire, scope, egg, dismount...), which don't go through PlayerInput
    Renderer[] rigLooks;

    bool menu = true, paused, wasOpen;
    string status = "", joinCode = "", typedCode = "";
    ISession session;
    // this round's tasks, and how far the Seagull is with each: each task's steps (just itself unless it's a Chain), the
    // places of each step, and which step it's on
    Task[] active = { };
    Task[][] parts;
    List<(Transform thing, Bounds box)>[][] partPlaces;
    int[] step = { };
    float[] progress;
    int[] counts;
    HashSet<Transform>[] visited;
    List<float>[] times; // Deed with Within: when each counted one was done
    int lastMoney;
    Transform[] stayingAt;
    Vector3 lastAt;
    StarterAssets.ThirdPersonController mover;
    float respawnAt = -1;
    Vector3 homeAt; // where the bird starts: offline and in the waiting room, a death comes back here
    Quaternion homeTurn;
    int lastRound, kept; // the match and round last begun, and last kept for the scoreboard (match * 10 + round)
    // each round's tasks as the scoreboard lists them, kept as the round ends
    readonly List<(string what, bool done)>[] played = { new(), new() };
    float bonusUntil; // the clock shows the bonus a task just put on it

    void Awake()
    {
        Instance = this;
        // the NetworkManager outlives scene loads, so an offline restart brings in a second one: keep the first
        foreach (NetworkManager extra in FindObjectsByType<NetworkManager>(FindObjectsSortMode.None))
            if (NetworkManager.Singleton && extra != NetworkManager.Singleton) Destroy(extra.gameObject);
        powers = FindFirstObjectByType<FoodPowers>();
        rig = powers.gameObject;
        rig.TryGetComponent(out health);
        health.noRestart = true; // a death respawns the bird (RespawnWhenDead, or the round's own respawns): never reload the scene, which brings the menu back
        pigeonHealth = health.maxHealth;
        homeAt = rig.transform.position;
        homeTurn = rig.transform.rotation;
        rig.TryGetComponent(out weapon);
        rig.TryGetComponent(out ride);
        rig.TryGetComponent(out input);
        rig.TryGetComponent(out moves);
        PieceEater eater = rig.GetComponentInChildren<PieceEater>();
        var all = new System.Collections.Generic.List<InputAction> { powers.eggAction, powers.flockAction, powers.useAction, powers.nextAction };
        if (weapon) all.AddRange(new[] { weapon.fireAction, weapon.reloadAction, weapon.scopeAction });
        if (ride) all.AddRange(new[] { ride.dismountAction, ride.assassinateAction });
        if (eater) all.Add(eater.eatAction);
        keys = all.ToArray();
        rigLooks = rig.GetComponentsInChildren<Renderer>(true);
        map = FindFirstObjectByType<Minimap>();
        if (!GetComponent<MenuShowcase>()) gameObject.AddComponent<MenuShowcase>();
        rig.TryGetComponent(out mover);
        if (!pool) pool = HuntTasks.Load();
        Weapon.Did += Deed;
    }

    public override void OnDestroy()
    {
        Weapon.Did -= Deed;
        Time.timeScale = 1;
        base.OnDestroy();
    }

    void Start()
    {
        RoundReset.Remember(); // where the cars and bikes are parked, for each round to put them back
        // the joining game switched to the host's map: join again
        if (rejoin != null) { typedCode = rejoin; rejoin = null; Join(); return; }
        // for testing builds: -hunt-host hosts and -hunt-join CODE joins straight away,
        // and -hunt-round SECONDS shortens the rounds
        string[] args = Environment.GetCommandLineArgs();
        int length = Array.IndexOf(args, "-hunt-round");
        if (length >= 0 && length + 1 < args.Length && float.TryParse(args[length + 1], out float seconds)) roundLength = seconds;
        int at = Array.IndexOf(args, "-hunt-join");
        if (at >= 0 && at + 1 < args.Length) { typedCode = args[at + 1]; Join(); }
        else if (Array.IndexOf(args, "-hunt-host") >= 0) Host();
    }

    // ---- connecting, through Relay ----

    public async void Host()
    {
        status = "Starting a match...";
        try
        {
            await SignIn();
            StampVersion();
            var options = new SessionOptions { MaxPlayers = 2 }.WithRelayNetwork();
            options.SessionProperties["map"] = new SessionProperty(gameObject.scene.name, VisibilityPropertyOptions.Member); // the joining game loads it
            options.SessionProperties["version"] = new SessionProperty(Application.version, VisibilityPropertyOptions.Member); // the joining game must match it
            session = await MultiplayerService.Instance.CreateSessionAsync(options);
            // the joining game already has the Hunt Gym open: keep it rather than load it again
            NetworkManager.Singleton.SceneManager.SetClientSynchronizationMode(UnityEngine.SceneManagement.LoadSceneMode.Additive);
            joinCode = session.Code;
            Debug.Log("Hunt match hosted, join code " + joinCode);
            status = "";
            Online();
        }
        catch (Exception e) { status = "Couldn't host: " + e.Message; Debug.LogException(e); }
    }

    public async void Join()
    {
        status = "Joining...";
        try
        {
            await SignIn();
            StampVersion();
            session = await MultiplayerService.Instance.JoinSessionByCodeAsync(typedCode.Trim().ToUpperInvariant());
            // a different build from the host's: the two games wouldn't agree, so don't play
            session.Properties.TryGetValue("version", out SessionProperty version);
            string theirs = version?.Value ?? "older";
            if (theirs != Application.version)
            {
                await session.LeaveAsync();
                session = null;
                if (NetworkManager.Singleton) NetworkManager.Singleton.Shutdown();
                status = "Version mismatch: you have " + Application.version + ", the host has " + theirs + ". Both players need the latest build from itch.io.";
                return;
            }
            // the host is on the other map: leave, load it, and join again from there (Start)
            if (session.Properties.TryGetValue("map", out SessionProperty map) && map.Value != gameObject.scene.name && Application.CanStreamedLevelBeLoaded(map.Value))
            {
                status = "Loading the host's map...";
                await session.LeaveAsync();
                session = null;
                if (NetworkManager.Singleton) NetworkManager.Singleton.Shutdown();
                rejoin = typedCode;
                Loading.Load(map.Value);
                return;
            }
            joinCode = typedCode;
            status = "";
            Online();
        }
        catch (Exception e) { status = "Couldn't join: " + e.Message; Debug.LogException(e); }
    }

    // Netcode turns away a game whose protocol version differs from the host's, so a build without the check above
    // (or one that skips it) still can't join a different build; bump the Version in Player Settings for each release
    static void StampVersion()
    {
        if (NetworkManager.Singleton) NetworkManager.Singleton.NetworkConfig.ProtocolVersion = (ushort)(Hash(Application.version) | 1);
    }

    // a stable string hash (string.GetHashCode differs between runs)
    static uint Hash(string text)
    {
        uint hash = 2166136261;
        foreach (char c in text) hash = (hash ^ c) * 16777619;
        return hash ^ (hash >> 16);
    }

    static async System.Threading.Tasks.Task SignIn()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
        {
            // ponytail: a new anonymous player every launch, so two copies of the game on one machine don't sign in
            // as the same player; keep one profile per install when accounts or stats matter
            var options = new InitializationOptions();
            options.SetProfile("p" + Guid.NewGuid().ToString("N").Substring(0, 20));
            await UnityServices.InitializeAsync(options);
        }
        if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    void Online()
    {
        CloseMenu();
        if (dummySeagull) dummySeagull.SetActive(false);
    }

    void CloseMenu() => menu = false;

    // the starting menu is up (MenuShowcase plays the park behind it)
    public bool InStartMenu => menu;

    // the Escape menu; offline it also stops the game (online the other player's game keeps going)
    void Pause(bool on)
    {
        paused = on;
        if (!IsSpawned) Time.timeScale = on ? 0 : 1;
    }

    // a menu's open: the bird takes no input (a click on a button mustn't shoot) and the cursor is free; closed, back again
    void Controls(bool on)
    {
        if (input) input.enabled = on;
        if (moves)
        {
            moves.cursorLocked = on; // or clicking into the game window locks the cursor again
            if (!on) { moves.move = moves.look = Vector2.zero; moves.jump = moves.sprint = false; } // or it keeps walking
        }
        foreach (InputAction key in keys)
            if (on) key.Enable();
            else key.Disable();
        Cursor.lockState = on ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !on;
    }


    // a bird joined or left
    public void Refresh()
    {
        if (map) map.target = NetBird.Remote ? NetBird.Remote.transform : null;
    }

    // ---- the server runs the clock ----

    void Update()
    {
        if (!menu && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) Pause(!paused);
        bool open = menu || paused || (IsSpawned && phase.Value == Phase.Over); // the scoreboard's buttons need the cursor
        Music.Open = open;
        // every frame while open: a revive re-enables the gun, and with it its keys
        if (open || wasOpen) Controls(!open);
        wasOpen = open;
        // offline, or online waiting for the other player: nothing's at stake, so a death just respawns
        if (!menu && (!IsSpawned || phase.Value == Phase.Waiting)) RespawnWhenDead();
        powers.speedScale = Playing && IAmHunter ? pigeonSpeed : 1f;
        if (weapon) weapon.badge = Playing && IAmHunter; // the detective: the police leave the Pigeon be (off again offline)
        if (!IsSpawned) return;
        if (IsServer) RunClock();
        int now = match.Value * 10 + round.Value;
        if (now != lastRound && phase.Value == Phase.Playing) { lastRound = now; BeginRound(); }
        if (now != kept && round.Value > 0 && phase.Value is Phase.Between or Phase.Over) { kept = now; Keep(); }
        if (Playing) WatchOwnBird();
        // both birds see where the Seagull's unfinished tasks are, so the Pigeon knows where to lie in wait; the Seagull
        // also gets an arrow on screen to the nearest
        if (map)
        {
            map.tasksDone = Playing ? tasksDone.Value : ~0;
            map.guide = Playing && !IAmHunter;
            map.marked = Playing && IAmHunter; // the Pigeon always sees the Seagull faintly lit and outlined
            if (Playing) Marks();
            else map.ClearPlaces();
        }
    }

    float nextAt;

    void RunClock()
    {
        switch (phase.Value)
        {
            case Phase.Waiting:
                if (NetworkManager.ConnectedClientsIds.Count < 2) { nextAt = Time.time + breather; return; }
                if (Time.time >= nextAt) StartRound(1, NetworkManager.ServerClientId);
                break;
            case Phase.Playing:
                timeLeft.Value -= Time.deltaTime;
                elapsed += Time.deltaTime;
                if (timeLeft.Value <= 0) EndRound(tasksDone.Value == AllTasks ? float.MaxValue : Unfinished);
                break;
            case Phase.Between:
                if (Time.time < nextAt) return;
                if (round.Value == 1 && NetworkManager.ConnectedClientsIds.Count == 2)
                    StartRound(2, NetworkManager.ConnectedClientsIds[0] == hunter.Value ? NetworkManager.ConnectedClientsIds[1] : NetworkManager.ConnectedClientsIds[0]);
                else phase.Value = Phase.Over;
                break;
            case Phase.Over:
                if (NetworkManager.ConnectedClientsIds.Count < 2) ready.Value &= 1; // the other player left
                else if (ready.Value == 3) Rematch();
                break;
        }
    }

    // both accepted: the same two from round 1 again, the host the Pigeon first
    void Rematch()
    {
        ready.Value = 0;
        kill1.Value = kill2.Value = -1;
        deaths1.Value = deaths2.Value = 0;
        match.Value++;
        StartRound(1, NetworkManager.ServerClientId);
    }

    [Rpc(SendTo.Server)]
    void ReadyRpc(bool on, RpcParams rpc = default)
    {
        if (phase.Value != Phase.Over) return;
        int bit = rpc.Receive.SenderClientId == NetworkManager.ServerClientId ? 1 : 2;
        ready.Value = (byte)(on ? ready.Value | bit : ready.Value & ~bit);
    }

    bool IAmReady => (ready.Value & (IsServer ? 1 : 2)) != 0;
    bool TheyAreReady => (ready.Value & (IsServer ? 2 : 1)) != 0;

    void StartRound(int number, ulong hunterId)
    {
        hunter.Value = hunterId;
        tasksDone.Value = 0;
        taskSeed.Value = UnityEngine.Random.Range(1, int.MaxValue);
        timeLeft.Value = roundLength;
        elapsed = 0;
        round.Value = number;
        phase.Value = Phase.Playing;
    }

    void EndRound(float killTime)
    {
        if (phase.Value != Phase.Playing) return;
        (round.Value == 1 ? kill1 : kill2).Value = killTime;
        Debug.Log("Hunt round " + round.Value + " over: " + Result(killTime));
        phase.Value = Phase.Between;
        nextAt = Time.time + breather;
    }

    [Rpc(SendTo.Server)]
    void SeagullDiedRpc() => EndRound(elapsed);

    [Rpc(SendTo.Server)]
    void PigeonDiedRpc()
    {
        if (phase.Value == Phase.Playing) (round.Value == 1 ? deaths1 : deaths2).Value++;
    }

    // the Seagull's game moved a long task on a step: the other game's markers follow it
    [Rpc(SendTo.NotMe)]
    void StepRpc(int task, int at)
    {
        if (task < step.Length && at < parts[task].Length) step[task] = at;
    }

    [Rpc(SendTo.Server)]
    void TaskDoneRpc(int task)
    {
        if (phase.Value != Phase.Playing || (tasksDone.Value & 1 << task) != 0) return; // a repeat: no second bonus
        tasksDone.Value |= 1 << task;
        timeLeft.Value += taskBonus;
        PingRpc();
    }

    // a task was finished: the Pigeon sees exactly where the Seagull is for a moment, and both clocks show the bonus
    [Rpc(SendTo.Everyone)]
    void PingRpc()
    {
        if (IAmHunter && map) map.Reveal(taskPing);
        bonusUntil = Time.time + 3;
    }

    // ---- each game looks after its own bird ----

    void BeginRound()
    {
        Debug.Log("Hunt round " + round.Value + " begins: I'm the " + (IAmHunter ? "Pigeon" : "Seagull"));
        if (round.Value == 1) { played[0].Clear(); played[1].Clear(); } // a new match (or a rematch)
        if (ride && ride.Riding) ride.Dismount();
        health.maxHealth = IAmHunter ? pigeonHealth : seagullHealth;
        health.Revive();
        if (weapon) weapon.Rearm();
        powers.Clear();
        Place(IAmHunter ? hunterSpawn : huntedSpawn);
        // both birds get the same rig (gun, food powers, riding); the Seagull is shown by its NetBird, so its pigeon
        // rig goes invisible except the gun
        // ponytail: the Seagull's gun stays where the pigeon's wing would be, and the other player sees neither bird's
        // gun; put a gun on the NetBird models when that matters
        foreach (Renderer look in rigLooks)
            if (look) look.enabled = IAmHunter || (weapon && weapon.gun && look.transform.IsChildOf(weapon.gun));
        Draw();
        RoundBegan?.Invoke();
        respawnAt = -1;
        // the park as it was at the start (RoundReset: a fresh crowd and flocks, food, chess pieces, vehicles, no mess), and
        // the map back to just the other's rough area circle (the Pigeon gets a brief exact ping per finished task)
        StartCoroutine(RoundReset.Run());
        if (map) map.ResetRound();
    }

    // dead: back where it started after a moment, on its feet, rearmed and with no status (like a Pigeon in a round)
    void RespawnWhenDead()
    {
        if (health.IsDead && respawnAt < 0) respawnAt = Time.time + pigeonRespawn;
        if (respawnAt < 0 || Time.time < respawnAt) return;
        respawnAt = -1;
        if (ride && ride.Riding) ride.Dismount();
        health.maxHealth = pigeonHealth; // out of a round, everyone's a plain pigeon
        health.Revive();
        if (weapon) weapon.Rearm();
        powers.Clear();
        var body = rig.GetComponent<CharacterController>();
        body.enabled = false;
        rig.transform.SetPositionAndRotation(homeAt, homeTurn);
        body.enabled = true;
    }

    void Place(Transform spawn)
    {
        if (!spawn) return;
        var body = rig.GetComponent<CharacterController>();
        body.enabled = false;
        rig.transform.SetPositionAndRotation(spawn.position, spawn.rotation);
        body.enabled = true;
    }

    void WatchOwnBird()
    {
        if (IAmHunter)
        {
            // a dead Pigeon is back after a moment, at its spawn and with no status; the clock keeps going
            if (health.IsDead && respawnAt < 0) { respawnAt = Time.time + pigeonRespawn; PigeonDiedRpc(); }
            if (respawnAt >= 0 && Time.time >= respawnAt)
            {
                respawnAt = -1;
                health.Revive();
                if (weapon) weapon.Rearm();
                powers.Clear();
                Place(hunterSpawn);
            }
            return;
        }

        if (health.IsDead) { if (respawnAt < 0) { respawnAt = 0; SeagullDiedRpc(); } return; }
        Vector3 at = rig.transform.position;
        float moved = new Vector2(at.x - lastAt.x, at.z - lastAt.z).magnitude;
        lastAt = at;
        bool flying = Flying;
        int money = weapon ? weapon.Money : 0, earned = Mathf.Max(0, money - lastMoney), spent = Mathf.Max(0, lastMoney - money);
        lastMoney = money;
        for (int i = 0; i < active.Length; i++)
        {
            if (Finished(i)) continue;
            Task task = Now(i);
            bool also = Meets(task.when);
            switch (task.goal)
            {
                case Goal.Stay: if (also) Stay(i, at); else { stayingAt[i] = null; if (task.inOneGo) progress[i] = 0; } break;
                case Goal.StandOn: Timed(i, also && Standing(at, task.at)); break;
                case Goal.RidePerson: Timed(i, also && ride && ride.Mount); break;
                case Goal.Airborne: Timed(i, also && flying); break;
                case Goal.Wanted: Timed(i, also && Npc.IsWanted(weapon)); break;
                case Goal.Survive: Timed(i, also); break;
                case Goal.DriveCar: Toward(i, also && ride && ride.Driving is Car ? moved : 0); break;
                case Goal.RideBike: Toward(i, also && ride && ride.Driving is CitiBike ? moved : 0); break;
                case Goal.Earn: Toward(i, also ? earned : 0); break;
                case Goal.Spend: Toward(i, also ? spent : 0); break;
                case Goal.Altitude: if (also && flying && !Physics.Raycast(at, Vector3.down, task.amount, ~(1 << rig.layer), QueryTriggerInteraction.Ignore)) Finish(i); break;
                case Goal.Money: if (also && money >= task.amount) Finish(i); break;
                case Goal.Reloads: if (also && weapon && weapon.reloadsLeft >= task.count) Finish(i); break;
                case Goal.Stock: if (also && Enum.TryParse(task.deed, out FoodPowers.Ability power) && powers.Count(power) >= task.count) Finish(i); break;
            }
        }
    }

    bool Flying => mover && !mover.Grounded && !(ride && ride.Riding);

    bool Meets(When when) => when switch
    {
        When.Flying => Flying,
        When.OnFoot => mover && mover.Grounded && !(ride && ride.Riding),
        When.Riding => ride && ride.Mount,
        When.Driving => ride && ride.Driving,
        When.Wanted => Npc.IsWanted(weapon),
        When.Hurt => health.Current <= health.maxHealth / 3,
        When.Hidden => weapon && weapon.unseen,
        _ => true,
    };

    // ---- this round's tasks ----

    // the round's over: its tasks as they ended, for the scoreboard
    void Keep()
    {
        List<(string what, bool done)> list = played[round.Value - 1];
        list.Clear();
        for (int i = 0; i < active.Length; i++) list.Add((active[i].description, Finished(i)));
    }

    bool Finished(int i) => (tasksDone.Value & (1 << i)) != 0;
    // the step task i is on (the task itself unless it's a Chain), and that step's places
    Task Now(int i) => parts[i][step[i]];
    List<(Transform thing, Bounds box)> Here(int i) => partPlaces[i][step[i]];

    // the current step's done: on to the next, or the task's done
    void Finish(int i)
    {
        progress[i] = 0;
        counts[i] = 0;
        stayingAt[i] = null;
        visited[i].Clear();
        times[i].Clear();
        if (step[i] < parts[i].Length - 1) { step[i]++; StepRpc(i, step[i]); return; }
        if (Finished(i)) return;
        TaskDoneRpc(i); // the server ignores a repeat that crosses with its update
        Sfx.Play("objective done"); // the sax, for the Seagull who did it
    }

    // each unfinished task's marker on the minimap: the nearest place of its current step not done yet (none if the
    // step can be done anywhere); and every such place gets a ring, which bursts green as it drops out (done)
    void Marks()
    {
        if (map.taskSpots.Length != active.Length) map.taskSpots = new Transform[active.Length];
        map.taskPlaces.Clear();
        Vector3 me = rig.transform.position;
        for (int i = 0; i < active.Length; i++)
        {
            Transform best = null;
            float bestDistance = float.MaxValue;
            foreach (var (thing, _) in Here(i))
            {
                if (!thing || visited[i].Contains(thing)) continue;
                if (!Finished(i)) map.taskPlaces.Add(thing);
                float d = (thing.position - me).sqrMagnitude;
                if (d < bestDistance) { bestDistance = d; best = thing; }
            }
            map.taskSpots[i] = best;
        }
    }

    // seconds while `on`: in one go, or adding up
    void Timed(int i, bool on)
    {
        if (on) progress[i] += Time.deltaTime;
        else if (Now(i).inOneGo) progress[i] = 0;
        if (progress[i] >= Now(i).amount) Finish(i);
    }

    // metres
    void Toward(int i, float metres)
    {
        progress[i] += metres;
        if (progress[i] >= Now(i).amount) Finish(i);
    }

    // at one of its places for the whole time, Count different ones
    void Stay(int i, Vector3 at)
    {
        Task task = Now(i);
        Transform here = null;
        foreach (var (thing, box) in Here(i))
            if (At(task, thing, box, at) && !visited[i].Contains(thing)) { here = thing; break; }
        if (here != stayingAt[i]) { stayingAt[i] = here; if (task.inOneGo) progress[i] = 0; }
        if (!here) return;
        progress[i] += Time.deltaTime;
        if (progress[i] < task.amount) return;
        progress[i] = 0;
        visited[i].Add(here);
        if (++counts[i] >= task.count) Finish(i);
    }

    static bool At(Task task, Transform thing, Bounds box, Vector3 at)
    {
        float flat, up;
        if (task.spot)
        {
            Vector3 off = at - task.spot.position;
            flat = new Vector2(off.x, off.z).magnitude;
            up = off.y;
        }
        else
        {
            Vector3 near = box.ClosestPoint(new Vector3(at.x, box.center.y, at.z));
            flat = new Vector2(at.x - near.x, at.z - near.z).magnitude;
            up = at.y - (task.onTop ? box.max.y : box.min.y);
        }
        return flat <= task.radius && up >= task.height.x && up <= task.height.y;
    }

    // what's right under the bird's feet is named `ground`
    bool Standing(Vector3 at, string ground) =>
        Physics.Raycast(at + Vector3.up * 0.3f, Vector3.down, out RaycastHit hit, 1f, ~(1 << rig.layer), QueryTriggerInteraction.Ignore) && Named(hit.collider.name, ground);

    static bool Named(string name, string at)
    {
        if (string.IsNullOrEmpty(at)) return false;
        foreach (string one in at.Split('|'))
            if (one != "" && !one.EndsWith("/") && name.StartsWith(one)) return true;
        return false;
    }

    // the round's tasks: shuffled with the round's seed, then the first Per Round that can be done in this scene
    // (the same in both games: same scene, same seed)
    void Draw()
    {
        var all = new List<Task>();
        if (!pool || pool.sceneTasks) all.AddRange(tasks);
        if (pool) all.AddRange(pool.tasks);
        var random = new System.Random(taskSeed.Value);
        for (int i = all.Count - 1; i > 0; i--) { int j = random.Next(i + 1); (all[i], all[j]) = (all[j], all[i]); }

        // a Chain's steps are the other tasks it names; with Long Tasks, chains are dealt first (singles only fill up)
        var named = new Dictionary<string, Task>();
        foreach (Task task in all) if (task.goal != Goal.Chain && !string.IsNullOrEmpty(task.name)) named[task.name] = task;
        if (pool && pool.longTasks) all.Sort((a, b) => (a.goal == Goal.Chain ? 0 : 1).CompareTo(b.goal == Goal.Chain ? 0 : 1));

        Transform[] scene = FindObjectsByType<Transform>(FindObjectsSortMode.InstanceID);
        var picked = new List<Task>();
        var pickedParts = new List<Task[]>();
        var where = new List<List<(Transform, Bounds)>[]>();
        int wanted = Mathf.Min(pool ? pool.perRound : 3, 30);
        foreach (Task task in all)
        {
            if (picked.Count >= wanted) break;
            Task[] steps = task.goal == Goal.Chain ? Steps(task, named) : new[] { task };
            if (steps == null) continue;
            var found = new List<(Transform, Bounds)>[steps.Length];
            bool doable = true;
            for (int s = 0; s < steps.Length && doable; s++) doable = Doable(steps[s], found[s] = Places(steps[s], scene), scene);
            if (!doable) continue;
            picked.Add(task);
            pickedParts.Add(steps);
            where.Add(found);
        }
        active = picked.ToArray();
        parts = pickedParts.ToArray();
        partPlaces = where.ToArray();
        step = new int[active.Length];
        progress = new float[active.Length];
        counts = new int[active.Length];
        stayingAt = new Transform[active.Length];
        visited = new HashSet<Transform>[active.Length];
        times = new List<float>[active.Length];
        for (int i = 0; i < active.Length; i++) { visited[i] = new HashSet<Transform>(); times[i] = new List<float>(); }
        lastMoney = weapon ? weapon.Money : 0;
        lastAt = rig.transform.position;
        if (map) map.taskSpots = new Transform[active.Length]; // Marks fills them in
    }

    // a Chain's steps by name, in order; null if one isn't a task (or is itself a chain)
    static Task[] Steps(Task chain, Dictionary<string, Task> named)
    {
        if (string.IsNullOrEmpty(chain.steps)) return null;
        string[] names = chain.steps.Split('>');
        var steps = new Task[names.Length];
        for (int s = 0; s < names.Length; s++)
            if (!named.TryGetValue(names[s].Trim(), out steps[s])) { Debug.LogWarning("Hunt task " + chain.name + ": no task called " + names[s].Trim()); return null; }
        return steps;
    }

    static List<(Transform, Bounds)> Places(Task task, Transform[] scene)
    {
        var found = new List<(Transform, Bounds)>();
        if (task.goal != Goal.Stay && task.goal != Goal.Deed) return found;
        if (task.spot) { found.Add((task.spot, default)); return found; }
        if (string.IsNullOrEmpty(task.at)) return found;
        var groups = new List<string>();
        foreach (string one in task.at.Split('|'))
            if (one.EndsWith("/")) groups.Add(one.TrimEnd('/'));
        foreach (Transform t in scene)
        {
            if (Named(t.name, task.at) && Box(t, out Bounds box)) found.Add((t, box));
            if (groups.Contains(t.name))
                foreach (Transform child in t)
                    if (child.gameObject.activeInHierarchy && Box(child, out Bounds childBox)) found.Add((child, childBox));
        }
        return found;
    }

    // its solid extent (colliders, or failing that what's drawn)
    static bool Box(Transform t, out Bounds box)
    {
        box = default;
        bool any = false;
        foreach (Collider c in t.GetComponentsInChildren<Collider>())
            if (!c.isTrigger) { if (any) box.Encapsulate(c.bounds); else box = c.bounds; any = true; }
        if (!any)
            foreach (Renderer r in t.GetComponentsInChildren<Renderer>())
            { if (any) box.Encapsulate(r.bounds); else box = r.bounds; any = true; }
        return any;
    }

    // whether this scene has what the task needs
    static bool Doable(Task task, List<(Transform, Bounds)> found, Transform[] scene)
    {
        switch (task.goal)
        {
            case Goal.Stay: return found.Count >= Mathf.Max(1, task.count);
            case Goal.StandOn: return Array.Exists(scene, t => Named(t.name, task.at) && t.TryGetComponent(out Collider _));
            case Goal.DriveCar: return FindAnyObjectByType<Car>();
            case Goal.RideBike: return FindAnyObjectByType<CitiBike>();
            case Goal.Deed:
                if ((task.spot || !string.IsNullOrEmpty(task.at)) && found.Count == 0) return false; // done at a place that isn't here
                string deed = task.deed ?? "";
                if (deed == "hijack") return FindAnyObjectByType<TrafficCar>();
                if (deed == "drive car") return FindAnyObjectByType<Car>();
                if (deed == "ride bike") return FindAnyObjectByType<CitiBike>();
                if (deed.StartsWith("run over") || deed == "roadkill") return FindAnyObjectByType<Vehicle>();
                if (deed == "eat chess piece") return GameObject.Find("ChessTables");
                if (deed == "search trash") return FindAnyObjectByType<TrashCan>();
                return true;
            default: return true;
        }
    }

    // something the Seagull did (Weapon.Done)
    void Deed(Weapon who, string deed)
    {
        if (who != weapon || !Playing || IAmHunter || active.Length == 0) return;
        Vector3 at = rig.transform.position;
        for (int i = 0; i < active.Length; i++)
        {
            if (Finished(i)) continue;
            Task task = Now(i);
            if (task.goal != Goal.Deed || task.deed != deed || !Meets(task.when)) continue;
            if (Here(i).Count > 0 && !Here(i).Exists(p => At(task, p.thing, p.box, at))) continue; // somewhere in particular
            if (task.within > 0)
            {
                times[i].Add(Time.time);
                times[i].RemoveAll(t => Time.time - t > task.within);
                counts[i] = times[i].Count;
            }
            else counts[i]++;
            if (counts[i] >= task.count) Finish(i);
        }
    }

    // ---- screen ----

    void OnGUI()
    {
        SubwayGUI.Scale();

        if (menu)
        {
            var sign = new Rect(20, 20, 420, 532); // title 50, the code box and eight choices 44 each, the status line
            SubwayGUI.Begin(sign);
            SubwayGUI.Title("ShitShow");
            if (SubwayGUI.Button("Host a match")) Host();
            typedCode = SubwayGUI.Field(typedCode, 12, "Join code");
            if (SubwayGUI.Button("Join with that code")) Join();
            if (SubwayGUI.Button("Play offline")) CloseMenu();
            // every mechanic, one station each, offline (GymMenu there comes back here)
            if (SubwayGUI.Button("Tutorial (learn the game)", Application.CanStreamedLevelBeLoaded(TutorialScene))) { Tutorial.Begin(); LoadMap(TutorialScene); }
            if (SubwayGUI.Button("Mechanics Gym (practice)", Application.CanStreamedLevelBeLoaded(Gym))) LoadMap(Gym);
            if (SubwayGUI.Button("Settings")) { GameSettings.Showing = !GameSettings.Showing; Credits.Showing = false; }
            if (SubwayGUI.Button("Credits")) { Credits.Showing = !Credits.Showing; GameSettings.Showing = false; }
            if (SubwayGUI.Button("Quit game")) Quit();
            SubwayGUI.Note(status);
            SubwayGUI.End();
            GameSettings.Panel(sign);
            Credits.Panel(sign);
            return;
        }
        if (paused)
        {
            var sign = new Rect(20, 20, 380, 304);
            SubwayGUI.Begin(sign);
            SubwayGUI.Title("Paused");
            if (SubwayGUI.Button("Resume")) Pause(false);
            if (SubwayGUI.Button("Settings")) GameSettings.Showing = !GameSettings.Showing;
            if (SubwayGUI.Button("Quit to menu")) ToMenu();
            if (SubwayGUI.Button("Quit game")) Quit();
            SubwayGUI.Note(IsSpawned && joinCode != "" ? "Join code: " + joinCode : "Esc to resume");
            SubwayGUI.End();
            GameSettings.Panel(sign);
            return;
        }
        if ((!IsSpawned || phase.Value == Phase.Waiting) && respawnAt > 0) SubwayGUI.Banner(300, "Respawning...");
        if (!IsSpawned) return;
        if (phase.Value == Phase.Over) { Scoreboard(); return; }

        string heading, text;
        switch (phase.Value)
        {
            case Phase.Waiting:
                heading = "Join code: " + joinCode;
                text = "Waiting for the other player...";
                break;
            case Phase.Playing:
                int left = active.Length - Bits(tasksDone.Value);
                heading = "Round " + round.Value + "/2   " + Clock(timeLeft.Value)
                          + (Time.time < bonusUntil ? "   <color=#00933C>+" + Clock(taskBonus) + "</color>" : "");
                text = (IAmHunter ? "<b>PIGEON</b>: kill the Seagull. Its tasks (" + left + " left, blue on map, each +" + Clock(taskBonus) + "):\n" + TaskList()
                                  : "<b>SEAGULL</b>: finish by 0:00 or lose; each task adds " + Clock(taskBonus) + ". Stay alive.\n" + TaskList()).TrimEnd();
                if (IAmHunter && respawnAt >= 0) SubwayGUI.Banner(300, "Respawning...");
                break;
            case Phase.Between:
                heading = "Round " + round.Value + " over";
                text = Result(round.Value == 1 ? kill1.Value : kill2.Value) + "\nNext round soon; roles swap.";
                break;
            default:
                return;
        }
        // small, in the corner: it stays up the whole round
        const float boardWidth = 330;
        SubwayGUI.SmallBoard(new Rect(20, 20, boardWidth, 54 + SubwayGUI.SmallBodyHeight(text, boardWidth)), heading, text);
    }

    // the match is over: both rounds side by side, and a rematch both have to accept, or back to the starting menu
    void Scoreboard()
    {
        const float width = 720, inside = width - 36;
        bool host = IsServer, there = NetBird.Remote;
        string[] head = { "Round 1", "Round 2" },
            pigeon = { host ? "You" : "Them", host ? "Them" : "You" },
            seagull = { pigeon[1], pigeon[0] },
            result = { Short(kill1.Value), Short(kill2.Value) },
            done = new string[2], bonus = new string[2], list = new string[2],
            deaths = { kill1.Value == -1 ? "-" : deaths1.Value.ToString(), kill2.Value == -1 ? "-" : deaths2.Value.ToString() };
        for (int r = 0; r < 2; r++)
        {
            int n = played[r].FindAll(t => t.done).Count;
            bool ran = (r == 0 ? kill1.Value : kill2.Value) != -1;
            done[r] = ran ? n + " of " + played[r].Count : "-";
            bonus[r] = ran ? "+" + Clock(n * taskBonus) : "-";
            list[r] = "";
            foreach (var (what, did) in played[r]) list[r] += (did ? "[x] " : "[ ] ") + what + "\n";
            list[r] = list[r].TrimEnd();
        }

        string note = !there ? "The other player left."
            : IAmReady ? "Waiting for the other player to accept..."
            : TheyAreReady ? "The other player wants a rematch."
            : "A rematch starts when you both accept.";
        float height = 20 + 50
            + SubwayGUI.RowHeight(inside, true, false, head)
            + 6 * SubwayGUI.RowHeight(inside, false, false, result) // the one-line rows are all as tall as this
            + SubwayGUI.RowHeight(inside, false, true, list)
            + 2 * 44 + 4 + 24 + 16;
        var sign = new Rect((1280 - width) / 2, Mathf.Max(20, (720 - height) / 2), width, height);
        SubwayGUI.Begin(sign);
        SubwayGUI.Title(Winner());
        SubwayGUI.Row(inside, true, false, "", head);
        SubwayGUI.Row(inside, false, false, "Pigeon", pigeon);
        SubwayGUI.Row(inside, false, false, "Seagull", seagull);
        SubwayGUI.Row(inside, false, false, "Result", result);
        SubwayGUI.Row(inside, false, false, "Tasks done", done);
        SubwayGUI.Row(inside, false, false, "Clock bonus", bonus);
        SubwayGUI.Row(inside, false, true, "Seagull tasks", list);
        SubwayGUI.Row(inside, false, false, "Pigeon deaths", deaths);
        if (SubwayGUI.Button(IAmReady ? "Cancel rematch" : TheyAreReady ? "Accept rematch" : "Rematch", there)) ReadyRpc(!IAmReady);
        if (SubwayGUI.Button("Back to main menu")) ToMenu();
        SubwayGUI.Note(note);
        SubwayGUI.End();
    }

    // a round's result, short enough for a scoreboard cell
    string Short(float killTime) =>
        killTime == Unfinished ? "Seagull ran out of time" :
        killTime < 0 ? "-" : killTime == float.MaxValue ? "Seagull got away" : "Killed after " + Clock(killTime);

    // leave the match (if online) and start the scene over, which brings the starting menu back
    // ponytail: the other player is just dropped (their game sits waiting); tell them and end the round when that matters
    void ToMenu()
    {
        if (session != null) _ = session.LeaveAsync();
        if (NetworkManager.Singleton) NetworkManager.Singleton.Shutdown();
        Time.timeScale = 1;
        Loading.Load(gameObject.scene.name);
    }

    static void LoadMap(string scene)
    {
        Time.timeScale = 1;
        Loading.Load(scene);
    }

    static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    string TaskList()
    {
        string list = "";
        for (int i = 0; i < active.Length; i++)
        {
            list += (Finished(i) ? "[x] " : "[ ] ") + active[i].description + (Finished(i) ? "" : Progress(i)) + "\n";
            if (parts[i].Length > 1 && !Finished(i)) list += "     " + (step[i] + 1) + "/" + parts[i].Length + ": " + Now(i).description + "\n";
        }
        return list;
    }

    // how far the Seagull is with a task (only its own game knows; the Pigeon sees just the ticks)
    string Progress(int i)
    {
        Task task = Now(i);
        string done = task.count > 1 && counts[i] > 0 ? "  " + counts[i] + "/" + task.count : "";
        if (progress[i] <= 0) return done;
        if (task.goal is Goal.Earn or Goal.Spend) return "  $" + Mathf.FloorToInt(progress[i]) + "/$" + task.amount;
        return task.goal is Goal.DriveCar or Goal.RideBike ? "  " + Mathf.FloorToInt(progress[i]) + "/" + task.amount + " m"
            : done + "  " + Mathf.CeilToInt(task.amount - progress[i]) + "s";
    }

    string Result(float killTime) =>
        killTime == Unfinished ? "the Seagull didn't finish its tasks: it loses the match" :
        killTime < 0 ? "-" : killTime == float.MaxValue ? "the Seagull got away" : "Pigeon killed in " + Clock(killTime);

    // round 1's Pigeon is the host, round 2's the other player: the faster kill wins
    string Winner()
    {
        bool hostWasFirst = NetworkManager.LocalClientId == NetworkManager.ServerClientId;
        float mine = hostWasFirst ? kill1.Value : kill2.Value, theirs = hostWasFirst ? kill2.Value : kill1.Value;
        return mine < theirs ? "You win!" : mine > theirs ? "You lose." : "A draw.";
    }

    static string Clock(float seconds)
    {
        int s = Mathf.Max(0, Mathf.CeilToInt(seconds));
        return s / 60 + ":" + (s % 60).ToString("00");
    }

    static int Bits(int n)
    {
        int count = 0;
        for (; n != 0; n &= n - 1) count++;
        return count;
    }
}
