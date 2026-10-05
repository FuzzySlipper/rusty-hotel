using Hotel.Game.Content;
using Hotel.Game.Floors.Mission;
using Hotel.Game.Floors.Modules;
using Hotel.Game.Scene.Kit;
using Rusty.Engine;

namespace Hotel.Game.Floors.Layout;

/// <summary>A module and how strongly the embedding prefers it.</summary>
internal sealed record ModuleWeight(string Module, int Weight);

/// <summary>
/// Which modules may stand for one kind of mission place, and the doorway kind it joins its host corridor by: an
/// archway for places on the corridor itself (halls, gates, hazards), a corridor door for rooms off it.
/// </summary>
internal sealed record PlaceRole(MissionNodeKind Node, DoorwayKind Joins, ModuleWeight[] Modules);

/// <summary>
/// The back-of-house passage the shortcut runs through: its width, surface style, label and lamp, what a turn costs in
/// lattice steps, and how many search steps routing may take before the floor is refused.
/// </summary>
internal sealed record PassageTuning(float Width, string Style, string Label, string Lamp, int TurnCost, int Search);

/// <summary>
/// How a mission graph is laid out as modules: the floor's extent, how many rooms by depth, which modules stand for
/// each kind of place, the corridor modules the spine grows with, the rooms that fill a floor out, the shortcut's
/// service passage, how many tries a phase gets, and how often filling branches the corridor.
/// </summary>
/// <param name="BranchOneIn">While filling, one step in this many grows the corridor at an open end instead of adding a room.</param>
internal sealed record LayoutTuning(float Extent, DepthCurve Rooms, PlaceRole[] Roles, ModuleWeight[] Spine, ModuleWeight[] Fill,
    PassageTuning Passage, int Tries, int BranchOneIn)
{
    internal const string Path = "floors/layout.json";

    internal PlaceRole Role(MissionNodeKind kind) => Roles.First(r => r.Node == kind);

    internal static LayoutTuning Load(IEngineContext engine, ModuleCatalog modules, KitDefinition kit)
    {
        LayoutTuning tuning = Authored.Read(engine, Path, ContentJson.Default.LayoutTuning);
        Authored.Positive(Path, "extent", tuning.Extent);
        tuning.Rooms.Validate(Path, "rooms");
        Authored.AtLeast(Path, "tries", tuning.Tries, 1);
        Authored.AtLeast(Path, "branchOneIn", tuning.BranchOneIn, 1);
        void Weights(string field, ModuleWeight[] weights, Func<ModuleDefinition, string?> refuse)
        {
            Authored.Require(weights.Length > 0 && weights.Any(w => w.Weight > 0), Path, field, "needs a module with a positive weight.");
            for (int i = 0; i < weights.Length; i++)
            {
                Authored.AtLeast(Path, $"{field}[{i}].weight", weights[i].Weight, 0);
                ModuleDefinition? module = modules.Find(weights[i].Module);
                Authored.Require(module is not null, Path, $"{field}[{i}].module", $"unknown module '{weights[i].Module}'.");
                if (refuse(module!) is { } problem) Authored.Require(false, Path, $"{field}[{i}].module", problem);
            }
        }
        string? Has(ModuleDefinition m, DoorwayKind kind, int count = 1) =>
            m.Doorways.Count(d => d.Kind == kind) >= count ? null : $"'{m.Id}' needs {count} {kind} doorway(s).";
        foreach (MissionNodeKind kind in Enum.GetValues<MissionNodeKind>())
            Authored.Require(tuning.Roles.Count(r => r.Node == kind) == 1, Path, "roles", $"exactly one role for '{kind}'.");
        for (int i = 0; i < tuning.Roles.Length; i++)
        {
            PlaceRole role = tuning.Roles[i];
            Weights($"roles[{i}].modules", role.Modules, m => role.Node switch
            {
                // The arrival is the stair core: the flight, a corridor archway and the service door the shortcut returns by.
                MissionNodeKind.Arrival => Has(m, DoorwayKind.Stair) ?? Has(m, DoorwayKind.Archway) ?? Has(m, DoorwayKind.ServiceDoor)
                    ?? (new[] { ContentSocketKind.Arrival, ContentSocketKind.StairUp, ContentSocketKind.StairDown }.All(k => m.Sockets.Count(s => s.Kind == k) == 1)
                        ? null : $"'{m.Id}' needs one arrival, one stair-up and one stair-down socket."),
                // A gate is a corridor piece with one door inside it between its two archways.
                MissionNodeKind.Gate => Has(m, DoorwayKind.Archway, 2) ?? (m.Links.Count(l => l.Kind == LinkKind.Door) == 1 ? null : $"'{m.Id}' needs one inner door."),
                MissionNodeKind.Shortcut => Has(m, role.Joins) ?? Has(m, DoorwayKind.ServiceDoor),
                MissionNodeKind.Hazard => Has(m, role.Joins) ?? (m.Sockets.Any(s => s.Kind == ContentSocketKind.ResidentPost) ? null : $"'{m.Id}' needs a resident post."),
                MissionNodeKind.Bell => Has(m, role.Joins) ?? (m.Sockets.Any(s => s.Kind == ContentSocketKind.Bell) ? null : $"'{m.Id}' needs a bell socket."),
                _ => Has(m, role.Joins)
            });
        }
        Weights("spine", tuning.Spine, m => Has(m, DoorwayKind.Archway, 2));
        Weights("fill", tuning.Fill, m => Has(m, DoorwayKind.CorridorDoor));
        Authored.Positive(Path, "passage.width", tuning.Passage.Width);
        Authored.Require(kit.Styles.ContainsKey(tuning.Passage.Style), Path, "passage.style", $"unknown style '{tuning.Passage.Style}'.");
        Template.Plain(Path, ("passage.label", tuning.Passage.Label));
        Authored.AtLeast(Path, "passage.turnCost", tuning.Passage.TurnCost, 0);
        Authored.AtLeast(Path, "passage.search", tuning.Passage.Search, 1);
        Authored.Require(MathF.Abs(tuning.Passage.Width / modules.Cell - MathF.Round(tuning.Passage.Width / modules.Cell)) < 1e-3f, Path, "passage.width",
            $"is a whole number of {modules.Cell} m lattice steps.");
        return tuning;
    }
}
