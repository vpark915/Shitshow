using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Put on the pigeon. Look at a chess piece close by and it lights up with a prompt; press the eat key and it's gone,
// and for a while the minimap shows exactly where the target is and they glow through walls. Food (FoodItem) works the
// same way: the prompt names it and its ability, and eating it hands the ability to FoodPowers.
public class PieceEater : MonoBehaviour
{
    [Tooltip("How close the pigeon has to be to the piece, in metres")]
    public float reach = 1.5f;
    [Tooltip("How close the pigeon has to be to food; servings sit up on cart counters, so a little further")]
    public float foodReach = 2.5f;
    [Tooltip("How close the pigeon has to be to any part of a food cart or truck to buy from it")]
    public float vendorReach = 4f;
    [Tooltip("Seconds the target stays revealed after eating a piece")]
    public float revealTime = 10f;
    public Minimap map;
    [Tooltip("Shown while looking at a piece in reach")]
    public Text prompt;
    [Tooltip("Worn by the piece being looked at")]
    public Material hoverMaterial;
    [Tooltip("Glow on the food, or the whole cart or truck, being looked at: the same gold as the piece")]
    public Color hoverGlow = new Color(0.9f, 0.65f, 0.15f);
    [Tooltip("How far off the crosshair, in metres, a piece or food still counts as looked at")]
    public float aimAssist = 0.3f;
    [Tooltip("Seconds until an eaten piece is back on the board; 0 for never (the gym uses this so it can be tried again)")]
    public float pieceRespawn;
    public InputAction eatAction = new InputAction("Eat", binding: "<Keyboard>/e");

    Health health;
    FoodPowers powers;
    Weapon wallet; // the money lives on the Weapon
    Renderer hover;
    Glow lit; // the food, or the whole cart or truck, glowing because it's looked at
    FoodItem shown; // what the prompt is offering, so a cart keeps offering it while the aim wanders over the cart
    Material hoverWas;
    string eatKey;

    void Start()
    {
        TryGetComponent(out health);
        TryGetComponent(out powers);
        TryGetComponent(out wallet);
        eatKey = eatAction.GetBindingDisplayString();
    }

    void OnEnable() => eatAction.Enable();
    void OnDisable() => eatAction.Disable();

    void Update()
    {
        Transform piece = null;
        FoodItem food = null;
        Camera cam = Camera.main;
        if (cam && !(health && health.IsDead))
        {
            // a fat ray, so a piece or food just off the crosshair still counts; the closest to the crosshair wins.
            // It stops a little past the first solid thing, so it can't pick through a wall.
            var ray = new Ray(cam.transform.position, cam.transform.forward);
            int mask = ~(1 << gameObject.layer);
            float far = Physics.Raycast(ray, out RaycastHit first, 30f, mask, QueryTriggerInteraction.Ignore) ? first.distance + aimAssist : 30f;
            float best = float.MaxValue;
            foreach (RaycastHit hit in Physics.SphereCastAll(ray, aimAssist, far, mask, QueryTriggerInteraction.Collide))
            {
                Bounds box = hit.collider.bounds;
                float distance = Vector3.Distance(box.ClosestPoint(transform.position), transform.position);
                float off = Vector3.Cross(ray.direction, box.center - ray.origin).magnitude;
                if (off >= best) continue;
                // ponytail: pieces are told apart by name (Piece_NN in Chessboard_Low.fbx); give them a component if something else needs to find them
                if (hit.collider.name.StartsWith("Piece_") && distance <= reach) { piece = hit.transform; food = null; best = off; }
                else if (powers && distance <= (Vendor(hit.collider.transform) ? vendorReach : foodReach) && Food(hit.collider) is FoodItem f && f) { food = f; piece = null; best = off; }
            }
        }

        shown = food;

        // a cart or truck lights up as a whole, servings included; food lying about lights up on its own
        Transform vendor = food ? Vendor(food.transform) : null;
        Glow look = food ? Glow.On(vendor ? vendor : food.transform) : null;
        if (lit != look)
        {
            if (lit) lit.steady = Color.black;
            lit = look;
            if (lit) lit.steady = hoverGlow;
        }

        if (food)
        {
            // from a cart or truck it's bought; found around the park it's free
            int price = vendor ? food.price : 0;
            bool afford = price == 0 || (wallet && wallet.Money >= price);
            if (prompt)
            {
                prompt.enabled = true;
                string what = food.label + (price > 0 ? " $" + price : "") + "  (" + FoodPowers.Describe(food) + ")";
                prompt.text = price == 0 ? eatKey + ": Eat " + what
                    : afford ? eatKey + ": Buy " + what
                    : what + "  you have $" + (wallet ? wallet.Money : 0);
            }
            if (eatAction.WasPressedThisFrame() && afford && (price == 0 || wallet.Spend(price)))
            {
                if (!vendor) { lit.steady = Color.black; lit = null; } // gone; a vendor stays lit while still looked at
                food.Eat();
                powers.Give(food.ability, food.seconds, food.label);
            }
        }
        else if (prompt) prompt.text = eatKey + ": Eat";

        // QuickOutline draws only scraps of an outline on the decimated pieces, so the piece swaps to a glowing material instead
        if ((hover ? hover.transform : null) != piece)
        {
            if (hover) hover.sharedMaterial = hoverWas;
            hover = piece ? piece.GetComponent<Renderer>() : null;
            if (hover) { hoverWas = hover.sharedMaterial; hover.sharedMaterial = hoverMaterial ? hoverMaterial : hoverWas; }
        }
        if (prompt && !food) prompt.enabled = piece;
        if (!piece || !eatAction.WasPressedThisFrame()) return;

        hover.sharedMaterial = hoverWas;
        hover = null;
        piece.gameObject.SetActive(false);
        if (pieceRespawn > 0) StartCoroutine(PutBack(piece.gameObject));
        if (map) map.Reveal(revealTime);
    }

    // the cart or truck (a child of the minimap's Vendors) that this is part of, or null
    Transform Vendor(Transform part)
    {
        if (!map || !map.vendors || !part.IsChildOf(map.vendors) || part == map.vendors) return null;
        while (part.parent != map.vendors) part = part.parent;
        return part;
    }

    // the serving being looked at, or, looking at any other part of a cart or truck, what it would sell you: the first
    // serving on its counter that's ready
    FoodItem Food(Collider looked)
    {
        FoodItem serving = looked.GetComponentInParent<FoodItem>();
        if (serving && serving.Ready) return serving;
        Transform vendor = Vendor(looked.transform);
        if (vendor && shown && shown.Ready && Vendor(shown.transform) == vendor) return shown;
        if (vendor)
            foreach (FoodItem other in vendor.GetComponentsInChildren<FoodItem>())
                if (other.Ready) return other;
        return null;
    }

    System.Collections.IEnumerator PutBack(GameObject piece)
    {
        yield return new WaitForSeconds(pieceRespawn);
        piece.SetActive(true);
    }
}
