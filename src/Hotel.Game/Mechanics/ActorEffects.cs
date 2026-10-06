using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using EngineEffectDefinition = Rusty.Engine.Mechanics.EffectDefinition;

namespace Hotel.Game.Mechanics;

/// <summary>Why a live effect left its bearer.</summary>
internal enum EffectEnd { Expired, Spent, Replaced, Cleared }

/// <summary>One effect on one actor: the Engine entry's identity with the product's remaining time, tick and ward.</summary>
internal sealed class LiveEffect(EffectDefinition definition, EffectInstanceId instance, string source)
{
    internal EffectDefinition Definition { get; } = definition;
    internal EffectInstanceId Instance { get; } = instance;
    /// <summary>Who applied it ("item.bandage", "resident.floor-1/p4/lamp"); an independent effect keeps one per source.</summary>
    internal string Source { get; set; } = source;
    internal int Stacks { get; set; }
    /// <summary>Admitted seconds left before it expires.</summary>
    internal float Remaining { get; set; }
    /// <summary>Admitted seconds since its last tick, for an effect that ticks.</summary>
    internal float SinceTick { get; set; }
    /// <summary>Damage a ward can still absorb.</summary>
    internal int WardLeft { get; set; }
    /// <summary>The Engine stat sources its stacks activated; they leave the stats when it ends.</summary>
    internal StatSource[] Sources { get; set; } = [];
}

/// <summary>
/// The effects one actor bears, over an Engine <see cref="EffectsComponent"/> that owns stacking and source provenance.
/// Adapted from rusty-dagger's <c>ActiveEffectLifecycle</c> (docs/reuse.md): every Engine admission and removal goes
/// through one path that keeps the product state and the stat sources in step. Durations and ticks advance only by
/// the admitted seconds passed to <see cref="Advance"/>, so a paused game holds every effect where it is.
/// </summary>
internal sealed class ActorEffects
{
    // Admitted seconds accumulate in float steps; a tick or expiry this close to its moment is due.
    private const float Due = 1e-4f;
    private readonly ActorStats stats;
    private readonly EntityId? owner;
    private readonly Dictionary<string, EngineEffectDefinition> engineDefinitions;
    private readonly Dictionary<EffectInstanceId, LiveEffect> live = [];
    private EffectsComponent component;
    private ulong next;

    internal ActorEffects(MechanicsDefinition mechanics, ActorStats stats, EntityId? owner)
    {
        this.stats = stats;
        this.owner = owner;
        component = new(owner);
        engineDefinitions = mechanics.Effects.ToDictionary(e => e.Id, e => new EngineEffectDefinition(EffectDefinitionId.Parse(e.Id),
            StackingGroupId.Parse(e.Group), e.Stacking switch
            {
                EffectStacking.Independent => EffectStackingPolicy.IndependentByProvenance,
                EffectStacking.Refresh => EffectStackingPolicy.Refresh,
                _ => EffectStackingPolicy.Replace
            }, checked((ushort)e.MaximumInstances), checked((ushort)e.MaximumStacks),
            e.ContributesStats ? [SourceDefinitionId.Parse($"effect.{e.Id}")] : []), StringComparer.Ordinal);
    }

    /// <summary>Active effects in admission order.</summary>
    internal IReadOnlyList<LiveEffect> Active => live.Values.OrderBy(e => e.Instance.Value, StringComparer.Ordinal).ToArray();
    internal bool Held => live.Values.Any(e => e.Definition.Hold is not null);
    /// <summary>The farthest-reaching carried light, if any.</summary>
    internal LightEffect? Light => live.Values.Select(e => e.Definition.Light).Where(l => l is not null).MaxBy(l => l!.Range);
    /// <summary>The widest reveal, if any.</summary>
    internal RevealEffect? Reveal => live.Values.Select(e => e.Definition.Reveal).Where(r => r is not null).MaxBy(r => r!.Radius);
    /// <summary>The Engine stat sources every active effect holds, for the stats to evaluate.</summary>
    internal IEnumerable<StatSource> Sources => live.Values.SelectMany(e => e.Sources);

