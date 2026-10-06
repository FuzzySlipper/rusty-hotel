using System.Text.Json;
using Rusty.Engine;
using Hotel.Game.Content;
using Hotel.Game.Expedition;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using ItemDefinition = Hotel.Game.Supplies.ItemDefinition;
using EngineItemDefinition = Rusty.Engine.Mechanics.ItemDefinition;

namespace Hotel.Game.Supplies;

/// <summary>
/// One owner for carried stacks, collected finds and player resources. The resources are the investigator's Engine
/// stats: health, ammunition and summon charges are tracks bounded by derived stats (see <see cref="Mechanics.ActorStats"/>).
/// </summary>
internal sealed class HotelSupplies
{
    // The tracks this owner spends and restores, by vocabulary id.
    internal const string HealthTrack = Mechanics.ActorStats.HealthTrack, AmmoTrack = "ammunition", SummonTrack = "summon";
    private readonly ItemDefinition[] items;
    private readonly Mechanics.MechanicsDefinition mechanics;
    private readonly SupplyMessages text;
    private readonly FindDefinition[] finds;
    private readonly InventoryStackId?[] slots;
    private readonly EntityId owner;
    private readonly Dictionary<string, EngineItemDefinition> itemMechanics;
    private InventoryStore inventory = new();
    private readonly Track health, ammo, summon;
    private ulong nextStack;
    private readonly HashSet<string> collected = new(StringComparer.Ordinal);

    internal HotelSupplies(SuppliesDefinition definition, FindDefinition[] finds, int capacity, EntityId owner,
        Mechanics.MechanicsDefinition mechanics, Mechanics.ActorStatBlock playerStats)
    {
        Stats = new(mechanics, playerStats, owner);
        items = definition.Items;
        this.mechanics = mechanics;
        text = definition.Text;
        this.finds = finds;
        this.owner = owner;
        slots = new InventoryStackId?[capacity];
        itemMechanics = definition.Items.ToDictionary(i => i.Id,
            i => new EngineItemDefinition(ItemDefinitionId.Parse(i.Id), ItemKind.Fungible, (ulong)i.StackLimit));
        health = Stats.Track(HealthTrack);
        ammo = Stats.Track(AmmoTrack);
        summon = Stats.Track(SummonTrack);
        Reset();
    }

    /// <summary>The investigator's stats; the resource tracks above are its tracks.</summary>
    internal Mechanics.ActorStats Stats { get; }

    internal ulong Revision { get; private set; }
    internal int Health => health.ValueInt;
    internal int MaximumHealth => (int)health.MaximumValue;
    internal int Ammo => ammo.ValueInt;
    internal int MaximumAmmo => (int)ammo.MaximumValue;
    internal int Summon => summon.ValueInt;
    internal int MaximumSummon => (int)summon.MaximumValue;
    internal int Capacity => slots.Length;
    internal int Occupied => slots.Count(s => s is not null);
    private float noticeSeconds;
    private string message = "";
    internal string Message
    {
        get => message;
        private set { message = value; noticeSeconds = value.Length == 0 ? 0 : text.NoticeSeconds; }
    }
    internal string Notice => noticeSeconds > 0 ? Message : "";
    /// <summary>Advances notices and the investigator's effects by admitted seconds; a tick that moves a track is a new revision.</summary>
    internal void Step(float admittedSeconds)
    {
        noticeSeconds = Math.Max(0, noticeSeconds - admittedSeconds);
        if (Health > 0 && Stats.Effects.Advance(admittedSeconds)) Revision++;
    }

    /// <summary>Applies an effect to the investigator from a source (an item, a resident's hit); false when refused.</summary>
    internal bool Afflict(string effect, string source)
    {
        if (Health == 0 || Stats.Effects.Apply(mechanics.Effect(effect)!, source) is null) return false;
        Revision++;
        return true;
    }
    // Pocket order is Hotel policy; quantities and definitions are read from the Engine ledger.
    internal ItemStack? Slot(int index)
    {
        if (slots[index] is not { } id) return null;
        InventoryStack stack = inventory.View(owner).Stacks.Single(s => s.Id == id);
        return new(stack.Definition.Value, checked((int)stack.Quantity));
    }
    internal ItemDefinition Item(string id) => items.First(i => i.Id == id);
    internal bool Collected(string id) => collected.Contains(id);
    internal FindDefinition[] Finds => finds;
    // Expedition finds are deposited at the refuge rather than used in the field.
    internal bool IsExpeditionFind(string findId) => Item(finds.Single(f => f.Id == findId).Item).Kind == SupplyKind.Expedition;

    internal bool Pickup(string id)
    {
        FindDefinition? find = finds.FirstOrDefault(f => f.Id == id);
        if (find is null || collected.Contains(id)) return Refuse(text.FindGone);
        if (!Add(find.Item, find.Count)) return Refuse(text.CaseFull);
        collected.Add(id);
        Revision++;
        Message = Template.Fill(text.Collected, ("item", Item(find.Item).Name), ("count", find.Count));
        return true;
    }

