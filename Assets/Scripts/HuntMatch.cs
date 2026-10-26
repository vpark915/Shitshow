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
//   then loses the whole match outright, whatever happens in the other round (both do: it's a draw).
// - A dead Pigeon comes back at its spawn after a few seconds with its status (food) gone; the clock keeps running.
// - Match: otherwise the faster kill as Pigeon wins.
// The server keeps the clock and the score; each game watches its own bird for deaths and tasks and reports them.
// Each round's Seagull tasks are drawn from HuntTasks (plus this scene's landmark Tasks) with a seed the server picks,
// so both games draw the same ones; HuntTasks says how each kind of task is done.
public class HuntMatch : NetworkBehaviour
{
    public static HuntMatch Instance { get; private set; }

    public enum Phase : byte { Waiting, Playing, Between, Over }

    // how a task is done; HuntTasks describes each and the fields it reads
    public enum Goal { Stay, StandOn, Deed, RidePerson, DriveCar, RideBike, Airborne, Altitude, Wanted, Money, Earn, Spend, Stock, Reloads, Survive }
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
    }

    public float roundLength = 180f;
    [Tooltip("Seconds between the two players being in and the first round, and between rounds")]
    public float breather = 5f;
    public float pigeonRespawn = 3f;
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

    public bool Playing => IsSpawned && phase.Value == Phase.Playing;
    public bool Started => IsSpawned && round.Value > 0;
    public ulong Hunter => hunter.Value;
    bool IAmHunter => hunter.Value == NetworkManager.LocalClientId;
    int AllTasks => (1 << active.Length) - 1;

    // this game's own bird
    GameObject rig;
    Health health;
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
    // this round's tasks, and how far the Seagull is with each
    Task[] active = { };
    List<(Transform thing, Bounds box)>[] places;
    float[] progress;
    int[] counts;
    HashSet<Transform>[] visited;
    List<float>[] times; // Deed with Within: when each counted one was done
    int lastMoney;
    Transform[] stayingAt;
    Vector3 lastAt;
    StarterAssets.ThirdPersonController mover;
    float respawnAt = -1;
    int lastRound;

    void Awake()
    {
        Instance = this;
        // the NetworkManager outlives scene loads, so an offline restart brings in a second one: keep the first
        foreach (NetworkManager extra in FindObjectsByType<NetworkManager>(FindObjectsSortMode.None))
            if (NetworkManager.Singleton && extra != NetworkManager.Singleton) Destroy(extra.gameObject);
        powers = FindFirstObjectByType<FoodPowers>();
        rig = powers.gameObject;
        rig.TryGetComponent(out health);
        rig.TryGetComponent(out weapon);
        rig.TryGetComponent(out ride);
        rig.TryGetComponent(out input);
        rig.TryGetComponent(out moves);
        PieceEater eater = rig.GetComponentInChildren<PieceEater>();
        var all = new System.Collections.Generic.List<InputAction> { powers.eggAction, powers.flockAction };
        if (weapon) all.AddRange(new[] { weapon.fireAction, weapon.reloadAction, weapon.scopeAction });
        if (ride) all.AddRange(new[] { ride.dismountAction, ride.assassinateAction });
        if (eater) all.Add(eater.eatAction);
        keys = all.ToArray();
        rigLooks = rig.GetComponentsInChildren<Renderer>(true);
        map = FindFirstObjectByType<Minimap>();
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
            session = await MultiplayerService.Instance.CreateSessionAsync(new SessionOptions { MaxPlayers = 2 }.WithRelayNetwork());
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
            session = await MultiplayerService.Instance.JoinSessionByCodeAsync(typedCode.Trim().ToUpperInvariant());
            joinCode = typedCode;
            status = "";
            Online();
        }
        catch (Exception e) { status = "Couldn't join: " + e.Message; Debug.LogException(e); }
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
        health.noRestart = true;
        if (dummySeagull) dummySeagull.SetActive(false);
    }

    void CloseMenu() => menu = false;

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
        bool open = menu || paused;
        // every frame while open: a revive re-enables the gun, and with it its keys
        if (open || wasOpen) Controls(!open);
        wasOpen = open;
        if (!IsSpawned) return;
        if (IsServer) RunClock();
        if (round.Value != lastRound && phase.Value == Phase.Playing) { lastRound = round.Value; BeginRound(); }
        if (Playing) WatchOwnBird();
        // both birds see where the Seagull's unfinished tasks are, so the Pigeon knows where to lie in wait
        if (map) map.tasksDone = Playing ? tasksDone.Value : ~0;
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
                if (timeLeft.Value <= 0) EndRound(tasksDone.Value == AllTasks ? float.MaxValue : Unfinished);
                break;
            case Phase.Between:
                if (Time.time < nextAt) return;
                if (round.Value == 1 && NetworkManager.ConnectedClientsIds.Count == 2)
                    StartRound(2, NetworkManager.ConnectedClientsIds[0] == hunter.Value ? NetworkManager.ConnectedClientsIds[1] : NetworkManager.ConnectedClientsIds[0]);
                else phase.Value = Phase.Over;
                break;
        }
    }

    void StartRound(int number, ulong hunterId)
    {
        hunter.Value = hunterId;
        tasksDone.Value = 0;
        taskSeed.Value = UnityEngine.Random.Range(1, int.MaxValue);
        timeLeft.Value = roundLength;
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
    void SeagullDiedRpc() => EndRound(roundLength - timeLeft.Value);

    [Rpc(SendTo.Server)]
    void TaskDoneRpc(int task)
    {
        if (phase.Value != Phase.Playing) return;
        tasksDone.Value |= 1 << task;
        PingRpc();
    }

    // a task was finished: the Pigeon sees exactly where the Seagull is for a moment
    [Rpc(SendTo.Everyone)]
    void PingRpc()
    {
        if (IAmHunter && map) map.Reveal(taskPing);
    }

    // ---- each game looks after its own bird ----

    void BeginRound()
    {
        Debug.Log("Hunt round " + round.Value + " begins: I'm the " + (IAmHunter ? "Pigeon" : "Seagull"));
        if (ride && ride.Riding) ride.Dismount();
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
        respawnAt = -1;
        // both birds see only the other's rough area circle; the Pigeon gets a brief exact ping per finished task
        if (map) map.Hide();
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
            if (health.IsDead && respawnAt < 0) respawnAt = Time.time + pigeonRespawn;
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
            Task task = active[i];
            if (Finished(i)) continue;
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

    bool Finished(int i) => (tasksDone.Value & (1 << i)) != 0;

    void Finish(int i)
    {
        progress[i] = 0;
        counts[i] = Mathf.Max(counts[i], active[i].count);
        if (!Finished(i)) TaskDoneRpc(i); // the server ignores a repeat that crosses with its update
    }

    // seconds while `on`: in one go, or adding up
    void Timed(int i, bool on)
    {
        if (on) progress[i] += Time.deltaTime;
        else if (active[i].inOneGo) progress[i] = 0;
        if (progress[i] >= active[i].amount) Finish(i);
    }

    // metres
    void Toward(int i, float metres)
    {
        progress[i] += metres;
        if (progress[i] >= active[i].amount) Finish(i);
    }

    // at one of its places for the whole time, Count different ones
    void Stay(int i, Vector3 at)
    {
        Task task = active[i];
        Transform here = null;
        foreach (var (thing, box) in places[i])
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

        Transform[] scene = FindObjectsByType<Transform>(FindObjectsSortMode.InstanceID);
        var picked = new List<Task>();
        var where = new List<List<(Transform, Bounds)>>();
        int wanted = Mathf.Min(pool ? pool.perRound : 3, 30);
        foreach (Task task in all)
        {
            if (picked.Count >= wanted) break;
            List<(Transform, Bounds)> found = Places(task, scene);
            if (!Doable(task, found, scene)) continue;
            picked.Add(task);
            where.Add(found);
        }
        active = picked.ToArray();
        places = where.ToArray();
        progress = new float[active.Length];
        counts = new int[active.Length];
        stayingAt = new Transform[active.Length];
        visited = new HashSet<Transform>[active.Length];
        times = new List<float>[active.Length];
        for (int i = 0; i < active.Length; i++) { visited[i] = new HashSet<Transform>(); times[i] = new List<float>(); }
        lastMoney = weapon ? weapon.Money : 0;
        lastAt = rig.transform.position;
        // a task with one place gets a diamond on the minimap
        if (map) map.taskSpots = Array.ConvertAll(places, p => p.Count == 1 ? p[0].Item1 : null);
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
            Task task = active[i];
            if (task.goal != Goal.Deed || task.deed != deed || Finished(i) || !Meets(task.when)) continue;
            if (places[i].Count > 0 && !places[i].Exists(p => At(task, p.thing, p.box, at))) continue; // somewhere in particular
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
        float scale = Screen.height / 720f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
        var box = new GUIStyle(GUI.skin.box) { fontSize = 18, alignment = TextAnchor.UpperLeft, wordWrap = true };
        var big = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter };

        if (menu)
        {
            GUILayout.BeginArea(new Rect(20, 20, 360, 260), GUI.skin.box);
            GUILayout.Label("<b>Pigeon vs Seagull: online</b>", new GUIStyle(GUI.skin.label) { fontSize = 20, richText = true });
            if (GUILayout.Button("Host a match", GUILayout.Height(36))) Host();
            GUILayout.BeginHorizontal();
            typedCode = GUILayout.TextField(typedCode, 12, new GUIStyle(GUI.skin.textField) { fontSize = 20 }, GUILayout.Height(36));
            if (GUILayout.Button("Join", GUILayout.Width(80), GUILayout.Height(36))) Join();
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Play offline", GUILayout.Height(30))) CloseMenu();
            GUILayout.Label(status, new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true });
            GUILayout.EndArea();
            return;
        }
        if (paused)
        {
            GUILayout.BeginArea(new Rect(20, 20, 360, 240), GUI.skin.box);
            GUILayout.Label("<b>Paused</b>", new GUIStyle(GUI.skin.label) { fontSize = 20, richText = true });
            if (GUILayout.Button("Resume", GUILayout.Height(36))) Pause(false);
            if (GUILayout.Button("Quit to menu", GUILayout.Height(30))) ToMenu();
            if (GUILayout.Button("Quit game", GUILayout.Height(30))) Quit();
            GUILayout.Label(IsSpawned && joinCode != "" ? "Join code: " + joinCode : "Esc to resume", new GUIStyle(GUI.skin.label) { fontSize = 15 });
            GUILayout.EndArea();
            return;
        }
        if (!IsSpawned) return;

        string text;
        switch (phase.Value)
        {
            case Phase.Waiting:
                text = "Join code: <b>" + joinCode + "</b>\nWaiting for the other player...";
                break;
            case Phase.Playing:
                int left = active.Length - Bits(tasksDone.Value);
                text = "Round " + round.Value + "/2   " + Clock(timeLeft.Value) + "\n" +
                       (IAmHunter ? "You're the <b>PIGEON</b>: kill the Seagull. Its tasks (" + left + " left, blue on the map):\n" + TaskList()
                                  : "You're the <b>SEAGULL</b>: finish your tasks and survive. Tasks unfinished at 0:00 and you <b>lose the match</b>. Shooting the Pigeon buys you time.\n" + TaskList());
                if (IAmHunter && respawnAt >= 0) GUI.Label(new Rect(0, 300, 1280, 40), "Respawning...", big);
                break;
            case Phase.Between:
                text = "Round " + round.Value + ": " + Result(round.Value == 1 ? kill1.Value : kill2.Value) + "\nNext round soon; roles swap.";
                break;
            default:
                text = "Round 1: " + Result(kill1.Value) + "\nRound 2: " + Result(kill2.Value) + "\n<b>" + Winner() + "</b>";
                break;
        }
        box.richText = true;
        GUI.Box(new Rect(20, 20, 420, phase.Value == Phase.Playing ? 80 + active.Length * 24 + 40 : 110), text, box);
    }

    // leave the match (if online) and start the scene over, which brings the starting menu back
    // ponytail: the other player is just dropped (their game sits waiting); tell them and end the round when that matters
    void ToMenu()
    {
        if (session != null) _ = session.LeaveAsync();
        if (NetworkManager.Singleton) NetworkManager.Singleton.Shutdown();
        Time.timeScale = 1;
        UnityEngine.SceneManagement.SceneManager.LoadScene(gameObject.scene.name);
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
            list += (Finished(i) ? "[x] " : "[ ] ") + active[i].description + (Finished(i) ? "" : Progress(i)) + "\n";
        return list;
    }

    // how far the Seagull is with a task (only its own game knows; the Pigeon sees just the ticks)
    string Progress(int i)
    {
        Task task = active[i];
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
