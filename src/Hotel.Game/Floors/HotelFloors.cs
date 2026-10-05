using System.Diagnostics;
using System.Security.Cryptography;
using Hotel.Game.Content;
using Hotel.Game.Floors.Layout;
using Hotel.Game.Floors.Modules;
using Hotel.Game.Scene.Kit;
using Hotel.Game.Supplies;
using Rusty.Engine;

namespace Hotel.Game.Floors;

/// <summary>
/// Which floor the player is on, the run its floors are drawn from, and every floor visited in the run with what the
/// player left it as. Depth 0 is the authored west wing with the refuge; each flight up is one generated floor deeper.
/// A floor is generated when first entered and kept: going back down and up again returns to it, and the checkpoint
/// keeps it as its resolved plan, rebuilt without drawing when the checkpoint is restored.
/// </summary>
internal sealed class HotelFloors
{
    private readonly IEngineContext engine;
    private readonly HotelContent content;
    private readonly Dictionary<int, (GeneratedFloor Floor, ExcursionDefinition Excursion)> visited = [];
    private readonly Dictionary<string, WorldMemory> memory = new(StringComparer.Ordinal);

    internal HotelFloors(IEngineContext engine, HotelContent content)
    {
        this.engine = engine;
        this.content = content;
        Tunings = FloorTunings.Load(engine, content.Modules, content.Kit, content.Fixtures, content.Supplies.Items, content.Combat.Residents);
        foreach (string surface in new[] { Tunings.Content.Doors.Material, Tunings.Content.Doors.HandleMaterial })
            Authored.Require(content.Surfaces.Any(s => s.Id == surface), Floors.Content.ContentTuning.Path, "doors", $"unknown surface '{surface}'.");
        Authored.Require(Tunings.Content.Spirit == content.Spirit.Id, Floors.Content.ContentTuning.Path, "spirit",
            $"generated floors ring the bell of the one pact, '{content.Spirit.Id}'.");
        Sources = new(content.Modules, content.Kit, content.Fixtures, content.Supplies.Items, content.Combat.Residents, content.Player.Controller(engine.Spatial));
    }

    internal FloorTunings Tunings { get; }
    internal FloorSources Sources { get; }
    internal ulong RunSeed { get; private set; }
    internal int Depth { get; private set; }
    internal GeneratedFloor? Current => visited.TryGetValue(Depth, out var floor) ? floor.Floor : null;
    internal string[] LastRefusals { get; private set; } = [];
    internal double LastMilliseconds { get; private set; }
    /// <summary>Finds collected this run on floors other than the one the player stands on.</summary>
    internal HashSet<string> Collected { get; } = new(StringComparer.Ordinal);

    /// <summary>Begins a run from a seed, forgetting every floor of the last one.</summary>
    internal void Begin(ulong seed)
    {
        RunSeed = seed;
        visited.Clear();
        foreach (string id in memory.Keys.Where(id => id != content.Excursion.Id).ToArray()) memory.Remove(id);
        Collected.RemoveWhere(id => !content.Excursion.Placements.Finds.Any(f => f.Id == id));
        Depth = 0;
    }

