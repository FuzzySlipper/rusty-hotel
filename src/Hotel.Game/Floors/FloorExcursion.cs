using System.Numerics;
using Hotel.Game.Combat;
using Hotel.Game.Content;
using Hotel.Game.Floors.Content;
using Hotel.Game.Floors.Mission;
using Hotel.Game.Floors.Modules;
using Hotel.Game.Player;
using Hotel.Game.Route;
using Hotel.Game.Scene;
using Hotel.Game.Scene.Kit;
using Hotel.Game.Spirits;
using Hotel.Game.Supplies;

namespace Hotel.Game.Floors;

/// <summary>
/// A generated floor as the same <see cref="ExcursionDefinition"/> authored floors load, so every owner plays it
/// unchanged. Each find is shown by its item's display fixture on its socket, and its id, like a resident's or a
/// notice's, is namespaced by the floor's depth so it cannot collide with another floor's.
/// </summary>
internal static class FloorExcursion
{
    internal static string Id(int depth) => $"floor-{depth}";

    internal static ExcursionDefinition From(GeneratedFloor floor, FloorTunings tunings, FloorSources sources, PlayerTuning player)
    {
        int depth = floor.Identity.Seed.Depth;
        string Scoped(string id) => $"{Id(depth)}/{id}";
        PlacedFind[] shown = floor.Content.Finds.Where(f => f.Item is not null).ToArray();
        FloorPlan plan = floor.Plan with
        {
            Fixtures = [.. floor.Plan.Fixtures, .. shown.Select(f => new FixturePlacement(tunings.Content.Display(f.Item!), Id: Display(f), On: f.Socket, Find: Scoped(f.Id)))]
        };
        BuiltFloor built = KitBuilder.Build(plan, $"generated {Id(depth)}", sources.Kit, sources.Fixtures);
        float[] At(Vector3 p) => [p.X, p.Y, p.Z];
        float[] Standing(string socket, float height) { Vector3 p = built.Sockets[socket]; return [p.X, p.Y + height / 2, p.Z]; }

        string landing = floor.Layout.Places[MissionGraph.ArrivalId];
        ModuleDefinition core = sources.Modules.Find(floor.Layout.Placements.First(p => p.Id == landing).Module)!;
        StairDefinition[] stairs = core.Sockets.Where(s => s.Kind is ContentSocketKind.StairUp or ContentSocketKind.StairDown)
            .Select(s => new StairDefinition(Scoped($"{landing}/{s.Id}"), s.Kind == ContentSocketKind.StairUp ? StairDirection.Up : StairDirection.Down,
                At(built.Sockets[$"{landing}/{s.Socket}"]))).ToArray();
        ReadingDefinition[] readings = floor.Content.Readings.Select(r =>
        {
            FloorReading text = tunings.Readings.Readings.First(t => t.Id == r.Reading);
            return new ReadingDefinition(Scoped(r.Id), text.Label, At(built.Sockets[r.Socket]), text.Title, text.Text);
        }).ToArray();
        ExcursionRoute route = new(tunings.Content.FallbackLocation, [], readings, built.Rooms, stairs);

        ArrivalPlacement arrival = new(Standing(floor.Content.Arrival, player.Height), floor.Content.ArrivalYaw);
        FindDefinition[] finds = shown.Select(f => new FindDefinition(Scoped(f.Id), f.Item!, f.Count, At(built.Sockets[$"{Display(f)}.focus"]))).ToArray();
        ResidentPlacement[] residents = floor.Content.Residents.Select(r =>
            new ResidentPlacement(Scoped(r.Id), r.Kind, Standing(r.Socket, sources.Residents.First(k => k.Id == r.Kind).Height))).ToArray();
        SpiritBellPlacement[] bells = floor.Content.Bell is { } bell ? [new(bell.Spirit, At(built.Sockets[bell.Socket]), bell.Place)] : [];
        ExcursionPlacements placements = new(arrival, null, finds, residents, bells, arrival);

        LightingDefinition lighting = new(sources.Modules.Lighting.AmbientColor, sources.Modules.Lighting.AmbientIntensity, built.Lights);
        return new(Id(depth), plan, new(built.Boxes, [], lighting), route, placements, []);
    }

    // The display fixture's own id: the find's, marked so it cannot be mistaken for a module fixture.
    private static string Display(PlacedFind find) => $"{find.Id}#shown";
}
