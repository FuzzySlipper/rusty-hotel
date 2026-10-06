using System.Text.Json.Serialization;
using Hotel.Game.Content;
using Hotel.Game.Mechanics;
using Rusty.Engine;

namespace Hotel.Game.Actions;

/// <summary>How an action reaches what it affects; each is resolved by an Engine spatial query.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DeliveryKind>))]
internal enum DeliveryKind
{
    /// <summary>A capsule swept along the aim from the eye: the first body it meets within reach.</summary>
    Melee,
    /// <summary>A ray along the aim: the first body or surface within range.</summary>
    Hitscan,
    /// <summary>A shot that travels along the aim at a speed, cast segment by segment on admitted time.</summary>
    Projectile,
    /// <summary>Every body within a radius of a point (the user, or along the aim at range) with a clear line to it.</summary>
    Area,
    /// <summary>The user alone.</summary>
    Self,
}

/// <summary>Which side of a hit a damage contribution belongs to.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ContributionSide>))]
internal enum ContributionSide { Outgoing, Incoming }

/// <summary>
/// One gameplay action used by the investigator's hands and by residents alike: how it is delivered, what it costs, its
/// timing, the damage packets it deals (scaled by the user's stats) and the effects it puts on what it hits and on its
/// user. The labels name its windup and commit phases on the HUD.
/// </summary>
internal sealed record ActionDefinition(string Id, string Name, ActionDelivery Delivery, ActionCost Cost, ActionTiming Timing,
    DamageDefinition[] Damage, string[] Effects, string[] SelfEffects, string WindupLabel, string CommitLabel);

/// <param name="Range">Reach (melee), distance (hitscan, projectile), or how far along the aim an area is centred (0 is the user).</param>
/// <param name="Width">A melee sweep's or a projectile's width.</param>
/// <param name="Radius">An area's radius.</param>
/// <param name="Speed">A projectile's speed, metres per second.</param>
internal sealed record ActionDelivery(DeliveryKind Kind, float Range = 0, float Width = 0, float Radius = 0, float Speed = 0);

/// <summary>
/// What an action costs. Track points are spent once, when it is accepted. An item cost names a classification: one
/// carried item of it is used, by its own use, when the action lands (loading a pistol uses a packet of cartridges).
/// </summary>
internal sealed record ActionCost(Dictionary<string, int> Tracks, string? Item = null);

/// <summary>Seconds of windup before it lands, of commitment after, of recovery, and before it may be used again.</summary>
internal sealed record ActionTiming(float Windup, float Commit, float Recovery, float Cooldown);

/// <summary>A damage packet: its kind, base amount and how much each point of a stat adds.</summary>
internal sealed record DamageDefinition(string Kind, int Amount, StatScaling[] Scaling);
internal sealed record StatScaling(string Stat, float PerPoint);

/// <summary>
/// A worn item's change to the damage of hits its wearer deals (outgoing) or takes (incoming), of the named kinds (none
/// is every kind): added, then multiplied, before resistance.
/// </summary>
internal sealed record DamageContribution(ContributionSide Side, string[] Kinds, int Add, float Multiply)
{
    internal bool Applies(ContributionSide side, string kind) => Side == side && (Kinds.Length == 0 || Kinds.Contains(kind));
}

internal sealed record ActionCatalog(ActionDefinition[] Actions)
{
    internal const string Path = "actions/actions.json";

    internal static ActionCatalog Load(IEngineContext engine, MechanicsDefinition mechanics, string[] classifications)
    {
        ActionCatalog catalog = Authored.Read(engine, Path, ContentJson.Default.ActionCatalog);
        string? repeated = catalog.Actions.GroupBy(a => a.Id).FirstOrDefault(g => g.Count() > 1)?.Key;
        Authored.Require(repeated is null, Path, "actions", $"id '{repeated}' appears more than once.");
        for (int i = 0; i < catalog.Actions.Length; i++) catalog.Actions[i].Validate($"actions[{i}]", mechanics, classifications);
        return catalog;
    }

    internal ActionDefinition? Action(string id) => Actions.FirstOrDefault(a => a.Id == id);

