using Hotel.Game.Content;
using Hotel.Game.Player;
using Hotel.Game.Route;
using Hotel.Game.Scene.Kit;
using Rusty.Engine;

namespace Hotel.Game.Floors.Modules;

/// <summary>
/// The room modules generated floors are assembled from, each in its own file under <c>floors/modules/</c>, with the
/// lattice they are authored on and how each doorway kind is built. Every module passes its isolated self-check.
/// </summary>
/// <param name="TrimStyle">The trim style a module is built in when it stands alone: its self-check and developer view.</param>
internal sealed record ModuleCatalog(float Cell, AmbientLighting Lighting, string TrimStyle, DoorwayStyle[] Doorways, ModuleDefinition[] Modules)
{
    internal const string Path = "floors/modules.json";
    internal static string ModulePath(string id) => $"floors/modules/{id}.json";

    internal DoorwayStyle? Style(DoorwayKind kind) => Doorways.FirstOrDefault(d => d.Kind == kind);
    internal ModuleDefinition? Find(string id) => Modules.FirstOrDefault(m => m.Id == id);

    internal static ModuleCatalog Load(IEngineContext engine, KitDefinition kit, FixtureCatalog fixtures, ModuleBody body)
    {
        ModuleCatalogFile file = Authored.Read(engine, Path, ContentJson.Default.ModuleCatalogFile);
        Authored.Positive(Path, "cell", file.Cell);
        Authored.Colour(Path, "lighting.ambientColor", file.Lighting.AmbientColor);
        Authored.AtLeast(Path, "lighting.ambientIntensity", file.Lighting.AmbientIntensity, 0);
        Authored.Require(kit.TrimStyles.ContainsKey(file.TrimStyle), Path, "trimStyle", $"unknown trim style '{file.TrimStyle}'.");
        for (int i = 0; i < file.Doorways.Length; i++)
        {
            DoorwayStyle style = file.Doorways[i];
            Authored.Require(style.Kind != DoorwayKind.Stair, Path, $"doorways[{i}].kind", "a stair cuts no wall, so it has no doorway style.");
            Authored.Require(style.Link != LinkKind.Hatch, Path, $"doorways[{i}].link", "a doorway is walked through; a hatch is not.");
            if (style.Link != LinkKind.Open) Authored.Positive(Path, $"doorways[{i}].width", style.Width);
        }
        foreach (DoorwayKind kind in Enum.GetValues<DoorwayKind>().Where(k => k != DoorwayKind.Stair))
            Authored.Require(file.Doorways.Count(d => d.Kind == kind) == 1, Path, "doorways", $"exactly one style for '{kind}'.");
        string? repeated = file.Modules.GroupBy(m => m).FirstOrDefault(g => g.Count() > 1)?.Key;
        Authored.Require(repeated is null, Path, "modules", $"'{repeated}' appears more than once.");
        ModuleCatalog catalog = new(file.Cell, file.Lighting, file.TrimStyle, file.Doorways, []);
        ModuleDefinition[] modules = file.Modules.Select(id =>
        {
            string path = ModulePath(id);
            ModuleDefinition module = Authored.Read(engine, path, ContentJson.Default.ModuleDefinition);
            Authored.Require(module.Id == id, path, "id", $"must be '{id}', the name it is listed under in {Path}.");
            Validate(module, path, catalog with { Modules = [module] });
            ModuleCheck.Require(module, path, catalog, kit, fixtures, body, turn: 0);
            return module;
        }).ToArray();
        return catalog with { Modules = modules };
    }

