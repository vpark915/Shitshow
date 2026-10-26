using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Redraws the ammo readout whenever the weapon reports a change, and the extra-jump count whenever it changes.
public class WeaponHUD : MonoBehaviour
{
    public Weapon weapon;
    public Text ammoText;
    public Text reloadsText;
    public StarterAssets.ThirdPersonController player;
    public Text jumpsText;
    public HeadRide ride;
    [Tooltip("Shown only while riding a person")]
    public Text rideText;
    public Text healthText;
    public Text moneyText;
    [Tooltip("Top left while the police are hunting this bird (Npc.Wanted); made from Health Text if left empty")]
    public Text wantedText;
    [Tooltip("Flashed over the crosshair on a headshot; made from Health Text if left empty")]
    public Text headshotText;

    int shownJumps = -1;
    float headshotUntil, markerUntil, markerStart;
    RectTransform marker; // the hitmarker: four short strokes in an X round the crosshair
    Image[] strokes;
    Health health;

    void OnEnable() { weapon.Changed += Refresh; weapon.Hit += OnHit; }
    void OnDisable() { weapon.Changed -= Refresh; weapon.Hit -= OnHit; }

    void OnHit(bool head)
    {
        if (head) headshotUntil = Time.time + 0.6f;
        markerStart = Time.time;
        markerUntil = Time.time + (head ? 0.35f : 0.2f);
        if (strokes != null) foreach (Image stroke in strokes) stroke.color = head ? new Color(1f, 0.2f, 0.15f) : Color.white;
    }
    void Start()
    {
        Refresh();
        if (player) player.TryGetComponent(out health);
        if (rideText) rideText.verticalOverflow = VerticalWrapMode.Overflow; // room for the silent kill line
        if (!wantedText && healthText)
        {
            wantedText = Instantiate(healthText, healthText.transform.parent);
            wantedText.name = "WantedText";
            var corner = wantedText.rectTransform;
            corner.anchorMin = corner.anchorMax = corner.pivot = new Vector2(0, 1);
            corner.anchoredPosition = new Vector2(32, -24);
            wantedText.alignment = TextAnchor.UpperLeft;
        }
        if (healthText)
        {
            marker = new GameObject("HitMarker", typeof(RectTransform)).GetComponent<RectTransform>();
            marker.SetParent(healthText.transform.parent, false);
            marker.anchorMin = marker.anchorMax = new Vector2(0.5f, 0.5f);
            marker.anchoredPosition = Vector2.zero;
            strokes = new Image[4];
            for (int i = 0; i < 4; i++)
            {
                var stroke = new GameObject("Stroke", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                stroke.raycastTarget = false;
                RectTransform r = stroke.rectTransform;
                r.SetParent(marker, false);
                r.sizeDelta = new Vector2(3, 12);
                float angle = 45 + 90 * i;
                r.localRotation = Quaternion.Euler(0, 0, angle);
                r.anchoredPosition = Quaternion.Euler(0, 0, angle) * Vector2.up * 14; // out from the middle, along the stroke
                strokes[i] = stroke;
            }
            marker.gameObject.SetActive(false);
        }
        if (!headshotText && healthText)
        {
            headshotText = Instantiate(healthText, healthText.transform.parent);
            headshotText.name = "HeadshotText";
            var above = headshotText.rectTransform;
            above.anchorMin = above.anchorMax = above.pivot = new Vector2(0.5f, 0.5f);
            above.anchoredPosition = new Vector2(0, 60);
            headshotText.alignment = TextAnchor.MiddleCenter;
            headshotText.text = "HEADSHOT";
            headshotText.color = new Color(1f, 0.25f, 0.2f);
        }
    }

    void Update()
    {
        if (ride && rideText)
        {
            rideText.enabled = ride.Riding;
            rideText.text = ride.dismountAction.GetBindingDisplayString() + ": Dismount" +
                            (ride.CanAssassinate ? "\n" + ride.assassinateAction.GetBindingDisplayString() + ": Silent kill" : "");
        }
        if (headshotText) headshotText.enabled = Time.time < headshotUntil;
        if (marker)
        {
            marker.gameObject.SetActive(Time.time < markerUntil);
            marker.localScale = Vector3.one * Mathf.Lerp(1.4f, 1f, (Time.time - markerStart) / 0.1f); // pops in, then holds
        }
        if (wantedText)
        {
            bool wanted = Npc.IsWanted(weapon);
            wantedText.enabled = wanted;
            if (wanted)
            {
                int left = Mathf.CeilToInt(Npc.WantedUntil - Time.time);
                wantedText.text = "WANTED  " + left / 60 + ":" + (left % 60).ToString("00");
                wantedText.color = Color.Lerp(Color.red, Color.white, Mathf.PingPong(Time.time * 2, 1) * 0.6f); // pulses
            }
        }
        if (health && healthText)
        {
            healthText.text = "Health: " + Mathf.CeilToInt(Mathf.Max(health.Current, 0));
            healthText.color = health.Current <= health.maxHealth * 0.3f ? Color.red : Color.white;
        }
        if (!player || !jumpsText || player.ExtraJumpsLeft == shownJumps) return;
        shownJumps = player.ExtraJumpsLeft;
        jumpsText.text = "Jumps: " + shownJumps;
        jumpsText.color = shownJumps == 0 ? Color.red : Color.white;
    }

    void Refresh()
    {
        ammoText.text = weapon.IsReloading ? "RELOADING" : weapon.Ammo + " / " + weapon.magazineSize;
        ammoText.color = weapon.Ammo == 0 || weapon.IsReloading ? Color.red : Color.white;
        reloadsText.text = "Reloads: " + weapon.reloadsLeft;
        reloadsText.color = weapon.reloadsLeft == 0 ? Color.red : Color.white;
        if (moneyText) moneyText.text = "$" + weapon.Money;
    }
}
