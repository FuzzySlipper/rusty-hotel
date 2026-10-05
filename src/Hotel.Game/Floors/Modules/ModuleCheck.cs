using System.Numerics;
using Hotel.Game.Content;
using Hotel.Game.Route;
using Hotel.Game.Scene;
using Hotel.Game.Scene.Kit;

namespace Hotel.Game.Floors.Modules;

/// <summary>
/// A module's isolated self-check. It realizes the module alone, with a porch outside each doorway, and walks its
/// floor on a fine planning grid: cells the player's body cannot stand in (any solid box between step height and head
/// height, inflated by the body radius) are blocked. Every doorway must reach every other, and every content socket
/// must be within reach of floor the doorways reach. Engine navigation confirms whole floors later; this catches a
/// module that is broken on its own.
/// </summary>
internal static class ModuleCheck
{
    private const float GridStep = 0.1f;
    private const float PorchDepth = 1.5f, PorchMargin = 0.2f;

    /// <summary>The module realized alone with porches, as the developer viewer and the self-check see it.</summary>
    internal static (BuiltFloor Floor, (PlacedDoorway Doorway, Vector2 Stand)[] Porches) Realize(ModuleDefinition module, string path,
        ModuleCatalog catalog, KitDefinition kit, FixtureCatalog fixtures, int turn, Vector2 corner = default)
    {
        PlacedModule placed = new("m", new(module, corner, turn));
        List<SpaceDefinition> porches = [];
        List<LinkDefinition> links = [];
        List<(PlacedDoorway, Vector2)> stands = [];
        foreach (PlacedDoorway doorway in ModuleRealizer.Doorways(placed, catalog))
        {
            if (doorway.Style is not { } style) { stands.Add((doorway, Inward(doorway, PorchMargin + 0.3f))); continue; }
            float half = style.Width / 2 + PorchMargin;
            Vector2 outward = Outward(doorway.Edge), across = new(MathF.Abs(outward.Y), MathF.Abs(outward.X));
            Vector2 a = doorway.Point - across * half, b = doorway.Point + across * half + outward * PorchDepth;
            string id = $"porch/{doorway.Doorway.Id}";
            SpaceDefinition inside = module.Spaces.First(s => s.Id == doorway.Doorway.Space);
            porches.Add(new(id, "", [Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)], [Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)], inside.Height, inside.Style));
            links.Add(ModuleRealizer.Link(doorway, id, $"porch-link/{doorway.Doorway.Id}"));
            stands.Add((doorway, doorway.Point + outward * (PorchDepth - 0.4f)));
        }
        FloorPlan plan = ModuleRealizer.Plan([placed], catalog, porches, links);
        return (KitBuilder.Build(plan, path, kit, fixtures), [.. stands]);
    }

    /// <summary>Throws naming the module file and the doorway or socket that fails.</summary>
    internal static void Require(ModuleDefinition module, string path, ModuleCatalog catalog, KitDefinition kit, FixtureCatalog fixtures,
        ModuleBody body, int turn)
    {
        (BuiltFloor floor, var porches) = Realize(module, path, catalog, kit, fixtures, turn);
        Authored.Require(porches.Length > 0, path, "doorways", "a module needs at least one doorway.");
        Grid grid = new(floor, body);
        bool[] reached = grid.Flood(porches[0].Stand);
        for (int i = 0; i < porches.Length; i++)
        {
            int index = Array.IndexOf(module.Doorways, porches[i].Doorway.Doorway);
            Authored.Require(grid.Reached(reached, porches[i].Stand), path, $"doorways[{index}]",
                $"'{porches[i].Doorway.Doorway.Id}' cannot be walked to from '{porches[0].Doorway.Doorway.Id}' (turn {turn}).");
        }
        for (int i = 0; i < module.Sockets.Length; i++)
        {
            ContentSocket socket = module.Sockets[i];
            Authored.Require(floor.Sockets.TryGetValue($"m/{socket.Socket}", out Vector3 point), path, $"sockets[{i}].socket",
                $"unknown socket '{socket.Socket}'.");
            bool ok = socket.Kind == ContentSocketKind.ResidentPost
                ? grid.Reached(reached, new(point.X, point.Z))
                : grid.WithinReach(reached, point);
            Authored.Require(ok, path, $"sockets[{i}]", socket.Kind == ContentSocketKind.ResidentPost
                ? $"'{socket.Id}' is not on floor the doorways reach (turn {turn})."
                : $"'{socket.Id}' is out of reach of any floor the doorways reach (turn {turn}).");
        }
    }

    private static Vector2 Outward(WallEdge edge) => edge switch
    {
        WallEdge.North => -Vector2.UnitY, WallEdge.South => Vector2.UnitY, WallEdge.West => -Vector2.UnitX, _ => Vector2.UnitX
    };

    private static Vector2 Inward(PlacedDoorway doorway, float distance) => doorway.Point - Outward(doorway.Edge) * distance;

    // Standing cells over the built floor's extent.
    private sealed class Grid
    {
        private readonly float minX, minZ;
        private readonly int width, depth;
        private readonly bool[] free;
        private readonly ModuleBody body;

        internal Grid(BuiltFloor floor, ModuleBody body)
        {
            this.body = body;
            RoomBox[] boxes = floor.Boxes;
            minX = boxes.Min(b => b.Min[0]); minZ = boxes.Min(b => b.Min[2]);
            width = (int)MathF.Ceiling((boxes.Max(b => b.Max[0]) - minX) / GridStep) + 1;
            depth = (int)MathF.Ceiling((boxes.Max(b => b.Max[2]) - minZ) / GridStep) + 1;
            free = new bool[width * depth];
            // Floor exists only inside rooms; walls and solid furniture in the body's height then block.
            // Rooms include their shared boundary, so a doorway's line belongs to both sides.
            const float edge = 1e-3f;
            foreach (RoomDefinition room in floor.Rooms) Mark(room.Min[0] - edge, room.Min[2] - edge, room.Max[0] + edge, room.Max[2] + edge, true);
            foreach (RoomBox box in boxes.Where(b => b.Solid && b.Max[1] > body.StepHeight && b.Min[1] < body.Height))
                Mark(box.Min[0] - body.Radius, box.Min[2] - body.Radius, box.Max[0] + body.Radius, box.Max[2] + body.Radius, false);
        }

        private void Mark(float x0, float z0, float x1, float z1, bool value)
        {
            for (int z = Math.Max(0, Cell(z0 - minZ)); z <= Math.Min(depth - 1, Cell(z1 - minZ)); z++)
                for (int x = Math.Max(0, Cell(x0 - minX)); x <= Math.Min(width - 1, Cell(x1 - minX)); x++)
                {
                    float px = minX + x * GridStep, pz = minZ + z * GridStep;
                    if (px > x0 && px < x1 && pz > z0 && pz < z1) free[z * width + x] = value;
                }
        }

        private static int Cell(float metres) => (int)MathF.Floor(metres / GridStep);

        private int Index(Vector2 p)
        {
            int x = (int)MathF.Round((p.X - minX) / GridStep), z = (int)MathF.Round((p.Y - minZ) / GridStep);
            return x < 0 || z < 0 || x >= width || z >= depth ? -1 : z * width + x;
        }

        internal bool[] Flood(Vector2 start)
        {
            bool[] reached = new bool[free.Length];
            int first = Index(start);
            if (first < 0 || !free[first]) return reached;
            Queue<int> open = new([first]);
            reached[first] = true;
            while (open.TryDequeue(out int at))
            {
                int x = at % width, z = at / width;
                foreach (int next in new[] { x > 0 ? at - 1 : -1, x < width - 1 ? at + 1 : -1, z > 0 ? at - width : -1, z < depth - 1 ? at + width : -1 })
                    if (next >= 0 && free[next] && !reached[next]) { reached[next] = true; open.Enqueue(next); }
            }
            return reached;
        }

        internal string Map(bool[] reached)
        {
            var b = new System.Text.StringBuilder();
            for (int z = 0; z < depth; z++) { for (int x = 0; x < width; x++) b.Append(reached[z * width + x] ? 'o' : free[z * width + x] ? '.' : '#'); b.Append('\n'); }
            return b.ToString();
        }

        internal bool Reached(bool[] reached, Vector2 p) => Index(p) is var i and >= 0 && reached[i];

        // Some reached cell lets the player's eye come within interaction reach of the point.
        internal bool WithinReach(bool[] reached, Vector3 point)
        {
            for (int i = 0; i < reached.Length; i++)
            {
                if (!reached[i]) continue;
                Vector3 eye = new(minX + i % width * GridStep, body.EyeHeight, minZ + i / width * GridStep);
                if (Vector3.Distance(eye, point) <= body.Reach) return true;
            }
            return false;
        }
    }
}
