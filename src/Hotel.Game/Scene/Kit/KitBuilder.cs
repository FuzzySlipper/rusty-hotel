using System.Numerics;
using Hotel.Game.Content;
using Hotel.Game.Route;

namespace Hotel.Game.Scene.Kit;

/// <summary>A cut through a shared wall: its ends on the wall centreline, which way faces the link's second space, and its height.</summary>
internal sealed record BuiltOpening(LinkDefinition Link, Vector3 Start, Vector3 End, Vector3 Normal, float Height);

/// <summary>A built floor: the boxes the scene meshes and collides, its lights, addressable sockets, rooms and openings.</summary>
internal sealed record BuiltFloor(RoomBox[] Boxes, PointLightDefinition[] Lights, IReadOnlyDictionary<string, Vector3> Sockets,
    RoomDefinition[] Rooms, IReadOnlyDictionary<string, BuiltOpening> Openings);

/// <summary>
/// Turns a floor plan into geometry. Walls are derived, not authored: each space builds the half of every wall on
/// its own side, in its own wall surface, so a partition shows each room's wallpaper. Links remove a shared wall or
/// cut an opening, with a lintel and continuing upper trim above, and an optional frame.
/// </summary>
internal static class KitBuilder
{
    private const float Tolerance = 1e-4f;

    internal static BuiltFloor Build(FloorPlan plan, string path, KitDefinition kit, FixtureCatalog catalog)
    {
        Builder builder = new(plan, path, kit, catalog);
        builder.ValidateSpaces();
        builder.ResolveLinks();
        foreach (Space space in builder.Spaces) builder.BuildSpace(space);
        builder.BuildFrames();
        builder.PlaceFixtures();
        return builder.Result();
    }

    private sealed class Space(SpaceDefinition definition, int index, SpaceStyle style)
    {
        internal SpaceDefinition Definition { get; } = definition;
        internal int Index { get; } = index;
        internal float MinX => Definition.Min[0];
        internal float MinZ => Definition.Min[1];
        internal float MaxX => Definition.Max[0];
        internal float MaxZ => Definition.Max[1];
        internal float Height => Definition.Height;
        internal string Wall => Definition.Wall ?? Style.Wall;
        internal string Floor => Definition.Floor ?? Style.Floor;
        internal string Ceiling => Definition.Ceiling ?? Style.Ceiling;
        internal SpaceStyle Style { get; } = style;

        // An edge's line coordinate, its extent along the line, and whether that line runs along x.
        internal (float Line, float From, float To, bool AlongX) Edge(WallEdge edge) => edge switch
        {
            WallEdge.North => (MinZ, MinX, MaxX, true),
            WallEdge.South => (MaxZ, MinX, MaxX, true),
            WallEdge.West => (MinX, MinZ, MaxZ, false),
            _ => (MaxX, MinZ, MaxZ, false)
        };

        internal static WallEdge Opposite(WallEdge edge) => edge switch
        {
            WallEdge.North => WallEdge.South, WallEdge.South => WallEdge.North, WallEdge.West => WallEdge.East, _ => WallEdge.West
        };

        // Unit normal pointing out of the space through this edge.
        internal static Vector3 Outward(WallEdge edge) => edge switch
        {
            WallEdge.North => -Vector3.UnitZ, WallEdge.South => Vector3.UnitZ, WallEdge.West => -Vector3.UnitX, _ => Vector3.UnitX
        };
    }

    // A stretch of one space's edge with no wall: an open link's whole shared wall, or one opening.
    private sealed record Cut(float From, float To, bool Open, float Height);

    private sealed class Builder(FloorPlan plan, string path, KitDefinition kit, FixtureCatalog catalog)
    {
        private readonly List<RoomBox> boxes = [];
        private readonly List<PointLightDefinition> lights = [];
        private readonly Dictionary<string, Vector3> sockets = new(StringComparer.Ordinal);
        private readonly Dictionary<string, BuiltOpening> openings = new(StringComparer.Ordinal);
        private readonly Dictionary<(int Space, WallEdge Edge), List<Cut>> cuts = [];
        private readonly List<(LinkDefinition Link, int Index, BuiltOpening Opening)> frames = [];
        private float Half => kit.WallThickness / 2;

