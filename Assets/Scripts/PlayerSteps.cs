using UnityEngine;
using StarterAssets;

// The player's pigeon running about: "player step" at its feet every stride while it's on the ground and moving under
// its own steam (not flying, not riding) and the move keys are held: let go and the step that's sounding is cut off,
// rather than steps trailing on while the bird coasts to a stop. The bird has no foot bones to read (its legs swing
// from the hip), so the steps come from its speed: one per Stride metres, never faster than Fastest apart. Put on every
// player rig by itself.
public class PlayerSteps : MonoBehaviour
{
    public string sound = "player step";
    public float stride = 0.35f, fastest = 0.11f, minSpeed = 0.4f;

    ThirdPersonController mover;
    StarterAssetsInputs input;
    AudioSource sounding;
    CharacterController body;
    HeadRide ride;
    Health health;
    float travelled;
    float lastStep;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Begin()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (_, _) => Attach();
        Attach();
    }

    static void Attach()
    {
        foreach (FoodPowers rig in FindObjectsByType<FoodPowers>(FindObjectsSortMode.None))
            if (!rig.GetComponent<PlayerSteps>()) rig.gameObject.AddComponent<PlayerSteps>();
    }

    void Start()
    {
        mover = GetComponent<ThirdPersonController>();
        input = GetComponent<StarterAssetsInputs>();
        body = GetComponent<CharacterController>();
        ride = GetComponent<HeadRide>();
        health = GetComponent<Health>();
    }

    void Update()
    {
        bool walking = mover && mover.enabled && mover.Grounded && body && body.enabled
            && !(ride && ride.Riding) && !(health && health.IsDead) && (!input || input.move.sqrMagnitude > 0.01f);
        float speed = walking ? new Vector2(body.velocity.x, body.velocity.z).magnitude : 0;
        if (speed < minSpeed)
        {
            travelled = stride * 0.7f; // the first step comes soon after setting off
            if (sounding && sounding.isPlaying && sounding.clip && sounding.clip.name.StartsWith("player_step")) sounding.Stop();
            sounding = null;
            return;
        }
        travelled += speed * Time.deltaTime;
        if (travelled < stride || Time.time - lastStep < fastest) return;
        travelled = 0;
        lastStep = Time.time;
        sounding = Sfx.Play(sound, transform.position);
    }
}
