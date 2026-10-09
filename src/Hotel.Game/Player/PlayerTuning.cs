using Hotel.Game.Content;
using Rusty.Engine;

namespace Hotel.Game.Player;

/// <summary>Investigator body, movement, look and camera tuning.</summary>
/// <param name="RunSpeed">Metres per second while running.</param>
/// <param name="RunStaminaPerSecond">Stamina a second of running spends; with none left the investigator walks.</param>
/// <param name="JumpHeight">How high a jump lifts the feet, metres.</param>
/// <param name="JumpStamina">Stamina one jump spends.</param>
internal sealed record PlayerTuning(float Height, float Radius, float EyeHeight, float Speed,
    float RunSpeed, float RunStaminaPerSecond, float JumpHeight, float JumpStamina, float Gravity, float MaximumStepHeight, float MaximumSlopeDegrees, float PointerRadiansPerUnit,
    float FieldOfViewDegrees)
{
    internal const string Path = "player/tuning.json";

    /// <summary>The Engine character controller for this body: the player's, and the one floor navigation is proven for.</summary>
    internal CharacterControllerConfig Controller(ISpatialService spatial)
    {
        CharacterControllerConfig baseline = spatial.DefaultCharacterControllerConfig();
        CharacterControllerConfig config = baseline with
        {
            Shape = baseline.Shape with { StandingHeight = Height, Radius = Radius },
            Ground = baseline.Ground with { ForwardSpeed = Speed, BackwardSpeed = Speed, StrafeSpeed = Speed },
            Air = baseline.Air with { MaximumSpeed = Speed, WishSpeedCap = Speed },
            Vertical = baseline.Vertical with { Gravity = Gravity },
            Surface = baseline.Surface with { MaximumStepHeight = MaximumStepHeight, MaximumSlopeRadians = MaximumSlopeDegrees * MathF.PI / 180 }
        };
        spatial.ValidateCharacterControllerConfig(config);
        return config;
    }

    /// <summary>The player's own body: the navigation controller, able to jump, and at a run its faster stride.</summary>
    internal CharacterControllerConfig Body(ISpatialService spatial, bool running)
    {
        CharacterControllerConfig walk = Controller(spatial);
        float speed = running ? RunSpeed : Speed;
        CharacterControllerConfig config = walk with
        {
            Ground = walk.Ground with { ForwardSpeed = speed, BackwardSpeed = Speed, StrafeSpeed = speed },
            Air = walk.Air with { MaximumSpeed = speed, WishSpeedCap = speed },
            Vertical = walk.Vertical with { JumpSpeed = MathF.Sqrt(2 * Gravity * JumpHeight) }
        };
        spatial.ValidateCharacterControllerConfig(config);
        return config;
    }
    internal static PlayerTuning Load(IEngineContext engine)
    {
        PlayerTuning t = Authored.Read(engine, Path, ContentJson.Default.PlayerTuning);
        Authored.Positive(Path, "height", t.Height);
        Authored.Within(Path, "radius", t.Radius, float.Epsilon, t.Height / 2);
        Authored.Within(Path, "eyeHeight", t.EyeHeight, 0, t.Height);
        Authored.Positive(Path, "speed", t.Speed);
        Authored.AtLeast(Path, "runSpeed", t.RunSpeed, t.Speed);
        Authored.AtLeast(Path, "runStaminaPerSecond", t.RunStaminaPerSecond, 0);
        Authored.Positive(Path, "jumpHeight", t.JumpHeight);
        Authored.AtLeast(Path, "jumpStamina", t.JumpStamina, 0);
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
    internal void Validate(string path, string field = "arrival")
    {
        Authored.Point(path, $"{field}.position", Position);
        Authored.Finite(path, $"{field}.yawDegrees", YawDegrees);
    }
}
