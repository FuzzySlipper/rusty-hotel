using System.Globalization;
using Hotel.Game.Content;
using Hotel.Game.Floors;
using Hotel.Game.Mechanics;
using Hotel.Game.Supplies;
using Rusty.Engine;

namespace Hotel.Game.Loot;

/// <summary>
/// A quality tier for a single worn or held item: how its name reads (<c>{item}</c>), how much its own stats are
/// scaled by, how many affixes it carries, and how often it comes up at a depth.
/// </summary>
internal sealed record QualityDefinition(string Id, string Name, float Multiply, int Affixes, DepthCurve Weight);

/// <summary>
/// An affix a generated item may carry: how its name reads (<c>{item}</c>), the classifications it can join, what it
/// adds to stats while worn, the hit contributions it brings, the effects its holder's hits put on what they strike, and
/// how often it comes up at a depth.
/// </summary>
internal sealed record AffixDefinition(string Id, string Name, string[] Classifications, StatEffect[] Stats,
    DamageContribution[] Contributions, string[] OnHit, DepthCurve Weight);

/// <summary>One possible result of a loot table: an item and how many (or nothing, with no item), and its weight at a depth.</summary>
internal sealed record LootEntry(string? Item, int[] Count, DepthCurve Weight);

/// <summary>A loot table: how many draws it makes and what each may give.</summary>
internal sealed record LootTable(string Id, int Rolls, LootEntry[] Entries);

/// <summary>
/// A resolved item: its kind and count, and for a generated single item its quality and affixes. This is what is
/// placed, carried and saved; nothing is drawn again.
/// </summary>
internal sealed record ItemRoll(string Item, int Count, string? Quality = null, string[]? Affixes = null)
{
    internal bool Generated => Quality is not null;
}

/// <summary>Keyed draws for loot: a pure function of a purpose and a key, as floor generation's draws are.</summary>
internal interface ILootDraws
{
    int Weighted(string purpose, string key, IReadOnlyList<int> weights);
    long Long(string purpose, string key, long minimum, long maximum);
}

/// <summary>Loot drawn while a floor is generated, through its <see cref="FloorDraws"/> content stage.</summary>
internal sealed class FloorLootDraws(FloorDraws draws) : ILootDraws
{
    public int Weighted(string purpose, string key, IReadOnlyList<int> weights) => draws.Weighted(FloorStage.Content, purpose, key, weights);
    public long Long(string purpose, string key, long minimum, long maximum) => draws.Long(FloorStage.Content, purpose, key, minimum, maximum);
}

/// <summary>
/// Loot drawn when something is searched in play: Engine keyed draws under <c>hotel.loot</c>, keyed by the run seed and
/// what is searched, so a search gives the same things however often it is restored and searched again.
/// </summary>
internal sealed class SearchLootDraws(IRandomService random, ulong run) : ILootDraws
{
    public int Weighted(string purpose, string key, IReadOnlyList<int> weights)
    {
        long pick = Long(purpose, key, 0, weights.Sum() - 1);
        for (int i = 0; i < weights.Count; i++)
            if ((pick -= weights[i]) < 0) return i;
        throw new InvalidOperationException("Unreachable: the pick lies within the total weight.");
    }

    public long Long(string purpose, string key, long minimum, long maximum) =>
        random.DrawKeyed(new KeyedRngRequest(run, $"hotel.loot.{purpose}", key, minimum, maximum)).Value;
}

/// <summary>The loot domain's authored files: quality tiers, affixes and loot tables, and the rules that roll them.</summary>
internal sealed record LootCatalog(QualityDefinition[] Qualities, AffixDefinition[] Affixes, LootTable[] Tables)
{
    internal const string QualitiesPath = "loot/qualities.json", AffixesPath = "loot/affixes.json", TablesPath = "loot/tables.json";

    internal static LootCatalog Load(IEngineContext engine, SuppliesDefinition supplies, MechanicsDefinition mechanics)
    {
        LootCatalog catalog = new(Authored.Read(engine, QualitiesPath, ContentJson.Default.QualityCatalog).Qualities,
            Authored.Read(engine, AffixesPath, ContentJson.Default.AffixCatalog).Affixes,
            Authored.Read(engine, TablesPath, ContentJson.Default.LootTableCatalog).Tables);
        catalog.Validate(supplies, mechanics);
        return catalog;
    }

