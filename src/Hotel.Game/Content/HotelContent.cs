using Hotel.Game.Residents;
using Hotel.Game.Audio;
using Hotel.Game.Combat;
using Hotel.Game.Expedition;
using Hotel.Game.Floors.Modules;
using Hotel.Game.Input;
using Hotel.Game.Interface;
using Hotel.Game.Mechanics;
using Hotel.Game.Player;
using Hotel.Game.Route;
using Hotel.Game.Scene;
using Hotel.Game.Scene.Kit;
using Hotel.Game.Spirits;
using Hotel.Game.Supplies;
using Rusty.Engine;

namespace Hotel.Game.Content;

/// <summary>
/// Every authored definition the product composes, loaded file by file through each domain's own record.
/// Only composition reads this; each owner receives the pieces it uses.
/// </summary>
internal sealed record HotelContent(ControlBindings Controls, PlayerTuning Player, MechanicsDefinition Mechanics, ActorStatBlock PlayerStats,
    RouteDefinition Route, InterfaceTuning Interface,
    SurfaceDefinition[] Surfaces, AgingCatalog Aging, SceneLook Look, KitDefinition Kit, FixtureCatalog Fixtures, ModuleCatalog Modules, SuppliesDefinition Supplies, Hotel.Game.Loot.LootCatalog Loot, Hotel.Game.Progression.GrowthDefinition Growth, CombatDefinition Combat, SpiritDefinition[] Spirits,
    SpiritMessages SpiritText, ExpeditionMessages ExpeditionText, ExcursionDefinition Excursion, TitleDefinition Title)
{
    internal static HotelContent Load(IEngineContext engine, string excursionId)
    {
        ControlBindings controls = ControlBindings.Load(engine);
        IReadOnlyDictionary<string, string> keys = controls.Labels;
        KitDefinition kit = KitDefinition.Load(engine);
        FixtureCatalog fixtures = FixtureCatalog.Load(engine);
        MechanicsDefinition mechanics = MechanicsDefinition.Load(engine);
        SuppliesDefinition supplies = SuppliesDefinition.Load(engine, mechanics);
        Actions.ActionCatalog actions = Actions.ActionCatalog.Load(engine, mechanics, supplies.Classifications.Select(c => c.Id).ToArray());
        supplies.ValidateActions(actions, mechanics);
        Hotel.Game.Loot.LootCatalog loot = Hotel.Game.Loot.LootCatalog.Load(engine, supplies, mechanics);
        Hotel.Game.Progression.GrowthDefinition growth = Hotel.Game.Progression.GrowthDefinition.Load(engine, mechanics, actions, supplies);
        SurfaceDefinition[] surfaces = SurfaceCatalog.Load(engine).Surfaces;
        CombatDefinition combat = CombatDefinition.Load(engine, keys, mechanics, actions, surfaces.Select(s => s.Id).ToArray(), loot);
        ExcursionDefinition excursion = ExcursionDefinition.Load(engine, excursionId, keys, kit, fixtures, combat.Residents);
        PlayerTuning player = PlayerTuning.Load(engine);
        RouteDefinition route = RouteDefinition.Load(engine, keys);
        ModuleCatalog modules = ModuleCatalog.Load(engine, kit, fixtures, ModuleBody.Of(player, route.Interaction));
        HotelContent content = new(controls, player, mechanics, Hotel.Game.Player.PlayerStats.Load(engine, mechanics), route, InterfaceTuning.Load(engine),
            surfaces, AgingCatalog.Load(engine), SceneLook.Load(engine), kit, fixtures, modules, supplies, loot, growth, combat,
            SpiritRoster.Load(engine, keys, mechanics, actions, surfaces.Select(s => s.Id).ToArray()), SpiritMessages.Load(engine, keys),
            ExpeditionMessages.Load(engine), excursion, TitleDefinition.Load(engine));
        content.Validate();
        return content;
    }

    internal SpiritDefinition Spirit(string id) => Spirits.First(s => s.Id == id);

    // References between files. Each file's own shape is checked by its JSON contract.
    private void Validate()
    {
        Unique(Surfaces.Select(s => s.Id), SurfaceCatalog.Path, "surfaces");
        for (int i = 0; i < Surfaces.Length; i++)
            if (Surfaces[i].Aging is string aging)
                Authored.Require(Aging.Profiles.ContainsKey(aging), SurfaceCatalog.Path, $"surfaces[{i}].aging", $"unknown profile '{aging}' in {AgingCatalog.Path}.");
        Authored.Require(Controls.QuickPockets.Length == Interface.QuickPockets && Interface.QuickPockets > 0, ControlBindings.Path, "quickPockets",
            $"one binding per quick pocket in {InterfaceTuning.Path} ({Interface.QuickPockets}).");
        Unique(Supplies.Items.Select(i => i.Id), ItemCatalog.Path, "items");
        // Every held item shows a look the hand can draw.
        for (int i = 0; i < Supplies.Items.Length; i++)
            if (Supplies.Items[i].Wear?.Look is string look)
                Authored.Require(Combat.Held.Looks.Any(l => l.Look == look), ItemCatalog.Path, $"items[{i}].wear.look",
                    $"unknown look '{look}' in {Hotel.Game.Combat.HeldCatalog.Path}.");
        Unique(Combat.Residents.Select(r => r.Id), ResidentCatalog.Path, "residents");

        string plan = Excursion.PlanPath, route = Excursion.RoutePath, placements = Excursion.PlacementsPath;
        ExcursionPlacements placed = Excursion.Placements;
        Unique(placed.Finds.Select(f => f.Id), placements, "finds");
        Unique(placed.Residents.Select(r => r.Id), placements, "residents");
        Unique(Excursion.Route.Doors.Select(d => d.Id), route, "doors");
        Unique(Excursion.Route.Readings.Select(r => r.Id), route, "readings");
        // Every surface named by the kit, a fixture or a space override exists, so every built box's does.
        void Surface(string path, string field, string id) =>
            Authored.Require(Surfaces.Any(s => s.Id == id), path, field, $"unknown surface '{id}'.");
        foreach (var (field, id) in Kit.Surfaces()) Surface(KitDefinition.Path, field, id);
        for (int i = 0; i < Fixtures.Fixtures.Length; i++)
            for (int p = 0; p < Fixtures.Fixtures[i].Parts.Length; p++)
                Surface(FixtureCatalog.Path, $"fixtures[{i}].parts[{p}].material", Fixtures.Fixtures[i].Parts[p].Material);
        foreach (ModuleDefinition module in Modules.Modules)
            for (int i = 0; i < module.Spaces.Length; i++)
                foreach (var (field, id) in new[] { ("floor", module.Spaces[i].Floor), ("wall", module.Spaces[i].Wall), ("ceiling", module.Spaces[i].Ceiling) })
                    if (id is not null) Surface(ModuleCatalog.ModulePath(module.Id), $"spaces[{i}].{field}", id);
        for (int i = 0; i < Excursion.Plan.Spaces.Length; i++)
        {
            SpaceDefinition space = Excursion.Plan.Spaces[i];
            if (space.Floor is { } floor) Surface(plan, $"spaces[{i}].floor", floor);
            if (space.Wall is { } wall) Surface(plan, $"spaces[{i}].wall", wall);
            if (space.Ceiling is { } ceiling) Surface(plan, $"spaces[{i}].ceiling", ceiling);
            Template.Plain(plan, ($"spaces[{i}].label", space.Label));
        }
        for (int i = 0; i < Excursion.Plan.Fixtures.Length; i++)
            if (Excursion.Plan.Fixtures[i].Find is { } find)
                Authored.Require(placed.Finds.Any(f => f.Id == find), plan, $"fixtures[{i}].find", $"unknown find '{find}'.");
        for (int i = 0; i < placed.Finds.Length; i++)
            Authored.Require(Excursion.Plan.Fixtures.Any(f => f.Find == placed.Finds[i].Id), placements, $"finds[{i}]",
                $"no fixture in {plan} shows '{placed.Finds[i].Id}'.");
        for (int i = 0; i < Excursion.Route.Doors.Length; i++)
        {
            DoorDefinition door = Excursion.Route.Doors[i];
            Surface(route, $"doors[{i}].material", door.Material);
            Surface(route, $"doors[{i}].handleMaterial", door.HandleMaterial);
            Authored.Require(!door.FarSideLatch || door.LockedPrompt is not null, route, $"doors[{i}].lockedPrompt",
                "a latched door needs the prompt shown from the locked side.");
        }
        Template.Plain(route, ("fallbackLocation", Excursion.Route.FallbackLocation));
        for (int i = 0; i < Excursion.Route.Doors.Length; i++)
            Template.Plain(route, ($"doors[{i}].label", Excursion.Route.Doors[i].Label), ($"doors[{i}].lockedPrompt", Excursion.Route.Doors[i].LockedPrompt ?? ""));
        for (int i = 0; i < Excursion.Route.Readings.Length; i++)
            Template.Plain(route, ($"readings[{i}].label", Excursion.Route.Readings[i].Label),
                ($"readings[{i}].title", Excursion.Route.Readings[i].Title), ($"readings[{i}].text", Excursion.Route.Readings[i].Text));
        for (int i = 0; i < placed.SpiritBells.Length; i++)
        {
            Template.Plain(placements, ($"spiritBells[{i}].place", placed.SpiritBells[i].Place));
            Authored.Require(Spirits.Any(s => s.Id == placed.SpiritBells[i].Spirit), placements, $"spiritBells[{i}].spirit",
                $"'{placed.SpiritBells[i].Spirit}' is not in content/{SpiritRoster.Path}.");
        }
        for (int i = 0; i < placed.Finds.Length; i++)
        {
            ItemDefinition? item = Supplies.Items.FirstOrDefault(item => item.Id == placed.Finds[i].Item);
            Authored.Require(item is not null, placements, $"finds[{i}].item", $"unknown item '{placed.Finds[i].Item}'.");
            // A find is admitted whole, so it must fit in an empty field case.
            Authored.Require(placed.Finds[i].Count <= item!.StackLimit * Interface.SupplyPockets, placements, $"finds[{i}].count",
                $"{placed.Finds[i].Count} exceeds what an empty field case holds ({item.StackLimit} per pocket × {Interface.SupplyPockets}).");
        }
    }

    private static void Unique(IEnumerable<string> ids, string path, string field)
    {
        string? repeated = ids.GroupBy(id => id, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1)?.Key;
        Authored.Require(repeated is null, path, field, $"id '{repeated}' appears more than once.");
    }
}

