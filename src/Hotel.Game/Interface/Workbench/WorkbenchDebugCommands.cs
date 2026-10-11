using System.Globalization;
using System.Text.Json;
using Hotel.Game.Actions;
using Hotel.Game.Combat;
using Hotel.Game.Content;
using Rusty.Engine.Debugging;

namespace Hotel.Game.Interface.Workbench;

/// <summary>
/// The first-person workbench's product side, in the Engine console: describes the controls for the look and action
/// the motion viewer shows (<see cref="WorkbenchView"/>, contract <c>tunables.v1</c>), selects them, sets one control,
/// and lists what has changed since the content loaded as JSON to paste. The workbench panel in the UI renders the
/// description and sends these commands, and the motion viewer's own for playback; nothing is written to disk.
/// </summary>
internal sealed class WorkbenchDebugCommands : IDebugCommandModule
{
    private readonly Func<CombatView> view;
    private readonly HeldCatalog held;
    private readonly HeldMotionDebugCommands viewer;
    // Each look's actions, from the items that show it.
    private readonly IReadOnlyDictionary<string, string[]> uses;
    // The loaded content's values, as JSON, to tell what has changed.
    private readonly Dictionary<string, string> loaded;

    internal WorkbenchDebugCommands(Func<CombatView> view, HeldCatalog held, HeldMotionDebugCommands viewer, IReadOnlyDictionary<string, string[]> uses)
    {
        (this.view, this.held, this.viewer, this.uses) = (view, held, viewer, uses);
        loaded = Snapshot();
    }

    [DebugCommand("hotel.dev.workbench.describe", Description = "Developer workbench: describe the controls for the look and action the motion viewer shows (JSON, contract tunables.v1), choosing the held look's first action when none is shown.")]
    public DebugCommandResult Describe()
    {
        if (viewer.Viewing is not { Look: { } look, Action: { } action })
        {
            string shown = view().ShownLook is { } holding && Uses(holding).Length > 0 ? holding : uses.First(u => u.Value.Length > 0).Key;
            return Select(shown, "-");
        }
        return DebugCommandResult.Success(JsonSerializer.Serialize(View(look, action), WorkbenchJson.Default.WorkbenchView));
    }

    [DebugCommand("hotel.dev.workbench.select", Description = "Developer workbench: show a look (e.g. prybar) with one of its actions (e.g. prybar-swing; '-' for its first) and describe its controls.")]
    public DebugCommandResult Select(string look, string action)
    {
        if (!held.Looks.Any(l => l.Look == look)) return Failure($"Unknown look '{look}'.");
        if (Uses(look) is not [var first, ..]) return Failure($"No item shows '{look}', so it has no actions to play.");
        string chosen = Uses(look).Contains(action) ? action : first;
        DebugCommandResult shown = viewer.Show(look, chosen);
        return shown.Succeeded ? Describe() : shown;
    }

    [DebugCommand("hotel.dev.workbench.set", Description = "Developer workbench: set one control (an id from hotel.dev.workbench.describe) to a value, in the loaded content.")]
    public DebugCommandResult Set(string id, float value)
    {
        if (viewer.Viewing is not { Look: { } look, Action: { } action, Key: var key }) return Failure("Select a look first: hotel.dev.workbench.select <look> <action>.");
        Tunable? tunable = ViewmodelTunables.For(view(), held, look, action, key).SelectMany(g => g.Controls).FirstOrDefault(t => t.Control.Id == id);
        if (tunable is null) return Failure($"No control '{id}' for {look} · {action.Id}.");
        float clamped = Math.Clamp(value, tunable.Control.Min, tunable.Control.Max);
        tunable.Set(clamped);
        return DebugCommandResult.Success(clamped.ToString(CultureInfo.InvariantCulture));
    }

