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
    private readonly HashSet<int> due = [];
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
    private readonly List<SecuredFind> secured = [];
    internal IReadOnlyList<SecuredFind> Secured => secured;

    /// <summary>Begins a run from a seed, forgetting every floor of the last one.</summary>
    internal void Begin(ulong seed)
    {
        RunSeed = seed;
        visited.Clear();
        due.Clear();
        foreach (string id in memory.Keys.Where(id => id != content.Excursion.Id).ToArray()) memory.Remove(id);
        Collected.RemoveWhere(id => !content.Excursion.Placements.Finds.Any(f => f.Id == id));
        secured.Clear();
        Depth = 0;
    }

    /// <summary>A new game's run: a fresh seed.</summary>
    internal void BeginNew() => Begin(BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(sizeof(ulong))));

    /// <summary>
    /// The floor at a depth as an excursion: generated on first entry, and shifted on the first entry after a refuge
    /// return; null when a new floor could not be generated.
    /// </summary>
    internal ExcursionDefinition? Floor(int depth)
    {
        if (due.Remove(depth)) Shift(depth);
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

    /// <summary>The floor at a depth as it stands now, without generating or shifting it; null when not visited.</summary>
    internal (GeneratedFloor Floor, ExcursionDefinition Excursion)? Stored(int depth) => visited.TryGetValue(depth, out var floor) ? floor : null;
    internal bool ShiftDue(int depth) => due.Contains(depth);

    internal void Remember(string excursion, WorldMemory left) => memory[excursion] = left;
    internal WorldMemory? Memory(string excursion) => memory.GetValueOrDefault(excursion);

    /// <summary>The run as the checkpoint keeps it. Expedition finds collected on generated floors count as secured.</summary>
    internal FloorsState Capture()
    {
        string[] collected = Collected.Where(id => !content.Excursion.Placements.Finds.Any(f => f.Id == id)).Order(StringComparer.Ordinal).ToArray();
        SecuredFind[] newlySecured = collected.Where(id => secured.All(s => s.Id != id))
            .Select(id => (id, FindItem(id))).Where(f => f.Item2?.Kind == SupplyKind.Expedition).Select(f => new SecuredFind(f.id, f.Item2!.Id)).ToArray();
        return new(RunSeed, visited.OrderBy(v => v.Key).Select(v => Record(v.Value.Floor, memory.GetValueOrDefault(v.Value.Excursion.Id)) with
            { ShiftDue = due.Contains(v.Key) }).ToArray(), collected, [.. secured, .. newlySecured]);
    }

    private static FloorRecord Record(GeneratedFloor f, WorldMemory? left) => new(f.Identity.Seed.Version, f.Identity.Seed.Depth, f.Identity.Seed.Shift,
        f.Identity.PlanHash, f.Candidate, f.Attempt, f.Graph, f.Layout, f.Content, left);

    /// <summary>
    /// Shifts a floor once around what it keeps: the stair core and landmark always stay, and the shortcut stays once
    /// the player has unlatched it. Rooms that re-roll forget what was collected in them, and the floor's doors,
    /// residents and keys start fresh; secured finds stay secured. A floor that cannot shift stays as it was.
    /// </summary>
    private void Shift(int depth)
    {
        var (floor, excursion) = visited[depth];
        WorldMemory? left = memory.GetValueOrDefault(excursion.Id);
        bool latchOpened = left?.OpenDoors.Contains($"{excursion.Id}/latch") == true;
        Layout.KeptSet kept = Layout.KeptSet.From(floor, latchOpened);
        long started = Stopwatch.GetTimestamp();
        FloorSeed seed = floor.Identity.Seed with { Shift = floor.Identity.Seed.Shift + 1 };
        GenerationResult result = FloorGenerator.Generate(engine, seed, Tunings, Sources, kept);
        LastMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        LastRefusals = result.Refusals;
        if (result.Floor is not { } next) return;
        HashSet<string> stays = kept.Placements.Select(p => $"{excursion.Id}/{p.Id}/").ToHashSet(StringComparer.Ordinal);
        Collected.RemoveWhere(id => id.StartsWith(excursion.Id + "/", StringComparison.Ordinal) && !stays.Any(id.StartsWith));
        memory.Remove(excursion.Id);
        if (latchOpened && kept.Latch is not null) memory[excursion.Id] = new([$"{excursion.Id}/latch"], [], []);
        visited[depth] = (next, FloorExcursion.From(next, Tunings, Sources, content.Player));
    }

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
        foreach (FloorRecord r in state.Floors.Where(f => f.ShiftDue)) due.Add(r.Depth);
        Collected.UnionWith(state.Collected);
        secured.AddRange(state.Secured);
    }

    private List<(int Depth, GeneratedFloor Floor, ExcursionDefinition Excursion, WorldMemory? Memory)> Rebuild(FloorsState state)
    {
        if (state.Floors is null || state.Collected is null || state.Secured is null) throw new InvalidOperationException("Checkpoint floors are incomplete.");
        if (state.Secured.Any(s => content.Supplies.Items.FirstOrDefault(i => i.Id == s.Item)?.Kind != SupplyKind.Expedition) ||
            state.Secured.Select(s => s.Id).Distinct().Count() != state.Secured.Length)
            throw new InvalidOperationException("Checkpoint secures an unknown expedition find.");
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

    /// <summary>The expedition finds a stored run has secured from its generated floors.</summary>
    internal static IEnumerable<string> ExpeditionFinds(FloorsState run) => run.Secured.Select(s => s.Id);

    /// <summary>The item a find on any visited floor holds, or that a secured find held; null when there is none.</summary>
    internal ItemDefinition? FindItem(string id) =>
        (visited.Values.SelectMany(v => v.Excursion.Placements.Finds).FirstOrDefault(f => f.Id == id)?.Item ?? secured.FirstOrDefault(s => s.Id == id)?.Item)
            is { } item ? content.Supplies.Items.First(i => i.Id == item) : null;
}
