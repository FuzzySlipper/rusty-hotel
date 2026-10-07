using Hotel.Game.Loot;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using EngineItemDefinition = Rusty.Engine.Mechanics.ItemDefinition;

namespace Hotel.Game.Supplies;

/// <summary>One pocket's contents by Engine identity: a fungible stack, or one single item.</summary>
internal readonly record struct Pocket(InventoryStackId? Stack, EntityId? Single);

/// <summary>A worn item: the slots it fills, in authored order, its kind and its Engine identity.</summary>
/// <param name="Roll">How a generated item was resolved (its quality and affixes); null for one as authored.</param>
internal sealed record WornItem(SlotDefinition[] Slots, ItemDefinition Item, EntityId Entity, ItemRoll? Roll);

/// <summary>Why the field case refused an item or an equipment change.</summary>
internal enum CaseRefusal { None, Pockets, Capacity, NotWorn, Exclusive, NoSlot, NoPocket, EmptySlot }

/// <summary>
/// The investigator's field case over one Engine <see cref="InventoryStore"/>: the stacks and single items carried, the
/// equipment slots single items are worn in, and the Engine capacity limits (weight, space) the whole case answers to.
/// Pocket order is Hotel layout; quantities, containment, capacity, slot rules and exclusivity are the Engine's. The
/// stat sources worn items activate are published in <see cref="Sources"/> for the investigator's stats.
/// </summary>
internal sealed class FieldCase
{
    // Single items are entities only to the inventory; their ids sit in their own range, apart from scene entities.
    private const ulong SingleItemIds = 1UL << 40;
    private readonly SuppliesDefinition definition;
    private readonly EntityId owner;
    private readonly Pocket?[] pockets;
    private readonly Dictionary<string, EngineItemDefinition> engineItems;
    private readonly Dictionary<string, EquipmentSlotDefinition> engineSlots;
    private readonly Dictionary<EntityId, (ItemDefinition Item, ItemRoll? Roll)> singles = [];
    private readonly LootCatalog loot;
    private InventoryStore store = new();
    private ulong nextStack, nextSingle;

    internal FieldCase(SuppliesDefinition definition, LootCatalog loot, int capacity, EntityId owner)
    {
        this.loot = loot;
        this.definition = definition;
        this.owner = owner;
        pockets = new Pocket?[capacity];
        engineItems = definition.Items.ToDictionary(i => i.Id, i => i.Engine(), StringComparer.Ordinal);
        engineSlots = definition.Slots.ToDictionary(s => s.Id, s => s.Engine(), StringComparer.Ordinal);
        Reset();
    }

    /// <summary>The Engine stat sources the worn items activate, with their authored contributions.</summary>
    internal StatSource[] Sources { get; private set; } = [];
    internal int Capacity => pockets.Length;
    internal int Occupied => pockets.Count(p => p is not null);

    internal ItemStack? Slot(int index)
    {
        if (pockets[index] is not { } pocket) return null;
        if (pocket.Single is { } single) return new(singles[single].Item.Id, 1, singles[single].Roll);
        InventoryStack stack = store.View(owner).Stacks.Single(s => s.Id == pocket.Stack);
        return new(stack.Definition.Value, checked((int)stack.Quantity));
    }

    /// <summary>Worn items in the order of their first slot.</summary>
    internal IReadOnlyList<WornItem> Worn
    {
        get
        {
            store.TryGetEquipment(owner, out EquipmentState? equipment);
            return equipment!.Assignments.GroupBy(a => a.Item)
                .Select(g => new WornItem(definition.Slots.Where(s => g.Any(a => a.Slot.Value == s.Id)).ToArray(), singles[g.Key].Item, g.Key, singles[g.Key].Roll))
                .OrderBy(w => Array.IndexOf(definition.Slots, w.Slots[0])).ToArray();
        }
    }

    internal WornItem? WornIn(SlotDefinition slot) => Worn.FirstOrDefault(w => w.Slots.Contains(slot));

    /// <summary>How much of one capacity metric the case holds now.</summary>
    internal int Used(CapacityMetric metric) =>
        checked((int)store.View(owner).Capacity.FirstOrDefault(c => c.Metric.Value == metric.Id).Used);

