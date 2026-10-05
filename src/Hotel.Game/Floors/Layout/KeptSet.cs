using Hotel.Game.Floors.Mission;
using Hotel.Game.Scene.Kit;

namespace Hotel.Game.Floors.Layout;

/// <summary>
/// What a floor keeps when it shifts: the stair core, its landmark, and the shortcut passage once the player has
/// unlatched it, with the corridor placements that join them to the stairs. Everything else re-rolls. The kept pieces
/// keep their placement ids, so what was collected in them stays collected.
/// </summary>
internal sealed record KeptSet(LayoutPlacement[] Placements, IReadOnlyDictionary<MissionNodeKind, string> Places,
    SpaceDefinition[] Passage, LinkDefinition[] PassageLinks, FixturePlacement[] PassageFixtures, string? Latch)
{
    /// <summary>The kept set of a floor about to shift: <paramref name="latchOpened"/> keeps its shortcut too.</summary>
    internal static KeptSet From(GeneratedFloor floor, bool latchOpened)
    {
        FloorLayout layout = floor.Layout;
        string Placement(string space) => space.Split('/')[0];
        // Placements joined by the realized floor's links, the service passage aside.
        Dictionary<string, List<string>> next = layout.Placements.ToDictionary(p => p.Id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (LinkDefinition link in floor.Plan.Links)
        {
            string a = Placement(link.Between[0]), b = Placement(link.Between[1]);
            if (a != b && next.ContainsKey(a) && next.ContainsKey(b)) { next[a].Add(b); next[b].Add(a); }
        }
        string core = layout.Places[MissionGraph.ArrivalId];
        Dictionary<string, string?> came = new(StringComparer.Ordinal) { [core] = null };
        Queue<string> queue = new([core]);
        while (queue.TryDequeue(out string? at))
            foreach (string n in next[at].Order(StringComparer.Ordinal))
                if (came.TryAdd(n, at)) queue.Enqueue(n);
        HashSet<string> kept = new(StringComparer.Ordinal) { core };
        Dictionary<MissionNodeKind, string> places = new() { [MissionNodeKind.Arrival] = core };
        void Keep(MissionNodeKind kind)
        {
            MissionNode? node = floor.Graph.Nodes.FirstOrDefault(n => n.Kind == kind);
            if (node is null || !came.ContainsKey(layout.Places[node.Id])) return;
            places[kind] = layout.Places[node.Id];
            for (string? at = layout.Places[node.Id]; at is not null; at = came[at]) kept.Add(at);
        }
        Keep(MissionNodeKind.Landmark);
        bool shortcut = latchOpened && layout.Latch is not null;
        if (shortcut) Keep(MissionNodeKind.Shortcut);
        return new(layout.Placements.Where(p => kept.Contains(p.Id)).Select(p => p with { Region = FloorEmbedding.OpenRegion }).ToArray(), places,
            shortcut ? layout.Passage : [], shortcut ? layout.PassageLinks : [], shortcut ? layout.PassageFixtures : [], shortcut ? layout.Latch : null);
    }
}
