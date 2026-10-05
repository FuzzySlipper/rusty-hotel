using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Hotel.Game.Content;
using Rusty.Engine;
using Rusty.Engine.Persistence;

namespace Hotel.Game.Floors;

/// <summary>The run every generated floor is drawn from: one seed per new game, kept until the run ends.</summary>
internal sealed record RunState(int Version, ulong Seed);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true)]
[JsonSerializable(typeof(RunState))]
internal sealed partial class RunJson : JsonSerializerContext;

/// <summary>
/// Which floor the player is on and the run its floors are drawn from. Depth 0 is the authored west wing with the
/// refuge; each flight up is one generated floor deeper. A floor is generated when first entered in a run and kept for
/// the rest of the session, so going back down and up again returns to the same floor.
/// </summary>
internal sealed class HotelFloors : IDisposable
{
    internal const string Scope = "hotel.run";
    internal const string Key = "run/current";
    private readonly IEngineContext engine;
    private readonly HotelContent content;
    private readonly ProductStateStore<RunState> store;
    private readonly Dictionary<int, (GeneratedFloor Floor, ExcursionDefinition Excursion)> visited = [];

    internal HotelFloors(IEngineContext engine, HotelContent content)
    {
        this.engine = engine;
        this.content = content;
        Tunings = FloorTunings.Load(engine, content.Modules, content.Kit, content.Fixtures, content.Supplies.Items, content.Combat.Residents);
        Sources = new(content.Modules, content.Kit, content.Fixtures, content.Supplies.Items, content.Combat.Residents, content.Player.Controller(engine.Spatial));
        store = new(engine, Scope, new JsonProductStateCodec<RunState>(RunJson.Default.RunState));
    }

    internal FloorTunings Tunings { get; }
    internal FloorSources Sources { get; }
    internal ulong RunSeed { get; private set; }
    internal int Depth { get; private set; }
    internal GeneratedFloor? Current => visited.TryGetValue(Depth, out var floor) ? floor.Floor : null;
    internal string[] LastRefusals { get; private set; } = [];
    internal double LastMilliseconds { get; private set; }

    /// <summary>Continues the stored run, or begins a new one with a fresh seed.</summary>
    internal void Start()
    {
        ProductStateLoad<RunState> loaded = store.Load(Key);
        if (loaded.Present && loaded.State is { Version: 1 } run) { RunSeed = run.Seed; return; }
        if (loaded.Present) throw new InvalidOperationException("The stored run is invalid; it has not been replaced.");
        Begin(BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(sizeof(ulong))));
    }

    /// <summary>Begins a run from a given seed, forgetting every floor generated so far.</summary>
    internal void Begin(ulong seed)
    {
        PersistenceSaveReceipt saved = store.Save(Key, new(1, seed));
        if (saved.Outcome != PersistenceSaveOutcome.Saved) throw new InvalidOperationException("Engine refused the run write: " + saved.Outcome);
        RunSeed = seed;
        visited.Clear();
        Depth = 0;
    }

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

    public void Dispose() => store.Dispose();
}
