using Hotel.Game.Content;
using Rusty.Engine;

namespace Hotel.Game.Scene.Kit;

/// <summary>
/// The hotel's architectural kit: how thick its walls, floors and ceilings are, the trim styles a floor can be dressed
/// in, how door frames are cut, the surface sets a space can be styled with, and the decors a floor can be furnished
/// in: per space style, the wallpaper, carpet and ceiling that replace the style's own.
/// </summary>
internal sealed record KitDefinition(float WallThickness, float FloorThickness, float CeilingThickness, DoorLeafTuning DoorLeaf,
    Dictionary<string, TrimStyle> TrimStyles, Dictionary<string, FrameDefinition> Frames, PilasterDefinition Pilaster,
    Dictionary<string, SpaceStyle> Styles, Dictionary<string, Dictionary<string, DecorSurfaces>> Decors)
{
    /// <summary>A space style as a decor furnishes it: the decor's surfaces where it names them, the style's otherwise.</summary>
    internal SpaceStyle Style(string style, string? decor)
    {
        SpaceStyle baseStyle = Styles[style];
        if (decor is null || !Decors[decor].TryGetValue(style, out DecorSurfaces? surfaces)) return baseStyle;
        return baseStyle with { Wall = surfaces.Wall ?? baseStyle.Wall, Floor = surfaces.Floor ?? baseStyle.Floor, Ceiling = surfaces.Ceiling ?? baseStyle.Ceiling };
    }

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
        foreach (var (styleId, style) in kit.TrimStyles)
        {
            foreach (var (role, bands) in style.Bands)
                for (int i = 0; i < bands.Length; i++) ValidateBand($"trimStyles.{styleId}.bands.{role}[{i}]", bands[i]);
            foreach (var (frame, architrave) in style.Architraves ?? [])
            {
                string at = $"trimStyles.{styleId}.architraves.{frame}";
                Authored.Require(kit.Frames.ContainsKey(frame), Path, at, $"unknown frame '{frame}'.");
                ValidateProfile($"{at}.profile", architrave.Profile, float.MaxValue, float.MaxValue);
            }
        }
        foreach (var (id, frame) in kit.Frames)
        {
            Authored.Positive(Path, $"frames.{id}.jambWidth", frame.JambWidth);
            Authored.Within(Path, $"frames.{id}.inset", frame.Inset, 0, frame.JambWidth);
            Authored.AtLeast(Path, $"frames.{id}.proud", frame.Proud, 0);
            Authored.Positive(Path, $"frames.{id}.headHeight", frame.HeadHeight);
        }
        Authored.Positive(Path, "pilaster.width", kit.Pilaster.Width);
        Authored.Within(Path, "pilaster.proud", kit.Pilaster.Proud, float.Epsilon, kit.Pilaster.Width / 2);
        foreach (var (decor, styles) in kit.Decors)
            foreach (var (style, _) in styles)
                Authored.Require(kit.Styles.ContainsKey(style), Path, $"decors.{decor}.{style}", $"unknown space style '{style}'.");
        foreach (var (id, style) in kit.Styles)
        {
            // Every trim style dresses every role a space style names, so any floor can take any style.
            foreach (var (styleId, trim) in kit.TrimStyles)
                Authored.Require(trim.Bands.ContainsKey(style.Trim), Path, $"trimStyles.{styleId}.bands",
                    $"has no '{style.Trim}' role, which styles.{id}.trim names.");
            if (style.Seams is { } seams)
            {
                Authored.Positive(Path, $"styles.{id}.seams.spacing", seams.Spacing);
                Authored.Positive(Path, $"styles.{id}.seams.width", seams.Width);
                Authored.Positive(Path, $"styles.{id}.seams.depth", seams.Depth);
            }
        }
        return kit;
    }

    private static void ValidateBand(string at, TrimBand band)
    {
        Authored.AtLeast(Path, $"{at}.from", band.From, 0);
        Authored.Require(band.To > band.From, Path, $"{at}.to", "must be above from.");
        Authored.AtLeast(Path, $"{at}.depth", band.Depth, 0);
        if (band.Profile is { } profile) ValidateProfile($"{at}.profile", profile, band.Depth, band.To - band.From);
    }

    // A swept cross-section: [out, along-the-face] points from the wall face round to the wall face.
    private static void ValidateProfile(string at, float[][] profile, float maxOut, float maxAcross)
    {
        Authored.Require(profile.Length >= 3 && profile.All(p => p.Length == 2), Path, at, "needs at least three [out, across] points.");
        for (int p = 0; p < profile.Length; p++)
        {
            Authored.Within(Path, $"{at}[{p}][0]", profile[p][0], 0, maxOut);
            Authored.Within(Path, $"{at}[{p}][1]", profile[p][1], 0, maxAcross);
        }
        Authored.Require(profile[0][0] == 0 && profile[^1][0] == 0, Path, at, "must start and end on the wall face (out 0).");
    }

    /// <summary>Every surface id the kit itself names, with the field that names it.</summary>
    internal IEnumerable<(string Field, string Surface)> Surfaces()
    {
        foreach (var (styleId, style) in TrimStyles)
        {
            foreach (var (role, bands) in style.Bands)
                for (int i = 0; i < bands.Length; i++) yield return ($"trimStyles.{styleId}.bands.{role}[{i}].material", bands[i].Material);
            foreach (var (frame, architrave) in style.Architraves ?? [])
                yield return ($"trimStyles.{styleId}.architraves.{frame}.material", architrave.Material);
        }
        foreach (var (id, frame) in Frames) yield return ($"frames.{id}.material", frame.Material);
        yield return ("pilaster.material", Pilaster.Material);
        foreach (var (decor, styles) in Decors)
            foreach (var (style, surfaces) in styles)
                foreach (var (field, surface) in new[] { ("wall", surfaces.Wall), ("floor", surfaces.Floor), ("ceiling", surfaces.Ceiling) })
                    if (surface is not null) yield return ($"decors.{decor}.{style}.{field}", surface);
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

/// <summary>
/// A floor's trim style: for each trim role a space style names (a dressed room, a plain service space), the bands
/// that run along its walls, and for door frames, the architrave swept around their openings on both faces.
/// </summary>
internal sealed record TrimStyle(Dictionary<string, TrimBand[]> Bands, Dictionary<string, ArchitraveDefinition>? Architraves = null);

/// <summary>
/// An architrave: its cross-section as [out, across] points, swept up both jambs and across the head, out from the
/// wall face and across away from the opening, starting at the frame's outer edge.
/// </summary>
internal sealed record ArchitraveDefinition(string Material, float[][] Profile);

/// <summary>One horizontal trim run (skirting, picture rail, cornice): its height band and how far it stands proud.</summary>
/// <param name="Profile">
/// The moulding's cross-section as [out, up] points in metres (the same [out, across] shape as an architrave's), from the wall face at the band's foot round to the wall
/// face at its head: out from the wall face (0 to <see cref="Depth"/>), up from <see cref="From"/> (0 to To - From). A
/// band with a profile is built as an extruded moulding; one without is a plain box.
/// </param>
internal sealed record TrimBand(string Name, float From, float To, float Depth, string Material, float[][]? Profile = null);

/// <summary>
/// A door frame: jambs <see cref="JambWidth"/> wide, overlapping the opening by <see cref="Inset"/>, standing
/// <see cref="Proud"/> beyond both wall faces, with a head <see cref="HeadHeight"/> above the opening.
/// </summary>
internal sealed record FrameDefinition(string Material, float JambWidth, float Inset, float Proud, float HeadHeight);

/// <summary>
/// The pilaster that dresses an outer wall corner (where an open link ends and only one space's wall runs on) as a
/// square column <see cref="Width"/> across, centred on the corner; and the casing of an unframed passage's sides,
/// <see cref="Width"/> wide and standing <see cref="Proud"/> beyond both wall faces. Both rise from the floor to the
/// lower ceiling (a passage's, to its opening's top), so the papers and trim ends that meet there meet behind them.
/// </summary>
internal sealed record PilasterDefinition(string Material, float Width, float Proud);

/// <summary>A space's surface set and the trim role it takes from its floor's trim style. Spaces may override any surface.</summary>
internal sealed record SpaceStyle(string Wall, string Floor, string Ceiling, string Trim, SeamDefinition? Seams = null);

/// <summary>The surfaces a decor gives one space style; any left out keep the style's own.</summary>
internal sealed record DecorSurfaces(string? Wall = null, string? Floor = null, string? Ceiling = null);

/// <summary>Ceiling joints every <see cref="Spacing"/> metres along a space's long axis.</summary>
internal sealed record SeamDefinition(float Spacing, float Width, float Depth, string Material);