    internal QualityDefinition? Quality(string id) => Qualities.FirstOrDefault(q => q.Id == id);
    internal AffixDefinition? Affix(string id) => Affixes.FirstOrDefault(a => a.Id == id);
    internal LootTable? Table(string id) => Tables.FirstOrDefault(t => t.Id == id);

    /// <summary>Whether an item kind is generated with a quality and affixes: a single worn or held item.</summary>
    internal static bool Generates(ItemDefinition item) => item.Wear is not null;

    /// <summary>
    /// Rolls a table at a depth: each draw picks an entry by its depth weight, then a count in its range, then for an
    /// item that is generated, a quality and that many distinct affixes that fit its classifications.
    /// </summary>
    internal ItemRoll[] Roll(string table, int depth, ILootDraws draws, string key, SuppliesDefinition supplies)
    {
        LootTable t = Table(table)!;
        List<ItemRoll> rolls = [];
        for (int r = 0; r < t.Rolls; r++)
        {
            string at = string.Create(CultureInfo.InvariantCulture, $"{key}/{r}");
            int[] weights = t.Entries.Select(e => Math.Max(0, e.Weight.At(depth))).ToArray();
            if (weights.Sum() == 0) continue;
            LootEntry entry = t.Entries[draws.Weighted($"table.{t.Id}", at, weights)];
            if (entry.Item is not { } item) continue;
            int count = (int)draws.Long($"count.{t.Id}", at, entry.Count[0], entry.Count[1]);
            rolls.Add(Generate(supplies.Item(item)!, count, depth, draws, at));
        }
        return rolls.ToArray();
    }

    /// <summary>Resolves one item kind at a depth: a generated single item draws its quality and affixes; others are as authored.</summary>
    internal ItemRoll Generate(ItemDefinition item, int count, int depth, ILootDraws draws, string key)
    {
        if (!Generates(item)) return new(item.Id, count);
        QualityDefinition quality = Qualities[draws.Weighted("quality", key, Qualities.Select(q => Math.Max(0, q.Weight.At(depth))).ToArray())];
        List<string> chosen = [];
        for (int a = 0; a < quality.Affixes; a++)
        {
            AffixDefinition[] fit = Affixes.Where(x => !chosen.Contains(x.Id) && x.Classifications.Intersect(item.Classifications).Any()
                && x.Weight.At(depth) > 0).ToArray();
            if (fit.Length == 0) break;
            chosen.Add(fit[draws.Weighted("affix", $"{key}/{a}", fit.Select(x => x.Weight.At(depth)).ToArray())].Id);
        }
        return new(item.Id, 1, quality.Id, chosen.ToArray());
    }

    /// <summary>Refuses a saved roll that this catalog could not have made for its item.</summary>
    internal bool Fits(ItemRoll roll, ItemDefinition item)
    {
        if (!roll.Generated) return roll.Affixes is null or [];
        if (!Generates(item) || roll.Count != 1 || Quality(roll.Quality!) is not { } quality || roll.Affixes is null) return false;
        return roll.Affixes.Length <= quality.Affixes && roll.Affixes.Distinct().Count() == roll.Affixes.Length &&
            roll.Affixes.All(id => Affix(id) is { } affix && affix.Classifications.Intersect(item.Classifications).Any());
    }

    /// <summary>How a resolved item reads: its quality's name around the item's, then each affix's around that.</summary>
    internal string Name(ItemDefinition item, ItemRoll? roll)
    {
        if (roll is not { Generated: true }) return item.Name;
        string name = Template.Fill(Quality(roll.Quality!)!.Name, ("item", item.Name));
        foreach (string affix in roll.Affixes!) name = Template.Fill(Affix(affix)!.Name, ("item", name));
        return name;
    }

    /// <summary>The stats a resolved worn item adds: its own, scaled by its quality, then its affixes'.</summary>
    internal IEnumerable<StatEffect> Stats(ItemDefinition item, ItemRoll? roll)
    {
        float scale = roll is { Generated: true } ? Quality(roll.Quality!)!.Multiply : 1;
        foreach (StatEffect s in item.Wear?.Stats ?? []) yield return s with { Amount = s.Amount * scale };
        foreach (string affix in roll?.Affixes ?? []) foreach (StatEffect s in Affix(affix)!.Stats) yield return s;
    }

    internal IEnumerable<DamageContribution> Contributions(ItemDefinition item, ItemRoll? roll) =>
        (item.Wear?.Contributions ?? []).Concat((roll?.Affixes ?? []).SelectMany(a => Affix(a)!.Contributions));

