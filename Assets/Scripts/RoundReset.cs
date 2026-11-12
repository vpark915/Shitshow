using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// The park as it was at the start, for each new round of a match (HuntMatch.BeginRound): a fresh crowd and fresh flocks
// (the dead and the panicked gone, the police no longer after anyone), every food served again, every chess piece back,
// every car and bike back where it was parked, and nothing left lying about (dropped wallets and magazines, egg splats
// and shells). Trash cans restock themselves (HuntMatch.RoundBegan). Each game resets its own park: only the birds are
// shared (see NetBird).
public static class RoundReset
{
    static readonly Dictionary<Vehicle, (Vector3 at, Quaternion turn)> parked = new();
    static Vehicle[] order = { };

    // where every vehicle stands as the scene starts (HuntMatch.Start), so a round can put them back
    public static void Remember()
    {
        parked.Clear();
        foreach (Vehicle v in Object.FindObjectsByType<Vehicle>(FindObjectsSortMode.None))
            parked[v] = (v.transform.position, v.transform.rotation);
        // numbered by where they started, the same in both games (same scene), so a driver can say which one (NetBird)
        order = new List<Vehicle>(parked.Keys).ToArray();
        System.Array.Sort(order, (a, b) =>
        {
            Vector3 p = parked[a].at, q = parked[b].at;
            return p.x != q.x ? p.x.CompareTo(q.x) : p.z != q.z ? p.z.CompareTo(q.z) : string.CompareOrdinal(a.name, b.name);
        });
    }

    // a vehicle's number for the other game, -1 if it isn't one of the scene's; and the vehicle with that number
    public static int Number(Vehicle v) => System.Array.IndexOf(order, v);
    public static Vehicle Numbered(int n) => n >= 0 && n < order.Length ? order[n] : null;

    // run on the match object; the minimap finds the new police a frame on, once the old crowd is gone
    public static IEnumerator Run()
    {
        Npc.CalmAll(); // nobody wanted (the crowd that was after them is about to go anyway)
        if (Object.FindFirstObjectByType<NpcSpawner>() is NpcSpawner crowd) crowd.Respawn();
        if (Object.FindFirstObjectByType<BirdFlocks>() is BirdFlocks flocks) flocks.Respawn();
        foreach (FoodItem food in Object.FindObjectsByType<FoodItem>(FindObjectsInactive.Include, FindObjectsSortMode.None)) food.Restock();
        foreach (PieceEater eater in Object.FindObjectsByType<PieceEater>(FindObjectsSortMode.None)) eater.PutBackAll();
        foreach (var (vehicle, spot) in parked)
            if (vehicle && !vehicle.rider) vehicle.PutBack(spot.at, spot.turn);
        // left lying about: what the dead dropped (a pickup with no respawn is a drop; the gym's own come back by
        // themselves), and the eggs' mess
        foreach (Pickup drop in Object.FindObjectsByType<Pickup>(FindObjectsSortMode.None)) if (drop.respawn <= 0) Object.Destroy(drop.gameObject);
        foreach (EggSplat splat in Object.FindObjectsByType<EggSplat>(FindObjectsSortMode.None)) Object.Destroy(splat.gameObject);
        foreach (EggShell shell in Object.FindObjectsByType<EggShell>(FindObjectsSortMode.None)) Object.Destroy(shell.gameObject);
        yield return null;
        if (Object.FindFirstObjectByType<Minimap>() is Minimap map) map.FindPolice();
    }
}
