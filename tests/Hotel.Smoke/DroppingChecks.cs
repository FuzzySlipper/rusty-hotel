using System.Numerics;
using Hotel.Game.Content;
using Hotel.Game.Loot;
using Hotel.Game.Supplies;
using Rusty.Engine;
using Rusty.Engine.Entities;

// Leaving stacks on the floor from the field case and taking them back with the ordinary use key.
internal static class DroppingChecks
{
    internal static void Run(IEngineContext engine)
    {
        void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
        var content = Owners.Content(engine);
        using var scene = Owners.Scene(engine, content);
        using var player = Owners.Player(engine, scene, content);
        HotelSupplies supplies = Owners.Supplies(content, scene.PlayerEntity, 4, player, engine, scene);
        var combat = Owners.Combat(engine, scene, player, supplies, content);
        var spirit = Owners.Spirit(content, supplies, combat, player);
        var route = Owners.Route(engine, scene, player, supplies, spirit, content);
        DroppingTuning tuning = content.Supplies.Dropping;
        string Claim(string action, int from) => $$"""{"action":"{{action}}","from":{{from}},"revision":{{supplies.Revision}}}""";

        // Standing in the corridor facing down it, a pocket of matches is dropped through the field case's own intent.
        player.Place(new(0, .875f, -4), 0);
        supplies.Give("matches", 2);
        supplies.HandleIntents([SuppliesChecks.Claim(Claim("drop", 0))]);
        DroppedStack lying = supplies.Dropped.Single();
        Vector3 expected = player.Feet + new Vector3(MathF.Sin(player.Yaw), 0, -MathF.Cos(player.Yaw)) * tuning.Ahead;
        Check(supplies.Slot(0) is null && lying.Stack == new ItemStack("matches", 2) && Vector3.Distance(new(lying.X, lying.Y, lying.Z), expected) < 1e-4f &&
            supplies.Message == Template.Fill(content.Supplies.Text.Dropped, ("item", supplies.Item("matches").Name), ("count", 2)),
            "the whole stack leaves the case and lies ahead of the investigator's feet");
        // Facing a wall a step away, the stack stays on this side of it, in reach.
        player.Place(new(0, .875f, 4.7f), 180);
        supplies.Give("matches", 1);
        Check(supplies.Drop(PocketOf(supplies, "matches"), supplies.Revision) && supplies.Dropped[^1].Z < 4.9f,
            $"a stack dropped facing the refuge's wall stays inside the room: z {supplies.Dropped[^1].Z:F2}");
        Check(supplies.PickupDropped(supplies.Dropped[^1].Id), "and is taken back");
        player.Place(new(0, .875f, -4), 0);
        ulong stale = supplies.Revision - 1;
        supplies.Give("bandage", 1);
        Check(!supplies.Drop(0, stale) && supplies.Slot(0) is not null, "a stale drop is refused");
        Check(!supplies.Drop(3, supplies.Revision) && supplies.Message == content.Supplies.Text.EmptyPocket, "an empty pocket drops nothing");
        supplies.Pickup("survey-reel");
        int reel = Enumerable.Range(0, supplies.Capacity).First(i => supplies.Slot(i)?.Item == "reel");
        Check(!supplies.Drop(reel, supplies.Revision) && supplies.Message == content.Supplies.Text.KeepForReturn,
            "an expedition find stays carried for the return");

        // A generated item keeps its quality on the floor and back in the case.
        ItemRoll roll = content.Loot.Roll("maid-remains", 3, new SearchLootDraws(engine.Random, 7), "dropping", content.Supplies)
            .FirstOrDefault(r => r.Generated) ?? new ItemRoll("kid-gloves", 1, content.Loot.Qualities[^1].Id, []);
        supplies.Reset();
        Check(supplies.Dropped.Count == 0, "a reset leaves nothing on the floor");
        // A search admits the rolled item as it was resolved.
        Check(supplies.Search(content.Excursion.Placements.Containers.FirstOrDefault()?.Id ?? Hotel.Game.HotelWorld.Remains(content.Excursion.Placements.Residents[0].Id), [roll]),
            "a rolled item is carried");
        string rolledName = supplies.Name(supplies.Slot(0)!.Value);
        Check(supplies.Drop(0, supplies.Revision) && supplies.Dropped.Single().Stack.Roll == roll, "the roll goes onto the floor with the stack");

        // Taken back with the use key, aimed at the bag.
        Vector3 bag = new(supplies.Dropped[0].X, supplies.Dropped[0].Y + content.Route.Interaction.DroppedFocusLift, supplies.Dropped[0].Z);
        Vector3 delta = bag - player.Eye;
        double turn = (Math.Atan2(delta.X, -delta.Z) - player.Yaw) * 180 / Math.PI;
        player.LookBy((turn % 360 + 540) % 360 - 180, Math.Atan2(delta.Y, Math.Sqrt(delta.X * delta.X + delta.Z * delta.Z)) * 180 / Math.PI);
        route.Update();
        Check(route.Prompt == Template.Fill(content.Route.Text.Ready, ("label", Template.Fill(content.Route.Text.Take, ("item", rolledName)))),
            $"the dropped stack is offered by name: '{route.Prompt}' (bag {bag}, feet {player.Feet}, yaw {player.Yaw:F2}, pitch {player.LookState.PitchRadians:F2})");
        route.Use();
        Check(supplies.Dropped.Count == 0 && supplies.Slot(0)?.Roll == roll, "the use key takes it back whole, roll and all");

        // A full case makes room by dropping; a floor holds only so many stacks.
        supplies.Reset();
        foreach (string item in new[] { "kid-gloves", "galoshes", "hip-flask", "bromide-tonic" }) supplies.Give(item, 1);
        Check(!supplies.Pickup("linen-rounds"), "a full case refuses a find");
        Check(supplies.Drop(0, supplies.Revision) && supplies.Pickup("linen-rounds"), "dropping a stack makes room for it");
        Check(supplies.Drop(1, supplies.Revision), "another pocket is freed");
        while (supplies.Dropped.Count < tuning.Limit) { supplies.Give("matches", 1); supplies.Drop(PocketOf(supplies, "matches"), supplies.Revision); }
        supplies.Give("matches", 1);
        Check(!supplies.Drop(PocketOf(supplies, "matches"), supplies.Revision) && supplies.Message == content.Supplies.Text.DropFull &&
            supplies.Dropped.Count == tuning.Limit, "a floor holds at most its limit of dropped stacks");

        // What a floor remembers: its stacks round trip, and impossible ones are refused.
        DroppedStack[] kept = [.. supplies.Dropped];
        supplies.ValidateDropped(kept);
        foreach (DroppedStack[] bad in new[] {
            new[] { kept[0] with { Stack = new("matches", 99) } }, new[] { kept[0] with { Stack = new("unknown", 1) } },
            new[] { kept[0] with { Stack = new("reel", 1) } }, new[] { kept[0], kept[0] }, new[] { kept[0] with { Id = "elsewhere" } },
            [.. kept, kept[0] with { Id = "dropped/999" }] })
        {
            bool refused = false;
            try { supplies.ValidateDropped(bad); } catch (InvalidOperationException) { refused = true; }
            Check(refused, "impossible dropped stacks are refused");
        }
        supplies.RestoreDropped([]);
        supplies.RestoreDropped(kept);
        supplies.Give("matches", 1);
        supplies.RestoreDropped(kept[..1]);
        Check(supplies.Drop(PocketOf(supplies, "matches"), supplies.Revision) && supplies.Dropped[^1].Id != kept[0].Id,
            "a restored floor names new drops apart from what lies there");
        Console.WriteLine($"Dropping checks passed: the field case intent, stale, empty and expedition refusals, a roll kept on the floor, taking back with the use key, making room, the floor's limit of {tuning.Limit} and remembered stacks.");
    }

    private static int PocketOf(HotelSupplies supplies, string item) =>
        Enumerable.Range(0, supplies.Capacity).First(i => supplies.Slot(i)?.Item == item);
}
