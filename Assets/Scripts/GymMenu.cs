using UnityEngine;
using UnityEngine.InputSystem;

// The Mechanics Gym's Escape menu (the gym has no HuntMatch): stops the game and frees the cursor, with a way back to
// the starting menu (the first scene in the build).
public class GymMenu : MonoBehaviour
{
    [Tooltip("Heading on the Escape menu")]
    public string title = "Mechanics Gym";
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
        Music.Open = paused;
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
        SubwayGUI.Scale();
        var sign = new Rect(20, 20, 380, 284);
        SubwayGUI.Begin(sign);
        SubwayGUI.Title(title);
        if (SubwayGUI.Button("Resume")) Pause(false);
        if (SubwayGUI.Button("Settings")) GameSettings.Showing = !GameSettings.Showing;
        if (SubwayGUI.Button("Back to menu")) Loading.Load(0);
        if (SubwayGUI.Button("Quit game"))
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
        SubwayGUI.End();
        GameSettings.Panel(sign);
    }
}
