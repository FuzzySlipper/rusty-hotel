using Hotel.Game.Actions;
using Hotel.Game.Combat;

namespace Hotel.Game.Interface.Workbench;

/// <summary>A workbench control and how setting it applies, through the owner of what it changes.</summary>
internal sealed record Tunable(WorkbenchControl Control, Action<float> Set);

/// <summary>
/// The first-person values the workbench tunes for a shown look and action: the guide, the look's stance and nudge,
/// the held key's pose, and the arms. Each setter changes the loaded content, then seats or redraws through
/// <see cref="HeldCatalog"/> and <see cref="CombatView"/>, as the console commands do.
/// </summary>
internal static class ViewmodelTunables
{
    internal static List<(string Id, string Title, Tunable[] Controls)> For(CombatView view, HeldCatalog held, string look, ActionDefinition action, int key)
    {
        HeldModel model = held.Model(look);
        HeldMotion motion = held.Motion.Motions[model.Motion];
        List<(string, string, Tunable[])> groups = [];
        void Redraw() => view.Publish();
        void Reseat(string stance)
        {
            foreach (HeldModel each in held.Looks.Where(l => l.Stance == stance)) held.Seat(each);
            Redraw();
        }

        groups.Add(("view", "View",
        [
            new(new("view.guide", "stance guide", "toggle", 0, 1, 1, view.Guiding ? 1 : 0), v => view.ShowGuide(v >= .5f))
        ]));

        HeldStance stance = held.Stances.Stances[model.Stance];
        float[] hand = stance.Hand ?? [.. held.Arms.MainHand.Rotation];
        groups.Add(($"stance.{model.Stance}", $"Stance · {model.Stance} (every look in it)",
        [
            .. Vector("stance.grip", "grip", stance.Grip, -0.8f, 0.8f, .005f, () => Reseat(model.Stance)),
            .. Vector("stance.toward", "toward", stance.Toward, -12, 12, .01f, () => Reseat(model.Stance)),
            .. Vector("stance.up", "up", stance.Up, -1, 1, .01f, () => Reseat(model.Stance)),
            .. Vector("stance.hand", "hand", hand, -180, 180, 1, () =>
            {
                held.Stances.Stances[model.Stance] = held.Stances.Stances[model.Stance] with { Hand = hand };
                Redraw();
            })
        ]));

        groups.Add(($"nudge.{look}", $"Nudge · {look}",
        [
            .. Vector("nudge.grip", "grip", model.Adjust.Grip, -0.2f, 0.2f, .002f, () => { held.Seat(model); Redraw(); }),
            .. Vector("nudge.turn", "turn", model.Adjust.Turn, -60, 60, .5f, () => { held.Seat(model); Redraw(); })
        ]));

        // The keys' timing and curves, for the action's track (its own, or the motion's default): each key's phase,
        // its moment through that phase, and the curve it eases in by; and the settle back to rest.
        if (motion.Track(action.Id) is { } track)
        {
            string trackId = motion.Tracks.ContainsKey(action.Id) ? action.Id : HeldMotionCatalog.DefaultTrack;
            List<Tunable> timing = [];
            for (int i = 0; i < track.Length; i++)
            {
                int index = i;
                MotionKey k = track[i];
                string at = $"key.{i}";
                timing.Add(new(new($"{at}.phase", $"{i} {k.Pose} phase", "choice", 0, Phases.Length - 1, 1, Array.IndexOf(Phases, k.Phase),
                    [.. Phases.Select(p => p.ToString())], true), v => { track[index] = track[index] with { Phase = Phases[(int)v] }; Redraw(); }));
                timing.Add(new(new($"{at}.at", $"{i} {k.Pose} at", "slider", 0, 1, .01f, k.At, null, true),
                    v => { track[index] = track[index] with { At = v }; Redraw(); }));
                timing.Add(new(new($"{at}.ease", $"{i} {k.Pose} ease", "choice", 0, Eases.Length - 1, 1, Array.IndexOf(Eases, k.Ease),
                    [.. Eases.Select(e => e.ToString())]), v => { track[index] = track[index] with { Ease = Eases[(int)v] }; Redraw(); }));
            }
            groups.Add(($"track.{model.Motion}.{trackId}", $"Keys · {model.Motion} · {trackId} (every look in it)", [.. timing]));
        }

        if (key >= 0 && motion.Track(action.Id) is { } keys && key < keys.Length)
        {
            string name = keys[key].Pose;
            HeldPose pose = motion.Poses[name];
            void Show() => view.Hold(pose);
            groups.Add(($"pose.{model.Motion}.{name}", $"Pose · {name} ({model.Motion}, every look in it)",
            [
                .. Vector("pose.position", "move", pose.Position, -0.5f, 0.5f, .005f, Show),
                .. Vector("pose.rotation", "turn", pose.Rotation, -180, 180, 1, Show)
            ]));
        }

        ArmsDefinition arms = held.Arms;
        groups.Add(("arms", "Arms",
        [
            .. Vector("arms.shoulder", "shoulder", arms.Shoulder, -0.6f, 0.6f, .005f, Redraw),
            .. Vector("arms.pole", "elbow toward", arms.Pole, -2, 2, .01f, Redraw),
            .. Vector("arms.main.palm", "main palm", arms.MainHand.Palm, -0.15f, 0.15f, .002f, Redraw),
            .. Vector("arms.off.rotation", "off hand", arms.OffHand.Rotation, -180, 180, 1, Redraw),
            .. Vector("arms.off.palm", "off palm", arms.OffHand.Palm, -0.15f, 0.15f, .002f, Redraw),
            .. Vector("arms.offPole", "off elbow toward", arms.OffPole, -2, 2, .01f, Redraw)
        ]));
        return groups;
    }

    private static readonly MotionPhase[] Phases = Enum.GetValues<MotionPhase>();
    // The named curves keys may use: those needing parameters (bezier, steps, spring) are not authored in held motion.
    private static readonly Rusty.Engine.TweenEasingKind[] Eases = [.. Enum.GetValues<Rusty.Engine.TweenEasingKind>()
        .Where(e => e is not (Rusty.Engine.TweenEasingKind.CubicBezier or Rusty.Engine.TweenEasingKind.Steps or Rusty.Engine.TweenEasingKind.Spring))];

    /// <summary>Where the keys of an action fall out of time order (empty when they are in order), as content checks them.</summary>
    internal static string Order(HeldMotion motion, ActionDefinition action)
    {
        if (motion.Track(action.Id) is not { } keys) return "";
        float[] times = [.. keys.Select(k => k.Time(action.Timing))];
        for (int i = 1; i < times.Length; i++)
            if (times[i] <= times[i - 1])
                return $"Key {i} ({keys[i].Pose}, {times[i]:0.###} s) does not follow key {i - 1} ({times[i - 1]:0.###} s): content refuses keys out of time order.";
        return "";
    }

    // Three sliders over one authored point, applied in place.
    private static IEnumerable<Tunable> Vector(string id, string label, float[] values, float min, float max, float step, Action applied)
    {
        string[] axes = ["x", "y", "z"];
        for (int i = 0; i < 3; i++)
        {
            int axis = i;
            yield return new(new($"{id}.{axes[i]}", $"{label} {axes[i]}", "slider", min, max, step, values[i]), v =>
            {
                values[axis] = v;
                applied();
            });
        }
    }
}
