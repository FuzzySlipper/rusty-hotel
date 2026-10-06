using System.Text.Json;
using Rusty.Engine;
using Hotel.Game.Content;
using Hotel.Game.Expedition;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using ItemDefinition = Hotel.Game.Supplies.ItemDefinition;

namespace Hotel.Game.Supplies;

/// <summary>
/// One owner for the field case, collected finds and the investigator's resources and effects. Carried and worn items
/// are the Engine inventory and equipment in <see cref="FieldCase"/>; the resources are the investigator's Engine stats
/// (see <see cref="Mechanics.ActorStats"/>), and worn items add their sources to them. Pickup, use, moves and equipment
/// changes from the world, quick keys, the field case and developer commands share these rules and revision checks.
/// </summary>
internal sealed class HotelSupplies
{
    // The tracks this owner spends and restores, by vocabulary id.
    internal const string HealthTrack = Mechanics.ActorStats.HealthTrack, AmmoTrack = "ammunition", SummonTrack = "summon",
        StaminaTrack = "stamina";
    private readonly SuppliesDefinition definition;
    private readonly Mechanics.MechanicsDefinition mechanics;
    private readonly SupplyMessages text;
    private readonly FindDefinition[] finds;
    private readonly SearchDefinition[] searches;
    private readonly Loot.LootCatalog loot;
    private readonly FieldCase fieldCase;
    private readonly Track health, ammo, summon;
    private readonly HashSet<string> collected = new(StringComparer.Ordinal);

    internal HotelSupplies(SuppliesDefinition definition, FindDefinition[] finds, int capacity, EntityId owner,
        Mechanics.MechanicsDefinition mechanics, Mechanics.ActorStatBlock playerStats, Loot.LootCatalog loot, SearchDefinition[] searches)
    {
        this.loot = loot;
        this.searches = searches;
        Stats = new(mechanics, playerStats, owner);
        this.definition = definition;
        this.mechanics = mechanics;
        text = definition.Text;
        this.finds = finds;
        fieldCase = new(definition, loot, capacity, owner);
        health = Stats.Track(HealthTrack);
        ammo = Stats.Track(AmmoTrack);
        summon = Stats.Track(SummonTrack);
        Reset();
    }

    /// <summary>The investigator's stats; the resource tracks above are its tracks.</summary>
    internal Mechanics.ActorStats Stats { get; }
    internal SuppliesDefinition Definition => definition;

    internal ulong Revision { get; private set; }
    internal int Health => health.ValueInt;
    internal int MaximumHealth => (int)health.MaximumValue;
    internal int Stamina => Stats.Track(StaminaTrack).ValueInt;
    internal int MaximumStamina => (int)Stats.Track(StaminaTrack).MaximumValue;
    internal int Ammo => ammo.ValueInt;
    internal int MaximumAmmo => (int)ammo.MaximumValue;
    internal int Summon => summon.ValueInt;
    internal int MaximumSummon => (int)summon.MaximumValue;
    internal int Capacity => fieldCase.Capacity;
    internal int Occupied => fieldCase.Occupied;
    internal IReadOnlyList<WornItem> Worn => fieldCase.Worn;
    internal WornItem? WornIn(SlotDefinition slot) => fieldCase.WornIn(slot);
    /// <summary>How much of one capacity metric the case holds, worn items included.</summary>
    internal int Used(CapacityMetric metric) => fieldCase.Used(metric);
    private float noticeSeconds;
    private string message = "";
    internal string Message
    {
        get => message;
        private set { message = value; noticeSeconds = value.Length == 0 ? 0 : text.NoticeSeconds; NoticeAge = 0; }
    }
    internal string Notice => noticeSeconds > 0 ? Message : "";
    /// <summary>Admitted seconds since the current notice was posted.</summary>
    internal float NoticeAge { get; private set; }
    /// <summary>Advances notices and the investigator's effects by admitted seconds; a tick that moves a track is a new revision.</summary>
    internal void Step(float admittedSeconds)
    {
        noticeSeconds = Math.Max(0, noticeSeconds - admittedSeconds);
        NoticeAge += admittedSeconds;
        if (Health == 0) return;
        if (Stats.Effects.Advance(admittedSeconds)) Revision++;
        Stats.Regenerate(admittedSeconds);
    }

