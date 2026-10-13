using System.Numerics;
using System.Text.Json.Serialization;
using Hotel.Game.Content;
using Hotel.Game.Supplies;
using Rusty.Engine;

namespace Hotel.Game.Combat;

/// <summary>
/// How what the hands hold is drawn in first person: the one model each held look shows, how it moves
/// (<see cref="Motion"/>, from <see cref="HeldMotionCatalog.Path"/>), where hands grip it, and where a firearm's muzzle
/// flash appears. Presentation only; what an item does is its actions.
/// </summary>
internal sealed record HeldCatalog(float[] FlashSize, HeldModel[] Looks)
{
    [System.Text.Json.Serialization.JsonIgnore] internal HeldMotionCatalog Motion { get; init; } = null!;
    /// <summary>The first-person arms that hold what the hands hold (<see cref="ArmsDefinition.Path"/>).</summary>
    [System.Text.Json.Serialization.JsonIgnore] internal ArmsDefinition Arms { get; init; } = null!;
    /// <summary>Where each kind of held thing sits at rest (<see cref="StanceCatalog.Path"/>).</summary>
    [System.Text.Json.Serialization.JsonIgnore] internal StanceCatalog Stances { get; init; } = null!;

    internal const string Path = "combat/held.json";

    internal static HeldCatalog Load(IEngineContext engine)
    {
        HeldCatalog catalog = Authored.Read(engine, Path, ContentJson.Default.HeldCatalog) with
        {
            Motion = HeldMotionCatalog.Load(engine), Arms = ArmsDefinition.Load(engine), Stances = StanceCatalog.Load(engine)
        };
        catalog.Validate();
        foreach (HeldModel look in catalog.Looks) catalog.Seat(look);
        return catalog;
    }

    /// <summary>
    /// Seats a look on its stance: its main grip on the stance's grip and its length and up side along the stance,
    /// after the look's own nudge. Its motion's poses move it from there. Changing a stance seats every look in it alike.
    /// </summary>
    internal void Seat(HeldModel look)
    {
        HeldStance stance = Stances.Stances[look.Stance];
        Vector3 nudge = Authored.Vector(look.Adjust.Turn) * (MathF.PI / 180);
        look.Seated = (Authored.Vector(stance.Grip) + Authored.Vector(look.Adjust.Grip),
            Quaternion.Normalize(Quaternion.CreateFromYawPitchRoll(nudge.Y, nudge.X, nudge.Z) * stance.Turn(look.Along)));
    }

    internal HeldModel Model(string look) => Looks.Single(l => l.Look == look);

    /// <summary>The content files the held presentation reads, reloaded in place under <c>rusty dev</c> (RustyEngineLiveContent).</summary>
    internal static readonly string[] Files = [Path, StanceCatalog.Path, HeldMotionCatalog.Path, ArmsDefinition.Path];