/// <summary>
/// One authored hotel section: its floor plan (built by the kit), route, placements and ambience, each in its own
/// file. Sockets in the built floor give the route and placements their points.
/// </summary>
internal sealed record ExcursionDefinition(string Id, FloorPlan Plan, ExcursionGeometry Geometry, ExcursionRoute Route,
    ExcursionPlacements Placements, AmbientDefinition[] Ambience)
{
    internal string PlanPath => Folder(Id) + "plan.json";
    internal string RoutePath => Folder(Id) + "route.json";
    internal string PlacementsPath => Folder(Id) + "placements.json";

    internal static ExcursionDefinition Load(IEngineContext engine, string id, IReadOnlyDictionary<string, string> keys,
        KitDefinition kit, FixtureCatalog fixtures, ResidentKind[] residents)
    {
        string folder = Folder(id);
        FloorPlan plan = Authored.Read(engine, folder + "plan.json", ContentJson.Default.FloorPlan);
        Authored.Colour(folder + "plan.json", "lighting.ambientColor", plan.Lighting.AmbientColor);
        Authored.AtLeast(folder + "plan.json", "lighting.ambientIntensity", plan.Lighting.AmbientIntensity, 0);
        BuiltFloor floor = KitBuilder.Build(plan, folder + "plan.json", kit, fixtures);
        ExcursionGeometry geometry = new(floor.Boxes, floor.Mouldings, [.. plan.Models, .. floor.Models],
            new(plan.Lighting.AmbientColor, plan.Lighting.AmbientIntensity, floor.Lights));
        // The kit's own output is checked like authored geometry, so a builder fault cannot reach the scene.
        geometry.Validate(folder + "plan.json (built)");
        ExcursionRoute route = Authored.Read(engine, folder + "route.json", ContentJson.Default.RoutePlan, keys)
            .Resolve(folder + "route.json", floor, kit.DoorLeaf);
        ExcursionPlacements placements = Authored.Read(engine, folder + "placements.json", ContentJson.Default.PlacementPlan)
            .Resolve(folder + "placements.json", floor, residents);
        AmbienceDefinition ambience = Authored.Read(engine, folder + "ambience.json", ContentJson.Default.AmbienceDefinition);
        ambience.Validate(folder + "ambience.json");
        return new(id, plan, geometry, route, placements, ambience.Voices);
    }

    private static string Folder(string id) => $"excursions/{id}/";
}

