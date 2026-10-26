using UnityEditor;
using UnityEngine;

// Tools > Wash Square > Build Citi Bike Prefab: makes Prefabs/CitiBike from Blender/CitiBike/citiBike.blend, facing +Z with its
// wheels on the ground, the front wheel moved under the bars so it steers, a perch on the saddle, a body and CitiBike.
// Run it again after changing the .blend. It also makes Prefabs/CitiBikeDock from Blender/citiBikeDock.blend: one post
// on its plate, with a Slot where a docked bike stands (front wheel against the post), and Station() lines docks up.
public static class CitiBikePrefab
{
    const string Model = "Assets/Blender/CitiBike/citiBike.blend", Prefab = "Assets/Prefabs/CitiBike.prefab";
    const string DockModel = "Assets/Blender/CitiBike/citiBikeDock.blend", DockPrefab = "Assets/Prefabs/CitiBikeDock.prefab";
    public const float DockSpacing = 0.75f; // a plate is 0.74 m wide

    [MenuItem("Tools/Wash Square/Build Citi Bike Prefab")]
    static void Build()
    {
        var root = new GameObject("CitiBike");
        Transform model = ((GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Model))).transform;
        PrefabUtility.UnpackPrefabInstance(model.gameObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        model.name = "Model";
        model.SetParent(root.transform, false);
        Transform Part(string name)
        {
            foreach (Transform t in model.GetComponentsInChildren<Transform>()) if (t.name == "CitiBike_" + name) return t;
            throw new System.Exception(Model + " has no CitiBike_" + name);
        }
        Transform rear = Part("Wheel_R"), front = Part("Wheel_F"), bars = Part("Steer"), seat = Part("Seat"), crank = Part("Crank");

        // face +Z, centred between the wheels, standing on y = 0
        Vector3 along = Vector3.ProjectOnPlane(front.position - rear.position, Vector3.up);
        model.rotation = Quaternion.FromToRotation(along, Vector3.forward) * model.rotation;
        Vector3 middle = (front.position + rear.position) / 2;
        model.position -= new Vector3(middle.x, 0, middle.z);
        Bounds all = Extent(model);
        model.position -= Vector3.up * all.min.y;

        front.SetParent(bars, true); // steers with the bars

        var perch = new GameObject("Perch").transform;
        Bounds saddle = Extent(seat);
        perch.position = new Vector3(saddle.center.x, saddle.max.y, saddle.center.z);
        perch.SetParent(seat, true);

        // ponytail: one upright capsule up to the saddle; see CitiBike
        var body = root.AddComponent<CharacterController>();
        body.radius = 0.3f;
        body.skinWidth = 0.03f;
        body.height = perch.position.y - body.skinWidth;
        body.center = Vector3.up * (body.height / 2 + body.skinWidth);
        body.stepOffset = 0.3f;

        var bike = root.AddComponent<CitiBike>();
        bike.perch = perch;
        bike.rearWheel = rear;
        bike.frontWheel = front;
        bike.crank = crank;
        bike.bars = bars;
        bike.lean = model;
        bike.wheelRadius = Extent(rear).extents.y;
        root.AddComponent<Rigidbody>().isKinematic = true; // turned dynamic while knocked over (Vehicle.Knock)

        float reach = Extent(front).max.z; // the root is at the origin, facing +Z
        PrefabUtility.SaveAsPrefabAsset(root, Prefab);
        Object.DestroyImmediate(root);
        Debug.Log("Built " + Prefab + ": " + all.size.ToString("F2") + " m, wheel radius " + bike.wheelRadius.ToString("F3"));
        BuildDock(reach);
    }

    // reach: how far in front of a bike's root its front wheel ends
    static void BuildDock(float reach)
    {
        var root = new GameObject("CitiBikeDock");
        Transform model = ((GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(DockModel))).transform;
        PrefabUtility.UnpackPrefabInstance(model.gameObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        model.name = "Model";
        model.SetParent(root.transform, false);
        Transform plate = null, post = null;
        foreach (Transform t in model.GetComponentsInChildren<Transform>())
        {
            if (t.name == "CitiBikeDock_Plate") plate = t;
            if (t.name == "CitiBikeDock_Post") post = t;
        }

        // the post sits off the plate's middle toward the side the bike stands on: turn that side to -Z
        Vector3 bikeSide = Vector3.ProjectOnPlane(Extent(post).center - Extent(plate).center, Vector3.up);
        float turn = Mathf.Round(Vector3.SignedAngle(bikeSide, Vector3.back, Vector3.up) / 90) * 90; // square to the plate
        model.rotation = Quaternion.Euler(0, turn, 0) * model.rotation;
        Vector3 middle = Extent(plate).center;
        model.position -= new Vector3(middle.x, Extent(model).min.y, middle.z);

        Bounds postBox = Extent(post);
        var solid = root.AddComponent<BoxCollider>();
        solid.center = postBox.center;
        solid.size = postBox.size;

        // a docked bike: its front wheel's front against the post's face
        var slot = new GameObject("Slot").transform;
        slot.SetParent(root.transform, false);
        slot.localPosition = new Vector3(0, 0, postBox.min.z - reach);

        Debug.Log("Built " + DockPrefab + ": slot " + slot.localPosition.z.ToString("F2") + " m in front of the plate");
        PrefabUtility.SaveAsPrefabAsset(root, DockPrefab);
        Object.DestroyImmediate(root);
    }

    // a row of `docks` docks along the station's right, bikes facing its forward; each dock has a bike with chance `filled`
    public static Transform Station(Transform parent, Vector3 position, Quaternion rotation, int docks, float filled, System.Random random)
    {
        var station = new GameObject("CitiBikeStation").transform;
        station.SetParent(parent, false);
        station.SetPositionAndRotation(position, rotation);
        GameObject dock = AssetDatabase.LoadAssetAtPath<GameObject>(DockPrefab), bike = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
        for (int i = 0; i < docks; i++)
        {
            var one = (GameObject)PrefabUtility.InstantiatePrefab(dock, station);
            one.transform.localPosition = Vector3.right * (i - (docks - 1) / 2f) * DockSpacing;
            if (random.NextDouble() >= filled) continue;
            Transform slot = one.transform.Find("Slot");
            var parked = (GameObject)PrefabUtility.InstantiatePrefab(bike, station);
            parked.transform.SetPositionAndRotation(slot.position, slot.rotation);
        }
        return station;
    }

    static Bounds Extent(Transform t)
    {
        Renderer[] looks = t.GetComponentsInChildren<Renderer>();
        Bounds b = looks[0].bounds;
        foreach (Renderer r in looks) b.Encapsulate(r.bounds);
        return b;
    }
}