    /// <summary>Checks action ids authored elsewhere, naming the file and field of an unknown one.</summary>
    internal void Require(string path, string field, params string[] ids)
    {
        for (int i = 0; i < ids.Length; i++)
            Authored.Require(Action(ids[i]) is not null, path, $"{field}[{i}]", $"unknown action '{ids[i]}'; see content/{Path}.");
    }
}

internal static class ActionValidation
{
    internal static void Validate(this ActionDefinition a, string at, MechanicsDefinition mechanics, string[] classifications)
    {
        string path = ActionCatalog.Path;
        Template.Plain(path, ($"{at}.name", a.Name), ($"{at}.windupLabel", a.WindupLabel), ($"{at}.commitLabel", a.CommitLabel));
        ActionDelivery d = a.Delivery;
        switch (d.Kind)
        {
            case DeliveryKind.Melee:
                Authored.Positive(path, $"{at}.delivery.range", d.Range);
                Authored.Positive(path, $"{at}.delivery.width", d.Width);
                break;
            case DeliveryKind.Hitscan:
                Authored.Positive(path, $"{at}.delivery.range", d.Range);
                break;
            case DeliveryKind.Projectile:
                Authored.Positive(path, $"{at}.delivery.range", d.Range);
                Authored.Positive(path, $"{at}.delivery.width", d.Width);
                Authored.Positive(path, $"{at}.delivery.speed", d.Speed);
                break;
            case DeliveryKind.Area:
                Authored.AtLeast(path, $"{at}.delivery.range", d.Range, 0);
                Authored.Positive(path, $"{at}.delivery.radius", d.Radius);
                break;
            case DeliveryKind.Self:
                Authored.Require(a.Damage.Length == 0 && a.Effects.Length == 0, path, at, "a self action deals no damage and puts no effect on a target; use selfEffects.");
                break;
        }
        Authored.AtLeast(path, $"{at}.timing.windup", a.Timing.Windup, 0);
        Authored.Positive(path, $"{at}.timing.commit", a.Timing.Commit);
        Authored.AtLeast(path, $"{at}.timing.recovery", a.Timing.Recovery, 0);
        Authored.AtLeast(path, $"{at}.timing.cooldown", a.Timing.Cooldown, 0);
        foreach (var (track, amount) in a.Cost.Tracks)
        {
            Authored.Require(mechanics.Tracks.Any(t => t.Id == track), path, $"{at}.cost.tracks.{track}", "is not a track.");
            Authored.AtLeast(path, $"{at}.cost.tracks.{track}", amount, 1);
        }
        if (a.Cost.Item is { } item)
            Authored.Require(classifications.Contains(item), path, $"{at}.cost.item", $"unknown classification '{item}'.");
        for (int i = 0; i < a.Damage.Length; i++)
        {
            Authored.Require(mechanics.DamageKind(a.Damage[i].Kind) is not null, path, $"{at}.damage[{i}].kind", $"unknown damage kind '{a.Damage[i].Kind}'.");
            Authored.AtLeast(path, $"{at}.damage[{i}].amount", a.Damage[i].Amount, 0);
            for (int s = 0; s < a.Damage[i].Scaling.Length; s++)
            {
                Authored.Require(mechanics.HasStat(a.Damage[i].Scaling[s].Stat), path, $"{at}.damage[{i}].scaling[{s}].stat",
                    $"'{a.Damage[i].Scaling[s].Stat}' is not a stat.");
                Authored.Finite(path, $"{at}.damage[{i}].scaling[{s}].perPoint", a.Damage[i].Scaling[s].PerPoint);
            }
        }
        mechanics.RequireEffects(path, $"{at}.effects", a.Effects);
        mechanics.RequireEffects(path, $"{at}.selfEffects", a.SelfEffects);
    }

    /// <summary>Checks a list of damage contributions authored on an item or elsewhere.</summary>
    internal static void Validate(DamageContribution[] contributions, string path, string field, MechanicsDefinition mechanics)
    {
        for (int i = 0; i < contributions.Length; i++)
        {
            for (int k = 0; k < contributions[i].Kinds.Length; k++)
                Authored.Require(mechanics.DamageKind(contributions[i].Kinds[k]) is not null, path, $"{field}[{i}].kinds[{k}]",
                    $"unknown damage kind '{contributions[i].Kinds[k]}'.");
            Authored.AtLeast(path, $"{field}[{i}].multiply", contributions[i].Multiply, 0);
        }
    }
}
