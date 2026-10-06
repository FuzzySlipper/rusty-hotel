using Hotel.Game.Actions;
using Hotel.Game.Mechanics;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;

namespace Hotel.Game.Progression;

/// <summary>
/// The investigator's growth through a run: experience (and the level it reaches), uses of each skill (and their
/// ranks), and the relics taken in. It holds no stat of its own: every level, rank and relic is an intrinsic Engine
/// source it gives the investigator's stats, so <see cref="ActorStats.Explain"/> names it. Counters only move forward;
/// a checkpoint restores them whole. Adapted from rusty-dagger's <c>ProgressionState</c> (docs/reuse.md).
/// </summary>
internal sealed class InvestigatorGrowth
{
    private readonly GrowthDefinition definition;
    private readonly ActorStats stats;
    private readonly EntityId owner;
    private readonly Func<string, ItemGrowth?> relicOf;
    private readonly Dictionary<string, int> uses = new(StringComparer.Ordinal);
    private readonly List<string> relics = [];

    /// <param name="relicOf">What an item teaches, by item id, for the relics taken in.</param>
    internal InvestigatorGrowth(GrowthDefinition definition, ActorStats stats, EntityId owner, Func<string, ItemGrowth?> relicOf)
    {
        this.definition = definition; this.stats = stats; this.owner = owner; this.relicOf = relicOf;
    }

    internal GrowthDefinition Definition => definition;
    internal int Experience { get; private set; }
    internal int Level => definition.LevelAt(Experience);
    internal int Uses(string skill) => uses.GetValueOrDefault(skill);
    internal int Rank(string skill) => definition.Skill(skill)!.RankAt(Uses(skill));
    internal IReadOnlyList<string> Relics => relics;

    /// <summary>The investigator's hit contributions from skill ranks: each rank adds its skill's contributions once more.</summary>
    internal IEnumerable<ActiveContribution> Contributions => definition.Skills
        .SelectMany(s => Enumerable.Repeat(s.Contributions, s.RankAt(Uses(s.Id))).SelectMany(c => c))
        .Select(c => new ActiveContribution(c));

    internal void Award(int experience)
    {
        if (experience <= 0) return;
        Experience = checked(Experience + experience);
        Refresh();
    }

    /// <summary>
    /// One use of every skill the action practises: its own action, or, for a hit that took health, a damage kind it dealt.
    /// </summary>
    internal void Practise(ActionDefinition action, bool damaged)
    {
        bool grew = false;
        foreach (SkillDefinition skill in definition.Skills)
            if (skill.Practice.Actions.Contains(action.Id) || damaged && action.Damage.Any(p => skill.Practice.DamageKinds.Contains(p.Kind)))
                grew |= Tally(skill, 1);
        if (grew) Refresh();
    }

    /// <summary>Whether using an item would teach anything: a relic always does; a tome while a skill it names is short of mastery.</summary>
    internal bool Teaches(ItemGrowth growth) => growth.Stats.Length > 0 || growth.Experience > 0 ||
        growth.Practice.Any(p => Uses(p.Key) < definition.Skill(p.Key)!.Mastery);

    /// <summary>Takes in what a used relic or tome teaches.</summary>
    internal void Learn(string item, ItemGrowth growth)
    {
        if (growth.Stats.Length > 0) relics.Add(item);
        foreach (var (skill, amount) in growth.Practice) Tally(definition.Skill(skill)!, amount);
        Experience = checked(Experience + growth.Experience);
        Refresh();
    }

    // Practice past mastery is not counted, so a saved count stays within its skill's ranks.
    private bool Tally(SkillDefinition skill, int amount)
    {
        int before = Uses(skill.Id);
        uses[skill.Id] = Math.Min(skill.Mastery, checked(before + amount));
        return uses[skill.Id] != before;
    }

    private void Refresh() => stats.SetGrowthSources(Sources(Capture()));

    /// <summary>
    /// The sources a growth state gives: each level past the first, each skill rank, and each relic taken in, one
    /// intrinsic source apiece so each explains itself.
    /// </summary>
    internal StatSource[] Sources(GrowthState state)
    {
        List<StatSource> sources = [];
        void Add(string instance, string source, IEnumerable<StatEffect> effects, float times)
        {
            StatContributionDefinition[] contributions = effects.Where(_ => times > 0).Select(e => new StatContributionDefinition(StatId.Parse(e.Stat),
                StackingGroupId.Parse($"{instance}.{e.Stat}"), MechanicsStackingPolicy.Sum, new StatContribution.Add(e.Amount * times))).ToArray();
            if (contributions.Length > 0)
                sources.Add(new(new IntrinsicSourceIdentity(owner, SourceInstanceId.Parse(instance)), SourceDefinitionId.Parse(source), 0, contributions));
        }
        Add("growth.level", "growth.level", definition.Levels.PerLevel, definition.LevelAt(state.Experience) - 1);
        foreach (SkillDefinition skill in definition.Skills)
            Add($"growth.skill.{skill.Id}", $"growth.skill.{skill.Id}", skill.PerRank, skill.RankAt(state.Uses.GetValueOrDefault(skill.Id)));
        for (int i = 0; i < state.Relics.Length; i++)
            Add($"growth.relic.{i}", $"growth.relic.{state.Relics[i]}", relicOf(state.Relics[i])!.Stats, 1);
        return sources.ToArray();
    }

    internal GrowthState Capture() => new(Experience, uses.Where(u => u.Value > 0).OrderBy(u => u.Key, StringComparer.Ordinal)
        .ToDictionary(u => u.Key, u => u.Value, StringComparer.Ordinal), relics.ToArray());

    /// <summary>Refuses negative experience, an unknown skill or practice past its mastery, and a relic that teaches no stats.</summary>
    internal void Validate(GrowthState state)
    {
        if (state is null || state.Uses is null || state.Relics is null || state.Experience < 0)
            throw new InvalidOperationException("Checkpoint growth is missing or negative.");
        foreach (var (skill, count) in state.Uses)
            if (definition.Skill(skill) is not { } known || count < 0 || count > known.Mastery)
                throw new InvalidOperationException($"Checkpoint skill '{skill}' is unknown or past its mastery.");
        foreach (string relic in state.Relics)
            if (relicOf(relic) is not { Stats.Length: > 0 })
                throw new InvalidOperationException($"Checkpoint relic '{relic}' is not a relic.");
    }

    /// <summary>Restores a validated state and gives the stats its sources, before their tracks are restored under them.</summary>
    internal void Restore(GrowthState state)
    {
        Experience = state.Experience;
        uses.Clear();
        foreach (var (skill, count) in state.Uses) uses[skill] = count;
        relics.Clear();
        relics.AddRange(state.Relics);
        Refresh();
    }

    internal void Reset() => Restore(new(0, [], []));
}

/// <summary>Saved growth: experience, uses by skill id, and the relics taken in, by item id, in the order they were.</summary>
internal sealed record GrowthState(int Experience, Dictionary<string, int> Uses, string[] Relics);
