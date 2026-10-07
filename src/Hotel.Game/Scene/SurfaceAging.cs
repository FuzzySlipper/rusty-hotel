using System.Numerics;
using Hotel.Game.Content;
using Rusty.Engine;

namespace Hotel.Game.Scene;

/// <summary>
/// How the hotel's surfaces have aged: named profiles a surface takes by its <c>aging</c>, drawn by the
/// <see cref="Shader"/> over the standard shading with the <see cref="Noise"/> map, all in world space so the marks run
/// across repeats and box edges.
/// </summary>
/// <param name="Shader">The product shader (content path) that ages a surface.</param>
/// <param name="Noise">Tileable linear noise the shader samples: red wear, green stains, blue seam lift, alpha grain.</param>
internal sealed record AgingCatalog(string Shader, string Noise, Dictionary<string, AgingProfile> Profiles)
{
    internal const string Path = "scene/aging.json";

    internal static AgingCatalog Load(IEngineContext engine)
    {
        AgingCatalog catalog = Authored.Read(engine, Path, ContentJson.Default.AgingCatalog);
        foreach ((string id, AgingProfile profile) in catalog.Profiles) profile.Validate($"profiles.{id}");
        return catalog;
    }

    /// <summary>The shader's four parameter vectors for one surface (laid out in content/shaders/aged.wgsl).</summary>
    internal static (Vector4, Vector4, Vector4, Vector4) Parameters(AgingProfile profile, SurfaceDefinition surface)
    {
        AgingStain? stain = profile.Stain;
        return (new(profile.Wear?.Strength ?? 0, profile.Wear?.Size ?? 1, profile.Fade?.Strength ?? 0, profile.Fade?.Reach ?? 1),
            new(stain?.Strength ?? 0, stain?.Size ?? 1, stain?.Foot ?? 0, stain?.Head ?? 0),
            new(profile.Seam is null ? 0 : surface.TileWidth, profile.Seam?.Strength ?? 0, 0, 0),
            stain is null ? Vector4.Zero : new(Authored.Vector(stain.Colour), 0));
    }
}

/// <summary>One way of aging; each part is optional.</summary>
internal sealed record AgingProfile(AgingWear? Wear = null, AgingFade? Fade = null, AgingStain? Stain = null, AgingSeam? Seam = null)
{
    internal void Validate(string field)
    {
        string path = AgingCatalog.Path;
        if (Wear is { } wear)
        {
            Authored.Within(path, $"{field}.wear.strength", wear.Strength, 0, 1);
            Authored.Positive(path, $"{field}.wear.size", wear.Size);
        }
        if (Fade is { } fade)
        {
            Authored.Within(path, $"{field}.fade.strength", fade.Strength, 0, 1);
            Authored.Positive(path, $"{field}.fade.reach", fade.Reach);
        }
        if (Stain is { } stain)
        {
            Authored.Within(path, $"{field}.stain.strength", stain.Strength, 0, 1);
            Authored.Positive(path, $"{field}.stain.size", stain.Size);
            Authored.Finite(path, $"{field}.stain.foot", stain.Foot);
            Authored.Finite(path, $"{field}.stain.head", stain.Head);
            Authored.Colour(path, $"{field}.stain.colour", stain.Colour);
        }
        if (Seam is { } seam) Authored.Within(path, $"{field}.seam.strength", seam.Strength, 0, 1);
    }
}

/// <summary>Broad patches darkened and dulled, about <see cref="Size"/> metres across.</summary>
internal sealed record AgingWear(float Strength, float Size);

/// <summary>Colour washed toward grey within <see cref="Reach"/> metres of each lamp, faces turned to it most.</summary>
internal sealed record AgingFade(float Strength, float Reach);

/// <summary>
/// Water stains about <see cref="Size"/> metres across, tinted by <see cref="Colour"/> with a darker tide line, rising
/// in from world height <see cref="Foot"/> to full at <see cref="Head"/>; a head at or below the foot stains everywhere.
/// </summary>
internal sealed record AgingStain(float Strength, float Size, float Foot, float Head, float[] Colour);

/// <summary>Paper lifting at its strip joins, one strip per repeat of the surface's texture.</summary>
internal sealed record AgingSeam(float Strength);
