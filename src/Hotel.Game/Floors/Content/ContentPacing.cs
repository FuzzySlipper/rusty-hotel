using Hotel.Game.Combat;
using Hotel.Game.Floors.Layout;
using Hotel.Game.Floors.Mission;
using Hotel.Game.Scene.Kit;
using Hotel.Game.Supplies;

namespace Hotel.Game.Floors.Content;

/// <summary>
/// The pacing budgets a floor's content must meet: one objective; a key for every lock; the bell when the graph has
/// one; a resident at every hazard; the recovery item reachable before every hazard; ammunition within its depth's range; every resident's leash
/// inside its own region. Returns why it fails, or null.
/// </summary>
internal static class ContentPacing
{
    internal static string? Check(FloorContent content, MissionGraph graph, FloorLayout layout, FloorPlan plan, BuiltFloor floor,
        ContentTuning tuning, ItemDefinition[] items, ResidentKind[] kinds, int depth)
    {
        if (content.Finds.Count(f => f.Role == FindRole.Objective) != 1) return "objective: the floor needs exactly one objective find.";
        foreach (MissionEdge edge in graph.Edges.Where(e => e.Kind == MissionEdgeKind.Locked))
            if (content.Finds.Count(f => f.Role == FindRole.Key && f.Grants == edge.Item) != 1) return $"key: lock '{edge.Id}' needs exactly one key find for '{edge.Item}'.";
        if (graph.Nodes.Any(n => n.Kind == MissionNodeKind.Bell) && content.Bell is null) return "bell: the bell place has no bell socket.";
        Dictionary<string, string> nodeOf = layout.Places.ToDictionary(p => p.Value, p => p.Key, StringComparer.Ordinal);
        foreach (MissionNode hazard in graph.Nodes.Where(n => n.Kind == MissionNodeKind.Hazard))
            if (!content.Residents.Any(r => r.Id.StartsWith(layout.Places[hazard.Id] + "/", StringComparison.Ordinal)))
                return $"hazard: '{hazard.Id}' has no resident that fits its post and region.";
        foreach (MissionNode hazard in graph.Nodes.Where(n => n.Kind == MissionNodeKind.Hazard))
        {
            IReadOnlySet<string> before = MissionReach.From(graph, MissionGraph.ArrivalId, blocked: hazard.Id).Reached;
            int recovery = content.Finds.Where(f => f.Item == tuning.Pacing.RecoveryItem && nodeOf.TryGetValue(f.Id.Split('/')[0], out string? node) && before.Contains(node))
                .Sum(f => f.Count);
            if (recovery < tuning.Pacing.RecoveryBeforeHazard)
                return $"recovery: only {recovery} {tuning.Pacing.RecoveryItem} before hazard '{hazard.Id}'; {tuning.Pacing.RecoveryBeforeHazard} needed.";
        }
        int ammo = content.Finds.Where(f => f.Item is not null && items.First(i => i.Id == f.Item).Kind == SupplyKind.Ammo)
            .Sum(f => f.Count * items.First(i => i.Id == f.Item).Amount);
        int minimum = tuning.Pacing.AmmoMinimum.At(depth), maximum = tuning.Pacing.AmmoMaximum.At(depth);
        if (ammo < minimum || ammo > maximum) return $"ammunition: {ammo} rounds on the floor; depth {depth} allows {minimum} to {maximum}.";
        foreach (PlacedResident resident in content.Residents)
            if (ContentPlacement.LeashCrossing(resident, layout, plan, floor, kinds.First(k => k.Id == resident.Kind).Leash) is { } space)
                return $"leash: '{resident.Id}' can follow the player into '{space}', outside its region.";
        return null;
    }
}
