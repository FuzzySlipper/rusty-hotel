using Hotel.Game.Audio;
using Hotel.Game.Combat;
using Hotel.Game.Expedition;
using Hotel.Game.Interface;
using Hotel.Game.Player;
using Hotel.Game.Route;
using Hotel.Game.Scene;
using Hotel.Game.Spirits;
using Hotel.Game.Supplies;
using Rusty.Engine;

namespace Hotel.Game.Content;

/// <summary>
/// Every authored definition the product composes, loaded file by file through each domain's own record.
/// Only composition reads this; each owner receives the pieces it uses.
/// </summary>
internal sealed record HotelContent(PlayerTuning Player, RouteDefinition Route, InterfaceTuning Interface,
    SurfaceDefinition[] Surfaces, SuppliesDefinition Supplies, CombatDefinition Combat, SpiritDefinition Spirit,
    SpiritMessages SpiritText, ExpeditionMessages ExpeditionText, ExcursionDefinition Excursion)
{
    internal static HotelContent Load(IEngineContext engine, string excursionId)
    {
        ExcursionDefinition excursion = ExcursionDefinition.Load(engine, excursionId);
        // The product implements one pact; its bell placement names which spirit file to read.
        Authored.Require(excursion.Placements.SpiritBells.Length == 1, excursion.PlacementsPath, "spiritBells",
            "exactly one spirit bell is supported.");
        HotelContent content = new(PlayerTuning.Load(engine), RouteDefinition.Load(engine), InterfaceTuning.Load(engine),
            SurfaceCatalog.Load(engine).Surfaces, SuppliesDefinition.Load(engine), CombatDefinition.Load(engine),
            SpiritDefinition.Load(engine, excursion.Placements.SpiritBells[0].Spirit), SpiritMessages.Load(engine),
            ExpeditionMessages.Load(engine), excursion);
        content.Validate();
        return content;
    }

    internal SpiritBellPlacement SpiritBell => Excursion.Placements.SpiritBells[0];

    // References between files. Each file's own shape is checked by its JSON contract.
    private void Validate()
    {
        Unique(Surfaces.Select(s => s.Id), SurfaceCatalog.Path, "surfaces");
        Unique(Supplies.Items.Select(i => i.Id), ItemCatalog.Path, "items");
        Unique(Combat.Weapons.Select(w => w.Id), WeaponCatalog.Path, "weapons");
        Unique(Combat.Residents.Select(r => r.Id), ResidentCatalog.Path, "residents");

        string geometry = Excursion.GeometryPath, route = Excursion.RoutePath, placements = Excursion.PlacementsPath;
        ExcursionPlacements placed = Excursion.Placements;
        Unique(placed.Finds.Select(f => f.Id), placements, "finds");
        Unique(placed.Residents.Select(r => r.Id), placements, "residents");
        Unique(Excursion.Route.Doors.Select(d => d.Id), route, "doors");
        Unique(Excursion.Route.Readings.Select(r => r.Id), route, "readings");
        for (int i = 0; i < Excursion.Geometry.Boxes.Length; i++)
        {
            RoomBox box = Excursion.Geometry.Boxes[i];
            Authored.Require(Surfaces.Any(s => s.Id == box.Material), geometry, $"boxes[{i}].material", $"unknown surface '{box.Material}'.");
            Authored.Require(box.Find is null || placed.Finds.Any(f => f.Id == box.Find), geometry, $"boxes[{i}].find", $"unknown find '{box.Find}'.");
        }
        for (int i = 0; i < Excursion.Route.Doors.Length; i++)
        {
            DoorDefinition door = Excursion.Route.Doors[i];
            Authored.Require(Surfaces.Any(s => s.Id == door.Material), route, $"doors[{i}].material", $"unknown surface '{door.Material}'.");
            Authored.Require(Surfaces.Any(s => s.Id == door.HandleMaterial), route, $"doors[{i}].handleMaterial", $"unknown surface '{door.HandleMaterial}'.");
            Authored.Require(!door.FarSideLatch || door.UnlockDirection is not null, route, $"doors[{i}].unlockDirection",
                "a far-side latch needs the direction it unlocks from.");
            Authored.Require(!door.FarSideLatch || door.LockedPrompt is not null, route, $"doors[{i}].lockedPrompt",
                "a far-side latch needs the prompt shown from the locked side.");
        }
        for (int i = 0; i < placed.Finds.Length; i++)
            Authored.Require(Supplies.Items.Any(item => item.Id == placed.Finds[i].Item), placements, $"finds[{i}].item", $"unknown item '{placed.Finds[i].Item}'.");
        for (int i = 0; i < placed.Residents.Length; i++)
            Authored.Require(Combat.Residents.Any(kind => kind.Id == placed.Residents[i].Kind), placements, $"residents[{i}].kind",
                $"unknown resident kind '{placed.Residents[i].Kind}'.");
    }

    private static void Unique(IEnumerable<string> ids, string path, string field)
    {
        string? repeated = ids.GroupBy(id => id, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1)?.Key;
        Authored.Require(repeated is null, path, field, $"id '{repeated}' appears more than once.");
    }
}

/// <summary>One authored hotel section: its geometry, route, placements and ambience, each in its own file.</summary>
internal sealed record ExcursionDefinition(string Id, ExcursionGeometry Geometry, ExcursionRoute Route,
    ExcursionPlacements Placements, AmbientDefinition[] Ambience)
{
    internal string GeometryPath => Folder(Id) + "geometry.json";
    internal string RoutePath => Folder(Id) + "route.json";
    internal string PlacementsPath => Folder(Id) + "placements.json";

    internal static ExcursionDefinition Load(IEngineContext engine, string id) => new(id,
        Authored.Read(engine, Folder(id) + "geometry.json", ContentJson.Default.ExcursionGeometry),
        Authored.Read(engine, Folder(id) + "route.json", ContentJson.Default.ExcursionRoute),
        Authored.Read(engine, Folder(id) + "placements.json", ContentJson.Default.ExcursionPlacements),
        Authored.Read(engine, Folder(id) + "ambience.json", ContentJson.Default.AmbienceDefinition).Voices);

    private static string Folder(string id) => $"excursions/{id}/";
}

/// <summary>Where one excursion puts the player, refuge, finds, residents and spirit bells.</summary>
internal sealed record ExcursionPlacements(ArrivalPlacement Arrival, RefugeDefinition Refuge, FindDefinition[] Finds,
    ResidentPlacement[] Residents, SpiritBellPlacement[] SpiritBells);
