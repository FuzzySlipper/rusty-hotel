using Hotel.Game.Floors;
using Hotel.Game.Floors.Confirm;
using Hotel.Game.Floors.Layout;
using Hotel.Game.Floors.Mission;
using Hotel.Game.Floors.Modules;
using Rusty.Engine;

// Generated floors are confirmed by Engine navigation over their real collision for the player's own body: every
// promised route both ways, every lock held shut. Refusals retry by attempt then candidate; a doorway too narrow for
// the body is refused naming that doorway.
internal static class ConfirmChecks
{
    internal static void Run(IEngineContext engine)
    {
        void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
        var content = Owners.Content(engine);
        FloorTunings tunings = FloorTunings.Load(engine, content.Modules, content.Kit);
        CharacterControllerConfig body = content.Player.Controller(engine.Spatial);
        GenerationResult Generate(FloorSeed seed, ModuleCatalog? modules = null) =>
            FloorGenerator.Generate(engine, seed, tunings, modules ?? content.Modules, content.Kit, content.Fixtures, body);

        Dictionary<string, int> refused = new(StringComparer.Ordinal);
        int floors = 0, confirmed = 0, firstTry = 0, routes = 0, locks = 0;
        double milliseconds = 0;
        for (int depth = 1; depth <= 3; depth++)
            for (ulong run = 0; run < 12; run++)
            {
                floors++;
                GenerationResult result = Generate(FloorSeed.Current(run, depth, 0));
                foreach (string why in result.Refusals) { string key = why.Split(' ')[1].TrimEnd(':') + " " + why.Split(' ')[2].TrimEnd(':'); refused[key] = refused.GetValueOrDefault(key) + 1; }
                if (result.Floor is not { } floor) continue;
                confirmed++;
                if (floor.Candidate == 0 && floor.Attempt == 0) firstTry++;
                routes += floor.Confirmation.Routes.Length;
                locks += floor.Confirmation.Locks.Length;
                milliseconds += floor.Confirmation.Milliseconds;
                Check(floor.Confirmation.Routes.Length >= 2 && floor.Confirmation.Confirmed, "a returned floor is confirmed both ways");
            }
        Check(confirmed == floors, $"every seed yields a confirmed floor after retries; {confirmed} of {floors}: {string.Join(", ", refused)}");

        FloorSeed seed = FloorSeed.Current(run: 3, depth: 2, shift: 0);
        Check(Generate(seed).Floor!.Identity == Generate(seed).Floor!.Identity, "the same seed generates the same confirmed floor");

        // A corridor door narrower than the body: rooms cannot be entered, and the refusal names the doorway.
        ModuleCatalog narrow = content.Modules with
        {
            Doorways = content.Modules.Doorways.Select(d => d.Kind == DoorwayKind.CorridorDoor ? d with { Width = 0.4f } : d).ToArray()
        };
        FloorDraws draws = new(engine.Random, seed);
        MissionResult graph = MissionGenerator.Generate(tunings.Mission, draws);
        FloorLayouts.Laid laid = FloorLayouts.Lay(graph.Graph, narrow, tunings.Layout, content.Kit, content.Fixtures, draws);
        Check(laid.Failure is null, "the narrow-door floor still lays out: " + laid.Failure);
        Confirmation verdict = FloorConfirmation.Confirm(engine, graph.Graph, laid.Layout!, laid.Plan!, laid.Floor!, body, tunings.Generation.Navigation);
        RouteVerdict? blocked = verdict.Routes.FirstOrDefault(r => !r.Reached);
        Check(!verdict.Confirmed && blocked?.Blocking is { } at && at.StartsWith("doorway ", StringComparison.Ordinal) && at.Contains("/door"),
            $"a doorway too narrow for the body is refused naming it: {blocked}");
        Console.WriteLine($"Confirm checks passed: {confirmed} of {floors} seeds Engine-confirmed ({firstTry} first try) with {routes} promised routes and " +
            $"{locks} shut-lock checks, {milliseconds / Math.Max(1, confirmed):0} ms navigation each; refusals before success {(refused.Count == 0 ? "none" : string.Join(", ", refused.Select(r => $"{r.Key} ×{r.Value}")))}; " +
            $"narrow door refused: {blocked}.");
    }
}
