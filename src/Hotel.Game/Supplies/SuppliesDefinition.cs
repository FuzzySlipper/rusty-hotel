using System.Text.Json.Serialization;
using Hotel.Game.Content;
using Hotel.Game.Mechanics;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using EngineItemDefinition = Rusty.Engine.Mechanics.ItemDefinition;

namespace Hotel.Game.Supplies;

/// <summary>
/// The supplies domain's authored files: item kinds, the classifications and equipment slots they are worn in, what the
/// field case can carry, and player-facing text. Resource bounds are stats.
/// </summary>
internal sealed record SuppliesDefinition(ItemDefinition[] Items, ClassificationDefinition[] Classifications, SlotDefinition[] Slots,
    CapacityMetric[] Capacity, SupplyMessages Text)
{
    internal static SuppliesDefinition Load(IEngineContext engine, MechanicsDefinition mechanics)
    {
        EquipmentCatalog equipment = Authored.Read(engine, EquipmentCatalog.Path, ContentJson.Default.EquipmentCatalog);
        CapacityMetric[] capacity = Authored.Read(engine, CapacityCatalog.Path, ContentJson.Default.CapacityCatalog).Metrics;
        ItemDefinition[] items = Authored.Read(engine, ItemCatalog.Path, ContentJson.Default.ItemCatalog).Items;
        SupplyMessages text = Authored.Read(engine, SupplyMessages.Path, ContentJson.Default.SupplyMessages);
        text.Validate();
        SuppliesDefinition definition = new(items, equipment.Classifications, equipment.Slots, capacity, text);
        definition.Validate(mechanics);
        return definition;
    }

    internal ItemDefinition? Item(string id) => Items.FirstOrDefault(i => i.Id == id);

    private void Validate(MechanicsDefinition mechanics)
    {
        Unique(EquipmentCatalog.Path, "classifications", Classifications.Select(c => c.Id));
        Unique(EquipmentCatalog.Path, "slots", Slots.Select(s => s.Id));
        Unique(CapacityCatalog.Path, "metrics", Capacity.Select(m => m.Id));
        for (int i = 0; i < Slots.Length; i++)
        {
            Template.Plain(EquipmentCatalog.Path, ($"slots[{i}].name", Slots[i].Name));
            Authored.Require(Slots[i].Accepts.Length > 0, EquipmentCatalog.Path, $"slots[{i}].accepts", "names no classification.");
            for (int c = 0; c < Slots[i].Accepts.Length; c++)
                Authored.Require(Classifications.Any(k => k.Id == Slots[i].Accepts[c]), EquipmentCatalog.Path, $"slots[{i}].accepts[{c}]",
                    $"unknown classification '{Slots[i].Accepts[c]}'.");
        }
        for (int i = 0; i < Capacity.Length; i++)
        {
            Template.Plain(CapacityCatalog.Path, ($"metrics[{i}].name", Capacity[i].Name));
            Authored.AtLeast(CapacityCatalog.Path, $"metrics[{i}].limit", Capacity[i].Limit, 1);
        }
        for (int i = 0; i < Items.Length; i++)
        {
            ItemDefinition item = Items[i];
            string at = $"items[{i}]";
            Template.Plain(ItemCatalog.Path, ($"{at}.name", item.Name), ($"{at}.mark", item.Mark));
            Template.Check(ItemCatalog.Path, $"{at}.description", item.Description, item.Use?.Restores.Keys.ToArray() ?? []);
            Authored.AtLeast(ItemCatalog.Path, $"{at}.stackLimit", item.StackLimit, 1);
            Authored.Require(item.Form == ItemKind.Fungible || item.StackLimit == 1, ItemCatalog.Path, $"{at}.stackLimit",
                "a single item does not stack; set it to 1.");
            for (int c = 0; c < item.Classifications.Length; c++)
                Authored.Require(Classifications.Any(k => k.Id == item.Classifications[c]), ItemCatalog.Path, $"{at}.classifications[{c}]",
                    $"unknown classification '{item.Classifications[c]}'.");
            foreach (CapacityMetric metric in Capacity)
            {
                Authored.Require(item.Costs.TryGetValue(metric.Id, out int cost), ItemCatalog.Path, $"{at}.costs.{metric.Id}", "is missing.");
                Authored.AtLeast(ItemCatalog.Path, $"{at}.costs.{metric.Id}", cost, 0);
            }
            foreach (string metric in item.Costs.Keys)
                Authored.Require(Capacity.Any(m => m.Id == metric), ItemCatalog.Path, $"{at}.costs.{metric}", "is not a capacity metric.");
            int roles = (item.Use is null ? 0 : 1) + (item.Wear is null ? 0 : 1) + (item.Deposit ? 1 : 0);
            Authored.Require(roles == 1, ItemCatalog.Path, at, "must be used (use), worn (wear) or kept for the refuge (deposit), exactly one.");
            if (item.Use is { } use)
            {
                Authored.Require(use.Restores.Count + use.Effects.Length > 0, ItemCatalog.Path, $"{at}.use", "restores nothing and applies no effect.");
                foreach (var (track, amount) in use.Restores)
                {
                    Authored.Require(mechanics.Tracks.Any(t => t.Id == track), ItemCatalog.Path, $"{at}.use.restores.{track}", "is not a track.");
                    Authored.AtLeast(ItemCatalog.Path, $"{at}.use.restores.{track}", amount, 1);
                }
                mechanics.RequireEffects(ItemCatalog.Path, $"{at}.use.effects", use.Effects);
            }
            if (item.Wear is { } wear)
            {
                Authored.Require(item.Form == ItemKind.Unique, ItemCatalog.Path, $"{at}.form", "a worn item is a single item.");
                Authored.Within(ItemCatalog.Path, $"{at}.wear.slots", wear.Slots, 1,
                    Slots.Count(s => s.Accepts.Intersect(item.Classifications).Any()));
                for (int s = 0; s < wear.Stats.Length; s++)
                {
                    Authored.Require(mechanics.HasStat(wear.Stats[s].Stat) || mechanics.DamageKinds.Any(k => wear.Stats[s].Stat == ActorStats.ResistanceStat(k.Id)),
                        ItemCatalog.Path, $"{at}.wear.stats[{s}].stat", $"'{wear.Stats[s].Stat}' is not a stat or resistance.");
                    Authored.Finite(ItemCatalog.Path, $"{at}.wear.stats[{s}].amount", wear.Stats[s].Amount);
                }
            }
        }
    }

    private static void Unique(string path, string field, IEnumerable<string> ids)
    {
        string? repeated = ids.GroupBy(id => id).FirstOrDefault(g => g.Count() > 1)?.Key;
        Authored.Require(repeated is null, path, field, $"id '{repeated}' appears more than once.");
    }
}