        internal List<Space> Spaces { get; } = [];

        internal void ValidateSpaces()
        {
            for (int i = 0; i < plan.Spaces.Length; i++)
            {
                SpaceDefinition s = plan.Spaces[i];
                string at = $"spaces[{i}]";
                Authored.Require(s.Min.Length == 2 && s.Min.All(float.IsFinite), path, $"{at}.min", "must be [x, z].");
                Authored.Require(s.Max.Length == 2 && s.Max.All(float.IsFinite), path, $"{at}.max", "must be [x, z].");
                Authored.Require(s.Max[0] - s.Min[0] > kit.WallThickness && s.Max[1] - s.Min[1] > kit.WallThickness, path, $"{at}.max",
                    "a space must be wider and deeper than one wall thickness.");
                Authored.Positive(path, $"{at}.height", s.Height);
                Authored.Require(kit.Styles.TryGetValue(s.Style, out SpaceStyle? style), path, $"{at}.style", $"unknown style '{s.Style}'.");
                Spaces.Add(new(s, i, style!));
            }
            string? repeated = plan.Spaces.GroupBy(s => s.Id).FirstOrDefault(g => g.Count() > 1)?.Key;
            Authored.Require(repeated is null, path, "spaces", $"id '{repeated}' appears more than once.");
            foreach (Space a in Spaces)
                foreach (Space b in Spaces.Where(b => b.Index > a.Index))
                    Authored.Require(Math.Min(a.MaxX, b.MaxX) - Math.Max(a.MinX, b.MinX) <= Tolerance ||
                        Math.Min(a.MaxZ, b.MaxZ) - Math.Max(a.MinZ, b.MinZ) <= Tolerance, path, $"spaces[{b.Index}]",
                        $"'{b.Definition.Id}' overlaps '{a.Definition.Id}'.");
        }

        internal void ResolveLinks()
        {
            for (int i = 0; i < plan.Links.Length; i++)
            {
                LinkDefinition link = plan.Links[i];
                string at = $"links[{i}]";
                Authored.Require(link.Between.Length == 2 && link.Between[0] != link.Between[1], path, $"{at}.between", "must name two different spaces.");
                Space a = Find(link.Between[0], $"{at}.between[0]"), b = Find(link.Between[1], $"{at}.between[1]");
                var shared = SharedEdges(a, b).ToArray();
                Authored.Require(shared.Length == 1, path, $"{at}.between",
                    $"'{a.Definition.Id}' and '{b.Definition.Id}' must share exactly one wall; found {shared.Length}.");
                var (edge, from, to) = shared[0];
                (float line, _, _, bool alongX) = a.Edge(edge);
                float height = Math.Min(a.Height, b.Height);
                Cut cut;
                if (link.Kind == LinkKind.Open)
                {
                    Authored.Require(plan.Links.Count(l => SamePair(l, link)) == 1, path, $"{at}.kind",
                        "an open link removes the whole shared wall, so it must be the pair's only link.");
                    cut = new(from, to, true, height);
                }
                else
                {
                    Authored.Positive(path, $"{at}.width", link.Width);
                    float lo = link.At - link.Width / 2, hi = link.At + link.Width / 2;
                    Authored.Require(lo >= from - Tolerance && hi <= to + Tolerance, path, $"{at}.at",
                        $"the opening {lo}..{hi} must lie within the shared wall {from}..{to}.");
                    float openingHeight = link.Kind == LinkKind.Passage && link.Height == 0 ? height : link.Height;
                    Authored.Within(path, $"{at}.height", openingHeight, Tolerance, height);
                    if (link.Frame is { } frame)
                        Authored.Require(kit.Frames.ContainsKey(frame), path, $"{at}.frame", $"unknown frame '{frame}'.");
                    cut = new(lo, hi, false, openingHeight);
                    Vector3 start = alongX ? new(lo, 0, line) : new(line, 0, lo), end = alongX ? new(hi, 0, line) : new(line, 0, hi);
                    BuiltOpening opening = new(link, start, end, Space.Outward(edge), openingHeight);
                    openings.Add(link.Id, opening);
                    if (link.Frame is not null) frames.Add((link, i, opening));
                }
                foreach (var (space, side) in new[] { (a, edge), (b, Space.Opposite(edge)) })
                {
                    List<Cut> list = cuts.TryGetValue((space.Index, side), out var found) ? found : cuts[(space.Index, side)] = [];
                    Authored.Require(!list.Any(c => c.From < cut.To - Tolerance && cut.From < c.To - Tolerance), path, $"{at}.at",
                        $"overlaps another opening in '{space.Definition.Id}'.");
                    list.Add(cut);
                }
            }
            string? repeated = plan.Links.GroupBy(l => l.Id).FirstOrDefault(g => g.Count() > 1)?.Key;
            Authored.Require(repeated is null, path, "links", $"id '{repeated}' appears more than once.");
        }

