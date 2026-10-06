using Hotel.Game.Mechanics;
using Hotel.Game.Supplies;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;

// Actors' stats are Engine stats in the content vocabulary: derived stats carry attribute sources that explain
// themselves, tracks spend or refuse, damage lands after the target's resistance to its kind, and a capture restores the
// same stats and tracks.
internal static class MechanicsChecks
{
    internal static void Run(IEngineContext engine)
    {
        void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
        var content = Owners.Content(engine);
        MechanicsDefinition mechanics = content.Mechanics;
        ActorStats player = new(mechanics, content.PlayerStats, new EntityId(1));

        // Derived from attributes, with the attribute source named in the evaluation.
        StatEvaluation health = player.Explain("maximum-health");
        Check(health.Value == 100 && health.Base == 60 && health.Decisions.Count == 2 &&
            health.Decisions.All(d => d.Outcome == MechanicsDecisionOutcome.Applied && d.SourceDefinition.Value == "derived.maximum-health") &&
            health.Decisions[0].Source is IntrinsicSourceIdentity, $"maximum health derives from might and nerve with provenance: {health.Value}");
        Check(player.Track(HotelSupplies.HealthTrack).ValueInt == 70 && player.Track(HotelSupplies.SummonTrack).MaximumValue == 3,
            "tracks start at their authored points under their derived maximums");

        // Raising an attribute raises what derives from it; the track keeps its points under the new maximum.
        player.Stat("might").BaseValue = 15;
        player.RefreshDerived();
        Check(player.Stat("maximum-health").Value == 110 && player.Track(HotelSupplies.HealthTrack).ValueInt == 70 &&
            player.Track(HotelSupplies.HealthTrack).MaximumValue == 110, "a stronger investigator has more maximum health");

        // Tracks spend or refuse whole.
        Track summon = player.Track(HotelSupplies.SummonTrack);
        Check(!summon.TrySpend(1) && summon.ValueInt == 0, "an empty track refuses a spend and is unchanged");
        summon.Restore(5);
        Check(summon.ValueInt == 3 && summon.TrySpend(2) && summon.ValueInt == 1, "a track restores to its maximum and spends within it");

        // Damage by kind against resistance: halved by the Lamplighter's fire resistance, untouched by kinds it has none to,
        // and doubled through a weakness.
        ActorStats lamp = new(mechanics, content.Combat.Residents.First(r => r.Id == "lamp").Stats, new EntityId(2));
        Check(lamp.Resistance("fire") == 0.5 && lamp.TakeDamage(new(20, "fire"), HotelSupplies.HealthTrack) == 10 &&
            lamp.TakeDamage(new(20, "pierce"), HotelSupplies.HealthTrack) == 20 && lamp.Track(HotelSupplies.HealthTrack).ValueInt == 30,
            "fire is halved by the Lamplighter's resistance, piercing is not");
        ActorStatBlock weak = content.PlayerStats with { Resistances = new() { ["spirit"] = -1 } };
        mechanics.Validate("weak", "stats", weak);
        ActorStats frail = new(mechanics, weak, new EntityId(3));
        Check(frail.TakeDamage(new(10, "spirit"), HotelSupplies.HealthTrack) == 20 &&
            frail.TakeDamage(new(1000, "spirit"), HotelSupplies.HealthTrack) == 50 && frail.Track(HotelSupplies.HealthTrack).ValueInt == 0,
            "a weakness doubles a hit, and no hit takes more than the health left");

        // A capture restores the same bases and currents; a track above its restored maximum is refused.
        ActorStatsState saved = player.Capture();
        ActorStats restored = new(mechanics, content.PlayerStats, new EntityId(4));
        restored.Validate(saved);
        restored.Restore(saved);
        Check(restored.Stat("maximum-health").Value == 110 && restored.Track(HotelSupplies.HealthTrack).ValueInt == 70 &&
            restored.Track(HotelSupplies.SummonTrack).ValueInt == 1 && restored.Capture().Tracks.SequenceEqual(saved.Tracks) &&
            restored.Capture().Bases.SequenceEqual(saved.Bases), "a captured actor restores identical stats and tracks");
        bool refused = false;
        try { restored.Validate(saved with { Tracks = new(saved.Tracks) { [HotelSupplies.HealthTrack] = 500 } }); }
        catch (InvalidOperationException) { refused = true; }
        Check(refused, "a track above its maximum is refused before anything changes");
        Console.WriteLine($"Mechanics checks passed: {mechanics.Attributes.Length} attributes, {mechanics.Derived.Length} derived stats with attribute provenance, " +
            $"{mechanics.Tracks.Length} tracks, {mechanics.DamageKinds.Length} damage kinds with resistance and weakness, and a stats round trip.");
    }
}
