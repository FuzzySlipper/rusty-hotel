using System.Numerics;
using Hotel.Game.Floors.Modules;
using Hotel.Game.Scene.Kit;
using Rusty.Engine;

// Room modules pass their isolated self-check at every quarter turn, keep their doorways on their turned outer
// wall, and mate doorway to doorway into one built floor.
internal static class ModuleChecks
{
    internal static void Run(IEngineContext engine)
    {
        void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
        var content = Owners.Content(engine);
        ModuleCatalog catalog = content.Modules;
        ModuleBody body = ModuleBody.Of(content.Player, content.Route.Interaction);
        foreach (ModuleDefinition module in catalog.Modules)
            for (int turn = 0; turn < 4; turn++)
            {
                ModuleCheck.Require(module, ModuleCatalog.ModulePath(module.Id), catalog, content.Kit, content.Fixtures, body, turn);
                ModuleTransform t = new(module, new(10, 20), turn);
                PlacedModule placed = new("p", t);
                FloorPlan plan = ModuleRealizer.Plan([placed], catalog, catalog.TrimStyle);
                float minX = plan.Spaces.Min(s => s.Min[0]), minZ = plan.Spaces.Min(s => s.Min[1]);
                float maxX = plan.Spaces.Max(s => s.Max[0]), maxZ = plan.Spaces.Max(s => s.Max[1]);
                Check(MathF.Abs(minX - 10) < 1e-3f && MathF.Abs(minZ - 20) < 1e-3f && MathF.Abs(maxX - 10 - t.Footprint.X) < 1e-3f &&
                    MathF.Abs(maxZ - 20 - t.Footprint.Y) < 1e-3f, $"{module.Id} turned {turn} fills its turned footprint from its corner");
                foreach (PlacedDoorway d in ModuleRealizer.Doorways(placed, catalog))
                {
                    float line = d.Edge switch { WallEdge.North => 20, WallEdge.South => 20 + t.Footprint.Y, WallEdge.West => 10, _ => 10 + t.Footprint.X };
                    Check(MathF.Abs((d.AlongX ? d.Point.Y : d.Point.X) - line) < 1e-3f, $"{module.Id} doorway {d.Doorway.Id} turned {turn} stays on its outer wall");
                }
            }

        // A guest room turned to face west mates its door with the corridor's east door: one link, built as a door.
        ModuleDefinition corridor = catalog.Find("corridor-straight")!, room = catalog.Find("guest-room-a")!;
        PlacedModule hall = new("c", new(corridor, Vector2.Zero, 0));
        PlacedDoorway east = ModuleRealizer.Doorways(hall, catalog).Single(d => d.Doorway.Id == "east");
        PlacedModule guest = new("g", new(room, new(corridor.Width, east.Point.Y - 2), 1));
        PlacedDoorway door = ModuleRealizer.Doorways(guest, catalog).Single();
        Check(door.Mates(east) && east.Mates(door), $"the turned room's door meets the corridor's east door ({door.Point} / {east.Point}, {door.Edge})");
        BuiltFloor built = KitBuilder.Build(ModuleRealizer.Plan([hall, guest], catalog, catalog.TrimStyle), "mated", content.Kit, content.Fixtures);
        Check(built.Openings.Values.Count(o => o.Link.Kind == LinkKind.Door) == 1 && built.Sockets.ContainsKey("g/table.top"),
            "mated doorways build one door between the modules, and the room's sockets are addressable under its placement");
        Console.WriteLine($"Module checks passed: {catalog.Modules.Length} modules self-check at every quarter turn, fill their turned footprints, keep doorways on their outer walls, and mate into one floor.");
    }
}
