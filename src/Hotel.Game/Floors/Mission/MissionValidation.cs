namespace Hotel.Game.Floors.Mission;

/// <summary>The size a mission graph may grow to.</summary>
internal sealed record MissionBudget(int Nodes, int Edges);

/// <summary>
/// What every floor's mission graph must promise: one arrival and one objective; every place reachable; every item a
/// lock needs granted by a reachable key and every lock both opened and needed; supplies reachable before every
/// hazard; and a way back to arrival from every place, there and back.
/// </summary>
internal static class MissionValidation
{
    internal static MissionProblem[] Check(MissionGraph graph, MissionBudget budget)
    {
        List<MissionProblem> problems = [];
        void Problem(string code, string detail, string? at = null) => problems.Add(new(code, detail, at));
        if (graph.Nodes.Length > budget.Nodes) Problem("node_budget", $"{graph.Nodes.Length} places; the budget allows {budget.Nodes}.");
        if (graph.Edges.Length > budget.Edges) Problem("edge_budget", $"{graph.Edges.Length} ways; the budget allows {budget.Edges}.");
        foreach (var repeated in graph.Nodes.GroupBy(n => n.Id).Where(g => g.Count() > 1)) Problem("duplicate_node", "place ids are unique.", repeated.Key);
        foreach (var repeated in graph.Edges.GroupBy(e => e.Id).Where(g => g.Count() > 1)) Problem("duplicate_edge", "edge ids are unique.", repeated.Key);
        HashSet<string> ids = graph.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        foreach (MissionEdge edge in graph.Edges.Where(e => !ids.Contains(e.From) || !ids.Contains(e.To)))
            Problem("edge_endpoint", $"joins a missing place ({edge.From} to {edge.To}).", edge.Id);
        foreach (MissionEdge edge in graph.Edges.Where(e => (e.Kind == MissionEdgeKind.Locked) != (e.Item is not null)))
            Problem("lock_item", "a locked edge, and only a locked edge, names the item it needs.", edge.Id);
        foreach (MissionNode gate in graph.Nodes.Where(n => n.Kind == MissionNodeKind.Gate))
            if (graph.Edges.FirstOrDefault(e => e.Id == gate.Gates) is not { Kind: MissionEdgeKind.Locked } door || door.From != gate.Id)
                Problem("gate_door", "a gate names the locked edge leading on from it.", gate.Id);
        foreach (var kind in new[] { MissionNodeKind.Arrival, MissionNodeKind.Objective })
            if (graph.Nodes.Count(n => n.Kind == kind) != 1) Problem("promise_count", $"a floor has exactly one {kind.ToString().ToLowerInvariant()}.");
        if (graph.Nodes.SingleOrDefault(n => n.Kind == MissionNodeKind.Arrival)?.Id != MissionGraph.ArrivalId)
            Problem("arrival_id", $"the arrival place is '{MissionGraph.ArrivalId}'.");
        if (problems.Count > 0) return [.. problems];

        MissionProgress full = MissionReach.From(graph, MissionGraph.ArrivalId);
        foreach (MissionNode node in graph.Nodes.Where(n => !full.Reached.Contains(n.Id)))
            Problem(node.Kind == MissionNodeKind.Objective ? "objective_unreachable" : "unreachable", "cannot be reached from arrival.", node.Id);
        foreach (MissionEdge edge in graph.Edges.Where(e => e.Item is not null))
        {
            if (!graph.Nodes.Any(n => n.Grants == edge.Item)) Problem("item_never_granted", $"needs '{edge.Item}', which no key grants.", edge.Id);
            else if (!full.Crossed.Contains(edge.Id)) Problem("lock_never_opened", $"its key '{edge.Item}' cannot be reached first.", edge.Id);
            else if (MissionReach.From(graph, MissionGraph.ArrivalId, without: edge.Id).Reached.Count == full.Reached.Count)
                Problem("lock_bypassed", "everything behind it is reachable another way, so the lock is not needed.", edge.Id);
        }
        foreach (MissionNode hazard in graph.Nodes.Where(n => n.Kind == MissionNodeKind.Hazard))
        {
            IReadOnlySet<string> before = MissionReach.From(graph, MissionGraph.ArrivalId, blocked: hazard.Id).Reached;
            if (!graph.Nodes.Any(n => n.Kind == MissionNodeKind.Supplies && before.Contains(n.Id)))
                Problem("hazard_unprepared", "no supplies can be reached before it.", hazard.Id);
        }
        // The way back is walked with everything the floor gives and with every latch the way in opened.
        MissionGraph opened = graph with
        {
            Edges = graph.Edges.Select(e => e.Kind == MissionEdgeKind.Latch && full.Reached.Contains(e.From)
                ? e with { Kind = MissionEdgeKind.Open } : e).ToArray()
        };
        foreach (MissionNode node in graph.Nodes.Where(n => full.Reached.Contains(n.Id) && n.Id != MissionGraph.ArrivalId))
            if (!MissionReach.From(opened, node.Id, full.Items).Reached.Contains(MissionGraph.ArrivalId))
                Problem("no_return", "there is no way back to arrival.", node.Id);
        return [.. problems];
    }
}
