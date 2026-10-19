using System;
using UnityEngine;
using UnityEngine.SceneManagement;

// Takes damage; on death goes limp if there's a RagdollToggle on the same object or under it.
public class Health : MonoBehaviour
{
    public float maxHealth = 30f;
    [Tooltip("For the player: seconds after dying before the scene starts again. 0 leaves the body where it fell")]
    public float restartDelay;
    [Tooltip("Flash on taking a hit; a hit a bagel soaks up flashes blue instead")]
    public Color hitFlash = new Color(3f, 0.1f, 0.05f);
    public Color blockedFlash = new Color(0.2f, 0.8f, 3f);
    public float flashTime = 0.2f;

    [HideInInspector] public float damageScale = 1f; // under 1 while a food ability (Full belly) softens hits
    [HideInInspector] public int shields; // bagels (FoodPowers): each one soaks up a whole hit
    [HideInInspector] public bool noRestart; // online (HuntMatch): the match revives the player instead of reloading the scene
    // online: this is the other player's bird (NetBird), so hits are sent to their game instead of landing here
    public Action<float> forward;

    public float Current => maxHealth - damageTaken;
    public bool IsDead => Current <= 0f;
    public event Action Died;

    float damageTaken;

    public void TakeDamage(float amount)
    {
        if (IsDead) return;
        if (forward != null) { Glow.On(this).Flash(hitFlash, flashTime); forward(amount); return; }
        if (shields > 0) { shields--; Glow.On(this).Flash(blockedFlash, flashTime); return; }
        Glow.On(this).Flash(hitFlash, flashTime);
        damageTaken += amount * damageScale;
        if (!IsDead) return;
        RagdollToggle ragdoll = GetComponentInChildren<RagdollToggle>();
        if (ragdoll) ragdoll.SetRagdoll(true);
        Died?.Invoke();
        if (restartDelay <= 0) return;
        // the player: no more moving or shooting (HeadRide sees the death itself and lets go of its mount)
        foreach (Behaviour part in GetComponents<Behaviour>())
            if (part is Weapon || part is StarterAssets.ThirdPersonController) part.enabled = false;
        if (TryGetComponent(out CharacterController body)) body.enabled = false; // or it shoves its own ragdoll about
        if (!noRestart) Invoke(nameof(Restart), restartDelay);
    }

    // back to full health and on its feet (online respawn and new rounds; offline the scene just reloads)
    public void Revive()
    {
        CancelInvoke(nameof(Restart));
        damageTaken = 0;
        shields = 0;
        RagdollToggle ragdoll = GetComponentInChildren<RagdollToggle>();
        if (ragdoll) ragdoll.SetRagdoll(false);
        foreach (Behaviour part in GetComponents<Behaviour>())
            if (part is Weapon || part is StarterAssets.ThirdPersonController) part.enabled = true;
        if (TryGetComponent(out CharacterController body)) body.enabled = true;
    }

    // the scene has to be in the build settings
    void Restart() => SceneManager.LoadScene(SceneManager.GetActiveScene().path);
}