/// <summary>Every carried item kind, independent of where an excursion places it.</summary>
internal sealed record ItemCatalog(ItemDefinition[] Items)
{
    internal const string Path = "supplies/items.json";
}

/// <summary>
/// One kind of item: its wording and mark, whether it stacks (<see cref="ItemKind.Fungible"/>) or is a single thing,
/// its classifications, what it costs the field case per unit, and exactly one role: used, worn, or kept for the refuge.
/// </summary>
/// <param name="Costs">Units of each capacity metric per item carried.</param>
/// <param name="Deposit">An expedition find: carried back and deposited at the refuge, never used.</param>
internal sealed record ItemDefinition(string Id, string Name, string Description, string Mark, ItemKind Form, int StackLimit,
    string[] Classifications, Dictionary<string, int> Costs, ItemUse? Use = null, ItemWear? Wear = null, bool Deposit = false)
{
    /// <summary>The description with the amounts its use restores filled in.</summary>
    internal string Details => Template.Fill(Description, (Use?.Restores ?? []).Select(r => (r.Key, (object)r.Value)).ToArray());

    /// <summary>Points one use restores to a track, zero when none.</summary>
    internal int Restores(string track) => Use?.Restores.GetValueOrDefault(track) ?? 0;

    /// <summary>The Engine's view of this item: kind, stack limit, classifications, capacity costs and, if worn, its policy and source.</summary>
    internal EngineItemDefinition Engine() => new(ItemDefinitionId.Parse(Id), Form, (ulong)StackLimit,
        Classifications.Select(ItemClassificationId.Parse),
        Costs.Where(c => c.Value > 0).Select(c => new ItemCapacityCost(CapacityMetricId.Parse(c.Key), (ulong)c.Value)),
        Wear is { } wear ? new ItemEquipmentPolicy(checked((ushort)wear.Slots),
            wear.Exclusive is { } group ? EquipmentExclusivityId.Parse(group) : null) : null,
        Wear is null ? [] : [WornSource]);

    /// <summary>The Engine source definition a worn item activates; its contributions are <see cref="ItemWear.Stats"/>.</summary>
    internal SourceDefinitionId WornSource => SourceDefinitionId.Parse($"worn.{Id}");
}

