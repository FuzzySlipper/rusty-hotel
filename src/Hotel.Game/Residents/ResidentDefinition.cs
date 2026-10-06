using System.Text.Json.Serialization;
using Hotel.Game.Actions;
using Hotel.Game.Content;
using Hotel.Game.Mechanics;
using Rusty.Engine;

namespace Hotel.Game.Residents;

/// <summary>
/// A kind of resident, composed from typed parts: how it looks and tells its attacks, its faction, how it perceives,
/// how it moves, which actions it chooses between, its stats and its body. Placements in an excursion name it.
/// </summary>
/// <param name="Look">Its silhouette and tell poses, by id in <c>combat/looks.json</c>.</param>
/// <param name="Actions">Its attacks in order of preference: the first that is ready, affordable and in reach is used.</param>
/// <param name="EyeHeight">Height of the eye above its body centre: sight lines, attacks and summon targets start here.</param>
/// <param name="Loot">The loot table its remains give when searched.</param>
/// <param name="Experience">The experience the investigator gains by felling it.</param>
internal sealed record ResidentKind(string Id, string Name, string Look, string Faction, ActorStatBlock Stats,
    ResidentPerception Perception, ResidentMovement Movement, ActionChoice[] Actions, float Radius, float Height, float EyeHeight, string Loot,
    int Experience)
{
    /// <summary>How far its farthest attack reaches, resolved from its actions when combat content loads.</summary>
    [JsonIgnore] internal float AttackReach { get; init; }
}

/// <summary>
/// What a resident notices: hostile bodies within <see cref="Range"/> and its field of view with a clear line (an Engine
/// visibility query), or any within <see cref="NearSense"/> with a clear line; it stays aware <see cref="Memory"/>
/// seconds after losing sight.
/// </summary>
/// <param name="FieldOfView">Degrees across, centred on where it faces; 360 sees all around.</param>
internal sealed record ResidentPerception(float Range, float FieldOfView, float Memory, float NearSense);

/// <summary>How a resident moves: exactly one way of keeping its post, and optionally fleeing when hurt.</summary>
/// <param name="Speed">Walking speed, metres per second; zero never moves.</param>
/// <param name="Leash">How far from its post it follows a target, metres.</param>
internal sealed record ResidentMovement(float Speed, float Leash, PostMovement? Post = null, PatrolMovement? Patrol = null,
    StalkMovement? Stalk = null, AmbushMovement? Ambush = null, FleeMovement? Flee = null)
{
    internal MovementKind Kind => Post is not null ? MovementKind.Post : Patrol is not null ? MovementKind.Patrol
        : Stalk is not null ? MovementKind.Stalk : MovementKind.Ambush;

    /// <summary>The farthest from its post a resident may be found: its leash, or its patrol's farthest point.</summary>
    internal float Range => Math.Max(Leash, Patrol?.Points.Max(p => MathF.Sqrt(p[0] * p[0] + p[1] * p[1])) ?? 0);
}

internal enum MovementKind { Post, Patrol, Stalk, Ambush }

/// <summary>Stands its post and turns to face what it notices; never walks.</summary>
internal sealed record PostMovement;
/// <summary>Walks a loop of points (offsets from its post, x and z), pausing at each; stalks what it notices.</summary>
internal sealed record PatrolMovement(float[][] Points, float Pause);
/// <summary>Waits at its post, approaches what it notices within its leash, and walks back when it forgets.</summary>
internal sealed record StalkMovement;
/// <summary>Waits unseen and unturning, noticing nothing until a hostile comes within <see cref="Trigger"/>; then stalks.</summary>
internal sealed record AmbushMovement(float Trigger);
/// <summary>Below <see cref="Below"/> of its health, backs away to keep <see cref="Distance"/> from its target.</summary>
internal sealed record FleeMovement(float Below, float Distance);

/// <summary>One attack a resident may choose, and the nearest its target may be for it (keeps a ranged attack for range).</summary>
internal sealed record ActionChoice(string Action, float Minimum);

/// <summary>Resident kinds, independent of where an excursion places them.</summary>
internal sealed record ResidentCatalog(ResidentKind[] Residents)
{
    internal const string Path = "combat/residents.json";
}

/// <summary>The factions of the hotel and which of them are hostile to each other; the investigator has one too.</summary>
internal sealed record FactionCatalog(FactionDefinition[] Factions, string[][] Hostile, string Investigator)
{
    internal const string Path = "combat/factions.json";

    internal bool AreHostile(string a, string b) => Hostile.Any(p => p[0] == a && p[1] == b || p[0] == b && p[1] == a);

    internal void Validate()
    {
        Authored.Require(Factions.Select(f => f.Id).Distinct().Count() == Factions.Length, Path, "factions", "an id appears more than once.");
        for (int i = 0; i < Factions.Length; i++) Template.Plain(Path, ($"factions[{i}].name", Factions[i].Name));
        Authored.Require(Factions.Any(f => f.Id == Investigator), Path, "investigator", $"unknown faction '{Investigator}'.");
        for (int i = 0; i < Hostile.Length; i++)
        {
            Authored.Require(Hostile[i].Length == 2 && Hostile[i][0] != Hostile[i][1], Path, $"hostile[{i}]", "pairs two different factions.");
            for (int s = 0; s < Hostile[i].Length; s++)
                Authored.Require(Factions.Any(f => f.Id == Hostile[i][s]), Path, $"hostile[{i}][{s}]", $"unknown faction '{Hostile[i][s]}'.");
        }
    }
}
internal sealed record FactionDefinition(string Id, string Name);

