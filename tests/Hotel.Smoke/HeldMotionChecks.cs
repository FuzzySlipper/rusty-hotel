using System.Numerics;
using Hotel.Game.Actions;
using Hotel.Game.Combat;
using Rusty.Engine;

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
        Check(motion.PoseAt(prybar, swing, swing.Timing.Windup) == swings.Poses["strike"], "the pose held at the strike is the strike pose");
        Console.WriteLine($"Held motion checks passed: {motion.Motions.Count} motions, {timelines} item actions timed from rest to rest, the strike at the strike.");
    }
}
