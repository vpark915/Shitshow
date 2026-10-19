using System;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.InputSystem;

// The online hunter vs hunted match (design doc sections 3, 4, 13), connected through Unity Relay so neither player
// needs an open port. One player hosts and gets a join code; the other types it in. Then two rounds: the host is the
// Pigeon (hunter) first, then roles swap.
// - The Pigeon wins a round by killing the Seagull, or by the clock running out with Seagull tasks unfinished.
// - The Seagull wins by finishing every task and still being alive at 0:00.
// - A dead Pigeon comes back at its spawn after a few seconds with its status (food) gone; the clock keeps running.
// - Match: the faster kill as Pigeon wins (a timed-out round counts as a kill at the full round length).
// The server keeps the clock and the score; each game watches its own bird for deaths and tasks and reports them.
public class HuntMatch : NetworkBehaviour
{
    public static HuntMatch Instance { get; private set; }

    public enum Phase : byte { Waiting, Playing, Between, Over }

    [Serializable]
    public class Task
    {
        public string name;
        [Tooltip("What the Seagull is told to do")]
        public string description;
        public Transform spot;
        [Tooltip("How close (flat distance, metres) to the spot counts")]
        public float radius = 3f;
        [Tooltip("Height range above the spot that counts, so the arch top needs standing on top")]
        public Vector2 height = new Vector2(-1f, 2f);
        [Tooltip("Seconds to stay there in one go")]
        public float seconds = 3f;
    }

    public float roundLength = 180f;
    [Tooltip("Seconds between the two players being in and the first round, and between rounds")]
    public float breather = 5f;
    public float pigeonRespawn = 3f;
    [Tooltip("Seconds the Pigeon sees exactly where a finished task was done")]
    public float taskPing = 3f;
    public Transform hunterSpawn, huntedSpawn;
    public Task[] tasks;
    [Tooltip("The offline stand-in seagull, hidden once the match is online")]
    public GameObject dummySeagull;

    readonly NetworkVariable<Phase> phase = new();
    readonly NetworkVariable<float> timeLeft = new();
    readonly NetworkVariable<int> round = new();
    readonly NetworkVariable<ulong> hunter = new();
    readonly NetworkVariable<int> tasksDone = new();
    // seconds each round's Pigeon took to kill; -1 not played yet, a round the Seagull won is float.MaxValue
    readonly NetworkVariable<float> kill1 = new(-1), kill2 = new(-1);

    public bool Playing => IsSpawned && phase.Value == Phase.Playing;
    public bool Started => IsSpawned && round.Value > 0;
    public ulong Hunter => hunter.Value;
    bool IAmHunter => hunter.Value == NetworkManager.LocalClientId;
    int AllTasks => (1 << tasks.Length) - 1;

    // this game's own bird
    GameObject rig;
    Health health;
    Weapon weapon;
    FoodPowers powers;
    HeadRide ride;
    Minimap map;
    PlayerInput input;
    Renderer[] rigLooks;

    bool menu = true;
    string status = "", joinCode = "", typedCode = "";
    float[] progress;
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
        rigLooks = rig.GetComponentsInChildren<Renderer>(true);
        map = FindFirstObjectByType<Minimap>();
        progress = new float[tasks.Length];
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
            ISession session = await MultiplayerService.Instance.CreateSessionAsync(new SessionOptions { MaxPlayers = 2 }.WithRelayNetwork());
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
            await MultiplayerService.Instance.JoinSessionByCodeAsync(typedCode.Trim().ToUpperInvariant());
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

    void CloseMenu()
    {
        menu = false;
        if (input) input.enabled = true;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    // a bird joined or left
    public void Refresh()
    {
        if (map) map.target = NetBird.Remote ? NetBird.Remote.transform : null;
    }

    // ---- the server runs the clock ----

    void Update()
    {
        if (menu)
        {
            if (input) input.enabled = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        if (!IsSpawned) return;
        if (IsServer) RunClock();
        if (round.Value != lastRound && phase.Value == Phase.Playing) { lastRound = round.Value; BeginRound(); }
        if (Playing) WatchOwnBird();
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
                if (timeLeft.Value <= 0) EndRound(tasksDone.Value == AllTasks ? float.MaxValue : roundLength);
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
        if (ride && ride.Mount) ride.Dismount();
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
        Array.Clear(progress, 0, progress.Length);
        respawnAt = -1;
        // the Seagull always knows where the Pigeon is (section 8: the hunted gets the better information)
        if (map) { map.Hide(); if (!IAmHunter) map.Reveal(roundLength); }
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
        for (int i = 0; i < tasks.Length; i++)
        {
            Task task = tasks[i];
            if ((tasksDone.Value & (1 << i)) != 0 || !task.spot) continue;
            Vector3 off = at - task.spot.position;
            bool there = new Vector2(off.x, off.z).magnitude <= task.radius && off.y >= task.height.x && off.y <= task.height.y;
            progress[i] = there ? progress[i] + Time.deltaTime : 0;
            if (progress[i] >= task.seconds) { progress[i] = 0; TaskDoneRpc(i); }
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
        if (!IsSpawned) return;

        string text;
        switch (phase.Value)
        {
            case Phase.Waiting:
                text = "Join code: <b>" + joinCode + "</b>\nWaiting for the other player...";
                break;
            case Phase.Playing:
                int left = tasks.Length - Bits(tasksDone.Value);
                text = "Round " + round.Value + "/2   " + Clock(timeLeft.Value) + "\n" +
                       (IAmHunter ? "You're the <b>PIGEON</b>: kill the Seagull.\nSeagull tasks left: " + left
                                  : "You're the <b>SEAGULL</b>: finish your tasks and survive. Shooting the Pigeon buys you time.\n" + TaskList());
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
        GUI.Box(new Rect(20, 20, 420, phase.Value == Phase.Playing && !IAmHunter ? 60 + tasks.Length * 24 + 40 : 110), text, box);
    }

    string TaskList()
    {
        string list = "";
        for (int i = 0; i < tasks.Length; i++)
        {
            bool done = (tasksDone.Value & (1 << i)) != 0;
            list += (done ? "[x] " : "[ ] ") + tasks[i].description +
                    (!done && progress[i] > 0 ? "  " + Mathf.CeilToInt(tasks[i].seconds - progress[i]) + "s" : "") + "\n";
        }
        return list;
    }

    string Result(float killTime) =>
        killTime < 0 ? "-" : killTime == float.MaxValue ? "the Seagull got away" : killTime >= roundLength ? "time up, tasks unfinished (a kill at " + Clock(roundLength) + ")" : "Pigeon killed in " + Clock(killTime);

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