    /// <summary>A new game's run: a fresh seed.</summary>
    internal void BeginNew() => Begin(BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(sizeof(ulong))));

    /// <summary>The floor at a depth as an excursion, generating it on first entry; null when generation failed.</summary>
    internal ExcursionDefinition? Floor(int depth)
    {
        if (visited.TryGetValue(depth, out var known)) return known.Excursion;
        long started = Stopwatch.GetTimestamp();
        GenerationResult result = FloorGenerator.Generate(engine, FloorSeed.Current(RunSeed, depth, 0), Tunings, Sources);
        LastMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        LastRefusals = result.Refusals;
        if (result.Floor is not { } floor) return null;
        ExcursionDefinition excursion = FloorExcursion.From(floor, Tunings, Sources, content.Player);
        visited[depth] = (floor, excursion);
        return excursion;
    }

    internal void Arrive(int depth) => Depth = depth;

    internal void Remember(string excursion, WorldMemory left) => memory[excursion] = left;
    internal WorldMemory? Memory(string excursion) => memory.GetValueOrDefault(excursion);

    /// <summary>The run as the checkpoint keeps it.</summary>
    internal FloorsState Capture() => new(RunSeed, visited.OrderBy(v => v.Key).Select(v =>
    {
        GeneratedFloor f = v.Value.Floor;
        return new FloorRecord(f.Identity.Seed.Version, f.Identity.Seed.Depth, f.Identity.Seed.Shift, f.Identity.PlanHash, f.Candidate, f.Attempt,
            f.Graph, f.Layout, f.Content, memory.GetValueOrDefault(v.Value.Excursion.Id));
    }).ToArray(), Collected.Where(id => !content.Excursion.Placements.Finds.Any(f => f.Id == id)).Order(StringComparer.Ordinal).ToArray());

    /// <summary>
    /// Proves a stored run rebuilds: each floor was made by this generator version, its plan still hashes to its
    /// identity, and it still realizes and builds. Throws without changing anything.
    /// </summary>
    internal void Validate(FloorsState state) => _ = Rebuild(state);

    /// <summary>Takes a validated stored run as the current one.</summary>
    internal void Restore(FloorsState state)
    {
        var rebuilt = Rebuild(state);
        Begin(state.Run);
        foreach (var (depth, floor, excursion, left) in rebuilt)
        {
            visited[depth] = (floor, excursion);
            if (left is not null) memory[excursion.Id] = left;
        }
        Collected.UnionWith(state.Collected);
    }

    private List<(int Depth, GeneratedFloor Floor, ExcursionDefinition Excursion, WorldMemory? Memory)> Rebuild(FloorsState state)
    {
        if (state.Floors is null || state.Collected is null) throw new InvalidOperationException("Checkpoint floors are incomplete.");
        List<(int, GeneratedFloor, ExcursionDefinition, WorldMemory?)> rebuilt = [];
        foreach (FloorRecord r in state.Floors)
        {
            if (r.Version != FloorSeed.CurrentVersion)
                throw new InvalidOperationException($"Floor {r.Depth} was generated by version {r.Version}; this is version {FloorSeed.CurrentVersion}.");
            if (r.Depth < 1 || rebuilt.Any(b => b.Item1 == r.Depth) || r.Graph is null || r.Layout is null || r.Content is null)
                throw new InvalidOperationException($"Checkpoint floor {r.Depth} is invalid.");
            FloorSeed seed = new(r.Version, state.Run, r.Depth, r.Shift);
            FloorIdentity identity = FloorGenerator.Identity(seed, r.Candidate, r.Attempt, r.Graph, r.Layout, r.Content);
            if (identity.PlanHash != r.PlanHash) throw new InvalidOperationException($"Checkpoint floor {r.Depth}'s plan does not match its identity.");
            FloorPlan plan = r.Layout.Realize(Sources.Modules);
            BuiltFloor built = KitBuilder.Build(plan, $"checkpoint floor {r.Depth}", Sources.Kit, Sources.Fixtures);
            GeneratedFloor floor = new(identity, r.Graph, r.Layout, r.Content, plan, built, null, r.Candidate, r.Attempt);
            rebuilt.Add((r.Depth, floor, FloorExcursion.From(floor, Tunings, Sources, content.Player), r.Memory));
        }
        if (state.Collected.Any(id => !rebuilt.Any(b => b.Item3.Placements.Finds.Any(f => f.Id == id))))
            throw new InvalidOperationException("Checkpoint collects a find on no visited floor.");
        return rebuilt;
    }

    /// <summary>The expedition finds a stored run collected on its generated floors, read from its plans alone.</summary>
    internal static IEnumerable<string> ExpeditionFinds(FloorsState run, HotelSupplies supplies) => run.Floors
        .SelectMany(r => r.Content.Finds.Where(f => f.Item is { } item && supplies.Item(item).Kind == SupplyKind.Expedition)
            .Select(f => $"{FloorExcursion.Id(r.Depth)}/{f.Id}"))
        .Where(run.Collected.Contains);

    /// <summary>The item a find on any visited floor holds, or null.</summary>
    internal ItemDefinition? FindItem(string id) => visited.Values.SelectMany(v => v.Excursion.Placements.Finds).FirstOrDefault(f => f.Id == id) is { } find
        ? content.Supplies.Items.First(i => i.Id == find.Item) : null;
}