    // Shape rules the kit builder cannot see: the lattice, the outer wall, and what modules may not decide themselves.
    private static void Validate(ModuleDefinition module, string path, ModuleCatalog catalog)
    {
        bool OnLattice(float value) => MathF.Abs(value / catalog.Cell - MathF.Round(value / catalog.Cell)) < 1e-3f;
        bool Pair(float[]? xz) => xz is { Length: 2 } && xz.All(float.IsFinite);
        Authored.Require(Pair(module.Size) && module.Size.All(v => v > 0 && OnLattice(v)), path, "size",
            $"must be [width, depth], positive and on the {catalog.Cell} m lattice.");
        // Shapes and references first: placing a module transforms these before the kit builder sees them.
        HashSet<string> spaces = module.Spaces.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        for (int i = 0; i < module.Spaces.Length; i++)
        {
            Authored.Require(Pair(module.Spaces[i].Min), path, $"spaces[{i}].min", "must be [x, z].");
            Authored.Require(Pair(module.Spaces[i].Max), path, $"spaces[{i}].max", "must be [x, z].");
            foreach (var (post, point) in module.Spaces[i].Posts ?? [])
                Authored.Require(Pair(point), path, $"spaces[{i}].posts.{post}", "must be [x, z].");
        }
        for (int i = 0; i < module.Links.Length; i++)
        {
            LinkDefinition link = module.Links[i];
            Authored.Require(link.Between.Length == 2, path, $"links[{i}].between", "must name two spaces.");
            for (int j = 0; j < 2; j++)
                Authored.Require(spaces.Contains(link.Between[j]), path, $"links[{i}].between[{j}]", $"unknown space '{link.Between[j]}'.");
        }
        for (int i = 0; i < module.Fixtures.Length; i++)
        {
            FixturePlacement f = module.Fixtures[i];
            if (f.At is not null) Authored.Require(Pair(f.At), path, $"fixtures[{i}].at", "must be [x, z].");
            if (f.Space is not null) Authored.Require(spaces.Contains(f.Space), path, $"fixtures[{i}].space", $"unknown space '{f.Space}'.");
            if (f.Edge is not null) Authored.Require(f.Space is not null, path, $"fixtures[{i}].space", "a wall fixture names its space.");
        }
        for (int i = 0; i < module.Spaces.Length; i++)
        {
            SpaceDefinition s = module.Spaces[i];
            Authored.Require(s.Min.Concat(s.Max).All(OnLattice), path, $"spaces[{i}]", $"walls lie on the {catalog.Cell} m lattice.");
            Authored.Require(s.Min[0] >= 0 && s.Min[1] >= 0 && s.Max[0] <= module.Width && s.Max[1] <= module.Depth, path, $"spaces[{i}]",
                "lies within the module's size.");
        }
        for (int i = 0; i < module.Fixtures.Length; i++)
            Authored.Require(module.Fixtures[i].Find is null, path, $"fixtures[{i}].find",
                "floor content decides finds; offer the place as a Find content socket instead.");
        for (int i = 0; i < module.Doorways.Length; i++)
        {
            DoorwayDefinition d = module.Doorways[i];
            SpaceDefinition? space = module.Spaces.FirstOrDefault(s => s.Id == d.Space);
            Authored.Require(space is not null, path, $"doorways[{i}].space", $"unknown space '{d.Space}'.");
            (float line, float from, float to, float boundary) = d.Edge switch
            {
                WallEdge.North => (space!.Min[1], space.Min[0], space.Max[0], 0f),
                WallEdge.South => (space!.Max[1], space.Min[0], space.Max[0], module.Depth),
                WallEdge.West => (space!.Min[0], space.Min[1], space.Max[1], 0f),
                _ => (space!.Max[0], space.Min[1], space.Max[1], module.Width)
            };
            Authored.Require(MathF.Abs(line - boundary) < 1e-4f, path, $"doorways[{i}].edge", "a doorway is on the module's outer wall.");
            Authored.Require(OnLattice(d.At), path, $"doorways[{i}].at", $"lies on the {catalog.Cell} m lattice, so mating doorways meet.");
            float half = (catalog.Style(d.Kind)?.Width ?? 0) / 2;
            Authored.Within(path, $"doorways[{i}].at", d.At, from + half, to - half);
        }
        Unique(module.Doorways.Select(d => d.Id), path, "doorways");
        Unique(module.Sockets.Select(s => s.Id), path, "sockets");
    }

    private static void Unique(IEnumerable<string> ids, string path, string field)
    {
        string? repeated = ids.GroupBy(id => id).FirstOrDefault(g => g.Count() > 1)?.Key;
        Authored.Require(repeated is null, path, field, $"id '{repeated}' appears more than once.");
    }
}

/// <summary>The player's body and reach, which a module's floor must leave room for.</summary>
internal sealed record ModuleBody(float Radius, float Height, float StepHeight, float EyeHeight, float Reach)
{
    internal static ModuleBody Of(PlayerTuning player, InteractionTuning interaction) =>
        new(player.Radius, player.Height, player.MaximumStepHeight, player.EyeHeight, interaction.Reach);
}
