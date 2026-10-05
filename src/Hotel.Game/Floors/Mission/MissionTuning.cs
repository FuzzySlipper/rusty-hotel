using Hotel.Game.Content;
using Rusty.Engine;

namespace Hotel.Game.Floors.Mission;

/// <summary>
/// How mission graphs grow: their size budget, how many weighted rule steps a floor at each depth takes, each rule's
/// weight and per-floor limit, and the rules every floor finishes with.
/// </summary>
internal sealed record MissionTuning(MissionBudget Budget, DepthCurve Steps, MissionRuleWeight[] Rules, MissionRule[] Finish)
{
    internal const string Path = "floors/mission.json";

    internal static MissionTuning Load(IEngineContext engine)
    {
        MissionTuning tuning = Authored.Read(engine, Path, ContentJson.Default.MissionTuning);
        tuning.Validate();
        return tuning;
    }

    internal void Validate()
    {
        Authored.AtLeast(Path, "budget.nodes", Budget.Nodes, 2);
        Authored.AtLeast(Path, "budget.edges", Budget.Edges, 1);
        Steps.Validate(Path, "steps");
        for (int i = 0; i < Rules.Length; i++)
        {
            Authored.AtLeast(Path, $"rules[{i}].weight", Rules[i].Weight, 0);
            Authored.AtLeast(Path, $"rules[{i}].limit", Rules[i].Limit, 0);
        }
        MissionRule? repeated = Rules.GroupBy(r => r.Rule).FirstOrDefault(g => g.Count() > 1)?.Key;
        Authored.Require(repeated is null, Path, "rules", $"'{repeated}' appears more than once.");
        Authored.Require(Rules.Any(r => r.Weight > 0 && r.Limit > 0), Path, "rules", "at least one rule needs a positive weight and limit.");
        MissionRule? twice = Finish.GroupBy(r => r).FirstOrDefault(g => g.Count() > 1)?.Key;
        Authored.Require(twice is null, Path, "finish", $"'{twice}' appears more than once.");
    }
}

/// <param name="Limit">The most times the rule may apply to one floor's weighted steps.</param>
internal sealed record MissionRuleWeight(MissionRule Rule, int Weight, int Limit);
