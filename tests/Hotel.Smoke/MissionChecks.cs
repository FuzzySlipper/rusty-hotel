using Hotel.Game.Floors;
using Hotel.Game.Floors.Mission;
using Rusty.Engine;

// Mission graphs grow by fail-atomic rules from authored weights, repeat by canonical hash, and are refused when a
// key sits behind its own gate or a place has no way back.
internal static class MissionChecks
{
    internal static void Run(IEngineContext engine)
    {
        void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
        MissionTuning tuning = MissionTuning.Load(engine);
        string Hash(FloorSeed seed, MissionGraph graph)
        {
            CanonicalText text = new();
            graph.Write(text);
            return FloorIdentity.Of(seed, text).PlanHash;
        }
        MissionResult Grow(FloorSeed seed) => MissionGenerator.Generate(tuning, new FloorDraws(engine.Random, seed));

        FloorSeed seed = FloorSeed.Current(run: 77, depth: 2, shift: 0);
        MissionResult first = Grow(seed);
        Check(first.Succeeded, "a floor's mission graph grows: " + string.Join("; ", first.Failure));
        Check(Hash(seed, Grow(seed).Graph) == Hash(seed, first.Graph), "the same seed grows the same graph");
        Check(Hash(seed with { Run = 78 }, Grow(seed with { Run = 78 }).Graph) != Hash(seed, first.Graph), "another seed grows another graph");

        // Census: every accepted floor is valid and has its promises; failures and refused steps are tallied by reason.
        Dictionary<string, int> refused = new(StringComparer.Ordinal), failed = new(StringComparer.Ordinal);
        int floors = 0, succeeded = 0, places = 0;
        for (int depth = 1; depth <= 4; depth++)
            for (ulong run = 0; run < 100; run++)
            {
                MissionResult result = Grow(FloorSeed.Current(run, depth, 0));
                floors++;
                foreach (MissionRejection r in result.Rejected) Tally(refused, $"{r.Rule}: {r.Problems[0].Code}");
                if (!result.Succeeded) { Tally(failed, result.Failure[0].Code); continue; }
                succeeded++;
                places += result.Graph.Nodes.Length;
                Check(MissionValidation.Check(result.Graph, tuning.Budget).Length == 0, "an accepted graph passes validation");
                Check(new[] { MissionNodeKind.Bell, MissionNodeKind.Landmark, MissionNodeKind.Shortcut }.All(k => result.Graph.Nodes.Any(n => n.Kind == k)),
                    "every floor finishes with its bell, landmark and shortcut");
            }
        Check(succeeded >= floors * 95 / 100, $"most seeds grow a floor; {succeeded} of {floors}: {string.Join(", ", failed)}");

        // A refused rule leaves the graph exactly as it was.
        MissionGraph initial = MissionGraph.Initial();
        List<MissionRejection> rejected = [];
        FloorDraws draws = new(engine.Random, seed);
        MissionGraph after = MissionGenerator.Apply(initial, MissionRule.DetourLoop, draws, "x", tuning.Budget, rejected);
        after = MissionGenerator.Apply(after, MissionRule.Bell, draws, "y", tuning.Budget with { Nodes = 3 }, rejected);
        Check(ReferenceEquals(after, initial) && rejected is [{ Problems: [{ Code: "no_detour" }] }, { Problems: [{ Code: "node_budget" }] }],
            "a refused proposal or an invalid proposed graph leaves the graph unchanged and says why");

        static MissionGraph Graph(MissionNode[] nodes, params MissionEdge[] edges) => new([new("arrival", MissionNodeKind.Arrival), .. nodes], edges, []);
        string[] Codes(MissionGraph graph) => MissionValidation.Check(graph, tuning.Budget).Select(p => p.Code).ToArray();
        MissionGraph behindGate = Graph([new("gate", MissionNodeKind.Gate, Gates: "b"), new("objective", MissionNodeKind.Objective), new("key", MissionNodeKind.Key, "pass")],
            new MissionEdge("a", "arrival", "gate", MissionEdgeKind.Open), new MissionEdge("b", "gate", "objective", MissionEdgeKind.Locked, "pass"),
            new MissionEdge("c", "objective", "key", MissionEdgeKind.Open));
        Check(Codes(behindGate).Contains("lock_never_opened") && Codes(behindGate).Contains("objective_unreachable"), "a key behind its own gate is refused");
        MissionGraph noReturn = Graph([new("objective", MissionNodeKind.Objective)], new MissionEdge("a", "arrival", "objective", MissionEdgeKind.OneWay));
        Check(Codes(noReturn).SequenceEqual(["no_return"]), "a place with no way back is refused");
        MissionGraph latched = Graph([new("objective", MissionNodeKind.Objective), new("hall", MissionNodeKind.Landmark)],
            new MissionEdge("a", "arrival", "hall", MissionEdgeKind.Open), new MissionEdge("b", "hall", "objective", MissionEdgeKind.OneWay),
            new MissionEdge("c", "objective", "arrival", MissionEdgeKind.Latch));
        Check(Codes(latched).Length == 0, "a latch opened from beyond is a way back");
        MissionGraph bypassed = Graph([new("objective", MissionNodeKind.Objective), new("key", MissionNodeKind.Key, "pass")],
            new MissionEdge("a", "arrival", "objective", MissionEdgeKind.Locked, "pass"), new MissionEdge("b", "arrival", "objective", MissionEdgeKind.Open),
            new MissionEdge("c", "arrival", "key", MissionEdgeKind.Open));
        Check(Codes(bypassed).SequenceEqual(["lock_bypassed"]), "a lock with a way round it is refused");
        MissionGraph unprepared = Graph([new("objective", MissionNodeKind.Objective), new("hazard", MissionNodeKind.Hazard), new("stop", MissionNodeKind.Supplies)],
            new MissionEdge("a", "arrival", "hazard", MissionEdgeKind.Open), new MissionEdge("b", "hazard", "objective", MissionEdgeKind.Open),
            new MissionEdge("c", "objective", "stop", MissionEdgeKind.Open));
        Check(Codes(unprepared).SequenceEqual(["hazard_unprepared"]), "a hazard with no supplies before it is refused");

        Console.WriteLine($"Mission checks passed: {succeeded} of {floors} seeds grew valid floors averaging {places / Math.Max(1, succeeded)} places; " +
            $"failed {Format(failed)}; refused steps {Format(refused)}. Repeatable by hash, atomic refusals, key behind its gate, no way back, latch return, bypassed lock and unprepared hazard.");
    }

    private static void Tally(Dictionary<string, int> tally, string reason) => tally[reason] = tally.GetValueOrDefault(reason) + 1;
    private static string Format(Dictionary<string, int> tally) =>
        tally.Count == 0 ? "none" : string.Join(", ", tally.OrderByDescending(p => p.Value).Select(p => $"{p.Key} ×{p.Value}"));
}