    /// <summary>
    /// Admits a whole find or refuses it unchanged: stacks fill matching pockets first, then empty ones, and the Engine
    /// checks the case's capacity limits on the one edit. <paramref name="metric"/> names the limit a refusal hit.
    /// </summary>
    /// <param name="roll">How a generated single item was resolved; it is carried with that quality and those affixes.</param>
    internal CaseRefusal Add(string item, int count, out CapacityMetric? metric, ItemRoll? roll = null)
    {
        metric = null;
        ItemDefinition kind = definition.Item(item)!;
        int limit = kind.StackLimit;
        ItemStack?[] carried = Enumerable.Range(0, Capacity).Select(Slot).ToArray();
        bool stacks = kind.Form == ItemKind.Fungible;
        int room = carried.Sum(s => s is null ? limit : stacks && s.Value.Item == item ? limit - s.Value.Count : 0);
        if (count <= 0 || room < count) return CaseRefusal.Pockets;
        int wanted = count;
        List<(int Pocket, Pocket Contents)> placed = [];
        try
        {
            using InventoryEdit edit = store.Prepare();
            for (int i = 0; i < pockets.Length && count > 0 && stacks; i++)
            {
                if (carried[i] is not { } stack || stack.Item != item) continue;
                int added = Math.Min(count, limit - stack.Count);
                if (added == 0) continue;
                edit.Grant(owner, engineItems[item], pockets[i]!.Value.Stack!, (ulong)added);
                count -= added;
            }
            for (int i = 0; i < pockets.Length && count > 0; i++)
            {
                if (pockets[i] is not null) continue;
                int added = Math.Min(count, limit);
                placed.Add((i, stacks ? Grant(edit, item, added) : Materialize(edit, kind, roll)));
                count -= added;
            }
            edit.Publish();
        }
        catch (MechanicsException refused) when (refused.Reason == MechanicsRefusal.Capacity)
        {
            // The Engine refused the edit as a whole; name the first limit this find would pass.
            metric = definition.Capacity.FirstOrDefault(m => Used(m) + kind.Costs[m.Id] * wanted > m.Limit) ?? definition.Capacity[0];
            foreach (var (_, contents) in placed) if (contents.Single is { } single) singles.Remove(single);
            return CaseRefusal.Capacity;
        }
        foreach (var (pocket, contents) in placed) pockets[pocket] = contents;
        return CaseRefusal.None;
    }

    /// <summary>Removes one item from a stack pocket, freeing the pocket when it was the last.</summary>
    internal void ConsumeOne(int index)
    {
        Pocket pocket = pockets[index]!.Value;
        bool last = Slot(index)!.Value.Count == 1;
        store.Consume(owner, pocket.Stack!, 1);
        if (last) pockets[index] = null;
    }

    /// <summary>Takes a whole pocket out of the case: the stack's items are consumed, or the single item destroyed.</summary>
    internal ItemStack Remove(int index)
    {
        ItemStack taken = Slot(index)!.Value;
        Pocket pocket = pockets[index]!.Value;
        if (pocket.Single is { } single)
        {
            store.DestroyUnique(single);
            singles.Remove(single);
        }
        else store.Consume(owner, pocket.Stack!, (ulong)taken.Count);
        pockets[index] = null;
        return taken;
    }

    /// <summary>Merges matching stacks as far as their limit allows, or swaps the two pockets. False when the target stack is full.</summary>
    internal bool Move(int from, int to)
    {
        ItemStack source = Slot(from)!.Value;
        if (Slot(to) is { } target && source.Item == target.Item && pockets[from]!.Value.Stack is not null)
        {
            int moved = Math.Min(source.Count, definition.Item(source.Item)!.StackLimit - target.Count);
            if (moved == 0) return false;
            if (moved == source.Count)
            {
                store.MergeFungible(owner, pockets[from]!.Value.Stack!, pockets[to]!.Value.Stack!);
                pockets[from] = null;
            }
            else
            {
                // A partial merge is one Engine edit; there is no intermediate carried stack.
                using InventoryEdit edit = store.Prepare();
                InventoryStackId split = NewStackId();
                edit.SplitFungible(owner, pockets[from]!.Value.Stack!, split, (ulong)moved);
                edit.MergeFungible(owner, split, pockets[to]!.Value.Stack!);
                edit.Publish();
            }
        }
        else (pockets[from], pockets[to]) = (pockets[to], pockets[from]);
        return true;
    }

