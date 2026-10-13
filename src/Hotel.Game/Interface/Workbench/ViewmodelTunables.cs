using Hotel.Game.Actions;
using Hotel.Game.Combat;
using Rusty.Engine;
using Rusty.Engine.Debugging;

namespace Hotel.Game.Interface.Workbench;

/// <summary>
/// The first-person values the Engine tunables panel shows for the look and action the motion viewer shows: the
/// guide, the look's stance and nudge, the action's key timing and curves, the held key's pose, and the arms. Each
/// control applies its value through <see cref="HeldCatalog"/> and <see cref="CombatView"/> as the console commands
/// do, and names where it is saved (its content file and JSON pointer), so the panel's Save writes it there.
/// </summary>
internal static class ViewmodelTunables
{
    internal static List<TunableGroup> For(CombatView view, HeldCatalog held, string look, ActionDefinition action, int key)
    {
        HeldModel model = held.Model(look);
        HeldMotion motion = held.Motion.Motions[model.Motion];
        int lookIndex = Array.FindIndex(held.Looks, l => l.Look == look);
        List<TunableGroup> groups = [];
        void Redraw() => view.Publish();
        void Reseat(string stance)
        {
            foreach (HeldModel each in held.Looks.Where(l => l.Stance == stance)) held.Seat(each);
            Redraw();
        }

        groups.Add(new("view", "View",
        [
            new(new("view.guide", "stance guide", TunableKind.Toggle, 0, 1, 1, view.Guiding ? 1 : 0), v => view.ShowGuide(v >= .5f))
        ]));

        HeldStance stance = held.Stances.Stances[model.Stance];
        string stancePointer = $"/stances/{Escape(model.Stance)}";
        groups.Add(new($"stance.{model.Stance}", $"Stance · {model.Stance} (every look in it)",
        [
            .. Vector("stance.grip", "grip", stance.Grip, -0.8f, 0.8f, .005f, StanceCatalog.Path, $"{stancePointer}/grip", () => Reseat(model.Stance)),
            .. Vector("stance.toward", "toward", stance.Toward, -12, 12, .01f, StanceCatalog.Path, $"{stancePointer}/toward", () => Reseat(model.Stance)),
            .. Vector("stance.up", "up", stance.Up, -1, 1, .01f, StanceCatalog.Path, $"{stancePointer}/up", () => Reseat(model.Stance)),
            // A stance without its own hand holds as the arms' main hand does; the sliders then tune and save that.
            .. (stance.Hand is { } hand
                ? Vector("stance.hand", "hand", hand, -180, 180, 1, StanceCatalog.Path, $"{stancePointer}/hand", Redraw)
                : Vector("stance.hand", "hand (arms' main hand)", held.Arms.MainHand.Rotation, -180, 180, 1, ArmsDefinition.Path, "/mainHand/rotation", Redraw))
        ]) { File = StanceCatalog.Path });

        string nudge = $"/looks/{lookIndex}/nudge";
        groups.Add(new($"nudge.{look}", $"Nudge · {look}",
        [
            .. Vector("nudge.grip", "grip", model.Adjust.Grip, -0.2f, 0.2f, .002f, HeldCatalog.Path, $"{nudge}/grip", () => { held.Seat(model); Redraw(); }),
            .. Vector("nudge.turn", "turn", model.Adjust.Turn, -60, 60, .5f, HeldCatalog.Path, $"{nudge}/turn", () => { held.Seat(model); Redraw(); })
        ]) { File = HeldCatalog.Path });

        // How the look shapes each hand's digits on the grip clip, joint by joint; the keys' closure acts on top.
        if (model.Shape is { } shape)
        {
            // Fitting curls the hands onto the model from open (as hotel.dev.hands.fit); the sliders then show it unsaved.
            string[] fits = model.Grips.Off is null ? ["-", "main"] : ["-", "main", "off", "both"];
            List<Tunable> digits =
            [
                new(new("shape.fit", "fit to the model", TunableKind.Choice, 0, fits.Length - 1, 1, 0) { Options = fits, Reshapes = true },
                    v => { if ((int)MathF.Round(v) is > 0 and var at && at < fits.Length) HandFitDebugCommands.Hands(view, held, look, fits[at], apply: true); })
            ];
            string[] joints = ["base", "middle", "tip"];
            foreach (var (side, digitsOf) in new[] { ("main", shape.Main), ("off", shape.Off) })
            {
                if (digitsOf is null) continue;
                string at = $"/looks/{lookIndex}/shape/{side}";
                for (int f = 0; f < digitsOf.Fingers.Length; f++)
                    for (int i = 0; i < 3; i++)
                        digits.Add(Angle($"shape.{side}.{f}.{i}", $"{side} {held.Arms.Hands.Fingers[f]} {joints[i]} curl", digitsOf.Fingers[f], i, -90, 120, $"{at}/fingers/{f}/{i}"));
                for (int i = 0; i < 3; i++)
                {
                    digits.Add(Angle($"shape.{side}.thumb.{i}.curl", $"{side} {held.Arms.Hands.Thumb} {joints[i]} curl", digitsOf.Thumb[i], 0, -90, 120, $"{at}/thumb/{i}/0"));
                    digits.Add(Angle($"shape.{side}.thumb.{i}.swing", $"{side} {held.Arms.Hands.Thumb} {joints[i]} swing", digitsOf.Thumb[i], 1, -90, 90, $"{at}/thumb/{i}/1"));
                }
            }
            groups.Add(new($"shape.{look}", $"Hands · {look}", digits) { File = HeldCatalog.Path });
        }
        Tunable Angle(string id, string label, float[] values, int index, float min, float max, string pointer) =>
            new(new(id, label, TunableKind.Slider, min, max, 1, values[index]) { Source = new(HeldCatalog.Path, pointer) },
                v => { values[index] = v; Redraw(); });

        // The keys' timing and curves, for the action's track (its own, or the motion's default): each key's phase,
        // its moment through that phase, and the curve it eases in by. Moving a key moves the timeline.
        string motionPointer = $"/motions/{Escape(model.Motion)}";
        if (motion.Track(action.Id) is { } track)
        {
            string trackId = motion.Tracks.ContainsKey(action.Id) ? action.Id : HeldMotionCatalog.DefaultTrack;
            List<Tunable> timing = [];
            for (int i = 0; i < track.Length; i++)
            {
                int index = i;
                MotionKey k = track[i];
                string at = $"{motionPointer}/tracks/{Escape(trackId)}/{i}";
                timing.Add(new(new($"key.{i}.phase", $"{i} {k.Pose} phase", TunableKind.Choice, 0, Phases.Length - 1, 1, Array.IndexOf(Phases, k.Phase))
                    { Options = [.. Phases.Select(p => p.ToString())], Reshapes = true, Source = new(HeldMotionCatalog.Path, $"{at}/phase") },
                    v => { track[index] = track[index] with { Phase = Phases[(int)MathF.Round(v)] }; Redraw(); }));
                timing.Add(new(new($"key.{i}.at", $"{i} {k.Pose} at", TunableKind.Slider, 0, 1, .01f, k.At)
                    { Reshapes = true, Source = new(HeldMotionCatalog.Path, $"{at}/at") },
                    v => { track[index] = track[index] with { At = v }; Redraw(); }));
                foreach (var (side, label) in new[] { (0, "main"), (1, "off") })
                    timing.Add(new(new($"key.{i}.hands.{label}", $"{i} {k.Pose} {label} hand closed", TunableKind.Slider, -1, 1, .05f, k.Hands[side])
                        { Source = new(HeldMotionCatalog.Path, $"{at}/hands/{side}") },
                        v => { track[index].Hands[side] = v; Redraw(); }));
                timing.Add(new(new($"key.{i}.ease", $"{i} {k.Pose} ease", TunableKind.Choice, 0, Eases.Length - 1, 1, Array.IndexOf(Eases, k.Ease))
                    { Options = [.. Eases.Select(e => e.ToString())], Source = new(HeldMotionCatalog.Path, $"{at}/ease") },
                    v => { track[index] = track[index] with { Ease = Eases[(int)MathF.Round(v)] }; Redraw(); }));
            }
            groups.Add(new($"track.{model.Motion}.{trackId}", $"Keys · {model.Motion} · {trackId} (every look in it)", timing) { File = HeldMotionCatalog.Path });
        }

        if (key >= 0 && motion.Track(action.Id) is { } keys && key < keys.Length)
        {
            string name = keys[key].Pose;
            HeldPose pose = motion.Poses[name];
            string posePointer = $"{motionPointer}/poses/{Escape(name)}";
            // The viewer holds this key and reads its pose as it is now.
            void Show() => view.Publish();
            groups.Add(new($"pose.{model.Motion}.{name}", $"Pose · {name} ({model.Motion}, every look in it)",
            [
                .. Vector("pose.position", "move", pose.Position, -0.5f, 0.5f, .005f, HeldMotionCatalog.Path, $"{posePointer}/position", Show),
                .. Vector("pose.rotation", "turn", pose.Rotation, -180, 180, 1, HeldMotionCatalog.Path, $"{posePointer}/rotation", Show)
            ]) { File = HeldMotionCatalog.Path });
        }

        ArmsDefinition arms = held.Arms;
        string path = ArmsDefinition.Path;
        groups.Add(new("arms", "Arms",
        [
            .. Vector("arms.shoulder", "shoulder", arms.Shoulder, -0.6f, 0.6f, .005f, path, "/shoulder", Redraw),
            .. Vector("arms.pole", "elbow toward", arms.Pole, -2, 2, .01f, path, "/pole", Redraw),
            .. Vector("arms.main.palm", "main palm", arms.MainHand.Palm, -0.15f, 0.15f, .002f, path, "/mainHand/palm", Redraw),
            .. Vector("arms.off.rotation", "off hand", arms.OffHand.Rotation, -180, 180, 1, path, "/offHand/rotation", Redraw),
            .. Vector("arms.off.palm", "off palm", arms.OffHand.Palm, -0.15f, 0.15f, .002f, path, "/offHand/palm", Redraw),
            .. Vector("arms.offPole", "off elbow toward", arms.OffPole, -2, 2, .01f, path, "/offPole", Redraw)
        ]) { File = path });
        return groups;
    }

    private static readonly MotionPhase[] Phases = Enum.GetValues<MotionPhase>();
    // The named curves keys may use: those needing parameters (bezier, steps, spring) are not authored in held motion.
    private static readonly TweenEasingKind[] Eases = [.. Enum.GetValues<TweenEasingKind>()
        .Where(e => e is not (TweenEasingKind.CubicBezier or TweenEasingKind.Steps or TweenEasingKind.Spring))];

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

    // Three sliders over one authored point, applied in place and saved to its elements.
    private static IEnumerable<Tunable> Vector(string id, string label, float[] values, float min, float max, float step, string file, string pointer,
        Action applied)
    {
        string[] axes = ["x", "y", "z"];
        for (int i = 0; i < 3; i++)
        {
            int axis = i;
            yield return new(new($"{id}.{axes[i]}", $"{label} {axes[i]}", TunableKind.Slider, min, max, step, values[i])
                { Source = new(file, $"{pointer}/{i}") },
                v =>
                {
                    values[axis] = v;
                    applied();
                });
        }
    }

    // A JSON pointer reference token (RFC 6901).
    private static string Escape(string token) => token.Replace("~", "~0").Replace("/", "~1");
}
