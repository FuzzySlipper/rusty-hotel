using Hotel.Game.Combat;
using Rusty.Engine;

// The arms reach every grip they hold: for each held look, at rest and at every key pose of every action its items
// grant, each holding wrist lies within the arms' reach of its shoulder. A pose a metre out is caught.
internal static class HeldReachChecks
{
    // Slack for the IK's straightened elbow, in metres.
    private const float Tolerance = .005f;

    internal static void Run(IEngineContext engine)
    {
        var content = Owners.Content(engine);
        HeldCatalog held = content.Combat.Held;
        HeldModel gun = held.Model("coach-gun");
        var placement = held.Motion.Motions[gun.Motion].Poses[HeldMotionCatalog.Rest].Place(gun);
        float supportBefore = HeldGrip.Beyond(held, gun, placement).Off;
        float mainTurn = gun.Grips.Hand![2];
        gun.Grips.Hand[2] += 45;
        float supportAfter = HeldGrip.Beyond(held, gun, placement).Off;
        gun.Grips.Hand[2] = mainTurn;
        if (MathF.Abs(supportAfter - supportBefore) > 1e-6f)
            throw new InvalidOperationException("changing the firing hand's turn must not rotate the support hand");
        List<string> beyond = [];
        int poses = 0;
        foreach (var looked in content.Supplies.Items.Where(i => i.Wear?.Look is not null).GroupBy(i => i.Wear!.Look!))
        {
            HeldModel look = held.Model(looked.Key);
            HeldMotion motion = held.Motion.Motions[look.Motion];
            IEnumerable<string> used = looked.SelectMany(i => i.Wear!.Actions).Distinct()
                .SelectMany(a => motion.Track(a)?.Select(k => k.Pose) ?? []).Prepend(HeldMotionCatalog.Rest).Distinct();
            foreach (string pose in used)
            {
                var (main, off) = HeldGrip.Beyond(held, look, motion.Poses[pose].Place(look));
                poses++;
                if (main > Tolerance) beyond.Add($"{look.Look} {pose}: main wrist {main * 100:0.0} cm beyond reach");
                if (off > Tolerance) beyond.Add($"{look.Look} {pose}: off wrist {off * 100:0.0} cm beyond reach");
            }
        }
        if (beyond.Count > 0) throw new InvalidOperationException("Held grips beyond the arms' reach:\n" + string.Join("\n", beyond));
        HeldModel prybar = held.Model("prybar");
        var (far, _) = HeldGrip.Beyond(held, prybar, new HeldPose([0, 0, -1], [0, 0, 0]).Place(prybar));
        if (far < .3f) throw new InvalidOperationException($"a pose a metre out is beyond reach ({far:0.###} m)");
        Console.WriteLine($"Held reach checks passed: {poses} held poses within the arms' {held.Arms.Reach.Length} m reach; a pose a metre out is {far:0.00} m beyond.");
    }
}