    /// <summary>
    /// Wears the single item in a pocket in the first free slots that accept it. When none is free and both it and the
    /// item in its first slot fill one slot, the two trade places: the worn one goes back into the pocket.
    /// </summary>
    internal CaseRefusal Wear(int index, out WornItem? other, out WornItem? worn)
    {
        other = worn = null;
        if (pockets[index] is not { Single: { } entity }) return CaseRefusal.NotWorn;
        ItemDefinition item = singles[entity].Item;
        if (item.Wear is not { } wear) return CaseRefusal.NotWorn;
        SlotDefinition[] fits = definition.Slots.Where(s => s.Accepts.Intersect(item.Classifications).Any()).ToArray();
        WornItem? displaced = WornIn(fits[0]);
        SlotDefinition[] free = fits.Where(s => WornIn(s) is null).Take(wear.Slots).ToArray();
        bool swap = free.Length < wear.Slots;
        if (swap && (displaced is null || wear.Slots != 1 || displaced.Slots.Length != 1)) return CaseRefusal.NoSlot;
        other = Worn.FirstOrDefault(w => wear.Exclusive is { } group && w.Item.Wear!.Exclusive == group && (!swap || w != displaced));
        if (other is not null) return CaseRefusal.Exclusive;
        EquipmentMutationReceipt receipt = swap
            ? store.Swap(owner, displaced!.Entity, entity, [engineSlots[fits[0].Id]])
            : store.Equip(owner, entity, free.Select(s => engineSlots[s.Id]));
        pockets[index] = swap ? new Pocket(null, displaced!.Entity) : null;
        other = swap ? displaced : null;
        Settle(receipt);
        worn = Worn.First(w => w.Entity == entity);
        return CaseRefusal.None;
    }

    /// <summary>Takes off the item worn in a slot into the first empty pocket.</summary>
    internal CaseRefusal TakeOff(SlotDefinition slot, out WornItem? worn)
    {
        worn = WornIn(slot);
        if (worn is null) return CaseRefusal.EmptySlot;
        int free = Array.FindIndex(pockets, p => p is null);
        if (free < 0) return CaseRefusal.NoPocket;
        Settle(store.Unequip(owner, worn.Entity));
        pockets[free] = new Pocket(null, worn.Entity);
        return CaseRefusal.None;
    }

    /// <summary>The item held in a hand, if any.</summary>
    internal WornItem? Held(Hand hand) => WornIn(definition.HandSlot(hand));

    /// <summary>
    /// Trades what the two hands hold in one Engine edit. False when both are empty or one hand's item would not fit the
    /// other hand's slot; nothing changes then.
    /// </summary>
    internal bool SwapHands()
    {
        SlotDefinition main = definition.HandSlot(Hand.Main), off = definition.HandSlot(Hand.Off);
        WornItem? inMain = WornIn(main), inOff = WornIn(off);
        bool Fits(WornItem? item, SlotDefinition slot) => item is null || item.Slots.Length == 1 && slot.Accepts.Intersect(item.Item.Classifications).Any();
        if (inMain is null && inOff is null || !Fits(inMain, off) || !Fits(inOff, main)) return false;
        EquipmentMutationReceipt? last = null;
        using (InventoryEdit edit = store.Prepare())
        {
            if (inMain is not null) edit.Unequip(owner, inMain.Entity);
            if (inOff is not null) edit.Unequip(owner, inOff.Entity);
            if (inMain is not null) last = edit.Equip(owner, inMain.Entity, [engineSlots[off.Id]]);
            if (inOff is not null) last = edit.Equip(owner, inOff.Entity, [engineSlots[main.Id]]);
            edit.Publish();
        }
        Settle(last!);
        return true;
    }

    internal (ItemStack?[] Pockets, WornState[] Worn) Capture() =>
        (Enumerable.Range(0, Capacity).Select(Slot).ToArray(), Worn.Select(w => new WornState(w.Item.Id, w.Slots.Select(s => s.Id).ToArray(), w.Roll)).ToArray());

