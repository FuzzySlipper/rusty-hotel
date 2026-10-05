using System.Globalization;
using Rusty.Engine;

namespace Hotel.Game.Floors;

/// <summary>The generator's stages. Each draws only under its own scope.</summary>
internal enum FloorStage { Graph, Embedding, Content }

/// <summary>
/// The only source of randomness a floor generator may use: Engine keyed draws. A draw is a pure function of the
/// floor's seed, its stage and purpose, and a key made of stable ids, so it holds no stream position and two stages
/// never disturb each other's results. Stages resolve their draws into value records; builders never draw.
/// </summary>
/// <remarks>Adapted from CraftSurvive's terrain generator contract; see docs/reuse.md.</remarks>
internal sealed class FloorDraws(IRandomService random, FloorSeed seed, int candidate = 0, int attempt = 0)
{
    // Spreads the version across every bit of the seed, so neighbouring versions share no draws.
    private const ulong VersionSpread = 0x9e3779b97f4a7c15UL;

    // The width of a one-in-N draw: fine enough that N up to a million is exact.
    private const long UnitScale = 1_000_000;

    internal FloorSeed Seed { get; } = seed;

    /// <summary>
    /// Which retry this is. A new candidate redraws the whole floor; a new attempt keeps the candidate's mission graph
    /// and redraws its layout and content.
    /// </summary>
    internal int Candidate { get; } = candidate;
    internal int Attempt { get; } = attempt;

    internal FloorDraws Retry(int nextCandidate, int nextAttempt) => new(random, Seed, nextCandidate, nextAttempt);

    /// <summary>
    /// A draw in [<paramref name="minimum"/>, <paramref name="maximum"/>], scoped <c>hotel.floor.stage.purpose</c>.
    /// The depth and shift lead the key, so each floor and each of its shifts draws afresh from the same run seed; the
    /// candidate follows, and for stages after the graph, the attempt.
    /// </summary>
    internal long Long(FloorStage stage, string purpose, string key, long minimum, long maximum)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (maximum < minimum) throw new ArgumentOutOfRangeException(nameof(maximum), maximum, "A draw range must not be inverted.");
        string scope = $"hotel.floor.{stage.ToString().ToLowerInvariant()}.{purpose}";
        string floorKey = stage == FloorStage.Graph
            ? string.Create(CultureInfo.InvariantCulture, $"{Seed.Depth}/{Seed.Shift}/c{Candidate}/{key}")
            : string.Create(CultureInfo.InvariantCulture, $"{Seed.Depth}/{Seed.Shift}/c{Candidate}/a{Attempt}/{key}");
        return random.DrawKeyed(new KeyedRngRequest(Seed.Run ^ (Seed.Version * VersionSpread), scope, floorKey, minimum, maximum)).Value;
    }

    /// <summary>An index into a list of <paramref name="count"/> choices.</summary>
    internal int Index(FloorStage stage, string purpose, string key, int count)
    {
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count), count, "A choice needs at least one option.");
        return (int)Long(stage, purpose, key, 0, count - 1);
    }

    /// <summary>
    /// True one time in <paramref name="oneIn"/>. It compares against a fraction of a wide range instead of testing a
    /// narrow draw for one value, whose uniformity would rest on the low bits of the source.
    /// </summary>
    internal bool OneIn(FloorStage stage, string purpose, string key, long oneIn)
    {
        if (oneIn < 1) throw new ArgumentOutOfRangeException(nameof(oneIn), oneIn, "A one-in-N draw needs N of at least one.");
        return Long(stage, purpose, key, 0, UnitScale - 1) < UnitScale / oneIn;
    }

    /// <summary>An index chosen in proportion to non-negative integer <paramref name="weights"/>, at least one positive.</summary>
    internal int Weighted(FloorStage stage, string purpose, string key, IReadOnlyList<int> weights)
    {
        long total = 0;
        foreach (int weight in weights)
        {
            if (weight < 0) throw new ArgumentOutOfRangeException(nameof(weights), weight, "Weights must not be negative.");
            total += weight;
        }
        if (total < 1) throw new ArgumentException("At least one weight must be positive.", nameof(weights));
        long pick = Long(stage, purpose, key, 0, total - 1);
        for (int i = 0; i < weights.Count; i++)
            if ((pick -= weights[i]) < 0) return i;
        throw new InvalidOperationException("Unreachable: the pick lies within the total weight.");
    }
}
