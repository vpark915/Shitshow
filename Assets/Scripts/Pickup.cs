using UnityEngine;

// Dropped where someone dies (a magazine from police, a wallet from civilians). Hovers and spins like a coin until the
// player comes close, then flies to them and is collected.
public class Pickup : MonoBehaviour
{
    [Tooltip("Height it floats above the spot it was dropped on, in metres")]
    public float hoverHeight = 0.5f;
    public float bobHeight = 0.1f;
    public float spinSpeed = 180f;
    [Tooltip("The player is pulled in from this many metres away")]
    public float magnetRadius = 4f;
    [Tooltip("How fast it flies to the player, in m/s; it speeds up as it goes")]
    public float magnetSpeed = 6f;
    [Tooltip("Extra reloads it gives the player")]
    public int magazines = 1;
    [Tooltip("Least and most money inside; rolled when it's dropped")]
    public Vector2Int money;
    [Tooltip("Seconds until it comes back after being collected; 0 for never (the gym uses this so it can be tried again)")]
    public float respawn;

    Weapon player;
    Vector3 home;
    float pull; // 0 until the player comes near, then how fast it's flying
    int cash;
    float backAt; // collected and waiting to respawn until then

    void Start()
    {
        cash = Random.Range(money.x, money.y + 1);
        player = FindFirstObjectByType<Weapon>();
        home = transform.position;
    }

    void Update()
    {
        if (backAt > 0)
        {
            if (Time.time < backAt) return;
            backAt = 0;
            Show(true);
        }
        transform.Rotate(0, spinSpeed * Time.deltaTime, 0, Space.World);
        if (!player || !player.enabled) { Hover(); return; } // a dead player doesn't collect anything

        Vector3 target = player.transform.position + Vector3.up * 0.3f; // middle of the pigeon
        if (pull == 0 && Vector3.Distance(transform.position, target) > magnetRadius) { Hover(); return; }
        pull = Mathf.Max(pull, magnetSpeed) + magnetSpeed * 2 * Time.deltaTime;
        transform.position = Vector3.MoveTowards(transform.position, target, pull * Time.deltaTime);
        if (Vector3.Distance(transform.position, target) > 0.2f) return;
        if (magazines > 0) player.AddMagazine(magazines);
        if (cash > 0) player.AddMoney(cash);
        if (respawn <= 0) { Destroy(gameObject); return; }
        pull = 0;
        transform.position = home;
        cash = Random.Range(money.x, money.y + 1);
        backAt = Time.time + respawn;
        Show(false);
    }

    void Show(bool on)
    {
        foreach (Renderer r in GetComponentsInChildren<Renderer>()) r.enabled = on;
    }

    void Hover() => transform.position = home + Vector3.up * (hoverHeight + Mathf.Sin(Time.time * 3) * bobHeight);
}
