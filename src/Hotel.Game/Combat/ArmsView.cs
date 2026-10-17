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
    // Whether the off hand held a grip when the arms were last posed.
    private bool offHeld;
    // Each joint's parent, the turns added to the digits last call, and for how many calls they have not changed: a
    // drawn frame can lag several posings, so the grip clip's digit chains are read from the drawn arms only once the
    // turns have held long enough that the frame was drawn with them.
    private readonly uint[] parents;
    private Dictionary<uint, Quaternion> sent = [];
    private int steady;
    private const int SteadyCalls = 8;
    private HandChains? mainChains, offChains;
    // Each hand's rig joints, named once for the Engine's grip landmarks (GripLandmarks).
    private readonly HandRig mainRig, offRig;
    private Vector3[][]? mainDrawn, offDrawn;
    // The wrist targets (camera space) the solve placed the drawn digits for.
    private (Vector3 Main, Vector3 Off) drawnFor;

    /// <summary>A hand's digits as the grip clip draws them (null until the arms have been drawn holding still).</summary>
    internal HandChains? Chains(bool offHand) => offHand ? offChains : mainChains;

    /// <summary>
    /// Where a hand's digit joints were last drawn (camera space; a row per finger, then the thumb), to check the fit's
    /// posing against; null unless the drawn frame's solve reached for <paramref name="wrist"/>, so a draw of another pose
    /// is never compared.
    /// </summary>
    internal Vector3[][]? Drawn(bool offHand, Vector3 wrist) =>
        Vector3.Distance(offHand ? drawnFor.Off : drawnFor.Main, wrist) < 1e-4f ? offHand ? offDrawn : mainDrawn : null;

    /// <summary>
    /// A hand as last drawn, whatever it held (camera space): its wrist and its digit joints (a row per finger, then the
    /// thumb); null before the arms are first drawn. What the grip landmarks measure.
    /// </summary>
    internal HandRig Rig(bool offHand) => offHand ? offRig : mainRig;

    /// <summary>
    /// How far each holding wrist was drawn from its target, as the Engine's solve reports it (metres; the off hand's
    /// -1 when it holds nothing): more than a centimetre or so is a grip out of the arm's reach.
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
            parents = [.. joints.ToArray().Select(j => j.Parent)];
            string[] clips = engine.Animation.ReadClips(model).ToArray().Select(c => c.Id).ToArray();
            if (!clips.Contains(arms.Clip))
                throw new InvalidDataException($"content/{ArmsDefinition.Path} clip: the arms model '{arms.Model}' has no clip '{arms.Clip}'. Its clips: {string.Join(", ", clips)}.");
            main = Chain(joints, arms.Main, "main");
            off = Chain(joints, arms.Off, "off");
            mainDigits = Digits(joints, arms.Main, "main");
            offDigits = Digits(joints, arms.Off, "off");
            // A digit's chain runs from the hand joint one joint to the next (the hand fit poses it so).
            foreach (var (chain, hand, digits) in new[] { (arms.Main, main.Hand, mainDigits), (arms.Off, off.Hand, offDigits) })
                foreach (uint[] digit in digits.Fingers.Append(digits.Thumb))
                    for (int i = 0; i < 3; i++)
                        if (parents[digit[i]] != (i == 0 ? hand : digit[i - 1]))
                            throw new InvalidDataException($"content/{ArmsDefinition.Path} hands: in the arms model '{arms.Model}', '{joints[(int)digit[i]].Id}' does not hang from {(i == 0 ? $"'{chain.Hand}'" : $"'{joints[(int)digit[i - 1]].Id}'")}.");
            mainRig = Rig(joints, arms.Main, HandSide.Right);
            offRig = Rig(joints, arms.Off, HandSide.Left);
            appearance = engine.Animation.CreateAnimatedMeshAppearance(new(model));
        }
        catch { model.Dispose(); throw; }
    }

    // A hand's rig joints named once for the Engine's hand landmarks: the hand joint and each digit's joints 1 to 3, the
    // fingers as the landmark tuning names them.
    private HandRig Rig(ReadOnlySpan<AnimationJointInfo> joints, ArmChain chain, HandSide side)
    {
        HandLandmarks names = arms.Hands.Landmarks;
        string[] Joints(string digit) => [.. Enumerable.Range(1, 3).Select(i => $"{chain.Digits}{digit}{i}")];
        return HandRig.FromJoints(joints, entity, side, chain.Hand, Joints(arms.Hands.Thumb), Joints(names.Index), Joints(names.Middle),
            Joints(names.Ring), Joints(names.Pinky));
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

    // A hand's digits as rotations added to the grip clip's finger and thumb joints: the look's shape for the hand, then
    // its closure, toward Close at 1 and toward Open at -1. Fingers curl about their joints' X (negative toward the
    // palm); a thumb also swings about Z, mirrored on the left.
    private IEnumerable<(uint Joint, Quaternion Turn)> Close((uint[][] Fingers, uint[] Thumb) digits, HandShape? shape, float closure, bool mirrored)
    {
        DigitBend bend = closure > 0 ? arms.Hands.Close : arms.Hands.Open;
        float weight = MathF.Abs(closure), side = mirrored ? -1 : 1;
        // Opening reaches the authored open pose, independently of the fitted grip. Adding Open on top of a
        // partially open fitted finger would bend it backwards on a throw. Squeezing remains relative to the fit.
        float Bend(float shaped, float target) => closure < 0 ? shaped * (1 - weight) + target * weight : shaped + target * weight;
        const float Radians = MathF.PI / 180;
        for (int f = 0; f < digits.Fingers.Length; f++)
            for (int i = 0; i < 3; i++)
            {
                float curl = Bend(shape?.Fingers[f][i] ?? 0, bend.Fingers[i]);
                if (curl != 0) yield return (digits.Fingers[f][i], Quaternion.CreateFromAxisAngle(Vector3.UnitX, -curl * Radians));
            }
        for (int i = 0; i < 3; i++)
        {
            float curl = Bend(shape?.Thumb[i][0] ?? 0, bend.Thumb[i][0]), swing = Bend(shape?.Thumb[i][1] ?? 0, bend.Thumb[i][1]);
            if (curl != 0 || swing != 0)
                yield return (digits.Thumb[i], Quaternion.Normalize(
                    Quaternion.CreateFromAxisAngle(Vector3.UnitZ, swing * side * Radians) * Quaternion.CreateFromAxisAngle(Vector3.UnitX, -curl * Radians)));
        }
    }

    private static bool Same(Dictionary<uint, Quaternion> a, Dictionary<uint, Quaternion> b) =>
        a.Count == b.Count && a.All(t => b.TryGetValue(t.Key, out Quaternion other) && other == t.Value);

    // A hand's digit chains from the drawn arms: each joint's turn and offset from its parent, less the turn added to it.
    private HandChains Read(ReadOnlySpan<JointPose> joints, (uint[][] Fingers, uint[] Thumb) digits)
    {
        JointPose[] drawn = joints.ToArray();
        DigitChain Chain(uint[] digit)
        {
            Quaternion[] turns = new Quaternion[3];
            Vector3[] offsets = new Vector3[3];
            for (int i = 0; i < 3; i++)
            {
                Transform at = drawn[digit[i]].Model, parent = drawn[parents[digit[i]]].Model;
                Quaternion from = Quaternion.Conjugate(parent.Rotation);
                Quaternion added = sent.TryGetValue(digit[i], out Quaternion turn) ? turn : Quaternion.Identity;
                turns[i] = Quaternion.Normalize(from * at.Rotation * Quaternion.Conjugate(added));
                offsets[i] = Vector3.Transform(at.Translation - parent.Translation, from);
            }
            return new(turns, offsets);
        }
        return new([.. digits.Fingers.Select(Chain)], Chain(digits.Thumb));
    }

    /// <summary>The arms' fact for the combat snapshot: at the shoulder in camera space, shown while something is held.</summary>
    internal AppearanceFact Fact(bool shown) =>
        new(entity, false, 0, new(Authored.Vector(arms.Shoulder), Quaternion.Identity, Vector3.One), appearance, shown, RenderLayer.Viewmodel);

    /// <summary>
    /// Reaches the hands for the grips (camera space) once the arms are in the published snapshot: the off hand hangs
    /// at its rest when <paramref name="offGrip"/> is null.
    /// </summary>
    /// <param name="shape">How the look shapes each hand's digits on the grip clip.</param>
    /// <param name="closure">How closed each hand is (main, off): -1 open, 0 the grip clip, 1 squeezed.</param>
    internal void Reach(Vector3 mainGrip, Vector3? offGrip, Quaternion item, float[]? lookHand = null, float[]? offHand = null, HeldShape? shape = null,
        (float Main, float Off) closure = default)
    {
        if (instance is null)
        {
            instance = engine.Animation.CreateInstance(new(appearance, entity));
            // The hands hold by the grip clip; each closes or opens from it by joint overrides, and IK places the arms.
            engine.Animation.SetPlayback(new(instance, AnimationPlaybackKind.Play, arms.Clip, AnimationLoopMode.Repeat, 1, 1, true, 0, true, 0));
        }
        Vector3 shoulder = Authored.Vector(arms.Shoulder);
        // Each holding hand turns with the item, and its wrist sits so the palm, not the wrist, is on the grip.
        // Main and support hands have independent turns: a pistol grip must not turn the supporting palm over too.
        var (mainWrist, mainTurn) = Wrist(mainGrip, item, arms.MainHand, lookHand);
        List<JointOverride> hands = [JointOverride.Orient(main.Hand, PoseSpace.Model, mainTurn)];
        Dictionary<uint, Quaternion> now = [];
        Vector3 offWrist = Authored.Vector(arms.OffRest);
        if (offGrip is { } grip)
        {
            (offWrist, Quaternion offTurn) = Wrist(grip, item, arms.OffHand, offHand);
            hands.Add(JointOverride.Orient(off.Hand, PoseSpace.Model, offTurn));
            foreach (var (joint, turn) in Close(offDigits, shape?.Off, closure.Off, arms.Off.Mirrored)) now[joint] = turn;
        }
        foreach (var (joint, turn) in Close(mainDigits, shape?.Main, closure.Main, arms.Main.Mirrored)) now[joint] = turn;
        hands.AddRange(now.Select(t => JointOverride.AddLocalRotation(t.Key, t.Value)));
        // The arms as last drawn: the solve's reach for each wrist (main, then off, as posed below) and the digits.
        if (engine.Animation.ReadJointPose(instance) is { Reported: true } drawn && drawn.IkChains.Length == 2)
        {
            ReadOnlySpan<JointPose> joints = drawn.Joints.Span;
            ReadOnlySpan<IkChainReach> reach = drawn.IkChains.Span;
            Shortfall = (reach[0].Shortfall, offHeld ? reach[1].Shortfall : -1);
            drawnFor = (reach[0].TargetModel + shoulder, reach[1].TargetModel + shoulder);
            if (steady >= SteadyCalls)
            {
                mainChains = Read(joints, mainDigits);
                offChains = Read(joints, offDigits);
            }
            Vector3[][] At((uint[][] Fingers, uint[] Thumb) digits, ReadOnlySpan<JointPose> all)
            {
                JointPose[] drawn = all.ToArray();
                return [.. digits.Fingers.Append(digits.Thumb).Select(d => d.Select(j => drawn[j].Model.Translation + shoulder).ToArray())];
            }
            mainDrawn = At(mainDigits, joints);
            offDrawn = At(offDigits, joints);
        }
        steady = Same(sent, now) ? steady + 1 : 0;
        sent = now;
        offHeld = offGrip is not null;
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
        HandFit fit = arms.Hands.Fit;
        foreach (var (field, value) in new[] { ("fingerRadius", fit.FingerRadius), ("thumbRadius", fit.ThumbRadius), ("tip", fit.Tip), ("step", fit.Step), ("reach", fit.Reach) })
            Authored.Positive(Path, $"hands.fit.{field}", value);
        Authored.Require(fit.Gap >= 0, Path, "hands.fit.gap", "is how far short of the surface a digit stops, 0 or more metres.");
        Authored.Require(fit.Open < fit.Closed, Path, "hands.fit.closed", "must curl further than hands.fit.open.");
        Authored.Require(fit.Samples >= 1, Path, "hands.fit.samples", "probes each segment at least once.");
        HandLandmarks marks = arms.Hands.Landmarks;
        foreach (var (field, finger) in new[] { ("index", marks.Index), ("middle", marks.Middle), ("ring", marks.Ring), ("pinky", marks.Pinky) })
            Authored.Require(arms.Hands.Fingers.Contains(finger), Path, $"hands.landmarks.{field}", $"names one of hands.fingers ({string.Join(", ", arms.Hands.Fingers)}).");
        foreach (var (field, value) in new[] { ("web", marks.Web), ("palm", marks.Palm) })
            Authored.Within(Path, $"hands.landmarks.{field}", value, 0, 1);
        foreach (var (field, value) in new[] { ("palmDepth", marks.PalmDepth), ("trigger", marks.Trigger), ("backstrap", marks.Backstrap), ("butt", marks.Butt), ("foreEnd", marks.ForeEnd), ("facing", marks.Facing) })
            Authored.Positive(Path, $"hands.landmarks.{field}", value);
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
internal sealed record ArmDigits(string[] Fingers, string Thumb, DigitBend Close, DigitBend Open, HandFit Fit, HandLandmarks Landmarks);

/// <summary>
/// How the developer hand fit (<see cref="HandFitting"/>) measures a digit against a held model: the digits' radii
/// about their joints (metres), how far the tip runs past the last joint (a fraction of the joint before it), the gap
/// it stops short of the surface by, the curls it sweeps each joint through (degrees, open to closed, by step), the
/// probes per segment and how far it looks for the surface.
/// </summary>
internal sealed record HandFit
{
    public required float FingerRadius { get; set; }
    public required float ThumbRadius { get; set; }
    public required float Tip { get; set; }
    public required float Gap { get; set; }
    public required float Open { get; set; }
    public required float Closed { get; set; }
    public required float Step { get; set; }
    public required int Samples { get; set; }
    public required float Reach { get; set; }

    internal void Take(HandFit fresh) =>
        (FingerRadius, ThumbRadius, Tip, Gap, Open, Closed, Step, Samples, Reach) =
        (fresh.FingerRadius, fresh.ThumbRadius, fresh.Tip, fresh.Gap, fresh.Open, fresh.Closed, fresh.Step, fresh.Samples, fresh.Reach);
}

/// <summary>
/// Where the grip landmarks (<see cref="GripLandmarks"/>, measured by the Engine on the drawn hands) find a hand's own
/// points, and how near a look's landmarks they must come: which fingers are the index, middle, ring and pinky; the web,
/// a fraction of the way from the thumb's first joint to the index finger's first (both fixed to the hand, so the web
/// stays put however the thumb turns); the palm centre, a fraction of the way from the wrist to the middle finger's first
/// joint and <see cref="PalmDepth"/> metres out along the palm's normal; then the tolerances, metres from the trigger,
/// the back strap's top, the butt and the fore-end, and degrees between the palm's normal and the side it should face.
/// </summary>
internal sealed record HandLandmarks
{
    public required string Index { get; set; }
    public required string Middle { get; set; }
    public required string Ring { get; set; }
    public required string Pinky { get; set; }
    public required float Web { get; set; }
    public required float Palm { get; set; }
    public required float PalmDepth { get; set; }
    public required float Trigger { get; set; }
    public required float Backstrap { get; set; }
    public required float Butt { get; set; }
    public required float ForeEnd { get; set; }
    public required float Facing { get; set; }

    internal void Take(HandLandmarks fresh) =>
        (Web, Palm, PalmDepth, Trigger, Backstrap, Butt, ForeEnd, Facing) =
        (fresh.Web, fresh.Palm, fresh.PalmDepth, fresh.Trigger, fresh.Backstrap, fresh.Butt, fresh.ForeEnd, fresh.Facing);
}

/// <summary>Degrees added to each finger joint's curl (base to tip, the same for every finger) and each thumb joint's [curl, swing].</summary>
internal sealed record DigitBend(float[] Fingers, float[][] Thumb);

/// <summary>
/// How far the arms reach: <see cref="Length"/>, shoulder to wrist with the elbow straight (the upper arm and forearm
/// of the arms model's rig), and where the left shoulder is from the right in the model (<see cref="OffShoulder"/>).
/// A grip whose wrist lies further from its shoulder cannot be held.
/// </summary>
internal sealed record ArmReach(float Length, float[] OffShoulder);
