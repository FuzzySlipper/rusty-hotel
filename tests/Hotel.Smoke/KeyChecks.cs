using System.Numerics;
using Hotel.Game.Floors;
using Hotel.Game.Route;
using Hotel.Game.Scene;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Interaction;

// A locked door opens only for the holder of its key; keys are world finds the route keeps; a generated floor hangs a
// keyed door in every lock and a far-side latch in its shortcut, and names each key for the room it opens.
internal static class KeyChecks
{
    internal static void Run(IEngineContext engine)
    {
        void Check(bool value, string why) { if (!value) throw new InvalidOperationException(why); }
        var content = Owners.Content(engine);
        // The west wing's survey door, locked with a key that lies in the corridor.
        DoorDefinition survey = content.Excursion.Route.Doors.Single(d => d.Id == "survey") with { Key = "test-key", LockedPrompt = "Locked · Needs the test key" };
        KeyDefinition key = new("test-key-find", "test-key", "test key", [0, 0.2f, -11.4f]);
        ExcursionRoute keyed = content.Excursion.Route with { Doors = [survey, .. content.Excursion.Route.Doors.Where(d => d.Id != "survey")], Keys = [key] };
        using HotelScene scene = new(engine, content.Surfaces, content.Aging, content.Excursion.Geometry, keyed.Doors);
        using var player = Owners.Player(engine, scene, content);
        var supplies = Owners.Supplies(content, scene.PlayerEntity);
        var combat = Owners.Combat(engine, scene, player, supplies, content);
        var spirit = Owners.Spirit(content, supplies, combat, player);
        HotelRoute route = new(engine, scene, player, supplies, spirit, content.Route, keyed, content.Excursion.Placements.Refuge,
            () => false, () => { }, _ => false);
        route.Reset();
        void At(float x, float z, Vector3 target)
        {
            player.Reset();
            scene.Entities.Set(scene.PlayerEntity, EngineComponentTypes.Transform, new Transform(new(x, .875f, z), Quaternion.Identity, Vector3.One));
            Vector3 delta = target - player.Eye;
            player.LookBy(Math.Atan2(delta.X, -delta.Z) * 180 / Math.PI, Math.Atan2(delta.Y, Math.Sqrt(delta.X * delta.X + delta.Z * delta.Z)) * 180 / Math.PI);
            route.Update();
        }
        Vector3 door = new(0, 1.3f, -13.94f);
        At(0, -12.5f, door);
        Check(route.Prompt == survey.LockedPrompt && !route.Interaction.UseFocused().Performed && route.OpenDoors.Length == 0,
            "a locked door shows its prompt and refuses without its key: " + route.Prompt);
        At(0, -10.2f, Authored(key.Point));
        Check(route.Prompt.Contains(key.Name) && route.Interaction.UseFocused().Performed && route.Keys.SequenceEqual(["test-key"]), "the key is taken: " + route.Prompt);
        At(0, -12.5f, door);
        Check(route.Interaction.UseFocused().Performed && route.OpenDoors.Contains("survey"), "the held key opens its door");
        route.Restore([], ["test-key", "unknown-key"]);
        Check(route.Keys.SequenceEqual(["test-key"]) && route.OpenDoors.Length == 0, "remembered keys come back; unknown ones do not");
        route.Restore([]);
        Check(route.Keys.Length == 0, "a fresh visit holds no keys");

        // A generated floor with locks: a keyed door per lock opening into what it guards, a key per lock, and the latch.
        HotelFloors floors = new(engine, content);
        floors.Begin(5);
        int locks = 0, latches = 0;
        for (int depth = 1; depth <= 6; depth++)
        {
            if (floors.Floor(depth) is not { } floor) continue;
            floors.Arrive(depth);
            GeneratedFloor generated = floors.Current!;
            DoorDefinition[] keyedDoors = floor.Route.Doors.Where(d => d.Key is not null).ToArray();
            Check(keyedDoors.Length == generated.Layout.Locks.Length && floor.Route.Keys.Length == generated.Layout.Locks.Length &&
                generated.Layout.Locks.All(l => keyedDoors.Count(d => d.Key == l.Item) == 1 && floor.Route.Keys.Count(k => k.Item == l.Item) == 1),
                $"depth {depth}: one keyed door and one key per lock");
            Check(floor.Route.Doors.Count(d => d.FarSideLatch && d.Key is null) == (generated.Layout.Latch is null ? 0 : 1) &&
                floor.Route.Doors.All(d => d.LockedPrompt is { Length: > 0 }), $"depth {depth}: the shortcut has its latch and every door its prompt");
            Check(floor.Route.Keys.All(k => k.Name.StartsWith("key to the ", StringComparison.Ordinal)), $"depth {depth}: keys are named for their rooms");
            if (floor.Route.Doors.FirstOrDefault(d => d.FarSideLatch) is { } latch)
            {
                var passage = generated.Layout.Passage[^1];
                Vector3 inside = new((passage.Min[0] + passage.Max[0]) / 2, 0, (passage.Min[1] + passage.Max[1]) / 2);
                Check(Vector3.Dot(Authored(latch.UnlockDirection!), inside - Authored(latch.Hinge) with { Y = 0 }) > 0, $"depth {depth}: the latch opens from the passage side");
            }
            locks += keyedDoors.Length;
            latches += generated.Layout.Latch is null ? 0 : 1;
        }
        Check(locks > 0 && latches > 0, "some generated floors have locks and latches");
        Console.WriteLine($"Key checks passed: a locked door refuses with its prompt until its key is taken, keys are remembered and cleared, the held key opens the door; {locks} generated locks each have one keyed door and one named key, and {latches} shortcuts their latch.");
        static Vector3 Authored(float[] v) => new(v[0], v[1], v[2]);
    }
}
