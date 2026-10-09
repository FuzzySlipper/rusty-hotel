using System.Numerics;
using Hotel.Game.Content;
using Hotel.Game.Route;

namespace Hotel.Game.Scene.Kit;

/// <summary>
/// A cut through a shared wall: its ends on the wall centreline at floor level, which way faces the link's second
/// space, the height of its sill and the height of the opening above that sill.
/// </summary>
internal sealed record BuiltOpening(LinkDefinition Link, Vector3 Start, Vector3 End, Vector3 Normal, float Bottom, float Height);

/// <summary>A built floor: the boxes the scene meshes and collides, its lights, addressable sockets, rooms and openings.</summary>
internal sealed record BuiltFloor(RoomBox[] Boxes, Moulding[] Mouldings, ModelDefinition[] Models, PointLightDefinition[] Lights, IReadOnlyDictionary<string, Vector3> Sockets,
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
        builder.BuildPilasters();
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

        // Whether [x, z] is within the room between its walls' inner faces.
        internal bool Inside(float x, float z, float half) =>
            x > MinX + half - Tolerance && x < MaxX - half + Tolerance && z > MinZ + half - Tolerance && z < MaxZ - half + Tolerance;

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

    // A stretch of one space's edge with no wall between Bottom and Top: an open link's whole shared wall, or one opening.
    private sealed record Cut(float From, float To, bool Open, float Bottom, float Top);

    private sealed class Builder(FloorPlan plan, string path, KitDefinition kit, FixtureCatalog catalog)
    {
        private readonly List<RoomBox> boxes = [];
        private readonly List<Moulding> mouldings = [];
        private readonly List<ModelDefinition> models = [];
        private readonly List<PointLightDefinition> lights = [];
        private readonly Dictionary<string, Vector3> sockets = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Space> socketSpaces = new(StringComparer.Ordinal);
        private readonly Dictionary<string, BuiltOpening> openings = new(StringComparer.Ordinal);
        private readonly Dictionary<(int Space, WallEdge Edge), List<Cut>> cuts = [];
        private readonly List<(LinkDefinition Link, int Index, BuiltOpening Opening)> frames = [];
        private readonly List<(string Name, Vector3 Min, Vector3 Max)> pilasters = [];
        private float Half => kit.WallThickness / 2;

        internal List<Space> Spaces { get; } = [];

        // The floor's trim style, checked when the floor builds.
        private TrimStyle Trim => kit.TrimStyles[plan.TrimStyle];

        internal void ValidateSpaces()
        {
            Authored.Require(kit.TrimStyles.ContainsKey(plan.TrimStyle), path, "trimStyle", $"unknown trim style '{plan.TrimStyle}'.");
            Authored.Require(plan.Decor is null || kit.Decors.ContainsKey(plan.Decor), path, "decor", $"unknown decor '{plan.Decor}'.");
            for (int i = 0; i < plan.Spaces.Length; i++)
            {
                SpaceDefinition s = plan.Spaces[i];
                string at = $"spaces[{i}]";
                Authored.Require(s.Min.Length == 2 && s.Min.All(float.IsFinite), path, $"{at}.min", "must be [x, z].");
                Authored.Require(s.Max.Length == 2 && s.Max.All(float.IsFinite), path, $"{at}.max", "must be [x, z].");
                Authored.Require(s.Max[0] - s.Min[0] > kit.WallThickness && s.Max[1] - s.Min[1] > kit.WallThickness, path, $"{at}.max",
                    "a space must be wider and deeper than one wall thickness.");
                Authored.Positive(path, $"{at}.height", s.Height);
                Authored.Require(kit.Styles.ContainsKey(s.Style), path, $"{at}.style", $"unknown style '{s.Style}'.");
                Space space = new(s, i, kit.Style(s.Style, plan.Decor));
                Spaces.Add(space);
                foreach (var (post, point) in s.Posts ?? [])
                {
                    Authored.Require(point.Length == 2 && point.All(float.IsFinite), path, $"{at}.posts.{post}", "must be [x, z].");
                    Authored.Require(space.Inside(point[0], point[1], Half), path, $"{at}.posts.{post}",
                        $"[{point[0]}, {point[1]}] is outside '{s.Id}'.");
                    AddSocket($"{s.Id}.{post}", new(point[0], 0, point[1]), space, $"{at}.posts.{post}");
                }
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
                    cut = new(from, to, true, 0, height);
                    OuterCorners(link, a, b, edge, from, to, height);
                }
                else
                {
                    Authored.Positive(path, $"{at}.width", link.Width);
                    float lo = link.At - link.Width / 2, hi = link.At + link.Width / 2;
                    Authored.Require(lo >= from - Tolerance && hi <= to + Tolerance, path, $"{at}.at",
                        $"the opening {lo}..{hi} must lie within the shared wall {from}..{to}.");
                    if (link.Kind == LinkKind.Hatch) Authored.Within(path, $"{at}.sill", link.Sill, Tolerance, height);
                    else Authored.Require(link.Sill == 0, path, $"{at}.sill", "only a hatch is raised on a sill.");
                    float openingHeight = link.Kind == LinkKind.Passage && link.Height == 0 ? height : link.Height;
                    Authored.Within(path, $"{at}.height", openingHeight, Tolerance, height - link.Sill);
                    if (link.Frame is { } frame)
                        Authored.Require(kit.Frames.ContainsKey(frame), path, $"{at}.frame", $"unknown frame '{frame}'.");
                    cut = new(lo, hi, false, link.Sill, link.Sill + openingHeight);
                    Vector3 start = alongX ? new(lo, 0, line) : new(line, 0, lo), end = alongX ? new(hi, 0, line) : new(line, 0, hi);
                    BuiltOpening opening = new(link, start, end, Space.Outward(edge), link.Sill, openingHeight);
                    openings.Add(link.Id, opening);
                    if (link.Frame is not null) frames.Add((link, i, opening));
                    else if (link.Kind == LinkKind.Passage) Casings(link, alongX, line, lo, hi, openingHeight);
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
            foreach (Cut cut in edgeCuts.Append(new Cut(to, to, false, 0, 0)))
            {
                if (cut.From - cursor > Tolerance)
                {
                    bool openBefore = edgeCuts.Any(c => c.Open && Math.Abs(c.To - cursor) < Tolerance) && cursor > from + Tolerance;
                    bool openAfter = cut.Open && cut.From < to - Tolerance;
                    WallRun(space, edge, line, cursor - (openBefore ? Half : 0), cut.From + (openAfter ? Half : 0), 0, space.Height, "wall");
                }
                if (!cut.Open && cut.To > cut.From && cut.Bottom > Tolerance)
                    WallRun(space, edge, line, cut.From, cut.To, 0, cut.Bottom, "sill");
                if (!cut.Open && cut.To > cut.From && cut.Top < space.Height - Tolerance)
                    WallRun(space, edge, line, cut.From, cut.To, cut.Top, space.Height, "lintel");
                cursor = Math.Max(cursor, cut.To);
            }
        }

        // One stretch of wall on this space's side, with the trim bands that start within its height.
        private void WallRun(Space space, WallEdge edge, float line, float from, float to, float bottom, float top, string piece)
        {
            string name = $"{space.Definition.Id} {edge.ToString().ToLowerInvariant()} {piece}";
            (Vector3 min, Vector3 max) = Slab(space, edge, line, from, to, bottom, top, Half);
            Add(name, min, max, space.Wall, true);
            foreach (TrimBand band in Trim.Bands[space.Style.Trim])
            {
                if (band.From < bottom - Tolerance || band.From >= top) continue;
                if (band.Profile is { } profile && band.To <= top + Tolerance)
                {
                    // Swept along the wall face, out into this space.
                    Vector3 outward = -Space.Outward(edge);
                    Vector3 Face(float along) => edge is WallEdge.North or WallEdge.South
                        ? new Vector3(along, band.From, line) + outward * Half : new Vector3(line, band.From, along) + outward * Half;
                    Vector3 a = Face(from), b = Face(to);
                    mouldings.Add(new($"{name} {band.Name}", band.Material, [a.X, a.Y, a.Z], [b.X, b.Y, b.Z], [outward.X, outward.Y, outward.Z],
                        [0, 1, 0], profile));
                    continue;
                }
                (min, max) = Slab(space, edge, line, from, to, band.From, Math.Min(band.To, top), Half + band.Depth);
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

        // Where an open link ends and only one space's wall runs on past it, that wall turns an outer corner: its face meets
        // the end of the other space's wall, half a thickness into the opening. A pilaster stands centred on that corner.
        private void OuterCorners(LinkDefinition link, Space a, Space b, WallEdge edge, float from, float to, float height)
        {
            (float line, float aFrom, float aTo, bool alongX) = a.Edge(edge);
            (_, float bFrom, float bTo, _) = b.Edge(Space.Opposite(edge));
            Vector3 along = alongX ? Vector3.UnitX : Vector3.UnitZ, outward = Space.Outward(edge), across = Vector3.Abs(outward);
            float w = kit.Pilaster.Width / 2;
            foreach (var (end, into, aRuns, bRuns) in new[] { (from, 1f, aFrom < from - Tolerance, bFrom < from - Tolerance),
                (to, -1f, aTo > to + Tolerance, bTo > to + Tolerance) })
            {
                if (aRuns == bRuns) continue;
                // On the face of the space whose wall runs on: a's face is toward a, against its outward normal.
                Vector3 corner = along * (end + into * Half) + across * line + outward * (aRuns ? -Half : Half);
                Vector3 reach = (along + across) * w;
                pilasters.Add(($"{link.Id} pilaster", corner - reach, corner + reach + Vector3.UnitY * height));
            }
        }

        // An unframed passage's sides are cased: the wall's end wrapped from the floor to the opening's top.
        private void Casings(LinkDefinition link, bool alongX, float line, float lo, float hi, float top)
        {
            float width = kit.Pilaster.Width, proud = kit.Pilaster.Proud, depth = Half + proud;
            foreach (var (from, to) in new[] { (lo - width + proud, lo + proud), (hi - proud, hi + width - proud) })
                pilasters.Add(($"{link.Id} casing", alongX ? new(from, 0, line - depth) : new(line - depth, 0, from),
                    alongX ? new(to, top, line + depth) : new(line + depth, top, to)));
        }

        internal void BuildPilasters()
        {
            foreach (var (name, min, max) in pilasters) Add(name, min, max, kit.Pilaster.Material, false);
        }

        // Frames are built once per opening, straddling the whole wall and standing proud of both faces. A raised
        // opening gets a sill piece under it.
        internal void BuildFrames()
        {
            foreach (var (link, _, opening) in frames)
            {
                FrameDefinition frame = kit.Frames[link.Frame!];
                bool alongX = Math.Abs(opening.End.X - opening.Start.X) > Tolerance;
                float lo = alongX ? opening.Start.X : opening.Start.Z, hi = alongX ? opening.End.X : opening.End.Z;
                float line = alongX ? opening.Start.Z : opening.Start.X, depth = Half + frame.Proud;
                float outer = frame.JambWidth - frame.Inset, bottom = opening.Bottom, top = bottom + opening.Height, head = top - frame.Inset;
                void Piece(string part, float from, float to, float bottom, float top) => Add($"{link.Id} {part}",
                    alongX ? new(from, bottom, line - depth) : new(line - depth, bottom, from),
                    alongX ? new(to, top, line + depth) : new(line + depth, top, to), frame.Material, false);
                float foot = bottom > 0 ? bottom + frame.Inset : 0;
                Piece("jamb", lo - outer, lo + frame.Inset, foot, head);
                Piece("jamb", hi - frame.Inset, hi + outer, foot, head);
                Piece("head", lo - outer, hi + outer, head, top + frame.HeadHeight);
                if (bottom > 0) Piece("sill", lo - outer, hi + outer, Math.Max(0, bottom - frame.HeadHeight), foot);
                // The floor's trim style may dress this frame with an architrave on both wall faces: up each jamb from the
                // floor (or sill) and across the head, the head running past the jambs so the corners meet.
                if (Trim.Architraves?.GetValueOrDefault(link.Frame!) is not { } architrave) continue;
                float width = architrave.Profile.Max(p => p[1]), crown = top + frame.HeadHeight;
                Vector3 alongAxis = alongX ? Vector3.UnitX : Vector3.UnitZ, normal = alongX ? Vector3.UnitZ : Vector3.UnitX;
                Vector3 At(float along, float y, float side) => alongAxis * along + Vector3.UnitY * y + normal * (line + side * Half);
                foreach (float side in new[] { -1f, 1f })
                {
                    Vector3 outward = normal * side;
                    void Run(string part, Vector3 a, Vector3 b, Vector3 across) => mouldings.Add(new($"{link.Id} {part}", architrave.Material,
                        [a.X, a.Y, a.Z], [b.X, b.Y, b.Z], [outward.X, outward.Y, outward.Z], [across.X, across.Y, across.Z], architrave.Profile));
                    Run("architrave", At(lo - outer, foot, side), At(lo - outer, crown, side), -alongAxis);
                    Run("architrave", At(hi + outer, foot, side), At(hi + outer, crown, side), alongAxis);
                    Run("architrave head", At(lo - outer - width, crown, side), At(hi + outer + width, crown, side), Vector3.UnitY);
                }
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
                (Vector3 origin, int turn, Space space, string field) = Origin(fixture!, placed, at);
                bool showsFind = fixture!.ShowsFind;
                Authored.Require(showsFind == placed.Find is not null, path, $"{at}.find", showsFind
                    ? $"'{placed.Kind}' shows a find; name the find it shows."
                    : $"'{placed.Kind}' has no part that shows a find.");
                string label = placed.Id ?? placed.Kind;
                if (placed.Flicker is { } flicker)
                {
                    Authored.Require((fixture.Lights ?? []).Length > 0, path, $"{at}.flicker", $"'{placed.Kind}' has no light to flicker.");
                    Authored.Within(path, $"{at}.flicker.depth", flicker.Depth, 0, 1);
                    Authored.Positive(path, $"{at}.flicker.seconds", flicker.Seconds);
                }
                foreach (FixturePart part in fixture.Parts)
                {
                    (Vector3 min, Vector3 max) = KitTransform.Box(part.Min, part.Max, origin, turn, placed.Mirror);
                    // Wall fixtures sit on the wall's face, so their footprint is checked against the face itself.
                    float inset = fixture.Mount == FixtureMount.Wall ? Half - Tolerance : Half;
                    Authored.Require(space.Inside(min.X, min.Z, inset) && space.Inside(max.X, max.Z, inset), path, field,
                        $"'{placed.Kind}' part '{part.Name}' reaches {min.X}..{max.X}, {min.Z}..{max.Z}, outside '{space.Definition.Id}'.");
                    Add($"{label} {part.Name}", min, max, part.Material, part.Solid, part.Find ? placed.Find : null, part.Collider);
                }
                // A model turns with its fixture; mirroring does not flip it, so a mirrored fixture's model should be symmetric.
                foreach (FixtureModel model in fixture.Models ?? [])
                {
                    Vector3 position = KitTransform.Point(model.Offset, origin, turn, placed.Mirror);
                    models.Add(new(model.Path, [position.X, position.Y, position.Z], model.Scale, turn * 90 + model.YawDegrees,
                        model.Find ? placed.Find : null));
                }
                foreach (FixtureLight light in fixture.Lights ?? [])
                {
                    Vector3 position = KitTransform.Point(light.Offset, origin, turn, placed.Mirror);
                    lights.Add(new([position.X, position.Y, position.Z], light.Color,
                        placed.Intensity ?? light.Intensity, placed.Range ?? light.Range, light.Shadow, placed.Flicker));
                }
                if (placed.Id is not null)
                    foreach (var (socket, point) in fixture.Sockets ?? [])
                        AddSocket($"{placed.Id}.{socket}", KitTransform.Point(point, origin, turn, placed.Mirror), space, $"{at}.id");
            }
        }

        // Where the fixture's frame sits, the space it must stay inside, and the field that placed it there.
        private (Vector3 Origin, int Turn, Space Space, string Field) Origin(FixtureDefinition fixture, FixturePlacement placed, string at)
        {
            if (fixture.Mount == FixtureMount.Socket)
            {
                Authored.Require(placed.On is not null && sockets.ContainsKey(placed.On), path, $"{at}.on",
                    $"'{placed.Kind}' goes on an earlier fixture's socket; '{placed.On}' is not one.");
                return (sockets[placed.On!], placed.Turn, socketSpaces[placed.On!], $"{at}.on");
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
                return (alongX ? new(placed.Along, 0, face) : new(face, 0, placed.Along), KitTransform.WallTurn(edge) + placed.Turn,
                    space, $"{at}.along");
            }
            Authored.Require(placed.At is { Length: 2 }, path, $"{at}.at", $"'{placed.Kind}' needs [x, z] within its space.");
            float x = placed.At![0], z = placed.At[1];
            Authored.Require(space.Inside(x, z, Half), path, $"{at}.at", $"[{x}, {z}] is outside '{space.Definition.Id}'.");
            return (new(x, fixture.Mount == FixtureMount.Ceiling ? space.Height : 0, z), placed.Turn, space, $"{at}.at");
        }

        private void AddSocket(string name, Vector3 point, Space space, string field)
        {
            Authored.Require(sockets.TryAdd(name, point), path, field, $"socket '{name}' is defined more than once.");
            socketSpaces[name] = space;
        }

        internal BuiltFloor Result() => new(boxes.ToArray(), mouldings.ToArray(), models.ToArray(), lights.ToArray(), sockets, Spaces.Select(s => new RoomDefinition(
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

        private void Add(string name, Vector3 min, Vector3 max, string material, bool solid, string? find = null, bool hidden = false) =>
            boxes.Add(new(name, [min.X, min.Y, min.Z], [max.X, max.Y, max.Z], material, solid, find, hidden));
    }
}
