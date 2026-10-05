using Hotel.Game.Content;
using Rusty.Engine;

namespace Hotel.Game.Route;

/// <summary>Reach and focus tuning shared by every world interaction.</summary>
internal sealed record InteractionTuning(float Reach, float FocusDistance, float AcquireAngle, float ReleaseAngle)
{
    internal const string Path = "route/interaction.json";
    internal static InteractionTuning Load(IEngineContext engine) => Authored.Read(engine, Path, ContentJson.Default.InteractionTuning);
}

/// <summary>One excursion's doors, readings and named rooms.</summary>
internal sealed record ExcursionRoute(string FallbackLocation, DoorDefinition[] Doors, ReadingDefinition[] Readings, RoomDefinition[] Rooms);
internal sealed record DoorDefinition(string Id, string Label, float[] Hinge, float Width, float Height,
    float ClosedYaw, float OpenYaw, string Material, string HandleMaterial, float[] FocusPoint, bool FarSideLatch = false,
    float[]? UnlockDirection = null);
internal sealed record ReadingDefinition(string Id, string Label, float[] Point, string Title, string Text);
internal sealed record RoomDefinition(string Id, string Label, float[] Min, float[] Max);