    internal string UseReason(int index)
    {
        if (Health == 0) return text.Overwhelmed;
        if (index < 0 || index >= slots.Length || Slot(index) is not { } stack) return text.EmptyPocket;
        ItemDefinition item = Item(stack.Item);
        string full = item.Kind switch
        {
            SupplyKind.Healing when Health >= MaximumHealth => text.HealthFull,
            SupplyKind.Ammo when Ammo >= MaximumAmmo => text.AmmoFull,
            SupplyKind.Summon when Summon >= MaximumSummon => text.SummonFull,
            SupplyKind.Expedition => text.KeepForReturn,
            _ => ""
        };
        // A full track refuses an item only when its effects would do no more than restore a full track.
        bool effective = item.Kind != SupplyKind.Expedition && item.Effects.Any(id => mechanics.Effect(id)!.Restore is not { } restore ||
            Stats.Track(restore.Track).Value < Stats.Track(restore.Track).MaximumValue);
        return effective ? "" : full;
    }

    internal bool Use(int index, ulong revision)
    {
        if (revision != Revision) return Refuse(text.CaseChanged);
        string reason = UseReason(index);
        if (reason.Length != 0) return Refuse(reason);
        ItemStack stack = Slot(index)!.Value;
        ItemDefinition item = Item(stack.Item);
        switch (item.Kind)
        {
            case SupplyKind.Healing: health.Restore(item.Amount); break;
            case SupplyKind.Ammo: ammo.Restore(item.Amount); break;
            case SupplyKind.Summon: summon.Restore(item.Amount); break;
        }
        foreach (string effect in item.Effects) Stats.Effects.Apply(mechanics.Effect(effect)!, $"item.{item.Id}");
        inventory.Consume(owner, slots[index]!, 1);
        if (stack.Count == 1) slots[index] = null;
        Revision++;
        Message = Template.Fill(text.Used, ("item", item.Name));
        return true;
    }

    internal bool Move(int from, int to, ulong revision)
    {
        if (revision != Revision) return Refuse(text.CaseChanged);
        if (from < 0 || to < 0 || from >= slots.Length || to >= slots.Length || from == to || Slot(from) is not { } source)
            return Refuse(text.ChooseStack);
        if (Slot(to) is { } target && source.Item == target.Item)
        {
            int moved = Math.Min(source.Count, Item(source.Item).StackLimit - target.Count);
            if (moved == 0) return Refuse(text.StackFull);
            if (moved == source.Count)
            {
                inventory.MergeFungible(owner, slots[from]!, slots[to]!);
                slots[from] = null;
            }
            else
            {
                // A partial merge is one Engine edit; there is no intermediate carried stack.
                using InventoryEdit edit = inventory.Prepare();
                InventoryStackId split = NewStackId();
                edit.SplitFungible(owner, slots[from]!, split, (ulong)moved);
                edit.MergeFungible(owner, split, slots[to]!);
                edit.Publish();
            }
        }
        else (slots[from], slots[to]) = (slots[to], slots[from]);
        Revision++;
        Message = text.Rearranged;
        return true;
    }

