using System.Numerics;
using Hotel.Game.Floors.Mission;
using Hotel.Game.Floors.Modules;
using Hotel.Game.Scene.Kit;

namespace Hotel.Game.Floors.Layout;

/// <summary>
/// Lays a mission graph out as hotel modules, in phases: the stair core and its corridor; a module for every mission
/// place, in breadth-first order from arrival, joined socket to socket to the corridor its parent stands on; the
/// service passage the shortcut returns by; then fill rooms to the floor's size. Every choice is drawn here, through
/// <see cref="FloorDraws"/> under the embedding stage, and resolved into the layout. A phase that cannot place what it
/// needs fails the floor with its reason.
/// </summary>
/// <remarks>The scored socket growth follows CraftSurvive's modular dungeon assembly; see docs/reuse.md.</remarks>
internal static class FloorEmbedding
{
    /// <param name="kept">When a floor shifts, the pieces it keeps: placed first, as they were, and grown around.</param>
    internal static LayoutResult Embed(MissionGraph graph, ModuleCatalog catalog, LayoutTuning tuning, FloorDraws draws, KeptSet? kept = null)
    {
        Assembly assembly = new(catalog, tuning, draws, kept);
        string? failure = assembly.Grow(graph);
        return failure is null ? new(assembly.Layout(), null) : new(null, failure);
    }

    /// <summary>
    /// The lock region of every place: places reached from arrival without crossing a locked edge share the open region;
    /// each locked edge into unreached places starts the region behind it. Latches lead back, so they join no region.
    /// </summary>
    internal static Dictionary<string, string> Regions(MissionGraph graph)
    {
        Dictionary<string, string> regions = new(StringComparer.Ordinal);
        MissionEdge[] edges = graph.Edges.OrderBy(e => e.Id, StringComparer.Ordinal).ToArray();
        void Flood(string start, string region)
        {
            Queue<string> open = new([start]);
            regions[start] = region;
            while (open.TryDequeue(out string? at))
                foreach (MissionEdge e in edges.Where(e => e.Kind is MissionEdgeKind.Open or MissionEdgeKind.OneWay && (e.From == at || e.To == at)))
                    if (regions.TryAdd(e.From == at ? e.To : e.From, region)) open.Enqueue(e.From == at ? e.To : e.From);
        }
        Flood(MissionGraph.ArrivalId, OpenRegion);
        for (bool changed = true; changed;)
        {
            changed = false;
            foreach (MissionEdge e in edges.Where(e => e.Kind == MissionEdgeKind.Locked))
                if (regions.ContainsKey(e.From) != regions.ContainsKey(e.To))
                {
                    Flood(regions.ContainsKey(e.From) ? e.To : e.From, $"behind/{e.Id}");
                    changed = true;
                }
        }
        return regions;
    }

    internal const string OpenRegion = "open";

    private sealed record Open(PlacedDoorway Doorway, string Region);

    private sealed class Assembly(ModuleCatalog catalog, LayoutTuning tuning, FloorDraws draws, KeptSet? kept)
    {
        // New placements of a shift are numbered apart from the kept ones, so no id is reused for a different room.
        private readonly string prefix = draws.Seed.Shift == 0 ? "p" : $"s{draws.Seed.Shift}p";
        private int numbered;
        // A corridor piece must leave an archway with this much free floor beyond it, so the corridor can go on.
        private const float Tolerance = 1e-3f, PorchDepth = 0.5f, ReservedDepth = 2.5f, WayOnDepth = 4f;
        private readonly List<PlacedModule> placed = [];
        private readonly Dictionary<string, string> placedRegions = new(StringComparer.Ordinal);
        private readonly List<(Vector2 Min, Vector2 Max)> taken = [];
        // Floor kept clear in front of a doorway something will need later; only modules of the owning region may use it.
        private readonly List<(Vector2 Min, Vector2 Max, string? Region, PlacedDoorway For)> reserved = [];
        private readonly List<Open> open = [];
        // Every doorway placed so far, joined or not, with its region: a doorway given up on can still meet a later one.
        private readonly List<Open> doorways = [];
        private readonly Dictionary<string, string> places = new(StringComparer.Ordinal), hosts = new(StringComparer.Ordinal);
        // A gate's placement and the locked edge its inner door stands for.
        private readonly Dictionary<string, string> behind = new(StringComparer.Ordinal), gated = new(StringComparer.Ordinal);
        private readonly List<LayoutLock> locks = [];
        private readonly Dictionary<string, string> beyond = new(StringComparer.Ordinal);
        private ServicePassage.Route? passage;
        private string? latch;

