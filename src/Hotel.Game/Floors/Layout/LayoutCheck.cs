using Hotel.Game.Floors.Mission;
using Hotel.Game.Scene.Kit;

namespace Hotel.Game.Floors.Layout;

/// <summary>
/// The cheap check on a realized layout, before Engine navigation: walking space to space through its links, every
/// space is reachable from the stair core carrying the keys found on the way, the shortcut's latch opens only from the
/// passage, and each lock really cuts off the places its mission edge guards (a blocked-door walk, as in rusty-dungeon's feature
/// placement).
/// </summary>
internal static class LayoutCheck
{
    internal static string? Check(FloorLayout layout, FloorPlan plan, MissionGraph graph)
    {
        // Every locked edge became exactly one door with its item, and every latch the passage's end door at the stairs.
        Dictionary<string, LinkDefinition> links = plan.Links.ToDictionary(l => l.Id, StringComparer.Ordinal);
        foreach (MissionEdge edge in graph.Edges.Where(e => e.Kind == MissionEdgeKind.Locked))
        {
            LayoutLock[] doors = layout.Locks.Where(l => l.Edge == edge.Id).ToArray();
            if (doors.Length != 1 || doors[0].Item != edge.Item || !links.ContainsKey(doors[0].Link))
                return $"lock: locked edge '{edge.Id}' needs exactly one realized door opened by '{edge.Item}'.";
        }
        if (layout.Locks.FirstOrDefault(l => !graph.Edges.Any(e => e.Id == l.Edge && e.Kind == MissionEdgeKind.Locked)) is { } stray)
            return $"lock: '{stray.Link}' stands for no locked edge.";
        string arrival = Space(layout, plan, MissionGraph.ArrivalId);
        bool latched = graph.Edges.Any(e => e.Kind == MissionEdgeKind.Latch);
        if (latched != (layout.Latch is not null)) return "latch: the shortcut's latch and the layout's latch door must both exist or neither.";
        if (layout.Latch is { } latch && (!links.TryGetValue(latch, out LinkDefinition? door) || door.Between[0] != arrival ||
            !layout.Passage.Any(s => s.Id == door.Between[1])))
            return $"latch: '{latch}' is not the door from the service passage into the stairs.";
        // Keys are picked up in the space of the place that grants them.
        Dictionary<string, string> keys = graph.Nodes.Where(n => n.Grants is not null)
            .ToDictionary(n => Space(layout, plan, n.Id), n => n.Grants!, StringComparer.Ordinal);
        HashSet<string> all = Walk(plan, layout, keys, arrival, closed: null);
        if (plan.Spaces.FirstOrDefault(s => !all.Contains(s.Id)) is { } stranded) return $"reach: '{stranded.Id}' cannot be reached from the stairs.";
        foreach (LayoutLock lck in layout.Locks)
        {
            IReadOnlySet<string> guarded = MissionReach.From(graph, MissionGraph.ArrivalId, without: lck.Edge).Reached;
            HashSet<string> without = Walk(plan, layout, keys, arrival, closed: lck.Link);
            foreach (MissionNode node in graph.Nodes.Where(n => !guarded.Contains(n.Id)))
                if (without.Contains(Space(layout, plan, node.Id)))
                    return $"lock: '{node.Id}' can be reached around the lock on {lck.Link}.";
        }
        return null;
    }

    // A place's space: the first space of the module standing for it.
    private static string Space(FloorLayout layout, FloorPlan plan, string node) =>
        plan.Spaces.First(s => s.Id.StartsWith(layout.Places[node] + "/", StringComparison.Ordinal)).Id;

    // Walks from a space, collecting keys, through every link but the closed one; a locked door needs its item.
    private static HashSet<string> Walk(FloorPlan plan, FloorLayout layout, Dictionary<string, string> keys, string start, string? closed)
    {
        HashSet<string> reached = new(StringComparer.Ordinal) { start }, held = new(StringComparer.Ordinal);
        Dictionary<string, string> locked = layout.Locks.ToDictionary(l => l.Link, l => l.Item, StringComparer.Ordinal);
        for (bool changed = true; changed;)
        {
            changed = false;
            foreach (string space in reached) if (keys.TryGetValue(space, out string? item)) changed |= held.Add(item);
            foreach (LinkDefinition link in plan.Links.Where(l => l.Id != closed && (!locked.TryGetValue(l.Id, out string? item) || held.Contains(item))))
            {
                // The latch's second space is the passage; it opens from there only.
                bool forward = reached.Contains(link.Between[0]) && link.Id != layout.Latch, back = reached.Contains(link.Between[1]);
                if (forward) changed |= reached.Add(link.Between[1]);
                if (back) changed |= reached.Add(link.Between[0]);
            }
        }
        return reached;
    }
}
