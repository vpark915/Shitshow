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

        // cover: a ring of blocks round the middle and scattered pillars, so sight lines break and the three-jump chain
        // has things to chain off
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI / 4;
            Vector3 at = new Vector3(Mathf.Sin(a) * 22, 0, Size / 2 + Mathf.Cos(a) * 22);
            Block(arena, "Cover", at + Vector3.up * 1.25f, new Vector3(i % 2 == 0 ? 6 : 2, 2.5f, 2), grey).localRotation = Quaternion.Euler(0, i * 45, 0);
        }
        foreach (Vector2 p in new[] { new Vector2(-30, 20), new Vector2(30, 20), new Vector2(-30, 70), new Vector2(30, 70), new Vector2(-8, 12), new Vector2(8, 78), new Vector2(-15, 45), new Vector2(15, 45) })
            Block(arena, "Pillar", new Vector3(p.x, 2, p.y), new Vector3(1.5f, 4, 1.5f), grey);
        for (int i = 0; i < 4; i++)
            Place("Assets/Prefabs/ParkBench.prefab", arena, new Vector3(i < 2 ? -8 : 8, 0, i % 2 == 0 ? 30 : 60), Quaternion.Euler(0, i < 2 ? 90 : -90, 0));

        // task spots (stand-ins until the real tasks are built)
        var tasks = new GameObject("Task Spots").transform;
        tasks.SetParent(gym, false);
        // fountain: "take a bath", in the open middle
        var fountain = new GameObject("Fountain").transform;
        fountain.SetParent(tasks, false);
        fountain.localPosition = new Vector3(0, 0, Size / 2);
        Shape(PrimitiveType.Cylinder, fountain, "Basin", new Vector3(0, 0.25f, 0), new Vector3(8, 0.25f, 8), grey);
        Object.DestroyImmediate(Shape(PrimitiveType.Cylinder, fountain, "Water", new Vector3(0, 0.3f, 0), new Vector3(7, 0.26f, 7), null).GetComponent<Collider>()); // wade into it
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

        // vendors along the east wall, facing in
        Transform vendors = gym.Find("Vendors");
        for (int i = 0; i < vendors.childCount; i++)
        {
            Transform v = vendors.GetChild(i);
            v.localPosition = new Vector3(Size / 2 - 6, 0, 12 + i * 15);
            v.localRotation = Quaternion.Euler(0, -90, 0);
        }

        // hunter at the south wall, hunted at the north, face to face across the fountain
        Transform player = Object.FindFirstObjectByType<FoodPowers>().transform;
        player.position = gym.TransformPoint(new Vector3(0, 0.05f, 4));
        player.rotation = gym.rotation;
        Transform seagull = GameObject.Find("Seagulls").transform.GetChild(0);
        seagull.position = gym.TransformPoint(new Vector3(0, 0, Size - 4));
        seagull.rotation = gym.rotation * Quaternion.Euler(0, 180, 0);

        // people: spawned in the south-west quarter, joggers lap just inside the walls (clear of the vendors on the east)
        GameObject.Find("NPCs").transform.position = gym.TransformPoint(new Vector3(-12, 0, 12));
        Transform jog = GameObject.Find("JogRoute").transform;
        Vector2[] corners = { new Vector2(-38, 6), new Vector2(-38, Size - 6), new Vector2(33, Size - 6), new Vector2(33, 6) };
        for (int i = 0; i < jog.childCount && i < corners.Length; i++)
            jog.GetChild(i).position = gym.TransformPoint(new Vector3(corners[i].x, -0.1f, corners[i].y));

        // the minimap covers just the arena
        Minimap map = Object.FindFirstObjectByType<Minimap>();
        if (map) map.area = new Bounds(gym.TransformPoint(new Vector3(0, 0, Size / 2)), new Vector3(Size, 10, Size));

        // the people walk on the gym's NavMesh: bake it for the new layout, into this scene's own asset
        NavMeshSurface walk = Object.FindFirstObjectByType<NavMeshSurface>();
        if (walk)
        {
            walk.BuildNavMesh();
            if (!AssetDatabase.IsValidFolder("Assets/Scenes/HuntGym")) AssetDatabase.CreateFolder("Assets/Scenes", "HuntGym");
            AssetDatabase.DeleteAsset("Assets/Scenes/HuntGym/NavMesh.asset");
            AssetDatabase.CreateAsset(walk.navMeshData, "Assets/Scenes/HuntGym/NavMesh.asset");
        }

        SetUpOnline();
        EditorSceneManager.SaveScene(scene);
        var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (!list.Exists(s => s.path == ScenePath)) { list.Add(new EditorBuildSettingsScene(ScenePath, true)); EditorBuildSettings.scenes = list.ToArray(); }
        Debug.Log("Built " + ScenePath);
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
            new HuntMatch.Task { name = "Bath", description = "Take a bath: stand in the fountain", spot = tasks.Find("Fountain"), radius = 3.5f, height = new Vector2(-1, 1.5f), seconds = 5 },
            new HuntMatch.Task { name = "Chess", description = "Ruin a chess game: stand on the chess table", spot = GameObject.Find("ChessTables").transform.GetChild(0), radius = 1.5f, height = new Vector2(0.5f, 2), seconds = 3 },
            new HuntMatch.Task { name = "Arch", description = "Perch on top of the arch", spot = tasks.Find("Arch"), radius = 5, height = new Vector2(9.5f, 13), seconds = 3 },
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

    static void Place(string prefab, Transform parent, Vector3 at, Quaternion facing)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefab);
        if (!asset) return;
        var g = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
        g.transform.SetLocalPositionAndRotation(at, facing);
    }
}