/// <summary>What using an item does: points restored to tracks at once, and effects applied to the investigator.</summary>
internal sealed record ItemUse(Dictionary<string, int> Restores, string[] Effects);

/// <summary>
/// How an item is worn: how many slots it fills, the exclusivity group no two worn items may share (null for none),
/// and what it adds to stats while worn.
/// </summary>
internal sealed record ItemWear(int Slots, string? Exclusive, StatEffect[] Stats);

/// <summary>A kind of item a slot may hold ("gloves", "ring").</summary>
internal sealed record ClassificationDefinition(string Id, string Name);

/// <summary>One equipment slot on the investigator and the classifications it accepts.</summary>
internal sealed record SlotDefinition(string Id, string Name, string[] Accepts)
{
    internal EquipmentSlotDefinition Engine() => new(EquipmentSlotId.Parse(Id), Accepts.Select(ItemClassificationId.Parse));
}

internal sealed record EquipmentCatalog(ClassificationDefinition[] Classifications, SlotDefinition[] Slots)
{
    internal const string Path = "supplies/equipment.json";
}

/// <summary>One dimension of what the field case can carry (weight, space) and its limit in item cost units.</summary>
internal sealed record CapacityMetric(string Id, string Name, int Limit);

internal sealed record CapacityCatalog(CapacityMetric[] Metrics)
{
    internal const string Path = "supplies/capacity.json";
}

/// <summary>Supply notices and refusal reasons; <see cref="NoticeSeconds"/> is how long a notice stays on the HUD.</summary>
internal sealed record SupplyMessages(float NoticeSeconds, string FindGone, string CaseFull, string TooMuch, string Collected, string Overwhelmed,
    string EmptyPocket, string TrackFull, string KeepForReturn, string WornNotUsed, string CaseChanged, string Used, string ChooseStack,
    string StackFull, string Rearranged, string ChooseAgain, string ChooseAction, string Unreadable, string Load,
    string NotWorn, string Wearing, string Exclusive, string TookOff, string NoPocketFree, string EmptySlot)
{
    internal const string Path = "supplies/messages.json";

    // Every field, with the placeholders its caller fills; any other placeholder is an authoring error.
    internal void Validate()
    {
        Authored.Positive(Path, "noticeSeconds", NoticeSeconds);
        Template.Plain(Path, ("findGone", FindGone), ("caseFull", CaseFull), ("overwhelmed", Overwhelmed), ("emptyPocket", EmptyPocket),
            ("keepForReturn", KeepForReturn), ("caseChanged", CaseChanged), ("chooseStack", ChooseStack), ("stackFull", StackFull),
            ("rearranged", Rearranged), ("chooseAgain", ChooseAgain), ("chooseAction", ChooseAction), ("unreadable", Unreadable),
            ("emptySlot", EmptySlot));
        Template.Check(Path, "tooMuch", TooMuch, "item", "metric");
        Template.Check(Path, "collected", Collected, "item", "count");
        Template.Check(Path, "trackFull", TrackFull, "track");
        Template.Check(Path, "wornNotUsed", WornNotUsed, "item");
        Template.Check(Path, "used", Used, "item");
        Template.Check(Path, "load", Load, "metric", "used", "limit");
        Template.Check(Path, "notWorn", NotWorn, "item");
        Template.Check(Path, "wearing", Wearing, "item", "slot");
        Template.Check(Path, "exclusive", Exclusive, "item", "other");
        Template.Check(Path, "tookOff", TookOff, "item");
        Template.Check(Path, "noPocketFree", NoPocketFree, "item");
    }
}

/// <summary>A collectable placement of an item in one excursion.</summary>
internal sealed record FindDefinition(string Id, string Item, int Count, float[] Point);
