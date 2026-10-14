using UnityEngine;

// The Quirky Series controllers default to an arbitrary state (Attack); this picks a calmer one.
public class PlayStateOnStart : MonoBehaviour
{
    public string state = "Idle_A";

    void Start() => GetComponent<Animator>().Play(state);
}
