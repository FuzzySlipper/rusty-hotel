using Hotel.Game.Combat;
using Hotel.Game.Expedition;
using Hotel.Game.Route;
using Hotel.Game.Spirits;
using Hotel.Game.Supplies;
using Rusty.Engine;

namespace Hotel.Game.Interface;

/// <summary>
/// Publishes the <c>rusty.hotel.hud</c> facts. Each fact is written once, grouped by the HUD region or
/// screen that shows it (docs/ui.md "HUD budget"); an unchanged value is not republished.
/// </summary>
internal sealed class HotelHud : IDisposable
{
    private readonly IEngineContext engine;
    private readonly UiStream stream;
    private readonly InterfaceTuning tuning;
    private UiValue? last;
    private ulong sequence;

    internal HotelHud(IEngineContext engine, InterfaceTuning tuning)
    {
        this.engine = engine;
        this.tuning = tuning;
        stream = engine.Ui.OpenStream(new UiStreamRequest("rusty-hotel", "rusty.hotel.hud"));
    }

    internal void Publish(HotelRoute route, HotelSupplies supplies, HotelCombat combat, HotelSpirit spirit, HotelExpedition expedition)
    {
        UiValueWriter w = new();
        UiValue value = w.Finish(
            w.Text("location", route.Location),
            w.Text("focusPrompt", route.Prompt),
            // One notice slot: the most recent spirit result outranks combat, which outranks supplies.
            w.Text("notice", First(spirit.Notice, combat.Notice, supplies.Notice)),
            w.Object("condition",
                w.Number("health", supplies.Health), w.Number("maximumHealth", supplies.MaximumHealth),
                w.Flag("hurt", combat.HurtFlash > 0)),
            w.Object("held",
                w.Text("weapon", combat.Weapon.Name), w.Text("action", combat.ActionText), w.Flag("hit", combat.HitFlash > 0),
                w.Number("ammo", supplies.Ammo), w.Number("maximumAmmo", supplies.MaximumAmmo),
                w.Number("summon", supplies.Summon), w.Number("maximumSummon", supplies.MaximumSummon),
                w.Text("spirit", spirit.HudLabel),
                w.Text("spiritStatus", spirit.Status)),
            Supplies(w, supplies),
            w.Object("spirit",
                w.Flag("acquired", spirit.Acquired), w.Flag("equipped", spirit.Equipped), w.Number("revision", spirit.Revision),
                w.Text("name", spirit.Definition.Name), w.Text("description", spirit.Description),
                w.Text("message", spirit.Message), w.Text("equipReason", spirit.EquipReason)),
            w.Object("reading",
                w.Number("sequence", route.ReadingSequence), w.Text("title", route.ReadingTitle), w.Text("text", route.ReadingText)),
            w.Object("refuge",
                w.Number("sequence", expedition.ReceiptSequence), w.Text("title", expedition.ReceiptTitle),
                w.Text("text", expedition.ReceiptText), w.Text("checkpointStatus", expedition.Status)));
        if (last is { } previous && UiValueWriter.Same(previous, value)) return;
        last = value;
        engine.Ui.PublishProjection(new(stream, ++sequence, value));
    }

    public void Dispose() => stream.Dispose();

    private uint Supplies(UiValueWriter w, HotelSupplies supplies)
    {
        List<uint> pockets = [];
        for (int i = 0; i < supplies.Capacity; i++)
        {
            ItemStack? stack = supplies.Slot(i);
            ItemDefinition? item = stack is { } carried ? supplies.Item(carried.Item) : null;
            pockets.Add(w.Object("",
                w.Text("id", item?.Id ?? ""), w.Text("name", item?.Name ?? ""), w.Text("description", item?.Details ?? ""),
                w.Text("mark", item?.Mark ?? "·"), w.Number("count", stack?.Count ?? 0), w.Number("stackLimit", item?.StackLimit ?? 0),
                w.Text("useReason", supplies.UseReason(i))));
        }
        return w.Object("supplies",
            w.Number("revision", supplies.Revision), w.Number("capacity", supplies.Capacity),
            w.Number("quickPockets", tuning.QuickPockets), w.Number("occupied", supplies.Occupied),
            w.Text("message", supplies.Message), w.Array("pockets", pockets));
    }

    private static string First(params string[] notices) => notices.FirstOrDefault(n => n.Length > 0) ?? "";
}
