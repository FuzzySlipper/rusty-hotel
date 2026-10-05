using Hotel.Game.Content;
using Rusty.Engine;

namespace Hotel.Game.Player;

/// <summary>Investigator body, movement, look and camera tuning.</summary>
internal sealed record PlayerTuning(float Height, float Radius, float EyeHeight, float Speed,
    float Gravity, float MaximumStepHeight, float MaximumSlopeDegrees, float PointerRadiansPerUnit,
    float FieldOfViewDegrees)
{
    internal const string Path = "player/tuning.json";
    internal static PlayerTuning Load(IEngineContext engine)
    {
        PlayerTuning t = Authored.Read(engine, Path, ContentJson.Default.PlayerTuning);
        Authored.Positive(Path, "height", t.Height);
        Authored.Within(Path, "radius", t.Radius, float.Epsilon, t.Height / 2);
        Authored.Within(Path, "eyeHeight", t.EyeHeight, 0, t.Height);
        Authored.Positive(Path, "speed", t.Speed);
        Authored.Positive(Path, "gravity", t.Gravity);
        Authored.AtLeast(Path, "maximumStepHeight", t.MaximumStepHeight, 0);
        Authored.Within(Path, "maximumSlopeDegrees", t.MaximumSlopeDegrees, 0, 89);
        Authored.Positive(Path, "pointerRadiansPerUnit", t.PointerRadiansPerUnit);
        Authored.Within(Path, "fieldOfViewDegrees", t.FieldOfViewDegrees, 10, 170);
        return t;
    }
}

/// <summary>Where an excursion starts the player: body centre and facing.</summary>
internal sealed record ArrivalPlacement(float[] Position, float YawDegrees)
{
    internal void Validate(string path)
    {
        Authored.Point(path, "arrival.position", Position);
        Authored.Finite(path, "arrival.yawDegrees", YawDegrees);
    }
}
