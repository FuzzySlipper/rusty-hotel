using Hotel.Game.Content;
using Hotel.Game.Supplies;
using Rusty.Engine;

namespace Hotel.Game.Combat;

/// <summary>
/// How what the hands hold is drawn in first person: where the hand rests and moves in each phase of an action, the one
/// model each held look shows, and where a firearm's muzzle flash appears. Presentation only; what an item does is its
/// actions.
/// </summary>
internal sealed record HeldCatalog(HandPose Hand, float[] FlashSize, HeldModel[] Looks)
{
    internal const string Path = "combat/held.json";

    internal static HeldCatalog Load(IEngineContext engine)
    {
        HeldCatalog catalog = Authored.Read(engine, Path, ContentJson.Default.HeldCatalog);
        catalog.Validate();
        return catalog;
    }

    internal HeldModel Model(string look) => Looks.Single(l => l.Look == look);

    private void Validate()
    {
        Authored.Point(Path, "hand.rest", Hand.Rest);
        Authored.Point(Path, "hand.windup", Hand.Windup);
        Authored.Point(Path, "hand.commit", Hand.Commit);
        Authored.Finite(Path, "hand.recoveryDrop", Hand.RecoveryDrop);
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
/// The hand in camera space (metres: right, up, back): at rest, the full lift of a windup (scaled by its progress), the
/// thrust of a commit, and how far it drops in recovery.
/// </summary>
internal sealed record HandPose(float[] Rest, float[] Windup, float[] Commit, float RecoveryDrop);

/// <summary>
/// One held look, named for the items that show it: its model, placed from the hand by an offset and a rotation (degrees about X, Y, Z) at a uniform
/// scale, and, for a firearm, the muzzle point in the same hand space where its flash appears.
/// </summary>
internal sealed record HeldModel(string Look, string Model, float[] Offset, float[] Rotation, float Scale, float[]? Muzzle = null);
