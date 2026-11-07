using UnityEngine;
using UnityEngine.InputSystem;

// The Mechanics Gym's Escape menu (the gym has no HuntMatch): stops the game and frees the cursor, with a way back to
// the starting menu (the first scene in the build).
public class GymMenu : MonoBehaviour
{
    bool paused;
    MonoBehaviour[] rig; // the bird's input, gun, riding, food and eating: off while paused, so a click doesn't shoot

    void Start()
    {
        var powers = FindFirstObjectByType<FoodPowers>();
        rig = powers ? powers.GetComponentsInChildren<MonoBehaviour>() : new MonoBehaviour[0];
        rig = System.Array.FindAll(rig, b => b is PlayerInput or StarterAssets.StarterAssetsInputs or Weapon or HeadRide or FoodPowers or PieceEater);
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) Pause(!paused);
    }

    void OnDestroy() => Time.timeScale = 1;

    void Pause(bool on)
    {
        paused = on;
        Time.timeScale = on ? 0 : 1;
        foreach (MonoBehaviour b in rig) if (b) b.enabled = !on;
        Cursor.lockState = on ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = on;
    }

    void OnGUI()
    {
        if (!paused) return;
        float scale = Screen.height / 720f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
        GUILayout.BeginArea(new Rect(20, 20, 360, 200), GUI.skin.box);
        GUILayout.Label("<b>Mechanics Gym</b>", new GUIStyle(GUI.skin.label) { fontSize = 20, richText = true });
        if (GUILayout.Button("Resume", GUILayout.Height(36))) Pause(false);
        if (GUILayout.Button("Back to menu", GUILayout.Height(30))) UnityEngine.SceneManagement.SceneManager.LoadScene(0);
        if (GUILayout.Button("Quit game", GUILayout.Height(30)))
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
        GUILayout.EndArea();
    }
}
