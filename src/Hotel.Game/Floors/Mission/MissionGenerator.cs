namespace Hotel.Game.Floors.Mission;

/// <summary>A rule application that was refused, with why. The graph it was proposed against is unchanged.</summary>
internal sealed record MissionRejection(MissionRule Rule, string Key, MissionProblem[] Problems);

/// <summary>
/// The outcome of growing one floor's mission graph: the last accepted graph, the refused steps along the way, and
/// when a finishing rule could not apply, why the floor failed.
/// </summary>
internal sealed record MissionResult(MissionGraph Graph, MissionRejection[] Rejected, MissionProblem[] Failure)
{
    internal bool Succeeded => Failure.Length == 0;
}

/// <summary>
/// Grows a floor's mission graph: weighted rule steps, as many as its depth allows, then the finishing rules every
/// floor needs. Each step is fail-atomic and validated whole. A pure function of tuning and draws.
/// </summary>
internal static class MissionGenerator
{
    /// <param name="keepsShortcut">
    /// The floor is shifting around a shortcut the player unlatched: its shortcut must stand where no lock guards it.
    /// </param>
    internal static MissionResult Generate(MissionTuning tuning, FloorDraws draws, bool keepsShortcut = false)
    {
        MissionGraph graph = MissionGraph.Initial();
        List<MissionRejection> rejected = [];
        int steps = tuning.Steps.At(draws.Seed.Depth);
        for (int i = 0; i < steps; i++)
        {
            string key = $"s{i}";
            MissionRuleWeight[] open = tuning.Rules.Where(r => r.Weight > 0 && graph.Applied(r.Rule) < r.Limit).ToArray();
            if (open.Length == 0) break;
            MissionRule rule = open[draws.Weighted(FloorStage.Graph, "rule", key, open.Select(r => r.Weight).ToArray())].Rule;
            graph = Apply(graph, rule, draws, key, tuning.Budget, rejected, keepsShortcut);
        }
        for (int i = 0; i < tuning.Finish.Length; i++)
        {
            int before = rejected.Count;
            graph = Apply(graph, tuning.Finish[i], draws, $"f{i}", tuning.Budget, rejected, keepsShortcut);
            if (rejected.Count > before) return new(graph, [.. rejected], rejected[^1].Problems);
        }
        return new(graph, [.. rejected], MissionValidation.Check(graph, tuning.Budget));
    }

    /// <summary>Applies one rule if the whole proposed graph is valid; otherwise records why and keeps the graph.</summary>
    internal static MissionGraph Apply(MissionGraph graph, MissionRule rule, FloorDraws draws, string key, MissionBudget budget,
        List<MissionRejection> rejected, bool keepsShortcut = false)
    {
        var (proposed, summary, refusal) = MissionRules.Propose(graph, rule, draws, key, keepsShortcut);
        MissionProblem[] problems = refusal is not null ? [refusal] : MissionValidation.Check(proposed!, budget);
        if (problems.Length > 0)
        {
            rejected.Add(new(rule, key, problems));
            return graph;
        }
        return proposed! with { Steps = [.. graph.Steps, new(rule, key, summary)] };
    }
}
