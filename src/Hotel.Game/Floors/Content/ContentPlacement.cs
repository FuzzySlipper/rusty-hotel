using System.Numerics;
using Hotel.Game.Floors.Layout;
using Hotel.Game.Floors.Mission;
using Hotel.Game.Floors.Modules;
using Hotel.Game.Scene.Kit;
using Hotel.Game.Combat;

namespace Hotel.Game.Floors.Content;

/// <summary>
/// Furnishes a laid-out floor: gives each mission place's module its content (the objective find, each key, a stop's
/// supplies, the bell, a hazard's resident), then the floor's readings, extra residents and loose supplies, all at the
/// modules' content sockets. Every choice is drawn here under the content stage and resolved into <see cref="FloorContent"/>.
/// A stop before a hazard is given the recovery item when the draws left it none, so the pacing budget is met by design.
/// </summary>
internal static class ContentPlacement
{
    private sealed record Offer(LayoutPlacement Placement, ModuleDefinition Module, ContentSocket Socket, string Name)
    {
        internal string Id => $"{Placement.Id}/{Socket.Id}";
    }

    internal static FloorContent Place(MissionGraph graph, FloorLayout layout, FloorPlan plan, BuiltFloor floor, ModuleCatalog catalog,
        ContentTuning tuning, ResidentKind[] kinds, FloorReadings readings, FloorDraws draws)
    {
        Offer[] offers = layout.Placements.OrderBy(p => p.Id, StringComparer.Ordinal).SelectMany(p =>
        {
            ModuleDefinition module = catalog.Find(p.Module)!;
            return module.Sockets.Select(s => new Offer(p, module, s, $"{p.Id}/{s.Socket}"));
        }).ToArray();
        HashSet<string> used = new(StringComparer.Ordinal);
        Dictionary<string, string> nodeOf = layout.Places.ToDictionary(p => p.Value, p => p.Key, StringComparer.Ordinal);
        Offer? Take(string placement, ContentSocketKind kind, string purpose)
        {
            Offer[] free = offers.Where(o => o.Placement.Id == placement && o.Socket.Kind == kind && !used.Contains(o.Id)).ToArray();
            if (free.Length == 0) return null;
            Offer chosen = free[draws.Index(FloorStage.Content, purpose, placement, free.Length)];
            used.Add(chosen.Id);
            return chosen;
        }
        ItemWeight Item(ItemWeight[] pool, string purpose, string key) =>
            pool[draws.Weighted(FloorStage.Content, purpose, key, pool.Select(w => w.Weight).ToArray())];

        // Arrival: the stair core's arrival post, facing the corridor the core opens on.
        string landing = layout.Places[MissionGraph.ArrivalId];
        Offer arrival = Take(landing, ContentSocketKind.Arrival, "arrival") ?? throw new InvalidOperationException($"'{landing}' has no arrival socket.");
        Vector3 standing = floor.Sockets[arrival.Name];
        bool Clear(string post, ResidentKind kind) => ArrivalClear(floor.Sockets[post], standing, kind, tuning.Pacing.ArrivalMargin);
        BuiltOpening? way = floor.Openings.Values.Where(o => o.Link.Kind != LinkKind.Door && o.Link.Between.Any(b => b.StartsWith(landing + "/", StringComparison.Ordinal)))
            .OrderBy(o => o.Link.Id, StringComparer.Ordinal).FirstOrDefault();
        Vector3 toward = way is null ? -Vector3.UnitZ : (way.Start + way.End) / 2 - standing;
        float yaw = MathF.Atan2(toward.X, -toward.Z) * 180 / MathF.PI;

        List<PlacedFind> finds = [];
        List<PlacedResident> residents = [];
        PlacedBell? bell = null;
        foreach (MissionNode node in graph.Nodes.OrderBy(n => n.Id, StringComparer.Ordinal))
        {
            string placement = layout.Places[node.Id];
            switch (node.Kind)
            {
                case MissionNodeKind.Objective when Take(placement, ContentSocketKind.Find, "objective") is { } at:
                    ItemWeight objective = Item(tuning.Objective, "objective.item", node.Id);
                    finds.Add(new(at.Id, FindRole.Objective, at.Name, objective.Item, objective.Count, null));
                    break;
                case MissionNodeKind.Key when Take(placement, ContentSocketKind.Find, "key") is { } at:
                    finds.Add(new(at.Id, FindRole.Key, at.Name, null, 1, node.Grants));
                    break;
                case MissionNodeKind.Supplies:
                    for (int i = 0; i < tuning.SuppliesPerStop && Take(placement, ContentSocketKind.Find, $"stop{i}") is { } at; i++)
                    {
                        ItemWeight supply = Item(tuning.Supplies, "stop.item", at.Id);
                        finds.Add(new(at.Id, FindRole.Supplies, at.Name, supply.Item, supply.Count, null));
                    }
                    break;
                case MissionNodeKind.Bell when Take(placement, ContentSocketKind.Bell, "bell") is { } at:
                    bell = new(tuning.Spirit, at.Name, plan.Spaces.First(s => s.Id.StartsWith(placement + "/", StringComparison.Ordinal)).Label);
                    break;
                case MissionNodeKind.Hazard when Take(placement, ContentSocketKind.ResidentPost, "hazard") is { } at:
                    if (Resident(at, "hazard.kind") is { } guard) residents.Add(guard);
                    break;
            }
        }

        // A stop the player can reach before a hazard holds the recovery item, whatever the draws gave it.
        foreach (MissionNode hazard in graph.Nodes.Where(n => n.Kind == MissionNodeKind.Hazard).OrderBy(n => n.Id, StringComparer.Ordinal))
        {
            IReadOnlySet<string> before = MissionReach.From(graph, MissionGraph.ArrivalId, blocked: hazard.Id).Reached;
            PlacedFind[] stops = finds.Where(f => f.Role == FindRole.Supplies && before.Contains(nodeOf[f.Id.Split('/')[0]])).ToArray();
            for (int missing = tuning.Pacing.RecoveryBeforeHazard - stops.Count(f => f.Item == tuning.Pacing.RecoveryItem), i = 0; missing > 0 && i < stops.Length; i++)
                if (stops[i].Item != tuning.Pacing.RecoveryItem)
                {
                    finds[finds.IndexOf(stops[i])] = stops[i] with { Item = tuning.Pacing.RecoveryItem, Count = 1 };
                    missing--;
                }
        }

        // Extra residents on free posts, and loose supplies in rooms no mission place claimed.
        Offer[] posts = offers.Where(o => o.Socket.Kind == ContentSocketKind.ResidentPost && !used.Contains(o.Id) && o.Placement.Id != landing).ToArray();
        for (int i = 0, wanted = tuning.ExtraResidents.At(draws.Seed.Depth); i < wanted && posts.Length > 0; i++)
        {
            Offer post = posts[draws.Index(FloorStage.Content, "extra.post", $"r{i}", posts.Length)];
            posts = posts.Where(p => p != post).ToArray();
            used.Add(post.Id);
            // An extra resident never becomes an encounter the player meets before any recovery.
            if (Resident(post, $"extra.kind{i}") is { } extra &&
                ContentPacing.RecoveryBefore(extra, finds, layout, plan, tuning.Pacing.RecoveryItem) >= tuning.Pacing.RecoveryBeforeHazard)
                residents.Add(extra);
        }
        Offer[] spare = offers.Where(o => o.Socket.Kind == ContentSocketKind.Find && !used.Contains(o.Id) && !nodeOf.ContainsKey(o.Placement.Id)).ToArray();
        for (int i = 0, wanted = tuning.LooseSupplies.At(draws.Seed.Depth); i < wanted && spare.Length > 0; i++)
        {
            Offer at = spare[draws.Index(FloorStage.Content, "loose.socket", $"l{i}", spare.Length)];
            spare = spare.Where(s => s != at).ToArray();
            used.Add(at.Id);
            ItemWeight supply = Item(tuning.Supplies, "loose.item", at.Id);
            finds.Add(new(at.Id, FindRole.Loose, at.Name, supply.Item, supply.Count, null));
        }

        // Readings: each notice shows a different authored reading while they last.
        List<FloorReading> unread = [.. readings.Readings];
        List<PlacedReading> notices = [];
        foreach (Offer notice in offers.Where(o => o.Socket.Kind == ContentSocketKind.Reading))
        {
            if (unread.Count == 0) break;
            FloorReading reading = unread[draws.Index(FloorStage.Content, "reading", notice.Id, unread.Count)];
            unread.Remove(reading);
            notices.Add(new(notice.Id, notice.Name, reading.Id));
        }
        string[] landmarks = offers.Where(o => o.Socket.Kind == ContentSocketKind.Landmark).Select(o => o.Name).ToArray();
        return new(arrival.Name, yaw, [.. finds], [.. residents], [.. notices], bell, landmarks);

        // A kind that may stand in the post's module, whose leash keeps it in the post's region, and that cannot see or
        // strike the arrival.
        PlacedResident? Resident(Offer post, string purpose)
        {
            ResidentWeight[] fit = tuning.Residents.Where(r => r.Weight > 0 && r.Tags.Any(post.Module.Tags.Contains) &&
                Clear(post.Name, kinds.First(k => k.Id == r.Kind)) &&
                LeashCrossing(new(post.Id, r.Kind, post.Name, post.Placement.Region), layout, plan, floor, kinds.First(k => k.Id == r.Kind).Leash) is null).ToArray();
            if (fit.Length == 0) return null;
            ResidentWeight kind = fit[draws.Weighted(FloorStage.Content, purpose, post.Id, fit.Select(r => r.Weight).ToArray())];
            return new(post.Id, kind.Kind, post.Name, post.Placement.Region);
        }
    }

