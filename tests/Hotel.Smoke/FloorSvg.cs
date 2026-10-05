using System.Globalization;
using System.Text;
using Hotel.Game.Floors.Layout;
using Hotel.Game.Scene.Kit;

// Off-build evidence: a plan drawing of a laid-out floor, written only when HOTEL_FLOOR_SVG names a directory.
internal static class FloorSvg
{
    internal static void Write(string name, FloorLayout layout, FloorPlan plan)
    {
        if (Environment.GetEnvironmentVariable("HOTEL_FLOOR_SVG") is not { Length: > 0 } folder) return;
        Directory.CreateDirectory(folder);
        float minX = plan.Spaces.Min(s => s.Min[0]) - 1, minZ = plan.Spaces.Min(s => s.Min[1]) - 1;
        float maxX = plan.Spaces.Max(s => s.Max[0]) + 1, maxZ = plan.Spaces.Max(s => s.Max[1]) + 1;
        const float scale = 12;
        string N(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        StringBuilder svg = new($"<svg xmlns='http://www.w3.org/2000/svg' width='{N((maxX - minX) * scale)}' height='{N((maxZ - minZ) * scale)}' " +
            $"viewBox='{N(minX)} {N(minZ)} {N(maxX - minX)} {N(maxZ - minZ)}' style='background:#1d1b19'>\n");
        Dictionary<string, string> fills = new() { ["corridor"] = "#6d6448", ["guest-room"] = "#3f6b66", ["service"] = "#7b7a72" };
        Dictionary<string, string> places = layout.Places.ToDictionary(p => p.Value, p => p.Key);
        foreach (SpaceDefinition s in plan.Spaces)
        {
            svg.Append($"<rect x='{N(s.Min[0])}' y='{N(s.Min[1])}' width='{N(s.Max[0] - s.Min[0])}' height='{N(s.Max[1] - s.Min[1])}' " +
                $"fill='{fills.GetValueOrDefault(s.Style, "#555")}' stroke='#111' stroke-width='0.2'/>\n");
            string placement = s.Id.Split('/')[0];
            if (places.TryGetValue(placement, out string? node) && s.Id == plan.Spaces.First(x => x.Id.StartsWith(placement + "/")).Id)
                svg.Append($"<text x='{N((s.Min[0] + s.Max[0]) / 2)}' y='{N((s.Min[1] + s.Max[1]) / 2)}' font-size='0.8' fill='#f2e6c9' " +
                    $"text-anchor='middle'>{node}</text>\n");
        }
        HashSet<string> locked = layout.Locks.Select(l => l.Link).ToHashSet();
        foreach (LinkDefinition link in plan.Links.Where(l => l.Kind != LinkKind.Open))
        {
            SpaceDefinition a = plan.Spaces.First(s => s.Id == link.Between[0]), b = plan.Spaces.First(s => s.Id == link.Between[1]);
            bool alongX = MathF.Abs(a.Max[1] - b.Min[1]) < 1e-3f || MathF.Abs(a.Min[1] - b.Max[1]) < 1e-3f;
            float line = alongX ? (MathF.Abs(a.Max[1] - b.Min[1]) < 1e-3f ? a.Max[1] : a.Min[1]) : (MathF.Abs(a.Max[0] - b.Min[0]) < 1e-3f ? a.Max[0] : a.Min[0]);
            string colour = link.Id == layout.Latch ? "#e0a33a" : locked.Contains(link.Id) ? "#d2463c" : link.Kind == LinkKind.Door ? "#e8dcc0" : "#9fb7a8";
            float h = link.Width / 2;
            svg.Append(alongX
                ? $"<line x1='{N(link.At - h)}' y1='{N(line)}' x2='{N(link.At + h)}' y2='{N(line)}' stroke='{colour}' stroke-width='0.45'/>\n"
                : $"<line x1='{N(line)}' y1='{N(link.At - h)}' x2='{N(line)}' y2='{N(link.At + h)}' stroke='{colour}' stroke-width='0.45'/>\n");
        }
        svg.Append("</svg>\n");
        File.WriteAllText(Path.Combine(folder, name + ".svg"), svg.ToString());
    }
}
