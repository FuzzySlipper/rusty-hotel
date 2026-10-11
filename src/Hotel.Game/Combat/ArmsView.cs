using System.Numerics;
using Hotel.Game.Content;
using Rusty.Engine;

namespace Hotel.Game.Combat;

/// <summary>
/// The investigator's arms in first person: one rigged model on the viewmodel layer with its right shoulder at
/// <see cref="ArmsDefinition.Shoulder"/>, whose hands reach the held item's grips by the Engine's two-bone IK each
/// update. Only the item moves by authored motion; the arms follow it, elbows bending toward a fixed pole. A hand the
/// item does not need (a one-handed item's off hand) hangs out of view.
/// </summary>
internal sealed class ArmsView : IDisposable
{
    private readonly IEngineContext engine;
    private readonly ArmsDefinition arms;
    private readonly RenderResource model;
    private readonly Appearance appearance;
    private readonly ulong entity;
    private readonly (uint Upper, uint Lower, uint Hand) main, off;
    private AnimationInstance? instance;

    internal ArmsView(IEngineContext engine, ArmsDefinition arms, ulong entity)
    {
        this.engine = engine; this.arms = arms; this.entity = entity;
        using ContentReference content = engine.Content.OpenReference(new(arms.Model));
        model = engine.Animation.OpenAnimatedMeshFromContent(new(content));
        try
        {
            ReadOnlySpan<AnimationJointInfo> joints = engine.Animation.ReadJoints(model).Span;
            string[] clips = engine.Animation.ReadClips(model).ToArray().Select(c => c.Id).ToArray();
            if (!clips.Contains(arms.Clip))
                throw new InvalidDataException($"content/{ArmsDefinition.Path} clip: the arms model '{arms.Model}' has no clip '{arms.Clip}'. Its clips: {string.Join(", ", clips)}.");
            main = Chain(joints, arms.Main, "main");
            off = Chain(joints, arms.Off, "off");
            appearance = engine.Animation.CreateAnimatedMeshAppearance(new(model));
        }
        catch { model.Dispose(); throw; }
    }

    private (uint, uint, uint) Chain(ReadOnlySpan<AnimationJointInfo> joints, ArmChain chain, string field)
    {
        uint Find(ReadOnlySpan<AnimationJointInfo> all, string name, string part)
        {
            for (int i = 0; i < all.Length; i++) if (all[i].Id == name) return (uint)i;
            throw new InvalidDataException($"content/{ArmsDefinition.Path} {field}.{part}: the arms model '{arms.Model}' has no joint '{name}'.");
        }
        return (Find(joints, chain.Upper, "upper"), Find(joints, chain.Lower, "lower"), Find(joints, chain.Hand, "hand"));
    }

    /// <summary>The arms' fact for the combat snapshot: at the shoulder in camera space, shown while something is held.</summary>
    internal AppearanceFact Fact(bool shown) =>
        new(entity, false, 0, new(Authored.Vector(arms.Shoulder), Quaternion.Identity, Vector3.One), appearance, shown, RenderLayer.Viewmodel);

    /// <summary>
    /// Reaches the hands for the grips (camera space) once the arms are in the published snapshot: the off hand hangs
    /// at its rest when <paramref name="offGrip"/> is null.
    /// </summary>
    internal void Reach(Vector3 mainGrip, Vector3? offGrip, Quaternion item)
    {
        if (instance is null)
        {
            instance = engine.Animation.CreateInstance(new(appearance, entity));
            // The hands' pose (how the fingers close) is the clip; IK places the arms over it.
            engine.Animation.SetPlayback(new(instance, AnimationPlaybackKind.Play, arms.Clip, AnimationLoopMode.Repeat, 1, 1, true, 0, true, 0));
        }
        Vector3 shoulder = Authored.Vector(arms.Shoulder);
        // Each holding hand turns with the item, and its wrist sits so the palm, not the wrist, is on the grip.
        (Vector3 Wrist, Quaternion Turn) Hold(Vector3 grip, ArmHand hand)
        {
            Quaternion turn = Quaternion.Normalize(item * hand.Turn);
            return (grip - Vector3.Transform(Authored.Vector(hand.Palm), turn), turn);
        }
        var (mainWrist, mainTurn) = Hold(mainGrip, arms.MainHand);
        List<JointOverride> hands = [JointOverride.Orient(main.Hand, PoseSpace.Model, mainTurn)];
        Vector3 offWrist = Authored.Vector(arms.OffRest);
        if (offGrip is { } grip)
        {
            (offWrist, Quaternion offTurn) = Hold(grip, arms.OffHand);
            hands.Add(JointOverride.Orient(off.Hand, PoseSpace.Model, offTurn));
        }
        engine.Animation.SetPose(new(instance,
            new TwoBoneIk[]
            {
                new(main.Upper, main.Lower, main.Hand, PoseSpace.Model, mainWrist - shoulder, Authored.Vector(arms.Pole) - shoulder, 1),
                new(off.Upper, off.Lower, off.Hand, PoseSpace.Model, offWrist - shoulder, Authored.Vector(arms.OffPole) - shoulder, 1),
            },
            hands.ToArray(), false));
    }

    public void Dispose()
    {
        instance?.Dispose();
        appearance.Dispose();
        model.Dispose();
    }
}

/// <summary>
/// The first-person arms: their model (facing -Z with the right shoulder at its origin), where that shoulder sits in
/// camera space, the clip that poses the hands, each arm's joint chain and elbow pole (camera space: the elbow bends
/// toward it), and where a free off hand hangs.
/// </summary>
internal sealed record ArmsDefinition(string Model, string Clip, float[] Shoulder, float[] Pole, float[] OffPole, ArmChain Main, ArmChain Off, float[] OffRest,
    ArmHand MainHand, ArmHand OffHand)
{
    internal const string Path = "combat/arms.json";

    internal static ArmsDefinition Load(IEngineContext engine)
    {
        ArmsDefinition arms = Authored.Read(engine, Path, ContentJson.Default.ArmsDefinition);
        Authored.Require(arms.Model.EndsWith(".glb", StringComparison.Ordinal), Path, "model", "must be a GLB content path.");
        Authored.Require(!string.IsNullOrWhiteSpace(arms.Clip), Path, "clip", "names the clip that poses the hands.");
        Authored.Point(Path, "shoulder", arms.Shoulder);
        Authored.Point(Path, "pole", arms.Pole);
        Authored.Point(Path, "offPole", arms.OffPole);
        Authored.Point(Path, "offRest", arms.OffRest);
        foreach (var (field, hand) in new[] { ("mainHand", arms.MainHand), ("offHand", arms.OffHand) })
        {
            Authored.Point(Path, $"{field}.rotation", hand.Rotation);
            Authored.Point(Path, $"{field}.palm", hand.Palm);
        }
        return arms;
    }
}

/// <summary>
/// How a hand holds: turned by <see cref="Rotation"/> (degrees about X, Y, Z) from the held item's own turn, and with
/// its palm <see cref="Palm"/> from the wrist (in that turned frame, metres), so the palm, not the wrist, is on the grip.
/// </summary>
internal sealed record ArmHand(float[] Rotation, float[] Palm)
{
    internal Quaternion Turn
    {
        get
        {
            Vector3 d = Authored.Vector(Rotation) * (MathF.PI / 180);
            return Quaternion.CreateFromYawPitchRoll(d.Y, d.X, d.Z);
        }
    }
}

/// <summary>An arm's joints from shoulder to hand.</summary>
internal sealed record ArmChain(string Upper, string Lower, string Hand);