        internal FloorLayout Layout() => new(
            placed.Select(p => new LayoutPlacement(p.Id, p.Module.Id, Corner(p).X, Corner(p).Y, p.Transform.Turn, placedRegions[p.Id])).ToArray(),
            passage?.Spaces ?? [], passage?.Links ?? [], passage?.Fixtures ?? [], places, [.. locks], latch, beyond, kept?.Doors ?? [],
            // A floor keeps its trim style and decor through every shift; a new floor draws each.
            kept?.TrimStyle ?? tuning.TrimStyles[draws.Index(FloorStage.Embedding, "trim.style", "floor", tuning.TrimStyles.Length)],
            kept?.Decor ?? tuning.Decors[draws.Index(FloorStage.Embedding, "decor", "floor", tuning.Decors.Length)]);

        private static Vector2 Corner(PlacedModule p) => p.Transform.Corner;

        internal string? Grow(MissionGraph graph)
        {
            Dictionary<string, string> regions = Regions(graph);
            Dictionary<string, MissionNode> nodes = graph.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);

            // Phase 1: the stair core, standing for arrival; on a shift, every kept piece as it was.
            Dictionary<string, string> keptPlaces = new(StringComparer.Ordinal);
            if (kept is not null)
            {
                foreach (LayoutPlacement p in kept.Placements) PlaceKept(p);
                Prune();
                foreach (var (kind, placement) in kept.Places)
                    if (graph.Nodes.FirstOrDefault(n => n.Kind == kind) is { } node)
                    {
                        if (regions[node.Id] != OpenRegion) return $"kept: '{node.Id}' is behind a lock in this graph, but its kept room is not.";
                        keptPlaces[node.Id] = placement;
                    }
                if (kept.Latch is not null)
                {
                    passage = new(kept.Passage, kept.PassageLinks, kept.PassageFixtures, kept.Latch);
                    latch = kept.Latch;
                    taken.AddRange(kept.Passage.Select(s => (new Vector2(s.Min[0], s.Min[1]), new Vector2(s.Max[0], s.Max[1]))));
                    Prune();
                }
                places[MissionGraph.ArrivalId] = hosts[MissionGraph.ArrivalId] = kept.Places[MissionNodeKind.Arrival];
            }
            else
            {
                PlaceRole arrivalRole = tuning.Role(MissionNodeKind.Arrival);
                ModuleDefinition core = catalog.Find(Pick(arrivalRole.Modules, "arrival", "core"))!;
                PlacedModule landing = Place(core, Vector2.Zero, draws.Index(FloorStage.Embedding, "arrival.turn", "core", 4), OpenRegion, null);
                places[MissionGraph.ArrivalId] = hosts[MissionGraph.ArrivalId] = landing.Id;
            }
            if (passage is null) Reserve(placed.First(p => p.Id == places[MissionGraph.ArrivalId]), DoorwayKind.ServiceDoor);

