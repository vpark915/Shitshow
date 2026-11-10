using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Tools > Wash Square > Update Mechanics Gym: Scenes/MechanicsGym brought up to the current game. Stations 1-9 run north
// up the old floor; an east wing (gym space x 35-75) has 10 cars, 11 Citi Bikes, 12 trash cans, 13 the fountain, 14
// police; a west wing (x -75 to -35) has 15 traffic (a block of road with cars driving round it, a traffic signal and a
// walk signal) and 16 kill signs (target dummies that get back up). Every sign is an NYC pole sign: the station's green
// street blade (Figma, Resources/StreetSigns/gym_*) over a white DOT panel with the words (StreetFurniture). Also: the
// trash cans refill, an Escape menu (GymMenu), the minimap baked, and the build is the park (the starting menu), this
// gym and the Tutorial. Run again to rebuild the wings.
static class MechanicsGymUpdate
{
    const string ScenePath = "Assets/Scenes/MechanicsGym.unity";
    const float X = 55, W = -55; // the wings' middles, gym space

    [MenuItem("Tools/Wash Square/Update Mechanics Gym")]
    static void Run()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var scene = EditorSceneManager.OpenScene(ScenePath);
        Transform gym = GameObject.Find("Gym").transform, stations = gym.Find("Stations");
        Material grey = gym.Find("Floor").GetComponent<Renderer>().sharedMaterial;

        EastWing(gym, grey);
        WestWing(gym, grey);

        // the old signs, brought up to date, then all of them made NYC pole signs
        Retext(gym, "Every system in the game", "Every system in the game, one station each. Walk forward for 1-9; 10-14 (cars, bikes, trash cans, fountain, police) are on the floor to the east, 15 traffic and 16 kill signs to the west. New? Play the Tutorial from the menu. Esc: menu.");
        Retext(gym, "Ice cream · Egg bomb", "Ice cream · Egg bomb, stash (F)\nblasts you 15 m up with your air jumps back; kills anyone close, hurts everyone within 6 m");
        Retext(gym, "The Seagull you hunt", "The Seagull you hunt (the one in the red headband) is behind this wall. You're the detective in the deerstalker. The minimap only shows a circle it's somewhere inside; eat a chess piece to see exactly where.");
        Retext(gym, "WASD move", "WASD move · Shift sprint · Space jump\nMouse look · Left click shoot · Right click scope · R reload\nE eat, rummage, or (out of ammo, on a head) assassinate\nLand on a roof or saddle to drive · Q hop off\nF egg bomb · G snitch flock · Esc menu");
        // listed first: converting one destroys its children, which the walk would otherwise still visit. Pole signs
        // from an earlier run are made again too, so they pick up changes to the art or the layout
        var old = System.Array.FindAll(gym.GetComponentsInChildren<Transform>(true), t => t.name.StartsWith("Sign") && (t.Find("Board") || t.Find("Blade")));
        foreach (Transform t in old)
            if (t.Find("Board")) Convert(t);
            else if (t.parent.name != "East Wing" && t.parent.name != "West Wing") Remake(t);
        int converted = old.Length;

        if (!Object.FindFirstObjectByType<GymMenu>()) new GameObject("GymMenu").AddComponent<GymMenu>();

