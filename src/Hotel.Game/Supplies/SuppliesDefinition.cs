using System.Text.Json.Serialization;
using Hotel.Game.Content;
using Rusty.Engine;

namespace Hotel.Game.Supplies;

/// <summary>The supplies domain's authored files: item kinds and player-facing text. Resource bounds are stats.</summary>
internal sealed record SuppliesDefinition(ItemDefinition[] Items, SupplyMessages Text)
{
    internal static SuppliesDefinition Load(IEngineContext engine, Mechanics.MechanicsDefinition mechanics)
    {
        ItemDefinition[] items = Authored.Read(engine, ItemCatalog.Path, ContentJson.Default.ItemCatalog).Items;
        for (int i = 0; i < items.Length; i++)
        {
            Template.Plain(ItemCatalog.Path, ($"items[{i}].name", items[i].Name), ($"items[{i}].mark", items[i].Mark));
            Template.Check(ItemCatalog.Path, $"items[{i}].description", items[i].Description, "amount");
        }
        SupplyMessages text = Authored.Read(engine, SupplyMessages.Path, ContentJson.Default.SupplyMessages);
        text.Validate();
        for (int i = 0; i < items.Length; i++)
        {
            Authored.AtLeast(ItemCatalog.Path, $"items[{i}].stackLimit", items[i].StackLimit, 1);
            Authored.AtLeast(ItemCatalog.Path, $"items[{i}].amount", items[i].Amount, 0);
            mechanics.RequireEffects(ItemCatalog.Path, $"items[{i}].effects", items[i].Effects);
            Authored.Require(items[i].Kind != SupplyKind.Expedition || items[i].Effects.Length == 0, ItemCatalog.Path, $"items[{i}].effects",
                "an expedition find is kept for the refuge, not used.");
            Authored.Require(items[i].Kind != SupplyKind.Consumable || items[i].Effects.Length > 0, ItemCatalog.Path, $"items[{i}].effects",
                "a consumable does nothing without an effect.");
        }
        return new(items, text);
    }
}


/// <summary>Every carried item kind, independent of where an excursion places it.</summary>
internal sealed record ItemCatalog(ItemDefinition[] Items)
{
    internal const string Path = "supplies/items.json";
}
/// <param name="Effects">Effects using the item applies to the investigator, by id in the effect catalog.</param>
internal sealed record ItemDefinition(string Id, string Name, string Description, SupplyKind Kind, int StackLimit, int Amount, string Mark,
    string[] Effects)
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
internal enum SupplyKind { Healing, Ammo, Summon, Consumable, Expedition }