    /// <summary>
    /// Applies an effect from a source under its stacking rule. Returns the live effect, or null when an independent
    /// effect's group already holds as many instances as it allows.
    /// </summary>
    internal LiveEffect? Apply(EffectDefinition definition, string source)
    {
        LiveEffect? present = live.Values.FirstOrDefault(e => e.Definition.Group == definition.Group &&
            (definition.Stacking != EffectStacking.Independent || e.Source == source));
        EngineEffectDefinition entry = engineDefinitions[definition.Id];
        switch (definition.Stacking)
        {
            case EffectStacking.Independent when present is not null && present.Definition == definition:
            {
                present.Remaining = definition.Duration;
                present.WardLeft = Absorbs(definition, present.Stacks);
                return present;
            }
            case EffectStacking.Refresh when present is not null && present.Definition == definition:
            {
                int stacks = Math.Min(definition.MaximumStacks, present.Stacks + 1);
                LiveEffect refreshed = Admit(component.Refresh(present.Instance, Provenance(source), checked((ushort)stacks)),
                    definition, source, stacks, definition.Duration, present.SinceTick, Absorbs(definition, stacks));
                return refreshed;
            }
            case EffectStacking.Replace:
                return Admit(component.Replace(entry, NewInstance(), Provenance(source), 1),
                    definition, source, 1, definition.Duration, 0, Absorbs(definition, 1));
        }
        // Another effect sharing the group from this source gives way; otherwise this is a first application.
        if (present is not null) End(present, EffectEnd.Replaced);
        if (definition.Stacking == EffectStacking.Independent &&
            live.Values.Count(e => e.Definition.Group == definition.Group) >= definition.MaximumInstances) return null;
        return Admit(component.Apply(entry, NewInstance(), Provenance(source), 1), definition, source, 1, definition.Duration, 0,
            Absorbs(definition, 1));
    }

    /// <summary>
    /// Advances every effect by admitted seconds: over-time effects tick on their interval, then effects whose time
    /// is up expire. Returns whether any track changed.
    /// </summary>
    internal bool Advance(float seconds)
    {
        if (seconds <= 0) return false;
        bool changed = false;
        foreach (LiveEffect effect in Active)
        {
            if (!live.ContainsKey(effect.Instance)) continue;
            float interval = effect.Definition.Interval;
            if (interval > 0)
            {
                effect.SinceTick += Math.Min(seconds, effect.Remaining);
                while (effect.SinceTick >= interval - Due && live.ContainsKey(effect.Instance))
                {
                    effect.SinceTick = Math.Max(0, effect.SinceTick - interval);
                    changed |= Tick(effect);
                }
            }
            if (!live.ContainsKey(effect.Instance)) continue;
            effect.Remaining -= seconds;
            if (effect.Remaining <= Due) End(effect, EffectEnd.Expired);
        }
        return changed;
    }

    /// <summary>Lets wards against this kind take what they can of a hit; returns what is left to land.</summary>
    internal int Absorb(string damageKind, int amount)
    {
        foreach (LiveEffect ward in Active.Where(e => e.Definition.Ward is { } w && w.DamageKinds.Contains(damageKind)))
        {
            if (amount <= 0) break;
            int taken = Math.Min(ward.WardLeft, amount);
            ward.WardLeft -= taken;
            amount -= taken;
            if (ward.WardLeft == 0) End(ward, EffectEnd.Spent);
        }
        return amount;
    }

    internal void End(LiveEffect effect, EffectEnd reason) => Settle(reason == EffectEnd.Expired
        ? component.Expire(effect.Instance) : component.Remove(effect.Instance), null);

    /// <summary>Removes every effect: a reset actor bears none.</summary>
    internal void Clear()
    {
        component = new(owner);
        live.Clear();
        next = 0;
        stats.ApplyEffectSources();
    }

    internal EffectState[] Capture() => Active.Select(e =>
        new EffectState(e.Definition.Id, e.Source, e.Stacks, e.Remaining, e.SinceTick, e.WardLeft)).ToArray();

