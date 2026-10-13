using System.Numerics;
using System.Text.Json.Serialization;
using Hotel.Game.Actions;
using Hotel.Game.Content;
using Rusty.Engine;

namespace Hotel.Game.Combat;

/// <summary>
/// How held items move through their actions: named hand poses and, per action, keys that ease the hand from pose to
/// pose at moments of the action's phases. The Engine plays each action's keys as one tween timeline over the held
/// model (its curves, at the display's rate, on world time); the product starts it when the action starts and settles
/// it back to rest when the action is cut short.
/// </summary>
/// <param name="SettleSeconds">How long a cut-short action takes to ease back to rest.</param>
/// <param name="SettleEase">The curve it eases back on.</param>
internal sealed record HeldMotionCatalog(float SettleSeconds, TweenEasingKind SettleEase, Dictionary<string, HeldMotion> Motions)
{
    internal const string Path = "combat/held-motion.json";
    /// <summary>Every motion has a pose of this name: where the hand rests between actions.</summary>
    internal const string Rest = "rest";
    /// <summary>A motion's track for actions with no track of their own.</summary>
    internal const string DefaultTrack = "default";

    internal static HeldMotionCatalog Load(IEngineContext engine)
    {
        HeldMotionCatalog catalog = Authored.Read(engine, Path, ContentJson.Default.HeldMotionCatalog);
        Authored.Positive(Path, "settleSeconds", catalog.SettleSeconds);
        Ease(Path, "settleEase", catalog.SettleEase);
        foreach (var (id, motion) in catalog.Motions)
        {
            string at = $"motions.{id}";
            Authored.Require(motion.Poses.ContainsKey(Rest), Path, $"{at}.poses", $"needs a '{Rest}' pose.");
            Authored.Require(motion.Poses.TryGetValue(Rest, out HeldPose? rest) && rest.Position.All(v => v == 0) && rest.Rotation.All(v => v == 0),
                Path, $"{at}.poses.{Rest}", "must be no change ([0, 0, 0] both): a look rests where its stance seats it.");
            foreach (var (name, pose) in motion.Poses)
            {
                Authored.Point(Path, $"{at}.poses.{name}.position", pose.Position);
                Authored.Point(Path, $"{at}.poses.{name}.rotation", pose.Rotation);
            }
            Authored.Require(motion.Tracks.Count > 0, Path, $"{at}.tracks", "needs at least one track.");
            foreach (var (track, keys) in motion.Tracks)
                for (int i = 0; i < keys.Length; i++)
                {
                    string key = $"{at}.tracks.{track}[{i}]";
                    Authored.Require(motion.Poses.ContainsKey(keys[i].Pose), Path, $"{key}.pose", $"unknown pose '{keys[i].Pose}'.");
                    Authored.Within(Path, $"{key}.at", keys[i].At, 0, 1);
                    Authored.Require(keys[i].Hands is [var main, var off] && main is >= -1 and <= 1 && off is >= -1 and <= 1, Path, $"{key}.hands",
                        "gives the main and off hands' closure, each from -1 (open) through 0 (the grip) to 1 (squeezed).");
                    Ease(Path, $"{key}.ease", keys[i].Ease);
                }
        }
        return catalog;
    }

    // Curves that need parameters (bezier, steps, spring) are not authored here.
    private static void Ease(string path, string field, TweenEasingKind ease) =>
        Authored.Require(ease is not (TweenEasingKind.CubicBezier or TweenEasingKind.Steps or TweenEasingKind.Spring), path, field,
            "must be a named curve (linear, or quad, cubic, quart, quint, sine, expo, circ, back, elastic or bounce in, out or in-out).");

