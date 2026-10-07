using System.Numerics;
using Hotel.Game.Scene.Kit;

namespace Hotel.Game.Floors.Modules;

/// <summary>A module placed on a floor under a stable placement id, which prefixes every id it contributes.</summary>
internal sealed record PlacedModule(string Id, ModuleTransform Transform)
{
    internal ModuleDefinition Module => Transform.Module;
    internal string Name(string local) => $"{Id}/{local}";
}

/// <summary>A placed doorway: its space, the wall it is on, its centre on that wall's centreline, and how it is built.</summary>
internal sealed record PlacedDoorway(PlacedModule Placement, DoorwayDefinition Doorway, string Space, WallEdge Edge, Vector2 Point,
    DoorwayStyle? Style)
{
    internal string Id => Placement.Name(Doorway.Id);
    internal bool AlongX => Edge is WallEdge.North or WallEdge.South;
    internal float Along => AlongX ? Point.X : Point.Y;

    /// <summary>Whether two doorways meet: the same kind, at the same point, on facing walls.</summary>
    internal bool Mates(PlacedDoorway other) => Style is not null && Doorway.Kind == other.Doorway.Kind &&
        Vector2.Distance(Point, other.Point) < 1e-3f && other.Edge == Opposite(Edge);

    internal static WallEdge Opposite(WallEdge edge) => edge switch
    {
        WallEdge.North => WallEdge.South, WallEdge.South => WallEdge.North, WallEdge.West => WallEdge.East, _ => WallEdge.West
    };
}

/// <summary>
/// Turns placed modules into one kit floor plan: each module's spaces, links, fixtures and posts moved into place
/// under its placement id, plus a link for every pair of mated doorways. Unmated doorways stay wall.
/// </summary>
internal static class ModuleRealizer
{
    internal static FloorPlan Plan(IReadOnlyList<PlacedModule> placements, ModuleCatalog catalog, string trimStyle,
        IEnumerable<SpaceDefinition>? extraSpaces = null, IEnumerable<LinkDefinition>? extraLinks = null,
        IEnumerable<FixturePlacement>? extraFixtures = null, string? decor = null)
    {
        List<SpaceDefinition> spaces = [];
        List<LinkDefinition> links = [];
        List<FixturePlacement> fixtures = [];
        foreach (PlacedModule placed in placements)
        {
            ModuleTransform t = placed.Transform;
            ModuleDefinition module = placed.Module;
            foreach (SpaceDefinition space in module.Spaces)
            {
                (float[] min, float[] max) = t.Rect(space.Min, space.Max);
                spaces.Add(space with
                {
                    Id = placed.Name(space.Id), Min = min, Max = max,
                    Posts = space.Posts?.ToDictionary(p => p.Key, p => t.Point(p.Value))
                });
            }
            foreach (LinkDefinition link in module.Links)
            {
                SpaceDefinition a = module.Spaces.First(s => s.Id == link.Between[0]), b = module.Spaces.First(s => s.Id == link.Between[1]);
                (bool alongX, float line) = SharedLine(a, b);
                links.Add(link with
                {
                    Id = placed.Name(link.Id), Between = [placed.Name(a.Id), placed.Name(b.Id)],
                    At = link.Kind == LinkKind.Open ? link.At : t.Along(alongX, link.At, line)
                });
            }
            foreach (FixturePlacement fixture in module.Fixtures)
                fixtures.Add(Place(fixture, placed, module));
        }
        PlacedDoorway[] doorways = placements.SelectMany(p => Doorways(p, catalog)).ToArray();
        for (int i = 0; i < doorways.Length; i++)
            for (int j = i + 1; j < doorways.Length; j++)
                if (doorways[i].Mates(doorways[j])) links.Add(Link(doorways[i], doorways[j].Space, JoinId(doorways[i].Id, doorways[j].Id)));
        return new([.. spaces, .. extraSpaces ?? []], [.. links, .. extraLinks ?? []], [.. fixtures, .. extraFixtures ?? []], catalog.Lighting, [], trimStyle, decor);
    }

    /// <summary>The link id of two mated doorways, the same whichever was placed first.</summary>
    internal static string JoinId(string a, string b) => string.CompareOrdinal(a, b) < 0 ? $"{a}~{b}" : $"{b}~{a}";

    /// <summary>A placement's doorways, moved into place.</summary>
    internal static IEnumerable<PlacedDoorway> Doorways(PlacedModule placed, ModuleCatalog catalog)
    {
        ModuleTransform t = placed.Transform;
        foreach (DoorwayDefinition doorway in placed.Module.Doorways)
        {
            SpaceDefinition space = placed.Module.Spaces.First(s => s.Id == doorway.Space);
            (float line, bool alongX) = doorway.Edge switch
            {
                WallEdge.North => (space.Min[1], true), WallEdge.South => (space.Max[1], true),
                WallEdge.West => (space.Min[0], false), _ => (space.Max[0], false)
            };
            Vector2 point = alongX ? t.Point(doorway.At, line) : t.Point(line, doorway.At);
            yield return new(placed, doorway, placed.Name(space.Id), t.Edge(doorway.Edge), point, catalog.Style(doorway.Kind));
        }
    }

    /// <summary>The link a doorway's style builds between its space and the space beyond it.</summary>
    internal static LinkDefinition Link(PlacedDoorway doorway, string beyond, string id)
    {
        DoorwayStyle style = doorway.Style!;
        return new(id, [doorway.Space, beyond], style.Link, style.Link == LinkKind.Open ? 0 : doorway.Along,
            style.Link == LinkKind.Open ? 0 : style.Width, style.Link == LinkKind.Open ? 0 : style.Height, Frame: style.Frame);
    }

    private static FixturePlacement Place(FixturePlacement fixture, PlacedModule placed, ModuleDefinition module)
    {
        ModuleTransform t = placed.Transform;
        FixturePlacement moved = fixture with
        {
            Id = fixture.Id is null ? null : placed.Name(fixture.Id),
            Space = fixture.Space is null ? null : placed.Name(fixture.Space),
            On = fixture.On is null ? null : placed.Name(fixture.On),
            At = fixture.At is null ? null : t.Point(fixture.At),
            Turn = fixture.Turn + t.Turn
        };
        if (fixture.Edge is not { } edge) return moved;
        SpaceDefinition space = module.Spaces.First(s => s.Id == fixture.Space);
        (float line, bool alongX) = edge switch
        {
            WallEdge.North => (space.Min[1], true), WallEdge.South => (space.Max[1], true),
            WallEdge.West => (space.Min[0], false), _ => (space.Max[0], false)
        };
        // A wall fixture's own turn is relative to its wall, which turns with the module.
        return moved with { Edge = t.Edge(edge), Along = t.Along(alongX, fixture.Along, line), Turn = fixture.Turn };
    }

    // The wall two touching spaces share: whether it runs along x, and its line coordinate.
    private static (bool AlongX, float Line) SharedLine(SpaceDefinition a, SpaceDefinition b)
    {
        const float tolerance = 1e-4f;
        if (Math.Abs(a.Max[1] - b.Min[1]) < tolerance) return (true, a.Max[1]);
        if (Math.Abs(a.Min[1] - b.Max[1]) < tolerance) return (true, a.Min[1]);
        return (false, Math.Abs(a.Max[0] - b.Min[0]) < tolerance ? a.Max[0] : a.Min[0]);
    }
}
