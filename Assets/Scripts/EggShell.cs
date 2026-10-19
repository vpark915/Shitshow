using UnityEngine;

// On the egg bomb prefab (SpottedEgg, laid by FoodPowers): bursts on the first thing it hits, or after a few seconds.
public class EggShell : MonoBehaviour
{
    public System.Action<Vector3> Burst;
    bool popped;

    void Start() => Invoke(nameof(TimeUp), 4f);
    void OnCollisionEnter(Collision c) => Pop(c.GetContact(0).point);
    void TimeUp() => Pop(transform.position);

    void Pop(Vector3 at)
    {
        if (popped) return;
        popped = true;
        Burst?.Invoke(at);
        Destroy(gameObject);
    }
}
