using System.Numerics;
using Hotel.Game.Combat;
using Hotel.Game.Content;
using Rusty.Engine;

// The grip check measures a drawn hand against a look's landmarks: a hand whose index pad sits on the survey pistol's
// trigger and whose web sits on its back strap's top passes those; moved 3 cm up it fails both and says which way.
internal static class GripLandmarkChecks
{
    internal static void Run(IEngineContext engine)
    {
        HeldCatalog held = Owners.Content(engine).Combat.Held;
        HeldModel pistol = held.Model("survey-pistol");
        HandLandmarks tuning = held.Arms.Hands.Landmarks;
        Check(pistol.Landmarks is { Trigger: not null, BackstrapTop: not null, Butt: not null }, "the survey pistol names its trigger, back strap top and butt");
        (Vector3, Quaternion) placed = (Vector3.Zero, Quaternion.Identity);
        Vector3 At(float[] point) => Authored.Vector(point) * pistol.Scale;
        Vector3 trigger = At(pistol.Landmarks!.Trigger!), top = At(pistol.Landmarks.BackstrapTop!);
        // A drawn hand built backwards from where its points should land: the index's joints run straight to a pad on the
        // trigger, the thumb's first joint and the index's first put the web on the back strap's top.
        (Vector3 Wrist, Vector3[][] Digits) Hand(Vector3 shift)
        {
            Vector3 indexBase = top + new Vector3(0.01f, -0.01f, 0) + shift, step = (trigger + shift - indexBase) / (2 + held.Arms.Hands.Fit.Tip);
            Vector3[] index = [indexBase, indexBase + step, indexBase + step * 2];
            Vector3 thumbBase = (top + shift - indexBase * tuning.Web) / (1 - tuning.Web);
            Vector3 wrist = indexBase + new Vector3(0.02f, -0.06f, 0.06f);
            Vector3[] Finger(float down) => [index[0] + new Vector3(0, -down, 0), index[1] + new Vector3(0, -down, 0), index[2] + new Vector3(0, -down, 0)];
            Vector3[][] digits = [.. held.Arms.Hands.Fingers.Select((f, i) => f == tuning.Index ? index : Finger(0.02f * i)), [thumbBase, thumbBase, thumbBase]];
            return (wrist, digits);
        }
        List<GripLandmarks.Pairing> on = GripLandmarks.Measure(pistol, placed, held.Arms, Hand(Vector3.Zero), null);
        GripLandmarks.Pairing Find(List<GripLandmarks.Pairing> all, string name) => all.Single(p => p.Name == name);
        Check(Find(on, "trigger").Measure < 1e-4f && Find(on, "trigger").Within, $"an index pad on the trigger passes: {Find(on, "trigger").Text}");
        Check(Find(on, "backstrap").Measure < 1e-4f && Find(on, "backstrap").Within, $"a web on the back strap's top passes: {Find(on, "backstrap").Text}");
        List<GripLandmarks.Pairing> high = GripLandmarks.Measure(pistol, placed, held.Arms, Hand(new(0, 0.03f, 0)), null);
        Check(!Find(high, "trigger").Within && Find(high, "trigger").Text.Contains("30 above"), $"3 cm high fails and says so: {Find(high, "trigger").Text}");
        Check(!Find(high, "backstrap").Within, $"3 cm high misses the back strap: {Find(high, "backstrap").Text}");
        Check(on.Any(p => p.Facing) && GripLandmarks.Measure(pistol, placed, held.Arms, null, null).Count == 0, "a facing is measured, and nothing without a drawn hand");
        Console.WriteLine($"Grip landmark checks passed: an index pad on the survey pistol's trigger and a web on its back strap's top pass; 3 cm high fails both ({Find(high, "trigger").Measure * 1000:0} mm, {Find(high, "backstrap").Measure * 1000:0} mm).");
    }

    private static void Check(bool value, string reason)
    {
        if (!value) throw new InvalidOperationException(reason);
    }
}
