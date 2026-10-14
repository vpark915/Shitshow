using System;
using UnityEngine;
using UnityEngine.SceneManagement;

// Takes damage; on death goes limp if there's a RagdollToggle on the same object or under it.
public class Health : MonoBehaviour
{
    public float maxHealth = 30f;
    [Tooltip("For the player: seconds after dying before the scene starts again. 0 leaves the body where it fell")]
    public float restartDelay;

    public float Current => maxHealth - damageTaken;
    public bool IsDead => Current <= 0f;
    public event Action Died;

    float damageTaken;

    public void TakeDamage(float amount)
    {
        if (IsDead) return;
        damageTaken += amount;
        if (!IsDead) return;
        RagdollToggle ragdoll = GetComponentInChildren<RagdollToggle>();
        if (ragdoll) ragdoll.SetRagdoll(true);
        Died?.Invoke();
        if (restartDelay <= 0) return;
        // the player: no more moving or shooting (HeadRide sees the death itself and lets go of its mount)
        foreach (Behaviour part in GetComponents<Behaviour>())
            if (part is Weapon || part is StarterAssets.ThirdPersonController) part.enabled = false;
        if (TryGetComponent(out CharacterController body)) body.enabled = false; // or it shoves its own ragdoll about
        Invoke(nameof(Restart), restartDelay);
    }

    // the scene has to be in the build settings
    void Restart() => SceneManager.LoadScene(SceneManager.GetActiveScene().path);
}
