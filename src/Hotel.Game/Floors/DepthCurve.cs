using Hotel.Game.Content;

namespace Hotel.Game.Floors;

/// <summary>
/// An authored count that grows with floor depth: <see cref="Base"/> on the first generated floor, then
/// <see cref="PerDepth"/> more each floor down, never beyond <see cref="Max"/>.
/// </summary>
internal sealed record DepthCurve(int Base, int PerDepth, int Max)
{
    internal int At(int depth) => Math.Min(Max, Base + PerDepth * Math.Max(0, depth - 1));

    internal void Validate(string path, string field)
    {
        Authored.AtLeast(path, $"{field}.base", Base, 0);
        Authored.AtLeast(path, $"{field}.perDepth", PerDepth, 0);
        Authored.AtLeast(path, $"{field}.max", Max, Base);
    }
}
