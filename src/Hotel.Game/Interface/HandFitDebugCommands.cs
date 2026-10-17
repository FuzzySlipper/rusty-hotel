using Hotel.Game.Combat;
using Rusty.Engine.Debugging;

namespace Hotel.Game.Interface;

/// <summary>
/// Developer hand fit in the Engine console: curl a look's hands onto its model from open (<see cref="HandFitting"/>)
/// as a measured start for tuning, or report how they meet it now. A fit changes only the loaded content; the
/// workbench's Hands group shows the values unsaved, to adjust and Save.
/// </summary>
/// <param name="view">The current floor's combat view; the product replaces it when the player changes floor.</param>
internal sealed class HandFitDebugCommands(Func<CombatView> view, HeldCatalog held) : IDebugCommandModule
{
    [DebugCommand("hotel.dev.hands.fit", Description = "Developer hand fit: curl a look's hands (main, off or both) onto its model at rest, joint by joint from open, and print the angles and how each digit meets it. Unsaved until the workbench saves it.")]
    public DebugCommandResult Fit(string look, string hands) => Run(look, hands, apply: true);

    [DebugCommand("hotel.dev.hands.contact", Description = "Developer hand fit: report how near a look's model each digit of its hands (main, off or both) comes at rest, and which pass into it; marks each probe in the overlay set hands.contact; then, for the shown look, how the drawn hands meet its grip landmarks (held.json landmarks), drawn as the overlay set held.landmarks.")]
    public DebugCommandResult Contact(string look, string hands) => Run(look, hands, apply: false);

    /// <summary>Fits or reports the hands of a look; the lines, or why it could not.</summary>
    internal static (bool Done, string Text) Hands(CombatView view, HeldCatalog held, string look, string hands, bool apply)
    {
        if (!held.Looks.Any(l => l.Look == look)) return (false, $"Unknown look '{look}'. Looks: {string.Join(", ", held.Looks.Select(l => l.Look))}.");
        bool[]? sides = hands switch { "main" => [false], "off" => [true], "both" => [false, true], _ => null };
        if (sides is null) return (false, "Hands are main, off or both.");
        List<string> lines = [];
        // Every digit the report measures is marked, one set for the hands it names (HeldInspection.ContactSet).
        List<Rusty.Engine.OverlayItem> marks = [];
        foreach (bool off in sides)
        {
            string side = off ? "off" : "main";
            if (off && held.Model(look).Grips.Off is null) { lines.Add($"{side}: {look} has no off grip."); continue; }
            if (view.FitHand(look, off, apply, marks) is not { } found)
                return (false, "The arms have not been drawn holding still yet: hold something, or show a look (hotel.dev.motion), then try again.");
            lines.Add($"{side} hand on {look}:");
            lines.AddRange(found.Select(l => "  " + l));
        }
        view.ShowContact(marks);
        // How the hold meets the look's landmarks, as the Engine measured them on the hands drawn now (the look must be
        // the one shown).
        if (held.Model(look).Landmarks is not null)
        {
            Rusty.Engine.LandmarkReadResult read = view.GripMeasures();
            if (view.ShownLook != look) lines.Add($"grip landmarks: show {look} (hotel.dev.motion) to measure them; {view.ShownLook ?? "nothing"} is shown.");
            else if (!read.Measured || read.Pairs.Length == 0) lines.Add("grip landmarks: not measured yet; the renderer has not drawn them.");
            else
            {
                lines.Add($"grip landmarks on {look}, as drawn at step {read.Step}:");
                foreach (Rusty.Engine.LandmarkMeasure pair in read.Pairs.Span) lines.Add("  " + GripLandmarks.Line(pair));
            }
        }
        return (true, string.Join("\n", lines));
    }

    private DebugCommandResult Run(string look, string hands, bool apply)
    {
        var (done, text) = Hands(view(), held, look, hands, apply);
        return done ? DebugCommandResult.Success(text) : DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, text);
    }
}