    /// <summary>Whether a resident at a post can neither see nor strike within the margin of the arrival.</summary>
    internal static bool ArrivalClear(Vector3 post, Vector3 arrival, ResidentKind kind, float margin) =>
        Vector2.Distance(new(post.X, post.Z), new(arrival.X, arrival.Z)) > Math.Max(kind.SightRange, kind.AttackReach) + kind.Leash + margin;

    /// <summary>
    /// The first space a resident could follow the player into that lies outside its region: walking from its post's
    /// space through links that are not locks or the latch, into spaces within its leash of the post. Null when none.
    /// </summary>
    internal static string? LeashCrossing(PlacedResident resident, FloorLayout layout, FloorPlan plan, BuiltFloor floor, float leash)
    {
        Vector3 post = floor.Sockets[resident.Socket];
        float Distance(SpaceDefinition s)
        {
            float dx = MathF.Max(0, MathF.Max(s.Min[0] - post.X, post.X - s.Max[0]));
            float dz = MathF.Max(0, MathF.Max(s.Min[1] - post.Z, post.Z - s.Max[1]));
            return MathF.Sqrt(dx * dx + dz * dz);
        }
        Dictionary<string, SpaceDefinition> spaces = plan.Spaces.ToDictionary(s => s.Id, StringComparer.Ordinal);
        HashSet<string> shut = [.. layout.Locks.Select(l => l.Link), .. layout.Latch is { } latch ? [latch] : Array.Empty<string>()];
        string start = plan.Spaces.First(s => Distance(s) == 0 && resident.Socket.StartsWith(s.Id.Split('/')[0] + "/", StringComparison.Ordinal)).Id;
        HashSet<string> seen = new(StringComparer.Ordinal) { start };
        Queue<string> open = new([start]);
        while (open.TryDequeue(out string? at))
        {
            if (layout.RegionOf(at) != resident.Region) return at;
            foreach (LinkDefinition link in plan.Links.Where(l => !shut.Contains(l.Id) && l.Between.Contains(at)))
            {
                string next = link.Between[0] == at ? link.Between[1] : link.Between[0];
                if (Distance(spaces[next]) <= leash && seen.Add(next)) open.Enqueue(next);
            }
        }
        return null;
    }
}
