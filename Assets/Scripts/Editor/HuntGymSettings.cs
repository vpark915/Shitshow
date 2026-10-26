using UnityEditor;
using UnityEngine;

// How much stuff the Hunt Gym has: select Assets/Settings/HuntGymSettings, move the sliders, press Apply to Hunt Gym.
// Applying scatters new cover, carts, trucks and food (and sets the people and flocks) in Scenes/HuntGym and saves it;
// Build Hunt Gym uses these too.
public class HuntGymSettings : ScriptableObject
{
    const string Path = "Assets/Settings/HuntGymSettings.asset";

    [Header("Cover")]
    [Tooltip("Pieces of cover to scatter. Fewer may fit if Spacing is large")]
    [Range(0, 120)] public int cover = 32;
    [Tooltip("Least distance between two pieces, metres. Small packs them in; large spreads them out (and fits fewer)")]
    [Range(3, 20)] public float spacing = 9f;
    [Tooltip("Layout number; the same number gives the same layout. 0 = a new random one each time")]
    public int seed;

    [Header("Mix (how often each kind comes up, relative to the others)")]
    [Range(0, 10)] public int lowWalls = 4;
    [Range(0, 10)] public int blocks = 3;
    [Range(0, 10)] public int pillars = 2;
    [Range(0, 10)] public int benches = 1;

    [Header("Carts and trucks (their food is bought)")]
    [Range(0, 5)] public int foodCarts = 1;
    [Range(0, 5)] public int coffeeCarts = 1;
    [Range(0, 5)] public int hotdogCarts = 1;
    [Range(0, 5)] public int mexicanTrucks = 1;
    [Range(0, 5)] public int iceCreamTrucks = 1;

    [Header("Cars to drive (land on the roof); Tools > Wash Square > Build Car Prefabs makes them")]
    [Range(0, 5)] public int taxis = 1;
    [Range(0, 5)] public int compactSedans = 1;
    [Range(0, 5)] public int midsizeSedans = 1;
    [Range(0, 5)] public int compactSUVs = 1;
    [Range(0, 5)] public int largeSUVs = 1;
    [Range(0, 5)] public int minivans = 1;

    [Header("Rides")]
    [Tooltip("Loose Citi Bikes standing about. Ride one by landing on the saddle")]
    [Range(0, 10)] public int citiBikes = 3;
    [Tooltip("Citi Bike stations: a row of docks with bikes in some of them")]
    [Range(0, 5)] public int bikeStations = 2;
    [Range(1, 15)] public int docksPerStation = 8;
    [Tooltip("Share of docks with a bike in")]
    [Range(0, 1)] public float dockedBikes = 0.6f;

    [Header("Stray food (lying about, free)")]
    [Range(0, 15)] public int bagels = 2;
    [Range(0, 15)] public int bread = 2;
    [Range(0, 15)] public int burritos = 2;
    [Range(0, 15)] public int coffee = 2;
    [Range(0, 15)] public int gyros = 2;
    [Range(0, 15)] public int hotdogs = 2;
    [Range(0, 15)] public int iceCream = 2;
    [Range(0, 15)] public int pizza = 2;
    [Range(0, 15)] public int pretzels = 2;

    [Tooltip("Chance that a person who dies drops a random food")]
    [Range(0, 1)] public float foodDropChance = 0.3f;

    [Header("Bird flocks")]
    [Range(0, 10)] public int pigeonFlocks = 2;
    [Range(0, 10)] public int seagullFlocks = 1;
    [Tooltip("Fewest birds in a flock")]
    [Range(1, 30)] public int flockMin = 8;
    [Tooltip("Most birds in a flock")]
    [Range(1, 30)] public int flockMax = 16;

    // the prefab (in Assets/Prefabs) each slider above puts up, and how many
    public (string prefab, int count)[] Vendors => new[]
    {
        ("FoodCart", foodCarts), ("CoffeeCart", coffeeCarts), ("HotdogCart", hotdogCarts),
        ("MexicanTruck", mexicanTrucks), ("IceCreamTruck", iceCreamTrucks),
    };
    public (string prefab, int count)[] Cars => new[]
    {
        ("Cars/NYCTaxi", taxis), ("Cars/CompactSedan", compactSedans), ("Cars/MidsizeSedan", midsizeSedans),
        ("Cars/CompactSUV", compactSUVs), ("Cars/LargeSUV", largeSUVs), ("Cars/Minivan", minivans),
    };
    public (string prefab, int count)[] Food => new[]
    {
        ("Bagel", bagels), ("Bread", bread), ("Burrito", burritos), ("Coffee", coffee), ("Gyro", gyros),
        ("Hotdog", hotdogs), ("IceCream", iceCream), ("Pizza", pizza), ("Pretzel", pretzels),
    };

    [Header("People")]
    [Range(0, 60)] public int walkers = 12;
    [Range(0, 20)] public int joggers;
    [Range(0, 40)] public int idlers;
    [Range(0, 10)] public int officers = 2;
    [Range(0, 10)] public int guards = 1;

    public static HuntGymSettings Load()
    {
        var settings = AssetDatabase.LoadAssetAtPath<HuntGymSettings>(Path);
        if (settings) return settings;
        settings = CreateInstance<HuntGymSettings>();
        AssetDatabase.CreateAsset(settings, Path);
        return settings;
    }

    [MenuItem("Tools/Wash Square/Hunt Gym Settings")]
    static void Select() => Selection.activeObject = Load();
}

[CustomEditor(typeof(HuntGymSettings))]
class HuntGymSettingsEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();
        if (GUILayout.Button("Apply to Hunt Gym", GUILayout.Height(30))) HuntGym.Reshuffle();
    }
}
