using System.Text;
using Hotel.Game.Scene;
using Hotel.Game.Supplies;
using Rusty.Engine;
using Rusty.Engine.Entities;

internal static class SuppliesChecks
{
    internal static void Run(IEngineContext engine)
    {
        var content = Owners.Content(engine);
        using EntityStore entities = new([]);
        EntityId owner = entities.Create();
        HotelSupplies supplies = Owners.Supplies(content, owner, 2);
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        Check(supplies.Pickup("refuge-dressing"), "real dressing pickup");
        Check(!supplies.Pickup("refuge-dressing") && supplies.Slot(0)?.Count == 1, "duplicate pickup refused");
        Check(supplies.Pickup("portrait-dressing") && supplies.Slot(0)?.Count == 3, "stack existing item before occupying new pocket");
        Check(supplies.Pickup("linen-rounds"), "second distinct item fits");
        Check(!supplies.Pickup("survey-incense") && !supplies.Collected("survey-incense"), "full case leaves find available");
        Check(!supplies.Give("bandage", 1) && supplies.Slot(0)?.Count == 3, "full stacks do not overflow");
        ulong before = supplies.Revision;
        Check(supplies.Use(0, before) && supplies.Health == 100 && supplies.Slot(0)?.Count == 2, "heal and consume one coherently");
        Check(!supplies.Use(0, before) && supplies.Slot(0)?.Count == 2, "stale consumption rejected");
        Check(!supplies.Use(0, supplies.Revision) && supplies.Slot(0)?.Count == 2, "full health refuses item waste");
        Check(supplies.Use(1, supplies.Revision) && supplies.Ammo == 6 && supplies.Slot(1) is null, "ammo transfers to reserve and releases pocket");
        Check(!supplies.Use(1, supplies.Revision), "empty pocket consumption refused");
        Check(supplies.Pickup("survey-incense") && supplies.Collected("survey-incense"), "previously refused find can be collected after making space");
        Check(supplies.Use(1, supplies.Revision) && supplies.Summon == 1, "incense replenishes shared reserve");
        Check(!supplies.SpendAmmo(7) && !supplies.SpendAmmo(-1) && supplies.Ammo == 6, "ammo cannot overspend or accept negative cost");
        Check(supplies.SpendAmmo(6) && !supplies.SpendAmmo(1) && supplies.Ammo == 0, "ammo reaches zero without going negative");
        Check(!supplies.SpendSummon(2) && supplies.SpendSummon(1) && !supplies.SpendSummon(1) && supplies.Summon == 0, "shared reserve never negative");
        Check(supplies.Pickup("survey-reel") && !supplies.Use(1, supplies.Revision), "expedition find remains carried and is not consumed");
        Check(supplies.Move(0, 1, supplies.Revision) && supplies.Slot(0)?.Item == "reel", "moving different items swaps pockets");
        Check(!supplies.Move(-1, 2, supplies.Revision), "invalid move refused");
        Check(!supplies.SetHealth(-1) && !supplies.Give("bandage", -2), "developer fixtures preserve resource bounds");
        supplies.Reset();
        Check(supplies.Occupied == 0 && !supplies.Collected("refuge-dressing") && supplies.Health == 70, "new run resets inventory and authored resources");
        HotelSupplies partial = Owners.Supplies(content, owner, 1);
        Check(partial.Give("bandage", 2), "partial stack fixture");
        Check(!partial.Pickup("portrait-dressing") && partial.Slot(0)?.Count == 2 && !partial.Collected("portrait-dressing"), "whole-find admission refuses partial capacity without changing stack or find");
        HotelSupplies merging = Owners.Supplies(content, owner, 2);
        Check(merging.Give("bandage", 4), "two-stack fixture");
        Check(merging.Move(0, 1, merging.Revision) && merging.Slot(0)?.Count == 1 && merging.Slot(1)?.Count == 3,
            "partial merge keeps the remainder and conserves quantity");
        Check(merging.Use(1, merging.Revision) && merging.Move(0, 1, merging.Revision) &&
            merging.Slot(0) is null && merging.Slot(1)?.Count == 3, "whole merge retires the source stack");
        ulong staleMove = merging.Revision;
        Check(merging.Move(1, 0, staleMove) && merging.Slot(0)?.Count == 3 && merging.Slot(1) is null &&
            !merging.Move(0, 1, staleMove), "empty-pocket move preserves stack and rejects stale replay");
        supplies.Reset(); supplies.Pickup("refuge-dressing");
        ulong claimRevision = supplies.Revision;
        supplies.HandleIntents([Claim($"{{\"action\":\"move\",\"from\":0,\"to\":1,\"revision\":{claimRevision}}}")]);
        Check(supplies.Slot(1)?.Item == "bandage" && supplies.Slot(0) is null, "semantic move changes the real ledger pocket");
        supplies.HandleIntents([Claim($"{{\"action\":\"use\",\"from\":1,\"revision\":{claimRevision}}}")]);
        Check(supplies.Health == 70 && supplies.Slot(1)?.Count == 1, "stale drag/use cannot consume a changed pocket");
        supplies.HandleIntents([Claim("{"), Claim("[]"), Claim("{\"action\":\"move\",\"from\":\"bad\",\"revision\":1}"),
            Claim("{\"action\":\"use\",\"from\":0,\"revision\":\"bad\"}")]);
        Check(supplies.Health == 70 && supplies.Slot(1)?.Count == 1, "malformed UI claims do not fault or mutate inventory");
        supplies.HandleIntents([Claim($"{{\"action\":\"use\",\"from\":1,\"revision\":{supplies.Revision}}}")]);
        Check(supplies.Health == 100 && supplies.Occupied == 0, "semantic use consumes and heals through the owner");
        supplies.Give("bandage", 1); supplies.Damage(new(1000, "blunt"));
        Check(!supplies.Use(0, supplies.Revision) && supplies.Health == 0 && supplies.Slot(0)?.Count == 1,
            "field case and quick pockets cannot revive defeat outside checkpoint recovery");
        Console.WriteLine("Supplies checks passed: capacity, stack admission, duplicate/stale actions, coherent consumption, refusal, movement, resources and reset.");
    }
    internal static ProductInputEvent Claim(string json) => default(ProductInputEvent) with
    {
        Kind = InputEventKind.DirectProductPayload, ValueKind = InputValueKind.ProductPayload,
        Intent = Encoding.UTF8.GetBytes("hotel.supplies"), PayloadContract = Encoding.UTF8.GetBytes("hotel.supplies.v1"),
        PayloadData = Encoding.UTF8.GetBytes(json)
    };
}
