using UnityEngine;

// Put on anything with a collider the player should bounce off when landing on top (the food cart umbrellas).
// ThirdPersonController does the launching.
public class Bouncer : MonoBehaviour
{
    [Tooltip("How high the player is thrown, in metres")]
    public float height = 12f;
}
