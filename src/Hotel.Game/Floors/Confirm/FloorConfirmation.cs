using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Hotel.Game.Floors.Layout;
using Hotel.Game.Floors.Mission;
using Hotel.Game.Floors.Modules;
using Hotel.Game.Scene;
using Hotel.Game.Scene.Kit;
using Rusty.Engine;

namespace Hotel.Game.Floors.Confirm;

/// <summary>Collision navigation settings for confirming floors: cell size and how far one route query may search.</summary>
internal sealed record NavigationTuning(float CellSize, uint MaxVisited);

/// <summary>One promised route and the Engine's answer, with the first step it refused when it refused.</summary>
internal sealed record RouteVerdict(string Name, NavigationPathOutcome Outcome, string? Blocking)
{
    internal bool Reached => Outcome == NavigationPathOutcome.Reached;
    public override string ToString() => Reached ? $"{Name}=Reached" : $"{Name}={Outcome}{(Blocking is null ? "" : $" at {Blocking}")}";
}

/// <summary>The Engine's word on a floor: every promised route both ways, every lock held shut, and what it cost.</summary>
internal sealed record Confirmation(RouteVerdict[] Routes, RouteVerdict[] Locks, ulong WalkableCells, double Milliseconds)
{
    internal bool Confirmed => Routes.All(r => r.Reached) && Locks.All(l => !l.Reached);
    internal string? FirstProblem => Routes.FirstOrDefault(r => !r.Reached)?.ToString()
        ?? (Locks.FirstOrDefault(l => l.Reached) is { } open ? $"{open.Name} reached past a closed lock" : null);
}

/// <summary>
/// Proves a laid-out floor with the Engine. The built floor's solid boxes become collision in a scratch spatial
/// session; collision navigation is derived for the player's own controller; and every promised route (arrival to each
/// promised place and back) is asked of it, with the shortcut's latch held shut on the way out. Each lock is then held
/// shut as a traversal overlay, with the latch, and what it guards must become unreachable. A refused route names the first doorway crossing the Engine refuses and why. Pattern after
/// CraftSurvive's dungeon routes; see docs/reuse.md.
/// </summary>
internal static class FloorConfirmation
{
    private static readonly MissionNodeKind[] Promised =
        [MissionNodeKind.Objective, MissionNodeKind.Key, MissionNodeKind.Bell, MissionNodeKind.Landmark, MissionNodeKind.Supplies, MissionNodeKind.Shortcut];

    private const float StepUnits = 4096, Inside = 0.75f;

