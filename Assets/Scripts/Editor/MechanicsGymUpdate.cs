using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Tools > Wash Square > Update Mechanics Gym: Scenes/MechanicsGym brought up to the current game. Stations 1-9 run north
// up the old floor; an east wing (gym space x 35-75) gets stations for what came later: 10 cars, 11 Citi Bikes, 12 trash
// cans, 13 the fountain, 14 police. Signs are copies of sign 6 with new words; the welcome, controls and people signs are
// rewritten. Also: the trash cans refill, an Escape menu (GymMenu), the minimap baked, and the build is the
// park (the starting menu) then this gym. Run again to rebuild the wing.
static class MechanicsGymUpdate
{
    const string ScenePath = "Assets/Scenes/MechanicsGym.unity";
    const float X = 55; // the wing's middle, gym space

    [MenuItem("Tools/Wash Square/Update Mechanics Gym")]
    static void Run()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var scene = EditorSceneManager.OpenScene(ScenePath);
        Transform gym = GameObject.Find("Gym").transform, stations = gym.Find("Stations");
        Transform old = gym.Find("East Wing");
        if (old) Object.DestroyImmediate(old.gameObject);
        var wing = new GameObject("East Wing").transform;
        wing.SetParent(gym, false);
        Material grey = gym.Find("Floor").GetComponent<Renderer>().sharedMaterial;

        // floor: joined to the old one's east edge, the same length
        Transform floor = Block(wing, "Floor", new Vector3(X, -0.5f, 60), new Vector3(40, 1, 150), grey);
        floor.gameObject.isStatic = false; // not in the old bake: lit by the probes instead of a black lightmap

        Transform pattern = null;
        foreach (Transform t in stations) if (t.name.StartsWith("Sign 6")) pattern = t;

        Sign(pattern, wing, "10 · CARS", "Land on a car's roof to drive it: W/S drive and brake, A/D steer, Shift faster, Q hops off. You can't shoot while driving. In the park, driving off the road makes you wanted.", 9);
        string[] cars = { "Cars/NYCTaxi", "Cars/CompactSedan", "Cars/LargeSUV" };
        for (int i = 0; i < cars.Length; i++) Place("Assets/Prefabs/" + cars[i] + ".prefab", wing, new Vector3(X - 8 + i * 8, 0, 16), Quaternion.identity);

        Sign(pattern, wing, "11 · CITI BIKES", "Land on a saddle to ride: steer like a car, Space hops, Q gets off. Bikes in the dock ride too.", 34);
        var random = new System.Random(11);
        CitiBikePrefab.Station(wing, wing.TransformPoint(new Vector3(X + 5, 0, 40)), wing.rotation * Quaternion.Euler(0, -90, 0), 6, 0.7f, random);
        Place("Assets/Prefabs/CitiBike.prefab", wing, new Vector3(X - 8, 0, 40), Quaternion.identity);

        Sign(pattern, wing, "12 · TRASH CANS", "Look at a can and press E to rummage: food (eaten on the spot), cash, a magazine, or just trash. In a match a can is empty till the next round; here it refills in 5 s.", 58);
        for (int i = 0; i < 3; i++)
            Place("Assets/Prefabs/TrashCan.prefab", wing, new Vector3(X - 6 + i * 6, 0, 63), Quaternion.Euler(0, i * 40, 0)).GetComponent<TrashCan>().refill = 5;

        Sign(pattern, wing, "13 · FOUNTAIN", "Wade in: the water splashes round anything in it. \"Take a bath\" tasks are done standing in a fountain.", 80);
        Transform basin = Shape(PrimitiveType.Cylinder, wing, "Basin", new Vector3(X, 0.25f, 88), new Vector3(8, 0.25f, 8), grey);
        Transform water = Shape(PrimitiveType.Cylinder, wing, "Water", new Vector3(X, 0.3f, 88), new Vector3(7, 0.26f, 7), null);
        Object.DestroyImmediate(water.GetComponent<Collider>());
        WaterSplashes splashes = water.gameObject.AddComponent<WaterSplashes>();
        splashes.spoutMistRate = splashes.plumeRate = 0; // no spout
        basin.gameObject.isStatic = water.gameObject.isStatic = false;

        Sign(pattern, wing, "14 · POLICE", "Fire near people and the police go after whoever fired: you're wanted till you lose them (a pretzel makes them ignore you). Out of ammo on someone's head, E kills them silently. Officers drop a magazine, everyone else a wallet.", 104);

        // the old signs, brought up to date
        Retext(stations, "Every system in the game", "Every system in the game, one station each. Walk forward for 1-9; 10-14 (cars, bikes, trash cans, fountain, police) are on the floor to the east. Esc: menu.");
        Retext(stations, "WASD move", "WASD move · Shift sprint · Space jump\nMouse look · Left click shoot · Right click scope · R reload\nE eat, rummage, or (out of ammo, on a head) assassinate · Q hop off\nF egg bomb · G snitch flock · Esc menu");

        if (!Object.FindFirstObjectByType<GymMenu>()) new GameObject("GymMenu").AddComponent<GymMenu>();

        EditorSceneManager.SaveScene(scene);
        // ponytail: photographed a tick later, with the gym left open: in the same tick as the scene switch the GPU
        // Resident Drawer still drew the previous scene's objects into the picture
        EditorApplication.delayCall += MinimapBake.Bake; // saves the scene
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/WashSquareHunt.unity", true), new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        Debug.Log("Update Mechanics Gym: stations 10-14 in the east wing, menu, minimap baked; build = WashSquareHunt, MechanicsGym");
    }

    // a copy of sign 6 at (X - 9, z), facing the same way, saying this; the bigger text is the title
    static void Sign(Transform pattern, Transform parent, string title, string body, float z)
    {
        Transform sign = Object.Instantiate(pattern, parent);
        sign.name = "Sign " + title;
        sign.localPosition = new Vector3(X - 9, 0, z);
        Text[] texts = sign.GetComponentsInChildren<Text>();
        System.Array.Sort(texts, (a, b) => b.fontSize.CompareTo(a.fontSize));
        texts[0].text = title;
        for (int i = 1; i < texts.Length; i++) texts[i].text = i == 1 ? body : "";
        ((RectTransform)texts[0].transform.parent).sizeDelta = new Vector2(500, 230); // room for the longer words
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
