using System.Collections.Generic;
using System.Linq;
using Unity.AI.Navigation;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

// Scenes/WashSquareHunt: the whole Washington Square map with what the Hunt Gym has.
// Tools > Wash Square > Build Wash Square Hunt makes it as a copy of WashSquareGym (which isn't touched), adding:
// - the online match (HuntMatch on Relay, NetBird players), the Pigeon's spawn where the rig stands and the Seagull's
//   across the fountain from it, one offline stand-in seagull (the map's others are switched off)
// - the three Seagull tasks on the real landmarks: bath in the fountain, the top of the arch, the chess table nearest
//   the fountain
// then lays everything out as Reshuffle does. Running it again starts over from WashSquareGym.
//
// Reshuffle (Wash Square Hunt Settings > Apply, or Tools > Wash Square > Reshuffle Wash Square Hunt) throws away every
// cart, truck, car, Citi Bike, piece of park food and trash can and puts up as many as WashSquareHuntSettings says, each only
// where its kind belongs:
// - trucks and Citi Bike stations: in the parking lanes along the park's four sides, every corner on the road
//   (not the pavement); trucks with their hatch to the park, stations with the bikes standing out into the street
// - carts: on the park's paving, backs to a path edge, with room to queue in front
// - loose Citi Bikes: on the park's paving
// - pizza on chess tables, bagels and bread on bench seats
// - trash cans (TrashCan): at the end of a bench, on the paving (Tools > Wash Square > Place Wash Square Trash Cans
//   redoes just them, leaving the rest of the layout; they carve their own way round in the walkable area)
// - every car as traffic (TrafficCar): driving one way round the park in the outer lane, none parked
// Nothing goes where it would overlap anything solid (benches, lamps, other vendors...), on a task spot or a spawn.
// It also sets the people and flocks, then bakes the scene's own walkable area (Scenes/WashSquareHunt/NavMesh) the way
// WashSquareGym's is (NpcSetup: people keep to paths and pavements, off the grass and the road), round the vendors, and saves.
static class WashSquareHunt
{
    const string Source = "Assets/Scenes/WashSquareGym.unity";
    const string Name = "WashSquareHunt";
    const string ScenePath = "Assets/Scenes/" + Name + ".unity";
    const float LaneOut = 4.8f;   // parking lane centre, metres out from the park's edge (the kerb is 2.75-3.25 m out)
    const float TruckOut = 1.2f;  // trucks are wider (and their hatch side juts out): this much further out than cars
    const float DriveOut = 9f;    // the driving lane, metres out from the park's edge (outside the trucks and bike stations)
    const float LaneEnds = 12f;   // kept clear at each end of a lane, for the corners
    const float Queue = 2.5f;     // metres of paving a cart keeps clear in front of it
    // the blockout's ground pieces (what things stand on, not in): the park's paths, lawns, beds, gravel..., the
    // pavements and the roads
    static bool IsGround(string name) => name.StartsWith("WSP_Park_") || name.StartsWith("WSP_Road") || name == "WSP_Sidewalk";
    static readonly string[] PavedPieces = { "WSP_Park_Paths", "WSP_Sidewalk" }, RoadPieces = { "WSP_Road", "WSP_Road_Cobble" };