    /// <summary>Checks every look's motion and, for every action its items grant, that the keys fall in time order.</summary>
    internal void Validate(string path, HeldModel[] looks, IEnumerable<(string Look, ActionDefinition Action)> uses)
    {
        for (int i = 0; i < looks.Length; i++)
        {
            Authored.Require(Motions.ContainsKey(looks[i].Motion), path, $"looks[{i}].motion", $"unknown motion '{looks[i].Motion}' in {Path}.");
            Authored.Point(path, $"looks[{i}].grips.main", looks[i].Grips.Main);
            if (looks[i].Grips.Off is { } off) Authored.Point(path, $"looks[{i}].grips.off", off);
        }
        foreach (var (look, action) in uses)
        {
            string id = looks.Single(l => l.Look == look).Motion;
            HeldMotion motion = Motions[id];
            Authored.Require(motion.Track(action.Id) is not null, Path, $"motions.{id}.tracks",
                $"has no track for '{action.Id}' and no '{DefaultTrack}' track.");
            float[] times = motion.Track(action.Id)!.Select(k => k.Time(action.Timing)).ToArray();
            for (int i = 1; i < times.Length; i++)
                Authored.Require(times[i] > times[i - 1], Path, $"motions.{id}.tracks",
                    $"keys for '{action.Id}' must fall in time order through its phases ({times[i - 1]:0.###} s then {times[i]:0.###} s).");
        }
    }

    /// <summary>
    /// The tween timeline that moves a held model through an action: from rest, through each key's pose by its curve,
    /// back to rest by the end of recovery. Values are offsets from where the model is published at rest: a translation
    /// in camera space and a rotation in the model's own frame, as the Engine applies them. <paramref name="speed"/>
    /// plays it faster or slower (a viewer's slow motion). An action already part way along is started that far in
    /// (<see cref="TweenStartRequest"/>'s elapsed seconds), so every key lands at its moment of the action. It begins at
    /// <paramref name="from"/>, the offset shown when it starts (none, at rest), so it carries on without a jump.
    /// </summary>
    internal TweenSegment[] Timeline(HeldModel look, ActionDefinition action, float speed = 1, (Vector3 Move, Quaternion Turn)? from = null)
    {
        HeldMotion motion = Motions[look.Motion];
        HeldPose rest = motion.Poses[Rest];
        (Vector3 restAt, Quaternion restModel) = rest.Place(look);
        (Vector3 Move, Quaternion Turn) Offset(HeldPose pose)
        {
            (Vector3 at, Quaternion model) = pose.Place(look);
            return (at - restAt, Quaternion.Normalize(Quaternion.Inverse(restModel) * model));
        }
        float total = action.Timing.Windup + action.Timing.Commit + action.Timing.Recovery;
        List<TweenSegment> segments = [];
        (Vector3 Move, Quaternion Turn) last = from ?? (Vector3.Zero, Quaternion.Identity);
        float start = 0;
        void Ease((Vector3 Move, Quaternion Turn) to, float time, TweenEasingKind ease)
        {
            if (time - start <= 0) return;
            float at = start / speed, seconds = (time - start) / speed;
            segments.Add(TweenSegment.Move(last.Move, to.Move, seconds, ease).At(at));
            segments.Add(TweenSegment.Rotate(last.Turn, to.Turn, seconds, ease).At(at));
            last = to;
            start = time;
        }
        foreach (MotionKey key in motion.Track(action.Id)!) Ease(Offset(motion.Poses[key.Pose]), key.Time(action.Timing), key.Ease);
        // Every timeline ends at rest, where the model is published, when the action ends.
        Ease((Vector3.Zero, Quaternion.Identity), total, SettleEase);
        return segments.ToArray();
    }

    /// <summary>
    /// How closed each hand is <paramref name="time"/> seconds into an action on a look (main, off; -1 open, 0 the grip,
    /// 1 squeezed): from the grip at the start, through each key's closure eased by the key's curve
    /// (<paramref name="ease"/> evaluates it, as the Engine's tweens do), back to the grip by the end of recovery.
    /// </summary>
    internal (float Main, float Off) Hands(HeldModel look, ActionDefinition action, float time, Func<TweenEasingKind, float, float> ease)
    {
        MotionKey[] keys = Motions[look.Motion].Track(action.Id) ?? [];
        float total = action.Timing.Windup + action.Timing.Commit + action.Timing.Recovery;
        (float Main, float Off) from = (0, 0);
        float start = 0;
        foreach ((float at, (float, float) hands, TweenEasingKind curve) in keys.Select(k => (k.Time(action.Timing), (k.Hands[0], k.Hands[1]), k.Ease))
            .Append((total, (0f, 0f), SettleEase)))
        {
            if (time <= at)
            {
                float t = at - start <= 0 ? 1 : ease(curve, Math.Clamp((time - start) / (at - start), 0, 1));
                return (from.Main + (hands.Item1 - from.Main) * t, from.Off + (hands.Item2 - from.Off) * t);
            }
            (from, start) = (hands, at);
        }
        return (0, 0);
    }

