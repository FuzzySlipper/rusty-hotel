using System.Text.Json.Serialization;
using Hotel.Game.Scene.Kit;

namespace Hotel.Game.Floors.Modules;

/// <summary>
/// How a module meets its neighbours. Corridor and service doors, fire doors and archways are openings in the
/// module's outer wall that mate with a neighbour's doorway of the same kind. A stair is the module's way to other
/// floors: it marks where the flight leaves, and cuts no wall.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<DoorwayKind>))]
internal enum DoorwayKind { CorridorDoor, ServiceDoor, FireDoor, Archway, Stair }

/// <summary>What a content socket offers to floor content placement. Arrival is where the player stands on entering a floor.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ContentSocketKind>))]
internal enum ContentSocketKind { Find, ResidentPost, Reading, Landmark, Bell, Notebook, Arrival }

/// <summary>What a module is, for the floor grammar choosing where it may go.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ModuleTag>))]
internal enum ModuleTag { Corridor, Guest, Service, Public, Refuge, Landmark, StairCore, DeadEnd, SetPieceOnce, Keepable }

/// <summary>
/// One room module: kit spaces, links and fixtures in its own frame, authored facing north. Its frame runs from
/// [0, 0] at the north-west corner to <see cref="Size"/> [width, depth], on wall centrelines and on the authoring
/// lattice. Doorways sit on its outer wall; content sockets name fixture sockets or space posts and say what they offer.
/// </summary>
internal sealed record ModuleDefinition(string Id, float[] Size, ModuleTag[] Tags, SpaceDefinition[] Spaces, LinkDefinition[] Links,
    FixturePlacement[] Fixtures, DoorwayDefinition[] Doorways, ContentSocket[] Sockets)
{
    internal float Width => Size[0];
    internal float Depth => Size[1];
}

/// <summary>A doorway on <see cref="Space"/>'s <see cref="Edge"/>, which must be the module's outer wall, centred <see cref="At"/> along it.</summary>
internal sealed record DoorwayDefinition(string Id, DoorwayKind Kind, string Space, WallEdge Edge, float At);

/// <summary>A place content can go: a fixture socket ("desk.top") or a space post ("room.post").</summary>
internal sealed record ContentSocket(string Id, ContentSocketKind Kind, string Socket);

/// <summary>The size and link every doorway of one kind is built with, so any two that mate agree.</summary>
internal sealed record DoorwayStyle(DoorwayKind Kind, LinkKind Link, float Width, float Height, string? Frame = null);

/// <summary>
/// The module catalog's own file: the authoring lattice, generated floors' ambient light, how each doorway kind is
/// built, and which module files exist.
/// </summary>
internal sealed record ModuleCatalogFile(float Cell, AmbientLighting Lighting, DoorwayStyle[] Doorways, string[] Modules);
