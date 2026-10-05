using System.Numerics;
using Hotel.Game.Floors.Modules;
using Hotel.Game.Scene.Kit;

namespace Hotel.Game.Floors.Layout;

/// <summary>
/// The back-of-house passage a shortcut returns by: generated kit spaces, not modules, so it can run between any two
/// service doors on the lattice. It leaves the first door, finds its way round what is already placed, and enters the
/// second; every leg and corner is a space, open to the next, and the door at its end is the latch.
/// </summary>
internal static class ServicePassage
{
    internal sealed record Route(SpaceDefinition[] Spaces, LinkDefinition[] Links, FixturePlacement[] Fixtures, string Latch);

    private const float Tolerance = 1e-3f;

    /// <summary>
    /// The cheapest clear route on the lattice, counting each turn as <see cref="PassageTuning.TurnCost"/> steps, with
    /// straight runs between turns at least the passage's width so corners never crowd. Null when none fits.
    /// </summary>
    internal static Route? Find(PlacedDoorway from, PlacedDoorway to, IReadOnlyList<(Vector2 Min, Vector2 Max)> taken, LayoutTuning tuning,
        float cell)
    {
        float half = tuning.Passage.Width / 2;
        Vector2 outFrom = Outward(from.Edge), outTo = Outward(to.Edge);
        Vector2 start = from.Point + outFrom * half, goal = to.Point + outTo * half;
        Vector2[] steps = [Vector2.UnitX, -Vector2.UnitX, Vector2.UnitY, -Vector2.UnitY];
        int minRun = (int)MathF.Round(tuning.Passage.Width / cell);
        (int X, int Z) Key(Vector2 p) => ((int)MathF.Round((p.X - start.X) / cell), (int)MathF.Round((p.Y - start.Y) / cell));
        Vector2 At((int X, int Z) k) => start + new Vector2(k.X, k.Z) * cell;
        bool Clear(Vector2 p)
        {
            (Vector2 Min, Vector2 Max) square = (p - new Vector2(half), p + new Vector2(half));
            return square.Min.X >= -tuning.Extent && square.Min.Y >= -tuning.Extent && square.Max.X <= tuning.Extent && square.Max.Y <= tuning.Extent &&
                !taken.Any(t => Overlap(square, t));
        }
        if (!Clear(start) || !Clear(goal)) return null;
        var target = Key(goal);
        // State: lattice point, heading, and straight steps since the last turn (capped where turning is allowed).
        PriorityQueue<((int X, int Z) At, int Heading, int Run), int> frontier = new();
        Dictionary<((int, int), int, int), int> cost = [];
        Dictionary<((int, int), int, int), ((int, int), int, int)> came = [];
        var first = (Key(start), Array.IndexOf(steps, outFrom), minRun);
        cost[first] = 0;
        frontier.Enqueue(first, 0);
        ((int, int), int, int)? reached = null;
        int budget = tuning.Passage.Search;
        while (frontier.TryDequeue(out var state, out _) && budget-- > 0)
        {
            var (at, heading, run) = state;
            if (at == target && (steps[heading] == -outTo || run >= minRun)) { reached = state; break; }
            for (int h = 0; h < steps.Length; h++)
            {
                if (steps[h] == -steps[heading] || (h != heading && run < minRun)) continue;
                Vector2 next = At(at) + steps[h] * cell;
                if (!Clear(next)) continue;
                var nextState = (Key(next), h, h == heading ? Math.Min(run + 1, minRun) : 1);
                int nextCost = cost[state] + 1 + (h == heading ? 0 : tuning.Passage.TurnCost);
                if (cost.TryGetValue(nextState, out int known) && known <= nextCost) continue;
                cost[nextState] = nextCost;
                came[nextState] = state;
                var k = Key(next);
                frontier.Enqueue(nextState, nextCost + Math.Abs(k.Item1 - target.X) + Math.Abs(k.Item2 - target.Z));
            }
        }
        if (reached is not { } end) return null;
        List<Vector2> path = [to.Point];
        for (var s = end; ; s = came[s])
        {
            path.Add(At(s.Item1));
            if (!came.ContainsKey(s)) break;
        }
        path.Add(from.Point);
        path.Reverse();
        return Build(Simplify([.. path]), outFrom, outTo, from, to, taken, tuning);
    }

