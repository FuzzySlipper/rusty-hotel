using System.Text.Json.Serialization;

namespace Hotel.Game.Scene.Kit;

/// <summary>
/// One floor authored in the kit's terms: spaces on wall centrelines, the links between adjoining spaces, and
/// the fixtures placed in them, dressed in one of the kit's trim styles and furnished in one of its decors (none keeps
/// each space style's own surfaces). The kit builder turns it into boxes, mouldings, lights,
/// sockets and named rooms.
/// </summary>
internal sealed record FloorPlan(SpaceDefinition[] Spaces, LinkDefinition[] Links, FixturePlacement[] Fixtures,
    AmbientLighting Lighting, ModelDefinition[] Models, string TrimStyle, string? Decor = null);

/// <summary>
/// A room or corridor. <see cref="Min"/> and <see cref="Max"/> are [x, z] on the wall centrelines; walls are built
/// half on each side. Any surface may override the style's. <see cref="Posts"/> are named [x, z] floor points,
/// addressed as "space.post": where residents stand and other placements that belong to the room, not a fixture.
/// </summary>
internal sealed record SpaceDefinition(string Id, string Label, float[] Min, float[] Max, float Height, string Style,
    string? Floor = null, string? Wall = null, string? Ceiling = null, Dictionary<string, float[]>? Posts = null);

/// <summary>
/// What joins two spaces along their shared wall. <see cref="LinkKind.Open"/> removes the whole shared wall; the
/// other kinds cut one opening centred at <see cref="At"/> along it. A passage with no height is open to the ceiling.
/// A hatch is raised off the floor: its opening starts at <see cref="Sill"/> and is <see cref="Height"/> tall.
/// </summary>
internal sealed record LinkDefinition(string Id, string[] Between, LinkKind Kind, float At = 0, float Width = 0, float Height = 0,
    float Sill = 0, string? Frame = null);

/// <summary>
/// Door: a doorway from the floor that a route door can hang in. Passage: an archway from the floor. Hatch: a
/// serving or crawl-through opening above a sill. Open: no wall between the spaces.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<LinkKind>))]
internal enum LinkKind { Open, Door, Passage, Hatch }

[JsonConverter(typeof(JsonStringEnumConverter<WallEdge>))]
internal enum WallEdge { North, South, West, East }

/// <summary>
/// One fixture in a space. Floor and ceiling fixtures stand at <see cref="At"/> [x, z]; wall fixtures sit on a
/// <see cref="Edge"/> of the space at <see cref="Along"/>; socket fixtures go <see cref="On"/> another fixture's
/// socket ("instance.socket"). <see cref="Turn"/> is quarter turns; <see cref="Mirror"/> flips the fixture's x.
/// </summary>
internal sealed record FixturePlacement(string Kind, string? Id = null, string? Space = null, WallEdge? Edge = null,
    float Along = 0, float[]? At = null, string? On = null, int Turn = 0, bool Mirror = false, string? Find = null,
    float? Intensity = null, float? Range = null, LightFlicker? Flicker = null);

internal sealed record AmbientLighting(float[] AmbientColor, float AmbientIntensity);
