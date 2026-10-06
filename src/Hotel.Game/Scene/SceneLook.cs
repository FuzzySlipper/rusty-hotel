using Hotel.Game.Content;
using Rusty.Engine;

namespace Hotel.Game.Scene;

/// <summary>
/// How the hotel is seen: the camera's tone mapping and exposure, the haze that swallows distance, and which lamps may
/// cast shadows at once.
/// </summary>
internal sealed record SceneLook(ToneMappingLook ToneMapping, FogLook Fog, ShadowFocus Shadows)
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
        Authored.AtLeast(Path, "shadows.budget", look.Shadows.Budget, 0);
        Authored.AtLeast(Path, "shadows.otherRoomPenalty", look.Shadows.OtherRoomPenalty, 0);
        Authored.AtLeast(Path, "shadows.hysteresis", look.Shadows.Hysteresis, 0);
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
/// The shadow budget: each shadowed point light renders the scene six more times a frame, so only <see cref="Budget"/>
/// lamps cast at once, the best placed for the eye. A lamp in another room counts as <see cref="OtherRoomPenalty"/>
/// metres farther; a lamp already casting counts as <see cref="Hysteresis"/> metres nearer, so the choice does not flicker.
/// </summary>
internal sealed record ShadowFocus(int Budget, float OtherRoomPenalty, float Hysteresis);
internal sealed record FogLook(FogMode Mode, float[] Color, float Start, float End, float Density);
