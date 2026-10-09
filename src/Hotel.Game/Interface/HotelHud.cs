using Hotel.Game.Content;
using Hotel.Game.Combat;
using Hotel.Game.Expedition;
using Hotel.Game.Input;
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

    internal void Publish(HotelRoute route, HotelSupplies supplies, HotelCombat combat, HotelSpirit spirit, HotelExpedition expedition,
        HotelTitle title, HotelControls controls)
    {
        UiValueWriter w = new();
        UiValue value = w.Finish(
            w.Text("location", route.Location),
            w.Text("focusPrompt", route.Prompt),
            // One notice slot: a spirit result outranks the rest; of combat and supplies, the newer result shows.
            w.Text("notice", First(spirit.Notice, supplies.Notice.Length > 0 && (combat.Notice.Length == 0 || supplies.NoticeAge < combat.NoticeAge)
                ? supplies.Notice : combat.Notice)),
            w.Object("condition",
                w.Number("health", supplies.Health), w.Number("maximumHealth", supplies.MaximumHealth),
                w.Number("stamina", supplies.Stamina), w.Number("maximumStamina", supplies.MaximumStamina),
                w.Flag("hurt", combat.HurtFlash > 0), Effects(w, supplies, combat)),
            w.Object("held",
                w.Text("weapon", combat.HandsText), w.Text("offHand", supplies.Held(Hand.Off) is { } off && off != combat.Holding ? supplies.Name(off) : ""), w.Text("action", combat.ActionText), w.Flag("hit", combat.HitFlash > 0),
                w.Number("ammo", supplies.Ammo), w.Number("maximumAmmo", supplies.MaximumAmmo),
                w.Number("summon", supplies.Summon), w.Number("maximumSummon", supplies.MaximumSummon),
                w.Text("spirit", spirit.HudLabel),
                w.Text("spiritStatus", spirit.Status)),
            Supplies(w, supplies, controls),
            Controls(w, controls),
            w.Object("spirit",
                w.Number("revision", spirit.Revision), w.Text("equipped", spirit.Equipped?.Id ?? ""),
                w.Array("pacts", spirit.Roster.Where(s => spirit.Acquired(s.Id)).Select(s => w.Object("",
                    w.Text("id", s.Id), w.Text("name", s.Name), w.Text("description", spirit.Description(s)))).ToArray()),
                w.Text("message", spirit.Message), w.Text("equipReason", spirit.EquipReason)),
            w.Object("reading",
                w.Number("sequence", route.ReadingSequence), w.Text("title", route.ReadingTitle), w.Text("text", route.ReadingText)),
            w.Object("refuge",
                w.Number("sequence", expedition.ReceiptSequence), w.Text("title", expedition.ReceiptTitle),
                w.Text("text", expedition.ReceiptText), w.Text("checkpointStatus", expedition.Status)),
            w.Object("title",
                w.Flag("active", title.Active), w.Number("revision", title.Revision), w.Text("message", title.Message),
                w.Flag("canContinue", title.Save.Condition == SaveCondition.Ready), w.Flag("saved", title.Save.Condition != SaveCondition.None),
                w.Text("replaceWarning", title.Definition.Text.ReplaceWarning), w.Text("deleteWarning", title.Definition.Text.DeleteWarning)));
        if (last is { } previous && UiValueWriter.Same(previous, value)) return;
        last = value;
        engine.Ui.PublishProjection(new(stream, ++sequence, value));
    }

    public void Dispose() => stream.Dispose();

    // Labels for the Controls screen, the opening hint and the keys the DOM companion handles itself.
    private static uint Controls(UiValueWriter w, HotelControls controls)
    {
        ControlBindings bound = controls.Bindings;
        return w.Object("controls",
            w.Array("hint", controls.Hint.Select(line => w.Text("", line)).ToArray()),
            w.Array("rows", controls.Rows.Select(row => w.Object("", w.Text("name", row.Name), w.Text("label", row.Label))).ToArray()),
            w.Object("screens",
                w.Object("fieldCase", w.Text("code", bound.FieldCase.Code), w.Text("label", bound.FieldCase.Label)),
                w.Object("menu", w.Text("code", bound.Menu.Code), w.Text("label", bound.Menu.Label)),
                w.Object("console", w.Text("code", bound.Console.Code), w.Text("label", bound.Console.Label))));
    }

    private uint Supplies(UiValueWriter w, HotelSupplies supplies, HotelControls controls)
    {
        List<uint> pockets = [];
        for (int i = 0; i < supplies.Capacity; i++)
        {
            ItemStack? stack = supplies.Slot(i);
            ItemDefinition? item = stack is { } carried ? supplies.Item(carried.Item) : null;
            pockets.Add(w.Object("",
                w.Text("id", item?.Id ?? ""), w.Text("name", stack is { } named ? supplies.Name(named) : ""), w.Text("description", item?.Details ?? ""),
                w.Text("mark", item?.Mark ?? "·"), w.Number("count", stack?.Count ?? 0), w.Number("stackLimit", item?.StackLimit ?? 0),
                w.Text("useReason", supplies.UseReason(i)), w.Flag("wearable", item?.Wear is not null),
                w.Text("wearReason", supplies.WearReason(i))));
        }
        SuppliesDefinition definition = supplies.Definition;
        uint[] worn = definition.Slots.Select(slot => supplies.WornIn(slot) is { } on
            ? w.Object("", w.Text("slot", slot.Name), w.Text("id", on.Item.Id), w.Text("name", supplies.Name(on)), w.Text("mark", on.Item.Mark),
                w.Text("description", on.Item.Details))
            : w.Object("", w.Text("slot", slot.Name), w.Text("id", ""), w.Text("name", ""), w.Text("mark", ""), w.Text("description", ""))).ToArray();
        uint[] load = definition.Capacity.Select(m => w.Text("", Template.Fill(definition.Text.Load,
            ("metric", m.Name), ("used", supplies.Used(m)), ("limit", m.Limit)))).ToArray();
        return w.Object("supplies",
            w.Number("revision", supplies.Revision), w.Number("capacity", supplies.Capacity),
            w.Number("quickPockets", tuning.QuickPockets), w.Number("occupied", supplies.Occupied),
            w.Array("quickKeys", controls.Bindings.QuickPockets.Select(q => w.Text("", q.Label)).ToArray()),
            w.Text("quickNote", controls.QuickPocketsNote),
            w.Text("message", supplies.Message), w.Array("pockets", pockets), w.Array("worn", worn), w.Array("load", load));
    }

    // The investigator's effects while they last, in the condition cluster: each one's mark and name with its stacks,
    // a ward's remaining absorption or what a reveal senses, and the whole seconds left.
    private static uint Effects(UiValueWriter w, HotelSupplies supplies, HotelCombat combat)
    {
        Mechanics.MechanicsMessages text = supplies.Stats.Mechanics.Text;
        return w.Array("effects", supplies.Stats.Effects.Active.Select(e => w.Object("",
            w.Text("id", e.Definition.Id), w.Text("mark", e.Definition.Mark), w.Text("name", e.Definition.Name),
            w.Text("state", string.Join(" ", new[] {
                e.Stacks > 1 ? Template.Fill(text.EffectStacks, ("stacks", e.Stacks)) : "",
                e.Definition.Ward is not null ? Template.Fill(text.EffectWard, ("ward", e.WardLeft)) : "",
                e.Definition.Reveal is { } reveal ? Template.Fill(reveal.Sensed, ("count", combat.Sensed(reveal.Radius))) : "" }
                .Where(part => part.Length > 0))),
            w.Text("time", Template.Fill(text.EffectSeconds, ("seconds", Math.Ceiling(e.Remaining)))))).ToArray());
    }

    private static string First(params string[] notices) => notices.FirstOrDefault(n => n.Length > 0) ?? "";
}
