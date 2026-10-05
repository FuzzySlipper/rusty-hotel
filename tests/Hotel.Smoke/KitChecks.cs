using Hotel.Game.Scene;
using Hotel.Game.Scene.Kit;
using Rusty.Engine;

// The kit builds the west wing's walls from its floor plan: half a wall per side, each in its own space's surface,
// cut by links, with lintels and upper trim over openings.
internal static class KitChecks
{
    internal static void Run(IEngineContext engine)
    {
        var content = Owners.Content(engine);
        RoomBox[] boxes = content.Excursion.Geometry.Boxes;
        void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
        // Boxes touching a point on the corridor/portrait-room partition (x = -1.9), at a given z and height.
        RoomBox[] At(float x, float y, float z) => boxes.Where(b => b.Solid && b.Min[0] <= x && b.Max[0] >= x &&
            b.Min[1] <= y && b.Max[1] >= y && b.Min[2] <= z && b.Max[2] >= z).ToArray();

        Check(At(-1.95f, 1.2f, -3).Any(b => b.Material == "medallion") && At(-1.85f, 1.2f, -3).Any(b => b.Material == "wall"),
            "a partition shows each room's own wallpaper on its own side");
        Check(At(-1.95f, 1.2f, -5).Length == 0 && At(-1.85f, 1.2f, -5).Length == 0, "the portrait-room door link cuts the partition");
        Check(At(-1.85f, 2.4f, -5).Any(b => b.Material == "wall"), "a lintel closes the wall above the door");
        Check(boxes.Any(b => b.Name.Contains("picture rail") && b.Min[2] <= -5 && b.Max[2] >= -5 && b.Min[0] < -1.8f && b.Max[0] > -1.85f),
            "the picture rail continues over the door");
        Check(At(0, 1.2f, 0.95f).Length == 0, "the open refuge link removes the shared wall");
        Check(content.Excursion.Route.Rooms.Any(r => r.Id == "corridor" && r.Label == "West wing corridor"), "spaces are the named rooms");
        Check(content.Excursion.Geometry.Lighting.Points.Length == 22, "fixture lights are the floor's point lights");

        // A hatch is a typed raised opening: wall and skirting run below its sill, a lintel closes above it, and a
        // frame gets a sill piece.
        FloorPlan hatched = new([new("pantry", "Pantry", [0, 0], [3, 3], 2.6f, "service"), new("hall", "Hall", [3, 0], [6, 3], 2.6f, "corridor")],
            [new("pass", ["pantry", "hall"], LinkKind.Hatch, At: 1.5f, Width: 1, Height: 0.6f, Sill: 1, Frame: "service-door")],
            [], new([1, 1, 1], 0.2f), []);
        BuiltFloor built = KitBuilder.Build(hatched, "hatch.json", content.Kit, content.Fixtures);
        RoomBox[] Solid(float x, float y, float z) => built.Boxes.Where(b => b.Solid && b.Min[0] <= x && b.Max[0] >= x &&
            b.Min[1] <= y && b.Max[1] >= y && b.Min[2] <= z && b.Max[2] >= z).ToArray();
        Check(Solid(2.95f, 1.3f, 1.5f).Length == 0 && Solid(3.05f, 1.3f, 1.5f).Length == 0, "the hatch cuts both sides of the wall");
        Check(Solid(2.95f, 0.5f, 1.5f).Any(b => b.Name == "pantry east sill"), "wall stands below the hatch's sill");
        Check(Solid(3.05f, 2f, 1.5f).Any(b => b.Name == "hall west lintel"), "a lintel closes the wall above the hatch");
        Check(built.Boxes.Any(b => b.Name == "pass sill" && b.Min[1] < 1 && b.Max[1] >= 1), "the hatch frame has a sill piece");
        Check(built.Openings["pass"] is { Bottom: 1, Height: 0.6f }, "the built opening records its sill and height");
        Console.WriteLine("Kit checks passed: per-side wall surfaces, door, open and hatch links, lintel, continuing trim, rooms and fixture lights.");
    }
}
