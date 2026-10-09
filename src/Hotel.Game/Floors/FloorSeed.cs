namespace Hotel.Game.Floors;

/// <summary>
/// What a generated floor is drawn from: the generator version, the run's seed, how deep the floor is, and which
/// shift of that floor this is. A shift is the same floor regenerated between visits around what it keeps.
/// </summary>
internal sealed record FloorSeed(uint Version, ulong Run, int Depth, int Shift)
{
    /// <summary>
    /// The generator version new floors are drawn with. A change to what any stage draws or how it resolves those
    /// draws is a version bump, which redraws every floor deliberately instead of silently reinterpreting a save.
    /// </summary>
    internal const uint CurrentVersion = 23;

    internal static FloorSeed Current(ulong run, int depth, int shift) => new(CurrentVersion, run, depth, shift);
}

/// <summary>A generated floor's identity: its seed and the canonical hash of the plan it resolved to.</summary>
internal sealed record FloorIdentity(FloorSeed Seed, string PlanHash)
{
    /// <summary>The identity of a resolved plan, whose canonical text the stages wrote into <paramref name="plan"/>.</summary>
    internal static FloorIdentity Of(FloorSeed seed, CanonicalText plan) => new(seed, plan.Hash(seed));
}
