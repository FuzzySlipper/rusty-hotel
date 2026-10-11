using System.Text.Json;
using Hotel.Game.Actions;
using Hotel.Game.Combat;
using Hotel.Game.Content;
using Rusty.Engine;
using Rusty.Engine.Debugging;

namespace Hotel.Game.Interface;

/// <summary>
/// Developer motion viewer in the Engine console: show any held look in first person, step its action's keys (each
/// held exactly), play the action at any speed, and try pose changes live. Edits change only the loaded content; each
/// prints the pose's JSON to paste into content/combat/held-motion.json.
/// </summary>
/// <param name="view">The current floor's combat view; the product replaces it when the player changes floor.</param>
internal sealed class HeldMotionDebugCommands(Func<CombatView> view, HeldCatalog held, ActionCatalog actions) : IDebugCommandModule
{
    private string? look;
    private ActionDefinition? action;
    private int key = -1;

    private HeldMotion Motion => held.Motion.Motions[held.Model(look!).Motion];
    private MotionKey[] Keys => Motion.Track(action!.Id)!;

    [DebugCommand("hotel.dev.motion", Description = "Developer motion viewer: show a held look (e.g. prybar) in first person at rest, with one of its actions (e.g. prybar-swing), and list that action's keys.")]
    public DebugCommandResult Show(string heldLook, string actionId)
    {
        if (!held.Looks.Any(l => l.Look == heldLook))
            return Failure($"Unknown look '{heldLook}'. Looks: {string.Join(", ", held.Looks.Select(l => l.Look))}.");
        if (actions.Action(actionId) is not { } chosen) return Failure($"Unknown action '{actionId}'.");
        look = heldLook;
        action = chosen;
        if (Motion.Track(chosen.Id) is null) return Failure($"Motion '{held.Model(heldLook).Motion}' has no track for '{actionId}' and no default.");
        key = -1;
        view().View(look);
        return DebugCommandResult.Success(Describe());
    }

    [DebugCommand("hotel.dev.motion.key", Description = "Developer motion viewer: hold the shown look exactly in key n's pose (0 is the first key; -1 is rest).")]
    public DebugCommandResult Key(int n)
    {
        if (look is null) return Failure("Choose a look first: hotel.dev.motion <look> <action>.");
        if (n < -1 || n >= Keys.Length) return Failure($"Keys run from 0 to {Keys.Length - 1} (-1 is rest).");
        key = n;
        view().Hold(n < 0 ? null : Motion.Poses[Keys[n].Pose]);
        return DebugCommandResult.Success(n < 0 ? "rest" : Line(n));
    }

    [DebugCommand("hotel.dev.motion.next", Description = "Developer motion viewer: hold the next key's pose (after the last, rest).")]
    public DebugCommandResult Next() => look is null ? Key(-1) : Key(key + 1 >= Keys.Length ? -1 : key + 1);

    [DebugCommand("hotel.dev.motion.prev", Description = "Developer motion viewer: hold the previous key's pose (before the first, rest).")]
    public DebugCommandResult Previous() => look is null ? Key(-1) : Key(key - 1 < -1 ? Keys.Length - 1 : key - 1);

    [DebugCommand("hotel.dev.motion.play", Description = "Developer motion viewer: play the action's motion from rest at a speed (1 is as in play, 0.25 a quarter).")]
    public DebugCommandResult Play(float speed)
    {
        if (look is null) return Failure("Choose a look first: hotel.dev.motion <look> <action>.");
        if (!(speed > 0.01f && speed <= 4)) return Failure("Speed runs from 0.01 to 4.");
        key = -1;
        view().Play(action!, speed);
        float total = action!.Timing.Windup + action.Timing.Commit + action.Timing.Recovery;
        return DebugCommandResult.Success($"Playing {action.Id} on {look} over {total / speed:0.##} s.");
    }

