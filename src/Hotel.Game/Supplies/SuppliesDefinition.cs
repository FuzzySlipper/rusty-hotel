using System.Text.Json.Serialization;
using Hotel.Game.Content;
using Rusty.Engine;

namespace Hotel.Game.Supplies;

/// <summary>The supplies domain's authored files: reserve bounds, item kinds and player-facing text.</summary>
internal sealed record SuppliesDefinition(SupplyResources Resources, ItemDefinition[] Items, SupplyMessages Text)
{
    internal static SuppliesDefinition Load(IEngineContext engine)
    {
        ItemDefinition[] items = Authored.Read(engine, ItemCatalog.Path, ContentJson.Default.ItemCatalog).Items;
        for (int i = 0; i < items.Length; i++)
        {
            Template.Plain(ItemCatalog.Path, ($"items[{i}].name", items[i].Name), ($"items[{i}].mark", items[i].Mark));
            Template.Check(ItemCatalog.Path, $"items[{i}].description", items[i].Description, "amount");
        }
        SupplyMessages text = Authored.Read(engine, SupplyMessages.Path, ContentJson.Default.SupplyMessages);
        text.Validate();
        return new(Authored.Read(engine, SupplyResources.Path, ContentJson.Default.SupplyResources), items, text);
    }
}

/// <summary>Health, ammunition and summon reserve bounds.</summary>
internal sealed record SupplyResources(int InitialHealth, int MaximumHealth, int MaximumAmmo, int MaximumSummon)
{
    internal const string Path = "supplies/resources.json";
}

/// <summary>Every carried item kind, independent of where an excursion places it.</summary>
internal sealed record ItemCatalog(ItemDefinition[] Items)
{
    internal const string Path = "supplies/items.json";
}
internal sealed record ItemDefinition(string Id, string Name, string Description, SupplyKind Kind, int StackLimit, int Amount, string Mark)
{
    internal string Details => Template.Fill(Description, ("amount", Amount));
}

/// <summary>Supply notices and refusal reasons; <see cref="NoticeSeconds"/> is how long a notice stays on the HUD.</summary>
internal sealed record SupplyMessages(float NoticeSeconds, string FindGone, string CaseFull, string Collected, string Overwhelmed,
    string EmptyPocket, string HealthFull, string AmmoFull, string SummonFull, string KeepForReturn, string CaseChanged,
    string Used, string ChooseStack, string StackFull, string Rearranged, string ChooseAgain, string ChooseAction, string Unreadable)
{
    internal const string Path = "supplies/messages.json";

    // Every field, with the placeholders its caller fills; any other placeholder is an authoring error.
    internal void Validate()
    {
        Authored.Positive(Path, "noticeSeconds", NoticeSeconds);
        Template.Plain(Path, ("findGone", FindGone), ("caseFull", CaseFull), ("overwhelmed", Overwhelmed), ("emptyPocket", EmptyPocket),
            ("healthFull", HealthFull), ("ammoFull", AmmoFull), ("summonFull", SummonFull), ("keepForReturn", KeepForReturn),
            ("caseChanged", CaseChanged), ("chooseStack", ChooseStack), ("stackFull", StackFull), ("rearranged", Rearranged),
            ("chooseAgain", ChooseAgain), ("chooseAction", ChooseAction), ("unreadable", Unreadable));
        Template.Check(Path, "collected", Collected, "item", "count");
        Template.Check(Path, "used", Used, "item");
    }
}

/// <summary>A collectable placement of an item in one excursion.</summary>
internal sealed record FindDefinition(string Id, string Item, int Count, float[] Point);

[JsonConverter(typeof(JsonStringEnumConverter<SupplyKind>))]
internal enum SupplyKind { Healing, Ammo, Summon, Expedition }
