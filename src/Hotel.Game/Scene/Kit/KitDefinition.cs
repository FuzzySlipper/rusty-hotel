using Hotel.Game.Content;
using Rusty.Engine;

namespace Hotel.Game.Scene.Kit;

/// <summary>
/// The hotel's architectural kit: how thick its walls, floors and ceilings are, which trim runs along a wall,
/// how door frames are cut, and the surface sets a space can be styled with.
/// </summary>
internal sealed record KitDefinition(float WallThickness, float FloorThickness, float CeilingThickness, DoorLeafTuning DoorLeaf,
    Dictionary<string, TrimBand[]> TrimSets, Dictionary<string, FrameDefinition> Frames, Dictionary<string, SpaceStyle> Styles)
{
    internal const string Path = "scene/kit.json";

    internal static KitDefinition Load(IEngineContext engine)
    {
        KitDefinition kit = Authored.Read(engine, Path, ContentJson.Default.KitDefinition);
        Authored.Positive(Path, "wallThickness", kit.WallThickness);
        Authored.Positive(Path, "floorThickness", kit.FloorThickness);
        Authored.Positive(Path, "ceilingThickness", kit.CeilingThickness);
        Authored.Positive(Path, "doorLeaf.thickness", kit.DoorLeaf.Thickness);
        Authored.Within(Path, "doorLeaf.thickness", kit.DoorLeaf.Thickness, 0, kit.WallThickness);
        Authored.AtLeast(Path, "doorLeaf.clearance", kit.DoorLeaf.Clearance, 0);
        Authored.Positive(Path, "doorLeaf.focusHeight", kit.DoorLeaf.FocusHeight);
        foreach (var (id, bands) in kit.TrimSets)
            for (int i = 0; i < bands.Length; i++)
            {
                Authored.AtLeast(Path, $"trimSets.{id}[{i}].from", bands[i].From, 0);
                Authored.Require(bands[i].To > bands[i].From, Path, $"trimSets.{id}[{i}].to", "must be above from.");
                Authored.AtLeast(Path, $"trimSets.{id}[{i}].depth", bands[i].Depth, 0);
            }
        foreach (var (id, frame) in kit.Frames)
        {
            Authored.Positive(Path, $"frames.{id}.jambWidth", frame.JambWidth);
            Authored.Within(Path, $"frames.{id}.inset", frame.Inset, 0, frame.JambWidth);
            Authored.AtLeast(Path, $"frames.{id}.proud", frame.Proud, 0);
            Authored.Positive(Path, $"frames.{id}.headHeight", frame.HeadHeight);
        }
        foreach (var (id, style) in kit.Styles)
        {
            Authored.Require(kit.TrimSets.ContainsKey(style.Trim), Path, $"styles.{id}.trim", $"unknown trim set '{style.Trim}'.");
            if (style.Seams is { } seams)
            {
                Authored.Positive(Path, $"styles.{id}.seams.spacing", seams.Spacing);
                Authored.Positive(Path, $"styles.{id}.seams.width", seams.Width);
                Authored.Positive(Path, $"styles.{id}.seams.depth", seams.Depth);
            }
        }
        return kit;
    }

    /// <summary>Every surface id the kit itself names, with the field that names it.</summary>
    internal IEnumerable<(string Field, string Surface)> Surfaces()
    {
        foreach (var (id, bands) in TrimSets)
            for (int i = 0; i < bands.Length; i++) yield return ($"trimSets.{id}[{i}].material", bands[i].Material);
        foreach (var (id, frame) in Frames) yield return ($"frames.{id}.material", frame.Material);
        foreach (var (id, style) in Styles)
        {
            yield return ($"styles.{id}.wall", style.Wall);
            yield return ($"styles.{id}.floor", style.Floor);
            yield return ($"styles.{id}.ceiling", style.Ceiling);
            if (style.Seams is { } seams) yield return ($"styles.{id}.seams.material", seams.Material);
        }
    }
}

/// <summary>A gameplay door leaf: its thickness, the gap left below the opening's head, and the height its use prompt aims at.</summary>
internal sealed record DoorLeafTuning(float Thickness, float Clearance, float FocusHeight);

/// <summary>One horizontal trim run (skirting, picture rail, cornice): its height band and how far it stands proud.</summary>
internal sealed record TrimBand(string Name, float From, float To, float Depth, string Material);

/// <summary>
/// A door frame: jambs <see cref="JambWidth"/> wide, overlapping the opening by <see cref="Inset"/>, standing
/// <see cref="Proud"/> beyond both wall faces, with a head <see cref="HeadHeight"/> above the opening.
/// </summary>
internal sealed record FrameDefinition(string Material, float JambWidth, float Inset, float Proud, float HeadHeight);

/// <summary>A space's surface set and trim. Spaces may override any surface.</summary>
internal sealed record SpaceStyle(string Wall, string Floor, string Ceiling, string Trim, SeamDefinition? Seams = null);

/// <summary>Ceiling joints every <see cref="Spacing"/> metres along a space's long axis.</summary>
internal sealed record SeamDefinition(float Spacing, float Width, float Depth, string Material);