    private static Route? Build(List<Vector2> points, Vector2 outFrom, Vector2 outTo, PlacedDoorway from, PlacedDoorway to,
        IReadOnlyList<(Vector2 Min, Vector2 Max)> taken, LayoutTuning tuning)
    {
        if (points.Count < 2) return null;
        // It must leave straight out of the first door and arrive straight into the second, never doubling back.
        if (Vector2.Dot(Direction(points[0], points[1]), outFrom) < 0.99f || Vector2.Dot(Direction(points[^2], points[^1]), -outTo) < 0.99f) return null;
        for (int i = 2; i < points.Count; i++)
            if (Vector2.Dot(Direction(points[i - 2], points[i - 1]), Direction(points[i - 1], points[i])) < -0.5f) return null;
        float half = tuning.Passage.Width / 2;
        List<(Vector2 Min, Vector2 Max)> rects = [];
        // A square at every turn, and a leg between turns (or between a door and a turn) where they do not touch.
        for (int i = 0; i < points.Count - 1; i++)
        {
            Vector2 a = points[i], b = points[i + 1], d = Direction(a, b);
            Vector2 start = i == 0 ? a : a + d * half, end = i == points.Count - 2 ? b : b - d * half;
            if (i > 0) rects.Add((points[i] - new Vector2(half), points[i] + new Vector2(half)));
            float length = Vector2.Dot(end - start, d);
            if (length < -Tolerance) return null;
            if (length > Tolerance)
            {
                if (length < 0.5f) return null;
                Vector2 across = new(MathF.Abs(d.Y), MathF.Abs(d.X));
                rects.Add((Vector2.Min(start - across * half, end + across * half), Vector2.Max(start - across * half, end + across * half)));
            }
        }
        foreach (var r in rects)
            if (r.Min.X < -tuning.Extent || r.Min.Y < -tuning.Extent || r.Max.X > tuning.Extent || r.Max.Y > tuning.Extent ||
                taken.Any(t => Overlap(r, t)) || rects.Any(o => o != r && Overlap(r, o))) return null;

        float height = from.Placement.Module.Spaces.First(s => from.Placement.Name(s.Id) == from.Space).Height;
        SpaceDefinition[] spaces = rects.Select((r, i) => new SpaceDefinition($"service/{i}", tuning.Passage.Label, [r.Min.X, r.Min.Y],
            [r.Max.X, r.Max.Y], height, tuning.Passage.Style)).ToArray();
        List<LinkDefinition> links = [ModuleRealizer.Link(from, spaces[0].Id, "service/in")];
        for (int i = 1; i < spaces.Length; i++)
            links.Add(new($"service/{i - 1}~{i}", [spaces[i - 1].Id, spaces[i].Id], LinkKind.Open));
        LinkDefinition latch = ModuleRealizer.Link(to, spaces[^1].Id, "service/latch");
        links.Add(latch);
        // A lamp over every space roomy enough for one along both sides.
        FixturePlacement[] lamps = spaces.Where(s => s.Max[0] - s.Min[0] >= tuning.Passage.Width - Tolerance && s.Max[1] - s.Min[1] >= tuning.Passage.Width - Tolerance)
            .Select(s => new FixturePlacement(tuning.Passage.Lamp, Space: s.Id, At: [(s.Min[0] + s.Max[0]) / 2, (s.Min[1] + s.Max[1]) / 2])).ToArray();
        return new(spaces, [.. links], lamps, latch.Id);
    }

    private static List<Vector2> Simplify(Vector2[] points)
    {
        List<Vector2> kept = [];
        foreach (Vector2 p in points)
        {
            if (kept.Count > 0 && Vector2.Distance(kept[^1], p) < Tolerance) continue;
            if (kept.Count >= 2 && MathF.Abs(Cross(kept[^1] - kept[^2], p - kept[^1])) < Tolerance && Vector2.Dot(kept[^1] - kept[^2], p - kept[^1]) > 0)
                kept[^1] = p;
            else kept.Add(p);
        }
        return kept;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    private static Vector2 Direction(Vector2 a, Vector2 b)
    {
        Vector2 d = b - a;
        return d.LengthSquared() < Tolerance ? Vector2.Zero : Vector2.Normalize(d);
    }

    private static bool Overlap((Vector2 Min, Vector2 Max) a, (Vector2 Min, Vector2 Max) b) =>
        Math.Min(a.Max.X, b.Max.X) - Math.Max(a.Min.X, b.Min.X) > Tolerance && Math.Min(a.Max.Y, b.Max.Y) - Math.Max(a.Min.Y, b.Min.Y) > Tolerance;

    private static Vector2 Outward(WallEdge edge) => edge switch
    {
        WallEdge.North => -Vector2.UnitY, WallEdge.South => Vector2.UnitY, WallEdge.West => -Vector2.UnitX, _ => Vector2.UnitX
    };
}
