using Hotel.Game.Combat;
using Hotel.Game.Content;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using Rusty.Engine.Input;

namespace Hotel.Game.Input;

/// <summary>Reads the authored bindings for input, playtest actions, the Controls screen and the opening hint.</summary>
internal sealed class HotelControls(ControlBindings bindings, WeaponDefinition[] weapons)
{
    // How long an Engine playtest action holds its control: a tap, or a short walk.
    private const double TapMilliseconds = 60, WalkMilliseconds = 200;
    private float openingRemaining = bindings.OpeningHint.Seconds;

    internal ControlBindings Bindings => bindings;

    internal static bool Pressed(PhysicalInputState physical, ControlBinding binding) =>
        (binding.Keys ?? []).Any(physical.Pressed) || (binding.Pointer ?? []).Any(physical.Pressed);

    internal static FpsInputBindings Fps(ControlBindings bindings, FpsInputBindings standard) => standard with
    {
        ForwardKey = bindings.Walk.Forward, BackwardKey = bindings.Walk.Backward,
        LeftKey = bindings.Walk.Left, RightKey = bindings.Walk.Right, UseKey = bindings.Use.Keys![0]
    };

    internal void Step(float admittedSeconds) => openingRemaining = Math.Max(0, openingRemaining - admittedSeconds);

    /// <summary>The opening reminder while it lasts; afterwards controls are hinted only where they apply.</summary>
    internal string[] Hint => openingRemaining > 0
        ? bindings.OpeningHint.Actions.Select(action => Template.Fill(bindings.Text.Hint,
            ("key", bindings.Labels[action]), ("action", bindings.ShortName(action)))).ToArray()
        : [];

    /// <summary>Every row of the Controls screen, in reading order.</summary>
    internal (string Name, string Label)[] Rows =>
    [
        (bindings.Walk.Name, bindings.Walk.Label), (bindings.Look.Name, bindings.Look.Label),
        (bindings.Attack.Name, bindings.Attack.Label),
        (string.Join(" / ", weapons.Select(w => w.Name)), string.Join(" / ", bindings.Weapons.Select(w => w.Label))),
        (bindings.Reload.Name, bindings.Reload.Label),
        (Template.Fill(bindings.Text.QuickPocketsName, ("first", Pocket(0)), ("last", Pocket(bindings.QuickPockets.Length - 1))),
            string.Join(" / ", bindings.QuickPockets.Select(q => q.Label))),
        (bindings.Summon.Name, bindings.Summon.Label), (bindings.Use.Name, bindings.Use.Label),
        (bindings.FieldCase.Name, bindings.FieldCase.Label), (bindings.Menu.Name, bindings.Menu.Label)
    ];

    internal string QuickPocketsNote => Template.Fill(bindings.Text.QuickPocketsNote,
        ("first", Pocket(0)), ("last", Pocket(bindings.QuickPockets.Length - 1)),
        ("firstKey", bindings.QuickPockets[0].Label), ("lastKey", bindings.QuickPockets[^1].Label));

    /// <summary>Engine playtest actions: the walk directions, each game control and each weapon by id.</summary>
    internal string[] ActionIds => ["forward", "back", "left", "right", "use", "attack", .. weapons.Select(w => w.Id), "reload", "summon"];

    internal PlaytestAction Action(string id)
    {
        int weapon = Array.FindIndex(weapons, w => w.Id == id);
        (KeyboardControl Key, double Ms)? control = id switch
        {
            "forward" => (bindings.Walk.Forward, WalkMilliseconds), "back" => (bindings.Walk.Backward, WalkMilliseconds),
            "left" => (bindings.Walk.Left, WalkMilliseconds), "right" => (bindings.Walk.Right, WalkMilliseconds),
            "use" => First(bindings.Use), "attack" => First(bindings.Attack), "reload" => First(bindings.Reload),
            "summon" => First(bindings.Summon),
            _ => weapon >= 0 ? First(bindings.Weapons[weapon]) : null
        };
        return control is { } found ? new(id, found.Key.ToString(), found.Ms, true) : new(id, "", 0, false, false, "Unknown hotel action");
    }

    private static (KeyboardControl, double)? First(ControlBinding binding) =>
        binding.Keys is [var key, ..] ? (key, TapMilliseconds) : null;
    private static string Pocket(int index) => (index + 1).ToString("00", System.Globalization.CultureInfo.InvariantCulture);
}