    /// <summary>Refuses saved effects whose values cannot be live ones; stacking conflicts surface on the scratch restore.</summary>
    internal static void Validate(MechanicsDefinition mechanics, EffectState[] effects)
    {
        foreach (EffectState? state in effects)
        {
            EffectDefinition? definition = state is null ? null : mechanics.Effect(state.Effect);
            if (state is null || definition is null || string.IsNullOrEmpty(state.Source) ||
                state.Stacks < 1 || state.Stacks > definition.MaximumStacks ||
                !float.IsFinite(state.Remaining) || state.Remaining <= 0 || state.Remaining > definition.Duration ||
                !float.IsFinite(state.SinceTick) || state.SinceTick < 0 || state.SinceTick > Math.Max(0, definition.Interval) ||
                state.WardLeft < (definition.Ward is null ? 0 : 1) || state.WardLeft > Absorbs(definition, state.Stacks))
                throw new InvalidOperationException($"Checkpoint effect '{state?.Effect}' is unknown or out of bounds.");
        }
    }

    /// <summary>Re-admits validated saved effects in their saved order, rebuilding the stat sources they hold.</summary>
    internal void Restore(MechanicsDefinition mechanics, EffectState[] effects)
    {
        Clear();
        foreach (EffectState state in effects)
        {
            EffectDefinition definition = mechanics.Effect(state.Effect)!;
            Admit(component.Apply(engineDefinitions[definition.Id], NewInstance(), Provenance(state.Source), checked((ushort)state.Stacks)),
                definition, state.Source, state.Stacks, state.Remaining, state.SinceTick, state.WardLeft);
        }
    }

    private bool Tick(LiveEffect effect)
    {
        if (effect.Definition.Restore is { } restore)
            return stats.Track(restore.Track).Restore(restore.Amount * effect.Stacks) > 0;
        if (effect.Definition.Damage is { } damage)
            return stats.TakeDamage(new(damage.Amount * effect.Stacks, damage.DamageKind), ActorStats.HealthTrack) > 0;
        return false;
    }

    // One path for every Engine receipt: entries it removed leave with their sources, and its current entry is recorded
    // with the sources its stacks activated.
    private LiveEffect Admit(EffectMutationReceipt receipt, EffectDefinition definition, string source, int stacks,
        float remaining, float sinceTick, int wardLeft)
    {
        LiveEffect current = new(definition, receipt.Current!.Instance, source)
        {
            Stacks = stacks, Remaining = remaining, SinceTick = sinceTick, WardLeft = wardLeft,
            Sources = receipt.ActivatedSources.Select(a => new StatSource(a.Identity, a.Definition, 0, Contributions(definition))).ToArray()
        };
        Settle(receipt, current);
        return current;
    }

    private void Settle(EffectMutationReceipt receipt, LiveEffect? current)
    {
        foreach (ActiveEffect removed in receipt.Removed) live.Remove(removed.Instance);
        if (current is not null) live[current.Instance] = current;
        stats.ApplyEffectSources();
    }

    private static StatContributionDefinition[] Contributions(EffectDefinition definition)
    {
        if (definition.Stat is { } stat)
            return [new(StatId.Parse(stat.Stat), StackingGroupId.Parse($"effect.{definition.Group}.{stat.Stat}"),
                MechanicsStackingPolicy.Sum, new StatContribution.Add(stat.Amount))];
        // Slows do not compound: the strongest one sets the pace.
        if (definition.Slow is { } slow)
            return [new(StatId.Parse(ActorStats.PaceStat), StackingGroupId.Parse("effect.slow"), MechanicsStackingPolicy.Lowest,
                new StatContribution.Multiply(slow.Factor))];
        return [];
    }

    private static int Absorbs(EffectDefinition definition, int stacks) => (definition.Ward?.Absorb ?? 0) * stacks;
    private MechanicsSourceIdentity Provenance(string source) => new IntrinsicSourceIdentity(owner, SourceInstanceId.Parse($"applied.{source}"));
    // Zero-padded so the Engine's ordinal order is admission order.
    private EffectInstanceId NewInstance() => EffectInstanceId.Parse($"effect-{++next:D8}");
}

/// <summary>One saved effect: what it is, who applied it, its stacks, time left, time since its last tick and ward left.</summary>
internal sealed record EffectState(string Effect, string Source, int Stacks, float Remaining, float SinceTick, int WardLeft);