    /// <summary>Applies an effect to the investigator from a source (an item, a resident's hit); false when refused.</summary>
    internal bool Afflict(string effect, string source)
    {
        if (Health == 0 || Stats.Effects.Apply(mechanics.Effect(effect)!, source) is null) return false;
        Revision++;
        return true;
    }

    internal ItemStack? Slot(int index) => fieldCase.Slot(index);

    /// <summary>Marks the investigator changed by another owner (an action's cost, a hit landed by combat): a new revision.</summary>
    internal void Changed() => Revision++;
    internal ItemDefinition Item(string id) => definition.Item(id)!;
    internal Loot.LootCatalog Loot => loot;
    /// <summary>How a carried stack or worn item reads: a generated item by its quality and affixes.</summary>
    internal string Name(ItemStack stack) => loot.Name(Item(stack.Item), stack.Roll);
    internal string Name(WornItem worn) => loot.Name(worn.Item, worn.Roll);
    internal SearchDefinition[] Searches => searches;
    internal bool Collected(string id) => collected.Contains(id);
    internal FindDefinition[] Finds => finds;
    // Expedition finds are deposited at the refuge rather than used in the field; a search made is not a find.
    internal bool IsExpeditionFind(string id) => finds.FirstOrDefault(f => f.Id == id) is { } find && Item(find.Item).Deposit;

    internal bool Pickup(string id)
    {
        FindDefinition? find = finds.FirstOrDefault(f => f.Id == id);
        if (find is null || collected.Contains(id)) return Refuse(text.FindGone);
        if (!Add(find.Item, find.Count, find.Roll)) return false;
        collected.Add(id);
        Revision++;
        Message = Template.Fill(text.Collected, ("item", Name(new ItemStack(find.Item, find.Count, find.Roll))), ("count", find.Count));
        return true;
    }

    /// <summary>Whether a search has been made; searched things give nothing again.</summary>
    internal bool Searched(string id) => collected.Contains(id);

    /// <summary>
    /// Searches something once for what its table rolled: all of it goes into the field case, or none of it and the
    /// search waits. Searched, it is collected like a find and saved with them.
    /// </summary>
    internal bool Search(string id, Loot.ItemRoll[] found)
    {
        if (Health == 0) return Refuse(text.Overwhelmed);
        if (collected.Contains(id) || searches.All(s => s.Id != id)) return Refuse(text.Searched);
        // Room for everything first, on a scratch case holding what is carried now.
        var (pockets, worn) = fieldCase.Capture();
        FieldCase scratch = new(definition, loot, Capacity, new EntityId(0));
        scratch.Restore(pockets, worn);
        if (found.Any(r => scratch.Add(r.Item, r.Count, out _, r.Generated ? r : null) != CaseRefusal.None)) return Refuse(text.SearchFull);
        foreach (Loot.ItemRoll roll in found) fieldCase.Add(roll.Item, roll.Count, out _, roll.Generated ? roll : null);
        collected.Add(id);
        Revision++;
        Message = found.Length == 0 ? text.FoundNothing : Template.Fill(text.Found, ("items",
            string.Join(text.FoundSeparator, found.Select(r => Template.Fill(text.FoundItem,
                ("item", Name(new ItemStack(r.Item, r.Count, r.Generated ? r : null))), ("count", r.Count))))));
        return true;
    }

