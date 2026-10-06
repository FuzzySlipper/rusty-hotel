using System.Numerics;
using Hotel.Game;
using Hotel.Game.Mechanics;
using Hotel.Game.Supplies;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using EffectDefinition = Hotel.Game.Mechanics.EffectDefinition;

// Effects are Engine effect entries with product time: each stacking rule meets a second application as authored,
// over-time effects tick on their interval and expire on admitted seconds, wards and stats change what a hit and a
// value are, a paused product holds every effect, and a capture restores the same effects with their time left.
internal static class EffectChecks
{
    private const float Step = 1f / 60;

    internal static void Run(IEngineContext engine)
    {
        void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
        var content = Owners.Content(engine);
        MechanicsDefinition mechanics = content.Mechanics;
        EffectDefinition Effect(string id) => mechanics.Effect(id)!;
        ActorStats Actor(ulong id) => new(mechanics, content.PlayerStats, new EntityId(id));
        void Advance(ActorStats actor, float seconds) { for (int i = 0; i < (int)Math.Round(seconds / Step); i++) actor.Effects.Advance(Step); }

        // Refresh gathers stacks up to its limit in one instance and restarts its time.
        ActorStats player = Actor(1);
        LiveEffect mending = player.Effects.Apply(Effect("mending"), "item.bandage")!;
        Advance(player, 2);
        LiveEffect again = player.Effects.Apply(Effect("mending"), "item.bandage")!;
        player.Effects.Apply(Effect("mending"), "item.bandage");
        Check(player.Effects.Active.Count == 1 && again.Instance == mending.Instance && player.Effects.Active[0].Stacks == 2 &&
            player.Effects.Active[0].Remaining == Effect("mending").Duration, "refresh stacks to its limit in one instance and restarts");

        // Ticks restore per stack on their interval; the effect ends at its duration, not a step later.
        player.Track(ActorStats.HealthTrack).SetCurrent(20);
        Advance(player, 1);
        Check(player.Track(ActorStats.HealthTrack).ValueInt == 26, $"two stacks of mending restore 6 a second: {player.Track(ActorStats.HealthTrack).ValueInt}");
        Advance(player, Effect("mending").Duration - 1 - Step);
        Check(player.Effects.Active.Count == 1 && player.Track(ActorStats.HealthTrack).ValueInt == 20 + 6 * 5, "mending lasts its duration");
        Advance(player, Step);
        Check(player.Effects.Active.Count == 0 && player.Track(ActorStats.HealthTrack).ValueInt == 20 + 6 * 6,
            "the last tick lands as the effect expires");

        // Independent effects keep one instance per source up to their limit; the same source restarts its own.
        ActorStats burned = Actor(2);
        LiveEffect first = burned.Effects.Apply(Effect("burning"), "resident.a")!;
        burned.Effects.Apply(Effect("burning"), "resident.b");
        Check(burned.Effects.Apply(Effect("burning"), "resident.c") is null && burned.Effects.Active.Count == 2,
            "a third source is refused at the instance limit");
        Advance(burned, 1);
        Check(burned.Effects.Apply(Effect("burning"), "resident.a") == first && first.Remaining == Effect("burning").Duration &&
            burned.Effects.Active.Count == 2, "the same source restarts its own instance");
        Check(burned.Track(ActorStats.HealthTrack).ValueInt == 70 - 4, "two burns deal a point each every half second");

        // Replace starts a fresh instance; a ward takes its share of a hit before health, and ends when spent.
        ActorStats warded = Actor(3);
        LiveEffect ward = warded.Effects.Apply(Effect("warded"), "item.incense")!;
        Advance(warded, 5);
        LiveEffect fresh = warded.Effects.Apply(Effect("warded"), "item.incense")!;
        Check(warded.Effects.Active.Count == 1 && fresh.Instance != ward.Instance && fresh.Remaining == Effect("warded").Duration,
            "replace swaps in a fresh instance");
        Check(warded.TakeDamage(new(20, "fire"), ActorStats.HealthTrack) == 0 && fresh.WardLeft == 4 &&
            warded.TakeDamage(new(10, "blunt"), ActorStats.HealthTrack) == 6 && warded.Effects.Active.Count == 0,
            "the ward absorbs what it can and ends when spent");

        // A stat effect is an Engine source with its own provenance: nerve rises and so does what derives from it.
        ActorStats steady = Actor(4);
        steady.Effects.Apply(Effect("steadied"), "item.salts");
        StatEvaluation nerve = steady.Explain("nerve");
        Check(nerve.Value == 14 && nerve.Decisions.Single().Source is EffectSourceIdentity source && source.Source.Value == "effect.steadied" &&
            steady.Stat("maximum-health").Value == 108, "steadied adds nerve through an effect source and maximum health follows");
        // A slow scales pace while it lasts, then pace returns.
        steady.Effects.Apply(Effect("staggered"), "resident.porter");
        Check(steady.Pace == .55f, $"a slow sets the pace: {steady.Pace}");
        Advance(steady, Effect("staggered").Duration - .1f);
        Check(steady.Effects.Active.Count == 2, "the slow holds until its time is up");
        Advance(steady, .1f);
        Check(steady.Pace == 1 && steady.Effects.Active.Count == 1, "pace returns when the slow expires");

        // A capture restores each effect with its stacks, time left, tick progress and ward, and the stats they decide.
        steady.Effects.Apply(Effect("mending"), "item.bandage");
        steady.Effects.Apply(Effect("mending"), "item.bandage");
        steady.Effects.Apply(Effect("warded"), "item.incense");
        Advance(steady, 1.5f);
        steady.TakeDamage(new(5, "spirit"), ActorStats.HealthTrack);
        ActorStatsState saved = steady.Capture();
        ActorStats restored = Actor(5);
        restored.Validate(saved);
        restored.Restore(saved);
        static string Shape(ActorStats a) => string.Join(";", a.Effects.Active.Select(e => $"{e.Definition.Id}:{e.Stacks}:{e.Remaining:F4}:{e.SinceTick:F4}:{e.WardLeft}"));
        Check(Shape(restored) == Shape(steady) && restored.Stat("maximum-health").Value == 108 &&
            restored.Track(ActorStats.HealthTrack).Value == steady.Track(ActorStats.HealthTrack).Value, "a capture restores effects and the stats they decide");
        Advance(steady, 3); Advance(restored, 3);
        Check(Shape(restored) == Shape(steady) && restored.Track(ActorStats.HealthTrack).Value == steady.Track(ActorStats.HealthTrack).Value,
            "restored effects carry on exactly as the originals do");
        foreach (ActorStatsState invalid in new[] {
            saved with { Effects = [.. saved.Effects, saved.Effects[0]] },
            saved with { Effects = [saved.Effects[0] with { Remaining = 999 }] },
            saved with { Effects = [saved.Effects[0] with { Stacks = 9 }] },
            saved with { Effects = [saved.Effects[0] with { Effect = "cursed" }] } })
        {
            bool refused = false;
            try { restored.Validate(invalid); } catch (InvalidOperationException) { refused = true; }
            Check(refused, "a duplicate, overlong, overstacked or unknown saved effect is refused");
        }

        // In the product: a paused use applies its effect but no time passes until the product is admitted again, and
        // a slowed investigator covers less ground for the same held key.
        using HotelProduct product = new(new ProductCreateContext(engine, new ProductContent(default),
            new ProductInputConfiguration(default, default, default, default, InputCursorMode.PointerLock), default!));
        product.Start();
        ulong at = 0;
        void Admit(uint count, params ProductInputEvent[] input)
        {
            product.Update(new ProductUpdate(new ProductUpdateFacts(ProductUpdateMode.Realtime,
                ProductLifecycleState.Running, 1, 1, 0, at, 60, count, 0, 1d / 60), input));
            at += count;
        }
        HotelSupplies supplies = product.World.Supplies;
        Admit(60);
        supplies.Give("bandage", 1);
        supplies.SetHealth(30);
        product.Pause();
        product.HandlePausedIntents([SuppliesChecks.Claim($"{{\"action\":\"use\",\"from\":0,\"revision\":{supplies.Revision}}}")]);
        LiveEffect paused = supplies.Stats.Effects.Active.Single();
        product.HandlePausedIntents([SuppliesChecks.Claim($"{{\"action\":\"move\",\"from\":0,\"to\":1,\"revision\":{supplies.Revision}}}")]);
        Check(paused.Definition.Id == "mending" && paused.Remaining == Effect("mending").Duration &&
            supplies.Health == 30 + supplies.Item("bandage").Restores(HotelSupplies.HealthTrack), "a paused use applies its effect and holds it");
        product.Resume();
        Admit(60);
        Check(MathF.Abs(paused.Remaining - (Effect("mending").Duration - 1)) < .001f && supplies.Health == 30 + supplies.Item("bandage").Restores(HotelSupplies.HealthTrack) + 3,
            "admitted time runs the effect again");
        Vector3 start = product.World.Player.Position;
        Admit(30, Key(KeyboardControl.KeyW));
        Admit(0, default(ProductInputEvent) with { Kind = InputEventKind.Clear });
        float stride = Vector3.Distance(start, product.World.Player.Position);
        Admit(30);
        supplies.Afflict("staggered", "resident.porter");
        start = product.World.Player.Position;
        Admit(30, Key(KeyboardControl.KeyW));
        Admit(0, default(ProductInputEvent) with { Kind = InputEventKind.Clear });
        float slowed = Vector3.Distance(start, product.World.Player.Position);
        Check(slowed < stride * .7f && slowed > stride * .4f, $"a staggered investigator walks at their slowed pace: {slowed:F2} of {stride:F2}");
        Console.WriteLine($"Effect checks passed: {mechanics.Effects.Length} effects; refresh, replace and independent stacking, ticks and expiry on " +
            "admitted time, ward absorption, stat and pace sources with provenance, paused holding, and a round trip with time left.");
    }

    private static ProductInputEvent Key(KeyboardControl key) => default(ProductInputEvent) with
    { Kind = InputEventKind.Key, Edge = InputEdge.Pressed, Keyboard = key, X = 1 };
}