    [DebugCommand("hotel.dev.motion.at", Description = "Developer motion viewer: show the action's motion paused at a moment, in seconds from its start (curves and all, as the Engine plays them).")]
    public DebugCommandResult At(float seconds)
    {
        if (look is null) return Failure("Choose a look first: hotel.dev.motion <look> <action>.");
        ActionTiming t = action!.Timing;
        float total = t.Windup + t.Commit + t.Recovery;
        if (!(seconds >= 0 && seconds <= total)) return Failure($"The action runs from 0 to {total:0.###} s.");
        key = -1;
        view().At(action, seconds);
        string phase = seconds < t.Windup ? "windup" : seconds < t.Windup + t.Commit ? "commit" : "recovery";
        return DebugCommandResult.Success($"{action.Id} at {seconds:0.###} s ({phase}; the strike is at {t.Windup:0.###} s).");
    }

    [DebugCommand("hotel.dev.motion.pose", Description = "Developer motion viewer: set a pose of the shown look's motion (position x y z in metres, rotation x y z in degrees) in the loaded content, hold it, and print its JSON.")]
    public DebugCommandResult Pose(string name, float x, float y, float z, float rx, float ry, float rz)
    {
        if (look is null) return Failure("Choose a look first: hotel.dev.motion <look> <action>.");
        if (!Motion.Poses.ContainsKey(name)) return Failure($"Motion '{held.Model(look).Motion}' has no pose '{name}'. Poses: {string.Join(", ", Motion.Poses.Keys)}.");
        HeldPose pose = new([x, y, z], [rx, ry, rz]);
        Motion.Poses[name] = pose;
        view().Hold(pose);
        return DebugCommandResult.Success($"\"{name}\": {JsonSerializer.Serialize(pose, ContentJson.Default.HeldPose)}  (in motions.{held.Model(look).Motion}.poses)");
    }

    [DebugCommand("hotel.dev.arms.hand", Description = "Developer arms tuning: set the main hand's turn from the held item (degrees x y z) and its palm offset from the wrist (metres x y z) in the loaded content, and print its JSON.")]
    public DebugCommandResult Hand(float rx, float ry, float rz, float px, float py, float pz)
    {
        ArmHand hand = view().Arms.MainHand;
        (hand.Rotation[0], hand.Rotation[1], hand.Rotation[2]) = (rx, ry, rz);
        (hand.Palm[0], hand.Palm[1], hand.Palm[2]) = (px, py, pz);
        view().Publish();
        return DebugCommandResult.Success($"\"mainHand\": {{\"rotation\": [{rx}, {ry}, {rz}], \"palm\": [{px}, {py}, {pz}]}}  (in {ArmsDefinition.Path})");
    }

    [DebugCommand("hotel.dev.arms.model", Description = "Developer arms comparison: show another arms model (a content path such as models/held/arms-knit.glb) with the same hold, until the floor is rebuilt.")]
    public DebugCommandResult ArmsModel(string model)
    {
        try { view().ShowArms(model); }
        catch (Exception error) when (error is EngineCallException or InvalidDataException)
        {
            return Failure($"Could not show '{model}': {error.Message}");
        }
        return DebugCommandResult.Success($"Showing arms '{model}'. content/{ArmsDefinition.Path} still names '{held.Arms.Model}'.");
    }

    [DebugCommand("hotel.dev.motion.off", Description = "Developer motion viewer: return the hands to what they hold.")]
    public DebugCommandResult Off()
    {
        look = null;
        action = null;
        view().View(null);
        return DebugCommandResult.Success("Motion viewer off.");
    }

    private string Describe()
    {
        ActionTiming t = action!.Timing;
        return $"{look} · {action.Id} (motion '{held.Model(look!).Motion}'; windup {t.Windup} s, commit {t.Commit} s, recovery {t.Recovery} s; the strike is at {t.Windup} s)\n" +
            string.Join("\n", Enumerable.Range(0, Keys.Length).Select(Line));
    }

    private string Line(int i)
    {
        MotionKey k = Keys[i];
        HeldPose pose = Motion.Poses[k.Pose];
        return $"{i}: {k.Pose} at {k.Phase} {k.At:0.##} = {k.Time(action!.Timing):0.###} s, {k.Ease}; position [{string.Join(", ", pose.Position)}] rotation [{string.Join(", ", pose.Rotation)}]";
    }

    private static DebugCommandResult Failure(string message) => DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, message);
}