    [MenuItem("Tools/Wash Square/Build Wash Square Hunt")]
    static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) &&
            !EditorUtility.DisplayDialog("Wash Square Hunt", ScenePath + " already exists. Rebuild it from WashSquareGym?", "Rebuild", "Cancel")) return;
        AssetDatabase.DeleteAsset(ScenePath);
        if (!AssetDatabase.CopyAsset(Source, ScenePath)) { Debug.LogError("Couldn't copy " + Source); return; }
        var scene = EditorSceneManager.OpenScene(ScenePath);

        Bounds fountain = Look(GameObject.Find("WSP_Fountain")), arch = Look(GameObject.Find("WSP_Arch"));

        // tasks, on the real landmarks
        var taskSpots = new GameObject("Seagull Tasks").transform;
        Transform bath = Spot(taskSpots, "Fountain", new Vector3(fountain.center.x, fountain.min.y, fountain.center.z));
        Transform archTop = Spot(taskSpots, "Arch", new Vector3(arch.center.x, arch.max.y, arch.center.z));
        Transform chess = null;
        foreach (Transform table in GameObject.Find("ChessTables").transform)
            if (!chess || Flat(table.position - fountain.center) < Flat(chess.position - fountain.center)) chess = table;

        // spawns: the Pigeon where the rig stands, the Seagull the same distance out on the far side of the fountain
        Transform rig = Object.FindFirstObjectByType<FoodPowers>().transform;
        Vector3 across = 2 * new Vector3(fountain.center.x, rig.position.y, fountain.center.z) - rig.position;
        if (NavMesh.SamplePosition(across, out NavMeshHit far, 20f, NavMesh.AllAreas)) across = far.position;
        var spawns = new GameObject("Spawns").transform;
        Transform hunter = Spot(spawns, "Hunter", rig.position), hunted = Spot(spawns, "Hunted", across + Vector3.up * 0.05f);
        hunter.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(hunted.position - hunter.position, Vector3.up));
        hunted.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(hunter.position - hunted.position, Vector3.up));

        // one offline stand-in seagull at the Seagull's spawn, like the Hunt Gym; the match hides it once online
        Transform seagulls = GameObject.Find("Seagulls").transform;
        GameObject dummy = seagulls.GetChild(0).gameObject;
        dummy.transform.SetPositionAndRotation(hunted.position, hunted.rotation);
        for (int i = 1; i < seagulls.childCount; i++) seagulls.GetChild(i).gameObject.SetActive(false);

        // online
        var manager = new GameObject("NetworkManager").AddComponent<NetworkManager>();
        var transport = manager.gameObject.AddComponent<UnityTransport>();
        manager.NetworkConfig = new NetworkConfig
        {
            NetworkTransport = transport,
            PlayerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/NetBird.prefab"),
            EnableSceneManagement = true, // matches up objects placed in the scene (HuntMatch)
        };
        var prefabs = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>("Assets/DefaultNetworkPrefabs.asset");
        if (prefabs) manager.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(prefabs);

        var matchObject = new GameObject("HuntMatch");
        matchObject.AddComponent<NetworkObject>();
        var match = matchObject.AddComponent<HuntMatch>();
        match.hunterSpawn = hunter;
        match.huntedSpawn = hunted;
        match.dummySeagull = dummy;
        float bathRadius = Mathf.Min(fountain.extents.x, fountain.extents.z) * 0.75f; // inside the rim
        match.tasks = new[]
        {
            new HuntMatch.Task { name = "Bath", description = "Take a bath: stand in the fountain", spot = bath, radius = bathRadius, height = new Vector2(-1, fountain.size.y + 1.5f), amount = 5 },
            new HuntMatch.Task { name = "Chess", description = "Ruin a chess game: stand on the chess table by the fountain", spot = chess, radius = 1.5f, height = new Vector2(0.5f, 2), amount = 3 },
            new HuntMatch.Task { name = "Arch", description = "Perch on top of the arch", spot = archTop, radius = Mathf.Max(arch.extents.x, arch.extents.z), height = new Vector2(-1, 3), amount = 3 },
        };

        new GameObject("Vehicles");
        WithGround(Lay);
        // as the scene is when played (any ground switched off stays off), or people walk on what isn't there
        NpcSetup.Bake("Assets/Scenes/" + Name + "/NavMesh.asset", Solid()); // paths, pavements, lawns and road told apart, as WashSquareGym's
        EditorSceneManager.SaveScene(scene);
        var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (!list.Exists(s => s.path == ScenePath)) { list.Add(new EditorBuildSettingsScene(ScenePath, true)); EditorBuildSettings.scenes = list.ToArray(); }
        Debug.Log("Built " + ScenePath + ": tasks at the fountain " + bath.position + ", arch top " + archTop.position + ", chess table " + chess.name);
    }

    // cut out of the NPCs' NavMesh: carts, benches and chess tables. Left in, the mesh ran under the benches and people
    // walked through them, stood up onto the seats by their feet-on-the-ground check, and floated off the far side
    static Transform[] Solid() => new[] { "FoodVendors", "Benches", "ChessTables" }.Select(n => GameObject.Find(n)).Where(g => g).Select(g => g.transform).ToArray();

    [MenuItem("Tools/Wash Square/Reshuffle Wash Square Hunt")]
    internal static void Reshuffle()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
        {
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath)) { Build(); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath);
        }
        WithGround(Lay);
        NpcSetup.Bake("Assets/Scenes/" + Name + "/NavMesh.asset", Solid());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
    }

    // ---- the layout

    // A ground piece may be switched off in the scene (the terrain shows over the lawns), and a switched-off collider
    // can't be hit, but the ground's colliders are how paving is told from lawn and road here and in the bake. So
    // they're all switched on for the layout and the bake, then put back as they were.
    static readonly HashSet<string> hidden = new HashSet<string>(); // ground pieces switched off in the scene (the terrain shows instead)

    static void WithGround(System.Action work)
    {
        hidden.Clear();
        Transform blockout = GameObject.Find("washingtonSquareBlockout").transform;
        var was = new List<(GameObject piece, bool on)>();
        foreach (Transform piece in blockout)
            if (IsGround(piece.name))
            {
                was.Add((piece.gameObject, piece.gameObject.activeSelf));
                if (!piece.gameObject.activeSelf) hidden.Add(piece.name);
                piece.gameObject.SetActive(true);
            }
        Physics.SyncTransforms();
        try { work(); }
        finally { foreach (var (piece, on) in was) piece.SetActive(on); }
    }

    static System.Random random;
    static List<Collider> ground; // every ground piece but the park's base slab, which lies under all the rest
    static Collider parkBase;
    static List<(Vector3 at, float radius)> keepClear;
    static Dictionary<string, int> refused; // why things weren't kept, logged when something comes up short

    static float Range(float low, float high) => low + (float)random.NextDouble() * (high - low);

    // the layout's random numbers, what counts as ground, and the spots kept clear; the seed it used
    static int Ready(WashSquareHuntSettings settings)
    {
        int seed = settings.seed != 0 ? settings.seed : System.Environment.TickCount & 0xFFFF;
        random = new System.Random(seed);
        refused = new Dictionary<string, int>();

        // what's under a spot is whichever ground piece is on top there: paths, lawn, bed, pavement, road...
        Transform blockout = GameObject.Find("washingtonSquareBlockout").transform;
        ground = new List<Collider>();
        parkBase = null;
        foreach (Transform piece in blockout)
            if (IsGround(piece.name) && piece.TryGetComponent(out Collider c)) { if (piece.name == "WSP_Park_Base") parkBase = c; else ground.Add(c); }

        // the match's spawns and task spots stay clear
        keepClear = new List<(Vector3, float)>();
        HuntMatch match = Object.FindFirstObjectByType<HuntMatch>();
        if (match)
        {
            keepClear.Add((match.hunterSpawn.position, 6));
            keepClear.Add((match.huntedSpawn.position, 6));
            foreach (HuntMatch.Task task in match.tasks) keepClear.Add((task.spot.position, task.radius + 2));
        }
        return seed;
    }

    static void Lay()
    {
        WashSquareHuntSettings settings = WashSquareHuntSettings.Load();
        Transform vendors = GameObject.Find("FoodVendors").transform, food = GameObject.Find("ParkFood").transform, vehicles = GameObject.Find("Vehicles").transform;
        foreach (Transform group in new[] { vendors, food, vehicles })
            for (int i = group.childCount - 1; i >= 0; i--) Object.DestroyImmediate(group.GetChild(i).gameObject);
        Physics.SyncTransforms();
        int seed = Ready(settings);
        Transform blockout = GameObject.Find("washingtonSquareBlockout").transform;
        HuntMatch match = Object.FindFirstObjectByType<HuntMatch>();

        Bounds park = Look(blockout.Find("WSP_Park_Base").gameObject);
        float y = park.min.y;
        var lanes = new (Vector3 a, Vector3 b, Vector3 outward)[]
        {
            (new Vector3(park.min.x + LaneEnds, y, park.max.z + LaneOut), new Vector3(park.max.x - LaneEnds, y, park.max.z + LaneOut), Vector3.forward),
            (new Vector3(park.min.x + LaneEnds, y, park.min.z - LaneOut), new Vector3(park.max.x - LaneEnds, y, park.min.z - LaneOut), Vector3.back),
            (new Vector3(park.min.x - LaneOut, y, park.min.z + LaneEnds), new Vector3(park.min.x - LaneOut, y, park.max.z - LaneEnds), Vector3.left),
            (new Vector3(park.max.x + LaneOut, y, park.min.z + LaneEnds), new Vector3(park.max.x + LaneOut, y, park.max.z - LaneEnds), Vector3.right),
        };
        float lanesLength = 0;
        foreach (var lane in lanes) lanesLength += Vector3.Distance(lane.a, lane.b);

        // up to `count` of something in the lanes: `make` puts one up at a spot along a lane, and it's kept only if it
        // stands wholly on the road and clear of everything
        int InLanes(int count, System.Func<Vector3, (Vector3 a, Vector3 b, Vector3 outward), GameObject> make)
        {
            int made = 0;
            for (int tries = 0; tries < 100 * count && made < count; tries++)
            {
                float along = Range(0, lanesLength);
                var lane = lanes[0];
                foreach (var l in lanes)
                {
                    lane = l;
                    float length = Vector3.Distance(l.a, l.b);
                    if (along <= length) break;
                    along -= length;
                }
                GameObject thing = make(Vector3.MoveTowards(lane.a, lane.b, along), lane);
                if (Keep(thing, Road)) made++;
            }
            return made;
        }

        // trucks, hatch (the side their food is on) to the park
        int trucks = 0;
        foreach (string prefab in Deal(settings.Trucks))
            trucks += InLanes(1, (at, lane) => Vendor(prefab, vendors, OnRoad(at + lane.outward * TruckOut), -lane.outward));
        // stations: docks along the kerb, bikes out into the street
        int stations = InLanes(settings.bikeStations, (at, lane) =>
            CitiBikePrefab.Station(vehicles, OnRoad(at - lane.outward * 0.8f), Quaternion.LookRotation(-lane.outward), settings.docksPerStation, settings.dockedBikes, random).gameObject);
        // traffic, every car (none parked): one way round the park in the driving lane, the park on the right; started apart along the loop
        Vector3[] loop =
        {
            OnRoad(new Vector3(park.min.x - DriveOut, y, park.max.z + DriveOut)), OnRoad(new Vector3(park.max.x + DriveOut, y, park.max.z + DriveOut)),
            OnRoad(new Vector3(park.max.x + DriveOut, y, park.min.z - DriveOut)), OnRoad(new Vector3(park.min.x - DriveOut, y, park.min.z - DriveOut)),
        };
        float loopLength = 0;
        for (int i = 0; i < loop.Length; i++) loopLength += Vector3.Distance(loop[i], loop[(i + 1) % loop.Length]);
        var moving = new List<Vector3>();
        var trafficDeck = Deal(settings.Cars);
        int traffic = 0;
        for (int tries = 0; tries < 50 * trafficDeck.Count && traffic < trafficDeck.Count; tries++)
        {
            float along = Range(0, loopLength);
            int leg = 0;
            while (leg < loop.Length - 1 && along > Vector3.Distance(loop[leg], loop[(leg + 1) % loop.Length])) { along -= Vector3.Distance(loop[leg], loop[(leg + 1) % loop.Length]); leg++; }
            Vector3 from = loop[leg], to = loop[(leg + 1) % loop.Length];
            Vector3 at = OnRoad(Vector3.MoveTowards(from, to, Mathf.Clamp(along, 10, Vector3.Distance(from, to) - 10))); // not mid-turn
            bool apart = true;
            foreach (Vector3 other in moving) apart &= Flat(at - other) > 15;
            if (!apart) continue;
            GameObject car = Put(trafficDeck[traffic], vehicles, at, Quaternion.LookRotation(to - from));
            if (!car) break;
            var driver = car.AddComponent<TrafficCar>();
            driver.route = loop;
            driver.cruise = settings.trafficSpeed * Range(0.85f, 1.15f);
            car.AddComponent<NavMeshModifier>().ignoreFromBuild = true; // it won't be there long: no hole in the walkable area
            if (!Keep(car, Road)) continue;
            car.name = "Traffic " + car.name;
            moving.Add(at);
            traffic++;
        }

        // carts against a path edge
        int carts = 0;
        foreach (string prefab in Deal(settings.Carts))
            for (int tries = 0; tries < 300; tries++)
                if (Cart(prefab, vendors, park)) { carts++; break; }

        // loose bikes on paving
        int bikes = 0;
        for (int tries = 0; tries < 100 * settings.citiBikes && bikes < settings.citiBikes; tries++)
        {
            var p = new Vector3(Range(park.min.x + 3, park.max.x - 3), 0, Range(park.min.z + 3, park.max.z - 3));
            if (!Paved(p, out Vector3 ground)) continue;
            if (Keep(Put("CitiBike", vehicles, ground, Quaternion.Euler(0, Range(0, 360), 0)), Paved)) bikes++;
        }

        // park food: pizza on chess tables (not the task's), bagels and bread on benches
        Transform chessTask = match && match.tasks.Length > 1 ? match.tasks[1].spot : null;
        var tables = new List<Transform>();
        foreach (Transform t in GameObject.Find("ChessTables").transform) if (t != chessTask) tables.Add(t);
        var benches = new List<Transform>();
        foreach (Transform b in GameObject.Find("Benches").transform) benches.Add(b);
        int pizza = OnTop("Pizza", settings.pizza, tables, food, 0.6f);
        int bagels = OnTop("Bagel", settings.bagels, benches, food, 0f), bread = OnTop("Bread", settings.bread, benches, food, 0f);
        int trash = TrashCans(settings.trashCans);

        BirdFlocks flocks = Object.FindFirstObjectByType<BirdFlocks>();
        if (flocks)
        {
            flocks.pigeonFlocks = settings.pigeonFlocks;
            flocks.seagullFlocks = settings.seagullFlocks;
            flocks.flockSize = new Vector2Int(settings.flockMin, Mathf.Max(settings.flockMin, settings.flockMax));
            EditorUtility.SetDirty(flocks);
        }
        NpcSpawner people = Object.FindFirstObjectByType<NpcSpawner>();
        if (people)
        {
            people.walkers = settings.walkers;
            people.joggers = settings.joggers;
            people.idlers = settings.idlers;
            people.officers = settings.officers;
            people.guards = settings.guards;
            people.foodDropChance = settings.foodDropChance;
            EditorUtility.SetDirty(people);
        }
        EditorSceneManager.MarkSceneDirty(vendors.gameObject.scene);

        if (carts < Deal(settings.Carts).Count || trucks < Deal(settings.Trucks).Count || stations < settings.bikeStations || traffic < trafficDeck.Count)
        {
            var why = new List<KeyValuePair<string, int>>(refused);
            why.Sort((a, b) => b.Value.CompareTo(a.Value));
            Debug.LogWarning("Wash Square Hunt: not everything fitted. Most common reasons a spot was turned down: "
                             + string.Join(", ", why.GetRange(0, Mathf.Min(8, why.Count)).ConvertAll(w => w.Key + " x" + w.Value)));
        }
        Debug.Log("Wash Square Hunt: " + carts + " carts, " + trucks + " trucks, " + stations + " bike stations, " + traffic + " traffic cars, " + bikes + " loose bikes, "
                  + pizza + " pizza, " + bagels + " bagels, " + bread + " bread, " + trash + " trash cans, seed " + seed + " (put it in Wash Square Hunt Settings to keep this layout)");
    }

    [MenuItem("Tools/Wash Square/Place Wash Square Trash Cans")]
    static void PlaceTrashCans()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath) { Debug.LogWarning("Open " + ScenePath + " first"); return; }
        WithGround(() =>
        {
            WashSquareHuntSettings settings = WashSquareHuntSettings.Load();
            int seed = Ready(settings);
            Debug.Log("Wash Square Hunt: " + TrashCans(settings.trashCans) + " trash cans, seed " + seed + " (save the scene to keep them)");
        });
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }

    // up to `count` trash cans (under TrashCans, the old ones thrown away), each at one end of a different bench, on
    // the paving and clear of everything. How many it put up
    static int TrashCans(int count)
    {
        GameObject group = GameObject.Find("TrashCans");
        if (!group) group = new GameObject("TrashCans");
        for (int i = group.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(group.transform.GetChild(i).gameObject);
        Physics.SyncTransforms();
        var benches = new List<Transform>();
        foreach (Transform b in GameObject.Find("Benches").transform) benches.Add(b);
        int made = 0;
        for (int tries = 0; tries < count * 5 && made < count && benches.Count > 0; tries++)
        {
            Transform bench = benches[random.Next(benches.Count)];
            benches.Remove(bench);
            Bounds b = Look(bench.gameObject);
            bool alongX = b.size.x > b.size.z; // its length (benches stand square to the paths, near enough)
            Vector3 end = (alongX ? Vector3.right * b.extents.x : Vector3.forward * b.extents.z) + (alongX ? Vector3.right : Vector3.forward) * 0.55f;
            if (!Paved(b.center + (random.Next(2) == 0 ? end : -end), out Vector3 floor)) continue;
            GameObject can = Put("TrashCan", group.transform, floor, Quaternion.Euler(0, Range(0, 360), 0));
            if (!Keep(can, Paved)) continue;
            can.name = "TrashCan_" + (++made).ToString("00");
        }
        return made;
    }

    // a cart at a random spot on the paving: its back to the nearest path edge (if one is within 4 m), all of it on
    // paving with room to queue in front, clear of everything. False if this spot didn't work
    static bool Cart(string prefab, Transform parent, Bounds park)
    {
        var p = new Vector3(Range(park.min.x + 3, park.max.x - 3), 0, Range(park.min.z + 3, park.max.z - 3));
        if (!Paved(p, out _)) return false;
        Vector3 back = Vector3.zero;
        float edge = 4f;
        for (int i = 0; i < 24; i++)
        {
            Vector3 way = Quaternion.Euler(0, i * 15f, 0) * Vector3.forward;
            for (float d = 0.25f; d < edge; d += 0.25f)
                if (!Paved(p + way * d, out _)) { edge = d; back = way; break; }
        }
        if (back == Vector3.zero) return false; // the middle of a plaza: carts stand at the edges

        GameObject cart = Vendor(prefab, parent, p, -back);
        // slide it so its back is just inside the edge
        Bounds local = LocalBounds(cart.transform);
        float halfDepth = Mathf.Abs(Vector3.Dot(Vector3.Scale(local.extents, cart.transform.lossyScale), Abs(cart.transform.InverseTransformDirection(back))));
        Vector3 middle = cart.transform.TransformPoint(local.center);
        Vector3 want = p + back * (edge - 0.2f - halfDepth);
        cart.transform.position += new Vector3(want.x - middle.x, 0, want.z - middle.z);
        if (Paved(cart.transform.position, out Vector3 ground)) cart.transform.position = new Vector3(cart.transform.position.x, ground.y, cart.transform.position.z);
        // and the queue in front has to be paving too
        Vector3 front = cart.transform.TransformPoint(local.center) - back * (halfDepth + Queue);
        if (!Paved(front, out _)) { Object.DestroyImmediate(cart); return false; }
        return Keep(cart, Paved);
    }

    // `count` of a food on top of things from `spots` (each used once), `spread` of the way from the middle to the edge
    static int OnTop(string prefab, int count, List<Transform> spots, Transform parent, float spread)
    {
        int made = 0;
        for (int tries = 0; tries < count * 5 && made < count && spots.Count > 0; tries++)
        {
            Transform spot = spots[random.Next(spots.Count)];
            spots.Remove(spot);
            Bounds b = Look(spot.gameObject);
            var from = new Vector3(b.center.x + b.extents.x * spread * Range(-1, 1), b.max.y + 1, b.center.z + b.extents.z * spread * Range(-1, 1));
            // the highest surface of this table or bench there (not a chess piece)
            float top = float.MinValue;
            foreach (RaycastHit hit in Physics.RaycastAll(from, Vector3.down, b.size.y + 2, ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider.transform.IsChildOf(spot) && !hit.collider.name.StartsWith("Piece_")) top = Mathf.Max(top, hit.point.y);
            if (top == float.MinValue) // no collider on the top (a chess table's model may have none): the top of how it looks, pieces aside
                foreach (Renderer look in spot.GetComponentsInChildren<Renderer>())
                    if (!look.name.StartsWith("Piece_")) top = Mathf.Max(top, look.bounds.max.y);
            GameObject item = Put(prefab, parent, new Vector3(from.x, top, from.z), Quaternion.Euler(0, Range(0, 360), 0));
            if (!item) return made;
            item.name = prefab + "_" + (made + 1).ToString("00");
            item.transform.position += Vector3.up * (top - Look(item).min.y + 0.01f); // sitting on it, not in it
            if (item.TryGetComponent(out FoodItem serving)) serving.respawn = 45;
            made++;
        }
        return made;
    }

    // ---- pieces

    // a vendor turned so the side its food is on faces `serve`
    static GameObject Vendor(string prefab, Transform parent, Vector3 at, Vector3 serve)
    {
        GameObject vendor = Put(prefab, parent, at, Quaternion.identity);
        Vector3 food = Vector3.zero;
        FoodItem[] servings = vendor.GetComponentsInChildren<FoodItem>();
        foreach (FoodItem s in servings) food += vendor.transform.InverseTransformPoint(s.transform.position) / servings.Length;
        Vector3 side = Vector3.ProjectOnPlane(food - LocalBounds(vendor.transform).center, Vector3.up);
        if (servings.Length == 0 || side == Vector3.zero) side = Vector3.forward;
        vendor.transform.rotation = Quaternion.LookRotation(serve) * Quaternion.Inverse(Quaternion.LookRotation(side));
        vendor.name = prefab + "_" + parent.childCount.ToString("000");
        return vendor;
    }

    // keeps `thing` if all four corners of its footprint pass `standsOn` and it overlaps nothing solid (what it stands
    // on aside) and no kept-clear spot; otherwise takes it away again
    static bool Keep(GameObject thing, StandsOn standsOn)
    {
        if (!thing) return false;
        Physics.SyncTransforms();
        Transform t = thing.transform;
        Bounds local = LocalBounds(t);
        Vector3 half = Vector3.Scale(local.extents, t.lossyScale);
        bool ok = true;
        // (80% of the way out: mirrors, awnings and handlebars may hang over a kerb or the lawn)
        string kind = thing.name.Split('_')[0];
        void Refuse(string why) { ok = false; refused.TryGetValue(kind + ": " + why, out int n); refused[kind + ": " + why] = n + 1; }
        for (int i = 0; i < 4 && ok; i++)
            if (!standsOn(t.TransformPoint(local.center + Vector3.Scale(local.extents * 0.8f, new Vector3(i % 2 * 2 - 1, 0, i / 2 * 2 - 1))), out _)) Refuse("off " + standsOn.Method.Name.ToLower());
        foreach (var (at, radius) in keepClear) if (ok && Flat(t.position - at) < radius + Mathf.Max(half.x, half.z)) Refuse("by a task spot or spawn");
        if (ok)
        {
            // lifted off the ground a little, so the floor and kerbs it stands by don't count
            Vector3 centre = t.TransformPoint(local.center) + Vector3.up * 0.1f;
            var box = new Vector3(half.x - 0.05f, Mathf.Max(0.05f, half.y - 0.1f), half.z - 0.05f);
            foreach (Collider c in Physics.OverlapBox(centre, box, t.rotation, ~0, QueryTriggerInteraction.Ignore))
                if (!c.transform.IsChildOf(t) && !(c is TerrainCollider) && !IsGround(c.name)) { Refuse("hits " + c.name); break; }
        }
        if (!ok) Object.DestroyImmediate(thing);
        return ok;
    }

    delegate bool StandsOn(Vector3 p, out Vector3 ground);

    // the ground piece on top under p (the base slab only where nothing else covers it), and the point on it
    static Collider TopGround(Vector3 p, out Vector3 point)
    {
        var down = new Ray(new Vector3(p.x, 50, p.z), Vector3.down);
        Collider top = null;
        point = p;
        foreach (Collider piece in ground)
            if (piece.Raycast(down, out RaycastHit hit, 100) && (!top || hit.point.y > point.y)) { top = piece; point = hit.point; }
        if (!top && parkBase && parkBase.Raycast(down, out RaycastHit slab, 100)) { top = parkBase; point = slab.point; }
        // a piece only switched on for the layout: things stand on the terrain that shows there, not on the hidden mesh
        Terrain terrain = Terrain.activeTerrain;
        if (top && hidden.Contains(top.name) && terrain) point.y = terrain.SampleHeight(point) + terrain.transform.position.y;
        return top;
    }

    // a path or pavement (not lawn, a bed, gravel, the road...) under p, and the point on it
    static bool Paved(Vector3 p, out Vector3 point)
    {
        Collider top = TopGround(p, out point);
        return top && (top == parkBase || System.Array.IndexOf(PavedPieces, top.name) >= 0);
    }

    // the road under p, and the point on it
    static bool Road(Vector3 p, out Vector3 point)
    {
        Collider top = TopGround(p, out point);
        return top && System.Array.IndexOf(RoadPieces, top.name) >= 0;
    }

    static Vector3 OnRoad(Vector3 p) => Road(p, out Vector3 ground) ? ground : p;

    // every prefab as many times as its slider says, shuffled, so if not all fit it's random which are left out
    static List<string> Deal((string prefab, int count)[] kinds)
    {
        var deck = new List<string>();
        foreach (var (prefab, count) in kinds)
            for (int i = 0; i < count; i++) deck.Add(prefab);
        for (int i = deck.Count - 1; i > 0; i--) { int j = random.Next(i + 1); (deck[i], deck[j]) = (deck[j], deck[i]); }
        return deck;
    }

    static GameObject Put(string prefab, Transform parent, Vector3 at, Quaternion facing)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/" + prefab + ".prefab");
        if (!asset) { Debug.LogWarning("Wash Square Hunt: no Assets/Prefabs/" + prefab + ".prefab"); return null; }
        var g = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
        g.transform.SetPositionAndRotation(at, facing);
        return g;
    }

    // the box round all of a thing's meshes, in its own space
    static Bounds LocalBounds(Transform root)
    {
        var b = new Bounds();
        bool first = true;
        void Add(Transform part, Mesh mesh)
        {
            if (!mesh) return;
            Bounds m = mesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = root.InverseTransformPoint(part.TransformPoint(m.center + Vector3.Scale(m.extents, new Vector3(i & 1, i >> 1 & 1, i >> 2 & 1) * 2 - Vector3.one)));
                if (first) b = new Bounds(corner, Vector3.zero); else b.Encapsulate(corner);
                first = false;
            }
        }
        foreach (MeshFilter f in root.GetComponentsInChildren<MeshFilter>()) Add(f.transform, f.sharedMesh);
        foreach (SkinnedMeshRenderer s in root.GetComponentsInChildren<SkinnedMeshRenderer>()) Add(s.transform, s.sharedMesh);
        return b;
    }

    static Bounds Look(GameObject thing)
    {
        if (!thing) throw new System.Exception("Missing an object Wash Square Hunt needs");
        Renderer[] looks = thing.GetComponentsInChildren<Renderer>();
        Bounds b = looks[0].bounds;
        foreach (Renderer r in looks) b.Encapsulate(r.bounds);
        return b;
    }

    static Transform Spot(Transform parent, string name, Vector3 at)
    {
        var spot = new GameObject(name).transform;
        spot.SetParent(parent, false);
        spot.position = at;
        return spot;
    }

    static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
    static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;
}
