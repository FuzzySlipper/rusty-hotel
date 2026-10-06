using System.Text.Json.Serialization;
using Hotel.Game.Content;

namespace Hotel.Game.Mechanics;

/// <summary>What an effect does while it lasts; each kind has its own typed settings on the definition.</summary>
internal enum EffectKind { Stat, Restore, Damage, Ward, Hold, Slow, Reveal, Light }

/// <summary>How a second application of an effect meets one already in its group (the Engine stacking policy).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EffectStacking>))]
internal enum EffectStacking
{
    /// <summary>Each source holds its own instance, up to the group's instance limit; the same source again restarts it.</summary>
    Independent,
    /// <summary>One instance in the group; another application adds a stack, up to the stack limit, and restarts it.</summary>
    Refresh,
    /// <summary>One instance in the group; another application replaces it with a fresh one.</summary>
    Replace,
}

/// <summary>
/// One reusable effect: its name and HUD mark, the stacking group it shares, how applications stack, how long it lasts
/// in admitted seconds, and exactly one kind's settings. Items, residents, weapons and spirits name it by id.
/// </summary>
internal sealed record EffectDefinition(string Id, string Name, string Mark, string Group, EffectStacking Stacking,
    int MaximumStacks, int MaximumInstances, float Duration,
    StatEffect? Stat = null, RestoreEffect? Restore = null, DamageEffect? Damage = null, WardEffect? Ward = null,
    HoldEffect? Hold = null, SlowEffect? Slow = null, RevealEffect? Reveal = null, LightEffect? Light = null)
{
    internal const string Path = "mechanics/effects.json";

    internal EffectKind Kind => Stat is not null ? EffectKind.Stat : Restore is not null ? EffectKind.Restore
        : Damage is not null ? EffectKind.Damage : Ward is not null ? EffectKind.Ward : Hold is not null ? EffectKind.Hold
        : Slow is not null ? EffectKind.Slow : Reveal is not null ? EffectKind.Reveal : EffectKind.Light;

    /// <summary>The stats this effect contributes to while active, one Engine source per stack.</summary>
    internal bool ContributesStats => Stat is not null || Slow is not null;

    /// <summary>Seconds between an over-time effect's ticks; zero for kinds that do not tick.</summary>
    internal float Interval => Restore?.Interval ?? Damage?.Interval ?? 0;

    internal void Validate(string field, MechanicsDefinition mechanics, string pace)
    {
        Template.Plain(Path, ($"{field}.name", Name), ($"{field}.mark", Mark));
        Authored.Require(Group.Length > 0, Path, $"{field}.group", "is empty.");
        Authored.Within(Path, $"{field}.maximumStacks", MaximumStacks, 1, ushort.MaxValue);
        Authored.Within(Path, $"{field}.maximumInstances", MaximumInstances, 1, ushort.MaxValue);
        Authored.Require(Stacking == EffectStacking.Refresh || MaximumStacks == 1, Path, $"{field}.maximumStacks",
            "only a refresh effect gathers stacks; set it to 1.");
        Authored.Require(Stacking == EffectStacking.Independent || MaximumInstances == 1, Path, $"{field}.maximumInstances",
            "only an independent effect holds several instances; set it to 1.");
        Authored.Positive(Path, $"{field}.duration", Duration);
        int kinds = new object?[] { Stat, Restore, Damage, Ward, Hold, Slow, Reveal, Light }.Count(k => k is not null);
        Authored.Require(kinds == 1, Path, field, "must set exactly one of stat, restore, damage, ward, hold, slow, reveal or light.");
        if (Stat is { } stat)
        {
            Authored.Require(mechanics.HasStat(stat.Stat) && stat.Stat != pace, Path, $"{field}.stat.stat",
                $"'{stat.Stat}' is not an attribute or derived stat (pace is changed by a slow).");
            Authored.Finite(Path, $"{field}.stat.amount", stat.Amount);
        }
        if (Restore is { } restore)
        {
            Authored.Require(mechanics.Tracks.Any(t => t.Id == restore.Track), Path, $"{field}.restore.track", $"unknown track '{restore.Track}'.");
            Authored.Positive(Path, $"{field}.restore.amount", restore.Amount);
            Ticks($"{field}.restore.interval", restore.Interval);
        }
        if (Damage is { } damage)
        {
            Authored.Require(mechanics.DamageKind(damage.DamageKind) is not null, Path, $"{field}.damage.damageKind",
                $"unknown damage kind '{damage.DamageKind}'.");
            Authored.Positive(Path, $"{field}.damage.amount", damage.Amount);
            Ticks($"{field}.damage.interval", damage.Interval);
        }
        if (Ward is { } ward)
        {
            Authored.Positive(Path, $"{field}.ward.absorb", ward.Absorb);
            Authored.Require(ward.DamageKinds.Length > 0, Path, $"{field}.ward.damageKinds", "names no damage kind.");
            for (int i = 0; i < ward.DamageKinds.Length; i++)
                Authored.Require(mechanics.DamageKind(ward.DamageKinds[i]) is not null, Path, $"{field}.ward.damageKinds[{i}]",
                    $"unknown damage kind '{ward.DamageKinds[i]}'.");
        }
        if (Slow is { } slow) Authored.Within(Path, $"{field}.slow.factor", slow.Factor, 0, 1);
        if (Reveal is { } reveal)
        {
            Authored.Positive(Path, $"{field}.reveal.radius", reveal.Radius);
            Template.Check(Path, $"{field}.reveal.sensed", reveal.Sensed, "count");
        }
        if (Light is { } light)
        {
            Authored.Colour(Path, $"{field}.light.color", light.Color);
            Authored.Positive(Path, $"{field}.light.intensity", light.Intensity);
            Authored.Positive(Path, $"{field}.light.range", light.Range);
            Authored.Finite(Path, $"{field}.light.lift", light.Lift);
        }

        void Ticks(string at, float interval)
        {
            Authored.Positive(Path, at, interval);
            Authored.Require(interval <= Duration, Path, at, "is longer than the effect lasts.");
        }
    }
}

