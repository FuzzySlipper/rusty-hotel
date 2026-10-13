using System.Numerics;
using Hotel.Game.Combat;
using Rusty.Engine;

// The hand fit curls digits onto a held model's own surface: straight fingers standing beside the pry bar's shaft,
// curling across it, stop on it (within a step of the gap) and do not pass into it; a hand far from the bar keeps
// the grip clip.
internal static class HandFitChecks
{
    internal static void Run(IEngineContext engine)
    {
        HeldCatalog held = Owners.Content(engine).Combat.Held;
        HeldModel prybar = held.Model("prybar");
        using ContentReference content = engine.Content.OpenReference(new(prybar.Model));
        using RenderResource model = engine.Animation.OpenAnimatedMeshFromContent(new(content));
        Transform placement = new(Vector3.Zero, Quaternion.Identity, new(prybar.Scale));
        Vector3 grip = new Vector3(prybar.Grips.Main[0], prybar.Grips.Main[1], prybar.Grips.Main[2]) * prybar.Scale;
        // The shaft runs along -Z; a hand turned so its fingers curl about the shaft (their X along it) curls toward -X.
        Quaternion turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
        DigitChain Finger(float along) => new([Quaternion.Identity, Quaternion.Identity, Quaternion.Identity],
            [new(along, 0.02f, 0), new(0, 0.035f, 0), new(0, 0.025f, 0)]);
        HandChains chains = new([Finger(-0.03f), Finger(-0.01f), Finger(0.01f), Finger(0.03f)], Finger(0));
        HandShape Shape() => new([.. Enumerable.Range(0, 4).Select(_ => new float[3])], [.. Enumerable.Range(0, 3).Select(_ => new float[2])]);

        // Beside the shaft: the wrist below and to +X of the grip, so the fingers rise past it and curl over it.
        HandShape beside = Shape();
        HandFitting.Hold near = new(model, placement, grip + new Vector3(0.03f, -0.045f, 0), turn, chains, false);
        List<string> fitted = HandFitting.Fit(engine, near, beside, held.Arms.Hands);
        Check(fitted.All(l => !l.Contains("meets nothing") && !l.Contains("into the model")), $"fingers beside the bar curl onto it without passing in:\n{string.Join("\n", fitted)}");
        Check(beside.Fingers.All(f => f.All(c => c > held.Arms.Hands.Fit.Open && c < held.Arms.Hands.Fit.Closed)), "each finger stops between open and closed");
        List<string> contact = HandFitting.Contact(engine, near, beside, held.Arms.Hands);
        Check(contact.All(l => !l.Contains("into the model")), $"the fitted hand reports no digit in the bar:\n{string.Join("\n", contact)}");

        HandShape far = Shape();
        List<string> away = HandFitting.Fit(engine, near with { Wrist = grip + new Vector3(0.5f, 0, 0) }, far, held.Arms.Hands);
        Check(away.All(l => l.Contains("meets nothing")) && far.Fingers.All(f => f.All(c => c == 0)), $"a hand far from the bar keeps the clip:\n{string.Join("\n", away)}");
        Console.WriteLine($"Hand fit checks passed: four fingers curl onto the pry bar's own surface ({string.Join(", ", beside.Fingers.Select(f => string.Join("/", f)))}) and none passes in; a far hand keeps the clip.");
    }

    private static void Check(bool value, string reason)
    {
        if (!value) throw new InvalidOperationException(reason);
    }
}
