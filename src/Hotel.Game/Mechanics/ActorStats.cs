using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;

namespace Hotel.Game.Mechanics;

/// <summary>
/// One actor's live stats: an Engine <see cref="StatsComponent"/> built from the vocabulary and the actor's block.
/// Attributes are base stats; each derived stat carries one intrinsic Engine source holding its attributes'
/// contributions, so <see cref="Explain"/> names where a value came from; tracks share their derived maximum; and a
/// resistance stat per damage kind decides how much of a hit lands. The actor's <see cref="Effects"/> add their own
/// Engine sources beside those, and wards among them take their share of a hit first.
/// </summary>
internal sealed class ActorStats
{
    // Resource pools count in whole points.
    private const double WholePoints = 1;
    /// <summary>The track hits land on, and the derived stat movement is scaled by, by vocabulary id.</summary>
    internal const string HealthTrack = "health", PaceStat = "pace";
    private readonly MechanicsDefinition mechanics;
    private readonly ActorStatBlock block;
    private readonly EntityId? owner;

    internal ActorStats(MechanicsDefinition mechanics, ActorStatBlock block, EntityId? owner)
    {
        this.mechanics = mechanics;
        this.block = block;
        this.owner = owner;
        foreach (AttributeDefinition a in mechanics.Attributes)
            Stats.AddStat(StatOf(a.Id), new Stat(block.Attributes[a.Id], a.Minimum, a.Maximum));
        foreach (DerivedStatDefinition d in mechanics.Derived)
            Stats.AddStat(StatOf(d.Id), new Stat(BaseOf(d), d.Minimum, d.Maximum, d.Quantum));
        foreach (DamageKindDefinition k in mechanics.DamageKinds)
            Stats.AddStat(ResistanceOf(k.Id), new Stat(block.Resistances.GetValueOrDefault(k.Id), k.MinimumResistance, k.MaximumResistance));
        RefreshDerived();
        foreach (TrackDefinition t in mechanics.Tracks)
        {
            Stat maximum = Stats.GetStat(StatOf(t.Maximum));
            Stats.AddTrack(TrackOf(t.Id), new Track(maximum, InitialOf(t, maximum), quantum: WholePoints));
        }
        Effects = new(mechanics, this, owner);
    }

    internal StatsComponent Stats { get; } = new();
    internal ActorEffects Effects { get; }
    internal MechanicsDefinition Mechanics => mechanics;
    /// <summary>The factor this actor's movement is scaled by.</summary>
    internal float Pace => (float)Stat(PaceStat).Value;

    internal Stat Stat(string id) => Stats.GetStat(StatOf(id));
    internal Track Track(string id) => Stats.GetTrack(TrackOf(id));
    internal double Resistance(string kind) => Stats.GetStat(ResistanceOf(kind)).Value;
    internal StatEvaluation Explain(string id) => Stat(id).Explain();

    // Regeneration owed but not yet a whole point, per track; a fraction of a point, not saved.
    private readonly Dictionary<string, float> owed = new(StringComparer.Ordinal);

    /// <summary>Recovers each regenerating track by admitted seconds, a whole point at a time.</summary>
    internal void Regenerate(float seconds)
    {
        foreach (TrackDefinition t in mechanics.Tracks)
        {
            if (t.Regeneration <= 0) continue;
            Track track = Track(t.Id);
            if (track.Value >= track.MaximumValue) { owed.Remove(t.Id); continue; }
            float due = owed.GetValueOrDefault(t.Id) + t.Regeneration * seconds;
            int whole = (int)due;
            if (whole > 0) track.Restore(whole);
            owed[t.Id] = due - whole;
        }
    }

    /// <summary>Lands damage on a track after this actor's resistance to its kind; returns what was taken.</summary>
    internal int TakeDamage(DamagePacket packet, string track) => Apply(Resist(packet), packet.Kind, track);

    /// <summary>What a packet comes to after this actor's resistance to its kind.</summary>
    internal float Resist(DamagePacket packet) => Math.Max(0, packet.Amount * (float)(1 - Resistance(packet.Kind)));

