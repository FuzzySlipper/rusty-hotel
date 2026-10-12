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
    /// Takes the tuned values of <paramref name="fresh"/> (stances, nudges, poses, keys, arm placement and holds) into
    /// this catalog in place, so the views holding it show them, and seats every look again. Returns what it could not
    /// take: changes to what exists (a look, model, motion, stance or the arms' rig), which need the product restarted.
    /// </summary>
    internal string[] Refresh(HeldCatalog fresh)
    {
        List<string> needRestart = [];
        void Same(bool same, string what) { if (!same) needRestart.Add(what); }
        Same(Looks.Select(l => (l.Look, l.Model, l.Scale, l.Motion, l.Stance)).SequenceEqual(fresh.Looks.Select(l => (l.Look, l.Model, l.Scale, l.Motion, l.Stance))),
            $"{Path} looks (ids, models, scales, motions or stances)");
        Same(Stances.Stances.Keys.Order().SequenceEqual(fresh.Stances.Stances.Keys.Order()), $"{StanceCatalog.Path} stance ids");
        Same(Motion.Motions.Keys.Order().SequenceEqual(fresh.Motion.Motions.Keys.Order()), $"{HeldMotionCatalog.Path} motion ids");
        Same(Arms.Model == fresh.Arms.Model && Arms.Clips == fresh.Arms.Clips && Arms.Main == fresh.Arms.Main && Arms.Off == fresh.Arms.Off,
            $"{ArmsDefinition.Path} model, clips or joint chains");
        if (needRestart.Count > 0) return [.. needRestart];

        foreach (var (id, stance) in fresh.Stances.Stances) Stances.Stances[id] = stance;
        foreach (HeldModel look in Looks)
        {
            HeldModel now = fresh.Model(look.Look);
            now.Adjust.Grip.CopyTo(look.Adjust.Grip, 0);
            now.Adjust.Turn.CopyTo(look.Adjust.Turn, 0);
        }
        foreach (var (id, motion) in fresh.Motion.Motions)
        {
            HeldMotion held = Motion.Motions[id];
            foreach (var (name, pose) in motion.Poses) held.Poses[name] = pose;
            foreach (var (track, keys) in motion.Tracks) held.Tracks[track] = keys;
        }
        ArmsDefinition arms = fresh.Arms;
        arms.Shoulder.CopyTo(Arms.Shoulder, 0);
        arms.Pole.CopyTo(Arms.Pole, 0);
        arms.OffPole.CopyTo(Arms.OffPole, 0);
        arms.OffRest.CopyTo(Arms.OffRest, 0);
        foreach (var (into, from) in new[] { (Arms.MainHand, arms.MainHand), (Arms.OffHand, arms.OffHand) })
        {
            from.Rotation.CopyTo(into.Rotation, 0);
            from.Palm.CopyTo(into.Palm, 0);
        }
        foreach (HeldModel look in Looks) Seat(look);
        return [];
    }

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
/// space); and, for a firearm, its muzzle in the same space, where its flash appears.
/// </summary>
internal sealed record HeldModel(string Look, string Model, float Scale, string Motion, string Stance, HeldGrips Grips,
    float[]? Muzzle = null, HeldAxis? Axis = null, HeldNudge? Nudge = null)
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