            // Phase 2: a module for every mission place, region by region from arrival, each region's gates last so the
            // corridor stays open for everything else on that side of them.
            foreach (var (id, parentId, edge) in Order(graph, regions, nodes))
            {
                MissionNode node = nodes[id], parent = nodes[parentId];
                // A kept room already stands; it hangs off the kept corridor it is joined to.
                if (keptPlaces.TryGetValue(id, out string? keptPlacement))
                {
                    places[id] = keptPlacement;
                    hosts[id] = Neighbour(keptPlacement) ?? keptPlacement;
                    continue;
                }
                PlaceRole role = tuning.Role(node.Kind);
                bool locked = edge.Kind == MissionEdgeKind.Locked;
                bool throughGate = locked && gated.GetValueOrDefault(parentId) == edge.Id;
                if (locked && !throughGate && role.Joins == DoorwayKind.Archway)
                    return $"rooms: '{id}' is behind a locked edge but is a corridor place not reached through a gate.";
                string host = throughGate ? behind[parentId] : hosts[parentId];
                string region = throughGate || !locked ? regions[id] : regions[parentId];
                float distance = node.Kind == MissionNodeKind.Hazard ? tuning.HazardDistance : 0;
                if (Attach(host, role.Joins, role.Modules, region, id, regions[id], role.Joins == DoorwayKind.Archway, distance) is not { } attached)
                    return $"rooms: no room for '{id}' ({node.Kind}) off {host}.";
                var (module, join) = attached;
                places[id] = module.Id;
                hosts[id] = role.Joins == DoorwayKind.Archway ? module.Id : join.Placement.Id;
                if (locked && !throughGate) locks.Add(new(edge.Id, join.LinkId, edge.Item!));
                if (node.Kind == MissionNodeKind.Gate)
                {
                    // The gate's other archway opens on the region behind its locked edge, and its inner door is the lock.
                    // Its own locked edge, from the gate onward.
                    MissionEdge? door = graph.Edges.FirstOrDefault(e => e.Id == node.Gates);
                    behind[id] = module.Id;
                    if (door is not null)
                    {
                        gated[id] = door.Id;
                        Retag(module, regions[door.To]);
                        if (open.FirstOrDefault(o => o.Doorway.Placement == module) is { } far)
                        {
                            this.beyond[far.Doorway.Space] = regions[door.To];
                            var (min, max) = Porch(far.Doorway, WayOnDepth);
                            reserved.Add((min, max, regions[door.To], far.Doorway));
                        }
                        locks.Add(new(door.Id, module.Name(module.Module.Links.Single(l => l.Kind == LinkKind.Door).Id), door.Item!));
                    }
                }
                if (node.Kind == MissionNodeKind.Shortcut) Reserve(module, DoorwayKind.ServiceDoor);
            }

            // Phase 3: the service passage from each shortcut back to the stair core's service door, latched at the core;
            // a kept passage already runs there.
            foreach (MissionEdge edge in graph.Edges.Where(e => e.Kind == MissionEdgeKind.Latch).OrderBy(e => e.Id, StringComparer.Ordinal))
            {
                if (kept?.Latch is not null && keptPlaces.ContainsKey(edge.From)) continue;
                if (passage is not null) return "service: only one shortcut passage is supported per floor.";
                PlacedModule from = placed.First(p => p.Id == places[edge.From]), to = placed.First(p => p.Id == places[edge.To]);
                PlacedDoorway? start = OpenOf(from, DoorwayKind.ServiceDoor), end = OpenOf(to, DoorwayKind.ServiceDoor);
                if (start is null || end is null) return $"service: '{edge.From}' or '{edge.To}' has no free service door.";
                reserved.RemoveAll(r => r.Region is null);
                passage = ServicePassage.Find(start, end, taken, tuning, catalog.Cell);
                if (passage is null) return $"service: no clear passage from '{edge.From}' back to '{edge.To}'.";
                taken.AddRange(passage.Spaces.Select(s => (new Vector2(s.Min[0], s.Min[1]), new Vector2(s.Max[0], s.Max[1]))));
                open.RemoveAll(o => o.Doorway == start || o.Doorway == end);
                latch = passage.Latch;
                Prune();
            }

            // Phase 4: fill rooms until the floor has its size, now and then branching the corridor somewhere new.
            int target = tuning.Rooms.At(draws.Seed.Depth);
            for (int i = 0; Rooms() < target && i < tuning.Tries * target; i++)
            {
                Open[] doors = Sorted(open.Where(o => o.Doorway.Doorway.Kind == DoorwayKind.CorridorDoor));
                string key = $"fill{i}";
                bool branch = draws.OneIn(FloorStage.Embedding, "fill.branch", key, tuning.BranchOneIn);
                if (doors.Length > 0 && !branch)
                {
                    Open at = doors[draws.Index(FloorStage.Embedding, "fill.door", key, doors.Length)];
                    if (TryAttach(at, tuning.Fill, at.Region, key, at.Region) is null) open.Remove(at);
                    continue;
                }
                Open[] ends = Sorted(open.Where(o => o.Doorway.Doorway.Kind == DoorwayKind.Archway));
                if (ends.Length == 0) break;
                Open end = ends[draws.Index(FloorStage.Embedding, "fill.spine", key, ends.Length)];
                if (TryAttach(end, tuning.Spine, end.Region, key, end.Region, corridor: true) is null) open.Remove(end);
            }
            if (Rooms() < target) return $"fill: only {Rooms()} of {target} rooms fit.";
            return null;
        }