    /// <summary>
    /// Applies damage already resisted: wards against its kind take their share, then the track, never below empty. When it
    /// would take the last point, <paramref name="defeating"/> may name health to keep. Returns what was taken.
    /// </summary>
    internal int Apply(float amount, string kind, string track, Func<int>? defeating = null)
    {
        int landed = (int)Math.Round(amount, MidpointRounding.AwayFromZero);
        if (landed <= 0) return 0;
        Track health = Track(track);
        landed = Effects.Absorb(kind, landed);
        int applied = Math.Clamp(landed, 0, health.ValueInt);
        if (applied > 0 && applied >= health.ValueInt && defeating?.Invoke() is int keep and > 0)
            applied = Math.Max(0, health.ValueInt - keep);
        health.Spend(applied);
        return applied;
    }

    /// <summary>
    /// Gives every stat the effect sources aimed at it, attributes first, then recomputes the derived stats from the
    /// attributes as they now stand.
    /// </summary>
    internal void ApplyEffectSources()
    {
        // Equipment, growth and effect sources are evaluated together; each is rebuilt by its owner, never saved.
        foreach (AttributeDefinition a in mechanics.Attributes) Stats.GetStat(StatOf(a.Id)).SetSources(StatOf(a.Id), EffectSources(a.Id));
        foreach (DamageKindDefinition k in mechanics.DamageKinds)
            Stats.GetStat(ResistanceOf(k.Id)).SetSources(ResistanceOf(k.Id), EffectSources(ResistanceStat(k.Id)));
        RefreshDerived();
    }

    /// <summary>Recomputes each derived stat's attribute source from the attributes as they stand, beside its effect sources.</summary>
    internal void RefreshDerived()
    {
        foreach (DerivedStatDefinition d in mechanics.Derived)
        {
            StatContributionDefinition[] contributions = d.From.Select(f => new StatContributionDefinition(StatOf(d.Id),
                StackingGroupId.Parse($"{d.Id}.{f.Attribute}"), MechanicsStackingPolicy.Sum,
                new StatContribution.Add(Stats.GetStat(StatOf(f.Attribute)).Value * f.PerPoint))).ToArray();
            StatSource[] derived = contributions.Length == 0 ? [] :
                [new StatSource(new IntrinsicSourceIdentity(owner, SourceInstanceId.Parse($"derived.{d.Id}")),
                    SourceDefinitionId.Parse($"derived.{d.Id}"), 0, contributions)];
            Stats.GetStat(StatOf(d.Id)).SetSources(StatOf(d.Id), [.. derived, .. EffectSources(d.Id)]);
        }
    }

    private IEnumerable<StatSource> EffectSources(string stat) =>
        (Effects?.Sources ?? []).Concat(equipment).Concat(growth).Where(s => s.Contributions.Any(c => c.Stat.Value == stat));

    private StatSource[] equipment = [], growth = [];

    /// <summary>Replaces the permanent sources the investigator has grown (see <see cref="Progression.InvestigatorGrowth"/>) and re-evaluates.</summary>
    internal void SetGrowthSources(StatSource[] sources)
    {
        growth = sources;
        ApplyEffectSources();
    }

    /// <summary>Replaces the sources worn equipment holds (see <see cref="Supplies.FieldCase.Sources"/>) and re-evaluates.</summary>
    internal void SetEquipmentSources(StatSource[] sources)
    {
        equipment = sources;
        ApplyEffectSources();
    }

    /// <summary>The block's starting values: bases as authored and tracks at their initial points.</summary>
    internal void Reset()
    {
        Effects.Clear();
        foreach (AttributeDefinition a in mechanics.Attributes) Stats.GetStat(StatOf(a.Id)).BaseValue = block.Attributes[a.Id];
        foreach (DerivedStatDefinition d in mechanics.Derived) Stats.GetStat(StatOf(d.Id)).BaseValue = BaseOf(d);
        RefreshDerived();
        foreach (TrackDefinition t in mechanics.Tracks)
        {
            Track track = Track(t.Id);
            track.SetCurrent(InitialOf(t, track.Maximum), true);
        }
    }