    internal string UseReason(int index)
    {
        if (Health == 0) return text.Overwhelmed;
        if (index < 0 || index >= Capacity || Slot(index) is not { } stack) return text.EmptyPocket;
        ItemDefinition item = Item(stack.Item);
        if (item.Deposit) return text.KeepForReturn;
        if (item.Use is not { } use) return Template.Fill(text.WornNotUsed, ("item", item.Name));
        // A full track refuses an item only when everything it does would restore a full track.
        bool Full(string track) => Stats.Track(track).Value >= Stats.Track(track).MaximumValue;
        bool effective = use.Restores.Keys.Any(t => !Full(t)) ||
            use.Effects.Any(id => mechanics.Effect(id)!.Restore is not { } restore || !Full(restore.Track));
        string? full = use.Restores.Keys.Concat(use.Effects.Select(id => mechanics.Effect(id)!.Restore?.Track).OfType<string>()).FirstOrDefault(Full);
        return effective || full is null ? "" : Template.Fill(text.TrackFull, ("track", mechanics.Tracks.First(t => t.Id == full).Name));
    }

    internal bool Use(int index, ulong revision)
    {
        if (revision != Revision) return Refuse(text.CaseChanged);
        string reason = UseReason(index);
        if (reason.Length != 0) return Refuse(reason);
        ItemDefinition item = Item(Slot(index)!.Value.Item);
        foreach (var (track, amount) in item.Use!.Restores) Stats.Track(track).Restore(amount);
        foreach (string effect in item.Use.Effects) Stats.Effects.Apply(mechanics.Effect(effect)!, $"item.{item.Id}");
        fieldCase.ConsumeOne(index);
        Revision++;
        Message = Template.Fill(text.Used, ("item", item.Name));
        return true;
    }

    /// <summary>Why the item in a pocket cannot be worn now, or empty when it can.</summary>
    internal string WearReason(int index)
    {
        if (Health == 0) return text.Overwhelmed;
        if (index < 0 || index >= Capacity || Slot(index) is not { } stack) return text.EmptyPocket;
        return Item(stack.Item).Wear is null ? Template.Fill(text.NotWorn, ("item", Item(stack.Item).Name)) : "";
    }

    /// <summary>Wears a pocket's item in the slots that take it, trading places with what its slot held if it must.</summary>
    internal bool Wear(int index, ulong revision)
    {
        if (revision != Revision) return Refuse(text.CaseChanged);
        string reason = WearReason(index);
        if (reason.Length != 0) return Refuse(reason);
        ItemDefinition item = Item(Slot(index)!.Value.Item);
        switch (fieldCase.Wear(index, out WornItem? other, out WornItem? worn))
        {
            case CaseRefusal.Exclusive: return Refuse(Template.Fill(text.Exclusive, ("item", item.Name), ("other", other!.Item.Name)));
            case CaseRefusal.NoSlot: return Refuse(Template.Fill(text.NotWorn, ("item", item.Name)));
        }
        Stats.SetEquipmentSources(fieldCase.Sources);
        Revision++;
        Message = Template.Fill(text.Wearing, ("item", item.Name), ("slot", string.Join(" · ", worn!.Slots.Select(s => s.Name))));
        return true;
    }

    /// <summary>Takes off what a slot (by its authored order) holds into the first empty pocket.</summary>
    internal bool TakeOff(int slot, ulong revision)
    {
        if (revision != Revision) return Refuse(text.CaseChanged);
        if (Health == 0) return Refuse(text.Overwhelmed);
        if (slot < 0 || slot >= definition.Slots.Length) return Refuse(text.EmptySlot);
        switch (fieldCase.TakeOff(definition.Slots[slot], out WornItem? worn))
        {
            case CaseRefusal.EmptySlot: return Refuse(text.EmptySlot);
            case CaseRefusal.NoPocket: return Refuse(Template.Fill(text.NoPocketFree, ("item", worn!.Item.Name)));
        }
        Stats.SetEquipmentSources(fieldCase.Sources);
        Revision++;
        Message = Template.Fill(text.TookOff, ("item", worn!.Item.Name));
        return true;
    }

    /// <summary>The item held in a hand, if any.</summary>
    internal WornItem? Held(Hand hand) => fieldCase.Held(hand);

