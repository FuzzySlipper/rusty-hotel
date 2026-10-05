using System.Numerics;

namespace Hotel.Game.Scene.Kit;

/// <summary>
/// Places a fixture's frame in the world: an optional mirror of local x, then whole quarter turns about +Y
/// (the same sense as <c>Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle)</c>), then the origin.
/// Quarter turns keep boxes axis-aligned.
/// </summary>
internal static class KitTransform
{
    internal static Vector3 Point(float[] local, Vector3 origin, int turn, bool mirror) =>
        Point(new Vector3(local[0], local[1], local[2]), origin, turn, mirror);

    internal static Vector3 Point(Vector3 local, Vector3 origin, int turn, bool mirror)
    {
        float x = mirror ? -local.X : local.X, z = local.Z;
        (int cos, int sin) = (((turn % 4) + 4) % 4) switch { 0 => (1, 0), 1 => (0, 1), 2 => (-1, 0), _ => (0, -1) };
        return origin + new Vector3(x * cos + z * sin, local.Y, -x * sin + z * cos);
    }

    internal static (Vector3 Min, Vector3 Max) Box(float[] min, float[] max, Vector3 origin, int turn, bool mirror)
    {
        Vector3 a = Point(min, origin, turn, mirror), b = Point(max, origin, turn, mirror);
        return (Vector3.Min(a, b), Vector3.Max(a, b));
    }

    /// <summary>The turn that points a wall fixture's +z out of the wall into the room.</summary>
    internal static int WallTurn(WallEdge edge) => edge switch
    {
        WallEdge.North => 0, WallEdge.West => 1, WallEdge.South => 2, _ => 3
    };
}