        private int Rooms() => placed.Count(p => !p.Module.Tags.Contains(ModuleTag.Corridor) && !p.Module.Tags.Contains(ModuleTag.StairCore));

        // The breadth-first tree, ordered by region (in the order regions open), gates after the rest of their region.
        // A place found from a gate by an open edge stands on the gate's near side, so it hangs off the gate's parent.
        private static IEnumerable<(string Id, string Parent, MissionEdge Edge)> Order(MissionGraph graph, Dictionary<string, string> regions,
            Dictionary<string, MissionNode> nodes)
        {
            var tree = Tree(graph).ToList();
            Dictionary<string, (string Parent, MissionEdge Edge)> parents = tree.ToDictionary(t => t.Id, t => (t.Parent, t.Edge), StringComparer.Ordinal);
            List<string> regionOrder = [OpenRegion, .. tree.Select(t => regions[t.Id]).Distinct().Where(r => r != OpenRegion)];
            return tree.Select((t, index) =>
                {
                    var (parent, edge) = (t.Parent, t.Edge);
                    while (nodes[parent].Kind == MissionNodeKind.Gate && edge.Kind != MissionEdgeKind.Locked && parents.ContainsKey(parent))
                        parent = parents[parent].Parent;
                    return (t.Id, Parent: parent, Edge: edge, Index: index);
                })
                // A room behind its own locked door joins its parent's corridor, so it is placed with its parent's region.
                .OrderBy(t => regionOrder.IndexOf(t.Edge.Kind == MissionEdgeKind.Locked && nodes[t.Parent].Kind != MissionNodeKind.Gate
                    ? regions[t.Parent] : regions[t.Id]))
                .ThenBy(t => nodes[t.Id].Kind == MissionNodeKind.Gate ? 1 : 0).ThenBy(t => t.Index)
                .Select(t => (t.Id, t.Parent, t.Edge));
        }

        // Breadth-first tree over the mission graph from arrival: each place with the place and edge it was found by.
        private static IEnumerable<(string Id, string Parent, MissionEdge Edge)> Tree(MissionGraph graph)
        {
            MissionEdge[] edges = graph.Edges.Where(e => e.Kind != MissionEdgeKind.Latch).OrderBy(e => e.Id, StringComparer.Ordinal).ToArray();
            HashSet<string> seen = new(StringComparer.Ordinal) { MissionGraph.ArrivalId };
            Queue<string> queue = new([MissionGraph.ArrivalId]);
            while (queue.TryDequeue(out string? at))
                foreach (MissionEdge e in edges.Where(e => e.From == at || e.To == at))
                {
                    string next = e.From == at ? e.To : e.From;
                    if (!seen.Add(next)) continue;
                    queue.Enqueue(next);
                    yield return (next, at, e);
                }
        }

        private string Pick(ModuleWeight[] weights, string purpose, string key) =>
            weights[draws.Weighted(FloorStage.Embedding, purpose, key, weights.Select(w => w.Weight).ToArray())].Module;

        /// <summary>
        /// Joins one of the modules to an open doorway of the right kind in the region, the host's own doorways first.
        /// When none takes, the corridor grows by a spine piece from the host and tries again.
        /// </summary>
        private (PlacedModule Module, (PlacedModule Placement, string LinkId) Join)? Attach(string host, DoorwayKind kind,
            ModuleWeight[] choices, string region, string key, string moduleRegion, bool corridor, float distance = 0)
        {
            // A doorway nearer the stair core than the distance is grown past rather than used.
            Vector2 core = placed[0].Transform.Corner + placed[0].Transform.Footprint / 2;
            for (int attempt = 0; attempt < tuning.Tries; attempt++)
            {
                foreach (Open at in Preferring(host, open.Where(o => o.Doorway.Doorway.Kind == kind && o.Region == region &&
                    Vector2.Distance(o.Doorway.Point, core) >= distance), $"{key}/{attempt}"))
                    if (TryAttach(at, choices, region, $"{key}/{attempt}", moduleRegion, corridor) is { } done)
                        return (done.Module, (at.Doorway.Placement, done.LinkId));
                bool grown = false;
                foreach (Open end in Preferring(host, open.Where(o => o.Doorway.Doorway.Kind == DoorwayKind.Archway && o.Region == region), $"{key}/spine{attempt}"))
                    if (TryAttach(end, tuning.Spine, region, $"{key}/spine{attempt}", region, corridor: true) is { } spine)
                    {
                        host = spine.Module.Id;
                        grown = true;
                        break;
                    }
                if (!grown) return null;
            }
            return null;
        }

