using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// Behind the starting menu (HuntMatch): the live park as a collage, one full-screen view cutting between slow drifting
// shots. Wide ones (the fountain, the arch, the food trucks, a street corner, overhead, the whole city from high up)
// take turns with close ones (the pigeon and the seagull from angled fronts, a flock pecking or taking off). The HUD
// and minimap are hidden: only the menu's sign shows. When a game starts, the view flies from wherever it is onto the
// pigeon's own camera, and then the HUD comes back. Added by HuntMatch; runs after Cinemachine has placed the camera.
[DefaultExecutionOrder(10000)]
public class MenuShowcase : MonoBehaviour
{
    public float shotTime = 5.5f, flyIn = 1.6f;

    struct Shot
    {
        public Vector3 from, to, lookFrom, lookTo;
        public float fov; // 0: the game camera's own
    }

    HuntMatch match;
    Camera main, view;
    Canvas hud;
    readonly List<Func<Shot>> shots = new();
    int index;
    float shotAt, leaveAt = -1;
    bool showing;
    Vector3 leavePos;
    Quaternion leaveRot;
    float leaveFov;
    int mainMask;
    bool mainPost;

    void Start()
    {
        match = GetComponent<HuntMatch>();
        main = Camera.main;
        var map = FindFirstObjectByType<Minimap>(FindObjectsInactive.Include);
        hud = map ? map.GetComponentInParent<Canvas>() : null;
        Shots();
        if (match && match.InStartMenu && main && shots.Count > 0) Show();
    }

    // ---- the shots ----

    static Transform Named(string prefix)
    {
        foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (Transform t in root.GetComponentsInChildren<Transform>())
                if (t.name.StartsWith(prefix)) return t;
        return null;
    }

    static Shot Drift(Vector3 from, Vector3 to, Vector3 look) => new Shot { from = from, to = to, lookFrom = look, lookTo = look };

    void Shots()
    {
        var wide = new List<Func<Shot>>();
        var close = new List<Func<Shot>>();

        if (Named("WSP_Fountain") is Transform fountain)
        {
            Vector3 c = fountain.position;
            wide.Add(() => Drift(c + new Vector3(18, 3, -14), c + new Vector3(12, 3.4f, -19), c + Vector3.up * 2));
        }
        // the whole city, from high over its south side
        wide.Add(() => Drift(new Vector3(-170, 150, -280), new Vector3(-90, 160, -290), new Vector3(0, 0, 20)));
        if (Named("WSP_Arch") is Transform arch)
        {
            Vector3 a = arch.position;
            Vector3 away = new Vector3(-a.x, 0, -a.z).normalized; // toward the park's middle, so the arch faces us
            if (away.sqrMagnitude < 0.1f) away = Vector3.back;
            Vector3 side = Vector3.Cross(Vector3.up, away);
            wide.Add(() => Drift(a + away * 34 + side * -5 + Vector3.up * 2.2f, a + away * 29 + side * 4 + Vector3.up * 2.8f, a + Vector3.up * 9));
        }
        if (GameObject.Find("FoodVendors") is GameObject vendors && vendors.transform.childCount > 0)
        {
            Transform cart = vendors.transform.GetChild(0);
            Vector3 p = cart.position, toPark = new Vector3(-p.x, 0, -p.z).normalized, side = Vector3.Cross(Vector3.up, toPark);
            wide.Add(() => Drift(p + toPark * 9 + side * 5 + Vector3.up * 1.8f, p + toPark * 9 - side * 4 + Vector3.up * 2, p + Vector3.up * 1.8f));
        }
        // overhead, sliding along above the south street (clear of the buildings either side)
        wide.Add(() => Drift(new Vector3(-55, 46, -79), new Vector3(55, 46, -79), new Vector3(0, 0, 10)));
        if (Named("Traffic Signal") is Transform signal)
        {
            Vector3 p = signal.position, toPark = new Vector3(-p.x, 0, -p.z).normalized, side = Vector3.Cross(Vector3.up, toPark);
            wide.Add(() => Drift(p + toPark * 10 + side * 5 + Vector3.up * 1.6f, p + toPark * 8 + side * 1 + Vector3.up * 2.2f, p + Vector3.up * 3));
        }

        var rig = FindFirstObjectByType<FoodPowers>();
        Transform pigeon = rig ? rig.transform.Find("Pigeon") : null;
        Transform gull = null;
        if (GameObject.Find("Seagulls") is GameObject gullRoot)
            foreach (Transform g in gullRoot.transform) if (g.gameObject.activeInHierarchy) { gull = g; break; }
        var pigeonShots = pigeon ? CloseUps(pigeon) : new List<Func<Shot>>();
        var gullShots = gull ? CloseUps(gull) : new List<Func<Shot>>();
        for (int i = 0; i < Mathf.Max(pigeonShots.Count, gullShots.Count); i++)
        {
            if (i < pigeonShots.Count) close.Add(pigeonShots[i]);
            if (i < gullShots.Count) close.Add(gullShots[i]);
        }
        if (FindFirstObjectByType<BirdFlocks>() is BirdFlocks flocks) close.Insert(Mathf.Min(2, close.Count), Flock(flocks));

        // wide and close by turns
        for (int i = 0; i < Mathf.Max(wide.Count, close.Count); i++)
        {
            if (i < wide.Count) shots.Add(wide[i]);
            if (i < close.Count) shots.Add(close[i]);
        }
    }

