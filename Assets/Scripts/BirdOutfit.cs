using System.Collections.Generic;
using UnityEngine;

// The players' birds in costume (Blender/Outfits, Resources/Outfits): the Pigeon is the detective, in a deerstalker;
// the Seagull wears a red headband whose two tails stream out behind it. Park
// birds stay plain. Dress() puts one on a Quirky bird model (root with Rig/root/body/head bones and a skinned mesh):
// the outfit was modelled in that bird's own space, so it's lined up through the bird's rest pose and its anchor bone
// rides the bird's head bone. The loose bones (the headband's tails) are springs: each swings toward where the
// bird's motion leaves it, trailing behind as the bird moves, with gravity and a slight breeze.
// It dresses itself in every scene: the player's pigeon, the Seagulls, and NetBird's models online.
public class BirdOutfit : MonoBehaviour
{
    public enum Look { DetectiveHat, Headband }

    class Spring
    {
        public Transform bone, child;
        public Quaternion rest;
        public Vector3 tip, last;
        public float length, phase;
    }

    Look look;
    readonly List<Spring> springs = new();
    Renderer[] birdLooks, mine;
    float stiffness, damping, gravity, breeze;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Begin()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (_, _) => DressScene();
        DressScene();
    }

    static void DressScene()
    {
        foreach (FoodPowers rig in FindObjectsByType<FoodPowers>(FindObjectsSortMode.None))
            if (rig.transform.Find("Pigeon") is Transform pigeon) Dress(pigeon.gameObject, Look.DetectiveHat);
        foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            if (root.name == "Seagulls")
                foreach (Transform gull in root.transform) Dress(gull.gameObject, Look.Headband);
    }

    public static void Dress(GameObject bird, Look look)
    {
        if (!bird || bird.GetComponent<BirdOutfit>()) return;
        var prefab = Resources.Load<GameObject>(look == Look.Headband ? "Outfits/SeagullHeadband" : "Outfits/PigeonHat");
        if (!prefab) { Debug.LogWarning("BirdOutfit: no Resources/Outfits prefab for " + look); return; }
        bird.AddComponent<BirdOutfit>().Wear(prefab, look);
    }

    void Wear(GameObject prefab, Look look)
    {
        this.look = look;
        var birdSkin = GetComponentInChildren<SkinnedMeshRenderer>(true);
        Transform anchorBone = Find(transform, "head");
        if (!birdSkin || !anchorBone) { Debug.LogWarning("BirdOutfit: " + name + " isn't a Quirky bird (no skinned mesh or bones)"); Destroy(this); return; }
        // the bird's own body only: the player's rig carries its gun in here too, and stays visible as the Seagull
        // (whose pigeon is hidden) only for the gun, which mustn't keep the hat showing
        birdLooks = GetComponentsInChildren<SkinnedMeshRenderer>(true);

        // the outfit sits in the bird's model space: under the bird, at no offset
        GameObject outfit = Instantiate(prefab, transform);
        outfit.name = prefab.name;
        outfit.transform.localPosition = Vector3.zero;
        // turned round: Unity brings a .blend in facing the other way to the birds' FBX (the same Blender front ends up
        // -Z here and +Z on the bird)
        outfit.transform.localRotation = Quaternion.Euler(0, 180, 0);
        outfit.transform.localScale = Vector3.one;
        mine = outfit.GetComponentsInChildren<Renderer>(true);
        foreach (var r in mine)
            if (r is SkinnedMeshRenderer s) s.updateWhenOffscreen = true; // its bones live on the bird now

        // the bird's anchor bone in its model space. Dressing happens before the bird's first animated frame, so the
        // bones are still in the rest pose the outfit was modelled on (not its bind poses: scenes override some birds'
        // mesh object scales, which skinning ignores but the bind poses don't)
        Matrix4x4 boneInModel = transform.worldToLocalMatrix * anchorBone.localToWorldMatrix;
        // the outfit's own anchor bone rides the bird's: the same offset from it as at bind time
        Transform anchor = Find(outfit.transform, look == Look.Headband ? "Band" : "Hat");
        Matrix4x4 anchorInModel = transform.worldToLocalMatrix * anchor.localToWorldMatrix;
        Matrix4x4 local = boneInModel.inverse * anchorInModel;
        anchor.SetParent(anchorBone, false);
        anchor.localPosition = local.GetColumn(3);
        anchor.localRotation = local.rotation;
        anchor.localScale = local.lossyScale;

        // springs: every bone of the outfit that has a child (each chain ends in an *_end bone)
        foreach (Transform bone in anchor.GetComponentsInChildren<Transform>())
            if (bone != anchor && bone.childCount > 0 && !bone.name.EndsWith("_end"))
                springs.Add(new Spring { bone = bone, child = bone.GetChild(0), rest = bone.localRotation, phase = Random.value * 10 });
        stiffness = 0.06f; damping = 0.1f; gravity = 3f; breeze = 0.015f;
        foreach (Spring s in springs)
        {
            s.length = Vector3.Distance(s.bone.position, s.child.position);
            s.tip = s.last = s.child.position;
        }
    }

    static Transform Find(Transform under, string name)
    {
        foreach (Transform t in under.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    // after the bird is posed: each spring bone first goes back to its pose on the bird, then turns toward its tip,
    // which chases where that pose puts it (down the chain, root first, so each link swings from its parent's swing)
    void LateUpdate()
    {
        if (mine == null) return;
        bool shown = false;
        foreach (Renderer r in birdLooks) if (r && r.enabled) { shown = true; break; }
        foreach (Renderer r in mine) if (r) r.enabled = shown; // hidden with the bird (the Seagull player's own pigeon rig)

        float dt = Mathf.Min(Time.deltaTime, 1 / 30f);
        if (dt <= 0) return;
        float scale = transform.lossyScale.x;
        foreach (Spring s in springs)
        {
            s.bone.localRotation = s.rest;
            Vector3 root = s.bone.position, target = s.child.position;
            // the breeze: a slight sideways sway, a little different for each bone
            target += s.bone.right * (Mathf.Sin(Time.time * 1.7f + s.phase) * breeze * s.length * 6);
            Vector3 velocity = (s.tip - s.last) * (1 - damping);
            s.last = s.tip;
            s.tip += velocity + Vector3.down * gravity * scale * dt * dt + (target - s.tip) * stiffness;
            // keep its length
            Vector3 along = s.tip - root;
            if (along.sqrMagnitude < 1e-10f) along = target - root;
            s.tip = root + along.normalized * s.length;
            s.bone.rotation = Quaternion.FromToRotation(target - root, s.tip - root) * s.bone.rotation;
        }
    }
}
