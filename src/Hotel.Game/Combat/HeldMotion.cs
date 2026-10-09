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
    /// in camera space and a rotation in the model's own frame, as the Engine applies them.
    /// </summary>
    internal TweenSegment[] Timeline(HeldModel look, ActionDefinition action)
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
        (Vector3 Move, Quaternion Turn) from = (Vector3.Zero, Quaternion.Identity);
        float start = 0;
        void Ease((Vector3 Move, Quaternion Turn) to, float time, TweenEasingKind ease)
        {
            float seconds = time - start;
            if (seconds <= 0) return;
            segments.Add(TweenSegment.Move(from.Move, to.Move, seconds, ease).At(start));
            segments.Add(TweenSegment.Rotate(from.Turn, to.Turn, seconds, ease).At(start));
            from = to;
            start = time;
        }
        foreach (MotionKey key in motion.Track(action.Id)!) Ease(Offset(motion.Poses[key.Pose]), key.Time(action.Timing), key.Ease);
        // Every timeline ends at rest, where the model is published, when the action ends.
        Ease((Vector3.Zero, Quaternion.Identity), total, SettleEase);
        return segments.ToArray();
    }

    /// <summary>The settle back to rest from wherever a cut-short action left the model.</summary>
    internal TweenSegment[] Settle() =>
    [
        TweenSegment.Move(Vector3.Zero, Vector3.Zero, SettleSeconds, SettleEase),
        TweenSegment.Rotate(Quaternion.Identity, Quaternion.Identity, SettleSeconds, SettleEase)
    ];

    /// <summary>
    /// The pose an action shows at a moment, holding each key's pose from its time until the next (no curve): where a
    /// muzzle flash goes while a firearm commits.
    /// </summary>
    internal HeldPose PoseAt(HeldModel look, ActionDefinition action, float time)
    {
        HeldMotion motion = Motions[look.Motion];
        HeldPose pose = motion.Poses[Rest];
        foreach (MotionKey key in motion.Track(action.Id)!)
            if (key.Time(action.Timing) <= time + 1e-4f) pose = motion.Poses[key.Pose];
        return pose;
    }
}

/// <summary>One family of held motion: its poses and its tracks by action id (with a default for the rest).</summary>
internal sealed record HeldMotion(Dictionary<string, HeldPose> Poses, Dictionary<string, MotionKey[]> Tracks)
{
    internal MotionKey[]? Track(string action) =>
        Tracks.GetValueOrDefault(action) ?? Tracks.GetValueOrDefault(HeldMotionCatalog.DefaultTrack);
}

/// <summary>
/// Where the hand is, in camera space (metres right, up, back), turned by <see cref="Rotation"/> (degrees about X, Y,
/// Z). The held model sits in the hand by its look's offset and rotation.
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

    /// <summary>Where a look's model is and how it is turned with the hand in this pose.</summary>
    internal (Vector3 At, Quaternion Model) Place(HeldModel look)
    {
        Quaternion hand = Turn;
        return (Authored.Vector(Position) + Vector3.Transform(Authored.Vector(look.Offset), hand), Quaternion.Normalize(hand * look.Turn));
    }
}

/// <summary>A key: the hand reaches <see cref="Pose"/> at <see cref="At"/> (0 to 1) through <see cref="Phase"/>, easing in by <see cref="Ease"/>.</summary>
internal sealed record MotionKey(string Pose, MotionPhase Phase, float At, TweenEasingKind Ease)
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

/// <summary>Where hands hold a look's model, in the model's own space: the main hand's grip and, for two hands, the other's.</summary>
internal sealed record HeldGrips(float[] Main, float[]? Off = null);
