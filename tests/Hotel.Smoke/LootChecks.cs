using Hotel.Game.Expedition;
using Hotel.Game.Floors;
using Hotel.Game.Floors.Content;
using Hotel.Game.Loot;
using Hotel.Game.Supplies;
using Rusty.Engine;
using Rusty.Engine.Entities;

// Loot: tables roll the same things for the same seed and key, generated items carry a quality and affixes that change
// what they add, deeper floors roll better and hold different residents, a search gives all or nothing once, and a
// generated item is carried, worn and saved as its resolved roll.
internal static class LootChecks
{
    internal static void Run(IEngineContext engine)
    {
        void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
        var content = Owners.Content(engine);
        LootCatalog loot = content.Loot;
        SuppliesDefinition supplies = content.Supplies;
        static string Shape(ItemRoll[] rolls) => string.Join(";", rolls.Select(r => $"{r.Item}x{r.Count}:{r.Quality}:{string.Join("+", r.Affixes ?? [])}"));

        // Deterministic per seed and key; another seed or key draws afresh.
        string[] keys = Enumerable.Range(0, 40).Select(i => $"floor-1/p{i}/tin").ToArray();
        string[] first = keys.Select(k => Shape(loot.Roll("sewing-tin", 3, new SearchLootDraws(engine.Random, 7), k, supplies))).ToArray();
        string[] again = keys.Select(k => Shape(loot.Roll("sewing-tin", 3, new SearchLootDraws(engine.Random, 7), k, supplies))).ToArray();
        string[] other = keys.Select(k => Shape(loot.Roll("sewing-tin", 3, new SearchLootDraws(engine.Random, 8), k, supplies))).ToArray();
        Check(first.SequenceEqual(again), "the same seed and key roll the same loot");
        Check(!first.SequenceEqual(other) && first.Distinct().Count() > 3, "another seed, or another key, rolls afresh");

        // Depth scaling: rare items only below the first floor, and more of them deeper.
        ItemDefinition gloves = supplies.Item("porter-gloves")!;
        int Rare(int depth) => keys.Count(k => loot.Generate(gloves, 1, depth, new SearchLootDraws(engine.Random, 7), k).Quality == "rare");
        Check(Rare(1) == 0 && Rare(3) > 0 && Rare(5) >= Rare(3), $"rare gloves grow with depth: {Rare(1)}, {Rare(3)}, {Rare(5)}");
        ItemRoll rare = keys.Select(k => loot.Generate(gloves, 1, 5, new SearchLootDraws(engine.Random, 7), k)).First(r => r.Quality == "rare");
        Check(rare.Affixes!.Length == 2 && loot.Fits(rare, gloves) && loot.Name(gloves, rare) != gloves.Name, $"a rare item carries two affixes and its name: {loot.Name(gloves, rare)}");

        // Affixes and quality change what a worn item adds, through its worn source.
        HotelSupplies carried = Owners.Supplies(content, new EntityId(1));
        ItemRoll mule = new("porter-gloves", 1, "fine", ["of-the-mule"]);
        string search = carried.Searches.First().Id;
        Check(carried.Search(search, [mule]) && !carried.Search(search, [mule]), "a search gives once");
        int pocket = Enumerable.Range(0, carried.Capacity).First(i => carried.Slot(i)?.Item == "porter-gloves");
        Check(carried.Slot(pocket)?.Roll == mule && carried.Wear(pocket, carried.Revision), "the rolled gloves are carried as rolled and worn");
        double might = 10 + 2 * loot.Quality("fine")!.Multiply + 2;
        Check(Math.Abs(carried.Stats.Stat("might").Value - might) < 1e-6, $"quality scales the gloves' might and the affix adds its own: {carried.Stats.Stat("might").Value}");

        // A capture keeps the roll; one the catalog could not have made is refused.
        SuppliesState saved = carried.Capture();
        HotelSupplies restored = Owners.Supplies(content, new EntityId(1));
        restored.Validate(saved);
        restored.Restore(saved);
        Check(restored.Worn.Any(w => w.Roll == mule) && Math.Abs(restored.Stats.Stat("might").Value - might) < 1e-6 && restored.Searched(search),
            "a capture restores the rolled item, its stats and the search made");
        foreach (SuppliesState invalid in new[] {
            saved with { Worn = saved.Worn.Select(w => w.Roll is null ? w : w with { Roll = w.Roll with { Affixes = ["of-embers"] } }).ToArray() },
            saved with { Worn = saved.Worn.Select(w => w.Roll is null ? w : w with { Roll = w.Roll with { Quality = "legendary" } }).ToArray() },
            saved with { Collected = [.. saved.Collected, "floor-1/nowhere#remains"] } })
        {
            bool refused = false;
            try { restored.Validate(invalid); } catch (InvalidOperationException) { refused = true; }
            Check(refused, "an affix that does not fit, an unknown quality, or an unknown search is refused");
        }

        // A search that does not fit is refused whole and waits.
        HotelSupplies full = Owners.Supplies(content, new EntityId(1), 1);
        full.Give("bandage", 1);
        Check(!full.Search(full.Searches.First().Id, [new("rounds", 1), new("bandage", 1)]) && !full.Searched(full.Searches.First().Id) &&
            full.Slot(0)?.Count == 1, "a search with no room for all of it gives nothing and waits");

        // Generated floors: depth tables decide the residents, wearables come rolled, containers are placed.
        FloorTunings tunings = FloorTunings.Load(engine, content.Modules, content.Kit, content.Fixtures, content.Supplies.Items, content.Combat.Residents);
        FloorSources sources = new(content.Modules, content.Kit, content.Fixtures, content.Supplies.Items, content.Combat.Residents,
            content.Player.Controller(engine.Spatial), loot);
        FloorContent[] Floors(int depth) => Enumerable.Range(0, 6).Select(r => FloorGenerator.Generate(engine, FloorSeed.Current((ulong)r, depth, 0), tunings, sources).Floor)
            .Where(f => f is not null).Select(f => f!.Content).ToArray();
        FloorContent[] shallow = Floors(1), deep = Floors(4);
        Check(shallow.Length >= 4 && deep.Length >= 4, "floors generate at both depths");
        Check(shallow.All(f => f.Residents.All(r => r.Kind != "night-maid")) && deep.Any(f => f.Residents.Any(r => r.Kind == "night-maid")),
            "the night maid walks only deeper floors");
        Check(deep.SelectMany(f => f.Finds).Where(f => f.Item is { } i && LootCatalog.Generates(supplies.Item(i)!)).All(f => f.Roll is { Generated: true }) &&
            shallow.Concat(deep).All(f => f.Containers.Length >= 1), "wearables are placed rolled and every floor holds a container");
        Console.WriteLine($"Loot checks passed: {loot.Tables.Length} tables, {loot.Qualities.Length} qualities, {loot.Affixes.Length} affixes; " +
            "deterministic rolls, depth scaling, affixes on worn stats, search once and whole, rolled saves, and depth tables on generated floors.");
    }
}
