using Hotel.Game.Combat;
using Rusty.Engine.Debugging;

namespace Hotel.Game.Interface.Workbench;

/// <summary>
/// The first-person workbench as an Engine tunables page: choosers for the look and the action the motion viewer
/// shows, the action's keys as the timeline (held, sought and played through the motion viewer's commands), and the
/// values from <see cref="ViewmodelTunables"/>. The Engine's <see cref="TunablesDebugModule"/> serves it, and its
/// panel renders it and saves each control where its source names.
/// </summary>
/// <param name="uses">Each look's actions, from the items that show it.</param>
internal sealed class HotelWorkbench(Func<CombatView> view, HeldCatalog held, HeldMotionDebugCommands viewer, IReadOnlyDictionary<string, string[]> uses)
{
    internal TunablesDebugModule Module() => new(Page, Choose);

    private TunablesPage Page()
    {
        if (viewer.Viewing is not { Look: { } look, Action: { } action, Key: var key })
        {
            // Nothing shown yet: the look the hands hold, or the first that has actions.
            string first = view().ShownLook is { } holding && Uses(holding).Length > 0 ? holding : uses.First(u => u.Value.Length > 0).Key;
            viewer.Show(first, Uses(first)[0]);
            return Page();
        }
        HeldModel model = held.Model(look);
        HeldMotion motion = held.Motion.Motions[model.Motion];
        MotionKey[] keys = motion.Track(action.Id) ?? [];
        Actions.ActionTiming t = action.Timing;
        return new("First-person workbench", ViewmodelTunables.For(view(), held, look, action, key))
        {
            Choosers =
            [
                new("look", "Look", look, [.. held.Looks.Select(l => l.Look).Where(l => Uses(l).Length > 0)]),
                new("action", "Action", action.Id, Uses(look))
            ],
            Timeline = new([.. keys.Select((k, i) => new TunableKey(i, k.Pose, k.Time(t)))], t.Windup + t.Commit + t.Recovery, key),
            Commands = new Dictionary<string, string>
            {
                ["key"] = "hotel.dev.motion.key {key}",
                ["at"] = "hotel.dev.motion.at {seconds}",
                ["play"] = "hotel.dev.motion.play {speed}"
            },
            Notice = ViewmodelTunables.Order(motion, action)
        };
    }

    // A chooser picks the look (with its first action) or the action; null when it is shown.
    private string? Choose(string chooser, string value)
    {
        string? look = viewer.Viewing.Look;
        switch (chooser)
        {
            case "look":
                if (Uses(value) is not [var first, ..]) return $"No item shows '{value}', so it has no actions to play.";
                return Refusal(viewer.Show(value, first));
            case "action":
                if (look is null || !Uses(look).Contains(value)) return $"'{value}' is not an action of {look}.";
                return Refusal(viewer.Show(look, value));
            default:
                return $"Unknown chooser '{chooser}'.";
        }
    }

    private static string? Refusal(DebugCommandResult result) => result.Succeeded ? null : result.Message;

    private string[] Uses(string look) => uses.GetValueOrDefault(look) ?? [];
}