/// <summary>Adds <see cref="Amount"/> per stack to an attribute or derived stat.</summary>
internal sealed record StatEffect(string Stat, float Amount);
/// <summary>Restores <see cref="Amount"/> points per stack to a track every <see cref="Interval"/> seconds.</summary>
internal sealed record RestoreEffect(string Track, int Amount, float Interval);
/// <summary>Deals <see cref="Amount"/> damage per stack of one kind every <see cref="Interval"/> seconds, against resistance.</summary>
internal sealed record DamageEffect(string DamageKind, int Amount, float Interval);
/// <summary>Absorbs up to <see cref="Absorb"/> points per stack of the named kinds of damage; spent, it ends.</summary>
internal sealed record WardEffect(int Absorb, string[] DamageKinds);
/// <summary>Holds its bearer still: no movement and no attack while it lasts.</summary>
internal sealed record HoldEffect;
/// <summary>Multiplies its bearer's pace by <see cref="Factor"/>.</summary>
internal sealed record SlowEffect(float Factor);
/// <summary>Senses living residents within <see cref="Radius"/>, through walls; <see cref="Sensed"/> names how many.</summary>
internal sealed record RevealEffect(float Radius, string Sensed);
/// <summary>A light carried at the bearer's eye, raised by <see cref="Lift"/>.</summary>
internal sealed record LightEffect(float[] Color, float Intensity, float Range, float Lift);

internal sealed record EffectCatalog(EffectDefinition[] Effects);

/// <summary>How an active effect's state reads on the HUD: time left, stacks and a ward's remaining absorption.</summary>
internal sealed record MechanicsMessages(string EffectSeconds, string EffectStacks, string EffectWard)
{
    internal const string Path = "mechanics/messages.json";

    internal void Validate()
    {
        Template.Check(Path, "effectSeconds", EffectSeconds, "seconds");
        Template.Check(Path, "effectStacks", EffectStacks, "stacks");
        Template.Check(Path, "effectWard", EffectWard, "ward");
    }
}
