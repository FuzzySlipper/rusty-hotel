using System.Numerics;
using System.Text.Json.Serialization;
using Hotel.Game.Content;
using Hotel.Game.Scene.Kit;
using Rusty.Engine;

namespace Hotel.Game.Route;

/// <summary>The route domain's shared files: interaction tuning and the focus prompt wording.</summary>
internal sealed record RouteDefinition(InteractionTuning Interaction, RouteMessages Text)
{
    internal static RouteDefinition Load(IEngineContext engine, IReadOnlyDictionary<string, string> keys)
    {
        RouteMessages text = Authored.Read(engine, RouteMessages.Path, ContentJson.Default.RouteMessages, keys);
        Template.Check(RouteMessages.Path, "ready", text.Ready, "label");
        Template.Check(RouteMessages.Path, "outOfReach", text.OutOfReach, "label");
        Template.Check(RouteMessages.Path, "take", text.Take, "item");
        Template.Plain(RouteMessages.Path, ("recordCheckpoint", text.RecordCheckpoint));
        InteractionTuning interaction = Authored.Read(engine, InteractionTuning.Path, ContentJson.Default.InteractionTuning);
        Authored.Positive(InteractionTuning.Path, "reach", interaction.Reach);
        Authored.Within(InteractionTuning.Path, "focusDistance", interaction.FocusDistance, interaction.Reach, float.MaxValue);
        Authored.Within(InteractionTuning.Path, "acquireAngle", interaction.AcquireAngle, float.Epsilon, MathF.PI / 2);
        Authored.Within(InteractionTuning.Path, "releaseAngle", interaction.ReleaseAngle, interaction.AcquireAngle, MathF.PI / 2);
        return new(interaction, text);
    }
}

/// <summary>Reach and focus tuning shared by every world interaction.</summary>
internal sealed record InteractionTuning(float Reach, float FocusDistance, float AcquireAngle, float ReleaseAngle)
{
    internal const string Path = "route/interaction.json";
}

/// <summary>Focus prompts and the labels of interactables that have no authored label of their own.</summary>
internal sealed record RouteMessages(string Ready, string OutOfReach, string Take, string RecordCheckpoint)
{
    internal const string Path = "route/messages.json";
}

/// <summary>One excursion's doors, readings and named rooms, resolved from its route plan and built floor.</summary>
internal sealed record ExcursionRoute(string FallbackLocation, DoorDefinition[] Doors, ReadingDefinition[] Readings, RoomDefinition[] Rooms);
/// <param name="Thickness">Leaf thickness; the leaf is centred in its wall when closed.</param>
internal sealed record DoorDefinition(string Id, string Label, float[] Hinge, float Width, float Height, float Thickness,
    float ClosedYaw, float OpenYaw, string Material, string HandleMaterial, float[] FocusPoint, bool FarSideLatch = false,
    float[]? UnlockDirection = null, string? LockedPrompt = null);
internal sealed record ReadingDefinition(string Id, string Label, float[] Point, string Title, string Text);
internal sealed record RoomDefinition(string Id, string Label, float[] Min, float[] Max);

/// <summary>
/// An excursion's authored route: doors hung in the floor plan's door links, and readings at fixture sockets.
/// Room names come from the plan's spaces.
/// </summary>
internal sealed record RoutePlan(string FallbackLocation, DoorPlacement[] Doors, ReadingPlacement[] Readings)
{
    internal ExcursionRoute Resolve(string path, BuiltFloor floor, DoorLeafTuning leaf)
    {
        DoorDefinition[] doors = new DoorDefinition[Doors.Length];
        for (int i = 0; i < Doors.Length; i++) doors[i] = Doors[i].Resolve(path, $"doors[{i}]", floor, leaf);
        ReadingDefinition[] readings = new ReadingDefinition[Readings.Length];
        for (int i = 0; i < Readings.Length; i++)
        {
            ReadingPlacement r = Readings[i];
            readings[i] = new(r.Id, r.Label, Socket(path, $"readings[{i}].socket", floor, r.Socket), r.Title, r.Text);
        }
        return new(FallbackLocation, doors, readings, floor.Rooms);
    }

    internal static float[] Socket(string path, string field, BuiltFloor floor, string socket)
    {
        Authored.Require(floor.Sockets.TryGetValue(socket, out Vector3 point), path, field, $"unknown socket '{socket}'.");
        return [point.X, point.Y, point.Z];
    }
}

[JsonConverter(typeof(JsonStringEnumConverter<DoorHinge>))]
internal enum DoorHinge { Start, End }

/// <summary>
/// A gameplay door hung in a door link's opening. It is hinged at the opening's <see cref="Hinge"/> end and
/// swings into <see cref="OpensInto"/>. A door <see cref="LatchedFrom"/> a space opens only from that side.
/// </summary>
internal sealed record DoorPlacement(string Id, string Label, string Link, DoorHinge Hinge, string OpensInto,
    string Material, string HandleMaterial, string? LatchedFrom = null, string? LockedPrompt = null)
{
    internal DoorDefinition Resolve(string path, string at, BuiltFloor floor, DoorLeafTuning leaf)
    {
        Authored.Require(floor.Openings.TryGetValue(Link, out BuiltOpening? opening) && opening.Link.Kind == LinkKind.Door,
            path, $"{at}.link", $"'{Link}' is not a door link in the floor plan.");
        string[] between = opening!.Link.Between;
        Vector3 Toward(string space, string field)
        {
            Authored.Require(between.Contains(space), path, field, $"'{space}' is not one of the spaces the door joins.");
            return space == between[1] ? opening.Normal : -opening.Normal;
        }
        Vector3 into = Toward(OpensInto, $"{at}.opensInto");
        Vector3 hingeEnd = Hinge == DoorHinge.Start ? opening.Start : opening.End;
        Vector3 along = Vector3.Normalize((Hinge == DoorHinge.Start ? opening.End : opening.Start) - hingeEnd);
        // Yaw turns the leaf's +x along the opening (same sense as Quaternion.CreateFromAxisAngle about +Y).
        float closed = MathF.Atan2(-along.Z, along.X);
        Vector3 thickness = new(MathF.Sin(closed), 0, MathF.Cos(closed));
        Vector3 hinge = hingeEnd - thickness * leaf.Thickness / 2;
        float swing = Vector3.Dot(new(MathF.Cos(closed + MathF.PI / 2), 0, -MathF.Sin(closed + MathF.PI / 2)), into) > 0 ? 90 : -90;
        Vector3 centre = (opening.Start + opening.End) / 2 + Vector3.UnitY * leaf.FocusHeight;
        float[]? unlock = null;
        if (LatchedFrom is { } side) { Vector3 d = Toward(side, $"{at}.latchedFrom"); unlock = [d.X, d.Y, d.Z]; }
        float degrees = closed * 180 / MathF.PI;
        return new(Id, Label, [hinge.X, hinge.Y, hinge.Z], Vector3.Distance(opening.Start, opening.End),
            opening.Height - leaf.Clearance, leaf.Thickness, degrees, degrees + swing, Material, HandleMaterial,
            [centre.X, centre.Y, centre.Z], LatchedFrom is not null, unlock, LockedPrompt);
    }
}

internal sealed record ReadingPlacement(string Id, string Label, string Socket, string Title, string Text);
