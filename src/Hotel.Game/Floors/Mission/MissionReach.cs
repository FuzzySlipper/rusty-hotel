namespace Hotel.Game.Floors.Mission;

/// <summary>Everything a player can reach from a place, carrying what they pick up on the way.</summary>
internal sealed record MissionProgress(IReadOnlySet<string> Reached, IReadOnlySet<string> Items, IReadOnlySet<string> Crossed);

/// <summary>
/// Item-aware reachability. Progress only grows: items are kept, and a door once opened stays open, so the walk is a
/// fixed point over reached places and held items.
/// </summary>
/// <remarks>Adapted from Rifles' <c>GraphValidator.ReachableWithItems</c>; see docs/reuse.md.</remarks>
internal static class MissionReach
{
    /// <param name="blocked">A place the walk may not enter, to ask what is reachable without passing it.</param>
    /// <param name="without">An edge treated as absent, to ask whether it is needed.</param>
    internal static MissionProgress From(MissionGraph graph, string start, IEnumerable<string>? items = null,
        string? blocked = null, string? without = null)
    {
        Dictionary<string, MissionNode> nodes = graph.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        HashSet<string> reached = new(StringComparer.Ordinal) { start };
        HashSet<string> held = new(items ?? [], StringComparer.Ordinal);
        HashSet<string> crossed = new(StringComparer.Ordinal);
        MissionEdge[] edges = graph.Edges.Where(e => e.Id != without && nodes.ContainsKey(e.From) && nodes.ContainsKey(e.To))
            .OrderBy(e => e.Id, StringComparer.Ordinal).ToArray();
        for (bool changed = true; changed;)
        {
            changed = false;
            foreach (string id in reached)
                if (nodes[id].Grants is { } item) changed |= held.Add(item);
            foreach (MissionEdge edge in edges)
            {
                bool forward = reached.Contains(edge.From) && Passable(edge, reached, held, edge.From);
                bool back = reached.Contains(edge.To) && Passable(edge, reached, held, edge.To);
                if (!forward && !back) continue;
                crossed.Add(edge.Id);
                if (forward && edge.To != blocked) changed |= reached.Add(edge.To);
                if (back && edge.From != blocked) changed |= reached.Add(edge.From);
            }
        }
        return new(reached, held, crossed);
    }

    // Whether the edge can be crossed leaving `side`.
    private static bool Passable(MissionEdge edge, HashSet<string> reached, HashSet<string> held, string side) => edge.Kind switch
    {
        MissionEdgeKind.Open => true,
        MissionEdgeKind.Locked => edge.Item is not null && held.Contains(edge.Item),
        MissionEdgeKind.OneWay => side == edge.From,
        _ => side == edge.From || reached.Contains(edge.From)
    };
}
