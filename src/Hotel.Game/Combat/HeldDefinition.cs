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

    internal const string Path = "combat/held.json";

    internal static HeldCatalog Load(IEngineContext engine)
    {
        HeldCatalog catalog = Authored.Read(engine, Path, ContentJson.Default.HeldCatalog) with { Motion = HeldMotionCatalog.Load(engine), Arms = ArmsDefinition.Load(engine) };
        catalog.Validate();
        return catalog;
    }

    internal HeldModel Model(string look) => Looks.Single(l => l.Look == look);

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
            Authored.Point(Path, $"{at}.offset", model.Offset);
            Authored.Point(Path, $"{at}.rotation", model.Rotation);
            Authored.Positive(Path, $"{at}.scale", model.Scale);
            if (model.Muzzle is { } muzzle) Authored.Point(Path, $"{at}.muzzle", muzzle);
        }
    }
}

/// <summary>
/// One held look, named for the items that show it: its model, placed from the hand by an offset and a rotation (degrees about X, Y, Z) at a uniform
/// scale; the <see cref="Motion"/> it moves by; where hands grip it (<see cref="Grips"/>, in the model's own space);
/// and, for a firearm, the muzzle point in the same hand space where its flash appears.
/// </summary>
internal sealed record HeldModel(string Look, string Model, float[] Offset, float[] Rotation, float Scale, string Motion, HeldGrips Grips,
    float[]? Muzzle = null)
{
    internal System.Numerics.Quaternion Turn
    {
        get
        {
            System.Numerics.Vector3 degrees = Authored.Vector(Rotation) * (MathF.PI / 180);
            return System.Numerics.Quaternion.CreateFromYawPitchRoll(degrees.Y, degrees.X, degrees.Z);
        }
    }
}
