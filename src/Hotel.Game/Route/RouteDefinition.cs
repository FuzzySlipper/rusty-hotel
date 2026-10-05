using Hotel.Game.Content;
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

/// <summary>One excursion's doors, readings and named rooms.</summary>
internal sealed record ExcursionRoute(string FallbackLocation, DoorDefinition[] Doors, ReadingDefinition[] Readings, RoomDefinition[] Rooms)
{
    internal void Validate(string path)
    {
        for (int i = 0; i < Doors.Length; i++)
        {
            DoorDefinition door = Doors[i];
            Authored.Point(path, $"doors[{i}].hinge", door.Hinge);
            Authored.Positive(path, $"doors[{i}].width", door.Width);
            Authored.Positive(path, $"doors[{i}].height", door.Height);
            Authored.Finite(path, $"doors[{i}].closedYaw", door.ClosedYaw);
            Authored.Finite(path, $"doors[{i}].openYaw", door.OpenYaw);
            Authored.Point(path, $"doors[{i}].focusPoint", door.FocusPoint);
            if (door.UnlockDirection is { } direction) Authored.Point(path, $"doors[{i}].unlockDirection", direction);
        }
        for (int i = 0; i < Readings.Length; i++) Authored.Point(path, $"readings[{i}].point", Readings[i].Point);
        for (int i = 0; i < Rooms.Length; i++)
        {
            Authored.Point(path, $"rooms[{i}].min", Rooms[i].Min);
            Authored.Point(path, $"rooms[{i}].max", Rooms[i].Max);
            Authored.Require(Rooms[i].Max[0] >= Rooms[i].Min[0] && Rooms[i].Max[2] >= Rooms[i].Min[2], path, $"rooms[{i}].max",
                "a room's max must not be below its min on x or z.");
        }
    }
}
internal sealed record DoorDefinition(string Id, string Label, float[] Hinge, float Width, float Height,
    float ClosedYaw, float OpenYaw, string Material, string HandleMaterial, float[] FocusPoint, bool FarSideLatch = false,
    float[]? UnlockDirection = null, string? LockedPrompt = null);
internal sealed record ReadingDefinition(string Id, string Label, float[] Point, string Title, string Text);
internal sealed record RoomDefinition(string Id, string Label, float[] Min, float[] Max);
