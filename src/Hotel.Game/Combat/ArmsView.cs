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
    // Each hand's finger joints (a row per finger, base to tip) and thumb joints, for closing it on what it holds.
    private readonly (uint[][] Fingers, uint[] Thumb) mainDigits, offDigits;
    private AnimationInstance? instance;
    // The wrists' last targets (model space; the off one only while it holds), and how far the drawn arms fell short.
    private (Vector3 Main, Vector3? Off)? asked;

    /// <summary>
    /// How far each holding wrist was drawn from where it was asked to be (metres; the off hand's -1 when it holds
    /// nothing): more than a centimetre or so is a grip out of the arm's reach.
    /// </summary>
    internal (float Main, float Off) Shortfall { get; private set; } = (0, -1);

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
            mainDigits = Digits(joints, arms.Main, "main");
            offDigits = Digits(joints, arms.Off, "off");
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

    // A hand's digit joints: the chain's digit prefix, then each finger's name (or the thumb's) and 1 to 3.
    private (uint[][], uint[]) Digits(ReadOnlySpan<AnimationJointInfo> joints, ArmChain chain, string field)
    {
        string[] ids = [.. joints.ToArray().Select(j => j.Id)];
        uint[] Joints(string digit) => [.. Enumerable.Range(1, 3).Select(i =>
        {
            int at = Array.IndexOf(ids, $"{chain.Digits}{digit}{i}");
            return at >= 0 ? (uint)at
                : throw new InvalidDataException($"content/{ArmsDefinition.Path} {field}.digits: the arms model '{arms.Model}' has no joint '{chain.Digits}{digit}{i}'.");
        })];
        return ([.. arms.Hands.Fingers.Select(Joints)], Joints(arms.Hands.Thumb));
    }

    // A hand's closure as rotations added to the grip clip's finger and thumb joints: toward Close at 1, toward Open at -1.
    // Fingers curl about their joints' X (negative toward the palm); a thumb also swings about Z, mirrored on the left.
    private IEnumerable<JointOverride> Close((uint[][] Fingers, uint[] Thumb) digits, float closure, bool mirrored)
    {
        if (closure == 0) yield break;
        DigitBend bend = closure > 0 ? arms.Hands.Close : arms.Hands.Open;
        float weight = MathF.Abs(closure), side = mirrored ? -1 : 1;
        const float Radians = MathF.PI / 180;
        foreach (uint[] finger in digits.Fingers)
            for (int i = 0; i < 3; i++)
                yield return JointOverride.AddLocalRotation(finger[i], Quaternion.CreateFromAxisAngle(Vector3.UnitX, -bend.Fingers[i] * weight * Radians));
        for (int i = 0; i < 3; i++)
            yield return JointOverride.AddLocalRotation(digits.Thumb[i], Quaternion.Normalize(
                Quaternion.CreateFromAxisAngle(Vector3.UnitZ, bend.Thumb[i][1] * side * weight * Radians) *
                Quaternion.CreateFromAxisAngle(Vector3.UnitX, -bend.Thumb[i][0] * weight * Radians)));
    }

    /// <summary>The arms' fact for the combat snapshot: at the shoulder in camera space, shown while something is held.</summary>
    internal AppearanceFact Fact(bool shown) =>
        new(entity, false, 0, new(Authored.Vector(arms.Shoulder), Quaternion.Identity, Vector3.One), appearance, shown, RenderLayer.Viewmodel);

    /// <summary>
    /// Reaches the hands for the grips (camera space) once the arms are in the published snapshot: the off hand hangs
    /// at its rest when <paramref name="offGrip"/> is null.
    /// </summary>
    /// <param name="closure">How closed each hand is (main, off): -1 open, 0 the grip clip, 1 squeezed.</param>
    internal void Reach(Vector3 mainGrip, Vector3? offGrip, Quaternion item, float[]? lookHand = null, (float Main, float Off) closure = default)
    {
        if (instance is null)
        {
            instance = engine.Animation.CreateInstance(new(appearance, entity));
            // The hands hold by the grip clip; each closes or opens from it by joint overrides, and IK places the arms.
            engine.Animation.SetPlayback(new(instance, AnimationPlaybackKind.Play, arms.Clip, AnimationLoopMode.Repeat, 1, 1, true, 0, true, 0));
        }
        Vector3 shoulder = Authored.Vector(arms.Shoulder);
        // Each holding hand turns with the item, and its wrist sits so the palm, not the wrist, is on the grip.
        // A look may turn the hands its own way on it (its grips' hand); the palm stays where the arms' hand puts it.
        (Vector3 Wrist, Quaternion Turn) Hold(Vector3 grip, ArmHand hand) => Wrist(grip, item, hand, lookHand);
        var (mainWrist, mainTurn) = Hold(mainGrip, arms.MainHand);
        List<JointOverride> hands = [JointOverride.Orient(main.Hand, PoseSpace.Model, mainTurn)];
        Vector3 offWrist = Authored.Vector(arms.OffRest);
        if (offGrip is { } grip)
        {
            (offWrist, Quaternion offTurn) = Hold(grip, arms.OffHand);
            hands.Add(JointOverride.Orient(off.Hand, PoseSpace.Model, offTurn));
            hands.AddRange(Close(offDigits, closure.Off, arms.Off.Mirrored));
        }
        hands.AddRange(Close(mainDigits, closure.Main, arms.Main.Mirrored));
        // The arms drawn last time against where their wrists were asked to be then.
        if (asked is { } last && engine.Animation.ReadJointPose(instance) is { Reported: true } drawn)
        {
            ReadOnlySpan<JointPose> joints = drawn.Joints.Span;
            Shortfall = (Vector3.Distance(joints[(int)main.Hand].Model.Translation, last.Main),
                last.Off is { } offAsked ? Vector3.Distance(joints[(int)off.Hand].Model.Translation, offAsked) : -1);
        }
        asked = (mainWrist - shoulder, offGrip is null ? null : offWrist - shoulder);
        engine.Animation.SetPose(new(instance,
            new TwoBoneIk[]
            {
                new(main.Upper, main.Lower, main.Hand, PoseSpace.Model, mainWrist - shoulder, Authored.Vector(arms.Pole) - shoulder, 1),
                new(off.Upper, off.Lower, off.Hand, PoseSpace.Model, offWrist - shoulder, Authored.Vector(arms.OffPole) - shoulder, 1),
            },
            hands.ToArray(), true));
    }

    /// <summary>
    /// Where a hand's wrist goes to hold a grip (camera space), and how the hand turns: with the item (its axis frame),
    /// by the look's own hold or the hand's, with the palm, not the wrist, on the grip.
    /// </summary>
    internal static (Vector3 Wrist, Quaternion Turn) Wrist(Vector3 grip, Quaternion item, ArmHand hand, float[]? lookHand)
    {
        Quaternion turn = Quaternion.Normalize(item * (lookHand is null ? hand.Turn : (hand with { Rotation = lookHand }).Turn));
        return (grip - Vector3.Transform(Authored.Vector(hand.Palm), turn), turn);
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
/// camera space, the clip that poses the hands as they hold, how a hand closes or opens from it (<see cref="ArmDigits"/>),
/// each arm's joint chain and elbow pole (camera space: the elbow bends toward it), and where a free off hand hangs.
/// </summary>
internal sealed record ArmsDefinition(string Model, string Clip, ArmDigits Hands, ArmReach Reach, float[] Shoulder, float[] Pole, float[] OffPole, ArmChain Main, ArmChain Off, float[] OffRest,
    ArmHand MainHand, ArmHand OffHand)
{
    internal const string Path = "combat/arms.json";

    internal static ArmsDefinition Load(IEngineContext engine)
    {
        ArmsDefinition arms = Authored.Read(engine, Path, ContentJson.Default.ArmsDefinition);
        Authored.Require(arms.Model.EndsWith(".glb", StringComparison.Ordinal), Path, "model", "must be a GLB content path.");
        Authored.Require(!string.IsNullOrWhiteSpace(arms.Clip), Path, "clip", "names the clip that poses the hands as they hold.");
        Authored.Require(arms.Hands.Fingers.Length > 0, Path, "hands.fingers", "names the fingers that close.");
        foreach (var (field, bend) in new[] { ("close", arms.Hands.Close), ("open", arms.Hands.Open) })
        {
            Authored.Require(bend.Fingers.Length == 3, Path, $"hands.{field}.fingers", "gives three finger joints' curl, in degrees.");
            Authored.Require(bend.Thumb.Length == 3 && bend.Thumb.All(j => j.Length == 2), Path, $"hands.{field}.thumb",
                "gives three thumb joints' [curl, swing], in degrees.");
        }
        Authored.Positive(Path, "reach.length", arms.Reach.Length);
        Authored.Point(Path, "reach.offShoulder", arms.Reach.OffShoulder);
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

/// <summary>
/// An arm's joints from shoulder to hand, the prefix of its digit joints (<c>R_</c> names <c>R_Index1</c>), and whether
/// it is the mirrored side (a thumb swings the other way).
/// </summary>
internal sealed record ArmChain(string Upper, string Lower, string Hand, string Digits, bool Mirrored = false);

/// <summary>
/// The hands' digits (finger names and the thumb's, each with joints 1 to 3) and how far they bend from the grip clip
/// at full closure (<see cref="Close"/>, closure 1) and fully open (<see cref="Open"/>, closure -1); closure in
/// between scales them.
/// </summary>
internal sealed record ArmDigits(string[] Fingers, string Thumb, DigitBend Close, DigitBend Open);

/// <summary>Degrees added to each finger joint's curl (base to tip, the same for every finger) and each thumb joint's [curl, swing].</summary>
internal sealed record DigitBend(float[] Fingers, float[][] Thumb);

/// <summary>
/// How far the arms reach: <see cref="Length"/>, shoulder to wrist with the elbow straight (the upper arm and forearm
/// of the arms model's rig), and where the left shoulder is from the right in the model (<see cref="OffShoulder"/>).
/// A grip whose wrist lies further from its shoulder cannot be held.
/// </summary>
internal sealed record ArmReach(float Length, float[] OffShoulder);
