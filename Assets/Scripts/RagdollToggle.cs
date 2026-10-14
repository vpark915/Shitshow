using UnityEngine;

// Put on the animated root. Bone Rigidbodies stay kinematic until ragdoll is ticked.
public class RagdollToggle : MonoBehaviour
{
    public bool ragdoll;

    Animator animator;
    Collider mainCollider;
    Rigidbody[] bodies;

    public bool IsRagdoll { get; private set; }

    void Awake()
    {
        animator = GetComponent<Animator>();
        mainCollider = GetComponent<Collider>();
        bodies = GetComponentsInChildren<Rigidbody>();

        // bone colliders overlap each other at rest; without this they explode apart
        var cols = GetComponentsInChildren<Collider>();
        for (int i = 0; i < cols.Length; i++)
            for (int j = i + 1; j < cols.Length; j++)
                Physics.IgnoreCollision(cols[i], cols[j]);

        SetRagdoll(ragdoll);
    }

    void Update()
    {
        if (ragdoll != IsRagdoll) SetRagdoll(ragdoll);
    }

    public void SetRagdoll(bool on)
    {
        ragdoll = IsRagdoll = on;
        if (animator) animator.enabled = !on;
        if (mainCollider) mainCollider.enabled = !on;
        foreach (var rb in bodies)
        {
            rb.isKinematic = !on;
            // interpolation is only for the simulated ragdoll: while the Animator drives the bones it makes each bone
            // lag behind its parent on its own, so small ones (hair, tail) visibly come apart from the body
            rb.interpolation = on ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
        }
    }

    [ContextMenu("Toggle Ragdoll")]
    void Toggle() => SetRagdoll(!IsRagdoll);
}
