using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools > Wash Square > Place Street Furniture, in the open WashSquareHunt scene: NYC street name blades on the lamp
// posts round the park edge (crossed pairs, as at a real corner; brown Greenwich Village Historic District blades on the
// north and west sides), One Way and Don't Honk signs, the Broadway guide sign, and at each park corner a traffic signal
// post with its mast arm over one street (pinwheeling round) and a walk signal post by the crosswalk. Everything goes
// under "Street Furniture"; run again to rebuild it.
static class StreetFurnitureTool
{
    const string TrafficModel = "Assets/Blender/Street/trafficSignal.blend", WalkModel = "Assets/Blender/Street/walkSignal.blend";
    // the park-side sidewalk corners: curbs at x = +-150, z = +-72 (a step in from them)
    const float CornerX = 149.3f, CornerZ = 71.3f, Sidewalk = 0.15f;

    static readonly Vector3 X = Vector3.right, Z = Vector3.forward;
    static readonly HashSet<string> Historic = new() { "street_washington_sq_n", "street_washington_sq_w", "street_5_av", "street_waverly_pl", "street_macdougal_st" };

    [MenuItem("Tools/Wash Square/Place Street Furniture")]
    static void Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        Transform old = GameObject.Find("Street Furniture")?.transform;
        if (old) Object.DestroyImmediate(old.gameObject);
        var root = new GameObject("Street Furniture").transform;
        Undo.RegisterCreatedObjectUndo(root.gameObject, "Place Street Furniture");

        var lamps = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Where(t => t.name.StartsWith("WSP_Prop_Lamp")).ToList();
        if (lamps.Count == 0) { Debug.LogError("Place Street Furniture: no WSP_Prop_Lamp in this scene"); return; }
        var used = new HashSet<Transform>();
        Transform Lamp(float x, float z)
        {
            Transform best = lamps.Where(l => !used.Contains(l)).OrderBy(l => (l.position.x - x) * (l.position.x - x) + (l.position.z - z) * (l.position.z - z)).First();
            used.Add(best);
            return best;
        }

        // ---- street name blades: the streets round Washington Square, on the lamps nearest each spot ----
        Blades(root, Lamp(-142, 62), ("street_washington_sq_n", X), ("street_washington_sq_w", Z));
        Blades(root, Lamp(5, 64), ("street_5_av", Z), ("street_washington_sq_n", X));
        Blades(root, Lamp(142, 62), ("street_washington_sq_n", X), ("street_university_pl", Z));
        Blades(root, Lamp(-73, 65), ("street_waverly_pl", X));
        Blades(root, Lamp(146, -3), ("street_washington_sq_e", Z));
        Blades(root, Lamp(-141, -3), ("street_washington_sq_w", Z));
        Blades(root, Lamp(141, -66), ("street_washington_sq_s", X), ("street_laguardia_pl", Z));
        Blades(root, Lamp(13, -68), ("street_w_4_st", X), ("street_thompson_st", Z));
        Blades(root, Lamp(-17, -69), ("street_washington_sq_s", X), ("street_sullivan_st", Z));
        Blades(root, Lamp(-142, -61), ("street_w_4_st", X), ("street_macdougal_st", Z));

        // ---- regulatory and guide signs, lower down the poles ----
        // the one-way streets round the park run anticlockwise seen from above: west along the north side, south down
        // the west side, east along the south side, north up the east side
        OneWay(root, Lamp(46, 67), -X);
        OneWay(root, Lamp(-140, 20), -Z);
        OneWay(root, Lamp(-50, -66), X);
        OneWay(root, Lamp(139, 20), Z);
        Mounted(root, Lamp(-60, 65), "regulatory_dont_honk", 2.2f, 0.62f, Z);
        Mounted(root, Lamp(98, -66), "regulatory_dont_honk", 2.2f, 0.62f, -Z);
        Mounted(root, Lamp(139, -30), "guide_broadway", 3.0f, 0.62f, -Z); // read facing north, the arrow points east

        // ---- signals at the park corners ----
        var traffic = AssetDatabase.LoadAssetAtPath<GameObject>(TrafficModel);
        var walk = AssetDatabase.LoadAssetAtPath<GameObject>(WalkModel);
        foreach (var (sx, sz, arm) in new[] { (1, 1, Z), (-1, 1, -X), (-1, -1, -Z), (1, -1, X) })
        {
            var corner = new Vector3(sx * CornerX, Sidewalk, sz * CornerZ);
            // a head controls the traffic coming at it; the mast arm (the model's -X) reaches out over the street
            Vector3 faces = Vector3.Cross(-arm, Vector3.up);
            Post(root, traffic, "Traffic Signal", Clear(corner, new Vector3(-sx, 0, 0)), Quaternion.LookRotation(faces));
            // the walk post a few steps along the curb, one head across each street
            Vector3 at = Clear(corner + new Vector3(-sx * 4.5f, 0, 0), new Vector3(-sx, 0, 0));
            Post(root, walk, "Walk Signal", at, WalkYaw(new Vector3(sx, 0, 0), new Vector3(0, 0, sz)));
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Place Street Furniture: " + root.childCount + " pieces placed and saved");
    }

