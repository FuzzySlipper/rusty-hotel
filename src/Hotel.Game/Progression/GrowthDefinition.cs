using Hotel.Game.Actions;
using Hotel.Game.Content;
using Hotel.Game.Mechanics;
using Hotel.Game.Supplies;
using Rusty.Engine;

namespace Hotel.Game.Progression;

/// <summary>
/// How the investigator grows: experience thresholds for each level and what every level past the first adds, and the
/// skills that grow by use, each rank adding attributes and damage contributions. Every growth is an intrinsic Engine
/// source on the investigator's stats (see <see cref="InvestigatorGrowth"/>); nothing is spent or allocated.
/// </summary>
internal sealed record GrowthDefinition(LevelTable Levels, SkillDefinition[] Skills, GrowthMessages Text)
{
    internal const string Path = "progression/growth.json";

    internal static GrowthDefinition Load(IEngineContext engine, MechanicsDefinition mechanics, ActionCatalog actions, SuppliesDefinition supplies)
    {
        GrowthCatalog catalog = Authored.Read(engine, Path, ContentJson.Default.GrowthCatalog);
        GrowthMessages text = Authored.Read(engine, GrowthMessages.Path, ContentJson.Default.GrowthMessages);
        text.Validate();
        GrowthDefinition definition = new(catalog.Levels, catalog.Skills, text);
        definition.Validate(mechanics, actions, supplies);
        return definition;
    }

    internal SkillDefinition? Skill(string id) => Skills.FirstOrDefault(s => s.Id == id);

    /// <summary>The level an amount of experience reaches: one past each threshold met after the first.</summary>
    internal int LevelAt(int experience) => Math.Max(1, Levels.Experience.Count(threshold => experience >= threshold));

    private void Validate(MechanicsDefinition mechanics, ActionCatalog actions, SuppliesDefinition supplies)
    {
        int[] thresholds = Levels.Experience;
        Authored.Require(thresholds.Length >= 1 && thresholds[0] == 0, Path, "levels.experience", "starts at 0, the first level.");
        for (int i = 1; i < thresholds.Length; i++)
            Authored.Require(thresholds[i] > thresholds[i - 1], Path, $"levels.experience[{i}]", "must be more than the level before.");
        Stats(mechanics, Path, "levels.perLevel", Levels.PerLevel);
        string? repeated = Skills.GroupBy(s => s.Id).FirstOrDefault(g => g.Count() > 1)?.Key;
        Authored.Require(repeated is null, Path, "skills", $"id '{repeated}' appears more than once.");
        for (int i = 0; i < Skills.Length; i++)
        {
            SkillDefinition skill = Skills[i];
            string at = $"skills[{i}]";
            Template.Plain(Path, ($"{at}.name", skill.Name));
            Authored.Require(skill.Practice.DamageKinds.Length + skill.Practice.Actions.Length > 0, Path, $"{at}.practice", "grows from nothing.");
            for (int k = 0; k < skill.Practice.DamageKinds.Length; k++)
                Authored.Require(mechanics.DamageKind(skill.Practice.DamageKinds[k]) is not null, Path, $"{at}.practice.damageKinds[{k}]",
                    $"unknown damage kind '{skill.Practice.DamageKinds[k]}'.");
            actions.Require(Path, $"{at}.practice.actions", skill.Practice.Actions);
            Authored.Require(skill.Ranks.Length >= 1 && skill.Ranks[0] >= 1, Path, $"{at}.ranks", "needs at least one rank, at 1 use or more.");
            for (int r = 1; r < skill.Ranks.Length; r++)
                Authored.Require(skill.Ranks[r] > skill.Ranks[r - 1], Path, $"{at}.ranks[{r}]", "must be more than the rank before.");
            Stats(mechanics, Path, $"{at}.perRank", skill.PerRank);
            DamageContribution.Validate(skill.Contributions, Path, $"{at}.contributions", mechanics, fromEffect: false);
            Authored.Require(skill.PerRank.Length + skill.Contributions.Length > 0, Path, at, "a rank adds nothing.");
        }
        for (int i = 0; i < supplies.Items.Length; i++)
        {
            if (supplies.Items[i].Use?.Growth is not { } growth) continue;
            string at = $"items[{i}].use.growth";
            Stats(mechanics, ItemCatalog.Path, $"{at}.stats", growth.Stats);
            foreach (var (skill, uses) in growth.Practice)
            {
                Authored.Require(Skill(skill) is not null, ItemCatalog.Path, $"{at}.practice.{skill}", $"is not a skill in content/{Path}.");
                Authored.AtLeast(ItemCatalog.Path, $"{at}.practice.{skill}", uses, 1);
            }
            Authored.AtLeast(ItemCatalog.Path, $"{at}.experience", growth.Experience, 0);
            Authored.Require(growth.Stats.Length + growth.Practice.Count > 0 || growth.Experience > 0, ItemCatalog.Path, at, "teaches nothing.");
        }
    }

    private static void Stats(MechanicsDefinition mechanics, string path, string field, StatEffect[] stats)
    {
        for (int s = 0; s < stats.Length; s++)
        {
            Authored.Require(mechanics.HasStat(stats[s].Stat), path, $"{field}[{s}].stat", $"'{stats[s].Stat}' is not a stat.");
            Authored.Finite(path, $"{field}[{s}].amount", stats[s].Amount);
        }
    }
}

/// <summary>Experience needed for each level, from the first (0), and what each level past the first adds.</summary>
internal sealed record LevelTable(int[] Experience, StatEffect[] PerLevel);

/// <summary>
/// A skill grown by use: a landed hit dealing one of its damage kinds, or one of its actions landing, is one use. Each
/// rank (the uses it needs) adds <see cref="PerRank"/> and its <see cref="Contributions"/> to the investigator's hits.
/// </summary>
internal sealed record SkillDefinition(string Id, string Name, SkillPractice Practice, int[] Ranks, StatEffect[] PerRank,
    DamageContribution[] Contributions)
{
    internal int RankAt(int uses) => Ranks.Count(needed => uses >= needed);
    /// <summary>The uses that reach the last rank; practice beyond it is not counted.</summary>
    internal int Mastery => Ranks[^1];
}

/// <summary>What counts as a use of a skill: hits dealing these damage kinds, and these actions landing.</summary>
internal sealed record SkillPractice(string[] DamageKinds, string[] Actions);

/// <summary>
/// What using a relic or tome teaches: permanent stats (a relic, kept as a source for the rest of the run), uses of
/// skills (a tome) and experience.
/// </summary>
internal sealed record ItemGrowth(StatEffect[] Stats, Dictionary<string, int> Practice, int Experience = 0);

internal sealed record GrowthCatalog(LevelTable Levels, SkillDefinition[] Skills);

/// <summary>How growth is told: on using a relic or tome, and on the refuge receipt for what grew since the last return.</summary>
internal sealed record GrowthMessages(string Learned, string NothingToLearn, string Grown, string GrownLevel, string GrownSkill, string GrownJoin)
{
    internal const string Path = "progression/messages.json";

    internal void Validate()
    {
        Template.Check(Path, "learned", Learned, "item");
        Template.Check(Path, "nothingToLearn", NothingToLearn, "item");
        Template.Check(Path, "grown", Grown, "changes");
        Template.Check(Path, "grownLevel", GrownLevel, "level");
        Template.Check(Path, "grownSkill", GrownSkill, "skill", "rank");
        Template.Plain(Path, ("grownJoin", GrownJoin));
    }
}
