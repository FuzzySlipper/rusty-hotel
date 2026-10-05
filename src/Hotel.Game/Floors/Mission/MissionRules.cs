namespace Hotel.Game.Floors.Mission;

/// <summary>
/// Proposes one rule's change to a graph. Proposals draw only through <see cref="FloorDraws"/> under the graph stage,
/// keyed by the step, and choose among places in id order, so the same graph and key always propose the same change.
/// A proposal is not yet accepted: <see cref="MissionGenerator"/> validates the whole proposed graph first.
/// </summary>
internal static class MissionRules
{
    internal static (MissionGraph? Proposed, string Summary, MissionProblem? Refusal) Propose(MissionGraph graph, MissionRule rule,
        FloorDraws draws, string key)
    {
        List<MissionNode> nodes = [.. graph.Nodes];
        List<MissionEdge> edges = [.. graph.Edges];
        void Join(string a, string b, MissionEdgeKind kind = MissionEdgeKind.Open, string? item = null) =>
            edges.Add(new($"{a}~{b}", a, b, kind, item));
        string Pick(string purpose, IReadOnlyList<string> from) => from[draws.Index(FloorStage.Graph, purpose, key, from.Count)];
        // A place behind its own locked door (a gated cache) is a room with one way in, so nothing hangs off it.
        bool Sealed(MissionGraph g, string id) =>
            g.Edges.Where(e => e.From == id || e.To == id) is var touching && touching.Any() && touching.All(e => e.Kind == MissionEdgeKind.Locked)
            && g.Nodes.First(n => n.Id == id).Kind != MissionNodeKind.Gate;
        string[] Reachable(MissionGraph g, string? blocked = null, string? without = null, params string[] except) =>
            MissionReach.From(g, MissionGraph.ArrivalId, blocked: blocked, without: without).Reached
                .Where(id => id != blocked && !except.Contains(id) && !Sealed(g, id)).Order(StringComparer.Ordinal).ToArray();
        MissionGraph Proposed() => graph with { Nodes = [.. nodes], Edges = [.. edges] };
        (MissionGraph?, string, MissionProblem?) Refuse(string code, string detail) => (null, "", new(code, detail));

        // Splitting an open edge on the route to the objective puts a new place in the player's way.
        (string From, string To, MissionEdge Edge)? Split(string purpose)
        {
            var route = Route(graph).Where(step => step.Edge.Kind == MissionEdgeKind.Open).ToArray();
            if (route.Length == 0) return null;
            var chosen = route[draws.Index(FloorStage.Graph, purpose, key, route.Length)];
            edges.Remove(chosen.Edge);
            return chosen;
        }

        switch (rule)
        {
            case MissionRule.LockKeyLoop:
            {
                string gate = $"gate.{key}", pass = $"pass.{key}", keyNode = $"key.{key}";
                if (Split("lock.edge") is not { } split) return Refuse("no_open_route", "the route to the objective has no open edge to lock.");
                (string from, string to) = (split.From, split.To);
                nodes.Add(new(gate, MissionNodeKind.Gate, Gates: $"{gate}~{to}"));
                Join(from, gate);
                Join(gate, to, MissionEdgeKind.Locked, pass);
                string anchor = Pick("lock.key", Reachable(Proposed(), without: $"{gate}~{to}", except: gate));
                nodes.Add(new(keyNode, MissionNodeKind.Key, pass));
                Join(anchor, keyNode);
                if (anchor != gate && draws.OneIn(FloorStage.Graph, "lock.loop", key, 2)) Join(keyNode, gate);
                return (Proposed(), $"locked {gate}~{to} with {pass} from {keyNode} off {anchor}", null);
            }
            case MissionRule.DetourLoop:
            {
                // A detour loops between two places on this side of the objective that are not already neighbours.
                string[] places = Reachable(graph, except: MissionGraph.ObjectiveId);
                string a = Pick("detour.from", places);
                string[] others = places.Where(p => p != a && !graph.Edges.Any(e => (e.From == a && e.To == p) || (e.From == p && e.To == a))).ToArray();
                if (others.Length == 0) return Refuse("no_detour", $"every place is already beside {a}.");
                string b = Pick("detour.to", others);
                string stop = $"supplies.{key}";
                nodes.Add(new(stop, MissionNodeKind.Supplies));
                Join(a, stop);
                Join(stop, b);
                return (Proposed(), $"looped {a} to {b} through {stop}", null);
            }
            case MissionRule.GatedBranch:
            {
                string cache = $"supplies.{key}", pass = $"pass.{key}", keyNode = $"key.{key}";
                string[] places = Reachable(graph);
                string anchor = Pick("branch.at", places), keyAt = Pick("branch.key", places);
                nodes.Add(new(cache, MissionNodeKind.Supplies));
                Join(anchor, cache, MissionEdgeKind.Locked, pass);
                nodes.Add(new(keyNode, MissionNodeKind.Key, pass));
                Join(keyAt, keyNode);
                return (Proposed(), $"gated {cache} off {anchor} with {pass} from {keyNode} off {keyAt}", null);
            }
            case MissionRule.ResourceBeforeHazard:
            {
                string hazard = $"hazard.{key}", stop = $"supplies.{key}";
                if (Split("hazard.edge") is not { } split) return Refuse("no_open_route", "the route to the objective has no open edge for a hazard.");
                (string from, string to) = (split.From, split.To);
                nodes.Add(new(hazard, MissionNodeKind.Hazard));
                Join(from, hazard);
                Join(hazard, to);
                string anchor = Pick("hazard.supplies", Reachable(Proposed(), blocked: hazard));
                nodes.Add(new(stop, MissionNodeKind.Supplies));
                Join(anchor, stop);
                return (Proposed(), $"put {hazard} between {from} and {to}, with {stop} off {anchor} before it", null);
            }
            case MissionRule.Bell or MissionRule.Landmark:
            {
                MissionNodeKind kind = rule == MissionRule.Bell ? MissionNodeKind.Bell : MissionNodeKind.Landmark;
                string place = $"{kind.ToString().ToLowerInvariant()}.{key}";
                string anchor = Pick($"{kind.ToString().ToLowerInvariant()}.at", Reachable(graph));
                nodes.Add(new(place, kind));
                Join(anchor, place);
                return (Proposed(), $"added {place} off {anchor}", null);
            }
            case MissionRule.Shortcut:
            {
                if (Route(graph).Length < 2) return Refuse("shortcut_pointless", "the objective is already beside arrival.");
                string door = $"shortcut.{key}";
                nodes.Add(new(door, MissionNodeKind.Shortcut));
                Join(MissionGraph.ObjectiveId, door);
                Join(door, MissionGraph.ArrivalId, MissionEdgeKind.Latch);
                return (Proposed(), $"latched {door} beside the objective back to arrival", null);
            }
            default: throw new ArgumentOutOfRangeException(nameof(rule), rule, "Unknown mission rule.");
        }
    }

