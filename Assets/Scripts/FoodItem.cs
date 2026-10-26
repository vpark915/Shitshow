using UnityEngine;

// A serving of food (design doc section 10, Food). Look at it with the eat prompt up (PieceEater) and press E: it's
// eaten, the pigeon gets back some health and gets its ability, now or in its stash for later (FoodPowers). It comes back after Respawn seconds, the vendor
// making the next serving.
public class FoodItem : MonoBehaviour
{
    public string label = "Hot dog";
    public FoodPowers.Ability ability;
    [Tooltip("Timed foods: how long the ability lasts. Pizza: seconds of riding. Bread: seconds the flock tails the target. Unused by ice cream and bagels")]
    public float seconds = 8f;
    [Tooltip("Dollars it costs at a cart or truck (anything under the minimap's Vendors); food found around the park is free")]
    public int price = 5;
    [Tooltip("Health it gives back when eaten (the pigeon has 100)")]
    public float heal = 15f;
    [Tooltip("Seconds until the next serving appears here; 0 for never")]
    public float respawn = 30f;

    public bool Ready { get; private set; } = true;

    public void Eat()
    {
        Show(false);
        if (respawn > 0) Invoke(nameof(Restock), respawn);
    }

    void Restock() => Show(true);

    void Show(bool on)
    {
        Ready = on;
        foreach (Renderer r in GetComponentsInChildren<Renderer>()) r.enabled = on;
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = on;
    }
}
