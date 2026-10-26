using UnityEditor;
using UnityEngine;

// Tools > Wash Square > Build Car Prefabs: makes Prefabs/Cars/<Name> from every .blend in Blender/Cars, each facing +Z,
// centred between its axles and standing on y = 0, with its body box, a perch on the roof and Car wired up. A car's
// .blend needs an empty named for the car holding <Name>_Body and <Name>_Wheel_FL, _FR, _RL and _RR, each wheel's
// origin on its axle. Run it again after changing or adding a car.
static class CarPrefabs
{
    const string Models = "Assets/Blender/Cars", Prefabs = "Assets/Prefabs/Cars";

    [MenuItem("Tools/Wash Square/Build Car Prefabs")]
    static void BuildAll()
    {
        if (!AssetDatabase.IsValidFolder(Prefabs)) AssetDatabase.CreateFolder("Assets/Prefabs", "Cars");
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { Models }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.EndsWith(".blend")) Build(path);
        }
    }

    static void Build(string modelPath)
    {
        Transform model = ((GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath))).transform;
        PrefabUtility.UnpackPrefabInstance(model.gameObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        Transform Part(string suffix)
        {
            foreach (Transform t in model.GetComponentsInChildren<Transform>()) if (t.name.EndsWith(suffix)) return t;
            throw new System.Exception(modelPath + " has no *" + suffix);
        }
        Transform body = Part("_Body");
        string name = body.name.Substring(0, body.name.Length - "_Body".Length);
        var root = new GameObject(name);
        model.name = "Model";
        model.SetParent(root.transform, false);
        Transform[] front = { Part("_Wheel_FL"), Part("_Wheel_FR") }, rear = { Part("_Wheel_RL"), Part("_Wheel_RR") };

        // face +Z, centred between the axles, standing on y = 0
        Vector3 Middle(Transform[] pair) => (pair[0].position + pair[1].position) / 2;
        model.rotation = Quaternion.FromToRotation(Vector3.ProjectOnPlane(Middle(front) - Middle(rear), Vector3.up), Vector3.forward) * model.rotation;
        Vector3 middle = (Middle(front) + Middle(rear)) / 2;
        model.position -= new Vector3(middle.x, 0, middle.z);
        model.position -= Vector3.up * Extent(model).min.y;
        Bounds all = Extent(model);

        var car = root.AddComponent<Car>();
        car.rearWheels = rear;
        car.frontWheels = front;
        car.wheelRadius = Extent(rear[0]).extents.y;

        // the box starts just above kerb height, so the ground check under it never hits the car itself
        float bottom = car.stepHeight + 0.02f;
        var box = root.GetComponent<BoxCollider>();
        box.center = new Vector3(all.center.x, (bottom + all.max.y) / 2, all.center.z);
        box.size = new Vector3(all.size.x, all.max.y - bottom, all.size.z);

        var perch = new GameObject("Perch").transform;
        perch.SetParent(model, false);
        perch.position = new Vector3(all.center.x, all.max.y, all.center.z); // ponytail: the middle of the top; a taxi's sign or a roof rack sits there
        car.perch = perch;

        car.speed = 12;
        car.sprintSpeed = 20;
        car.acceleration = 6;
        car.backSpeed = 4;
        car.braking = 15;
        car.coasting = 2;
        car.turnRate = 60;
        car.fullTurnSpeed = 5;
        car.standingTurn = 0;
        car.maxSteer = 30;
        car.landRadius = 1f;
        car.cameraDistance = 8;
        car.knockDamage = 10;
        car.knockSpeed = 0; // any touch while moving
        car.mass = 1400;
        root.AddComponent<Rigidbody>().isKinematic = true; // a moving kinematic body pushes ragdolls and loose things properly

        Debug.Log("Built " + Prefabs + "/" + name + ": " + all.size.ToString("F2") + " m, wheel radius " + car.wheelRadius.ToString("F3"));
        PrefabUtility.SaveAsPrefabAsset(root, Prefabs + "/" + name + ".prefab");
        Object.DestroyImmediate(root);
    }

    static Bounds Extent(Transform t)
    {
        Renderer[] looks = t.GetComponentsInChildren<Renderer>();
        Bounds b = looks[0].bounds;
        foreach (Renderer r in looks) b.Encapsulate(r.bounds);
        return b;
    }
}