        // The host's own doorways first, then the rest of the region, each group in a drawn order.
        private Open[] Preferring(string host, IEnumerable<Open> candidates, string key)
        {
            Open[] sorted = Shuffled(Sorted(candidates), key);
            return [.. sorted.Where(o => o.Doorway.Placement.Id == host), .. sorted.Where(o => o.Doorway.Placement.Id != host)];
        }

        private Open[] Shuffled(Open[] items, string key) =>
            items.OrderBy(o => draws.Long(FloorStage.Embedding, "order", $"{key}/{o.Doorway.Id}", 0, 1_000_000)).ThenBy(o => o.Doorway.Id, StringComparer.Ordinal).ToArray();

        private static Open[] Sorted(IEnumerable<Open> candidates) => candidates.OrderBy(o => o.Doorway.Id, StringComparer.Ordinal).ToArray();

        /// <summary>
        /// Every turn and doorway of every choice that meets the open doorway face to face on free floor is scored by its
        /// weight, by how much free floor lies beyond its other doorways, and by a drawn jitter; the best is placed.
        /// </summary>
        private (PlacedModule Module, string LinkId)? TryAttach(Open at, ModuleWeight[] choices, string region, string key, string moduleRegion,
            bool corridor = false)
        {
            (ModuleDefinition Module, Vector2 Corner, int Turn, PlacedDoorway Mine)? best = null;
            double bestScore = double.NegativeInfinity;
            foreach (ModuleWeight choice in choices.Where(c => c.Weight > 0))
            {
                ModuleDefinition module = catalog.Find(choice.Module)!;
                if (module.Tags.Contains(ModuleTag.SetPieceOnce) && placed.Any(p => p.Module == module)) continue;
                for (int turn = 0; turn < 4; turn++)
                    foreach (PlacedDoorway probe in ModuleRealizer.Doorways(new("probe", new(module, Vector2.Zero, turn)), catalog))
                    {
                        if (probe.Doorway.Kind != at.Doorway.Doorway.Kind || probe.Edge != PlacedDoorway.Opposite(at.Doorway.Edge)) continue;
                        Vector2 corner = at.Doorway.Point - probe.Point;
                        ModuleTransform t = new(module, corner, turn);
                        if (!Free(corner, corner + t.Footprint, region)) continue;
                        PlacedDoorway[] mine = ModuleRealizer.Doorways(new("probe", t), catalog).ToArray();
                        if (mine.Any(d => doorways.Any(o => o.Region != region && o.Doorway.Mates(d)))) continue;
                        PlacedDoorway[] onward = mine.Where(d => !Meets(at.Doorway, d) && Free(Porch(d, WayOnDepth).Min, Porch(d, WayOnDepth).Max, region)).ToArray();
                        if (corridor && !onward.Any(d => d.Doorway.Kind == DoorwayKind.Archway)) continue;
                        double score = choice.Weight + 2 * onward.Length
                            + draws.Long(FloorStage.Embedding, "attach", $"{key}/{at.Doorway.Id}/{module.Id}/{turn}/{probe.Doorway.Id}", 0, 999) / 1000.0 * 3;
                        if (score > bestScore) { bestScore = score; best = (module, corner, turn, probe); }
                    }
            }
            if (best is not { } chosen) return null;
            PlacedModule placedModule = Place(chosen.Module, chosen.Corner, chosen.Turn, moduleRegion, at);
            return (placedModule, ModuleRealizer.JoinId(at.Doorway.Id, placedModule.Name(chosen.Mine.Doorway.Id)));
        }

        // A kept piece stands where it stood, under its old id, in the open region.
        private void PlaceKept(LayoutPlacement p)
        {
            PlacedModule module1 = new(p.Id, new(catalog.Find(p.Module)!, new(p.X, p.Z), p.Turn));
            placed.Add(module1);
            placedRegions[module1.Id] = OpenRegion;
            taken.Add((module1.Transform.Corner, module1.Transform.Corner + module1.Transform.Footprint));
            foreach (PlacedDoorway d in ModuleRealizer.Doorways(module1, catalog))
            {
                doorways.Add(new(d, OpenRegion));
                Open? mate = open.FirstOrDefault(o => o.Doorway.Mates(d));
                if (mate is not null) open.Remove(mate);
                else open.Add(new(d, OpenRegion));
            }
        }

