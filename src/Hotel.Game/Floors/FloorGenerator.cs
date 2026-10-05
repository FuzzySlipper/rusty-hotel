using System.Globalization;
using Hotel.Game.Content;
using Hotel.Game.Floors.Confirm;
using Hotel.Game.Floors.Layout;
using Hotel.Game.Floors.Mission;
using Hotel.Game.Floors.Modules;
using Hotel.Game.Scene.Kit;
using Rusty.Engine;

namespace Hotel.Game.Floors;

/// <summary>How hard generation tries: layout attempts per mission graph, mission candidates per floor, and Engine navigation.</summary>
internal sealed record GenerationTuning(int Attempts, int Candidates, NavigationTuning Navigation)
{
    internal const string Path = "floors/generation.json";
}

/// <summary>Every authored file floor generation reads, loaded and checked together.</summary>
internal sealed record FloorTunings(MissionTuning Mission, LayoutTuning Layout, GenerationTuning Generation)
{
    internal static FloorTunings Load(IEngineContext engine, ModuleCatalog modules, KitDefinition kit)
    {
        GenerationTuning generation = Authored.Read(engine, GenerationTuning.Path, ContentJson.Default.GenerationTuning);
        Authored.AtLeast(GenerationTuning.Path, "attempts", generation.Attempts, 1);
        Authored.AtLeast(GenerationTuning.Path, "candidates", generation.Candidates, 1);
        Authored.Positive(GenerationTuning.Path, "navigation.cellSize", generation.Navigation.CellSize);
        Authored.Require(generation.Navigation.MaxVisited > 0, GenerationTuning.Path, "navigation.maxVisited", "must be positive.");
        return new(MissionTuning.Load(engine), LayoutTuning.Load(engine, modules, kit), generation);
    }
}

/// <summary>A generated floor, proven by the Engine: its identity, mission graph, layout, plan, built geometry and verdict.</summary>
internal sealed record GeneratedFloor(FloorIdentity Identity, MissionGraph Graph, FloorLayout Layout, FloorPlan Plan, BuiltFloor Floor,
    Confirmation Confirmation, int Candidate, int Attempt);

/// <summary>A floor, or why every candidate and attempt was refused.</summary>
internal sealed record GenerationResult(GeneratedFloor? Floor, string[] Refusals);

/// <summary>
/// Generates one floor: a mission graph per candidate, then layout attempts for it, each built and confirmed by the
/// Engine. A refused attempt tries the next attempt's draws; a candidate whose attempts all fail tries the next
/// candidate's graph; when every candidate fails, the floor fails honestly with every reason. Deterministic: the same
/// seed takes the same path to the same floor.
/// </summary>
internal static class FloorGenerator
{
    internal static GenerationResult Generate(IEngineContext engine, FloorSeed seed, FloorTunings tunings, ModuleCatalog modules,
        KitDefinition kit, FixtureCatalog fixtures, CharacterControllerConfig body)
    {
        List<string> refusals = [];
        FloorDraws root = new(engine.Random, seed);
        for (int candidate = 0; candidate < tunings.Generation.Candidates; candidate++)
        {
            MissionResult mission = MissionGenerator.Generate(tunings.Mission, root.Retry(candidate, 0));
            if (!mission.Succeeded) { refusals.Add($"c{candidate} mission: {mission.Failure[0]}"); continue; }
            for (int attempt = 0; attempt < tunings.Generation.Attempts; attempt++)
            {
                string at = string.Create(CultureInfo.InvariantCulture, $"c{candidate}/a{attempt}");
                FloorLayouts.Laid laid = FloorLayouts.Lay(mission.Graph, modules, tunings.Layout, kit, fixtures, root.Retry(candidate, attempt));
                if (laid.Failure is { } failure) { refusals.Add($"{at} layout: {failure}"); continue; }
                Confirmation confirmation = FloorConfirmation.Confirm(engine, mission.Graph, laid.Layout!, laid.Plan!, laid.Floor!, body, tunings.Generation.Navigation);
                if (!confirmation.Confirmed) { refusals.Add($"{at} navigation: {confirmation.FirstProblem}"); continue; }
                CanonicalText plan = new CanonicalText().Line("retry", CanonicalText.Number(candidate), CanonicalText.Number(attempt));
                mission.Graph.Write(plan);
                laid.Layout!.Write(plan);
                return new(new(FloorIdentity.Of(seed, plan), mission.Graph, laid.Layout, laid.Plan!, laid.Floor!, confirmation, candidate, attempt), [.. refusals]);
            }
        }
        return new(null, [.. refusals]);
    }
}
