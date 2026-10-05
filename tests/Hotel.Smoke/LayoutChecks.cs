using Hotel.Game.Floors;
using Hotel.Game.Floors.Layout;
using Hotel.Game.Floors.Mission;
using Rusty.Engine;

// Mission graphs lay out as hotel modules: repeatable by hash, most seeds embed with failures tallied by reason,
// every lock cuts what it guards, and a graph that cannot fit is refused with a reason.
internal static class LayoutChecks
{
    internal static void Run(IEngineContext engine)
    {
        void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
        var content = Owners.Content(engine);
        MissionTuning mission = MissionTuning.Load(engine);
        LayoutTuning tuning = LayoutTuning.Load(engine, content.Modules, content.Kit);
        FloorLayouts.Laid Lay(FloorSeed seed, LayoutTuning? with = null)
        {
            FloorDraws draws = new(engine.Random, seed);
            MissionResult graph = MissionGenerator.Generate(mission, draws);
            return FloorLayouts.Lay(graph.Graph, content.Modules, with ?? tuning, content.Kit, content.Fixtures, draws);
        }
        string Hash(FloorSeed seed, FloorLayout layout) { CanonicalText text = new(); layout.Write(text); return FloorIdentity.Of(seed, text).PlanHash; }

        FloorSeed seed = FloorSeed.Current(run: 5, depth: 1, shift: 0);
        FloorLayouts.Laid first = Lay(seed);
        Check(first.Failure is null, "a floor lays out: " + first.Failure);
        Check(Hash(seed, Lay(seed).Layout!) == Hash(seed, first.Layout!), "the same seed lays out the same floor");

        Dictionary<string, int> failures = new(StringComparer.Ordinal);
        int floors = 0, laid = 0, rooms = 0, minRooms = int.MaxValue, maxRooms = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (int depth = 1; depth <= 3; depth++)
            for (ulong run = 0; run < 40; run++)
            {
                floors++;
                FloorLayouts.Laid result = Lay(FloorSeed.Current(run, depth, 0));
                if (result.Failure is { } failure) { string reason = failure.Split(':')[0]; failures[reason] = failures.GetValueOrDefault(reason) + 1; continue; }
                laid++;
                if (run < 4) FloorSvg.Write($"floor-d{depth}-r{run}", result.Layout!, result.Plan!);
                int count = result.Layout!.Placements.Count(p => !content.Modules.Find(p.Module)!.Tags.Any(t => t is Hotel.Game.Floors.Modules.ModuleTag.Corridor or Hotel.Game.Floors.Modules.ModuleTag.StairCore));
                rooms += count; minRooms = Math.Min(minRooms, count); maxRooms = Math.Max(maxRooms, count);
            }
        Check(laid >= floors * 3 / 4, $"most seeds lay out; {laid} of {floors}: {string.Join(", ", failures)}");

        // The check itself refuses a layout whose lock or latch mapping is missing, and a latch that opens the wrong way.
        MissionGraph graph = MissionGenerator.Generate(mission, new FloorDraws(engine.Random, seed)).Graph;
        FloorLayout layout = first.Layout!;
        Check(layout.Locks.Length == graph.Edges.Count(e => e.Kind == MissionEdgeKind.Locked) && layout.Latch is not null,
            "every locked edge and the latch map to doors");
        Check(LayoutCheck.Check(layout with { Locks = layout.Locks[1..] }, first.Plan!, graph) is { } noLock && noLock.StartsWith("lock: locked edge", StringComparison.Ordinal),
            "a locked edge without a door is refused");
        Check(LayoutCheck.Check(layout with { Latch = null }, first.Plan!, graph) is { } noLatch && noLatch.StartsWith("latch:", StringComparison.Ordinal),
            "a shortcut without its latch door is refused");
        // Reversed, the latch would open from the stairs and let the arrival walk round a lock to the shortcut's side.
        (FloorSeed Seed, FloorLayouts.Laid Laid, MissionGraph Graph)? behindLock = null;
        for (ulong run = 0; run < 40 && behindLock is null; run++)
        {
            FloorSeed s = FloorSeed.Current(run, 2, 0);
            MissionGraph g = MissionGenerator.Generate(mission, new FloorDraws(engine.Random, s)).Graph;
            FloorLayouts.Laid l = Lay(s);
            string shortcut = g.Nodes.First(n => n.Kind == MissionNodeKind.Shortcut).Id;
            if (l.Failure is null && FloorEmbedding.Regions(g)[shortcut] != FloorEmbedding.OpenRegion) behindLock = (s, l, g);
        }
        Check(behindLock is not null, "some seed puts the shortcut behind a lock");
        var (_, locked, lockedGraph) = behindLock!.Value;
        Hotel.Game.Scene.Kit.FloorPlan reversed = locked.Plan! with
        {
            Links = locked.Plan!.Links.Select(l => l.Id == locked.Layout!.Latch ? l with { Between = [l.Between[1], l.Between[0]] } : l).ToArray()
        };
        Check(LayoutCheck.Check(locked.Layout!, reversed, lockedGraph) is { } wrongWay && wrongWay.StartsWith("latch:", StringComparison.Ordinal),
            "a latch that opens from the stairs is refused");
        string? wrongDoor = LayoutCheck.Check(locked.Layout! with { Latch = "service/0~1" }, locked.Plan!, lockedGraph);
        // The stairs' ordinary corridor archway is a door into the stairs too, but not the passage's: refused.
        string arrivalSpace = locked.Plan!.Spaces.First(s => s.Id.StartsWith(locked.Layout!.Places[MissionGraph.ArrivalId] + "/", StringComparison.Ordinal)).Id;
        string corridorDoor = locked.Plan.Links.First(l => l.Between[0] == arrivalSpace && l.Id != locked.Layout!.Latch).Id;
        string? corridorLatch = LayoutCheck.Check(locked.Layout! with { Latch = corridorDoor }, locked.Plan, lockedGraph);
        Check(corridorLatch?.StartsWith("latch:", StringComparison.Ordinal) == true, "a latch on the stairs' corridor archway is refused: " + corridorLatch);
        Check(wrongDoor?.StartsWith("latch:", StringComparison.Ordinal) == true, "a latch on a door that does not lead into the stairs is refused: " + wrongDoor);

        FloorLayouts.Laid cramped = Lay(seed, tuning with { Extent = 6 });
        Check(cramped.Failure is { } why && why.StartsWith("rooms:", StringComparison.Ordinal), "a graph that cannot fit is refused with a reason: " + cramped.Failure);
        Console.WriteLine($"Layout checks passed: {laid} of {floors} seeds laid out ({rooms / Math.Max(1, laid)} rooms on average, {minRooms}–{maxRooms}) in " +
            $"{clock.ElapsedMilliseconds / Math.Max(1, floors)} ms each; failures {(failures.Count == 0 ? "none" : string.Join(", ", failures.Select(f => $"{f.Key} ×{f.Value}")))}; repeatable by hash; cramped floor refused ({cramped.Failure}).");
    }
}
