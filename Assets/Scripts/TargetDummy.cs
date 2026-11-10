using UnityEngine;

// Someone to shoot who stays put (the Mechanics Gym's kill signs station): stands still, keels over when killed, and is
// back on their feet a few seconds later, so there's always a target for a Kill, a headshot or a double kill.
[RequireComponent(typeof(Health))]
public class TargetDummy : MonoBehaviour
{
    [Tooltip("Seconds lying down before getting back up")]
    public float getUpAfter = 3f;

    Health health;
    Quaternion standing;

    void Awake()
    {
        health = GetComponent<Health>();
        standing = transform.rotation;
        health.Died += Fall;
    }

    void OnDestroy()
    {
        if (health) health.Died -= Fall;
    }

    // the pivot is at the feet, so this lays them on their back
    void Fall()
    {
        transform.rotation = standing * Quaternion.Euler(-85, 0, 0);
        Invoke(nameof(GetUp), getUpAfter);
    }

    void GetUp()
    {
        health.Revive();
        transform.rotation = standing;
    }
}
