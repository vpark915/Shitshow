using UnityEngine;

// Puts the birds' feet on the ground. The player's pigeon model hung 1-2 cm over it (the CharacterController keeps its
// skin width clear of the ground, and the model's feet sit a little above the capsule's bottom): on a 30 cm bird
// that reads as floating, so the model is lowered by the gap. The Seagulls (placed by hand) are dropped until their
// lowest point touches whatever's under them. Runs on every scene load, before the birds first animate; NetBird
// lowers its models by the same amount as the pigeon rig's (RigDrop), since it stands where the rig stands.
public static class BirdFeet
{
    public static float RigDrop { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Begin()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (_, _) => Ground();
        Ground();
    }

    static void Ground()
    {
        foreach (FoodPowers rig in Object.FindObjectsByType<FoodPowers>(FindObjectsSortMode.None))
        {
            Transform model = rig.transform.Find("Pigeon");
            if (!model || !rig.TryGetComponent(out CharacterController body) || !Lowest(model, out float low)) continue;
            float scale = rig.transform.lossyScale.y;
            float capsuleBottom = rig.transform.position.y + (body.center.y - body.height / 2) * scale;
            float drop = low - capsuleBottom + body.skinWidth * scale;
            if (drop <= 0 || drop > 0.2f) continue; // nothing to fix, or a pose this can't read
            model.position -= Vector3.up * drop;
            RigDrop = drop;
        }
        foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name != "Seagulls") continue;
            foreach (Transform gull in root.transform)
            {
                if (!Lowest(gull, out float low)) continue;
                float ground = float.NaN, best = float.MaxValue;
                foreach (RaycastHit hit in Physics.RaycastAll(new Vector3(gull.position.x, low + 1f, gull.position.z), Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore))
                    if (!hit.transform.IsChildOf(gull) && hit.distance < best) { best = hit.distance; ground = hit.point.y; }
                if (!float.IsNaN(ground) && Mathf.Abs(low - ground) < 0.5f) gull.position -= Vector3.up * (low - ground);
            }
        }
    }

    // the lowest point of the bird as drawn now (its first skinned mesh, posed)
    static bool Lowest(Transform bird, out float low)
    {
        low = float.MaxValue;
        var skin = bird.GetComponentInChildren<SkinnedMeshRenderer>();
        if (!skin || !skin.sharedMesh) return false;
        var posed = new Mesh();
        skin.BakeMesh(posed, true);
        foreach (Vector3 v in posed.vertices) low = Mathf.Min(low, skin.transform.TransformPoint(v).y);
        Object.Destroy(posed);
        return low < float.MaxValue;
    }
}
