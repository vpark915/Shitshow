using System.Collections.Generic;
using UnityEngine;
using Goal = HuntMatch.Goal;
using When = HuntMatch.When;
using Task = HuntMatch.Task;

// Every task the Seagull can be given. Each round HuntMatch draws Per Round of them (the same ones in both games), from
// this list plus the scene's own landmark tasks (HuntMatch > Tasks). Edit the list in the asset
// (Tools > Wash Square > Hunt Tasks); Reset in its menu (the cog) puts back the list below.
//
// How each kind of task is fulfilled (its Goal), and which fields it reads:
// - Stay: be at a place for Amount seconds in one go. Place: Spot, or else every scene object whose name starts with
//   At (alternatives split by |; "Benches/" means each child of the object named Benches). Radius is metres from the object's
//   footprint (0 = right over it), Height the range above its foot (or its top, with On Top). Count > 1: that many
//   different ones, each for the whole time.
// - StandOn: be standing on ground whose name starts with At (the lawn, the sandpit, the road...) for Amount seconds.
// - Deed: do Deed Count times; with Within, all of them inside that many seconds; with At (or Spot), only while
//   at one of those places (Radius, Height, On Top as for Stay). The deeds (Weapon.Done says when):
//     shoot, reload, hit, body shot, headshot, hit civilian / officer / guard / bird (bird: the Pigeon),
//     kill, kill civilian / officer / guard / walker / jogger / idler, shot kill, headshot kill, long shot kill
//     (40 m+), point blank kill (3 m or less), double kill, triple kill (kills under 3 s apart), roadkill,
//     egg kill, silent kill, run over, run over civilian / officer / guard, run over bird (a park bird),
//     crash (knock over another vehicle), assassinate, assassinate civilian / officer / guard,
//     egg bomb, egg hit, egg hit civilian / officer / guard, snitch flock, eat, eat <food> (hot dog, coffee, gyro,
//     burrito, ice cream, soft pretzel, pizza slice, bagel, bread), eat chess piece, buy, wallet, magazine,
//     ride person, ride civilian / officer / guard, drive car, ride bike, hijack (take over a car driving round), wanted
// - RidePerson: ride people for Amount seconds. DriveCar / RideBike: drive Amount metres.
// - Airborne: stay in the air (not riding) for Amount seconds. Altitude: get Amount metres above whatever is below.
// - Wanted: stay alive while wanted by the police for Amount seconds. Survive: stay alive for Amount seconds (with When,
//   the point: while hurt, hidden...). Money: hold $Amount at once. Earn / Spend: gain / spend $Amount in all.
// - Stock: hold Count of the food power named in Deed (EggBomb, BagelShield, CrowdFavourite, SnitchFlock).
//   Reloads: hold Count spare magazines.
// When (any goal): it only counts while that's true too: Flying, OnFoot, Riding (a person), Driving, Wanted,
// Hurt (a third of health or less), Hidden (soft pretzel's Low profile).
// In One Go: off, the seconds add up across tries instead of starting over.
[CreateAssetMenu(menuName = "Shitshow/Hunt Tasks")]
public class HuntTasks : ScriptableObject
{
    [Tooltip("Tasks the Seagull gets each round")]
    [Range(1, 10)] public int perRound = 3;
    [Tooltip("Also draw from the scene's landmark tasks (HuntMatch > Tasks: the fountain, arch and chess table)")]
    public bool sceneTasks = true;
    public List<Task> tasks = new List<Task>();

    // the one HuntMatch uses when it isn't given one
    public static HuntTasks Load() => Resources.Load<HuntTasks>("HuntTasks");

    void Reset() => tasks = Defaults();

