using Hotel.Game.Floors.Mission;
using Hotel.Game.Floors.Modules;
using Hotel.Game.Scene.Kit;

namespace Hotel.Game.Floors.Layout;

/// <summary>Lays out a mission graph, realizes it as one kit floor plan, builds it, and checks its locks and reach.</summary>
internal static class FloorLayouts
{
    internal sealed record Laid(FloorLayout? Layout, FloorPlan? Plan, BuiltFloor? Floor, string? Failure);

    internal static Laid Lay(MissionGraph graph, ModuleCatalog catalog, LayoutTuning tuning, KitDefinition kit, FixtureCatalog fixtures,
        FloorDraws draws)
    {
        LayoutResult result = FloorEmbedding.Embed(graph, catalog, tuning, draws);
        if (result.Layout is not { } layout) return new(null, null, null, result.Failure);
        FloorPlan plan = layout.Realize(catalog);
        BuiltFloor floor;
        // The kit builder's checks are authoring errors for authored floors; for a generated one they are a refusal.
        try { floor = KitBuilder.Build(plan, "generated floor", kit, fixtures); }
        catch (InvalidOperationException error) { return new(layout, plan, null, $"realize: {error.Message}"); }
        return LayoutCheck.Check(layout, plan, graph) is { } problem ? new(layout, plan, floor, problem) : new(layout, plan, floor, null);
    }
}