    // UI claims use the same rules during running and paused Engine admission.
    internal void HandleIntents(ReadOnlySpan<ProductInputEvent> intents)
    {
        foreach (ProductInputEvent input in intents)
        {
            if (input.Kind != InputEventKind.DirectProductPayload || !input.Intent.Span.SequenceEqual("hotel.supplies"u8)
                || !input.PayloadContract.Span.SequenceEqual("hotel.supplies.v1"u8)) continue;
            try
            {
                using JsonDocument payload = JsonDocument.Parse(input.PayloadData);
                JsonElement root = payload.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("action", out var action)
                    || action.ValueKind != JsonValueKind.String || !root.TryGetProperty("revision", out var rev)
                    || rev.ValueKind != JsonValueKind.Number || !rev.TryGetUInt64(out ulong revision)
                    || !Integer(root, "from", out int from))
                { Refuse(text.ChooseAgain); continue; }
                if (action.GetString() == "use") Use(from, revision);
                else if (action.GetString() == "move" && Integer(root, "to", out int to)) Move(from, to, revision);
                else Refuse(text.ChooseAction);
            }
            catch (JsonException) { Refuse(text.Unreadable); }
        }
    }

    private static bool Integer(JsonElement root, string name, out int value)
    {
        value = 0;
        return root.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.Number && field.TryGetInt32(out value);
    }

    internal bool SpendAmmo(int amount) => Spend(amount, false);
    /// <summary>A hit on the investigator, after their resistance to its kind; returns the health taken.</summary>
    internal int Damage(Mechanics.DamagePacket packet)
    {
        if (packet.Amount <= 0 || Health == 0) return 0;
        int applied = Stats.TakeDamage(packet, HealthTrack);
        Revision++;
        return applied;
    }

    internal int AmmoPocket => Enumerable.Range(0, Capacity).FirstOrDefault(
        i => Slot(i) is { } stack && Item(stack.Item).Kind == SupplyKind.Ammo, -1);
    internal void RestoreSummon(int amount)
    {
        if (amount <= 0) return;
        summon.Restore(amount);
        Revision++;
    }
    internal bool SpendSummon(int amount) => Spend(amount, true);
    private bool Spend(int amount, bool summonResource)
    {
        if (amount <= 0 || !(summonResource ? summon : ammo).TrySpend(amount)) return false;
        Revision++;
        return true;
    }

    // Developer fixtures only: a bound on one console request, not a game rule. Its messages are console output.
    private const int MaximumDeveloperGift = 99;

    internal bool Give(string item, int count)
    {
        if (count <= 0 || count > MaximumDeveloperGift || !items.Any(i => i.Id == item)) return Refuse("Unknown item or invalid quantity.");
        if (!Add(item, count)) return Refuse("Field case full.");
        Revision++;
        Message = $"Developer supplied {Item(item).Name} ×{count}.";
        return true;
    }

    internal bool SetHealth(int health)
    {
        if (health < 0 || health > MaximumHealth) return Refuse("Health is outside its allowed range.");
        this.health.SetCurrent(health);
        Revision++;
        Message = $"Developer set health to {health}.";
        return true;
    }

    internal SuppliesState Capture() => new(Stats.Capture(),
        Enumerable.Range(0, Capacity).Select(Slot).ToArray(), collected.Order().ToArray());

    internal void Validate(SuppliesState state)
    {
        if (state.Stats is null) throw new InvalidOperationException("Checkpoint supplies have no stats.");
        Stats.Validate(state.Stats);
        if (!(state.Stats.Tracks.GetValueOrDefault(HealthTrack) > 0) || state.Pockets is null || state.Pockets.Length != Capacity ||
            state.Collected is null || state.Collected.Distinct().Count() != state.Collected.Length ||
            state.Collected.Any(id => !finds.Any(f => f.Id == id)))
            throw new InvalidOperationException("Checkpoint supplies or collected finds are invalid.");
        foreach (ItemStack? pocket in state.Pockets)
            if (pocket is { } stack && (stack.Count <= 0 || !items.Any(i => i.Id == stack.Item && stack.Count <= i.StackLimit)))
                throw new InvalidOperationException("Checkpoint contains an invalid carried stack.");
    }

    internal void Restore(SuppliesState state)
    {
        Reset();
        using InventoryEdit edit = inventory.Prepare();
        for (int i = 0; i < Capacity; i++)
        {
            if (state.Pockets[i] is not { } stack) continue;
            InventoryStackId id = NewStackId();
            edit.Grant(owner, itemMechanics[stack.Item], id, (ulong)stack.Count);
            slots[i] = id;
        }
        edit.Publish();
        collected.UnionWith(state.Collected);
        Stats.Restore(state.Stats);
    }

    internal void Reset()
    {
        Array.Clear(slots);
        collected.Clear();
        inventory = new();
        inventory.RegisterInventory(new(owner));
        nextStack = 0;
        Stats.Reset();
        Revision++;
        Message = "";
    }

    // Check total room before changing any stack: a refused pickup leaves all of it in the world.
    private bool Add(string item, int count)
    {
        int limit = Item(item).StackLimit;
        ItemStack?[] carried = Enumerable.Range(0, Capacity).Select(Slot).ToArray();
        int room = carried.Sum(s => s is null ? limit : s.Value.Item == item ? limit - s.Value.Count : 0);
        if (count <= 0 || room < count) return false;
        using InventoryEdit edit = inventory.Prepare();
        List<(int Pocket, InventoryStackId Id)> newPockets = [];
        for (int i = 0; i < slots.Length && count > 0; i++)
        {
            if (carried[i] is not { } stack || stack.Item != item) continue;
            int added = Math.Min(count, limit - stack.Count);
            if (added == 0) continue;
            edit.Grant(owner, itemMechanics[item], slots[i]!, (ulong)added);
            count -= added;
        }
        for (int i = 0; i < slots.Length && count > 0; i++)
        {
            if (slots[i] is not null) continue;
            int added = Math.Min(count, limit);
            InventoryStackId id = NewStackId();
            edit.Grant(owner, itemMechanics[item], id, (ulong)added);
            newPockets.Add((i, id));
            count -= added;
        }
        edit.Publish();
        foreach (var pocket in newPockets) slots[pocket.Pocket] = pocket.Id;
        return true;
    }

    private InventoryStackId NewStackId() => InventoryStackId.Parse($"hotel-stack-{++nextStack}");

    private bool Refuse(string message) { Message = message; return false; }
}

internal readonly record struct ItemStack(string Item, int Count);
