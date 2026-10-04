using System.Text;
using Hotel.Game.Scene;
using Hotel.Game.Expedition;
using Hotel.Game.Route;
using Hotel.Game.Supplies;
using Hotel.Game.Combat;
using Hotel.Game.Spirits;
using Rusty.Engine;

namespace Hotel.Game.Interface;

internal sealed class HotelHud : IDisposable
{
    private readonly IEngineContext engine;
    private readonly UiStream stream;
    private readonly InterfaceTuning tuning;
    private HudState? last;
    private ulong sequence;

    internal HotelHud(IEngineContext engine, InterfaceTuning tuning)
    {
        this.engine = engine;
        this.tuning = tuning;
        stream = engine.Ui.OpenStream(new UiStreamRequest("rusty-hotel", "rusty.hotel.hud"));
    }

    internal void Publish(HotelRoute route, HotelSupplies supplies, HotelCombat combat, HotelSpirit spirit, HotelExpedition expedition)
    {
        HudState current = new(route.Location, route.Prompt, route.ReadingTitle, route.ReadingText,
            route.ReadingSequence, supplies.Revision, supplies.Message, supplies.Notice, combat.Weapon.Name, combat.ActionText, combat.Notice, combat.HurtFlash > 0, combat.HitFlash > 0, spirit.Revision, spirit.Message, spirit.Notice, spirit.Status, spirit.EquipReason, expedition.ReceiptSequence, expedition.ReceiptTitle, expedition.ReceiptText, expedition.Status);
        if (current == last) return;
        last = current;
        engine.Ui.PublishProjection(new(stream, ++sequence, Create(tuning, current, supplies, spirit)));
    }
    private sealed record HudState(string Location, string Prompt, string Title, string Text,
        ulong ReadingSequence, ulong InventoryRevision, string SupplyMessage, string SupplyNotice, string Weapon, string CombatAction, string CombatNotice, bool Hurt, bool Hit, ulong SpiritRevision, string SpiritMessage, string SpiritNotice, string SpiritStatus, string EquipReason, ulong RefugeSequence, string RefugeTitle, string RefugeText, string CheckpointStatus);
    public void Dispose() => stream.Dispose();

    private static UiValue Create(InterfaceTuning tuning, HudState state, HotelSupplies supplies, HotelSpirit spirit)
    {
        List<StructuredValueNode> nodes = [default];
        List<uint> edges = [];
        List<byte> text = [];
        (uint Offset, uint Length) Text(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            uint offset = (uint)text.Count;
            text.AddRange(bytes);
            return (offset, (uint)bytes.Length);
        }
        uint Scalar(string key, string? value = null, double number = 0)
        {
            var name = Text(key);
            var contents = Text(value ?? "");
            uint index = (uint)nodes.Count;
            nodes.Add(new(value is null ? StructuredValueKind.Number : StructuredValueKind.String,
                0, number, name.Offset, name.Length, contents.Offset, contents.Length, 0, 0));
            return index;
        }
        uint Container(string key, StructuredValueKind kind, List<uint> children)
        {
            var name = Text(key);
            uint first = (uint)edges.Count, index = (uint)nodes.Count;
            edges.AddRange(children);
            nodes.Add(new(kind, 0, 0, name.Offset, name.Length, 0, 0, first, (uint)children.Count));
            return index;
        }
        List<uint> items = [];
        for (int i = 0; i < supplies.Capacity; i++)
        {
            ItemStack? stack = supplies.Slot(i);
            ItemDefinition? item = stack is { } carried ? supplies.Item(carried.Item) : null;
            items.Add(Container("", StructuredValueKind.Object,
            [Scalar("slot", number: i), Scalar("id", item?.Id ?? ""), Scalar("name", item?.Name ?? ""),
             Scalar("description", item?.Description ?? ""), Scalar("mark", item?.Mark ?? "·"),
             Scalar("count", number: stack?.Count ?? 0), Scalar("stackLimit", number: item?.StackLimit ?? 0),
             Scalar("useReason", supplies.UseReason(i))]));
        }
        List<uint> root = [
            Scalar("refugeSequence", number: state.RefugeSequence), Scalar("refugeTitle", state.RefugeTitle),
            Scalar("refugeText", state.RefugeText), Scalar("checkpointStatus", state.CheckpointStatus),
            Scalar("location", state.Location), Scalar("focusPrompt", state.Prompt),
            Scalar("readingTitle", state.Title), Scalar("readingText", state.Text),
            Scalar("readingSequence", number: state.ReadingSequence),
            Scalar("supplyPockets", number: supplies.Capacity), Scalar("quickPockets", number: tuning.QuickPockets),
            Scalar("supplyCount", number: supplies.Occupied), Scalar("spiritCount", number: spirit.Acquired ? 1 : 0),
            Scalar("weapon", state.Weapon), Scalar("combatAction", state.CombatAction), Scalar("combatNotice", state.CombatNotice),
            Scalar("hurt", number: state.Hurt ? 1 : 0), Scalar("hit", number: state.Hit ? 1 : 0), Scalar("spirit", spirit.Equipped ? spirit.Definition.Name : spirit.Acquired ? "Pact in field case" : "No pact"),
            Scalar("spiritName", spirit.Definition.Name), Scalar("spiritDescription", spirit.Definition.Description),
            Scalar("spiritEquipped", number: spirit.Equipped ? 1 : 0), Scalar("spiritRevision", number: spirit.Revision),
            Scalar("spiritMessage", state.SpiritMessage), Scalar("spiritNotice", state.SpiritNotice),
            Scalar("spiritStatus", state.SpiritStatus), Scalar("spiritEquipReason", state.EquipReason),
            Scalar("health", number: supplies.Health), Scalar("maximumHealth", number: supplies.MaximumHealth),
            Scalar("ammo", number: supplies.Ammo), Scalar("maximumAmmo", number: supplies.MaximumAmmo),
            Scalar("summon", number: supplies.Summon), Scalar("maximumSummon", number: supplies.MaximumSummon),
            Scalar("inventoryRevision", number: state.InventoryRevision), Scalar("supplyMessage", state.SupplyMessage), Scalar("supplyNotice", state.SupplyNotice),
            Container("items", StructuredValueKind.Array, items)];
        uint firstRootEdge = (uint)edges.Count;
        edges.AddRange(root);
        nodes[0] = new(StructuredValueKind.Object, 0, 0, 0, 0, 0, 0, firstRootEdge, (uint)root.Count);
        return new(nodes.ToArray(), edges.ToArray(), 0, text.ToArray());
    }
}