    /// <summary>Stat bases and track currents, read through the Engine capture so the saved values are the live ones.</summary>
    internal ActorStatsState Capture()
    {
        StatsComponentSnapshot snapshot = StatsComponentCapture.Capture(Stats);
        return new(snapshot.Stats.ToDictionary(s => s.Id, s => s.BaseValue, StringComparer.Ordinal),
            snapshot.Tracks.ToDictionary(t => t.Id, t => t.Current, StringComparer.Ordinal), Effects.Capture());
    }

    /// <summary>Refuses a saved state that does not name exactly this vocabulary's stats and tracks within their bounds.</summary>
    /// <param name="worn">The equipment sources the state would be restored beside, for track maximums.</param>
    /// <param name="grown">The growth sources it would be restored beside, likewise.</param>
    internal void Validate(ActorStatsState state, StatSource[]? worn = null, StatSource[]? grown = null)
    {
        if (state.Bases is null || state.Tracks is null || state.Effects is null || state.Bases.Count != Stats.Stats.Count || state.Tracks.Count != Stats.Tracks.Count)
            throw new InvalidOperationException("Checkpoint stats do not match the stat vocabulary.");
        foreach (var (id, value) in state.Bases)
            if (!StatId.TryParse(id, out StatId? stat) || stat is null || !Stats.TryGetStat(stat, out Stat? live) || !double.IsFinite(value) ||
                value < live!.Minimum || value > live.Maximum)
                throw new InvalidOperationException($"Checkpoint stat '{id}' is unknown or out of bounds.");
        foreach (var (id, value) in state.Tracks)
            if (!TrackId.TryParse(id, out TrackId? track) || track is null || !Stats.TryGetTrack(track, out Track? live) || !double.IsFinite(value) ||
                value < live!.Minimum)
                throw new InvalidOperationException($"Checkpoint track '{id}' is unknown or out of bounds.");
        ActorEffects.Validate(mechanics, state.Effects);
        // A track's maximum follows the restored bases and effects, and effects may conflict in their groups, so these
        // are checked on a scratch copy, never the live stats.
        ActorStats scratch = new(mechanics, block, owner);
        scratch.SetEquipmentSources(worn ?? equipment);
        scratch.SetGrowthSources(grown ?? growth);
        scratch.Restore(state);
    }

    /// <summary>
    /// Restores a validated state: bases first, then the derived sources they feed, then the effects and their sources,
    /// then track currents under the maximums those decide.
    /// </summary>
    internal void Restore(ActorStatsState state)
    {
        foreach (var (id, value) in state.Bases) Stats.GetStat(StatId.Parse(id)).BaseValue = value;
        Effects.Restore(mechanics, state.Effects);
        foreach (var (id, value) in state.Tracks)
        {
            Track track = Stats.GetTrack(TrackId.Parse(id));
            if (value > track.MaximumValue) throw new InvalidOperationException($"Checkpoint track '{id}' exceeds its maximum.");
            track.SetCurrent(value, false);
        }
    }

    private float BaseOf(DerivedStatDefinition d) => block.Bases.TryGetValue(d.Id, out float authored) ? authored : d.Base;

    private double InitialOf(TrackDefinition t, Stat maximum) => block.Initial.TryGetValue(t.Id, out float initial)
        ? Math.Min(initial, maximum.Value) : maximum.Value;

    private static StatId StatOf(string id) => StatId.Parse(id);
    private static TrackId TrackOf(string id) => TrackId.Parse(id);
    private static StatId ResistanceOf(string kind) => StatId.Parse(ResistanceStat(kind));
    /// <summary>The id of the stat holding an actor's resistance to a damage kind.</summary>
    internal static string ResistanceStat(string kind) => $"resistance.{kind}";
}

/// <summary>An actor's saved stats: every stat's base and every track's current points, by Engine id, and its effects.</summary>
internal sealed record ActorStatsState(Dictionary<string, double> Bases, Dictionary<string, double> Tracks, EffectState[] Effects);
