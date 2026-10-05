using Hotel.Game.Floors.Mission;
using Hotel.Game.Scene.Kit;

namespace Hotel.Game.Floors.Layout;

/// <summary>
/// What a floor keeps when it shifts: the stair core, its landmark, every door the player opened with the rooms on
/// both sides of it, and the shortcut passage once the player has unlatched it, with the corridor placements that join
/// them to the stairs. Everything else re-rolls. The kept pieces keep their placement ids, so what was collected in
/// them stays collected, and kept doors keep their ids and stay open.
/// </summary>
internal sealed record KeptSet(LayoutPlacement[] Placements, IReadOnlyDictionary<MissionNodeKind, string> Places,
    SpaceDefinition[] Passage, LinkDefinition[] PassageLinks, FixturePlacement[] PassageFixtures, string? Latch, KeptDoor[] Doors)
{
    /// <summary>The route door id a lock's door is hung under on its floor.</summary>
    internal static string LockDoor(string floor, LayoutLock lck) => $"{floor}/lock/{lck.Edge}";

    /// <summary>The route door id a kept door is hung under: its link's, stable across every later shift.</summary>
    internal static string KeptDoorId(string floor, string link) => $"{floor}/door/{link}";

    /// <summary>
    /// The kept set of a floor about to shift, given the route doors open on it (<paramref name="floorId"/>/latch keeps
    /// its shortcut).
    /// </summary>
    internal static KeptSet From(GeneratedFloor floor, string floorId, IReadOnlyCollection<string> openDoors)
    {
        bool latchOpened = openDoors.Contains($"{floorId}/latch");
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
        // Every door the player opened, with the rooms on both sides of it and their way to the stairs; doors kept by an
        // earlier shift are opened doors too.
        List<KeptDoor> doors = [];
        IEnumerable<KeptDoor> opened = layout.Locks.Select(l => new KeptDoor(LockDoor(floorId, l), l.Link)).Concat(layout.KeptDoors);
        foreach (KeptDoor door in opened.Where(d => openDoors.Contains(d.Id)))
        {
            LinkDefinition link = floor.Plan.Links.First(l => l.Id == door.Link);
            foreach (string side in link.Between.Select(Placement).Where(came.ContainsKey))
                for (string? at = side; at is not null; at = came[at]) kept.Add(at);
            // Named for its link, which kept placements keep, so no lock of a later shift can share its id.
            doors.Add(new(KeptDoorId(floorId, door.Link), door.Link));
        }
        bool shortcut = latchOpened && layout.Latch is not null;
        if (shortcut) Keep(MissionNodeKind.Shortcut);
        return new(layout.Placements.Where(p => kept.Contains(p.Id)).Select(p => p with { Region = FloorEmbedding.OpenRegion }).ToArray(), places,
            shortcut ? layout.Passage : [], shortcut ? layout.PassageLinks : [], shortcut ? layout.PassageFixtures : [], shortcut ? layout.Latch : null,
            [.. doors]);
    }
}