    /// <summary>The settle back to rest from <paramref name="from"/>, the offset a cut-short action left the model at.</summary>
    internal TweenSegment[] Settle((Vector3 Move, Quaternion Turn) from) =>
    [
        TweenSegment.Move(from.Move, Vector3.Zero, SettleSeconds, SettleEase),
        TweenSegment.Rotate(from.Turn, Quaternion.Identity, SettleSeconds, SettleEase)
    ];
}

/// <summary>One family of held motion: its poses and its tracks by action id (with a default for the rest).</summary>
internal sealed record HeldMotion(Dictionary<string, HeldPose> Poses, Dictionary<string, MotionKey[]> Tracks)
{
    internal MotionKey[]? Track(string action) =>
        Tracks.GetValueOrDefault(action) ?? Tracks.GetValueOrDefault(HeldMotionCatalog.DefaultTrack);
}

/// <summary>
/// A pose of a held look, from where its stance seats it: its main grip moved by <see cref="Position"/> (camera space,
/// metres right, up, back) and the item turned further by <see cref="Rotation"/> (degrees about camera X, Y, Z) about
/// that grip. The rest pose is no change: the look as its stance holds it.
/// </summary>
internal sealed record HeldPose(float[] Position, float[] Rotation)
{
    internal Quaternion Turn
    {
        get
        {
            Vector3 degrees = Authored.Vector(Rotation) * (MathF.PI / 180);
            return Quaternion.CreateFromYawPitchRoll(degrees.Y, degrees.X, degrees.Z);
        }
    }

    /// <summary>Where a look's model is and how it is turned in this pose (camera space).</summary>
    internal (Vector3 At, Quaternion Model) Place(HeldModel look)
    {
        Quaternion turn = Quaternion.Normalize(Turn * look.Seated.Turn);
        Vector3 grip = look.Seated.Grip + Authored.Vector(Position);
        return (grip - Vector3.Transform(Authored.Vector(look.Grips.Main) * look.Scale, turn), turn);
    }
}

/// <summary>
/// A key: the look reaches <see cref="Pose"/> at <see cref="At"/> (0 to 1) through <see cref="Phase"/>, easing in by
/// <see cref="Ease"/>, with the hands closed to <see cref="Hands"/> (main, off: -1 open, 0 the grip, 1 squeezed).
/// </summary>
internal sealed record MotionKey(string Pose, MotionPhase Phase, float At, TweenEasingKind Ease, float[] Hands)
{
    /// <summary>Seconds from the action's start: windup, then commit (whose start is the strike), then recovery.</summary>
    internal float Time(ActionTiming timing) => Phase switch
    {
        MotionPhase.Windup => timing.Windup * At,
        MotionPhase.Commit => timing.Windup + timing.Commit * At,
        _ => timing.Windup + timing.Commit + timing.Recovery * At
    };
}

/// <summary>The action phase a key is timed through.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MotionPhase>))]
internal enum MotionPhase { Windup, Commit, Recovery }

/// <summary>
/// Where hands hold a look's model, in the model's own space: the main hand's grip and, for two hands, the other's; and
/// how the hands turn on it (<see cref="Hand"/>, degrees about X, Y, Z from the item's turn), when this look is not held as
/// the arms' default hand holds (<see cref="ArmsDefinition.MainHand"/>).
/// </summary>
internal sealed record HeldGrips(float[] Main, float[]? Off = null, float[]? Hand = null);