/// <summary>Where one excursion puts the player, refuge, finds, residents and spirit bells, resolved to points.</summary>
/// <param name="Refuge">The notebook, on the floor that has the refuge; none on generated floors.</param>
/// <param name="FromAbove">Where the player stands after coming down the stairs to this floor.</param>
/// <param name="Containers">Things to search once for what their loot table gives.</param>
internal sealed record ExcursionPlacements(ArrivalPlacement Arrival, RefugeDefinition? Refuge, FindDefinition[] Finds,
    ResidentPlacement[] Residents, SpiritBellPlacement[] SpiritBells, ArrivalPlacement FromAbove, ContainerPlacement[] Containers);

/// <summary>A container in one excursion: its saved identity, how it is named, its loot table and where it is searched.</summary>
internal sealed record ContainerPlacement(string Id, string Name, string Table, float[] Point);

/// <summary>
/// The authored placements. The refuge notebook, finds and bells sit at fixture sockets in the floor plan, and
/// residents stand on posts; the arrival point is a body centre in the world.
/// </summary>
internal sealed record PlacementPlan(ArrivalPlacement Arrival, RefugePlacement Refuge, FindPlacement[] Finds,
    ResidentPost[] Residents, SpiritBellSocket[] SpiritBells, ArrivalPlacement FromAbove)
{
    internal ExcursionPlacements Resolve(string path, BuiltFloor floor, ResidentKind[] kinds)
    {
        Arrival.Validate(path);
        FromAbove.Validate(path, "fromAbove");
        ResidentPlacement[] residents = new ResidentPlacement[Residents.Length];
        for (int i = 0; i < Residents.Length; i++)
        {
            ResidentPost post = Residents[i];
            ResidentKind? kind = kinds.FirstOrDefault(k => k.Id == post.Kind);
            Authored.Require(kind is not null, path, $"residents[{i}].kind", $"unknown resident kind '{post.Kind}'.");
            // A post is the resident's footing; its body centre stands half its height above.
            float[] feet = RoutePlan.Socket(path, $"residents[{i}].socket", floor, post.Socket);
            residents[i] = new(post.Id, post.Kind, [feet[0], feet[1] + kind!.Height / 2, feet[2]]);
        }
        FindDefinition[] finds = new FindDefinition[Finds.Length];
        for (int i = 0; i < Finds.Length; i++)
        {
            Authored.AtLeast(path, $"finds[{i}].count", Finds[i].Count, 1);
            finds[i] = new(Finds[i].Id, Finds[i].Item, Finds[i].Count, RoutePlan.Socket(path, $"finds[{i}].socket", floor, Finds[i].Socket));
        }
        SpiritBellPlacement[] bells = SpiritBells.Select((b, i) =>
            new SpiritBellPlacement(b.Spirit, RoutePlan.Socket(path, $"spiritBells[{i}].socket", floor, b.Socket), b.Place)).ToArray();
        return new(Arrival, new(Refuge.Id, RoutePlan.Socket(path, "refuge.socket", floor, Refuge.Socket)), finds, residents, bells, FromAbove, []);
    }
}

internal sealed record RefugePlacement(string Id, string Socket);
internal sealed record FindPlacement(string Id, string Item, int Count, string Socket);
internal sealed record ResidentPost(string Id, string Kind, string Socket);
internal sealed record SpiritBellSocket(string Spirit, string Socket, string Place);
