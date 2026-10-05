using System.Text.Json.Serialization;

namespace Hotel.Game.Scene.Kit;

/// <summary>
/// One floor authored in the kit's terms: spaces on wall centrelines, the links between adjoining spaces, and
/// the fixtures placed in them. The kit builder turns it into boxes, lights, sockets and named rooms.
/// </summary>
internal sealed record FloorPlan(SpaceDefinition[] Spaces, LinkDefinition[] Links, FixturePlacement[] Fixtures,
    AmbientLighting Lighting, ModelDefinition[] Models);

/// <summary>
/// A room or corridor. <see cref="Min"/> and <see cref="Max"/> are [x, z] on the wall centrelines; walls are built
/// half on each side. Any surface may override the style's.
/// </summary>
internal sealed record SpaceDefinition(string Id, string Label, float[] Min, float[] Max, float Height, string Style,
    string? Floor = null, string? Wall = null, string? Ceiling = null);

/// <summary>
/// What joins two spaces along their shared wall. <see cref="LinkKind.Open"/> removes the whole shared wall; a door
/// or passage cuts one opening centred at <see cref="At"/> along it. A passage with no height is open to the ceiling.
/// </summary>
internal sealed record LinkDefinition(string Id, string[] Between, LinkKind Kind, float At = 0, float Width = 0, float Height = 0,
    string? Frame = null);

[JsonConverter(typeof(JsonStringEnumConverter<LinkKind>))]
internal enum LinkKind { Open, Door, Passage }

[JsonConverter(typeof(JsonStringEnumConverter<WallEdge>))]
internal enum WallEdge { North, South, West, East }

/// <summary>
/// One fixture in a space. Floor and ceiling fixtures stand at <see cref="At"/> [x, z]; wall fixtures sit on a
/// <see cref="Edge"/> of the space at <see cref="Along"/>; socket fixtures go <see cref="On"/> another fixture's
/// socket ("instance.socket"). <see cref="Turn"/> is quarter turns; <see cref="Mirror"/> flips the fixture's x.
/// </summary>
internal sealed record FixturePlacement(string Kind, string? Id = null, string? Space = null, WallEdge? Edge = null,
    float Along = 0, float[]? At = null, string? On = null, int Turn = 0, bool Mirror = false, string? Find = null,
    float? Intensity = null, float? Range = null);

internal sealed record AmbientLighting(float[] AmbientColor, float AmbientIntensity);