        internal void BuildSpace(Space space)
        {
            string name = space.Definition.Id;
            Add($"{name} floor", new(space.MinX, -kit.FloorThickness, space.MinZ), new(space.MaxX, 0, space.MaxZ), space.Floor, true);
            Add($"{name} ceiling", new(space.MinX, space.Height, space.MinZ),
                new(space.MaxX, space.Height + kit.CeilingThickness, space.MaxZ), space.Ceiling, true);
            foreach (WallEdge edge in Enum.GetValues<WallEdge>()) BuildEdge(space, edge);
            if (space.Style.Seams is { } seams) BuildSeams(space, seams);
        }

        // A wall runs the whole edge except its cuts. Where a wall stops at an open link it continues half a
        // thickness, closing the corner it would otherwise leave against the neighbour's wall.
        private void BuildEdge(Space space, WallEdge edge)
        {
            (float line, float from, float to, _) = space.Edge(edge);
            List<Cut> edgeCuts = (cuts.TryGetValue((space.Index, edge), out var found) ? found : []).OrderBy(c => c.From).ToList();
            float cursor = from;
            foreach (Cut cut in edgeCuts.Append(new Cut(to, to, false, 0)))
            {
                if (cut.From - cursor > Tolerance)
                {
                    bool openBefore = edgeCuts.Any(c => c.Open && Math.Abs(c.To - cursor) < Tolerance) && cursor > from + Tolerance;
                    bool openAfter = cut.Open && cut.From < to - Tolerance;
                    WallRun(space, edge, line, cursor - (openBefore ? Half : 0), cut.From + (openAfter ? Half : 0), 0, space.Height, false);
                }
                if (!cut.Open && cut.To > cut.From && cut.Height < space.Height - Tolerance)
                    WallRun(space, edge, line, cut.From, cut.To, cut.Height, space.Height, true);
                cursor = Math.Max(cursor, cut.To);
            }
        }

        // One stretch of wall on this space's side, with the trim bands that fit within its height.
        private void WallRun(Space space, WallEdge edge, float line, float from, float to, float bottom, float top, bool lintel)
        {
            string name = $"{space.Definition.Id} {edge.ToString().ToLowerInvariant()} {(lintel ? "lintel" : "wall")}";
            (Vector3 min, Vector3 max) = Slab(space, edge, line, from, to, bottom, top, Half);
            Add(name, min, max, space.Wall, true);
            foreach (TrimBand band in kit.TrimSets[space.Style.Trim])
            {
                if (band.From < bottom - Tolerance || band.From >= space.Height) continue;
                (min, max) = Slab(space, edge, line, from, to, band.From, Math.Min(band.To, space.Height), Half + band.Depth);
                Add($"{name} {band.Name}", min, max, band.Material, false);
            }
        }

