using System.Numerics;
using Hotel.Game.Content;
using Hotel.Game.Input;
using Hotel.Game.Scene;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Input;

namespace Hotel.Game.Player;

internal sealed class HotelPlayer : IDisposable
{
    private readonly IEngineContext engine;
    private readonly HotelScene scene;
    private readonly PlayerTuning tuning;
    private readonly ArrivalPlacement arrival;
    private readonly CharacterControllerConfig config;
    private readonly Camera camera;
    private ulong commandSequence;
    private bool cut = true;

    internal HotelPlayer(IEngineContext engine, HotelScene scene, PlayerTuning tuning, ArrivalPlacement arrival, ControlBindings controls)
    {
        this.engine = engine;
        this.scene = scene;
        this.tuning = tuning;
        this.arrival = arrival;
        CharacterControllerConfig baseline = engine.Spatial.DefaultCharacterControllerConfig();
        config = baseline with
        {
            Shape = baseline.Shape with { StandingHeight = tuning.Height, Radius = tuning.Radius },
            Ground = baseline.Ground with { ForwardSpeed = tuning.Speed, BackwardSpeed = tuning.Speed, StrafeSpeed = tuning.Speed },
            Air = baseline.Air with { MaximumSpeed = tuning.Speed, WishSpeedCap = tuning.Speed },
            Vertical = baseline.Vertical with { Gravity = tuning.Gravity },
            Surface = baseline.Surface with { MaximumStepHeight = tuning.MaximumStepHeight, MaximumSlopeRadians = tuning.MaximumSlopeDegrees * MathF.PI / 180 }
        };
        engine.Spatial.ValidateCharacterControllerConfig(config);
        Input = new FpsInput(FpsInputConfig.Standard with
        {
            Bindings = HotelControls.Fps(controls, FpsInputConfig.Standard.Bindings),
            PointerLookConfig = FpsInputConfig.Standard.PointerLookConfig with
            { HorizontalRadiansPerUnit = tuning.PointerRadiansPerUnit, VerticalRadiansPerUnit = tuning.PointerRadiansPerUnit }
        });
        Reset();
        camera = engine.CameraView.CreateCamera(Descriptor());
        try { engine.CameraView.SetActiveCamera(camera); }
        catch { camera.Dispose(); throw; }
    }

    internal PlayerTuning Tuning => tuning;
    internal FpsInput Input { get; }
    internal LookState LookState { get; private set; }
    internal Vector3 Position => scene.Entities.Get(scene.PlayerEntity, EngineComponentTypes.Transform).Translation;
    internal CharacterMotion Motion => scene.Entities.Get(scene.PlayerEntity, EngineComponentTypes.CharacterMotion);
    internal Vector3 Eye => Position + Vector3.UnitY * (tuning.EyeHeight - tuning.Height / 2);
    internal Vector3 Forward => Look.IntegrateClamped(new LookRequest(LookState, Vector2.Zero, Input.Config.PointerLookConfig)).Forward;

    internal FpsInputFrame ReadInput(ReadOnlySpan<ProductInputEvent> events, float admittedSeconds)
    {
        FpsInputFrame frame = Input.Consume(events, admittedSeconds);
        LookState = Input.IntegrateLook(LookState, frame).After;
        return frame;
    }

    internal void Step(FpsInputFrame input, float delta, ReadOnlyMemory<CharacterObstacle> obstacles = default)
    {
        CharacterStepReceipt receipt = engine.Spatial.ProposeCharacterStep(new CharacterStepRequest(
            scene.Session, Position, Motion, default, obstacles,
            ReadOnlyMemory<CharacterMeshInstance>.Empty, config,
            new CharacterControllerCommand(input.Movement, LookState.YawRadians, false, false, false,
                Vector3.Zero, Vector3.Zero, delta, ++commandSequence)));
        scene.Entities.Set(scene.PlayerEntity, EngineComponentTypes.Transform, receipt.Transform);
        scene.Entities.Set(scene.PlayerEntity, EngineComponentTypes.CharacterMotion, receipt.Motion);
    }

    internal void ClearInput() => Input.Physical.Clear();

    internal void LookBy(double yawDegrees, double pitchDegrees)
    {
        LookState = Look.IntegrateClamped(new LookRequest(LookState,
            new Vector2((float)(yawDegrees * Math.PI / 180), (float)(pitchDegrees * Math.PI / 180)),
            Input.Config.PointerLookConfig with
            { HorizontalRadiansPerUnit = 1, VerticalRadiansPerUnit = 1, InvertVertical = false })).After;
    }

    internal void Reset()
    {
        Vector3 position = Authored.Vector(arrival.Position);
        scene.Entities.Set(scene.PlayerEntity, EngineComponentTypes.Transform, new Transform(position, Quaternion.Identity, Vector3.One));
        scene.Entities.Set(scene.PlayerEntity, EngineComponentTypes.CharacterMotion, new CharacterMotion(Vector3.Zero, Vector3.Zero,
            false, CharacterStance.Standing, 0, 0, 0, false, 0, Vector3.Zero, Vector3.Zero, Quaternion.Identity,
            Vector3.Zero, position.Y, position.Y, 0, 0));
        LookState = new(arrival.YawDegrees * MathF.PI / 180, 0);
        ClearInput();
        cut = true;
    }

    internal void Publish(double sampleTime)
    {
        engine.CameraView.UpdateCameraSample(new CameraSampleRequest(camera, Descriptor(), sampleTime,
            1d / 60, CameraInterpolation.Position, cut ? (byte)1 : (byte)0));
        cut = false;
    }

    public void Dispose()
    {
        engine.CameraView.ClearActiveCamera(new ClearActiveCameraRequest(0));
        camera.Dispose();
    }

    private CameraDescriptor Descriptor() => new(new CameraPose(Eye,
        LookState.PitchRadians * 180 / Math.PI, LookState.YawRadians * 180 / Math.PI), CameraBasisMode.Derived,
        default, new CameraProjection(CameraProjectionKind.Perspective, tuning.FieldOfViewDegrees, 0, .05, 80),
        new CameraViewport(0, 0, 1, 1));
}