    static Task Stay(string name, string description, string at, float seconds, bool onTop = true, float radius = 0f, float low = -0.5f, float high = 2.5f, int count = 1) =>
        new Task { name = name, description = description, goal = Goal.Stay, at = at, amount = seconds, onTop = onTop, radius = radius, height = new Vector2(low, high), count = count };
    static Task On(string name, string description, string ground, float seconds, bool inOneGo = true) =>
        new Task { name = name, description = description, goal = Goal.StandOn, at = ground, amount = seconds, inOneGo = inOneGo };
    static Task Do(string name, string description, string deed, int count = 1, float within = 0, When when = When.Any,
        string at = null, bool onTop = true, float radius = 0, float low = -0.5f, float high = 2.5f) =>
        new Task { name = name, description = description, goal = Goal.Deed, deed = deed, count = count, within = within, when = when,
                   at = at, onTop = onTop, radius = radius, height = new Vector2(low, high) };
    static Task For(Goal goal, string name, string description, float amount, bool inOneGo = true, When when = When.Any) =>
        new Task { name = name, description = description, goal = goal, amount = amount, inOneGo = inOneGo, when = when };
    static Task Hold(Goal goal, string name, string description, int count, string power = null) =>
        new Task { name = name, description = description, goal = goal, count = count, deed = power };

    const string Rooftops = "WSP_BobstLibrary|WSP_KimmelCenter|WSP_VanderbiltHall|WSP_HaydenHall|WSP_SilverCenter|WSP_GoddardHall|WSP_ShimkinHall|WSP_PlessHall|WSP_WilfHall|WSP_EducationBuilding|WSP_KevorkianCenter|WSP_KingJuanCarlosCenter|WSP_GlobalCenterSpiritualLife|WSP_1UniversityPlace|WSP_JudsonChurch|WSP_WashSqHotel|WSP_TheRow|WSP_RowWest|WSP_1FifthAve|WSP_2FifthAve|WSP_Fill_";