        // A box against the centreline on the space's own side, reaching `depth` into the space.
        private static (Vector3, Vector3) Slab(Space space, WallEdge edge, float line, float from, float to, float bottom, float top, float depth)
        {
            float inward = edge is WallEdge.North or WallEdge.West ? depth : -depth;
            float a = Math.Min(line, line + inward), b = Math.Max(line, line + inward);
            return edge is WallEdge.North or WallEdge.South
                ? (new(from, bottom, a), new(to, top, b))
                : (new(a, bottom, from), new(b, top, to));
        }

        private void BuildSeams(Space space, SeamDefinition seams)
        {
            bool alongZ = space.MaxZ - space.MinZ >= space.MaxX - space.MinX;
            float lo = (alongZ ? space.MinZ : space.MinX) + Half, hi = (alongZ ? space.MaxZ : space.MaxX) - Half;
            for (float p = MathF.Ceiling(lo / seams.Spacing) * seams.Spacing; p + seams.Width < hi; p += seams.Spacing)
            {
                if (p <= lo + Tolerance) continue;
                Vector3 min = alongZ ? new(space.MinX + Half, space.Height - seams.Depth, p) : new(p, space.Height - seams.Depth, space.MinZ + Half);
                Vector3 max = alongZ ? new(space.MaxX - Half, space.Height, p + seams.Width) : new(p + seams.Width, space.Height, space.MaxZ - Half);
                Add($"{space.Definition.Id} ceiling seam", min, max, seams.Material, false);
            }
        }

        // Frames are built once per door, straddling the whole wall and standing proud of both faces.
        internal void BuildFrames()
        {
            foreach (var (link, _, opening) in frames)
            {
                FrameDefinition frame = kit.Frames[link.Frame!];
                bool alongX = Math.Abs(opening.End.X - opening.Start.X) > Tolerance;
                float lo = alongX ? opening.Start.X : opening.Start.Z, hi = alongX ? opening.End.X : opening.End.Z;
                float line = alongX ? opening.Start.Z : opening.Start.X, depth = Half + frame.Proud;
                float outer = frame.JambWidth - frame.Inset, head = opening.Height - frame.Inset;
                void Piece(string part, float from, float to, float bottom, float top) => Add($"{link.Id} {part}",
                    alongX ? new(from, bottom, line - depth) : new(line - depth, bottom, from),
                    alongX ? new(to, top, line + depth) : new(line + depth, top, to), frame.Material, false);
                Piece("jamb", lo - outer, lo + frame.Inset, 0, head);
                Piece("jamb", hi - frame.Inset, hi + outer, 0, head);
                Piece("head", lo - outer, hi + outer, head, opening.Height + frame.HeadHeight);
            }
        }

        internal void PlaceFixtures()
        {
            Dictionary<string, FixtureDefinition> kinds = catalog.Fixtures.ToDictionary(f => f.Id, StringComparer.Ordinal);
            HashSet<string> ids = new(StringComparer.Ordinal);
            for (int i = 0; i < plan.Fixtures.Length; i++)
            {
                FixturePlacement placed = plan.Fixtures[i];
                string at = $"fixtures[{i}]";
                Authored.Require(kinds.TryGetValue(placed.Kind, out FixtureDefinition? fixture), path, $"{at}.kind", $"unknown fixture '{placed.Kind}'.");
                if (placed.Id is { } id) Authored.Require(ids.Add(id), path, $"{at}.id", $"id '{id}' appears more than once.");
                (Vector3 origin, int turn) = Origin(fixture!, placed, at);
                bool showsFind = fixture!.Parts.Any(p => p.Find);
                Authored.Require(showsFind == placed.Find is not null, path, $"{at}.find", showsFind
                    ? $"'{placed.Kind}' shows a find; name the find it shows."
                    : $"'{placed.Kind}' has no part that shows a find.");
                string label = placed.Id ?? placed.Kind;
                foreach (FixturePart part in fixture.Parts)
                {
                    (Vector3 min, Vector3 max) = KitTransform.Box(part.Min, part.Max, origin, turn, placed.Mirror);
                    Add($"{label} {part.Name}", min, max, part.Material, part.Solid, part.Find ? placed.Find : null);
                }
                foreach (FixtureLight light in fixture.Lights ?? [])
                {
                    Vector3 position = KitTransform.Point(light.Offset, origin, turn, placed.Mirror);
                    lights.Add(new([position.X, position.Y, position.Z], light.Color,
                        placed.Intensity ?? light.Intensity, placed.Range ?? light.Range));
                }
                if (placed.Id is not null)
                    foreach (var (socket, point) in fixture.Sockets ?? [])
                        sockets[$"{placed.Id}.{socket}"] = KitTransform.Point(point, origin, turn, placed.Mirror);
            }
        }

