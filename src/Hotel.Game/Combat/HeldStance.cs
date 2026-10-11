using System.Numerics;
using Hotel.Game.Content;

namespace Hotel.Game.Combat;

/// <summary>
/// Where held things sit in first person at rest, by kind: a melee weapon off at the lower right and rising, a firearm
/// nearer the centre and converging on the crosshair. Each stance gives, in camera space (metres right, up, back), where
/// the main grip sits, a point the item's length heads toward, and the way its up side faces. A look names its stance;
/// the developer stance guide draws it, and fitting places a look on it, so every item of a kind sits alike.
/// </summary>
internal sealed record StanceCatalog(Dictionary<string, HeldStance> Stances)
{
    internal const string Path = "combat/stances.json";

    internal static StanceCatalog Load(Rusty.Engine.IEngineContext engine)
    {
        StanceCatalog catalog = Authored.Read(engine, Path, ContentJson.Default.StanceCatalog);
        Authored.Require(catalog.Stances.Count > 0, Path, "stances", "needs at least one stance.");
        foreach (var (id, stance) in catalog.Stances)
        {
            Authored.Point(Path, $"stances.{id}.grip", stance.Grip);
            Authored.Point(Path, $"stances.{id}.toward", stance.Toward);
            Authored.Point(Path, $"stances.{id}.up", stance.Up);
            if (stance.Hand is { } hand) Authored.Point(Path, $"stances.{id}.hand", hand);
            Vector3 along = Authored.Vector(stance.Toward) - Authored.Vector(stance.Grip);
            Authored.Require(along.Length() > 0.05f, Path, $"stances.{id}.toward", "must lie away from the grip.");
            Authored.Require(Vector3.Cross(Vector3.Normalize(along), Authored.Vector(stance.Up)).Length() > 0.1f, Path,
                $"stances.{id}.up", "must not lie along the item's length.");
        }
        return catalog;
    }
}

/// <summary>
/// A stance: the grip's place, a point the item's length heads toward, and the way its up side faces (camera space);
/// and how hands hold things in it (<see cref="Hand"/>, degrees about X, Y, Z in the item's axis frame), when not as
/// the arms' default hand holds: a firearm is held round its pistol grip, across its length.
/// </summary>
internal sealed record HeldStance(float[] Grip, float[] Toward, float[] Up, float[]? Hand = null)
{
    /// <summary>The turn that lays an item's length (<paramref name="axis"/>'s tip) toward the stance's point with its up side facing the stance's up.</summary>
    internal Quaternion Turn(HeldAxis axis)
    {
        Vector3 along = Vector3.Normalize(Authored.Vector(Toward) - Authored.Vector(Grip));
        return Frames.Between(axis.TipVector, axis.UpVector, along, Authored.Vector(Up));
    }

}

/// <summary>
/// An item model's own length and up side, in its model space: <see cref="Tip"/> from the grip toward its business end
/// (a blade, a barrel's muzzle, a candelabra's candles) and <see cref="Up"/> across it (a gun's sights, an axe's head).
/// </summary>
internal sealed record HeldAxis(float[] Tip, float[] Up)
{
    internal static readonly HeldAxis Default = new([0, 0, -1], [0, 1, 0]);
    internal Vector3 TipVector => Vector3.Normalize(Authored.Vector(Tip));
    internal Vector3 UpVector => Vector3.Normalize(Authored.Vector(Up));
}

/// <summary>Rotations between frames, and the product's degree convention (about X, Y, Z: yaw about Y, then pitch about X, then roll about Z, as <see cref="Quaternion.CreateFromYawPitchRoll"/>).</summary>
internal static class Frames
{
    /// <summary>The rotation taking <paramref name="tip"/> to <paramref name="toward"/> and <paramref name="up"/> as near <paramref name="upToward"/> as it can.</summary>
    internal static Quaternion Between(Vector3 tip, Vector3 up, Vector3 toward, Vector3 upToward)
    {
        (Vector3 a, Vector3 b, Vector3 c) = Basis(tip, up);
        (Vector3 x, Vector3 y, Vector3 z) = Basis(toward, upToward);
        // Each axis of the space goes where its share of the item's basis says, rebuilt on the target basis.
        Vector3 Image(Vector3 v) => x * Vector3.Dot(v, a) + y * Vector3.Dot(v, b) + z * Vector3.Dot(v, c);
        Vector3 ix = Image(Vector3.UnitX), iy = Image(Vector3.UnitY), iz = Image(Vector3.UnitZ);
        // System.Numerics matrices carry each basis vector's image as a row.
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new(ix.X, ix.Y, ix.Z, 0, iy.X, iy.Y, iy.Z, 0, iz.X, iz.Y, iz.Z, 0, 0, 0, 0, 1)));
    }

    private static (Vector3, Vector3, Vector3) Basis(Vector3 along, Vector3 upHint)
    {
        Vector3 a = Vector3.Normalize(along);
        Vector3 b = Vector3.Normalize(upHint - a * Vector3.Dot(upHint, a));
        return (a, b, Vector3.Cross(a, b));
    }

    /// <summary>Degrees about X, Y and Z that <see cref="Quaternion.CreateFromYawPitchRoll"/> (yaw Y, pitch X, roll Z) turns back into <paramref name="turn"/>.</summary>
    internal static Vector3 Degrees(Quaternion turn)
    {
        // The rotation's columns are the images of the axes; for R = Ry Rx Rz, R12 = -sin x, R02 / R22 = tan y and
        // R10 / R11 = tan z.
        Vector3 cx = Vector3.Transform(Vector3.UnitX, turn), cy = Vector3.Transform(Vector3.UnitY, turn), cz = Vector3.Transform(Vector3.UnitZ, turn);
        float x = MathF.Asin(Math.Clamp(-cz.Y, -1, 1));
        float y = MathF.Atan2(cz.X, cz.Z);
        float z = MathF.Atan2(cx.Y, cy.Y);
        return new Vector3(x, y, z) * (180 / MathF.PI);
    }
}
