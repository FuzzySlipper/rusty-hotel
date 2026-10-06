using Hotel.Game.Floors;
using Hotel.Game.Floors.Confirm;
using Hotel.Game.Floors.Content;
using Hotel.Game.Floors.Layout;
using Hotel.Game.Floors.Mission;
using Hotel.Game.Floors.Modules;
using Rusty.Engine;

// Generated floors are furnished within their pacing budgets and confirmed by Engine navigation over their real
// collision for the player's own body: every promised route both ways, every lock held shut. Refusals retry by attempt
// then candidate; a hazard with no recovery before it, and a doorway too narrow for the body, are refused with reasons.
internal static class GenerationChecks
{
    internal static void Run(IEngineContext engine)
    {
        void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
        var content = Owners.Content(engine);
        FloorTunings tunings = FloorTunings.Load(engine, content.Modules, content.Kit, content.Fixtures, content.Supplies.Items, content.Combat.Residents);
        CharacterControllerConfig body = content.Player.Controller(engine.Spatial);
        FloorSources sources = new(content.Modules, content.Kit, content.Fixtures, content.Supplies.Items, content.Combat.Residents, body, content.Loot);
        GenerationResult Generate(FloorSeed seed) => FloorGenerator.Generate(engine, seed, tunings, sources);

        Dictionary<string, int> refused = new(StringComparer.Ordinal);
        int floors = 0, accepted = 0, firstTry = 0, routes = 0, locks = 0, finds = 0, residents = 0;
        HashSet<string> styles = new(StringComparer.Ordinal);
        GeneratedFloor? hazardous = null;
        double milliseconds = 0;
        for (int depth = 1; depth <= 3; depth++)
            for (ulong run = 0; run < 12; run++)
            {
                floors++;
                GenerationResult result = Generate(FloorSeed.Current(run, depth, 0));
                foreach (string why in result.Refusals) { string key = why.Split(' ')[1].TrimEnd(':') + " " + why.Split(' ')[2].TrimEnd(':'); refused[key] = refused.GetValueOrDefault(key) + 1; }
                if (result.Floor is not { } floor) continue;
                accepted++;
                styles.Add(floor.Layout.TrimStyle);
                if (floor.Candidate == 0 && floor.Attempt == 0) firstTry++;
                Confirmation confirmed = floor.Confirmation ?? throw new InvalidOperationException("a generated floor carries its confirmation");
                routes += confirmed.Routes.Length;
                locks += confirmed.Locks.Length;
                milliseconds += confirmed.Milliseconds;
                Check(confirmed.Routes.Length >= 2 && confirmed.Confirmed, "a returned floor is confirmed both ways");
                Check(ContentPacing.Check(floor.Content, floor.Graph, floor.Layout, floor.Plan, floor.Floor, tunings.Content, sources.Items, sources.Residents, depth) is null,
                    "every pacing budget holds on an accepted floor");
                Check(floor.Content.Finds.All(f => floor.Floor.Sockets.ContainsKey(f.Socket)) && floor.Content.Residents.All(r => floor.Floor.Sockets.ContainsKey(r.Socket)),
                    "content stands at real sockets");
                finds += floor.Content.Finds.Length;
                residents += floor.Content.Residents.Length;
                if (floor.Graph.Nodes.Any(n => n.Kind == MissionNodeKind.Hazard)) hazardous ??= floor;
            }
        Check(accepted == floors, $"every seed yields a confirmed floor after retries; {accepted} of {floors}: {string.Join(", ", refused)}");

        // A hazard with no recovery before it: the stops reachable before it lose their dressings, and the floor is refused.
        Check(hazardous is not null, "some floor has a hazard");
        GeneratedFloor h = hazardous!;
        MissionNode hazard = h.Graph.Nodes.First(n => n.Kind == MissionNodeKind.Hazard);
        IReadOnlySet<string> before = MissionReach.From(h.Graph, MissionGraph.ArrivalId, blocked: hazard.Id).Reached;
        HashSet<string> early = before.Select(n => h.Layout.Places[n]).ToHashSet();
        FloorContent bare = h.Content with
        {
            Finds = h.Content.Finds.Select(f => early.Contains(f.Id.Split('/')[0]) && f.Item == tunings.Content.Pacing.RecoveryItem ? f with { Item = "incense" } : f).ToArray()
        };
        string? unprepared = ContentPacing.Check(bare, h.Graph, h.Layout, h.Plan, h.Floor, tunings.Content, sources.Items, sources.Residents, h.Identity.Seed.Depth);
        Check(unprepared?.StartsWith("recovery:", StringComparison.Ordinal) == true, "a hazard placed before any recovery is refused: " + unprepared);

        // An extra resident on a post the player meets before any recovery is refused, wherever the mission graph puts it.
        (GeneratedFloor Floor, PlacedResident Resident)? firstMet = null;
        for (ulong run = 0; run < 12 && firstMet is null; run++)
            if (Generate(FloorSeed.Current(run, 1, 0)).Floor is { } f)
                foreach (var placement in f.Layout.Placements)
                    foreach (var socket in content.Modules.Find(placement.Module)!.Sockets.Where(s => s.Kind == ContentSocketKind.ResidentPost))
                    {
                        PlacedResident candidate = new($"{placement.Id}/{socket.Id}", "porter", $"{placement.Id}/{socket.Socket}", placement.Region);
                        if (firstMet is null && !f.Content.Residents.Any(r => r.Id == candidate.Id) &&
                            ContentPacing.RecoveryBefore(candidate, f.Content.Finds, f.Layout, f.Plan, tunings.Content.Pacing.RecoveryItem) == 0)
                            firstMet = (f, candidate);
                    }
        Check(firstMet is not null, "some floor has a post the player meets before any recovery");
        var (ef, er) = firstMet!.Value;
        string? firstEncounter = ContentPacing.Check(ef.Content with { Residents = [.. ef.Content.Residents, er] }, ef.Graph, ef.Layout, ef.Plan, ef.Floor,
            tunings.Content, sources.Items, sources.Residents, ef.Identity.Seed.Depth);
        Check(firstEncounter?.StartsWith("recovery:", StringComparison.Ordinal) == true || firstEncounter?.StartsWith("arrival:", StringComparison.Ordinal) == true,
            "a resident met before any recovery is refused: " + firstEncounter);

        // Recovery behind a lock does not count when its key lies in the resident's own room: the walk respects keys.
        (GeneratedFloor Floor, PlacedResident Resident, FloorContent Content)? behindKey = null;
        for (ulong run = 0; run < 24 && behindKey is null; run++)
            if (Generate(FloorSeed.Current(run, 2, 0)).Floor is { } f)
                foreach (var lck in f.Layout.Locks)
                {
                    PlacedFind? keyFind = f.Content.Finds.FirstOrDefault(x => x.Grants == lck.Item);
                    string keyRoom = keyFind?.Id.Split('/')[0] ?? "";
                    var keyModule = f.Layout.Placements.FirstOrDefault(p => p.Id == keyRoom);
                    var post = keyModule is null ? null : content.Modules.Find(keyModule.Module)!.Sockets.FirstOrDefault(x => x.Kind == ContentSocketKind.ResidentPost);
                    var behind = f.Layout.Placements.FirstOrDefault(p => p.Region == $"behind/{lck.Edge}" &&
                        content.Modules.Find(p.Module)!.Sockets.Any(x => x.Kind == ContentSocketKind.Find));
                    if (keyFind is null || post is null || behind is null || behindKey is not null) continue;
                    var socket = content.Modules.Find(behind.Module)!.Sockets.First(x => x.Kind == ContentSocketKind.Find);
                    PlacedFind bandage = new($"{behind.Id}/{socket.Id}", FindRole.Supplies, $"{behind.Id}/{socket.Socket}", tunings.Content.Pacing.RecoveryItem, 1, null);
                    FloorContent only = f.Content with { Finds = [keyFind, bandage] };
                    behindKey = (f, new($"{keyRoom}/{post.Id}", "porter", $"{keyRoom}/{post.Socket}", keyModule!.Region), only);
                }
        Check(behindKey is not null, "some floor's key room can hold a resident while recovery waits behind its lock");
        var (bf, br, bc) = behindKey!.Value;
        Check(ContentPacing.RecoveryBefore(br, bc.Finds, bf.Layout, bf.Plan, tunings.Content.Pacing.RecoveryItem) == 0,
            "a dressing behind a door whose key the resident guards is not recovery before it");

        FloorSeed seed = FloorSeed.Current(run: 3, depth: 2, shift: 0);
        GeneratedFloor same = Generate(seed).Floor!;
        Check(same.Identity == Generate(seed).Floor!.Identity, "the same seed generates the same confirmed floor");
        // A floor's trim style is part of what it is, and new floors are drawn in every style the layout names.
        string other = tunings.Layout.TrimStyles.First(s => s != same.Layout.TrimStyle);
        Check(FloorGenerator.Identity(same.Identity.Seed, same.Candidate, same.Attempt, same.Graph, same.Layout with { TrimStyle = other }, same.Content).PlanHash
            != same.Identity.PlanHash, "a floor's trim style is part of its identity");
        Check(styles.SetEquals(tunings.Layout.TrimStyles), $"floors are drawn in every trim style: {string.Join(", ", styles)}");

        // A corridor door narrower than the body: rooms cannot be entered, and the refusal names the doorway.
        ModuleCatalog narrow = content.Modules with
        {
            Doorways = content.Modules.Doorways.Select(d => d.Kind == DoorwayKind.CorridorDoor ? d with { Width = 0.4f } : d).ToArray()
        };
        FloorDraws draws = new(engine.Random, seed);
        MissionResult graph = MissionGenerator.Generate(tunings.Mission, draws);
        FloorLayouts.Laid laid = FloorLayouts.Lay(graph.Graph, narrow, tunings.Layout, content.Kit, content.Fixtures, draws);
        Check(laid.Failure is null, "the narrow-door floor still lays out: " + laid.Failure);
        Confirmation verdict = FloorConfirmation.Confirm(engine, graph.Graph, laid.Layout!, laid.Plan!, laid.Floor!, narrow, body, tunings.Generation.Navigation);
        // Some route is refused at a corridor door (another may first meet the latch, which is not narrowed).
        RouteVerdict? blocked = verdict.Routes.FirstOrDefault(r => !r.Reached && r.Blocking?.Contains("/door") == true);
        Check(!verdict.Confirmed && blocked?.Blocking is { } at && at.StartsWith("doorway ", StringComparison.Ordinal),
            $"a doorway too narrow for the body is refused naming it: {blocked}");
        Console.WriteLine($"Generation checks passed: {accepted} of {floors} seeds furnished within budget ({finds / accepted} finds, {residents / accepted} residents each) and Engine-confirmed ({firstTry} first try) with {routes} promised routes and " +
            $"{locks} shut-lock checks, {milliseconds / Math.Max(1, accepted):0} ms navigation each; refusals before success {(refused.Count == 0 ? "none" : string.Join(", ", refused.Select(r => $"{r.Key} ×{r.Value}")))}; " +
            $"unprepared hazard refused ({unprepared}); narrow door refused: {blocked}.");
    }
}
