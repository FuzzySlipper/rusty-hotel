using System.Numerics;
using Hotel.Game.Combat;
using Hotel.Game.Player;
using Hotel.Game.Residents;
using Hotel.Game.Scene;
using Hotel.Game.Supplies;
using Rusty.Engine;
using Rusty.Engine.Entities;

// Residents composed from parts, in the west wing's long corridor: an Engine vision cone and remembered awareness, each
// way of moving, a resident choosing between two actions by distance, and residents of hostile factions fighting.
internal static class ResidentChecks
{
    internal static void Run(IEngineContext engine)
    {
        var content = Owners.Content(engine);
        void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
        ResidentKind maid = content.Combat.Residents.Single(r => r.Id == "night-maid");
        ResidentKind porter = content.Combat.Residents.Single(r => r.Id == "porter");
        // The corridor runs north-south at x = 0; a resident placed at z = -9 faces -z (away from the refuge) at rest.
        (HotelScene Scene, HotelPlayer Player, HotelSupplies Supplies, HotelCombat Combat) World(params (string Id, ResidentKind Kind, Vector3 At)[] placed)
        {
            HotelScene scene = Owners.Scene(engine, content);
            HotelPlayer player = Owners.Player(engine, scene, content);
            HotelSupplies supplies = Owners.Supplies(content, scene.PlayerEntity, 12);
            var definition = content.Combat with { Residents = [.. content.Combat.Residents, .. placed.Select(p => p.Kind with { Id = p.Id })] };
            HotelCombat combat = new(engine, scene, player, supplies, definition,
                placed.Select(p => new ResidentPlacement(p.Id, p.Id, [p.At.X, p.At.Y, p.At.Z])).ToArray());
            return (scene, player, supplies, combat);
        }
        void Stand(HotelScene scene, HotelPlayer player, float z) =>
            scene.Entities.Set(scene.PlayerEntity, EngineComponentTypes.Transform, new Transform(new(0, .875f, z), Quaternion.Identity, Vector3.One));
        void Steps(HotelCombat combat, float seconds) { for (int i = 0; i < (int)Math.Round(seconds * 60); i++) combat.Step(1f / 60); }
        float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new(a.X, a.Z), new(b.X, b.Z));

        // Perception: a narrow cone facing away misses the investigator; facing them, the Engine query sees them; out of
        // sight, the resident stays aware for its memory, then forgets.
        ResidentKind watcher = porter with { Perception = new(8, 90, 1, 0), Movement = new(0, 0, Post: new()) };
        {
            var (scene, player, supplies, combat) = World(("watcher", watcher, new(0, .9f, -9)));
            using var _ = scene; using var __ = player;
            HotelEnemy w = combat.Enemies.Single();
            Stand(scene, player, -5);
            w.Yaw = 0; Steps(combat, .1f);
            Check(w.Awareness.Target is null, "a resident facing away does not see the investigator behind it");
            w.Yaw = MathF.PI; Steps(combat, .1f);
            Check(w.Awareness.Target is not null, "facing the investigator within range and cone, the Engine query sees them");
            Stand(scene, player, 3.5f); // Back in the refuge, beyond its range.
            w.Yaw = 0; Steps(combat, .5f);
            Check(w.Awareness.Target is not null, "out of sight, the resident stays aware for its memory");
            Steps(combat, 1);
            Check(w.Awareness.Target is null, "awareness fades after its memory");
        }

        // Post: turns to face but never walks.
        {
            var (scene, player, supplies, combat) = World(("post", watcher with { Perception = new(8, 360, 1, 0) }, new(0, .9f, -9)));
            using var _ = scene; using var __ = player;
            HotelEnemy post = combat.Enemies.Single();
            Stand(scene, player, -6); Steps(combat, 2);
            Check(Flat(post.Position, post.Spawn) < .01f && MathF.Abs(post.Yaw - MathF.PI) < .2f, "a post resident turns to the investigator and stays put");
        }

        // Stalk: closes in within its leash, stops at reach, and walks back to its post once it forgets.
        {
            ResidentKind stalker = porter with { Perception = new(6, 360, 1, 0) };
            var (scene, player, supplies, combat) = World(("stalker", stalker, new(0, .9f, -9)));
            using var _ = scene; using var __ = player;
            HotelEnemy s = combat.Enemies.Single();
            Stand(scene, player, -5); Steps(combat, 3);
            Check(Flat(s.Position, s.Spawn) > 1.5f && Flat(s.Position, player.Position) < 2.4f, $"a stalker closes in: {s.Position}");
            Stand(scene, player, 3.5f); Steps(combat, 6);
            Check(Flat(s.Position, s.Spawn) < .5f, $"having forgotten, the stalker walks back to its post: {s.Position}");
        }

        // Patrol: unaware, it walks its loop of points.
        {
            ResidentKind patroller = maid with { Movement = maid.Movement with { Patrol = new([[0, 0], [0, 2.5f]], 0) } };
            var (scene, player, supplies, combat) = World(("patroller", patroller, new(0, .9f, -9)));
            using var _ = scene; using var __ = player;
            HotelEnemy p = combat.Enemies.Single();
            Stand(scene, player, 3.5f); Steps(combat, 2.2f);
            Check(p.Awareness.Target is null && Flat(p.Position, p.Spawn) > 1.5f, $"a patroller walks toward its next point: {p.Position}");
        }

