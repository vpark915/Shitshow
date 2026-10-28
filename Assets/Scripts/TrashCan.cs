using UnityEngine;

// A park litter basket (Prefabs/TrashCan; Tools > Wash Square > Build Trash Can Prefab makes it). Look at it with the
// eat prompt up (PieceEater) and press E to rummage through it: whatever it was stocked with at the start of the round
// is yours, a random food (as if eaten: its health and ability), some cash or a spare magazine, or nothing but trash.
// Then it's empty, its pile of trash gone, until the next round restocks it (HuntMatch.RoundBegan; offline, never).
// ponytail: each game rolls its own cans, like its own food (see NetBird); share them when both players racing for the
// same can matters
public class TrashCan : MonoBehaviour
{
    [Tooltip("Chance it holds nothing")]
    [Range(0, 1)] public float emptyChance = 0.35f;
    [Tooltip("Of the cans with something in: chance it's a food (the rest are cash or a magazine, half each)")]
    [Range(0, 1)] public float foodChance = 0.6f;
    [Tooltip("The foods it can hold, one picked at random (their prefabs: label, ability, seconds and heal are read)")]
    public FoodItem[] foods;
    [Tooltip("Least and most cash, when it's cash")]
    public Vector2Int money = new Vector2Int(3, 12);
    [Tooltip("The pile of trash inside, hidden once it's been searched")]
    public GameObject trash;

    public bool Searched { get; private set; }
    public FoodItem Food { get; private set; }
    public int Cash { get; private set; }
    public int Magazines { get; private set; }

    void OnEnable() => HuntMatch.RoundBegan += Restock;
    void OnDisable() => HuntMatch.RoundBegan -= Restock;
    void Start() => Restock();

    public void Restock()
    {
        Searched = false;
        Food = null;
        Cash = Magazines = 0;
        if (trash) trash.SetActive(true);
        if (Random.value < emptyChance) return;
        float roll = Random.value;
        if (roll < foodChance && foods.Length > 0) Food = foods[Random.Range(0, foods.Length)];
        else if (roll < (1 + foodChance) / 2) Cash = Random.Range(money.x, money.y + 1);
        else Magazines = 1;
    }

    // searched: nothing left in it till the next round
    public void Empty()
    {
        Searched = true;
        Food = null;
        Cash = Magazines = 0;
        if (trash) trash.SetActive(false);
    }
}