    internal IEnumerable<string> OnHit(ItemRoll? roll) => (roll?.Affixes ?? []).SelectMany(a => Affix(a)!.OnHit);

    private void Validate(SuppliesDefinition supplies, MechanicsDefinition mechanics)
    {
        Unique(QualitiesPath, "qualities", Qualities.Select(q => q.Id));
        Unique(AffixesPath, "affixes", Affixes.Select(a => a.Id));
        Unique(TablesPath, "tables", Tables.Select(t => t.Id));
        Authored.Require(Qualities.Length > 0, QualitiesPath, "qualities", "needs a quality.");
        for (int i = 0; i < Qualities.Length; i++)
        {
            QualityDefinition q = Qualities[i];
            Template.Check(QualitiesPath, $"qualities[{i}].name", q.Name, "item");
            Authored.Positive(QualitiesPath, $"qualities[{i}].multiply", q.Multiply);
            Authored.AtLeast(QualitiesPath, $"qualities[{i}].affixes", q.Affixes, 0);
            q.Weight.Validate(QualitiesPath, $"qualities[{i}].weight");
        }
        for (int i = 0; i < Affixes.Length; i++)
        {
            AffixDefinition a = Affixes[i];
            string at = $"affixes[{i}]";
            Template.Check(AffixesPath, $"{at}.name", a.Name, "item");
            Authored.Require(a.Classifications.Length > 0, AffixesPath, $"{at}.classifications", "names no classification.");
            for (int c = 0; c < a.Classifications.Length; c++)
                Authored.Require(supplies.Classifications.Any(k => k.Id == a.Classifications[c]), AffixesPath, $"{at}.classifications[{c}]",
                    $"unknown classification '{a.Classifications[c]}'.");
            for (int s = 0; s < a.Stats.Length; s++)
            {
                Authored.Require(mechanics.HasStat(a.Stats[s].Stat) || mechanics.DamageKinds.Any(k => a.Stats[s].Stat == ActorStats.ResistanceStat(k.Id)),
                    AffixesPath, $"{at}.stats[{s}].stat", $"'{a.Stats[s].Stat}' is not a stat or resistance.");
                Authored.Finite(AffixesPath, $"{at}.stats[{s}].amount", a.Stats[s].Amount);
            }
            DamageContribution.Validate(a.Contributions, AffixesPath, $"{at}.contributions", mechanics, fromEffect: false);
            mechanics.RequireEffects(AffixesPath, $"{at}.onHit", a.OnHit);
            a.Weight.Validate(AffixesPath, $"{at}.weight");
        }
        for (int i = 0; i < Tables.Length; i++)
        {
            LootTable t = Tables[i];
            Authored.AtLeast(TablesPath, $"tables[{i}].rolls", t.Rolls, 1);
            Authored.Require(t.Entries.Length > 0, TablesPath, $"tables[{i}].entries", "needs an entry.");
            for (int e = 0; e < t.Entries.Length; e++)
            {
                LootEntry entry = t.Entries[e];
                string at = $"tables[{i}].entries[{e}]";
                entry.Weight.Validate(TablesPath, $"{at}.weight");
                Authored.Require(entry.Count is { Length: 2 } && entry.Count[0] >= 0 && entry.Count[1] >= entry.Count[0], TablesPath, $"{at}.count",
                    "is a minimum and a maximum.");
                if (entry.Item is not { } item) continue;
                Authored.Require(supplies.Item(item) is { } kind && !kind.Deposit, TablesPath, $"{at}.item", $"'{item}' is not an item loot can give.");
                Authored.Require(entry.Count[0] >= 1 && entry.Count[1] <= supplies.Item(item)!.StackLimit, TablesPath, $"{at}.count",
                    "gives between one and a full stack.");
            }
        }
    }

    private static void Unique(string path, string field, IEnumerable<string> ids)
    {
        string? repeated = ids.GroupBy(id => id).FirstOrDefault(g => g.Count() > 1)?.Key;
        Authored.Require(repeated is null, path, field, $"id '{repeated}' appears more than once.");
    }
}

internal sealed record QualityCatalog(QualityDefinition[] Qualities);
internal sealed record AffixCatalog(AffixDefinition[] Affixes);
internal sealed record LootTableCatalog(LootTable[] Tables);