    internal static Confirmation Confirm(IEngineContext engine, MissionGraph graph, FloorLayout layout, FloorPlan plan, BuiltFloor floor,
        ModuleCatalog catalog, CharacterControllerConfig body, NavigationTuning tuning)
    {
        long started = Stopwatch.GetTimestamp();
        using SpatialSession session = engine.Spatial.CreateSession(new SpatialSessionConfig(.25f, 16, VoxelSurfaceMode.GreedyCubes));
        (Vector3 min, Vector3 max) = Collide(engine, session, floor.Boxes);
        CollisionNavigationConfig config = engine.Spatial.DefaultCollisionNavigationConfig() with
        {
            GridId = 1, CellSize = tuning.CellSize, Character = body,
            MaximumCells = checked((uint)((max.X - min.X) / tuning.CellSize + 2) * (uint)((max.Z - min.Z) / tuning.CellSize + 2) * 2)
        };
        CollisionNavigationReplaceReceipt navigation = engine.Spatial.ReplaceCollisionNavigation(new(session, min, max, config));

        Dictionary<string, Vector3> stands = graph.Nodes.ToDictionary(n => n.Id, n => Stand(layout, plan, floor, catalog, n), StringComparer.Ordinal);
        Vector3 arrival = stands[MissionGraph.ArrivalId];
        // The shortcut's latch opens only from the passage: going out from the stairs it is shut, coming back it is open.
        PlanarNavCell[] latch = layout.Latch is { } id ? Cells(engine, session, floor.Openings[id], tuning.CellSize).ToArray() : [];
        void Shut(IEnumerable<PlanarNavCell> cells)
        {
            NavigationTraversalCell[] closed = cells.Distinct().Select(c => new NavigationTraversalCell(c, false, 1)).ToArray();
            if (closed.Length > 0) engine.Spatial.ReplaceNavigationTraversal(new(session, closed));
            else engine.Spatial.ClearNavigationTraversal(new(session));
        }
        MissionNode[] promised = graph.Nodes.Where(n => Promised.Contains(n.Kind)).OrderBy(n => n.Id, StringComparer.Ordinal).ToArray();
        List<RouteVerdict> routes = [];
        Shut(latch);
        foreach (MissionNode node in promised)
            routes.Add(Route(engine, session, tuning, $"arrival->{node.Id}", arrival, stands[node.Id], layout, plan, floor));
        engine.Spatial.ClearNavigationTraversal(new(session));
        foreach (MissionNode node in promised)
            routes.Add(Route(engine, session, tuning, $"{node.Id}->arrival", stands[node.Id], arrival, layout, plan, floor));

        // Held shut, a lock cuts off what its mission edge guards (keys are not modelled here; the lock alone is closed).
        List<RouteVerdict> locks = [];
        foreach (LayoutLock lck in layout.Locks.OrderBy(l => l.Edge, StringComparer.Ordinal))
        {
            BuiltOpening opening = floor.Openings[lck.Link];
            PlanarNavCell[] door = Cells(engine, session, opening, tuning.CellSize).ToArray();
            if (door.Length == 0) { locks.Add(new($"{lck.Link} shut", NavigationPathOutcome.Reached, "no walkable cells in its doorway")); continue; }
            // Without this lock the player also lacks every key found only beyond it, so those keys' doors stay shut too.
            MissionProgress without = MissionReach.From(graph, MissionGraph.ArrivalId, without: lck.Edge);
            IEnumerable<PlanarNavCell> alsoShut = layout.Locks.Where(l => l != lck && !without.Items.Contains(l.Item))
                .SelectMany(l => Cells(engine, session, floor.Openings[l.Link], tuning.CellSize));
            Shut([.. door, .. latch, .. alsoShut]);
            IReadOnlySet<string> unguarded = without.Reached;
            foreach (MissionNode node in graph.Nodes.Where(n => !unguarded.Contains(n.Id) && Promised.Contains(n.Kind)).OrderBy(n => n.Id, StringComparer.Ordinal))
            {
                NavigationStepResult held = Query(engine, session, tuning, arrival, stands[node.Id]);
                locks.Add(new($"{lck.Link} shut: arrival->{node.Id}", held.Outcome, null));
            }
            engine.Spatial.ClearNavigationTraversal(new(session));
        }
        return new([.. routes], [.. locks], navigation.WalkableCellCount, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    // Every solid box as collision triangles in one asset, and the extent navigation is derived over.
    private static (Vector3 Min, Vector3 Max) Collide(IEngineContext engine, SpatialSession session, RoomBox[] boxes)
    {
        List<Vector3> vertices = [];
        List<Triangle> triangles = [];
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        uint[] faces = [4, 6, 5, 4, 7, 6, 0, 1, 2, 0, 2, 3, 0, 4, 5, 0, 5, 1, 3, 2, 6, 3, 6, 7, 0, 3, 7, 0, 7, 4, 1, 5, 6, 1, 6, 2];
        foreach (RoomBox box in boxes.Where(b => b.Solid))
        {
            Vector3 a = Authored(box.Min), b = Authored(box.Max);
            min = Vector3.Min(min, a); max = Vector3.Max(max, b);
            uint first = (uint)vertices.Count;
            vertices.AddRange([new(a.X, a.Y, a.Z), new(b.X, a.Y, a.Z), new(b.X, a.Y, b.Z), new(a.X, a.Y, b.Z),
                new(a.X, b.Y, a.Z), new(b.X, b.Y, a.Z), new(b.X, b.Y, b.Z), new(a.X, b.Y, b.Z)]);
            for (int i = 0; i < faces.Length; i += 3) triangles.Add(new(first + faces[i], first + faces[i + 1], first + faces[i + 2]));
        }
        engine.Spatial.ReplaceCollision(new(session, new StaticMeshAsset[] { new(1, 0, (uint)vertices.Count, 0, (uint)triangles.Count) },
            vertices.ToArray(), triangles.ToArray(), new StaticMeshInstance[] { new(1, 1, new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One)) }));
        return (min - Vector3.One, max + Vector3.One);
    }

    private static Vector3 Authored(float[] v) => new(v[0], v[1], v[2]);

    /// <summary>
    /// Where a place is stood at: the arrival post for arrival, the middle of a corridor place's first space, or just
    /// inside a room's door, at floor level. Furniture never stands in a corridor, and the self-check keeps a doorway's
    /// floor clear.
    /// </summary>
    private static Vector3 Stand(FloorLayout layout, FloorPlan plan, BuiltFloor floor, ModuleCatalog catalog, MissionNode node)
    {
        string placement = layout.Places[node.Id];
        ModuleDefinition module = catalog.Find(layout.Placements.First(p => p.Id == placement).Module)!;
        if (module.Sockets.FirstOrDefault(s => s.Kind == ContentSocketKind.Arrival) is { } post) return floor.Sockets[$"{placement}/{post.Socket}"] with { Y = 0 };
        SpaceDefinition space = plan.Spaces.First(s => s.Id.StartsWith(placement + "/", StringComparison.Ordinal));
        BuiltOpening? door = floor.Openings.Values.Where(o => o.Link.Between.Any(b => b.StartsWith(placement + "/", StringComparison.Ordinal)) &&
            o.Link.Between.Any(b => !b.StartsWith(placement + "/", StringComparison.Ordinal)) && !o.Link.Id.StartsWith("service/", StringComparison.Ordinal))
            .OrderBy(o => o.Link.Id, StringComparer.Ordinal).FirstOrDefault();
        bool room = node.Kind is not (MissionNodeKind.Arrival or MissionNodeKind.Hall or MissionNodeKind.Gate or MissionNodeKind.Hazard);
        if (!room || door is null) return new((space.Min[0] + space.Max[0]) / 2, 0, (space.Min[1] + space.Max[1]) / 2);
        Vector3 centre = (door.Start + door.End) / 2;
        bool intoSecond = door.Link.Between[1].StartsWith(placement + "/", StringComparison.Ordinal);
        return centre + door.Normal * (intoSecond ? Inside : -Inside);
    }

    private static NavigationStepResult Query(IEngineContext engine, SpatialSession session, NavigationTuning tuning, Vector3 from, Vector3 to) =>
        engine.Spatial.EvaluateNavigationStep(new NavigationStepRequest(session, from, to, StepUnits, tuning.MaxVisited));

    private static RouteVerdict Route(IEngineContext engine, SpatialSession session, NavigationTuning tuning, string name, Vector3 from, Vector3 to,
        FloorLayout layout, FloorPlan plan, BuiltFloor floor)
    {
        NavigationStepResult result = Query(engine, session, tuning, from, to);
        return result.Outcome == NavigationPathOutcome.Reached
            ? new(name, result.Outcome, null)
            : new(name, result.Outcome, Blocking(engine, session, tuning, from, to, plan, floor, result));
    }

    /// <summary>
    /// Follows the space-to-space route between the two points and tries each doorway crossing alone; the first the
    /// Engine refuses is named with the Engine's reading of the edge across it. When every crossing passes, the block is
    /// inside a space, and the nearest point the query reached is named instead.
    /// </summary>
    private static string Blocking(IEngineContext engine, SpatialSession session, NavigationTuning tuning, Vector3 from, Vector3 to,
        FloorPlan plan, BuiltFloor floor, NavigationStepResult result)
    {
        string? start = SpaceAt(plan, from), goal = SpaceAt(plan, to);
        if (start is not null && goal is not null)
            foreach (LinkDefinition link in Links(plan, start, goal))
            {
                if (!floor.Openings.TryGetValue(link.Id, out BuiltOpening? opening)) continue;
                Vector3 centre = (opening.Start + opening.End) / 2;
                NavigationStepResult crossing = Query(engine, session, tuning, centre - opening.Normal * Inside, centre + opening.Normal * Inside);
                if (crossing.Outcome == NavigationPathOutcome.Reached) continue;
                PlanarNavCell a = Cell(centre - opening.Normal * (tuning.CellSize / 2), tuning.CellSize), b = Cell(centre + opening.Normal * (tuning.CellSize / 2), tuning.CellSize);
                CollisionNavigationEdgeReadout edge = engine.Spatial.ExplainCollisionNavigationEdge(new(session, a, b));
                return string.Create(CultureInfo.InvariantCulture, $"doorway {link.Id} ({crossing.Outcome}; edge {edge.Outcome})");
            }
        return result.NearestPresent
            ? string.Create(CultureInfo.InvariantCulture, $"inside {SpaceAt(plan, result.Nearest) ?? "?"} near ({result.Nearest.X:0.0}, {result.Nearest.Z:0.0})")
            : "no reachable point";
    }

    // The links walked from one space to another, breadth first through every link.
    private static IEnumerable<LinkDefinition> Links(FloorPlan plan, string start, string goal)
    {
        Dictionary<string, LinkDefinition> came = new(StringComparer.Ordinal);
        Queue<string> open = new([start]);
        HashSet<string> seen = new(StringComparer.Ordinal) { start };
        while (open.TryDequeue(out string? at) && at != goal)
            foreach (LinkDefinition link in plan.Links.Where(l => l.Between.Contains(at)))
            {
                string next = link.Between[0] == at ? link.Between[1] : link.Between[0];
                if (!seen.Add(next)) continue;
                came[next] = link;
                open.Enqueue(next);
            }
        List<LinkDefinition> path = [];
        for (string at = goal; came.TryGetValue(at, out LinkDefinition? link); at = link.Between[0] == at ? link.Between[1] : link.Between[0]) path.Add(link);
        path.Reverse();
        return path;
    }

    private static string? SpaceAt(FloorPlan plan, Vector3 p) =>
        plan.Spaces.FirstOrDefault(s => p.X >= s.Min[0] && p.X <= s.Max[0] && p.Z >= s.Min[1] && p.Z <= s.Max[1])?.Id;

    private static PlanarNavCell Cell(Vector3 p, float size) =>
        new((long)MathF.Floor(p.X / size), (long)MathF.Floor(p.Y / size), (long)MathF.Floor(p.Z / size));

    // The walkable cells a closed door fills: every support the Engine finds at floor level across the doorway and a
    // cell either side of its wall line, so no path slips round the leaf.
    private static IEnumerable<PlanarNavCell> Cells(IEngineContext engine, SpatialSession session, BuiltOpening opening, float size)
    {
        HashSet<(long X, long Z)> columns = [];
        Vector3 along = opening.End - opening.Start, unit = Vector3.Normalize(along);
        int steps = Math.Max(1, (int)MathF.Ceiling((along.Length() + 2 * size) / (size / 2)));
        for (int i = 0; i <= steps; i++)
        {
            Vector3 p = opening.Start - unit * size + unit * (i * size / 2);
            foreach (float side in new[] { -size, -size / 2, 0, size / 2, size })
            {
                PlanarNavCell c = Cell(p + opening.Normal * side, size);
                columns.Add((c.X, c.Z));
            }
        }
        List<PlanarNavCell> cells = [];
        foreach (var (x, z) in columns)
            foreach (CollisionNavigationSample sample in engine.Spatial.ExplainCollisionNavigationColumn(new(session, x, z)).Samples.Span)
                if (sample.Outcome == CollisionNavigationSampleOutcome.Support && Math.Abs(sample.SurfaceY) < size) cells.Add(sample.Cell);
        return cells;
    }
}