    // ponytail: places are Wash Square's object names (the blockout's WSP_ pieces, the scenes' group objects); a task
    // whose place isn't in the scene is never drawn there, so the Hunt Gym just gets fewer to pick from
    public static List<Task> Defaults() => new List<Task>
    {
        // ---- perches: on top of things ----
        Stay("Lamp", "Perch on top of a lamp post", "WSP_Prop_Lamp", 4),
        Stay("Lamps", "Perch on top of 3 different lamp posts", "WSP_Prop_Lamp", 2, count: 3),
        Stay("Bench", "Stand on a bench", "Benches/|WSP_Prop_Bench", 3),
        Stay("Benches", "Stand on 5 different benches", "Benches/|WSP_Prop_Bench", 1.5f, count: 5),
        Stay("Chess", "Ruin a chess game: stand on any chess table", "ChessTables/|WSP_Prop_ChessTable", 3),
        Stay("Chess tour", "Stand on 3 different chess tables", "ChessTables/|WSP_Prop_ChessTable", 2, count: 3),
        Stay("Garibaldi", "Stand on Garibaldi's head (the statue)", "WSP_Prop_GaribaldiStatue", 4),
        Stay("Holley", "Perch on the Holley bust", "WSP_Prop_HolleyBust", 4),
        Stay("Flagpole", "Perch on top of the flagpole", "WSP_Prop_Flagpole", 3, radius: 0.5f),
        Stay("Stage", "Perform: stand on the stage for 8 s", "WSP_Prop_Stage", 8, low: -1, high: 2),
        Stay("Park house", "Sit on the park house roof", "WSP_Prop_ParkHouse", 5),
        Stay("Swings", "Sit on the swings frame", "WSP_Prop_Swings", 3),
        Stay("Play big", "Stand on top of the big play structure", "WSP_Prop_PlayStructure_Large", 4),
        Stay("Play small", "Stand on top of the small play structure", "WSP_Prop_PlayStructure_Small", 4),
        Stay("Play toddler", "Stand on top of the toddler play structure", "WSP_Prop_PlayStructure_Toddler", 4),
        Stay("Mound", "King of the hill: stand on a mound", "WSP_Prop_Mound", 5, onTop: false, low: 0.3f, high: 4),
        Stay("Long benches", "Strut along the long benches", "WSP_Prop_LongBenches", 4, low: -0.3f, high: 2),
        Stay("Cart roof", "Stand on a food cart or truck roof", "FoodVendors/", 4),
        Stay("Vendor tour", "Stand on 3 different food carts or trucks", "FoodVendors/", 2, count: 3),
        Stay("Bike dock", "Sit on a Citi Bike docking station", "CitiBikeStation", 3),
        Stay("Fence", "Walk the park fence", "WSP_ParkFence", 4, low: -0.3f, high: 1.5f),
        // ---- the landmarks (the scene's own fountain, arch and chess table tasks are drawn too) ----
        Stay("Fountain dip", "Splash about in the fountain for 10 s", "WSP_Fountain|Fountain", 10, onTop: false, radius: 0, low: -1, high: 3),
        Stay("Arch side", "Cling to the arch: anywhere on it, off the ground", "WSP_Arch|Arch", 6, onTop: false, radius: 0.5f, low: 3, high: 30),
        // ---- rooftops round the park ----
        Stay("Judson", "Perch on Judson Memorial Church", "WSP_JudsonChurch", 4),
        Stay("Bobst", "Perch on the roof of Bobst Library", "WSP_BobstLibrary", 4),
        Stay("Kimmel", "Perch on the Kimmel Center roof", "WSP_KimmelCenter", 4),
        Stay("Vanderbilt", "Perch on Vanderbilt Hall", "WSP_VanderbiltHall", 4),
        Stay("Hayden", "Perch on Hayden Hall", "WSP_HaydenHall", 4),
        Stay("Silver", "Perch on the Silver Center", "WSP_SilverCenter", 4),
        Stay("Goddard", "Perch on Goddard Hall", "WSP_GoddardHall", 4),
        Stay("Wash Sq Hotel", "Perch on the Washington Square Hotel", "WSP_WashSqHotel", 4),
        Stay("The Row", "Perch on The Row", "WSP_TheRow|WSP_RowWest", 4),
        Stay("1 Fifth", "Perch on top of One Fifth Avenue", "WSP_1FifthAve", 4),
        Stay("2 Fifth", "Perch on Two Fifth Avenue", "WSP_2FifthAve", 4),
        Stay("Rooftop tour", "Perch on 3 different NYU buildings", "WSP_BobstLibrary|WSP_KimmelCenter|WSP_VanderbiltHall|WSP_HaydenHall|WSP_SilverCenter|WSP_GoddardHall|WSP_ShimkinHall|WSP_PlessHall|WSP_WilfHall|WSP_EducationBuilding|WSP_KevorkianCenter|WSP_KingJuanCarlosCenter|WSP_GlobalCenterSpiritualLife|WSP_1UniversityPlace", 2, count: 3),
        Stay("Roof hopper", "Perch on 5 different rooftops round the park", Rooftops, 1.5f, count: 5),
        // ---- ground ----
        On("Lawn", "Trample the grass for 8 s", "WSP_Park_Lawn|ParkTerrain", 8),
        On("Sandpit", "Dust bath: sit in the sandpit for 6 s", "WSP_Park_Sand", 6),
        On("Dog run", "Visit the dog run for 6 s", "WSP_Park_DogRun", 6),
        On("Gravel", "Peck about on the gravel for 6 s", "WSP_Park_Gravel", 6),
        On("Playground", "Hang out in the playground for 8 s", "WSP_Park_Playground", 8),
        On("Flower beds", "Wreck the planting beds for 5 s", "WSP_Park_PlantingBeds", 5),
        On("Jaywalk", "Stand in the road for 10 s (it adds up)", "WSP_Road", 10, inOneGo: false),
        On("Cobbles", "Stroll the cobbled street for 6 s", "WSP_Road_Cobble", 6),
        // ---- food ----
        Do("Eat 3", "Eat 3 things", "eat", 3),
        Do("Eat 6", "Pig out: eat 6 things", "eat", 6),
        Do("Hot dog", "Eat a hot dog", "eat hot dog"),
        Do("Coffee", "Drink a coffee", "eat coffee"),
        Do("Gyro", "Eat a gyro", "eat gyro"),
        Do("Burrito", "Eat a burrito", "eat burrito"),
        Do("Ice cream", "Eat an ice cream", "eat ice cream"),
        Do("Pretzel", "Eat a soft pretzel", "eat soft pretzel"),
        Do("Pizza", "Steal a pizza slice", "eat pizza slice"),
        Do("Pizza 2", "Steal 2 pizza slices", "eat pizza slice", 2),
        Do("Bagel", "Eat a bagel", "eat bagel"),
        Do("Bread", "Eat some bread", "eat bread"),
        Do("Chess piece", "Eat a chess piece", "eat chess piece"),
        Do("Chess pieces", "Eat 3 chess pieces", "eat chess piece", 3),
        Do("Customer", "Buy something from a cart or truck", "buy"),
        Do("Regular", "Buy 3 things from carts or trucks", "buy", 3),
        // ---- money and loot ----
        Do("Wallet", "Pick up a wallet", "wallet"),
        Do("Wallets", "Pick up 3 wallets", "wallet", 3),
        Do("Magazine", "Pick up a police magazine", "magazine"),
        For(Goal.Money, "Rich", "Hold $50 at once", 50),
        For(Goal.Money, "Loaded", "Hold $150 at once", 150),
        // ---- people ----
        Do("Kill", "Kill someone", "kill"),
        Do("Kill 5", "Kill 5 people", "kill", 5),
        Do("Massacre", "Kill 10 people", "kill", 10),
        Do("Cop killer", "Kill a police officer", "kill officer"),
        Do("Cop killer 3", "Kill 3 police officers", "kill officer", 3),
        Do("Guard", "Kill a security guard", "kill guard"),
        Do("Headshot", "Land 3 headshots", "headshot", 3),
        Do("Headshot kill", "Kill someone with a headshot", "headshot kill"),
        Do("Headshot kills", "Kill 3 people with headshots", "headshot kill", 3),
        Do("Silent", "Silently kill someone from their head (needs an empty gun)", "assassinate"),
        Do("Silent cop", "Silently kill a police officer from their head", "assassinate officer"),
        Do("Egg kill", "Kill someone with an egg bomb", "egg kill"),
        Do("Egg 3", "Hit 3 people with egg bombs", "egg hit", 3),
        Do("Egg bomb", "Set off an egg bomb", "egg bomb"),
        Do("Snitch", "Send a snitch flock after the Pigeon", "snitch flock"),
        Do("Ride", "Land on someone's head and ride them", "ride person"),
        Do("Ride cop", "Ride a police officer", "ride officer"),
        Do("Ride guard", "Ride a security guard", "ride guard"),
        For(Goal.RidePerson, "Joyride", "Ride people for 20 s", 20, inOneGo: false),
        For(Goal.RidePerson, "Long ride", "Ride one person for 15 s in one go", 15),
        // ---- vehicles ----
        Do("Drive", "Drive a car", "drive car"),
        Do("Bike", "Ride a Citi Bike", "ride bike"),
        Do("Hijack", "Take over a car driving round the park", "hijack"),
        For(Goal.DriveCar, "Road trip", "Drive 300 m in a car", 300),
        For(Goal.DriveCar, "Lap", "Drive 800 m in a car (about a lap of the park)", 800),
        For(Goal.RideBike, "Bike ride", "Cycle 200 m on a Citi Bike", 200),
        Do("Run over", "Run someone over", "run over"),
        Do("Run over 5", "Run over 5 people", "run over", 5),
        Do("Run over cop", "Run over a police officer", "run over officer"),
        Do("Roadkill", "Kill someone with a vehicle", "roadkill"),
        // ---- the police ----
        Do("Wanted", "Get wanted by the police (drive off the road)", "wanted"),
        For(Goal.Wanted, "Fugitive", "Survive 20 s while wanted", 20),
        For(Goal.Wanted, "Most wanted", "Survive 45 s while wanted (it adds up)", 45, inOneGo: false),
        Do("Hit cop", "Shoot 3 police officers", "hit officer", 3),
        // ---- the Pigeon ----
        Do("Wing it", "Shoot the Pigeon", "hit bird"),
        Do("Pest control", "Shoot the Pigeon 5 times", "hit bird", 5),
        // ---- kills, the creative ones ----
        Do("Jogger", "Stop a jogger mid-run: kill one", "kill jogger"),
        Do("Joggers", "Cardio is murder: kill 3 joggers", "kill jogger", 3),
        Do("Loiterer", "Kill someone just standing around", "kill idler"),
        Do("Commuters", "Ruin 3 commutes: kill 3 people walking by", "kill walker", 3),
        Do("Double kill", "Double kill: two kills under 3 s apart", "double kill"),
        Do("Triple kill", "Triple kill: three kills, each under 3 s after the last", "triple kill"),
        Do("Sniper", "Sniper: kill someone from 40 m or more", "long shot kill"),
        Do("Sniper 3", "Marksman: 3 kills from 40 m or more", "long shot kill", 3),
        Do("Point blank", "Point blank: kill someone from 3 m or less", "point blank kill"),
        Do("Spree", "Killing spree: 3 kills within 15 s", "kill", 3, within: 15),
        Do("Bloodbath", "Bloodbath: 6 kills within 30 s", "kill", 6, within: 30),
        Do("Cop spree", "Bad day for the force: 2 officers within 20 s", "kill officer", 2, within: 20),
        Do("Headhunter", "Headhunter: 3 headshot kills within 30 s", "headshot kill", 3, within: 30),
        Do("From above", "Death from above: kill someone while in the air", "kill", when: When.Flying),
        Do("Air headshot", "Headshot someone while in the air", "headshot", when: When.Flying),
        Do("Puppet master", "Puppet master: kill someone while riding someone else", "kill", when: When.Riding),
        Do("Trick shot", "Trick shot: a headshot kill while riding someone", "headshot kill", when: When.Riding),
        Do("Outlaw", "Outlaw: kill 2 people while wanted", "kill", 2, when: When.Wanted),
        Do("Cop while wanted", "Resisting arrest: kill an officer while wanted", "kill officer", when: When.Wanted),
        Do("Last stand", "Last stand: kill someone while badly hurt", "kill", when: When.Hurt),
        Do("Ghost", "Ghost: kill someone while hidden (eat a soft pretzel first)", "kill", when: When.Hidden),
        Do("Silent 3", "Silent but deadly: 3 silent kills from heads", "assassinate", 3),
        Do("Silent guard", "Inside job: silently kill a security guard", "assassinate guard"),
        Do("Rooftop sniper", "Rooftop sniper: kill someone while perched on a rooftop", "kill", at: Rooftops, low: -1, high: 3),
        Do("Arch nemesis", "Arch nemesis: kill someone from the top of the arch", "kill", at: "WSP_Arch|Arch", low: -1, high: 4),
        Do("Fountain of blood", "Kill someone while standing in the fountain", "kill", at: "WSP_Fountain|Fountain", onTop: false, low: -1, high: 3),
        Do("Checkmate", "Checkmate: kill someone while standing on a chess table", "kill", at: "ChessTables/|WSP_Prop_ChessTable"),
        Do("Bench warrant", "Bench warrant: kill an officer while standing on a bench", "kill officer", at: "Benches/|WSP_Prop_Bench"),
        Do("Lamp sniper", "Kill someone from on top of a lamp post", "kill", at: "WSP_Prop_Lamp"),
        Do("Hit and run", "Hit and run: run over 3 people within 15 s", "run over", 3, within: 15),
        Do("Rampage", "Rampage: kill 3 people with vehicles", "roadkill", 3),
        Do("Getaway kill", "Run someone over while wanted", "run over", when: When.Wanted),
        Do("Pigeon pile-up", "Feathers everywhere: run over 3 park birds", "run over bird", 3),
        Do("Demolition derby", "Demolition derby: knock over 2 vehicles", "crash", 2),
        Do("Egg 2", "Egg-cellent: kill 2 people with egg bombs", "egg kill", 2),
        Do("Egg cop", "Scrambled cop: hit an officer with an egg bomb", "egg hit officer"),
        Do("Egg from above", "Bombing run: egg bomb someone while riding a person", "egg hit", when: When.Riding),
        // ---- resources ----
        For(Goal.Earn, "Payday", "Payday: pick up $50 in all", 50),
        For(Goal.Earn, "Cash grab", "Cash grab: pick up $150 in all", 150),
        For(Goal.Earn, "Dirty money", "Dirty money: pick up $30 while wanted", 30, when: When.Wanted),
        For(Goal.Spend, "Big spender", "Big spender: spend $20", 20),
        For(Goal.Spend, "Shopaholic", "Shopaholic: spend $40", 40),
        Do("Pickpocket", "Pickpocket: 2 wallets within 20 s", "wallet", 2, within: 20),
        Do("Wallet cop", "Grab a wallet while wanted", "wallet", when: When.Wanted),
        Do("Magazines", "Supply run: pick up 3 police magazines", "magazine", 3),
        Hold(Goal.Reloads, "Ammo hoarder", "Ammo hoarder: hold 5 spare magazines", 5),
        Hold(Goal.Reloads, "Arsenal", "Arsenal: hold 8 spare magazines", 8),
        Hold(Goal.Stock, "Egg stash", "Egg stash: hold 2 egg bombs", 2, "EggBomb"),
        Hold(Goal.Stock, "Egg hoard", "Egg hoard: hold 4 egg bombs", 4, "EggBomb"),
        Hold(Goal.Stock, "Bagel armour", "Bagel armour: hold 3 bagel shields", 3, "BagelShield"),
        Hold(Goal.Stock, "Pizza party", "Pizza party: hold 2 pizza slices", 2, "CrowdFavourite"),
        Hold(Goal.Stock, "Snitch network", "Snitch network: hold 2 snitch flocks", 2, "SnitchFlock"),
        For(Goal.Money, "Rich and wanted", "Hold $50 while wanted", 50, when: When.Wanted),
        Do("Fast food", "Fast food: eat 3 things within 20 s", "eat", 3, within: 20),
        Do("Caffeine", "Double espresso: 2 coffees within 60 s", "eat coffee", 2, within: 60),
        Do("Snack ride", "Snack on the go: eat something while riding someone", "eat", when: When.Riding),
        Do("Wanted snack", "Stress eating: eat something while wanted", "eat", when: When.Wanted),
        Do("Wanted shopping", "Shoplift-adjacent: buy something while wanted", "buy", when: When.Wanted),
        Do("Hurt snack", "Comfort food: eat something while badly hurt", "eat", when: When.Hurt),
        Do("Chess picnic", "Eat a chess piece while standing on its table", "eat chess piece", at: "ChessTables/|WSP_Prop_ChessTable"),
        Do("Chess 3 fast", "Speed chess: eat 3 chess pieces within 20 s", "eat chess piece", 3, within: 20),
        // ---- staying alive ----
        For(Goal.Survive, "By a thread", "Hanging by a thread: stay alive badly hurt for 15 s", 15, when: When.Hurt),
        For(Goal.Survive, "Invisible", "Invisible: stay hidden for 15 s", 15, when: When.Hidden),
        For(Goal.RidePerson, "Human shield", "Human shield: ride people while wanted for 10 s", 10, when: When.Wanted),
        For(Goal.DriveCar, "Getaway", "Getaway driver: drive 200 m while wanted", 200, when: When.Wanted),
        For(Goal.RideBike, "Bike courier", "Bike courier: cycle 150 m while wanted", 150, when: When.Wanted),
        For(Goal.Airborne, "Air escape", "Air escape: 8 s in the air while wanted", 8, when: When.Wanted),
        // ---- flying ----
        For(Goal.Airborne, "Glide", "Stay in the air for 6 s in one go", 6),
        For(Goal.Airborne, "Frequent flyer", "Spend 30 s in the air (it adds up)", 30, inOneGo: false),
        For(Goal.Altitude, "High", "Get 15 m above whatever is below you", 15),
        For(Goal.Altitude, "Sky high", "Get 30 m above whatever is below you", 30),
        // ---- the gun ----
        Do("Spray", "Fire 40 shots", "shoot", 40),
        Do("Reload", "Reload twice", "reload", 2),
    };
}
