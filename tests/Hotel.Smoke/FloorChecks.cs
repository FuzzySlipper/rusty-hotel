using Hotel.Game.Floors;
using Rusty.Engine;
using Rusty.Engine.Testing;

// Floor generation draws only through Engine keyed draws: the same seed resolves to the same plan hash in a fresh
// host, and any change to run, version, depth or shift changes it.
internal static class FloorChecks
{
    // A stand-in stage: it resolves every draw once into a value record, as real stages must.
    private sealed record StubRoom(string Id, string Module, bool Locked, long Width);
    private static readonly string[] Modules = ["guest-a", "guest-b", "linen", "lounge"];

    private static StubRoom[] Stub(FloorDraws draws) => Enumerable.Range(0, 12).Select(i =>
    {
        string id = $"r{i}";
        return new StubRoom(id, Modules[draws.Weighted(FloorStage.Embedding, "module", id, [4, 4, 1, 1])],
            draws.OneIn(FloorStage.Graph, "lock", id, 3), draws.Long(FloorStage.Embedding, "width", id, 3, 6));
    }).ToArray();

    private static string Hash(IEngineContext engine, FloorSeed seed)
    {
        CanonicalText plan = new();
        foreach (StubRoom room in Stub(new FloorDraws(engine.Random, seed)))
            plan.Line("room", room.Id, room.Module, room.Locked ? "locked" : "open", CanonicalText.Number(room.Width));
        return FloorIdentity.Of(seed, plan).PlanHash;
    }

    private static T Hosted<T>(Func<IEngineContext, T> call)
    {
        using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions
            { PersistenceRoot = Path.Combine(Path.GetTempPath(), "hotel-floors-" + Guid.NewGuid()) });
        return host.Call(call);
    }

    internal static void Run()
    {
        void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
        FloorSeed seed = FloorSeed.Current(run: 4242, depth: 2, shift: 0);
        string first = Hosted(engine => Hash(engine, seed));
        Check(Hosted(engine => Hash(engine, seed)) == first, "the same seed resolves to the same plan hash in a fresh host");
        Check(Hosted(engine => Hash(engine, seed with { Run = 4243 })) != first, "a different run seed changes the plan");
        Check(Hosted(engine => Hash(engine, seed with { Version = seed.Version + 1 })) != first, "a generator version bump redraws the plan");
        Check(Hosted(engine => Hash(engine, seed with { Depth = 3 })) != first, "a different depth draws a different floor");
        Check(Hosted(engine => Hash(engine, seed with { Shift = 1 })) != first, "a shift redraws the floor");

        Hosted(engine =>
        {
            FloorDraws draws = new(engine.Random, seed);
            // Draws are keyed, not streamed: order and interleaving do not change a result.
            long a = draws.Long(FloorStage.Graph, "probe", "a", 0, 1_000_000);
            draws.Long(FloorStage.Content, "probe", "b", 0, 1_000_000);
            Check(draws.Long(FloorStage.Graph, "probe", "a", 0, 1_000_000) == a, "a keyed draw holds no stream position");
            Check(draws.Long(FloorStage.Content, "probe", "a", 0, 1_000_000) != a, "stages draw under their own scopes");
            // One-in-N is uniform across a wide range: about a quarter of 4000 keys at N = 4.
            int hits = Enumerable.Range(0, 4000).Count(i => draws.OneIn(FloorStage.Content, "census", $"k{i}", 4));
            Check(hits is > 880 and < 1120, $"one-in-four draws land about a quarter of the time; found {hits} of 4000");
            int[] picked = new int[3];
            for (int i = 0; i < 3000; i++) picked[draws.Weighted(FloorStage.Content, "weights", $"k{i}", [1, 0, 2])]++;
            Check(picked[1] == 0 && picked[0] is > 850 and < 1150, $"weighted draws follow their weights; found {string.Join(", ", picked)}");
            bool refused = false;
            try { draws.Long(FloorStage.Graph, "probe", "a", 2, 1); } catch (ArgumentOutOfRangeException) { refused = true; }
            Check(refused, "an inverted range is refused");
            return 0;
        });
        Console.WriteLine("Floor checks passed: keyed Engine draws, repeatable plan hashes across hosts, run/version/depth/shift sensitivity, scope independence and uniform one-in-N and weighted draws.");
    }
}
