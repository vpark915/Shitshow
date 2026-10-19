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

    int shownJumps = -1;
    Health health;

    void OnEnable() => weapon.Changed += Refresh;
    void OnDisable() => weapon.Changed -= Refresh;
    void Start()
    {
        Refresh();
        if (player) player.TryGetComponent(out health);
        if (ride && rideText) rideText.text = ride.dismountAction.GetBindingDisplayString() + ": Dismount";
    }

    void Update()
    {
        if (ride && rideText) rideText.enabled = ride.Mount;
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