    /// <summary>
    /// The shortest walk from arrival to the objective with everything the floor gives, as edges in walking order:
    /// each from the place nearer arrival to the next. Empty when there is none.
    /// </summary>
    internal static (string From, string To, MissionEdge Edge)[] Route(MissionGraph graph)
    {
        IReadOnlySet<string> items = MissionReach.From(graph, MissionGraph.ArrivalId).Items;
        Dictionary<string, (string From, MissionEdge Edge)> came = new(StringComparer.Ordinal);
        Queue<string> open = new([MissionGraph.ArrivalId]);
        HashSet<string> seen = new(StringComparer.Ordinal) { MissionGraph.ArrivalId };
        MissionEdge[] edges = graph.Edges.OrderBy(e => e.Id, StringComparer.Ordinal).ToArray();
        while (open.TryDequeue(out string? at) && at != MissionGraph.ObjectiveId)
            foreach (MissionEdge edge in edges)
            {
                string? next = edge.From == at ? edge.To : edge.To == at && edge.Kind is not MissionEdgeKind.OneWay ? edge.From : null;
                if (next is null || (edge.Kind == MissionEdgeKind.Locked && !items.Contains(edge.Item!)) || !seen.Add(next)) continue;
                came[next] = (at, edge);
                open.Enqueue(next);
            }
        if (!came.ContainsKey(MissionGraph.ObjectiveId)) return [];
        List<(string, string, MissionEdge)> route = [];
        for (string at = MissionGraph.ObjectiveId; at != MissionGraph.ArrivalId; at = came[at].From) route.Add((came[at].From, at, came[at].Edge));
        route.Reverse();
        return [.. route];
    }
}
