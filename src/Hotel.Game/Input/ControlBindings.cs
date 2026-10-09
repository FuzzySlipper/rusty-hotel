using Hotel.Game.Content;
using Rusty.Engine;

namespace Hotel.Game.Input;

/// <summary>
/// The one binding table. Game controls name Engine keys and pointer buttons; screen controls name the browser
/// key code the DOM companion handles. Every label shown to the player, and every <c>{key.action}</c> in
/// authored text, comes from here.
/// </summary>
/// <param name="Primary">Acts with what the main hand holds: its first action.</param>
/// <param name="Secondary">Acts with the held item's second action (or what the other hand holds), and restores the checkpoint when downed.</param>
/// <param name="SwapHands">Trades what the two hands hold: the one slot-cycle control, in place of a key per weapon.</param>
internal sealed record ControlBindings(WalkBinding Walk, ControlBinding Run, ControlBinding Jump, ControlBinding Look, ControlBinding Primary, ControlBinding SwapHands,
    ControlBinding Secondary, ControlBinding[] QuickPockets, ControlBinding Summon, ControlBinding Use,
    ScreenBinding FieldCase, ScreenBinding Menu, ScreenBinding Console, ControlText Text, OpeningHint OpeningHint)
{
    internal const string Path = "input/bindings.json";

    internal static ControlBindings Load(IEngineContext engine)
    {
        ControlBindings bindings = Authored.Read(engine, Path, ContentJson.Default.ControlBindings);
        Authored.Require(bindings.Use.Keys is [_], Path, "use.keys", "the Engine FPS use control takes exactly one key.");
        Authored.Require(bindings.Run.Keys is [_], Path, "run.keys", "the Engine FPS sprint control takes exactly one key.");
        Authored.Require(bindings.Jump.Keys is [_], Path, "jump.keys", "the Engine FPS jump control takes exactly one key.");
        Template.Check(Path, "text.hint", bindings.Text.Hint, "key", "action");
        Authored.Within(Path, "openingHint.seconds", bindings.OpeningHint.Seconds, 0, float.MaxValue);
        Template.Plain(Path, ("walk.name", bindings.Walk.Name), ("walk.shortName", bindings.Walk.ShortName), ("walk.label", bindings.Walk.Label));
        foreach (var (field, control) in new[] { ("run", bindings.Run), ("jump", bindings.Jump), ("look", bindings.Look), ("primary", bindings.Primary), ("secondary", bindings.Secondary),
            ("swapHands", bindings.SwapHands),
            ("summon", bindings.Summon), ("use", bindings.Use) })
            Template.Plain(Path, ($"{field}.name", control.Name), ($"{field}.shortName", control.ShortName), ($"{field}.label", control.Label));
        foreach (var (field, screen) in new[] { ("fieldCase", bindings.FieldCase), ("menu", bindings.Menu), ("console", bindings.Console) })
            Template.Plain(Path, ($"{field}.name", screen.Name), ($"{field}.shortName", screen.ShortName), ($"{field}.label", screen.Label));
        for (int i = 0; i < bindings.QuickPockets.Length; i++) Template.Plain(Path, ($"quickPockets[{i}].label", bindings.QuickPockets[i].Label));
        Template.Check(Path, "text.quickPocketsName", bindings.Text.QuickPocketsName, "first", "last");
        Template.Check(Path, "text.quickPocketsNote", bindings.Text.QuickPocketsNote, "first", "last", "firstKey", "lastKey");
        foreach (string action in bindings.OpeningHint.Actions)
            Authored.Require(bindings.Labels.ContainsKey(action), Path, "openingHint.actions", $"unknown action '{action}'.");
        return bindings;
    }

    /// <summary>Action name to its key label, for <c>{key.action}</c> in authored text.</summary>
    internal IReadOnlyDictionary<string, string> Labels => new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["walk"] = Walk.Label, ["run"] = Run.Label, ["jump"] = Jump.Label, ["look"] = Look.Label, ["primary"] = Primary.Label, ["secondary"] = Secondary.Label, ["swapHands"] = SwapHands.Label,
        ["summon"] = Summon.Label, ["use"] = Use.Label, ["fieldCase"] = FieldCase.Label, ["menu"] = Menu.Label,
        ["console"] = Console.Label
    };

    internal string ShortName(string action) => action switch
    {
        "walk" => Walk.ShortName, "run" => Run.ShortName, "jump" => Jump.ShortName, "look" => Look.ShortName, "primary" => Primary.ShortName, "secondary" => Secondary.ShortName,
        "swapHands" => SwapHands.ShortName,
        "summon" => Summon.ShortName, "use" => Use.ShortName, "fieldCase" => FieldCase.ShortName, "menu" => Menu.ShortName,
        _ => Console.ShortName
    };
}

internal sealed record WalkBinding(string Name, string ShortName, string Label,
    KeyboardControl Forward, KeyboardControl Backward, KeyboardControl Left, KeyboardControl Right);

/// <param name="Name">The row on the Controls screen; empty for a slot (a quick pocket) named by its group.</param>
/// <param name="ShortName">The word an on-screen hint uses.</param>
internal sealed record ControlBinding(string Label, string Name = "", string ShortName = "",
    KeyboardControl[]? Keys = null, PointerButton[]? Pointer = null);

/// <param name="Code">The browser <c>KeyboardEvent.code</c> the DOM companion handles for this screen.</param>
internal sealed record ScreenBinding(string Name, string ShortName, string Label, string Code);

internal sealed record ControlText(string Hint, string QuickPocketsName, string QuickPocketsNote);

/// <summary>Controls reminded on screen when a session opens, for <see cref="Seconds"/> of admitted play.</summary>
internal sealed record OpeningHint(float Seconds, string[] Actions);