    /// <summary>
    /// Takes the values of <paramref name="fresh"/> from the files in <paramref name="changed"/> into this catalog in
    /// place (unchanged files keep their live, possibly unsaved, values), so the views holding it, the motion viewer's
    /// held pose included, show them; then seats every look again. Returns what it could not take: changes to what
    /// exists or how it is drawn (looks, models, grips, motions' settle, stances, the arms' rig), which need the product
    /// restarted; the rest of those files is still taken.
    /// </summary>
    internal string[] Refresh(HeldCatalog fresh, IReadOnlySet<string> changed)
    {
        List<string> needRestart = [];
        void Same(bool same, string what) { if (!same) needRestart.Add(what); }

        if (changed.Contains(Path))
        {
            Same(FlashSize.SequenceEqual(fresh.FlashSize), $"{Path} flashSize");
            Same(Looks.Length == fresh.Looks.Length && Looks.Zip(fresh.Looks).All(p => Drawn(p.First) == Drawn(p.Second)),
                $"{Path} looks (ids, models, scales, motions, stances, grips, axes or muzzles)");
            foreach (HeldModel look in Looks)
                if (fresh.Looks.FirstOrDefault(l => l.Look == look.Look) is { } now)
                {
                    now.Adjust.Grip.CopyTo(look.Adjust.Grip, 0);
                    now.Adjust.Turn.CopyTo(look.Adjust.Turn, 0);
                    if (look.Shape is { } shape && now.Shape is { } fresher) shape.Take(fresher);
                }
        }
        if (changed.Contains(StanceCatalog.Path))
        {
            Same(Stances.Stances.Keys.Order().SequenceEqual(fresh.Stances.Stances.Keys.Order()), $"{StanceCatalog.Path} stance ids");
            foreach (var (id, stance) in fresh.Stances.Stances)
                if (Stances.Stances.TryGetValue(id, out HeldStance? live))
                {
                    stance.Grip.CopyTo(live.Grip, 0);
                    stance.Toward.CopyTo(live.Toward, 0);
                    stance.Up.CopyTo(live.Up, 0);
                    if (live.Hand is { } hand && stance.Hand is { } next) next.CopyTo(hand, 0);
                    else Stances.Stances[id] = live with { Hand = stance.Hand };
                }
        }
        if (changed.Contains(HeldMotionCatalog.Path))
        {
            Same(Motion.SettleSeconds == fresh.Motion.SettleSeconds && Motion.SettleEase == fresh.Motion.SettleEase,
                $"{HeldMotionCatalog.Path} settleSeconds or settleEase");
            Same(Motion.Motions.Keys.Order().SequenceEqual(fresh.Motion.Motions.Keys.Order()), $"{HeldMotionCatalog.Path} motion ids");
            foreach (var (id, motion) in fresh.Motion.Motions)
            {
                if (!Motion.Motions.TryGetValue(id, out HeldMotion? live)) continue;
                // Poses keep their identity (the motion viewer may hold one); tracks follow the file exactly.
                foreach (var (name, pose) in motion.Poses)
                    if (live.Poses.TryGetValue(name, out HeldPose? held))
                    {
                        pose.Position.CopyTo(held.Position, 0);
                        pose.Rotation.CopyTo(held.Rotation, 0);
                    }
                    else live.Poses[name] = pose;
                foreach (string gone in live.Poses.Keys.Except(motion.Poses.Keys).ToArray()) live.Poses.Remove(gone);
                live.Tracks.Clear();
                foreach (var (track, keys) in motion.Tracks) live.Tracks[track] = keys;
            }
        }
        if (changed.Contains(ArmsDefinition.Path))
        {
            ArmsDefinition arms = fresh.Arms;
            Same(Arms.Model == arms.Model && Arms.Clip == arms.Clip && Arms.Main == arms.Main && Arms.Off == arms.Off
                && Arms.Hands.Fingers.SequenceEqual(arms.Hands.Fingers) && Arms.Hands.Thumb == arms.Hands.Thumb,
                $"{ArmsDefinition.Path} model, clip, joint chains or the hands' finger and thumb joints");
            // How the hands close is taken in place too.
            arms.Hands.Close.Fingers.CopyTo(Arms.Hands.Close.Fingers, 0);
            arms.Hands.Open.Fingers.CopyTo(Arms.Hands.Open.Fingers, 0);
            for (int i = 0; i < 3; i++)
            {
                arms.Hands.Close.Thumb[i].CopyTo(Arms.Hands.Close.Thumb[i], 0);
                arms.Hands.Open.Thumb[i].CopyTo(Arms.Hands.Open.Thumb[i], 0);
            }
            arms.Shoulder.CopyTo(Arms.Shoulder, 0);
            arms.Pole.CopyTo(Arms.Pole, 0);
            arms.OffPole.CopyTo(Arms.OffPole, 0);
            arms.OffRest.CopyTo(Arms.OffRest, 0);
            foreach (var (into, from) in new[] { (Arms.MainHand, arms.MainHand), (Arms.OffHand, arms.OffHand) })
            {
                from.Rotation.CopyTo(into.Rotation, 0);
                from.Palm.CopyTo(into.Palm, 0);
            }
        }
        foreach (HeldModel look in Looks) Seat(look);
        return [.. needRestart];
    }

    // What a look is and how it is drawn, beyond the nudge a reload takes: a change needs a restart.
    private static string Drawn(HeldModel look) => string.Join("|", look.Look, look.Model, look.Scale, look.Motion, look.Stance,
        string.Join(",", look.Grips.Main), string.Join(",", look.Grips.Off ?? []), string.Join(",", look.Grips.Hand ?? []),
        string.Join(",", look.Along.Tip), string.Join(",", look.Along.Up), string.Join(",", look.Muzzle ?? []));

