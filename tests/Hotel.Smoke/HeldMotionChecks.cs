using System.Numerics;
using Hotel.Game.Actions;
using Hotel.Game.Combat;
using Hotel.Game;
using Rusty.Engine;
using Rusty.Engine.Input;

// Held motion: every held item's actions have keys in time order, each timeline starts and ends at rest (the published
// pose) exactly when its action does, and a key timed at the end of windup reaches its pose at the strike.
internal static class HeldMotionChecks
{
    internal static void Run(IEngineContext engine)
    {
        void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
        var content = Owners.Content(engine);
        HeldCatalog held = content.Combat.Held;
        HeldMotionCatalog motion = held.Motion;
        int timelines = 0;
        foreach (var item in content.Supplies.Items.Where(i => i.Wear?.Look is not null))
            foreach (string id in item.Wear!.Actions)
            {
                ActionDefinition action = content.Combat.Actions.Action(id)!;
                HeldModel look = held.Model(item.Wear.Look!);
                TweenSegment[] segments = motion.Timeline(look, action);
                float total = action.Timing.Windup + action.Timing.Commit + action.Timing.Recovery;
                TweenSegment last = segments.Where(s => s.Channel == TweenChannel.Translation).MaxBy(s => s.StartSeconds + s.DurationSeconds);
                Check(segments.Length > 0 && Math.Abs(last.StartSeconds + last.DurationSeconds - total) < 1e-4f && last.To == Vector4.Zero &&
                    segments.Where(s => s.Channel == TweenChannel.Translation).Min(s => s.StartSeconds) == 0,
                    $"{item.Id} {id}: the motion runs from the action's start to rest at its end ({total} s)");
                timelines++;
            }
        // The pry bar's swing reaches its strike pose exactly as the swing lands.
        ActionDefinition swing = content.Combat.Actions.Action("prybar-swing")!;
        HeldModel prybar = held.Model("prybar");
        HeldMotion swings = motion.Motions[prybar.Motion];
        (Vector3 strike, _) = swings.Poses["strike"].Place(prybar);
        (Vector3 rest, _) = swings.Poses[HeldMotionCatalog.Rest].Place(prybar);
        Check(motion.Timeline(prybar, swing).Any(s => s.Channel == TweenChannel.Translation &&
            Math.Abs(s.StartSeconds + s.DurationSeconds - swing.Timing.Windup) < 1e-4f && Vector3.Distance(new(s.To.X, s.To.Y, s.To.Z), strike - rest) < 1e-4f),
            "the swing's strike key ends at the end of windup, where the action lands");
        // The Engine's own evaluation of the timeline shows the strike pose at the strike.
        TweenSample atStrike = engine.Tween.Sample(new TweenStartRequest(1, motion.Timeline(prybar, swing)), swing.Timing.Windup);
        Check(Vector3.Distance(atStrike.Translation, strike - rest) < 1e-3f, $"the timeline shows the strike pose at the strike: {atStrike.Translation} vs {strike - rest}");
        // Played: an update that admits several steps after the swing begins starts its motion that far along, so the
        // strike still lands with the hit; a hold that cuts the swing short eases the model back to rest.
        using (HotelProduct product = new(new ProductCreateContext(engine, new ProductContent(default),
            new ProductInputConfiguration(default, default, default, default, InputCursorMode.PointerLock), default!)))
        {
            TitleChecks.Begin(product);
            ulong step = 0;
            void Admit(uint count, params ProductInputEvent[] input)
            {
                product.Update(new ProductUpdate(new ProductUpdateFacts(ProductLifecycleState.Running, 1, 1, 0, step, 60, count, 0, 1d / 60), input));
                step += count;
            }
            Admit(30);
            ProductInputEvent press = default(ProductInputEvent) with { Kind = InputEventKind.Key, Edge = InputEdge.Pressed, Keyboard = KeyboardControl.ControlLeft, X = 1 };
            Admit(4, press);
            float along = product.World.Combat.User.Elapsed;
            TweenReadout playing = engine.Tween.Read(product.World.CombatView.Motion);
            float total = swing.Timing.Windup + swing.Timing.Commit + swing.Timing.Recovery;
            Check(product.World.Combat.User.Current?.Id == swing.Id && along > 1.5f / 60 && playing.State != TweenState.Ended &&
                Math.Abs(playing.TotalSeconds - total) < 1e-3f && Math.Abs(playing.ElapsedSeconds - along) < 1e-3f,
                $"a swing begun in a four-step update starts its motion {along:F3} s along, its whole {playing.TotalSeconds:F3} s timeline at {playing.ElapsedSeconds:F3} s; {product.World.Combat.User.Current?.Id}, {playing.State})");
            product.World.Supplies.Afflict("stilled", "test");
            Admit(1);
            TweenReadout settling = engine.Tween.Read(product.World.CombatView.Motion);
            Check(product.World.Combat.User.Current is null && Math.Abs(settling.TotalSeconds - motion.SettleSeconds) < 1e-3f,
                "a hold that cuts the swing short eases the model back to rest");
        }
        Console.WriteLine($"Held motion checks passed: {motion.Motions.Count} motions, {timelines} item actions timed from rest to rest, the strike at the strike.");
    }
}