    // angled fronts of a bird: three-quarters left and right, and a low hero angle; each turns a little round it
    static List<Func<Shot>> CloseUps(Transform bird)
    {
        Transform head = null;
        foreach (Transform t in bird.GetComponentsInChildren<Transform>()) if (t.name == "head") { head = t; break; }
        float h = 0.3f;
        foreach (Renderer r in bird.GetComponentsInChildren<SkinnedMeshRenderer>()) { h = r.bounds.size.y; break; }
        Shot Around(float yaw, float turn, float lift, float distance)
        {
            Vector3 aim = (head ? head.position : bird.position + Vector3.up * h * 0.7f) - Vector3.up * h * 0.12f;
            Vector3 Dir(float a) => Quaternion.AngleAxis(a, Vector3.up) * Vector3.ProjectOnPlane(bird.forward, Vector3.up).normalized;
            return new Shot
            {
                from = aim + Dir(yaw) * h * distance + Vector3.up * h * lift,
                to = aim + Dir(yaw + turn) * h * distance + Vector3.up * h * lift,
                lookFrom = aim, lookTo = aim,
                fov = 34,
            };
        }
        return new List<Func<Shot>> { () => Around(35, 14, 0.25f, 3f), () => Around(-40, -12, 0.15f, 2.8f), () => Around(18, 10, -0.25f, 2.5f) };
    }

    // a flock up close, following one of its birds round as it pecks (or takes off); picks another if that one's gone
    static Func<Shot> Flock(BirdFlocks flocks)
    {
        Transform followed = null;
        float side = 1;
        return () =>
        {
            if (!followed || !followed.gameObject.activeInHierarchy || !followed.GetComponent<Health>())
            {
                var birds = flocks.GetComponentsInChildren<Health>();
                followed = birds.Length > 0 ? birds[UnityEngine.Random.Range(0, birds.Length)].transform : null;
                side = UnityEngine.Random.value < 0.5f ? -1 : 1;
            }
            if (!followed) return Drift(new Vector3(0, 30, -60), new Vector3(10, 30, -60), Vector3.zero);
            Vector3 b = followed.position + Vector3.up * 0.2f;
            Vector3 back = -Vector3.ProjectOnPlane(followed.forward, Vector3.up).normalized;
            Vector3 across = Vector3.Cross(Vector3.up, back) * side;
            return new Shot { from = b + back * 2.4f + across * 1.2f + Vector3.up * 0.9f, to = b + back * 1.6f + across * 2f + Vector3.up * 1.1f, lookFrom = b, lookTo = b, fov = 45 };
        };
    }

    // ---- the camera ----

    void Show()
    {
        showing = true;
        view = new GameObject("Menu Showcase Camera").AddComponent<Camera>();
        view.CopyFrom(main);
        view.depth = main.depth + 1;
        var mine = view.GetUniversalAdditionalCameraData();
        var theirs = main.GetUniversalAdditionalCameraData();
        mine.renderPostProcessing = theirs.renderPostProcessing;
        mine.antialiasing = theirs.antialiasing;
        mine.volumeLayerMask = theirs.volumeLayerMask;
        mine.renderShadows = theirs.renderShadows;
        // the game camera stays on (Camera.main is what the minimap, prompts and gun go by; Cinemachine keeps placing
        // it, and the fly-in ends there) but draws nothing under the showcase
        mainMask = main.cullingMask;
        main.cullingMask = 0;
        var mainData = main.GetUniversalAdditionalCameraData();
        mainPost = mainData.renderPostProcessing;
        mainData.renderPostProcessing = false;
        if (hud) hud.enabled = false;
        shotAt = Time.time;
        index = UnityEngine.Random.Range(0, shots.Count);
    }

    void Place(Shot s, float t)
    {
        t = t * t * (3 - 2 * t);
        Vector3 at = Vector3.Lerp(s.from, s.to, t), look = Vector3.Lerp(s.lookFrom, s.lookTo, t);
        view.transform.SetPositionAndRotation(at, Quaternion.LookRotation(look - at));
        view.fieldOfView = s.fov > 0 ? s.fov : main.fieldOfView;
    }

    void LateUpdate()
    {
        if (!showing) return;
        float now = Time.time;
        if (leaveAt < 0 && !match.InStartMenu)
        {
            // a game's starting: fly the view onto the pigeon
            leaveAt = now;
            leavePos = view.transform.position;
            leaveRot = view.transform.rotation;
            leaveFov = view.fieldOfView;
        }
        if (leaveAt >= 0)
        {
            float k = Mathf.Clamp01((now - leaveAt) / flyIn);
            k = k * k * (3 - 2 * k);
            view.transform.SetPositionAndRotation(Vector3.Lerp(leavePos, main.transform.position, k), Quaternion.Slerp(leaveRot, main.transform.rotation, k));
            view.fieldOfView = Mathf.Lerp(leaveFov, main.fieldOfView, k);
            if (k >= 1)
            {
                main.cullingMask = mainMask;
                main.GetUniversalAdditionalCameraData().renderPostProcessing = mainPost;
                if (hud) hud.enabled = true;
                Destroy(view.gameObject);
                showing = false;
            }
            return;
        }

        if (now - shotAt > shotTime) { shotAt = now; index = (index + 1) % shots.Count; }
        Place(shots[index](), (now - shotAt) / shotTime);
    }
}
