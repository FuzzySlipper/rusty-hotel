using Hotel.Game.Content;
using Rusty.Engine;

namespace Hotel.Game.Scene;

/// <summary>
/// How the hotel is seen: the camera's tone mapping and exposure, the haze that swallows distance, and the detail of
/// the lamps' shadows.
/// </summary>
internal sealed record SceneLook(ToneMappingLook ToneMapping, FogLook Fog, ShadowLook Shadows)
{
    internal const string Path = "scene/look.json";

    internal static SceneLook Load(IEngineContext engine)
    {
        SceneLook look = Authored.Read(engine, Path, ContentJson.Default.SceneLook);
        Authored.Positive(Path, "toneMapping.exposure", look.ToneMapping.Exposure);
        Authored.Colour(Path, "fog.color", look.Fog.Color);
        Authored.AtLeast(Path, "fog.start", look.Fog.Start, 0);
        Authored.Require(look.Fog.Mode != FogMode.Linear || look.Fog.End > look.Fog.Start, Path, "fog.end", "linear fog must end beyond its start.");
        Authored.AtLeast(Path, "fog.density", look.Fog.Density, 0);
        Authored.Require(look.Shadows.Resolution is 256 or 512 or 1024 or 2048, Path, "shadows.resolution",
            "is 256, 512, 1024 or 2048 texels.");
        return look;
    }

    internal void Apply(ICameraViewService camera)
    {
        camera.SetToneMapping(new(ToneMapping.Operator, ToneMapping.Exposure));
        camera.SetFog(new(Fog.Mode, new Color(Fog.Color[0], Fog.Color[1], Fog.Color[2], 1), Fog.Start, Fog.End, Fog.Density));
    }
}

internal sealed record ToneMappingLook(ToneMappingOperator Operator, float Exposure);
/// <summary>
/// Every shadowed lamp's shadow detail: texels on a side of each of its six layers. Lamps reach a few metres, so a
/// small layer keeps the atlas small without visible loss.
/// </summary>
internal sealed record ShadowLook(uint Resolution);
internal sealed record FogLook(FogMode Mode, float[] Color, float Start, float End, float Density);