    [DebugCommand("hotel.dev.workbench.changes", Description = "Developer workbench: list what has changed since the content loaded, per content file, as JSON to paste (nothing is written to disk).")]
    public DebugCommandResult Changes()
    {
        Dictionary<string, string> now = Snapshot();
        Dictionary<string, string[]> files = now.Where(e => !loaded.TryGetValue(e.Key, out string? was) || was != e.Value)
            .GroupBy(e => e.Key.Split('|')[0])
            .ToDictionary(g => g.Key, g => g.Select(e => $"{e.Key.Split('|')[1]}: {e.Value}").ToArray());
        return DebugCommandResult.Success(JsonSerializer.Serialize(new WorkbenchChanges(files), WorkbenchJson.Default.WorkbenchChanges));
    }

    private WorkbenchView View(string look, ActionDefinition action)
    {
        HeldModel model = held.Model(look);
        HeldMotion motion = held.Motion.Motions[model.Motion];
        MotionKey[] keys = motion.Track(action.Id) ?? [];
        ActionTiming t = action.Timing;
        int key = viewer.Viewing.Key;
        return new("tunables.v1", "First-person workbench",
            [
                new("look", "Look", look, [.. held.Looks.Select(l => l.Look).Where(l => Uses(l).Length > 0)]),
                new("action", "Action", action.Id, Uses(look))
            ],
            [.. keys.Select((k, i) => new WorkbenchKey(i, k.Pose, k.Time(t)))], t.Windup + t.Commit + t.Recovery, key,
            [.. ViewmodelTunables.For(view(), held, look, action, key).Select(g => new WorkbenchGroup(g.Id, g.Title, [.. g.Controls.Select(c => c.Control)]))],
            new()
            {
                ["describe"] = "hotel.dev.workbench.describe",
                ["select"] = "hotel.dev.workbench.select {look} {action}",
                ["set"] = "hotel.dev.workbench.set {id} {value}",
                ["changes"] = "hotel.dev.workbench.changes",
                ["key"] = "hotel.dev.motion.key {key}",
                ["at"] = "hotel.dev.motion.at {seconds}",
                ["play"] = "hotel.dev.motion.play {speed}",
                ["close"] = "hotel.dev.motion.off"
            });
    }

    // The actions the items showing a look grant (a look no item shows has none to play).
    private string[] Uses(string look) => uses.GetValueOrDefault(look) ?? [];

    // Every tunable value, keyed "file|where", as the JSON it would be pasted as.
    private Dictionary<string, string> Snapshot()
    {
        Dictionary<string, string> values = [];
        foreach (var (id, stance) in held.Stances.Stances)
            values[$"{StanceCatalog.Path}|stances.{id}"] = JsonSerializer.Serialize(stance, ContentJson.Default.HeldStance);
        foreach (HeldModel look in held.Looks)
            values[$"{HeldCatalog.Path}|looks '{look.Look}' nudge"] = JsonSerializer.Serialize(look.Adjust, ContentJson.Default.HeldNudge);
        foreach (var (id, motion) in held.Motion.Motions)
            foreach (var (name, pose) in motion.Poses)
                values[$"{HeldMotionCatalog.Path}|motions.{id}.poses.{name}"] = JsonSerializer.Serialize(pose, ContentJson.Default.HeldPose);
        ArmsDefinition arms = held.Arms;
        values[$"{ArmsDefinition.Path}|shoulder"] = JsonSerializer.Serialize(arms.Shoulder, ContentJson.Default.SingleArray);
        values[$"{ArmsDefinition.Path}|pole"] = JsonSerializer.Serialize(arms.Pole, ContentJson.Default.SingleArray);
        values[$"{ArmsDefinition.Path}|offPole"] = JsonSerializer.Serialize(arms.OffPole, ContentJson.Default.SingleArray);
        values[$"{ArmsDefinition.Path}|mainHand"] = JsonSerializer.Serialize(arms.MainHand, ContentJson.Default.ArmHand);
        values[$"{ArmsDefinition.Path}|offHand"] = JsonSerializer.Serialize(arms.OffHand, ContentJson.Default.ArmHand);
        return values;
    }

    private static DebugCommandResult Failure(string message) => DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, message);
}