    private void Validate()
    {
        Authored.Point(Path, "flashSize", FlashSize);
        foreach (string look in Looks.Select(l => l.Look).Distinct())
            Authored.Require(Looks.Count(l => l.Look == look) == 1, Path, "looks", $"names the '{look}' look more than once.");
        for (int i = 0; i < Looks.Length; i++)
        {
            HeldModel model = Looks[i];
            string at = $"looks[{i}]";
            Authored.Require(model.Model.EndsWith(".glb", StringComparison.Ordinal), Path, $"{at}.model", "must be a GLB content path.");
            Authored.Point(Path, $"{at}.nudge.grip", model.Adjust.Grip);
            Authored.Point(Path, $"{at}.nudge.turn", model.Adjust.Turn);
            Authored.Positive(Path, $"{at}.scale", model.Scale);
            if (model.Muzzle is { } muzzle) Authored.Point(Path, $"{at}.muzzle", muzzle);
            Authored.Require(Motion.Motions.ContainsKey(model.Motion), Path, $"{at}.motion", $"unknown motion '{model.Motion}' in {HeldMotionCatalog.Path}.");
            Authored.Require(Stances.Stances.ContainsKey(model.Stance), Path, $"{at}.stance", $"unknown stance '{model.Stance}' in {StanceCatalog.Path}.");
            if (model.Grips.Hand is { } hand) Authored.Point(Path, $"{at}.grips.hand", hand);
            Authored.Require(model.Shape is not null, Path, $"{at}.shape", "gives how each hand's fingers and thumb sit on this look (main, and off with an off grip).");
            Authored.Require((model.Shape!.Off is null) == (model.Grips.Off is null), Path, $"{at}.shape.off",
                model.Grips.Off is null ? "is only given for a look with an off grip." : "is required: the look has an off grip.");
            foreach (var (side, shape) in new[] { ("main", model.Shape.Main), ("off", model.Shape.Off) })
            {
                if (shape is null) continue;
                Authored.Require(shape.Fingers.Length == Arms.Hands.Fingers.Length && shape.Fingers.All(f => f.Length == 3), Path, $"{at}.shape.{side}.fingers",
                    $"gives each of the arms' {Arms.Hands.Fingers.Length} fingers ({ArmsDefinition.Path} hands.fingers) three joints' curl, in degrees.");
                Authored.Require(shape.Thumb.Length == 3 && shape.Thumb.All(j => j.Length == 2), Path, $"{at}.shape.{side}.thumb",
                    "gives three thumb joints' [curl, swing], in degrees.");
            }
            Authored.Point(Path, $"{at}.axis.tip", model.Along.Tip);
            Authored.Point(Path, $"{at}.axis.up", model.Along.Up);
            Authored.Require(System.Numerics.Vector3.Cross(model.Along.TipVector, model.Along.UpVector).Length() > 0.5f, Path, $"{at}.axis",
                "tip and up must lie across each other.");
        }
    }
}

/// <summary>
/// One held look, named for the items that show it: its model at a uniform scale; the <see cref="Motion"/> it moves
/// by; the <see cref="Stance"/> it sits in at rest, its own length and up side (<see cref="Axis"/>, default -Z with +Y
/// up) and an optional <see cref="Nudge"/> from that stance; where hands grip it (<see cref="Grips"/>, in the model's own
/// space); how each hand's digits sit on it (<see cref="Shape"/>); and, for a firearm, its muzzle in the same space, where its flash appears.
/// </summary>
internal sealed record HeldModel(string Look, string Model, float Scale, string Motion, string Stance, HeldGrips Grips,
    float[]? Muzzle = null, HeldAxis? Axis = null, HeldNudge? Nudge = null, HeldShape? Shape = null)
{
    /// <summary>Where its stance seats it (camera space): its main grip's place and the model's turn, set by <see cref="HeldCatalog.Seat"/>.</summary>
    [JsonIgnore] internal (Vector3 Grip, Quaternion Turn) Seated { get; set; }
    /// <summary>This look's nudge from its stance (none when it has none).</summary>
    [JsonIgnore] internal HeldNudge Adjust { get; } = Nudge ?? new([0, 0, 0], [0, 0, 0]);

    /// <summary>The model's length and up side: its <see cref="Axis"/>, or -Z with +Y up.</summary>
    internal HeldAxis Along => Axis ?? HeldAxis.Default;

}

/// <summary>
/// A look's correction to its stance, judged by eye: its grip moved by <see cref="Grip"/> (camera space, metres) and the
/// item turned further by <see cref="Turn"/> (degrees about camera X, Y, Z) about that grip.
/// </summary>
internal sealed record HeldNudge(float[] Grip, float[] Turn);

/// <summary>How each hand's digits sit on a look, added to the arms' grip clip: the main hand, and the off hand when the look has an off grip.</summary>
internal sealed record HeldShape(HandShape Main, HandShape? Off = null)
{
    internal void Take(HeldShape fresh)
    {
        Main.Take(fresh.Main);
        if (Off is { } off && fresh.Off is { } next) off.Take(next);
    }
}

/// <summary>
/// Degrees added to a hand's digits on the grip clip, under the motion keys' closure: each finger's three joints' curl
/// (base to tip, in the order of the arms' fingers; positive toward the palm) and each thumb joint's [curl, swing].
/// </summary>
internal sealed record HandShape(float[][] Fingers, float[][] Thumb)
{
    internal void Take(HandShape fresh)
    {
        for (int i = 0; i < Math.Min(Fingers.Length, fresh.Fingers.Length); i++) fresh.Fingers[i].CopyTo(Fingers[i], 0);
        for (int i = 0; i < 3; i++) fresh.Thumb[i].CopyTo(Thumb[i], 0);
    }
}