        // The placement a kept room's doorway is joined to.
        private string? Neighbour(string placement) => doorways
            .Where(d => d.Doorway.Placement.Id == placement)
            .SelectMany(d => doorways.Where(o => o.Doorway.Placement.Id != placement && o.Doorway.Mates(d.Doorway)))
            .Select(o => o.Doorway.Placement.Id).FirstOrDefault();

        private PlacedModule Place(ModuleDefinition module, Vector2 corner, int turn, string region, Open? joined)
        {
            PlacedModule module1 = new($"{prefix}{numbered++}", new(module, corner, turn));
            placed.Add(module1);
            placedRegions[module1.Id] = region;
            taken.Add((corner, corner + module1.Transform.Footprint));
            if (joined is not null)
            {
                open.Remove(joined);
                reserved.RemoveAll(r => r.For == joined.Doorway);
            }
            foreach (PlacedDoorway d in ModuleRealizer.Doorways(module1, catalog))
            {
                doorways.Add(new(d, region));
                if (joined is not null && Meets(joined.Doorway, d)) continue;
                Open? mate = open.FirstOrDefault(o => o.Doorway.Mates(d));
                if (mate is not null) { open.Remove(mate); continue; }
                open.Add(new(d, region));
            }
            Prune();
            return module1;
        }

        // A gate's far archway belongs to the region behind it.
        private void Retag(PlacedModule gate, string region)
        {
            for (int i = 0; i < open.Count; i++)
                if (open[i].Doorway.Placement == gate) open[i] = open[i] with { Region = region };
            for (int i = 0; i < doorways.Count; i++)
                if (doorways[i].Doorway.Placement == gate && open.Any(o => o.Doorway == doorways[i].Doorway)) doorways[i] = doorways[i] with { Region = region };
        }

        private void Reserve(PlacedModule module, DoorwayKind kind)
        {
            if (OpenOf(module, kind) is { } d) { var (min, max) = Porch(d, ReservedDepth); reserved.Add((min, max, null, d)); }
        }

        private PlacedDoorway? OpenOf(PlacedModule module, DoorwayKind kind) =>
            open.FirstOrDefault(o => o.Doorway.Placement == module && o.Doorway.Doorway.Kind == kind)?.Doorway;

        // Open doorways now facing a placed wall can never be joined.
        private void Prune() => open.RemoveAll(o => !Free(Porch(o.Doorway, PorchDepth).Min, Porch(o.Doorway, PorchDepth).Max, o.Region, ignoreReserved: true));

        private static bool Meets(PlacedDoorway a, PlacedDoorway b) => Vector2.Distance(a.Point, b.Point) < Tolerance;

        private bool Free(Vector2 min, Vector2 max, string? region, bool ignoreReserved = false)
        {
            if (min.X < -tuning.Extent || min.Y < -tuning.Extent || max.X > tuning.Extent || max.Y > tuning.Extent) return false;
            bool Overlaps(Vector2 rMin, Vector2 rMax) =>
                Math.Min(max.X, rMax.X) - Math.Max(min.X, rMin.X) > Tolerance && Math.Min(max.Y, rMax.Y) - Math.Max(min.Y, rMin.Y) > Tolerance;
            return !taken.Any(t => Overlaps(t.Min, t.Max)) &&
                (ignoreReserved || !reserved.Any(r => (r.Region is null || r.Region != region) && Overlaps(r.Min, r.Max)));
        }

        // The floor just outside a doorway, as wide as its opening.
        internal static (Vector2 Min, Vector2 Max) Porch(PlacedDoorway d, float depth)
        {
            float half = (d.Style?.Width ?? 1) / 2;
            Vector2 outward = d.Edge switch
            {
                WallEdge.North => -Vector2.UnitY, WallEdge.South => Vector2.UnitY, WallEdge.West => -Vector2.UnitX, _ => Vector2.UnitX
            };
            Vector2 across = new(MathF.Abs(outward.Y), MathF.Abs(outward.X));
            Vector2 a = d.Point - across * half, b = d.Point + across * half + outward * depth;
            return (Vector2.Min(a, b), Vector2.Max(a, b));
        }
    }
}
