using UnityEditor;
using UnityEngine;

// How much stuff Scenes/WashSquareHunt has: Tools > Wash Square > Wash Square Hunt Settings, move the sliders, press
// Apply. Applying re-places every cart, truck, car, Citi Bike, piece of park food and trash can, each only where its kind belongs
// (WashSquareHunt.Reshuffle says where), sets the people and flocks, re-bakes the walkable area and saves the scene.
public class WashSquareHuntSettings : ScriptableObject
{
    const string Path = "Assets/Settings/WashSquareHuntSettings.asset";

    [Tooltip("Layout number; the same number gives the same layout. 0 = a new random one each time")]
    public int seed;

    [Header("Carts (on the park paths, backs to the path edge)")]
    [Range(0, 10)] public int foodCarts = 3;
    [Range(0, 10)] public int coffeeCarts = 3;
    [Range(0, 10)] public int hotdogCarts = 3;

    [Header("Trucks (in the parking lanes round the park, hatch to the park)")]
    [Range(0, 8)] public int mexicanTrucks = 3;
    [Range(0, 8)] public int iceCreamTrucks = 3;

    [Header("Cars (all driving round the park, one way); land on the roof to take one over")]
    [Range(0, 10)] public int taxis = 4;
    [Range(0, 10)] public int compactSedans = 4;
    [Range(0, 10)] public int midsizeSedans = 4;
    [Range(0, 10)] public int compactSUVs = 4;
    [Range(0, 10)] public int largeSUVs = 4;
    [Range(0, 10)] public int minivans = 4;

    [Tooltip("Their cruising speed, m/s (8 is about 18 mph)")]
    [Range(3, 15)] public float trafficSpeed = 8f;

    [Header("Citi Bikes")]
    [Tooltip("Loose bikes standing on the park paths")]
    [Range(0, 30)] public int citiBikes = 10;
    [Tooltip("Docking stations in the parking lanes")]
    [Range(0, 10)] public int bikeStations = 4;
    [Range(1, 15)] public int docksPerStation = 10;
    [Tooltip("Share of docks with a bike in")]
    [Range(0, 1)] public float dockedBikes = 0.6f;

    [Header("Park food (free, comes back 45 s after eaten)")]
    [Tooltip("Slices on chess tables")]
    [Range(0, 25)] public int pizza = 8;
    [Tooltip("On bench seats")]
    [Range(0, 40)] public int bagels = 10;
    [Tooltip("On bench seats")]
    [Range(0, 40)] public int bread = 10;
    [Tooltip("Chance that a person who dies drops a random food")]
    [Range(0, 1)] public float foodDropChance = 0.3f;

    [Header("Trash cans (at bench ends; E to search, a random food, cash or magazine, restocked each round)")]
    [Range(0, 60)] public int trashCans = 24;

    [Header("Bird flocks")]
    [Range(0, 15)] public int pigeonFlocks = 5;
    [Range(0, 15)] public int seagullFlocks = 3;
    [Range(1, 30)] public int flockMin = 8;
    [Range(1, 30)] public int flockMax = 16;

    [Header("People")]
    [Range(0, 300)] public int walkers = 171;
    [Range(0, 60)] public int joggers = 24;
    [Range(0, 100)] public int idlers = 39;
    [Range(0, 30)] public int officers = 8;
    [Range(0, 30)] public int guards = 8;

    // the prefab (in Assets/Prefabs) each slider puts up, and how many
    public (string prefab, int count)[] Carts => new[] { ("FoodCart", foodCarts), ("CoffeeCart", coffeeCarts), ("HotdogCart", hotdogCarts) };
    public (string prefab, int count)[] Trucks => new[] { ("MexicanTruck", mexicanTrucks), ("IceCreamTruck", iceCreamTrucks) };
    public (string prefab, int count)[] Cars => new[]
    {
        ("Cars/NYCTaxi", taxis), ("Cars/CompactSedan", compactSedans), ("Cars/MidsizeSedan", midsizeSedans),
        ("Cars/CompactSUV", compactSUVs), ("Cars/LargeSUV", largeSUVs), ("Cars/Minivan", minivans),
    };

    public static WashSquareHuntSettings Load()
    {
        var settings = AssetDatabase.LoadAssetAtPath<WashSquareHuntSettings>(Path);
        if (settings) return settings;
        settings = CreateInstance<WashSquareHuntSettings>();
        AssetDatabase.CreateAsset(settings, Path);
        return settings;
    }

    [MenuItem("Tools/Wash Square/Wash Square Hunt Settings")]
    static void Select() => Selection.activeObject = Load();
}

[CustomEditor(typeof(WashSquareHuntSettings))]
class WashSquareHuntSettingsEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();
        if (GUILayout.Button("Apply to Wash Square Hunt", GUILayout.Height(30))) WashSquareHunt.Reshuffle();
    }
}