    /// <summary>Trades what the hands hold; false when there is nothing to trade or it would not fit.</summary>
    internal bool SwapHands()
    {
        if (Health == 0 || !fieldCase.SwapHands()) return false;
        Stats.SetEquipmentSources(fieldCase.Sources);
        Revision++;
        return true;
    }

    /// <summary>The first pocket holding an item of a classification, or -1.</summary>
    internal int PocketOf(string classification) => Enumerable.Range(0, Capacity).FirstOrDefault(
        i => Slot(i) is { } stack && Item(stack.Item).Classifications.Contains(classification), -1);

    internal bool Move(int from, int to, ulong revision)
    {
        if (revision != Revision) return Refuse(text.CaseChanged);
        if (from < 0 || to < 0 || from >= Capacity || to >= Capacity || from == to || Slot(from) is null)
            return Refuse(text.ChooseStack);
        if (!fieldCase.Move(from, to)) return Refuse(text.StackFull);
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
                switch (action.GetString())
                {
                    case "use": Use(from, revision); break;
                    case "wear": Wear(from, revision); break;
                    // For take-off, "from" is the slot in authored order.
                    case "takeOff": TakeOff(from, revision); break;
                    case "move" when Integer(root, "to", out int to): Move(from, to, revision); break;
                    default: Refuse(text.ChooseAction); break;
                }
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
        if (count <= 0 || count > MaximumDeveloperGift || definition.Item(item) is null) return Refuse("Unknown item or invalid quantity.");
        if (!Add(item, count)) return false;
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

    internal SuppliesState Capture()
    {
        var (pockets, worn) = fieldCase.Capture();
        return new(Stats.Capture(), pockets, worn, collected.Order().ToArray());
    }

    internal void Validate(SuppliesState state)
    {
        if (state.Stats is null) throw new InvalidOperationException("Checkpoint supplies have no stats.");
        fieldCase.Validate(state.Pockets, state.Worn);
        // Track maximums follow what the saved case wears.
        FieldCase scratch = new(definition, loot, Capacity, new EntityId(0));
        scratch.Restore(state.Pockets, state.Worn);
        Stats.Validate(state.Stats, scratch.Sources);
        if (!(state.Stats.Tracks.GetValueOrDefault(HealthTrack) > 0) || state.Collected is null ||
            state.Collected.Distinct().Count() != state.Collected.Length ||
            state.Collected.Any(id => !finds.Any(f => f.Id == id) && !searches.Any(s => s.Id == id)))
            throw new InvalidOperationException("Checkpoint supplies or collected finds are invalid.");
    }

    /// <summary>Restores validated supplies: the case and what it wears first, so the stats restore under their maximums.</summary>
    internal void Restore(SuppliesState state)
    {
        Reset();
        fieldCase.Restore(state.Pockets, state.Worn);
        Stats.SetEquipmentSources(fieldCase.Sources);
        collected.UnionWith(state.Collected);
        Stats.Restore(state.Stats);
    }

    internal void Reset()
    {
        fieldCase.Restore(new ItemStack?[Capacity], definition.Kit);
        collected.Clear();
        Stats.SetEquipmentSources(fieldCase.Sources);
        Stats.Reset();
        Revision++;
        Message = "";
    }

    // Room for the whole find or none of it: a refusal leaves it in the world and names what is short.
    private bool Add(string item, int count, Loot.ItemRoll? roll = null)
    {
        switch (fieldCase.Add(item, count, out CapacityMetric? metric, roll is { Generated: true } ? roll : null))
        {
            case CaseRefusal.Pockets: return Refuse(text.CaseFull);
            case CaseRefusal.Capacity: return Refuse(Template.Fill(text.TooMuch, ("item", Name(new ItemStack(item, count, roll))), ("metric", metric!.Name)));
        }
        return true;
    }

    private bool Refuse(string message) { Message = message; return false; }
}
