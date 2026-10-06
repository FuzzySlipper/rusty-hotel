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
        // Profiled trim is a moulding run along the wall face rather than a box.
        Check(content.Excursion.Geometry.Mouldings.Any(m => m.Name.Contains("picture rail") && Math.Min(m.Start[2], m.End[2]) <= -5 &&
            Math.Max(m.Start[2], m.End[2]) >= -5 && Math.Abs(m.Start[0] + 1.8f) < .01f && m.Outward[0] > 0), "the picture rail continues over the door");
        Check(At(0, 1.2f, 0.95f).Length == 0, "the open refuge link removes the shared wall");
        Check(content.Excursion.Route.Rooms.Any(r => r.Id == "corridor" && r.Label == "West wing corridor"), "spaces are the named rooms");
        Check(content.Excursion.Geometry.Lighting.Points.Length == 23, "fixture lights are the floor's point lights");

        // A hatch is a typed raised opening: wall and skirting run below its sill, a lintel closes above it, and a
        // frame gets a sill piece.
        FloorPlan hatched = new([new("pantry", "Pantry", [0, 0], [3, 3], 2.6f, "service"), new("hall", "Hall", [3, 0], [6, 3], 2.6f, "corridor")],
            [new("pass", ["pantry", "hall"], LinkKind.Hatch, At: 1.5f, Width: 1, Height: 0.6f, Sill: 1, Frame: "service-door")],
            [], new([1, 1, 1], 0.2f), [], "hotel");
        BuiltFloor built = KitBuilder.Build(hatched, "hatch.json", content.Kit, content.Fixtures);
        RoomBox[] Solid(float x, float y, float z) => built.Boxes.Where(b => b.Solid && b.Min[0] <= x && b.Max[0] >= x &&
            b.Min[1] <= y && b.Max[1] >= y && b.Min[2] <= z && b.Max[2] >= z).ToArray();
        Check(Solid(2.95f, 1.3f, 1.5f).Length == 0 && Solid(3.05f, 1.3f, 1.5f).Length == 0, "the hatch cuts both sides of the wall");
        Check(Solid(2.95f, 0.5f, 1.5f).Any(b => b.Name == "pantry east sill"), "wall stands below the hatch's sill");
        Check(Solid(3.05f, 2f, 1.5f).Any(b => b.Name == "hall west lintel"), "a lintel closes the wall above the hatch");
        Check(built.Boxes.Any(b => b.Name == "pass sill" && b.Min[1] < 1 && b.Max[1] >= 1), "the hatch frame has a sill piece");
        Check(built.Openings["pass"] is { Bottom: 1, Height: 0.6f }, "the built opening records its sill and height");

        // Trim styles: the same framed plan dressed in each style takes that style's bands and architraves.
        FloorPlan framed = new([new("room", "Room", [0, 0], [4, 4], 2.65f, "guest-room"), new("hall", "Hall", [4, 0], [8, 4], 2.65f, "corridor")],
            [new("door", ["room", "hall"], LinkKind.Door, At: 2, Width: 0.9f, Height: 2.05f, Frame: "guest-door")], [], new([1, 1, 1], 0.2f), [], "hotel");
        string[] Mouldings(string style) => KitBuilder.Build(framed with { TrimStyle = style }, "framed.json", content.Kit, content.Fixtures)
            .Mouldings.Select(m => m.Name.Split(' ', 2)[1]).Distinct().Order().ToArray();
        string[] hotel = Mouldings("hotel"), deco = Mouldings("deco");
        Check(!hotel.SequenceEqual(deco) && hotel.Any(n => n.EndsWith("picture rail")) && deco.Any(n => n.EndsWith("chair rail")),
            $"two trim styles dress the same floor differently: {string.Join(", ", hotel)} / {string.Join(", ", deco)}");
        Check(hotel.Contains("architrave") && hotel.Contains("architrave head") && deco.Contains("architrave"), "a framed door takes its style's architrave on both faces");
        bool unknown = false;
        try { KitBuilder.Build(framed with { TrimStyle = "no-such-style" }, "framed.json", content.Kit, content.Fixtures); }
        catch (InvalidOperationException e) { unknown = e.Message.Contains("framed.json trimStyle"); }
        Check(unknown, "an unknown trim style is refused naming the plan's field");
        int sconces = content.Excursion.Plan.Fixtures.Count(f => f.Kind == "sconce");
        Check(content.Excursion.Geometry.Models.Count(m => m.Path == "models/sconce.glb") == sconces && sconces > 0, "every sconce shows its fitting model");
        // Furniture is a model with hidden collider boxes: it blocks like the boxes it replaced and draws only the model.
        RoomBox[] colliders = boxes.Where(b => b.Hidden).ToArray();
        Check(colliders.Length > 0 && colliders.All(b => b.Solid) && content.Excursion.Geometry.Models.Any(m => m.Path.StartsWith("models/furniture/")),
            "furniture models keep solid, undrawn collider boxes");
        Console.WriteLine("Kit checks passed: per-side wall surfaces, door, open and hatch links, lintel, continuing trim, trim styles with mouldings and architraves, fixture models, furniture colliders, rooms and fixture lights.");
    }
}
