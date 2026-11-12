using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// The crowd was the biggest GPU cost (profiled: ~10 ms of ~22): every person is a skinned mesh, which can't be batched,
// drawn about five times a frame (depth, surface, a shadow pass per cascade). So:
// - each person gets a one-level LOD group that stops drawing them past about DrawDistance, cross-fading them out (and
//   back in) with URP's dithered LOD fade rather than popping. It goes by their size on screen, so the LOD bias moves it
//   (Potato halves the bias, so half the distance)
// - per camera as it renders, people past ShadowDistance stop casting (a far person's shadow is a few pixels; the bake
//   has none of theirs anyway); in Potato nobody does (GraphicsQuality)
// Starts itself.
public class CrowdCulling : MonoBehaviour
{
    const float ShadowDistance = 25f, DrawDistance = 60f;
    const float FieldOfView = 60f; // the game camera's, for turning DrawDistance into a share of the screen

    readonly List<SkinnedMeshRenderer> crowd = new();
    float nextLook;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Begin()
    {
        if (FindFirstObjectByType<CrowdCulling>()) return;
        var go = new GameObject("Crowd Culling");
        DontDestroyOnLoad(go);
        go.AddComponent<CrowdCulling>();
    }

    void OnEnable() => RenderPipelineManager.beginCameraRendering += Cull;
    void OnDisable() => RenderPipelineManager.beginCameraRendering -= Cull;

    void Update()
    {
        // people come and go (a scene loads, a round resets the crowd): look again once a second
        if (Time.unscaledTime < nextLook) return;
        nextLook = Time.unscaledTime + 1;
        crowd.Clear();
        foreach (Npc npc in FindObjectsByType<Npc>(FindObjectsSortMode.None))
        {
            SkinnedMeshRenderer[] body = npc.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            crowd.AddRange(body);
            if (!npc.TryGetComponent(out LODGroup _)) FadeOut(npc.gameObject, body);
        }
    }

    // one LOD: drawn until they're as small on screen as a person DrawDistance away, then faded out
    static void FadeOut(GameObject person, Renderer[] body)
    {
        var group = person.AddComponent<LODGroup>();
        group.fadeMode = LODFadeMode.CrossFade;
        group.animateCrossFading = true;
        group.SetLODs(new[] { new LOD(0.5f, body) });
        group.RecalculateBounds();
        float screenHeight = group.size / (2f * DrawDistance * Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad));
        group.SetLODs(new[] { new LOD(screenHeight, body) });
    }

    void Cull(ScriptableRenderContext context, Camera cam)
    {
        // the game's own views (the player's camera, the menu's showcase), not the minimap's snapshot or the scene view
        if (cam.cameraType != CameraType.Game || cam.targetTexture) return;
        float shadow = GraphicsQuality.Potato ? -1 : ShadowDistance * ShadowDistance;
        Vector3 eye = cam.transform.position;
        foreach (SkinnedMeshRenderer r in crowd)
        {
            if (!r) continue;
            r.shadowCastingMode = (r.transform.position - eye).sqrMagnitude > shadow ? ShadowCastingMode.Off : ShadowCastingMode.On;
        }
    }
}
