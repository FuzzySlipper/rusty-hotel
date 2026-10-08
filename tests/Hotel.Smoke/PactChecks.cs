using System.Numerics;
using Hotel.Game.Combat;
using Hotel.Game.Expedition;
using Hotel.Game.Player;
using Hotel.Game.Residents;
using Hotel.Game.Scene;
using Hotel.Game.Spirits;
using Hotel.Game.Supplies;
using Rusty.Engine;
using Rusty.Engine.Entities;

// The spirit roster: pacts are made at bells, one is equipped in the pact slot and can be switched, each call is its
// spirit's action through that slot, and each ability kind (hold, ward, reveal, push, lure) does what it says to a
// resident standing in the west wing corridor. A capture restores the pacts made and the one equipped.
internal static class PactChecks
{
    internal static void Run(IEngineContext engine)
    {
        void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
        var content = Owners.Content(engine);
        using HotelScene scene = Owners.Scene(engine, content);
        using HotelPlayer player = Owners.Player(engine, scene, content);
        HotelSupplies supplies = Owners.Supplies(content, scene.PlayerEntity, 12);
        // A resident that never walks of its own accord, so only the spirits move it.
        ResidentKind still = content.Combat.Residents.Single(r => r.Id == "porter") with
        {
            Id = "still", Movement = new(0, 0, Post: new()), Perception = new(0.1f, 1, 0, 0)
        };
        HotelCombat combat = new(engine, scene, player, supplies, content.Combat with { Residents = [.. content.Combat.Residents, still] },
            [new ResidentPlacement("still", "still", [0, .9f, -9])]);
        HotelSpirit spirit = new(content.Spirits, content.SpiritText, content.Excursion.Placements.SpiritBells, supplies, combat, player,
            content.Combat.Actions, content.Mechanics);
        HotelEnemy target = combat.Enemies.Single();
        void Stand(float z) { player.Reset(); scene.Entities.Set(scene.PlayerEntity, EngineComponentTypes.Transform, new Transform(new(0, .875f, z), Quaternion.Identity, Vector3.One)); }
        void Steps(float seconds) { for (int i = 0; i < (int)Math.Round(seconds * 60); i++) { combat.Step(1f / 60); spirit.Step(1f / 60); } }
        void Settle() { Steps(5); target.Reset(); supplies.Stats.Effects.Clear(); supplies.RestoreSummon(9); }
        bool Has(HotelEnemy e, string effect) => e.Stats.Effects.Active.Any(x => x.Definition.Id == effect);

        // Two bells in the west wing: each frees its own spirit, and its welcome charges.
        int bells = content.Excursion.Placements.SpiritBells.Length;
        Check(bells >= 2 && spirit.Acquire(0) && spirit.Acquire(1) && spirit.AnyAcquired, "two bells free two pacts");
        string first = spirit.Bells[0].Spirit, second = spirit.Bells[1].Spirit;
        Check(spirit.Acquired(first) && spirit.Acquired(second) && !spirit.Acquire(1), "each pact is made once");

        // Hold: Hushwing at the resident under the reticle.
        Check(spirit.Equip("hushwing", spirit.Revision) && spirit.Equipped?.Id == "hushwing", "one pact goes in the pact slot");
        Stand(-6); supplies.RestoreSummon(9);
        Check(spirit.Call(), "Hushwing answers"); Steps(.1f);
        Check(target.Stats.Effects.Held && target.Phase == AttackPhase.Interrupted && spirit.Visiting?.Id == "hushwing", "hold: the resident is held still");
        Check(!spirit.Equip("lintmoth", spirit.Revision), "the pact cannot be switched while a spirit visits");
        Settle();

        // Ward and reveal: the Lintmoth settles on the investigator.
        Check(spirit.Equip("lintmoth", spirit.Revision) && spirit.Equipped?.Id == "lintmoth", "the pact slot switches to another pact");
        int charges = supplies.Summon;
        Check(spirit.Call() && supplies.Summon == charges - 1, "the Lintmoth answers for its charge"); Steps(.1f);
        Check(supplies.Stats.Effects.Active.Any(e => e.Definition.Ward is not null) && supplies.Stats.Effects.Reveal is not null && spirit.Visiting?.Id == "lintmoth",
            "ward and reveal: the investigator is warded and keen");
        Settle();

        // Push and lure need pacts not made in the west wing; a restored state makes them.
        spirit.Restore(new SpiritState(content.Spirits.Select(s => s.Id).ToArray(), "doorman"));
        Stand(-7);
        float before = Vector3.Distance(target.Position, player.Position);
        Check(spirit.Call(), "the Doorman answers"); Steps(.1f);
        Check(Has(target, "pushed"), "push: the Doorman's sweep throws the resident back");
        Steps(.6f);
        Check(Vector3.Distance(target.Position, player.Position) > before + 1, $"the resident is driven away: {before:F2} to {Vector3.Distance(target.Position, player.Position):F2}");
        Settle();
        spirit.Equip("wickling", spirit.Revision);
        Stand(-1);
        Check(spirit.Call(), "the Wickling answers"); Steps(.1f);
        Check(Has(target, "lured"), "lure: residents near where the Wickling lands are drawn to it");
        Steps(1.5f);
        Check(target.Position.Z > -8.6f && target.Awareness.Target is null, $"the lured resident walks toward the light: {target.Position}");
        Settle();
        // Burn and stop: the Embermoth scatters embers where it is sent; the Stillwing stops what stands near it.
        spirit.Equip("embermoth", spirit.Revision);
        Stand(target.Position.Z + content.Combat.Actions.Action("embermoth-scatter")!.Delivery.Range);
        int unburnt = target.Health.ValueInt;
        Check(spirit.Call(), "the Embermoth answers"); Steps(.1f);
        Check(Has(target, "burning") && target.Health.ValueInt < unburnt, $"burn: embers scorch the resident and leave it burning ({unburnt} to {target.Health.ValueInt})");
        Settle();
        spirit.Equip("stillwing", spirit.Revision);
        Check(spirit.Call(), "the Stillwing answers"); Steps(.1f);
        Check(Has(target, "stilled") && target.Stats.Effects.Held, "stop: the resident stands stopped");
        Steps(content.Mechanics.Effect("stilled")!.Duration + .2f);
        Check(!Has(target, "stilled") && !target.Stats.Effects.Held, "and moves again when the clocks start");
        Settle();

        // A capture restores the pacts made and the one equipped; one not in the roster, or equipped unmade, is refused.
        SpiritState saved = spirit.Capture();
        HotelSpirit restored = new(content.Spirits, content.SpiritText, content.Excursion.Placements.SpiritBells, supplies, combat, player,
            content.Combat.Actions, content.Mechanics);
        restored.Validate(saved);
        restored.Restore(saved);
        Check(restored.Capture().Acquired.SequenceEqual(saved.Acquired) && restored.Equipped?.Id == "stillwing", "a capture restores pacts and the pact slot");
        foreach (SpiritState invalid in new[] { saved with { Acquired = [.. saved.Acquired, "ghostmoth"] }, new SpiritState(["hushwing"], "doorman") })
        {
            bool refused = false;
            try { restored.Validate(invalid); } catch (InvalidOperationException) { refused = true; }
            Check(refused, "an unknown pact, or one equipped but not made, is refused");
        }
        Console.WriteLine($"Pact checks passed: {content.Spirits.Length} spirits, {bells} west-wing bells; acquiring, equipping and switching, " +
            "hold, ward, reveal, push, lure, burn and stop through each pact's call, and a round trip.");
    }
}
