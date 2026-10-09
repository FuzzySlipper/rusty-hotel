using Hotel.Game.Content;
using Hotel.Game.Expedition;
using Hotel.Game.Mechanics;
using Hotel.Game.Supplies;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;

// The field case is the Engine inventory and equipment: its capacity limits refuse a find whole, a worn item adds its
// stats as an Engine source with its own provenance and takes them away when taken off, a slot that is full trades
// places, an exclusivity group admits one item, and a capture restores the same pockets, worn items and stats.
internal static class EquipmentChecks
{
    internal static void Run(IEngineContext engine)
    {
        void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
        var content = Owners.Content(engine);
        SuppliesDefinition definition = content.Supplies;
        CapacityMetric weight = definition.Capacity.Single(m => m.Id == "weight");
        HotelSupplies supplies = Owners.Supplies(content, new EntityId(1));
        int Pocket(string item) => Enumerable.Range(0, supplies.Capacity).First(i => supplies.Slot(i)?.Item == item);
        SlotDefinition Slot(string id) => definition.Slots.Single(s => s.Id == id);

        // Capacity: four coats are within the weight limit, a fifth is refused whole with the limit it would pass.
        int coat = definition.Item("night-coat")!.Costs["weight"];
        int kit = supplies.Used(weight), fit = (weight.Limit - kit) / coat;
        Check(supplies.Give("night-coat", fit) && supplies.Used(weight) == kit + fit * coat, "single items fill one pocket each and count their weight");
        int occupied = supplies.Occupied;
        Check(!supplies.Give("night-coat", 1) && supplies.Occupied == occupied && supplies.Used(weight) == kit + fit * coat &&
            supplies.Message.Contains(weight.Name), $"the Engine refuses a find over the weight limit and names it: {supplies.Message}");

        // Wearing: the coat leaves its pocket for the body slot, and its resistance lands as a worn-item source.
        supplies.Reset();
        supplies.Give("night-coat", 2);
        Check(supplies.Wear(0, supplies.Revision) && supplies.Slot(0) is null && supplies.WornIn(Slot("body"))?.Item.Id == "night-coat",
            "a worn coat leaves its pocket for the body slot");
        StatEvaluation blunt = supplies.Stats.Explain(ActorStats.ResistanceStat("blunt"));
        Check(Math.Abs(blunt.Value - .15) < 1e-6 && blunt.Decisions.Single().Source is EquippedItemSourceIdentity worn &&
            worn.Source.Value == "worn.night-coat", "the coat's resistance is an Engine source naming the worn item");
        Check(supplies.Damage(new(20, "blunt")) == 17, "a worn coat turns part of a blow");
        // A full slot trades places: the second coat goes on, the first comes back into its pocket.
        EntityId first = supplies.WornIn(Slot("body"))!.Entity;
        Check(supplies.Wear(1, supplies.Revision) && supplies.Slot(1)?.Item == "night-coat" && supplies.WornIn(Slot("body"))!.Entity != first,
            "wearing into a full slot swaps the worn item back into the pocket");

        // Taking off returns the item to a pocket and its stats to their base; nowhere to put it is refused.
        supplies.Reset();
        supplies.Give("porter-gloves", 1);
        Check(supplies.Wear(0, supplies.Revision) && supplies.Stats.Stat("might").Value == 12 && supplies.MaximumHealth == 104,
            "gloves add might, and maximum health follows");
        supplies.Give("signet-ring", supplies.Capacity - supplies.Occupied);
        Check(!supplies.TakeOff(Array.IndexOf(definition.Slots, Slot("hands")), supplies.Revision) && supplies.Stats.Stat("might").Value == 12,
            "taking off with every pocket full is refused and changes nothing");
        supplies.Reset();
        supplies.Give("porter-gloves", 1);
        supplies.Wear(0, supplies.Revision);
        Check(supplies.TakeOff(Array.IndexOf(definition.Slots, Slot("hands")), supplies.Revision) && supplies.Slot(0)?.Item == "porter-gloves" &&
            supplies.Stats.Stat("might").Value == 10 && supplies.MaximumHealth == 100, "taking off returns the gloves to a pocket and might to its base");
        Check(!supplies.Wear(Pocket("porter-gloves"), supplies.Revision - 1), "a stale wear is refused");
        // Worn into a chosen slot: a held item dropped on the off hand goes there, a taken slot trades with what it held,
        // and a slot that does not take the item refuses it.
        int SlotIndex(string id) => Array.IndexOf(definition.Slots, Slot(id));
        foreach (string hand in new[] { "main-hand", "off-hand" })
            if (supplies.WornIn(Slot(hand)) is not null) supplies.TakeOff(SlotIndex(hand), supplies.Revision);
        supplies.Give("letter-opener", 1);
        Check(supplies.Wear(Pocket("letter-opener"), supplies.Revision, SlotIndex("off-hand")) && supplies.WornIn(Slot("off-hand"))?.Item.Id == "letter-opener" &&
            supplies.WornIn(Slot("main-hand")) is null, "a held item worn on the off hand goes there, though the main hand is free");
        supplies.Give("walking-cane", 1);
        Check(supplies.Wear(Pocket("walking-cane"), supplies.Revision, SlotIndex("off-hand")) && supplies.WornIn(Slot("off-hand"))?.Item.Id == "walking-cane" &&
            supplies.WornIn(Slot("main-hand")) is null && Enumerable.Range(0, supplies.Capacity).Any(i => supplies.Slot(i)?.Item == "letter-opener"),
            "a taken slot trades with what it held, not with a free one");
        Check(!supplies.Wear(Pocket("letter-opener"), supplies.Revision, SlotIndex("head")) &&
            supplies.Message == Template.Fill(definition.Text.NotWornThere, ("item", definition.Item("letter-opener")!.Name), ("slot", Slot("head").Name)),
            "a slot that does not take the item refuses it");
        Check(!supplies.Use(Pocket("porter-gloves"), supplies.Revision) && supplies.Slot(0)?.Item == "porter-gloves", "a worn item is not used up");
        supplies.Give("bandage", 1);
        Check(!supplies.Wear(Pocket("bandage"), supplies.Revision), "a remedy is not worn");

        // Exclusivity: two ring slots, but one signet at a time.
        supplies.Reset();
        supplies.Give("signet-ring", 2);
        Check(supplies.Wear(0, supplies.Revision) && supplies.Stats.Stat("attunement").Value == 14, "a signet is worn in a ring slot");
        Check(!supplies.Wear(1, supplies.Revision) && supplies.Slot(1)?.Item == "signet-ring" && supplies.Stats.Stat("attunement").Value == 14 &&
            supplies.WornIn(Slot("ring-right")) is null, "a second signet is refused although a ring slot is free");

        // A capture restores pockets, worn items and the stats they feed; health above the base maximum is valid while worn.
        supplies.Reset();
        supplies.Give("porter-gloves", 1); supplies.Give("night-coat", 1); supplies.Give("bandage", 2);
        supplies.Wear(Pocket("porter-gloves"), supplies.Revision);
        supplies.Wear(Pocket("night-coat"), supplies.Revision);
        supplies.SetHealth(104);
        SuppliesState saved = supplies.Capture();
        HotelSupplies restored = Owners.Supplies(content, new EntityId(1));
        restored.Validate(saved);
        restored.Restore(saved);
        static string Shape(HotelSupplies s) => string.Join(";", s.Worn.Select(w => $"{w.Item.Id}@{string.Join("+", w.Slots.Select(x => x.Id))}")) + "|" +
            string.Join(",", Enumerable.Range(0, s.Capacity).Select(i => s.Slot(i)?.ToString() ?? "-")) + "|" + s.Health + "/" + s.MaximumHealth;
        Check(Shape(restored) == Shape(supplies) && restored.Stats.Stat("might").Value == 12 &&
            restored.Stats.Resistance("blunt") == supplies.Stats.Resistance("blunt"), "a capture restores pockets, worn items and their stats");
        foreach (SuppliesState invalid in new[] {
            saved with { Worn = [.. saved.Worn, new WornState("signet-ring", ["body"])] },
            saved with { Worn = [new WornState("signet-ring", ["ring-left"]), new WornState("signet-ring", ["ring-right"])] },
            saved with { Worn = [new WornState("porter-gloves", ["no-such-slot"])] },
            saved with { Worn = [new WornState("bandage", ["hands"])] },
            saved with { Worn = [] } })
        {
            bool refused = false;
            try { restored.Validate(invalid); } catch (InvalidOperationException) { refused = true; }
            Check(refused, "a worn item in a slot that does not take it, two signets, an unknown slot, a worn remedy, or health over the unworn maximum is refused");
        }
        Console.WriteLine($"Equipment checks passed: {definition.Slots.Length} slots, {definition.Capacity.Length} capacity limits; weight refusal, " +
            "worn sources with provenance, swap, take-off, exclusivity and a round trip.");
    }
}
