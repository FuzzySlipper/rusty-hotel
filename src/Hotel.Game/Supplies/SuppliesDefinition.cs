using System.Text.Json.Serialization;
using Hotel.Game.Content;
using Rusty.Engine;

namespace Hotel.Game.Supplies;

/// <summary>Health, ammunition and summon reserve bounds.</summary>
internal sealed record SupplyResources(int InitialHealth, int MaximumHealth, int MaximumAmmo, int MaximumSummon)
{
    internal const string Path = "supplies/resources.json";
    internal static SupplyResources Load(IEngineContext engine) => Authored.Read(engine, Path, ContentJson.Default.SupplyResources);
}

/// <summary>Every carried item kind, independent of where an excursion places it.</summary>
internal sealed record ItemCatalog(ItemDefinition[] Items)
{
    internal const string Path = "supplies/items.json";
    internal static ItemCatalog Load(IEngineContext engine) => Authored.Read(engine, Path, ContentJson.Default.ItemCatalog);
}
internal sealed record ItemDefinition(string Id, string Name, string Description, SupplyKind Kind, int StackLimit, int Amount, string Mark);

/// <summary>A collectable placement of an item in one excursion.</summary>
internal sealed record FindDefinition(string Id, string Item, int Count, float[] Point);

[JsonConverter(typeof(JsonStringEnumConverter<SupplyKind>))]
internal enum SupplyKind { Healing, Ammo, Summon, Expedition }
