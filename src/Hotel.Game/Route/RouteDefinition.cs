using Hotel.Game.Content;
using Rusty.Engine;

namespace Hotel.Game.Route;

/// <summary>The route domain's shared files: interaction tuning and the focus prompt wording.</summary>
internal sealed record RouteDefinition(InteractionTuning Interaction, RouteMessages Text)
{
    internal static RouteDefinition Load(IEngineContext engine)
    {
        RouteMessages text = Authored.Read(engine, RouteMessages.Path, ContentJson.Default.RouteMessages);
        Template.Check(RouteMessages.Path, "ready", text.Ready, "label");
        Template.Check(RouteMessages.Path, "outOfReach", text.OutOfReach, "label");
        Template.Check(RouteMessages.Path, "take", text.Take, "item");
        return new(Authored.Read(engine, InteractionTuning.Path, ContentJson.Default.InteractionTuning), text);
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
internal sealed record ExcursionRoute(string FallbackLocation, DoorDefinition[] Doors, ReadingDefinition[] Readings, RoomDefinition[] Rooms);
internal sealed record DoorDefinition(string Id, string Label, float[] Hinge, float Width, float Height,
    float ClosedYaw, float OpenYaw, string Material, string HandleMaterial, float[] FocusPoint, bool FarSideLatch = false,
    float[]? UnlockDirection = null, string? LockedPrompt = null);
internal sealed record ReadingDefinition(string Id, string Label, float[] Point, string Title, string Text);
internal sealed record RoomDefinition(string Id, string Label, float[] Min, float[] Max);
