using UnityEngine;

// Keeps the cursor locked to the game and hidden whenever no menu is up (SubwayGUI.MenuOpen), in every scene, so it
// never gets left loose after alt-tabbing, a click outside the window or Escape in the editor; while a menu is up it's
// free and showing. Starts itself: nothing to put in a scene.
public class CursorLock : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Begin()
    {
        var go = new GameObject("Cursor Lock") { hideFlags = HideFlags.HideInHierarchy };
        DontDestroyOnLoad(go);
        go.AddComponent<CursorLock>();
    }

    void LateUpdate()
    {
        bool free = SubwayGUI.MenuOpen || !Application.isFocused;
        CursorLockMode mode = free ? CursorLockMode.None : CursorLockMode.Locked;
        if (Cursor.lockState != mode) Cursor.lockState = mode;
        if (Cursor.visible != free) Cursor.visible = free;
    }
}
