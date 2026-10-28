using Unity.AI.Navigation;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Builds Scenes/HuntGym: a walled square arena for trying hunter (Pigeon) against hunted (Seagull) away from the park.
// It starts as a copy of MechanicsGym, so the pigeon, camera, HUD, minimap, NPCs, flocks, vendors, chess table and the
// seagull come along already wired up; the tutorial stations go, and walls, cover and stand-ins for the design doc's
// first three Seagull task spots (section 18: fountain, chess table, arch) go up. Run again to rebuild it from scratch.
static class HuntGym
{
    const string Source = "Assets/Scenes/MechanicsGym.unity";
    const string ScenePath = "Assets/Scenes/HuntGym.unity";
    const float Size = 90f;      // arena side, metres
    const float WallHeight = 4f;

    [MenuItem("Tools/Wash Square/Build Hunt Gym")]
    static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) &&
            !EditorUtility.DisplayDialog("Hunt Gym", ScenePath + " already exists. Rebuild it from MechanicsGym?", "Rebuild", "Cancel")) return;
        AssetDatabase.DeleteAsset(ScenePath);
        if (!AssetDatabase.CopyAsset(Source, ScenePath)) { Debug.LogError("Couldn't copy " + Source); return; }
        var scene = EditorSceneManager.OpenScene(ScenePath);

        Transform gym = GameObject.Find("Gym").transform; // everything is laid out in the gym's own space, far from the park
        Object.DestroyImmediate(gym.Find("Stations").gameObject); // signs, pedestals, steps and the food on them

        // floor: one square, the south edge at z 0
        Transform floor = gym.Find("Floor");
        floor.localPosition = new Vector3(0, -0.5f, Size / 2);
        floor.localScale = new Vector3(Size, 1, Size);
        Material grey = floor.GetComponent<Renderer>().sharedMaterial;

        var arena = new GameObject("Arena").transform;
        arena.SetParent(gym, false);

        // walls all round, so neither bird can leave
        for (int side = 0; side < 4; side++)
        {
            bool alongX = side < 2;
            float edge = side % 2 == 0 ? 0 : Size;
            Vector3 at = alongX ? new Vector3(0, WallHeight / 2, edge) : new Vector3(edge - Size / 2, WallHeight / 2, Size / 2);
            Block(arena, "Wall", at, alongX ? new Vector3(Size, WallHeight, 1) : new Vector3(1, WallHeight, Size), grey);
        }


        // task spots (stand-ins until the real tasks are built)
        var tasks = new GameObject("Task Spots").transform;
        tasks.SetParent(gym, false);
        // fountain: "take a bath", in the open middle
        var fountain = new GameObject("Fountain").transform;
        fountain.SetParent(tasks, false);
        fountain.localPosition = new Vector3(0, 0, Size / 2);
        Shape(PrimitiveType.Cylinder, fountain, "Basin", new Vector3(0, 0.25f, 0), new Vector3(8, 0.25f, 8), grey);
        Transform water = Shape(PrimitiveType.Cylinder, fountain, "Water", new Vector3(0, 0.3f, 0), new Vector3(7, 0.26f, 7), null);
        Object.DestroyImmediate(water.GetComponent<Collider>()); // wade into it
        WaterSplashes splashes = water.gameObject.AddComponent<WaterSplashes>(); // mist where anything wades
        splashes.spoutMistRate = splashes.plumeRate = 0; // no spout here
        // arch: "perch on the arch", against the north wall
        var arch = new GameObject("Arch").transform;
        arch.SetParent(tasks, false);
        arch.localPosition = new Vector3(0, 0, Size - 8);
        Block(arch, "Leg", new Vector3(-4, 4, 0), new Vector3(2, 8, 2), grey);
        Block(arch, "Leg", new Vector3(4, 4, 0), new Vector3(2, 8, 2), grey);
        Block(arch, "Top", new Vector3(0, 9, 0), new Vector3(10, 2, 2), grey);
        // chess table: "ruin a chess game", the one MechanicsGym already has, moved into the west half
        Transform chess = GameObject.Find("ChessTables").transform.GetChild(0);
        chess.position = gym.TransformPoint(new Vector3(-25, 0, Size / 2));

        // MechanicsGym's vendors go; Scatter puts up as many as the settings say
        Transform vendors = gym.Find("Vendors");
        for (int i = vendors.childCount - 1; i >= 0; i--) Object.DestroyImmediate(vendors.GetChild(i).gameObject);
        Scatter(gym, grey);

        // hunter at the south wall, hunted at the north, face to face across the fountain
        Transform player = Object.FindFirstObjectByType<FoodPowers>().transform;
        player.position = gym.TransformPoint(new Vector3(0, 0.05f, 4));
        player.rotation = gym.rotation;
        Transform seagull = GameObject.Find("Seagulls").transform.GetChild(0);
        seagull.position = gym.TransformPoint(new Vector3(0, 0, Size - 4));
        seagull.rotation = gym.rotation * Quaternion.Euler(0, 180, 0);

        // people: spawned in the south-west quarter (Scatter sets the joggers' lap)
        GameObject.Find("NPCs").transform.position = gym.TransformPoint(new Vector3(-12, 0, 12));

        // the minimap covers just the arena
        Minimap map = Object.FindFirstObjectByType<Minimap>();
        if (map) map.area = new Bounds(gym.TransformPoint(new Vector3(0, 0, Size / 2)), new Vector3(Size, 10, Size));

        BakeNavMesh();
        SetUpOnline();
        EditorSceneManager.SaveScene(scene);
        var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (!list.Exists(s => s.path == ScenePath)) { list.Add(new EditorBuildSettingsScene(ScenePath, true)); EditorBuildSettings.scenes = list.ToArray(); }
        Debug.Log("Built " + ScenePath);
    }

    // new cover, vendors, stray food and people in the Hunt Gym from HuntGymSettings, keeping everything else
    [MenuItem("Tools/Wash Square/Reshuffle Hunt Gym Cover")]
    internal static void Reshuffle()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath);
        }
        Transform gym = GameObject.Find("Gym").transform;
        foreach (string group in new[] { "Arena", "Vendors", "Stray Food" })
        {
            Transform things = gym.Find(group);
            if (!things) continue;
            for (int i = things.childCount - 1; i >= 0; i--)
                if (things.GetChild(i).name != "Wall") Object.DestroyImmediate(things.GetChild(i).gameObject);
        }
        Scatter(gym, gym.Find("Arena/Wall").GetComponent<Renderer>().sharedMaterial);
        BakeNavMesh();
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
    }

    // places (gym space, x across, z from the south wall) cover must keep clear of, and how far: the spawns, the task
    // spots and the joggers' lane inside the walls
    static readonly (Vector2 at, float clear)[] KeepClear =
    {
        (new Vector2(0, 4), 8), (new Vector2(0, Size - 4), 8),               // Pigeon and Seagull spawns
        (new Vector2(0, Size / 2), 8), (new Vector2(0, Size - 8), 7),        // fountain, arch
        (new Vector2(-25, Size / 2), 4),                                      // chess table
    };

    // everything HuntGymSettings says, spread over the whole arena at random spots kept apart (so no clumps and no big
    // empty patches): carts and trucks (under Gym/Vendors, so their food is bought), then cover (low walls, jump-height
    // blocks for the three-jump chain, pillars, park benches), then free food lying about (under Gym/Stray Food) and trash
    // cans (under Gym/Arena, so a reshuffle replaces them). Each
    // thing claims some room round it, and no two things' rooms overlap. Also sets how many people and bird flocks the gym spawns.
    static void Scatter(Transform gym, Material look)
    {
        const float Edge = 10f, VendorRoom = 10f, FoodRoom = 3f; // Edge keeps the joggers' lane by the walls free
        HuntGymSettings settings = HuntGymSettings.Load();
        int seed = settings.seed != 0 ? settings.seed : System.Environment.TickCount & 0xFFFF;
        var random = new System.Random(seed);
        float Range(float low, float high) => low + (float)random.NextDouble() * (high - low);
        Quaternion Turn() => Quaternion.Euler(0, Range(0, 360), 0);
        var placed = new System.Collections.Generic.List<(Vector2 at, float room)>();

        // up to `count` free spots each needing `room` metres (a truck needs more than a bagel), handed to `put`
        int Fill(int count, float room, float inset, System.Action<Vector2> put)
        {
            int made = 0;
            for (int tries = 0; tries < 50 * count && made < count; tries++)
            {
                var p = new Vector2(Range(-Size / 2 + inset, Size / 2 - inset), Range(inset, Size - inset));
                bool ok = true;
                foreach (var (at, clear) in KeepClear) ok &= (p - at).magnitude >= clear + room / 2;
                foreach (var (at, other) in placed) ok &= (p - at).magnitude >= (room + other) / 2;
                if (!ok) continue;
                placed.Add((p, room));
                put(p);
                made++;
            }
            if (made < count) Debug.LogWarning("Hunt Gym: only " + made + " of " + count + " fit; lower the spacing or the other amounts");
            return made;
        }

        // every prefab as many times as its slider says, shuffled, so if not all fit it's random which are left out
        System.Collections.Generic.List<string> Deal((string prefab, int count)[] kinds)
        {
            var deck = new System.Collections.Generic.List<string>();
            foreach (var (prefab, count) in kinds)
                for (int i = 0; i < count; i++) deck.Add("Assets/Prefabs/" + prefab + ".prefab");
            for (int i = deck.Count - 1; i > 0; i--) { int j = random.Next(i + 1); (deck[i], deck[j]) = (deck[j], deck[i]); }
            return deck;
        }

        Transform vendors = gym.Find("Vendors");
        var vendorDeck = Deal(settings.Vendors);
        int vendorsMade = Fill(vendorDeck.Count, VendorRoom, Edge + 4, p =>
            Place(vendorDeck[vendors.childCount], vendors, new Vector3(p.x, 0, p.y), Turn()));

        Transform arena = gym.Find("Arena");
        int[] weights = { settings.lowWalls, settings.blocks, settings.pillars, settings.benches };
        int total = weights[0] + weights[1] + weights[2] + weights[3];
        int coverMade = total == 0 ? 0 : Fill(settings.cover, settings.spacing, Edge, p =>
        {
            int kind = 0;
            for (int roll = random.Next(total); roll >= weights[kind]; kind++) roll -= weights[kind];
            if (kind == 0) Block(arena, "Cover", new Vector3(p.x, 1.25f, p.y), new Vector3(Range(4, 7), 2.5f, 1), look).localRotation = Turn();
            else if (kind == 1) Block(arena, "Cover", new Vector3(p.x, 1.25f, p.y), new Vector3(2, 2.5f, 2), look).localRotation = Turn();
            else if (kind == 2) Block(arena, "Pillar", new Vector3(p.x, 2, p.y), new Vector3(1.5f, 4, 1.5f), look);
            else Place("Assets/Prefabs/ParkBench.prefab", arena, new Vector3(p.x, 0, p.y), Turn());
        });

        var carDeck = Deal(settings.Cars);
        int carsMade = 0;
        Fill(carDeck.Count, 7, Edge, p => Place(carDeck[carsMade++], arena, new Vector3(p.x, 0, p.y), Turn()));
        int bikesMade = Fill(settings.citiBikes, 3, Edge, p => Place("Assets/Prefabs/CitiBike.prefab", arena, new Vector3(p.x, 0, p.y), Turn()));
        // a station is a row of docks with room for the bikes in front of them, so it needs its length plus a bike's
        float stationRoom = settings.docksPerStation * CitiBikePrefab.DockSpacing + 3;
        int stationsMade = Fill(settings.bikeStations, stationRoom, Edge, p =>
            CitiBikePrefab.Station(arena, arena.TransformPoint(new Vector3(p.x, 0, p.y)), arena.rotation * Turn(), settings.docksPerStation, settings.dockedBikes, random));

        Transform strays = gym.Find("Stray Food");
        if (!strays) { strays = new GameObject("Stray Food").transform; strays.SetParent(gym, false); }
        var foodDeck = Deal(settings.Food);
        int foodMade = Fill(foodDeck.Count, FoodRoom, Edge, p =>
        {
            Transform food = Place(foodDeck[strays.childCount], strays, new Vector3(p.x, 0, p.y), Turn());
            Renderer[] looks = food ? food.GetComponentsInChildren<Renderer>() : new Renderer[0];
            if (looks.Length == 0) return;
            Bounds b = looks[0].bounds;
            foreach (Renderer r in looks) b.Encapsulate(r.bounds);
            food.position += Vector3.up * (gym.position.y - b.min.y + 0.01f); // sitting on the floor, not in it
        });

        int cansMade = Fill(settings.trashCans, FoodRoom, Edge, p => Place("Assets/Prefabs/TrashCan.prefab", arena, new Vector3(p.x, 0, p.y), Turn()));

        Debug.Log("Hunt Gym: " + vendorsMade + " vendors, " + coverMade + " cover, " + carsMade + " cars, " + bikesMade + " bikes, " + stationsMade + " bike stations, " + foodMade + " stray food, " + cansMade + " trash cans, seed " + seed +
                  " (put it in HuntGymSettings to keep this layout)");

        // joggers lap 6 m inside the walls, in the lane Edge keeps free
        Transform jog = GameObject.Find("JogRoute").transform;
        Vector2[] corners = { new Vector2(-Size / 2 + 6, 6), new Vector2(-Size / 2 + 6, Size - 6), new Vector2(Size / 2 - 6, Size - 6), new Vector2(Size / 2 - 6, 6) };
        for (int i = 0; i < jog.childCount && i < corners.Length; i++)
            jog.GetChild(i).position = gym.TransformPoint(new Vector3(corners[i].x, -0.1f, corners[i].y));

        BirdFlocks flocks = Object.FindFirstObjectByType<BirdFlocks>();
        if (flocks)
        {
            flocks.pigeonFlocks = settings.pigeonFlocks;
            flocks.seagullFlocks = settings.seagullFlocks;
            flocks.flockSize = new Vector2Int(settings.flockMin, Mathf.Max(settings.flockMin, settings.flockMax));
            EditorUtility.SetDirty(flocks);
        }

        NpcSpawner people = Object.FindFirstObjectByType<NpcSpawner>();
        if (!people) return;
        people.walkers = settings.walkers;
        people.joggers = settings.joggers;
        people.idlers = settings.idlers;
        people.officers = settings.officers;
        people.guards = settings.guards;
        people.foodDropChance = settings.foodDropChance;
        EditorUtility.SetDirty(people);
    }

    // the people walk on the scene's NavMesh: bake it for the layout, into this scene's own asset (Scenes/<scene>/NavMesh)
    internal static void BakeNavMesh(string scene = "HuntGym")
    {
        NavMeshSurface walk = Object.FindFirstObjectByType<NavMeshSurface>();
        if (!walk) return;
        walk.BuildNavMesh();
        string Path = "Assets/Scenes/" + scene + "/NavMesh.asset";
        var saved = AssetDatabase.LoadAssetAtPath<UnityEngine.AI.NavMeshData>(Path);
        if (!saved)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Scenes/" + scene)) AssetDatabase.CreateFolder("Assets/Scenes", scene);
            AssetDatabase.CreateAsset(walk.navMeshData, Path);
            return;
        }
        // Unity won't create an asset over the old bake, so the new bake is copied into it
        EditorUtility.CopySerialized(walk.navMeshData, saved);
        saved.name = "NavMesh";
        walk.RemoveData();
        walk.navMeshData = saved;
        walk.AddData();
        EditorUtility.SetDirty(saved);
        EditorUtility.SetDirty(walk);
        AssetDatabase.SaveAssetIfDirty(saved);
    }

    // the online match (HuntMatch) in the open Hunt Gym: the NetBird player prefab, a NetworkManager on Unity Transport
    // (Relay fills in the addresses), the match with its spawns and the design doc's three first Seagull tasks
    [MenuItem("Tools/Wash Square/Set Up Hunt Gym Online")]
    static void SetUpOnline()
    {
        Transform gym = GameObject.Find("Gym").transform;
        FoodPowers rig = Object.FindFirstObjectByType<FoodPowers>();

        // the stand-in bird, sized like the rig
        var body = rig.GetComponent<CharacterController>();
        Transform rigModel = rig.transform.Find("Pigeon");
        var bird = new GameObject("NetBird");
        bird.AddComponent<NetworkObject>();
        var moves = bird.AddComponent<NetworkTransform>();
        moves.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
        moves.SyncRotAngleX = moves.SyncRotAngleZ = false;
        moves.SyncScaleX = moves.SyncScaleY = moves.SyncScaleZ = false;
        var hitbox = bird.AddComponent<CapsuleCollider>();
        hitbox.center = body.center;
        hitbox.radius = body.radius;
        hitbox.height = body.height;
        bird.AddComponent<Health>().maxHealth = 1e6f; // only flashes and passes hits on (NetBird); the owner's rig counts them
        var net = bird.AddComponent<NetBird>();
        net.pigeonModel = Model("Assets/Quirky Series/Ultimate Pack Vol.1/Mega Pack Vol.1/Pets Vol.1/Prefabs/Single LODs/Pigeon_LOD1.prefab", bird.transform, rigModel);
        net.seagullModel = Model("Assets/Quirky Series/Ultimate Pack Vol.1/Mega Pack Vol.4/Sea Vol.2/Prefabs/Single LODs/Seagull_LOD1.prefab", bird.transform, rigModel);
        GameObject birdPrefab = PrefabUtility.SaveAsPrefabAsset(bird, "Assets/Prefabs/NetBird.prefab");
        Object.DestroyImmediate(bird);

        // connections
        NetworkManager manager = Object.FindFirstObjectByType<NetworkManager>();
        if (!manager) manager = new GameObject("NetworkManager").AddComponent<NetworkManager>();
        if (!manager.TryGetComponent(out UnityTransport transport)) transport = manager.gameObject.AddComponent<UnityTransport>();
        if (manager.NetworkConfig == null) manager.NetworkConfig = new NetworkConfig();
        manager.NetworkConfig.NetworkTransport = transport;
        manager.NetworkConfig.PlayerPrefab = birdPrefab;
        manager.NetworkConfig.EnableSceneManagement = true; // needed to match up objects placed in the scene (HuntMatch)

        // the match
        HuntMatch match = Object.FindFirstObjectByType<HuntMatch>();
        if (!match)
        {
            var go = new GameObject("HuntMatch");
            go.AddComponent<NetworkObject>();
            match = go.AddComponent<HuntMatch>();
        }
        Transform spawns = gym.Find("Spawns") ?? new GameObject("Spawns").transform;
        spawns.SetParent(gym, false);
        match.hunterSpawn = Spawn(spawns, "Hunter", new Vector3(0, 0.05f, 4), 0);
        match.huntedSpawn = Spawn(spawns, "Hunted", new Vector3(0, 0.05f, Size - 4), 180);
        Transform tasks = gym.Find("Task Spots");
        match.tasks = new[]
        {
            new HuntMatch.Task { name = "Bath", description = "Take a bath: stand in the fountain", spot = tasks.Find("Fountain"), radius = 3.5f, height = new Vector2(-1, 1.5f), amount = 5 },
            new HuntMatch.Task { name = "Chess", description = "Ruin a chess game: stand on the chess table", spot = GameObject.Find("ChessTables").transform.GetChild(0), radius = 1.5f, height = new Vector2(0.5f, 2), amount = 3 },
            new HuntMatch.Task { name = "Arch", description = "Perch on top of the arch", spot = tasks.Find("Arch"), radius = 5, height = new Vector2(9.5f, 13), amount = 3 },
        };
        match.dummySeagull = GameObject.Find("Seagulls").transform.GetChild(0).gameObject;
        EditorUtility.SetDirty(match);
        EditorUtility.SetDirty(manager);
        EditorSceneManager.MarkSceneDirty(match.gameObject.scene);
    }

    static GameObject Model(string prefab, Transform parent, Transform like)
    {
        var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefab), parent);
        model.transform.SetLocalPositionAndRotation(like.localPosition, like.localRotation);
        model.transform.localScale = like.localScale;
        return model;
    }

    static Transform Spawn(Transform parent, string name, Vector3 at, float facing)
    {
        Transform spot = parent.Find(name) ?? new GameObject(name).transform;
        spot.SetParent(parent, false);
        spot.SetLocalPositionAndRotation(at, Quaternion.Euler(0, facing, 0));
        return spot;
    }

    static Transform Block(Transform parent, string name, Vector3 at, Vector3 size, Material look) =>
        Shape(PrimitiveType.Cube, parent, name, at, size, look);

    static Transform Shape(PrimitiveType type, Transform parent, string name, Vector3 at, Vector3 size, Material look)
    {
        GameObject g = GameObject.CreatePrimitive(type);
        g.name = name;
        g.transform.SetParent(parent, false);
        g.transform.localPosition = at;
        g.transform.localScale = size;
        g.isStatic = true;
        if (look) g.GetComponent<Renderer>().sharedMaterial = look;
        return g.transform;
    }

    static Transform Place(string prefab, Transform parent, Vector3 at, Quaternion facing)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefab);
        if (!asset) { Debug.LogWarning("Hunt Gym: no " + prefab); return null; }
        var g = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
        g.transform.SetLocalPositionAndRotation(at, facing);
        return g.transform;
    }
}
