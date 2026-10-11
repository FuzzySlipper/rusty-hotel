using System.Text.Json;
using Hotel.Game.Combat;
using Hotel.Game.Content;
using Rusty.Engine.Debugging;

namespace Hotel.Game.Interface;

/// <summary>
/// Developer stance guide in the Engine console: draw where the shown look's stance puts it (amber) beside where it
/// is (blue), try stance values (every look in the stance follows), and nudge one look from its stance by eye. Edits
/// change only the loaded content and print the JSON to paste into content/combat/stances.json or held.json.
/// </summary>
/// <param name="view">The current floor's combat view; the product replaces it when the player changes floor.</param>
internal sealed class HeldStanceDebugCommands(Func<CombatView> view, HeldCatalog held) : IDebugCommandModule
{
    [DebugCommand("hotel.dev.stance", Description = "Developer stance guide: show (true) or hide (false) where the shown look's stance puts its grip, length and up side (amber) beside where they are (blue).")]
    public DebugCommandResult Guide(bool on)
    {
        view().ShowGuide(on);
        if (!on) return DebugCommandResult.Success("Stance guide off.");
        return view().ShownLook is { } look
            ? DebugCommandResult.Success($"Stance guide on: {look} in stance '{held.Model(look).Stance}'.")
            : DebugCommandResult.Success("Stance guide on; it shows while the hands hold something (or hotel.dev.motion shows a look).");
    }

    [DebugCommand("hotel.dev.held.nudge", Description = "Developer stance guide: move the shown look's grip from its stance (camera-space metres x y z) and turn it further about that grip (degrees about camera x y z), in the loaded content, and print the nudge for held.json.")]
    public DebugCommandResult Nudge(float x, float y, float z, float rx, float ry, float rz)
    {
        if (view().ShownLook is not { } look) return Failure("Hold something, or show a look with hotel.dev.motion, first.");
        HeldModel model = held.Model(look);
        (model.Adjust.Grip[0], model.Adjust.Grip[1], model.Adjust.Grip[2]) = (x, y, z);
        (model.Adjust.Turn[0], model.Adjust.Turn[1], model.Adjust.Turn[2]) = (rx, ry, rz);
        held.Seat(model);
        view().Publish();
        return DebugCommandResult.Success($"\"nudge\": {JsonSerializer.Serialize(model.Adjust, ContentJson.Default.HeldNudge)}  (in {HeldCatalog.Path}, look '{look}')");
    }

    [DebugCommand("hotel.dev.stance.set", Description = "Developer stance guide: set a stance's grip (x y z), the point its length heads toward (x y z) and its up side (x y z), camera space, in the loaded content, and print its JSON for stances.json.")]
    public DebugCommandResult Set(string stance, float gx, float gy, float gz, float tx, float ty, float tz, float ux, float uy, float uz)
    {
        if (!held.Stances.Stances.ContainsKey(stance))
            return Failure($"Unknown stance '{stance}'. Stances: {string.Join(", ", held.Stances.Stances.Keys)}.");
        HeldStance next = held.Stances.Stances[stance] with { Grip = [gx, gy, gz], Toward = [tx, ty, tz], Up = [ux, uy, uz] };
        held.Stances.Stances[stance] = next;
        foreach (HeldModel look in held.Looks.Where(l => l.Stance == stance)) held.Seat(look);
        view().Publish();
        return DebugCommandResult.Success($"\"{stance}\": {JsonSerializer.Serialize(next, ContentJson.Default.HeldStance)}  (in {StanceCatalog.Path})");
    }

    [DebugCommand("hotel.dev.stance.hand", Description = "Developer stance guide: set how hands hold things in a stance (degrees x y z in the item's axis frame) in the loaded content, and print its JSON for stances.json.")]
    public DebugCommandResult Hand(string stance, float rx, float ry, float rz)
    {
        if (!held.Stances.Stances.TryGetValue(stance, out HeldStance? current))
            return Failure($"Unknown stance '{stance}'. Stances: {string.Join(", ", held.Stances.Stances.Keys)}.");
        held.Stances.Stances[stance] = current with { Hand = [rx, ry, rz] };
        view().Publish();
        return DebugCommandResult.Success($"\"{stance}\": {JsonSerializer.Serialize(held.Stances.Stances[stance], ContentJson.Default.HeldStance)}  (in {StanceCatalog.Path})");
    }

    private static DebugCommandResult Failure(string message) => DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, message);
}
