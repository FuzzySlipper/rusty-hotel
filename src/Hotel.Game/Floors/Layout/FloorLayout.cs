using Hotel.Game.Floors.Modules;
using Hotel.Game.Scene.Kit;

namespace Hotel.Game.Floors.Layout;

/// <summary>
/// One placed module: its stable placement id, which module, its turned north-west corner, its quarter turns, and the
/// lock region it stands in.
/// </summary>
internal sealed record LayoutPlacement(string Id, string Module, float X, float Z, int Turn, string Region);

/// <summary>The door a mission graph's locked edge became: the realized link, and the item that opens it.</summary>
internal sealed record LayoutLock(string Edge, string Link, string Item);

/// <summary>
/// A floor's resolved layout: every module placement, the generated service passage, which placement each mission
/// place became, the doors its locks became, and the latch the shortcut opens. Pure data; realizing it draws nothing.
/// </summary>
internal sealed record FloorLayout(LayoutPlacement[] Placements, SpaceDefinition[] Passage, LinkDefinition[] PassageLinks,
    FixturePlacement[] PassageFixtures, IReadOnlyDictionary<string, string> Places, LayoutLock[] Locks, string? Latch)
{
    internal PlacedModule[] Placed(ModuleCatalog catalog) => Placements.Select(p =>
        new PlacedModule(p.Id, new(catalog.Find(p.Module)!, new(p.X, p.Z), p.Turn))).ToArray();

    /// <summary>The service passage is its own region: it belongs to neither side of the latch.</summary>
    internal const string PassageRegion = "passage";

    /// <summary>The region a realized space stands in.</summary>
    internal string RegionOf(string space)
    {
        string placement = space.Split('/')[0];
        return Placements.FirstOrDefault(p => p.Id == placement)?.Region ?? PassageRegion;
    }

    internal FloorPlan Realize(ModuleCatalog catalog) =>
        ModuleRealizer.Plan(Placed(catalog), catalog, Passage, PassageLinks, PassageFixtures);

    internal void Write(CanonicalText text)
    {
        foreach (LayoutPlacement p in Placements.OrderBy(p => p.Id, StringComparer.Ordinal))
            text.Line("layout.module", p.Id, p.Module, CanonicalText.Number(p.X), CanonicalText.Number(p.Z), CanonicalText.Number(p.Turn), p.Region);
        foreach (SpaceDefinition s in Passage.OrderBy(s => s.Id, StringComparer.Ordinal))
            text.Line("layout.passage", s.Id, CanonicalText.Number(s.Min[0]), CanonicalText.Number(s.Min[1]),
                CanonicalText.Number(s.Max[0]), CanonicalText.Number(s.Max[1]));
        foreach (LinkDefinition l in PassageLinks.OrderBy(l => l.Id, StringComparer.Ordinal))
            text.Line("layout.passage-link", l.Id, l.Between[0], l.Between[1], l.Kind.ToString(), CanonicalText.Number(l.At));
        foreach (var (node, placement) in Places.OrderBy(p => p.Key, StringComparer.Ordinal)) text.Line("layout.place", node, placement);
        foreach (LayoutLock l in Locks.OrderBy(l => l.Edge, StringComparer.Ordinal)) text.Line("layout.lock", l.Edge, l.Link, l.Item);
        text.Line("layout.latch", Latch ?? "");
    }
}

/// <summary>The outcome of laying out a floor: the layout when every phase succeeded, otherwise the phase and why.</summary>
internal sealed record LayoutResult(FloorLayout? Layout, string? Failure)
{
    internal bool Succeeded => Layout is not null;
}
