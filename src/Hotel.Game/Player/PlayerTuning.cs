using Hotel.Game.Content;
using Rusty.Engine;

namespace Hotel.Game.Player;

/// <summary>Investigator body, movement, look and camera tuning.</summary>
internal sealed record PlayerTuning(float Height, float Radius, float EyeHeight, float Speed,
    float Gravity, float MaximumStepHeight, float MaximumSlopeDegrees, float PointerRadiansPerUnit,
    float FieldOfViewDegrees)
{
    internal const string Path = "player/tuning.json";
    internal static PlayerTuning Load(IEngineContext engine) => Authored.Read(engine, Path, ContentJson.Default.PlayerTuning);
}

/// <summary>Where an excursion starts the player: body centre and facing.</summary>
internal sealed record ArrivalPlacement(float[] Position, float YawDegrees);