        // Ambush: an investigator in plain sight is ignored until within the trigger; then it springs and stalks.
        {
            ResidentKind lurker = porter with { Perception = new(8, 360, 1, 0), Movement = porter.Movement with { Stalk = null, Ambush = new(2) } };
            var (scene, player, supplies, combat) = World(("lurker", lurker, new(0, .9f, -9)));
            using var _ = scene; using var __ = player;
            HotelEnemy a = combat.Enemies.Single();
            Stand(scene, player, -5); Steps(combat, 1);
            Check(a.Awareness.Target is null && Flat(a.Position, a.Spawn) < .01f, "an ambusher ignores an investigator outside its trigger");
            Stand(scene, player, -7.4f); Steps(combat, .2f);
            Check(a.Awareness.Target is not null && a.Awareness.Sprung, "within the trigger the ambush springs");
        }

        // Flee: badly hurt, it backs away to keep its distance.
        {
            var (scene, player, supplies, combat) = World(("fleer", maid, new(0, .9f, -9)));
            using var _ = scene; using var __ = player;
            HotelEnemy f = combat.Enemies.Single();
            f.Yaw = MathF.PI;
            f.Health.SetCurrent(f.Health.MaximumValue * maid.Movement.Flee!.Below / 2);
            Stand(scene, player, -7); Steps(combat, 1.5f);
            Check(Flat(f.Position, player.Position) > 2.4f && f.User.Current is null, $"a hurt resident backs away instead of attacking: {f.Position}");
        }

        // Two actions: the maid throws a cup from range and slaps up close.
        {
            var (scene, player, supplies, combat) = World(("maid", maid, new(0, .9f, -9)));
            using var _ = scene; using var __ = player;
            HotelEnemy m = combat.Enemies.Single();
            m.Yaw = MathF.PI;
            Stand(scene, player, -5); Steps(combat, .1f);
            Check(m.User.Current?.Id == "maid-cup", $"at range the maid throws: {m.User.Current?.Id}");
            Steps(combat, 3);
            Check(supplies.Health < 70 || combat.Projectiles.Count > 0, "the cup flies on admitted time and lands");
            m.User.Reset(); scene.Entities.Set(m.EntityId, EngineComponentTypes.Transform, new Transform(m.Spawn, Quaternion.Identity, Vector3.One));
            Stand(scene, player, -7.9f); Steps(combat, .1f);
            Check(m.User.Current?.Id == "maid-slap", $"up close the maid slaps: {m.User.Current?.Id}");
        }

        // A resident's shot passes its own faction by: the maid's cup flies through a still porter of the staff and
        // lands on the investigator behind it.
        {
            ResidentKind ally = porter with { Perception = new(.1f, 1, 0, 0), Movement = new(0, 0, Post: new()) };
            var (scene, player, supplies, combat) = World(("maid", maid with { Movement = new(0, 0, Post: new()) }, new(0, .9f, -11)),
                ("ally", ally, new(0, .9f, -9)));
            using var _ = scene; using var __ = player;
            HotelEnemy m = combat.Enemies.Single(e => e.Id == "maid"), a = combat.Enemies.Single(e => e.Id == "ally");
            m.Yaw = MathF.PI;
            Stand(scene, player, -7); Steps(combat, .1f);
            Check(m.User.Current?.Id == "maid-cup", "the maid throws past its ally");
            Steps(combat, 2);
            Check(a.Health.Value == a.Health.MaximumValue && supplies.Health < 70, $"the cup spares the allied porter and hits the investigator: ally {a.Health.Value}, me {supplies.Health}");
        }

        // Factions: a lost guest and a maid of the staff, hostile to each other, fight while the investigator is away.
        {
            ResidentKind guest = maid with { Faction = "guests", Movement = new(1.1f, 6, Stalk: new()) };
            var (scene, player, supplies, combat) = World(("maid", maid with { Movement = new(1.1f, 6, Stalk: new()) }, new(0, .9f, -9)), ("guest", guest, new(0, .9f, -11)));
            using var _ = scene; using var __ = player;
            HotelEnemy m = combat.Enemies.Single(e => e.Id == "maid"), g = combat.Enemies.Single(e => e.Id == "guest");
            m.Yaw = 0; g.Yaw = MathF.PI; // Facing each other along the corridor.
            Stand(scene, player, 3.5f);
            Steps(combat, 6);
            Check(m.Awareness.Target == g && g.Awareness.Target == m, "hostile factions notice each other");
            Check(m.Health.Value < m.Health.MaximumValue && g.Health.Value < g.Health.MaximumValue, $"hostile residents strike each other: maid {m.Health.Value} at {m.Position} {m.User.Phase}, guest {g.Health.Value} at {g.Position} {g.User.Phase}");
            Check(content.Combat.Factions.AreHostile("staff", "investigator") && !content.Combat.Factions.AreHostile("staff", "lamps"),
                "staff and lamps are each hostile to the investigator, not to each other");
        }
        Console.WriteLine("Resident checks passed: Engine vision cone and awareness memory, post, stalk and return, patrol, ambush, flee, " +
            "choosing between two actions by distance, and hostile factions fighting each other.");
    }
}