    // crossed blades up a lamp post, the first on top; each reaches out from the pole along its street, away from the
    // park's middle
    static void Blades(Transform root, Transform lamp, params (string art, Vector3 along)[] blades)
    {
        float y = 3.6f;
        foreach (var (art, along) in blades)
        {
            Texture2D tex = StreetFurniture.Art(art);
            float h = Historic.Contains(art) ? 0.3f : 0.26f; // the district band makes those a little taller (339 px to 298 in Figma)
            Vector2 size = StreetFurniture.Size(tex);
            float w = h * size.x / size.y;
            Vector3 outward = Vector3.Dot(lamp.position, along) >= 0 ? along : -along;
            Vector3 at = lamp.position + Vector3.up * y + outward * (w / 2 + 0.11f);
            StreetFurniture.Sign(art, root, at, Quaternion.LookRotation(Vector3.Cross(along, Vector3.up)), h, twoSided: true).name = art.Replace("street_", "Blade ") + " (" + lamp.name + ")";
            y -= 0.34f;
        }
    }

    // a ONE WAY sign whose arrow points this way, facing the street beside the lamp
    static void OneWay(Transform root, Transform lamp, Vector3 arrow)
    {
        // read standing in front, the art's arrow points to the reader's right: the sign faces -(arrow x up)
        Vector3 faces = -Vector3.Cross(arrow, Vector3.up);
        if (Vector3.Dot(faces, lamp.position) < 0) faces = -faces; // toward the street, which is away from the park
        float h = 0.3f;
        StreetFurniture.Sign("regulatory_one_way", root, lamp.position + Vector3.up * 2.75f + faces * 0.11f, Quaternion.LookRotation(faces), h)
            .name = "One Way (" + lamp.name + ")";
    }

    static void Mounted(Transform root, Transform lamp, string art, float y, float height, Vector3 faces)
    {
        StreetFurniture.Sign(art, root, lamp.position + Vector3.up * y + faces * 0.11f, Quaternion.LookRotation(faces), height)
            .name = art + " (" + lamp.name + ")";
    }

    static void Post(Transform root, GameObject model, string name, Vector3 at, Quaternion rotation)
    {
        var post = (GameObject)PrefabUtility.InstantiatePrefab(model, root);
        post.name = name;
        post.transform.SetPositionAndRotation(at, rotation * post.transform.localRotation);
        post.AddComponent<TrafficSignal>();
        // solid pole for the player and bullets (the heads and arm are left open)
        var pole = new GameObject("Pole Collider").AddComponent<CapsuleCollider>();
        pole.transform.SetParent(post.transform, false);
        pole.transform.position = at;
        pole.transform.rotation = Quaternion.identity;
        pole.direction = 1;
        pole.radius = 0.12f;
        pole.height = name.StartsWith("Traffic") ? 5.6f : 3.6f;
        pole.center = Vector3.up * pole.height / 2;
    }

    // the walk post's heads: A faces the model's +Z, B its -X; turn it so they face across the two streets
    static Quaternion WalkYaw(Vector3 acrossSide, Vector3 acrossEnd)
    {
        for (int yaw = 0; yaw < 360; yaw += 90)
        {
            Quaternion q = Quaternion.Euler(0, yaw, 0);
            Vector3 a = q * Vector3.forward, b = q * Vector3.left;
            bool hit = (Vector3.Dot(a, acrossSide) > 0.9f && Vector3.Dot(b, acrossEnd) > 0.9f) || (Vector3.Dot(a, acrossEnd) > 0.9f && Vector3.Dot(b, acrossSide) > 0.9f);
            if (hit) return q;
        }
        return Quaternion.identity;
    }

    // at, or a step further along `away` until nothing (tree pit, lamp, bench) stands there
    static Vector3 Clear(Vector3 at, Vector3 away)
    {
        for (int i = 0; i < 8; i++)
        {
            bool blocked = Physics.OverlapCapsule(at + Vector3.up * 0.6f, at + Vector3.up * 2.5f, 0.35f)
                .Any(c => !c.isTrigger && !(c is CharacterController) && c.bounds.size.y > 0.4f && c.bounds.size.x < 40 && c.bounds.size.z < 40);
            if (!blocked) return at;
            at += away * 1f;
        }
        return at;
    }
}
