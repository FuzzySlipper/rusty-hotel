using System.Numerics;
using Hotel.Game.Combat;
using Hotel.Game.Content;
using Hotel.Game.Floors.Content;
using Hotel.Game.Floors.Layout;
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
        DoorTuning doors = tunings.Content.Doors;
        PlacedFind[] shown = floor.Content.Finds.Where(f => f.Item is not null).ToArray();
        PlacedFind[] keyFinds = floor.Content.Finds.Where(f => f.Role == FindRole.Key).ToArray();
        FloorPlan plan = floor.Plan with
        {
            Fixtures = [.. floor.Plan.Fixtures,
                .. shown.Select(f => new FixturePlacement(tunings.Content.Display(f.Item!), Id: Display(f), On: f.Socket, Find: Scoped(f.Id))),
                .. keyFinds.Select(f => new FixturePlacement(doors.KeyFixture, Id: Display(f), On: f.Socket, Find: Scoped(f.Id)))]
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
        // A lock's door opens into the space behind it, the side in the region its edge guards; its key is named for that room.
        Dictionary<string, LinkDefinition> links = plan.Links.ToDictionary(l => l.Id, StringComparer.Ordinal);
        string Guarded(LayoutLock lck) => KeptSet.Guarded(floor, lck);
        string RoomName(string space) => plan.Spaces.First(s => s.Id == space).Label.ToLowerInvariant();
        DoorDefinition[] lockDoors = floor.Layout.Locks.Select((lck, i) =>
        {
            string guarded = Guarded(lck);
            string key = Template.Fill(doors.KeyName, ("room", RoomName(guarded)));
            return new DoorPlacement(KeptSet.LockDoor(Id(depth), lck), doors.LockedLabel, lck.Link, DoorHinge.Start, guarded, doors.Material, doors.HandleMaterial,
                LockedPrompt: Template.Fill(doors.LockedPrompt, ("key", key))).Resolve($"generated {Id(depth)}", $"locks[{i}]", built, sources.Kit.DoorLeaf) with { Key = lck.Item };
        }).ToArray();
        DoorDefinition[] latch = floor.Layout.Latch is { } latchLink
            ? [new DoorPlacement(Scoped("latch"), doors.LatchLabel, latchLink, DoorHinge.Start, links[latchLink].Between[0], doors.Material, doors.HandleMaterial,
                LatchedFrom: links[latchLink].Between[1], LockedPrompt: doors.LatchPrompt).Resolve($"generated {Id(depth)}", "latch", built, sources.Kit.DoorLeaf)]
            : [];
        // Doors a shift kept hang again under their old ids: unlocked once opened, still locked to a key the player holds.
        DoorDefinition[] keptDoors = floor.Layout.KeptDoors.Select((d, i) => new DoorPlacement(d.Id, doors.LockedLabel, d.Link, DoorHinge.Start,
            d.Locked?.Guards ?? links[d.Link].Between[1], doors.Material, doors.HandleMaterial,
            LockedPrompt: d.Locked is { } held ? Template.Fill(doors.LockedPrompt, ("key", Template.Fill(doors.KeyName, ("room", RoomName(held.Guards))))) : null)
            .Resolve($"generated {Id(depth)}", $"keptDoors[{i}]", built, sources.Kit.DoorLeaf) with { Key = d.Locked?.Item }).ToArray();
        KeyDefinition[] keys = keyFinds.Select(f =>
        {
            LayoutLock lck = floor.Layout.Locks.First(l => l.Item == f.Grants);
            return new KeyDefinition(Scoped(f.Id), f.Grants!, Template.Fill(doors.KeyName, ("room", RoomName(Guarded(lck)))), At(built.Sockets[$"{Display(f)}.focus"]));
        }).ToArray();
        ExcursionRoute route = new(tunings.Content.FallbackLocation, [.. lockDoors, .. latch, .. keptDoors], readings, built.Rooms, stairs, keys);

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
