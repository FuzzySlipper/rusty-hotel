using Hotel.Game.Floors.Mission;
using Hotel.Game.Scene.Kit;

namespace Hotel.Game.Floors.Layout;

/// <summary>
/// What a floor keeps when it shifts: the stair core, its landmark, every door the player opened or holds the key to,
/// with the rooms on both sides of it, and the shortcut passage once the player has unlatched it, with the corridor
/// placements that join them to the stairs. Everything else re-rolls. The kept pieces keep their placement ids, so what
/// was collected in them stays collected. Kept doors keep their ids and the opened ones (<see cref="Open"/>) stay open;
/// one whose key is held, open or shut, stays locked to that key under an item no later lock can share.
/// </summary>
internal sealed record KeptSet(LayoutPlacement[] Placements, IReadOnlyDictionary<MissionNodeKind, string> Places,
    SpaceDefinition[] Passage, LinkDefinition[] PassageLinks, FixturePlacement[] PassageFixtures, string? Latch, KeptDoor[] Doors,
    string[] Open, string TrimStyle)
{
    /// <summary>The route door id a lock's door is hung under on its floor.</summary>
    internal static string LockDoor(string floor, LayoutLock lck) => $"{floor}/lock/{lck.Edge}";

    /// <summary>The route door id a kept door is hung under: its link's, stable across every later shift.</summary>
    internal static string KeptDoorId(string floor, string link) => $"{floor}/door/{link}";

    /// <summary>The item a kept locked door opens with: its link's, stable across every later shift.</summary>
    internal static string KeptKey(string floor, string link) => $"{floor}/key/{link}";

    /// <summary>
    /// The kept set of a floor about to shift, given the route doors open on it (<paramref name="floorId"/>/latch keeps
    /// its shortcut) and the keys held on it.
    /// </summary>
    internal static KeptSet From(GeneratedFloor floor, string floorId, IReadOnlyCollection<string> openDoors, IReadOnlyCollection<string> heldKeys)
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
        // Every door the player opened or holds the key to, with the rooms on both sides of it and their way to the
        // stairs; doors kept by an earlier shift count as well.
        List<KeptDoor> doors = [];
        List<string> stillOpen = [];
        IEnumerable<(string Id, string Link, string? Item, string? Guards)> lockable = layout.Locks
            .Select(l => (LockDoor(floorId, l), l.Link, (string?)l.Item, (string?)Guarded(floor, l)))
            .Concat(layout.KeptDoors.Select(d => (d.Id, d.Link, d.Locked?.Item, d.Locked?.Guards)));
        foreach (var door in lockable)
        {
            bool open = openDoors.Contains(door.Id), held = door.Item is not null && heldKeys.Contains(door.Item);
            if (!open && !held) continue;
            LinkDefinition link = floor.Plan.Links.First(l => l.Id == door.Link);
            foreach (string side in link.Between.Select(Placement).Where(came.ContainsKey))
                for (string? at = side; at is not null; at = came[at]) kept.Add(at);
            // Named for its link, which kept placements keep, so no lock of a later shift can share its id or its key.
            doors.Add(new(KeptDoorId(floorId, door.Link), door.Link, held ? new(KeptKey(floorId, door.Link), door.Guards!) : null));
            if (open) stillOpen.Add(KeptDoorId(floorId, door.Link));
        }
        bool shortcut = latchOpened && layout.Latch is not null;
        if (shortcut) Keep(MissionNodeKind.Shortcut);
        return new(layout.Placements.Where(p => kept.Contains(p.Id)).Select(p => p with { Region = FloorEmbedding.OpenRegion }).ToArray(), places,
            shortcut ? layout.Passage : [], shortcut ? layout.PassageLinks : [], shortcut ? layout.PassageFixtures : [], shortcut ? layout.Latch : null,
            [.. doors], [.. stillOpen], layout.TrimStyle);
    }

    /// <summary>The key a held item becomes on the shifted floor: a held lock's kept key, or none.</summary>
    internal static string? Carried(KeptSet kept, GeneratedFloor floor, string floorId, string item) =>
        floor.Layout.Locks.Where(l => l.Item == item).Select(l => l.Link)
            .Concat(floor.Layout.KeptDoors.Where(d => d.Locked?.Item == item).Select(d => d.Link))
            .Select(link => kept.Doors.FirstOrDefault(d => d.Link == link)?.Locked?.Item).FirstOrDefault(k => k is not null);

    /// <summary>The space a lock's door opens into: the side in the region its edge guards.</summary>
    internal static string Guarded(GeneratedFloor floor, LayoutLock lck)
    {
        LinkDefinition link = floor.Plan.Links.First(l => l.Id == lck.Link);
        string a = link.Between[0], b = link.Between[1];
        return floor.Layout.RegionOf(a) == $"behind/{lck.Edge}" ? a : b;
    }
}