    /// <summary>
    /// Refuses saved contents that name unknown items or slots or overfill a stack; the Engine's own rules (capacity,
    /// slot classifications, slot counts, exclusivity) are checked by restoring onto a scratch case.
    /// </summary>
    internal void Validate(ItemStack?[] saved, WornState[] worn)
    {
        if (saved is null || saved.Length != Capacity || worn is null)
            throw new InvalidOperationException("Checkpoint field case has the wrong shape.");
        foreach (ItemStack? pocket in saved)
            if (pocket is { } stack && (stack.Count <= 0 || definition.Item(stack.Item) is not { } item || stack.Count > item.StackLimit ||
                stack.Roll is { } roll && (roll.Item != stack.Item || !loot.Fits(roll, item))))
                throw new InvalidOperationException("Checkpoint contains an invalid carried stack.");
        foreach (WornState? state in worn)
            if (state?.Slots is null || definition.Item(state.Item)?.Wear is null || state.Slots.Any(id => !engineSlots.ContainsKey(id)) ||
                state.Roll is { } roll && (roll.Item != state.Item || !loot.Fits(roll, definition.Item(state.Item)!)))
                throw new InvalidOperationException("Checkpoint contains an invalid worn item.");
        if (worn.SelectMany(w => w.Slots).Distinct().Count() != worn.Sum(w => w.Slots.Length))
            throw new InvalidOperationException("Checkpoint wears two items in one slot.");
        new FieldCase(definition, loot, Capacity, owner).Restore(saved, worn);
    }

    /// <summary>Rebuilds validated contents: every pocket and worn item in one edit, then each worn item equipped in its slots.</summary>
    internal void Restore(ItemStack?[] saved, WornState[] worn)
    {
        Reset();
        List<(WornState State, EntityId Entity)> wearing = [];
        using (InventoryEdit edit = store.Prepare())
        {
            for (int i = 0; i < Capacity; i++)
            {
                if (saved[i] is not { } stack) continue;
                ItemDefinition item = definition.Item(stack.Item)!;
                pockets[i] = item.Form == ItemKind.Fungible ? Grant(edit, item.Id, stack.Count) : Materialize(edit, item, stack.Roll);
            }
            foreach (WornState state in worn) wearing.Add((state, Materialize(edit, definition.Item(state.Item)!, state.Roll).Single!.Value));
            edit.Publish();
        }
        foreach (var (state, entity) in wearing)
            Settle(store.Equip(owner, entity, state.Slots.Select(id => engineSlots[id])));
    }

    internal void Reset()
    {
        Array.Clear(pockets);
        singles.Clear();
        store = new();
        store.RegisterInventory(new(owner, definition.Capacity.Select(m => new InventoryCapacityLimit(CapacityMetricId.Parse(m.Id), (ulong)m.Limit))));
        store.RegisterEquipment(new(owner));
        nextStack = nextSingle = 0;
        Sources = [];
    }

    private Pocket Grant(InventoryEdit edit, string item, int count)
    {
        InventoryStackId id = NewStackId();
        edit.Grant(owner, engineItems[item], id, (ulong)count);
        return new(id, null);
    }

    private Pocket Materialize(InventoryEdit edit, ItemDefinition item, ItemRoll? roll = null)
    {
        EntityId entity = new(SingleItemIds + ++nextSingle);
        edit.MaterializeUnique(new ItemState(entity, engineItems[item.Id]), owner);
        singles[entity] = (item, roll);
        return new(null, entity);
    }

    // Every equipment receipt lists the sources of everything worn after it; those replace the published set. A worn
    // item's source carries its own stats as its quality scales them, and its affixes'.
    private void Settle(EquipmentMutationReceipt receipt) => Sources = receipt.SourceActivations.Select(a =>
    {
        var (item, roll) = singles[a.Identity.Item!.Value];
        return new StatSource(a.Identity, a.Definition, 0, loot.Stats(item, roll).Select(s => new StatContributionDefinition(StatId.Parse(s.Stat),
            StackingGroupId.Parse($"worn.{s.Stat}"), MechanicsStackingPolicy.Sum, new StatContribution.Add(s.Amount))).ToArray());
    }).ToArray();

    private InventoryStackId NewStackId() => InventoryStackId.Parse($"hotel-stack-{++nextStack}");
}

/// <summary>A saved worn item: its kind and the slots it fills.</summary>
internal sealed record WornState(string Item, string[] Slots, ItemRoll? Roll = null);

/// <param name="Roll">How a generated single item was resolved; null for items as authored.</param>
internal readonly record struct ItemStack(string Item, int Count, ItemRoll? Roll = null);
