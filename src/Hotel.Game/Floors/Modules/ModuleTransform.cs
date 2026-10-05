using System.Numerics;
using Hotel.Game.Scene.Kit;

namespace Hotel.Game.Floors.Modules;

/// <summary>
/// Places a module's frame on a floor: whole quarter turns about +Y in the kit's sense (see <see cref="KitTransform"/>),
/// then a shift that puts the turned module's north-west corner at <see cref="Corner"/>.
/// </summary>
internal sealed record ModuleTransform(ModuleDefinition Module, Vector2 Corner, int Turn)
{
    private int Quarter => ((Turn % 4) + 4) % 4;

    // The turned frame's offset: where local [0, 0] lands when the turned footprint's corner sits at the origin.
    private Vector2 Offset
    {
        get
        {
            Vector2[] corners = [new(0, 0), new(Module.Width, 0), new(0, Module.Depth), new(Module.Width, Module.Depth)];
            Vector2 min = corners.Select(Rotate).Aggregate(Vector2.Min);
            return Corner - min;
        }
    }

    /// <summary>The footprint's size once turned: width and depth swap on odd turns.</summary>
    internal Vector2 Footprint => Quarter % 2 == 0 ? new(Module.Width, Module.Depth) : new(Module.Depth, Module.Width);

    internal Vector2 Point(float x, float z) => Rotate(new(x, z)) + Offset;

    internal float[] Point(float[] xz) { Vector2 p = Point(xz[0], xz[1]); return [p.X, p.Y]; }

    internal (float[] Min, float[] Max) Rect(float[] min, float[] max)
    {
        Vector2 a = Point(min[0], min[1]), b = Point(max[0], max[1]);
        return ([Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)], [Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)]);
    }

    internal WallEdge Edge(WallEdge edge) => ((KitTransform.WallTurn(edge) + Quarter) % 4) switch
    {
        0 => WallEdge.North, 1 => WallEdge.West, 2 => WallEdge.South, _ => WallEdge.East
    };

    /// <summary>
    /// A coordinate along a wall: for a wall running along x it is an x, otherwise a z. Returns the coordinate along
    /// the same wall once placed, whichever axis it then runs along.
    /// </summary>
    internal float Along(bool alongX, float at, float line)
    {
        Vector2 placed = alongX ? Point(at, line) : Point(line, at);
        return PlacedAlongX(alongX) ? placed.X : placed.Y;
    }

    internal bool PlacedAlongX(bool alongX) => alongX == (Quarter % 2 == 0);

    private Vector2 Rotate(Vector2 p)
    {
        Vector3 turned = KitTransform.Point(new Vector3(p.X, 0, p.Y), Vector3.Zero, Quarter, false);
        return new(turned.X, turned.Z);
    }
}
