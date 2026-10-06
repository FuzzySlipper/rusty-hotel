using System.Numerics;
using Hotel.Game.Actions;
using Hotel.Game.Combat;
using Hotel.Game.Player;
using Hotel.Game.Scene;
using Hotel.Game.Supplies;
using Rusty.Engine;
using Rusty.Engine.Entities;

// One action pipeline for every hand: each delivery kind lands through an Engine query in the real west wing, a cost
// is checked and spent once at acceptance, a cooldown refuses until it runs out, and what is worn adds to the damage.
internal static class ActionChecks
{
    internal static void Run(IEngineContext engine)
    {
        var content = Owners.Content(engine);
        using HotelScene scene = Owners.Scene(engine, content);
        using HotelPlayer player = Owners.Player(engine, scene, content);
        HotelSupplies supplies = Owners.Supplies(content, scene.PlayerEntity, 12);
        HotelCombat combat = Owners.Combat(engine, scene, player, supplies, content);
        HotelEnemy porter = combat.Enemies.Single(e => e.Id == "porter");
        ActionCatalog actions = content.Combat.Actions;
        void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
        void Steps(float seconds) { for (int i = 0; i < (int)Math.Round(seconds * 60); i++) combat.Step(1f / 60); }
        void At(float x, float z, Vector3 target)
        {
            player.Reset();
            scene.Entities.Set(scene.PlayerEntity, EngineComponentTypes.Transform, new Transform(new(x, .875f, z), Quaternion.Identity, Vector3.One));
            Vector3 delta = target - player.Eye;
            player.LookBy(Math.Atan2(delta.X, -delta.Z) * 180 / Math.PI, Math.Atan2(delta.Y, Math.Sqrt(delta.X * delta.X + delta.Z * delta.Z)) * 180 / Math.PI);
        }
        void Hold(string item)
        {
            supplies.SetHealth(supplies.MaximumHealth); // The porter answers every test; it must not end one.
            supplies.Give(item, 1);
            supplies.Wear(Enumerable.Range(0, supplies.Capacity).First(i => supplies.Slot(i)?.Item == item), supplies.Revision);
        }
        float Seconds(string action) { ActionTiming t = actions.Action(action)!.Timing; return t.Windup + t.Commit + t.Recovery; }
        int health = porter.Health.ValueInt;

        // Melee: a swept capsule meets the porter; worn gloves add might and their outgoing blunt contribution.
        Hold("porter-gloves");
        At(-4.1f, -5.3f, porter.Eye);
        Check(combat.HandAction(false)?.Delivery.Kind == DeliveryKind.Melee && combat.Act(false), "the pry bar swings");
        Steps(Seconds("prybar-swing"));
        int swing = 14 + 12 + 2;
        Check(porter.Health.ValueInt == health - swing, $"might and the gloves' contribution raise a swing to {swing}: {health - porter.Health.ValueInt}");
        health = porter.Health.ValueInt;

        // Area: the bell staggers and stings every resident in reach with a clear line; its stamina is spent once, at acceptance.
        Hold("service-bell");
        Check(combat.Holding?.Item.Id == "service-bell" && supplies.Slot(Enumerable.Range(0, supplies.Capacity).First(i => supplies.Slot(i)?.Item == "prybar")) is not null,
            "a held item worn into a full hand trades places with the pry bar");
        At(-4.1f, -5.3f, porter.Eye);
        int stamina = supplies.Stamina;
        Check(combat.Act(false) && supplies.Stamina == stamina - 20, "the bell spends its stamina when accepted");
        Steps(Seconds("bell-ring"));
        int ring = (int)Math.Round((6 + 10 * .5) * (1 - .2), MidpointRounding.AwayFromZero);
        Check(porter.Health.ValueInt == health - ring && porter.Stats.Effects.Active.Any(e => e.Definition.Id == "staggered") &&
            supplies.Stamina <= stamina - 20 + 8 * Seconds("bell-ring") + 1, $"the ring lands once on the porter, against its spirit resistance: {health - porter.Health.ValueInt}");
        health = porter.Health.ValueInt;
        // Cooldown: ready again only after its time.
        Check(!combat.Act(false) && combat.Notice.Contains(actions.Action("bell-ring")!.Name) && combat.User.Cooldown("bell-ring") > 0,
            "the bell is refused while it cools down");
        Steps(actions.Action("bell-ring")!.Timing.Cooldown);
        // Refused cost: too little stamina refuses before anything is spent.
        supplies.Stats.Track(HotelSupplies.StaminaTrack).SetCurrent(5);
        int accepted = combat.AcceptedAttacks;
        Check(!combat.Act(false) && supplies.Stamina == 5 && combat.AcceptedAttacks == accepted && combat.Notice.Contains("Stamina"),
            "a cost that cannot be met is refused, naming the track, and nothing is spent");

        // Projectile: the flare flies on admitted time and lands on the porter; it is in flight between.
        Hold("flare-pistol");
        supplies.Give("rounds", 1); supplies.Use(supplies.PocketOf("ammunition"), supplies.Revision);
        // The porter is held at its post so the flare has a distance to fly.
        scene.Entities.Set(porter.EntityId, EngineComponentTypes.Transform, new Transform(porter.Spawn, Quaternion.Identity, Vector3.One));
        combat.Interrupt(porter, content.Mechanics.Effect("hushed")!, "test.hold");
        At(-4.1f, -7.5f, porter.Eye);
        int ammo = supplies.Ammo;
        Check(combat.Act(false) && supplies.Ammo == ammo - 2, "the flare draws two rounds once");
        Steps(actions.Action("flare-shot")!.Timing.Windup + 2f / 60);
        Check(combat.Projectiles.Count == 1 && porter.Health.ValueInt == health, $"the flare is in flight and has not landed yet: {combat.Projectiles.Count} in flight, porter {porter.Health.ValueInt}/{health} at {porter.Position}, player {player.Position}, notice '{combat.Notice}'");
        Steps(1);
        Check(combat.Projectiles.Count == 0 && porter.Health.ValueInt <= health - 18 && porter.Stats.Effects.Active.Any(e => e.Definition.Id == "burning"),
            "the flare lands on the porter and sets it burning");
        health = porter.Health.ValueInt;

        // Self: the pistol's reload uses a carried packet when it lands, and touches no one.
        Hold("survey-pistol");
        supplies.Give("rounds", 1);
        ammo = supplies.Ammo;
        Check(combat.HandAction(true)?.Delivery.Kind == DeliveryKind.Self && combat.Act(true) && supplies.Ammo == ammo, "a reload costs nothing at acceptance");
        Steps(Seconds("pistol-reload") + 2f / 60);
        Check(supplies.Ammo == ammo + 6 && supplies.PocketOf("ammunition") < 0 && porter.Health.ValueInt <= health, "the reload uses the packet once as it lands");

        // Hitscan: the shot reaches the porter at once on commitment.
        At(-4.1f, -3.8f, porter.Eye);
        Check(combat.HandAction(false)?.Delivery.Kind == DeliveryKind.Hitscan && combat.Act(false), "the pistol fires");
        Steps(actions.Action("pistol-shot")!.Timing.Windup + 2f / 60);
        Check(!porter.Alive || porter.Health.ValueInt <= health - 36 + 1, "the shot lands on the porter");
        Console.WriteLine($"Action checks passed: {actions.Actions.Length} actions; melee, area, projectile, self and hitscan deliveries through Engine queries, " +
            "costs spent once and refused whole, cooldowns, and worn contributions raising damage.");
    }
}