        private (Vector3 Origin, int Turn) Origin(FixtureDefinition fixture, FixturePlacement placed, string at)
        {
            if (fixture.Mount == FixtureMount.Socket)
            {
                Authored.Require(placed.On is not null && sockets.ContainsKey(placed.On), path, $"{at}.on",
                    $"'{placed.Kind}' goes on an earlier fixture's socket; '{placed.On}' is not one.");
                return (sockets[placed.On!], placed.Turn);
            }
            Authored.Require(placed.Space is not null, path, $"{at}.space", $"'{placed.Kind}' needs the space it stands in.");
            Space space = Find(placed.Space!, $"{at}.space");
            if (fixture.Mount == FixtureMount.Wall)
            {
                Authored.Require(placed.Edge is not null, path, $"{at}.edge", $"'{placed.Kind}' is wall-mounted; name the wall edge.");
                WallEdge edge = placed.Edge!.Value;
                (float line, float from, float to, bool alongX) = space.Edge(edge);
                Authored.Within(path, $"{at}.along", placed.Along, from + Half, to - Half);
                float face = line + (edge is WallEdge.North or WallEdge.West ? Half : -Half);
                return (alongX ? new(placed.Along, 0, face) : new(face, 0, placed.Along), KitTransform.WallTurn(edge) + placed.Turn);
            }
            Authored.Require(placed.At is { Length: 2 }, path, $"{at}.at", $"'{placed.Kind}' needs [x, z] within its space.");
            float x = placed.At![0], z = placed.At[1];
            Authored.Require(x > space.MinX + Half && x < space.MaxX - Half && z > space.MinZ + Half && z < space.MaxZ - Half,
                path, $"{at}.at", $"[{x}, {z}] is outside '{space.Definition.Id}'.");
            return (new(x, fixture.Mount == FixtureMount.Ceiling ? space.Height : 0, z), placed.Turn);
        }

        internal BuiltFloor Result() => new(boxes.ToArray(), lights.ToArray(), sockets, Spaces.Select(s => new RoomDefinition(
            s.Definition.Id, s.Definition.Label, [s.MinX, 0, s.MinZ], [s.MaxX, s.Height, s.MaxZ])).ToArray(), openings);

        private Space Find(string id, string field)
        {
            Space? space = Spaces.FirstOrDefault(s => s.Definition.Id == id);
            Authored.Require(space is not null, path, field, $"unknown space '{id}'.");
            return space!;
        }

        // Edges where `a` meets `b` face to face, with the overlapping stretch.
        private IEnumerable<(WallEdge Edge, float From, float To)> SharedEdges(Space a, Space b)
        {
            foreach (WallEdge edge in Enum.GetValues<WallEdge>())
            {
                (float line, float from, float to, _) = a.Edge(edge);
                (float otherLine, float otherFrom, float otherTo, _) = b.Edge(Space.Opposite(edge));
                if (Math.Abs(line - otherLine) > Tolerance) continue;
                float lo = Math.Max(from, otherFrom), hi = Math.Min(to, otherTo);
                if (hi - lo > Tolerance) yield return (edge, lo, hi);
            }
        }

        private static bool SamePair(LinkDefinition a, LinkDefinition b) =>
            a.Between.Order().SequenceEqual(b.Between.Order());

        private void Add(string name, Vector3 min, Vector3 max, string material, bool solid, string? find = null) =>
            boxes.Add(new(name, [min.X, min.Y, min.Z], [max.X, max.Y, max.Z], material, solid, find));
    }
}