        EditorSceneManager.SaveScene(scene);
        // ponytail: photographed a tick later, with the gym left open: in the same tick as the scene switch the GPU
        // Resident Drawer still drew the previous scene's objects into the picture
        EditorApplication.delayCall += MinimapBake.Bake; // saves the scene
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene("Assets/Scenes/WashSquareHunt.unity", true), new EditorBuildSettingsScene(ScenePath, true),
            new EditorBuildSettingsScene(TutorialBuild.ScenePath, true),
        };
        AssetDatabase.SaveAssets();
        Debug.Log("Update Mechanics Gym: stations 10-16 in the wings, " + converted + " signs made NYC pole signs, menu, minimap baked; build = WashSquareHunt, MechanicsGym, Tutorial");
    }

    static void EastWing(Transform gym, Material grey)
    {
        Transform old = gym.Find("East Wing");
        if (old) Object.DestroyImmediate(old.gameObject);
        var wing = new GameObject("East Wing").transform;
        wing.SetParent(gym, false);

        // floor: joined to the old one's east edge, the same length
        Transform floor = Block(wing, "Floor", new Vector3(X, -0.5f, 60), new Vector3(40, 1, 150), grey);
        floor.gameObject.isStatic = false; // not in the old bake: lit by the probes instead of a black lightmap

        Station(wing, X - 9, 9, -35, "10 · CARS", "Land on a car's roof to drive it: W/S drive and brake, A/D steer, Shift faster, Q hops off. You can't shoot while driving. In the park, driving off the road makes you wanted.");
        string[] cars = { "Cars/NYCTaxi", "Cars/CompactSedan", "Cars/LargeSUV" };
        for (int i = 0; i < cars.Length; i++) Place("Assets/Prefabs/" + cars[i] + ".prefab", wing, new Vector3(X - 8 + i * 8, 0, 16), Quaternion.identity);

        Station(wing, X - 9, 34, -35, "11 · CITI BIKES", "Land on a saddle to ride: steer like a car, Space hops, Q gets off. Bikes in the dock ride too.");
        var random = new System.Random(11);
        CitiBikePrefab.Station(wing, wing.TransformPoint(new Vector3(X + 5, 0, 40)), wing.rotation * Quaternion.Euler(0, -90, 0), 6, 0.7f, random);
        Place("Assets/Prefabs/CitiBike.prefab", wing, new Vector3(X - 8, 0, 40), Quaternion.identity);

        Station(wing, X - 9, 58, -35, "12 · TRASH CANS", "Look at a can and press E to rummage: food (eaten on the spot), cash, a magazine, or just trash. In a match a can is empty till the next round; here it refills in 5 s.");
        for (int i = 0; i < 3; i++)
            Place("Assets/Prefabs/TrashCan.prefab", wing, new Vector3(X - 6 + i * 6, 0, 63), Quaternion.Euler(0, i * 40, 0)).GetComponent<TrashCan>().refill = 5;

        Station(wing, X - 9, 80, -35, "13 · FOUNTAIN", "Wade in: the water splashes round anything in it. \"Take a bath\" tasks are done standing in a fountain.");
        Transform basin = Shape(PrimitiveType.Cylinder, wing, "Basin", new Vector3(X, 0.25f, 88), new Vector3(8, 0.25f, 8), grey);
        Transform water = Shape(PrimitiveType.Cylinder, wing, "Water", new Vector3(X, 0.3f, 88), new Vector3(7, 0.26f, 7), null);
        Object.DestroyImmediate(water.GetComponent<Collider>());
        WaterSplashes splashes = water.gameObject.AddComponent<WaterSplashes>();
        splashes.spoutMistRate = splashes.plumeRate = 0; // no spout
        basin.gameObject.isStatic = water.gameObject.isStatic = false;

        Station(wing, X - 9, 104, -35, "14 · POLICE", "Fire near people and the police go after whoever fired: you're wanted till you lose them (a pretzel makes them ignore you). Out of ammo on someone's head, E kills them silently. Officers drop a magazine, everyone else a wallet.");
    }

    static void WestWing(Transform gym, Material grey)
    {
        Transform old = gym.Find("West Wing");
        if (old) Object.DestroyImmediate(old.gameObject);
        var wing = new GameObject("West Wing").transform;
        wing.SetParent(gym, false);
        Transform floor = Block(wing, "Floor", new Vector3(W, -0.5f, 60), new Vector3(40, 1, 150), grey);
        floor.gameObject.isStatic = false;

        // ---- 15 traffic: a block of road with cars driving round it, a signal at the corner ----
        Station(wing, W + 9, 9, 35, "15 · TRAFFIC", "Cars drive round the block by themselves: land on a moving one's roof to hijack it, then hit people to run them over (roadkill). The lights and walk signals cycle like a real crossing; the cars don't stop for them, so look both ways.");
        Material asphalt = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Gym Asphalt", color = new Color(0.16f, 0.16f, 0.17f) };
        float x0 = W - 13, x1 = W + 5, z0 = 16, z1 = 52, lane = 6;
        // the four sides of the loop, named as the park's road so driving on it doesn't count as off road
        Block(wing, "WSP_Road", new Vector3((x0 + x1) / 2, 0.01f, z0), new Vector3(x1 - x0 + lane, 0.02f, lane), asphalt);
        Block(wing, "WSP_Road", new Vector3((x0 + x1) / 2, 0.01f, z1), new Vector3(x1 - x0 + lane, 0.02f, lane), asphalt);
        Block(wing, "WSP_Road", new Vector3(x0, 0.01f, (z0 + z1) / 2), new Vector3(lane, 0.02f, z1 - z0 - lane), asphalt);
        Block(wing, "WSP_Road", new Vector3(x1, 0.01f, (z0 + z1) / 2), new Vector3(lane, 0.02f, z1 - z0 - lane), asphalt);
        Vector3[] loop = { wing.TransformPoint(new Vector3(x0, 0, z0)), wing.TransformPoint(new Vector3(x1, 0, z0)), wing.TransformPoint(new Vector3(x1, 0, z1)), wing.TransformPoint(new Vector3(x0, 0, z1)) };
        string[] cars = { "Cars/NYCTaxi", "Cars/Minivan" };
        for (int i = 0; i < cars.Length; i++)
        {
            Vector3 at = i == 0 ? new Vector3((x0 + x1) / 2, 0, z0) : new Vector3((x0 + x1) / 2, 0, z1);
            Transform car = Place("Assets/Prefabs/" + cars[i] + ".prefab", wing, at, Quaternion.Euler(0, i == 0 ? 90 : 270, 0));
            var driver = car.gameObject.AddComponent<TrafficCar>();
            driver.route = loop;
            driver.cruise = 6f;
            car.name = "Traffic " + car.name;
        }
        // the signal on the inside corner, its arm over the near road; a walk post beside it
        Transform post = Place("Assets/Blender/Street/trafficSignal.blend", wing, new Vector3(x1 - lane / 2 - 1, 0, z0 + lane / 2 + 1), Quaternion.Euler(0, 180, 0) * Quaternion.Euler(-90, 0, 0));
        post.gameObject.AddComponent<TrafficSignal>();
        Transform walk = Place("Assets/Blender/Street/walkSignal.blend", wing, new Vector3(x0 + lane / 2 + 1, 0, z0 + lane / 2 + 1), Quaternion.Euler(0, 90, 0) * Quaternion.Euler(-90, 0, 0));
        walk.gameObject.AddComponent<TrafficSignal>();

        // ---- 16 kill signs: a row of people who stand there and get back up ----
        Station(wing, W + 9, 70, 35, "16 · KILL SIGNS", "Every kill pops a Kill sign over the crosshair, Double kill or Triple kill when they come under 3 s apart. A headshot that doesn't kill shows the Headshot sign instead. These people get back up after 3 s: line up a triple.");
        var spawner = Object.FindFirstObjectByType<NpcSpawner>();
        if (spawner && spawner.civilians.Length > 0)
            for (int i = 0; i < 5; i++)
            {
                var person = (GameObject)PrefabUtility.InstantiatePrefab(spawner.civilians[i % spawner.civilians.Length], wing);
                person.name = "Dummy " + (i + 1);
                person.transform.SetLocalPositionAndRotation(new Vector3(W - 10 + i * 4, 0, 84), Quaternion.Euler(0, 180, 0));
                var animator = person.GetComponent<Animator>();
                if (animator) { animator.runtimeAnimatorController = spawner.controller; animator.applyRootMotion = false; }
                var body = person.AddComponent<CapsuleCollider>();
                body.center = Vector3.up * NpcSpawner.Height / 2;
                body.height = NpcSpawner.Height;
                body.radius = NpcSpawner.Radius;
                person.AddComponent<Health>().maxHealth = spawner.health;
                person.AddComponent<TargetDummy>();
            }
        else Debug.LogWarning("Update Mechanics Gym: no NpcSpawner with civilians in the gym, so station 16 has no dummies");
    }

    // an NYC pole sign at (x, z) in the wing, turned `yaw`: the station's green blade over the panel with the words
    static void Station(Transform wing, float x, float z, float yaw, string title, string body)
    {
        PoleSign(wing, wing.TransformPoint(new Vector3(x, 0, z)), wing.rotation * Quaternion.Euler(0, yaw + 180, 0), title, body);
    }

    // an old board sign (post, board, title and body) swapped for an NYC pole sign, where it stood, read the same way
    static void Convert(Transform old)
    {
        Text[] texts = old.GetComponentsInChildren<Text>(true);
        System.Array.Sort(texts, (a, b) => b.fontSize.CompareTo(a.fontSize));
        string title = texts.Length > 0 ? texts[0].text : old.name.Substring(5);
        string body = texts.Length > 1 ? texts[1].text : "";
        // a world canvas reads from behind its forward, so the reader stands at -forward
        Vector3 reader = -texts[0].canvas.transform.forward;
        reader.y = 0;
        PoleSign(old.parent, old.position, Quaternion.LookRotation(reader.normalized), title, body);
        Undo.DestroyObjectImmediate(old.gameObject);
    }

    // a pole sign from an earlier run, built again where it stands with the same words
    static void Remake(Transform sign)
    {
        Text words = sign.GetComponentInChildren<Text>(true);
        PoleSign(sign.parent, sign.position, sign.rotation, sign.name.Substring("Sign ".Length), words ? words.text : "");
        Undo.DestroyObjectImmediate(sign.gameObject);
    }

    static Material poleMetal;

    // StreetFurniture signs face forward toward the reader; the pole is behind them
    static void PoleSign(Transform parent, Vector3 at, Quaternion facing, string title, string body)
    {
        var sign = new GameObject("Sign " + title).transform;
        sign.SetParent(parent, false);
        sign.SetPositionAndRotation(at, facing);
        if (!poleMetal) poleMetal = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Gym Sign Pole", color = new Color(0.6f, 0.62f, 0.64f) };
        poleMetal.SetFloat("_Metallic", 0.75f);
        Transform pole = Shape(PrimitiveType.Cylinder, sign, "Pole", new Vector3(0, 1.6f, 0), new Vector3(0.1f, 1.6f, 0.1f), poleMetal);
        pole.localPosition = new Vector3(0, 1.6f, 0);
        string art = "gym_" + System.Text.RegularExpressions.Regex.Replace(title.ToLowerInvariant().Replace("·", " "), "[^a-z0-9]+", "_").Trim('_');
        if (!StreetFurniture.Art(art)) Debug.LogWarning("Update Mechanics Gym: no blade art " + art + " for " + title);
        else StreetFurniture.Sign(art, sign, at + Vector3.up * 2.95f + facing * Vector3.forward * 0.07f, facing, 0.34f, twoSided: true).name = "Blade";
        if (body.Length > 0) StreetFurniture.Panel(sign, at + Vector3.up * 1.75f + facing * Vector3.forward * 0.07f, facing, 1.25f, body).name = "Panel";
    }

    static void Retext(Transform under, string starts, string text)
    {
        foreach (Text t in under.GetComponentsInChildren<Text>(true))
            if (t.text.StartsWith(starts)) { Undo.RecordObject(t, "Update Mechanics Gym"); t.text = text; EditorUtility.SetDirty(t); }
    }

    static Transform Block(Transform parent, string name, Vector3 at, Vector3 size, Material look) => Shape(PrimitiveType.Cube, parent, name, at, size, look);

    static Transform Shape(PrimitiveType type, Transform parent, string name, Vector3 at, Vector3 size, Material look)
    {
        GameObject g = GameObject.CreatePrimitive(type);
        g.name = name;
        g.transform.SetParent(parent, false);
        g.transform.localPosition = at;
        g.transform.localScale = size;
        if (look) g.GetComponent<Renderer>().sharedMaterial = look;
        return g.transform;
    }

    static Transform Place(string prefab, Transform parent, Vector3 at, Quaternion facing)
    {
        var g = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefab), parent);
        g.transform.SetLocalPositionAndRotation(at, facing);
        return g.transform;
    }
}
