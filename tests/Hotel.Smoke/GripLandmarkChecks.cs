using System.Numerics;
using Hotel.Game.Combat;
using Hotel.Game.Content;
using Rusty.Engine;

// The grip check declares Engine landmark pairs; the Engine measures them on the drawn hands (the headless host draws
// nothing, so this checks what is declared). The survey pistol pairs its trigger, back strap top and butt with the main
// hand's index pad, web and pinky, its palm facing the handle; the coach gun adds the off palm on the fore-end; a hand on
// a look without landmarks pairs nothing.
internal static class GripLandmarkChecks
{
    internal static void Run(IEngineContext engine)
    {
        HeldCatalog held = Owners.Content(engine).Combat.Held;
        ArmsDefinition arms = held.Arms;
        HandLandmarks tuning = arms.Hands.Landmarks;
        using ContentReference content = engine.Content.OpenReference(new(arms.Model));
        using RenderResource model = engine.Animation.OpenAnimatedMeshFromContent(new(content));
        AnimationJointInfo[] joints = engine.Animation.ReadJoints(model).ToArray();
        string[] Joints(ArmChain chain, string digit) => [.. Enumerable.Range(1, 3).Select(i => $"{chain.Digits}{digit}{i}")];
        HandRig Rig(ArmChain chain, HandSide side, ulong entity) => HandRig.FromJoints(joints, entity, side, chain.Hand, Joints(chain, arms.Hands.Thumb),
            Joints(chain, tuning.Index), Joints(chain, tuning.Middle), Joints(chain, tuning.Ring), Joints(chain, tuning.Pinky));
        const ulong Arms = 7, Pistol = 11, Gun = 13;
        HandRig main = Rig(arms.Main, HandSide.Right, Arms), off = Rig(arms.Off, HandSide.Left, Arms);

        HeldModel pistol = held.Model("survey-pistol");
        LandmarkPair[] pairs = GripLandmarks.Pairs(pistol, Pistol, main, null, arms.Hands);
        Check(pairs.Select(p => p.Name).SequenceEqual(["trigger", "backstrap", "butt", "main-facing"]), $"the survey pistol's pairs: {string.Join(", ", pairs.Select(p => p.Name))}");
        LandmarkPair Find(LandmarkPair[] all, string name) => all.Single(p => p.Name == name);
        bool On(Landmark mark, ulong entity, float[] point) =>
            mark is { Kind: LandmarkKind.At, Space: OverlaySpace.Model } && mark.Entity == entity && mark.Point == Authored.Vector(point);
        LandmarkPair trigger = Find(pairs, "trigger"), backstrap = Find(pairs, "backstrap"), butt = Find(pairs, "butt"), facing = Find(pairs, "main-facing");
        Check(On(trigger.To, Pistol, pistol.Landmarks!.Trigger!) && trigger.From.Kind == LandmarkKind.Beyond && trigger.From.Entity == Arms
            && trigger.Tolerance == tuning.Trigger, "the index pad pairs with the trigger on the pistol's model, within the trigger tolerance");
        Check(On(backstrap.To, Pistol, pistol.Landmarks.BackstrapTop!) && backstrap.From.Kind == LandmarkKind.Between && backstrap.From.Amount == tuning.Web,
            "the web pairs with the back strap's top");
        Check(On(butt.To, Pistol, pistol.Landmarks.Butt!) && butt.Tolerance == tuning.Butt, "the pinky pairs with the butt");
        Vector3 handle = Vector3.Normalize(Authored.Vector(pistol.Landmarks.Butt!) - Authored.Vector(pistol.Landmarks.BackstrapTop!));
        Check(facing is { Aim: LandmarkAim.Line } && facing.From.Kind == LandmarkKind.Palm && facing.AngleTolerance == tuning.Facing
            && Vector3.Distance(facing.To.Axis, handle) < 1e-5f && On(facing.To, Pistol, pistol.Grips.Main), "the palm faces the handle's line through the main grip");

        HeldModel coach = held.Model("coach-gun");
        LandmarkPair[] two = GripLandmarks.Pairs(coach, Gun, main, off, arms.Hands);
        Check(coach.Landmarks?.ForeEnd is not null && Find(two, "fore-end").From.Kind == LandmarkKind.Palm && Find(two, "fore-end").From.Left
            && Find(two, "off-facing").Aim == LandmarkAim.Point, $"the coach gun pairs the off palm with its fore-end: {string.Join(", ", two.Select(p => p.Name))}");
        Check(!GripLandmarks.Pairs(coach, Gun, main, null, arms.Hands).Any(p => p.Name.StartsWith("off") || p.Name == "fore-end"), "an off hand that does not hold pairs nothing");
        HeldModel plain = held.Looks.First(l => l.Landmarks is null);
        Check(GripLandmarks.Pairs(plain, Gun, main, off, arms.Hands).Length == 0, $"{plain.Look} has no landmarks and pairs nothing");
        Console.WriteLine($"Grip landmark checks passed: the survey pistol declares {pairs.Length} Engine landmark pairs on its model and the main hand's rig, the coach gun {two.Length} with its fore-end, a look without landmarks none.");
    }

    private static void Check(bool value, string reason)
    {
        if (!value) throw new InvalidOperationException(reason);
    }
}