/// <summary>What a look part is to its tells: the body, the arm that rises and strikes, or the tell that lights during an attack.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LookRole>))]
internal enum LookRole { Body, Arm, Tell }

/// <summary>
/// A resident silhouette in boxes, in the resident's own frame (metres from its body centre, facing -z), and the tell
/// poses that make its attacks readable: where the arm goes in windup and at the strike, and how far it droops in recovery.
/// </summary>
internal sealed record ResidentLook(string Id, LookPart[] Parts, float[] ArmWindup, float[] ArmStrike, float Droop);
internal sealed record LookPart(string Material, float[] Offset, float[] Size, LookRole Role);

internal sealed record LookCatalog(ResidentLook[] Looks)
{
    internal const string Path = "combat/looks.json";
}

internal static class ResidentValidation
{
    internal static void Validate(this ResidentKind r, int i, MechanicsDefinition mechanics, ActionCatalog actions,
        FactionCatalog factions, LookCatalog looks)
    {
        string path = ResidentCatalog.Path, at = $"residents[{i}]";
        Template.Plain(path, ($"{at}.name", r.Name));
        Authored.Require(looks.Looks.Any(l => l.Id == r.Look), path, $"{at}.look", $"unknown look '{r.Look}'; see content/{LookCatalog.Path}.");
        Authored.Require(factions.Factions.Any(f => f.Id == r.Faction) && r.Faction != factions.Investigator, path, $"{at}.faction",
            $"'{r.Faction}' is not a resident faction in content/{FactionCatalog.Path}.");
        mechanics.Validate(path, $"{at}.stats", r.Stats);
        Authored.Positive(path, $"{at}.perception.range", r.Perception.Range);
        Authored.Within(path, $"{at}.perception.fieldOfView", r.Perception.FieldOfView, 1, 360);
        Authored.AtLeast(path, $"{at}.perception.memory", r.Perception.Memory, 0);
        Authored.AtLeast(path, $"{at}.perception.nearSense", r.Perception.NearSense, 0);
        ResidentMovement m = r.Movement;
        Authored.AtLeast(path, $"{at}.movement.speed", m.Speed, 0);
        Authored.AtLeast(path, $"{at}.movement.leash", m.Leash, 0);
        int ways = new object?[] { m.Post, m.Patrol, m.Stalk, m.Ambush }.Count(x => x is not null);
        Authored.Require(ways == 1, path, $"{at}.movement", "must set exactly one of post, patrol, stalk or ambush.");
        Authored.Require(m.Post is not null || m.Speed > 0, path, $"{at}.movement.speed", "a resident that leaves its post needs a speed.");
        if (m.Patrol is { } patrol)
        {
            Authored.Require(patrol.Points.Length >= 2, path, $"{at}.movement.patrol.points", "a patrol needs two points or more.");
            for (int p = 0; p < patrol.Points.Length; p++)
                Authored.Require(patrol.Points[p].Length == 2 && patrol.Points[p].All(float.IsFinite), path, $"{at}.movement.patrol.points[{p}]",
                    "is an x, z offset from the post.");
            Authored.AtLeast(path, $"{at}.movement.patrol.pause", patrol.Pause, 0);
        }
        if (m.Ambush is { } ambush) Authored.Positive(path, $"{at}.movement.ambush.trigger", ambush.Trigger);
        if (m.Flee is { } flee)
        {
            Authored.Within(path, $"{at}.movement.flee.below", flee.Below, 0, 1);
            Authored.Positive(path, $"{at}.movement.flee.distance", flee.Distance);
        }
        Authored.Require(r.Actions.Length > 0, path, $"{at}.actions", "a resident needs an attack.");
        actions.Require(path, $"{at}.actions", r.Actions.Select(a => a.Action).ToArray());
        for (int a = 0; a < r.Actions.Length; a++)
        {
            Authored.Require(actions.Action(r.Actions[a].Action)!.Delivery.Kind != DeliveryKind.Self, path, $"{at}.actions[{a}].action",
                "a resident attacks with an action that reaches its target.");
            Authored.AtLeast(path, $"{at}.actions[{a}].minimum", r.Actions[a].Minimum, 0);
        }
        Authored.Positive(path, $"{at}.radius", r.Radius);
        Authored.Positive(path, $"{at}.height", r.Height);
        // The eye sits within the upper half of the body, measured from its centre.
        Authored.Within(path, $"{at}.eyeHeight", r.EyeHeight, 0, r.Height / 2);
    }

    internal static void Validate(this ResidentLook look, int i, string[] materials)
    {
        string path = LookCatalog.Path, at = $"looks[{i}]";
        Authored.Require(look.Parts.Length > 0, path, $"{at}.parts", "a look needs a part.");
        for (int p = 0; p < look.Parts.Length; p++)
        {
            Authored.Require(materials.Contains(look.Parts[p].Material), path, $"{at}.parts[{p}].material", $"unknown surface '{look.Parts[p].Material}'.");
            Authored.Require(look.Parts[p].Offset.Length == 3 && look.Parts[p].Offset.All(float.IsFinite), path, $"{at}.parts[{p}].offset", "is x, y, z.");
            Authored.Require(look.Parts[p].Size.Length == 3 && look.Parts[p].Size.All(s => s > 0), path, $"{at}.parts[{p}].size", "is three positive sizes.");
        }
        Authored.Require(look.ArmWindup.Length == 3 && look.ArmStrike.Length == 3, path, at, "armWindup and armStrike are x, y, z offsets.");
        Authored.AtLeast(path, $"{at}.droop", look.Droop, 0);
    }
}
