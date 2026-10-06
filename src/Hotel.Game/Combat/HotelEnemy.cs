using System.Numerics;
using Hotel.Game.Actions;
using Hotel.Game.Content;
using Hotel.Game.Mechanics;
using Hotel.Game.Residents;
using Hotel.Game.Scene;
using Hotel.Game.Supplies;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;

namespace Hotel.Game.Combat;

/// <summary>One placed resident: its body in the scene, its Engine stats and effects, and its action timing.</summary>
internal sealed class HotelEnemy : IActionActor
{
    private readonly HotelScene scene;
    internal HotelEnemy(IEngineContext engine, HotelScene scene, ResidentPlacement placement, ResidentKind kind, float gravity,
        MechanicsDefinition mechanics)
    {
        this.scene = scene; Kind = kind; Id = placement.Id; Spawn = Authored.Vector(placement.Position);
        EntityId = scene.Entities.Create();
        Stats = new(mechanics, kind.Stats, EntityId);
        CharacterControllerConfig baseline = engine.Spatial.DefaultCharacterControllerConfig();
        Controller = baseline with
        {
            Shape = baseline.Shape with { StandingHeight = kind.Height, Radius = kind.Radius },
            Ground = baseline.Ground with { ForwardSpeed = kind.Movement.Speed, BackwardSpeed = kind.Movement.Speed, StrafeSpeed = kind.Movement.Speed },
            Vertical = baseline.Vertical with { Gravity = gravity }
        };
        engine.Spatial.ValidateCharacterControllerConfig(Controller);
        Reset();
    }
    /// <summary>Saved identity of this placed resident.</summary>
    internal string Id { get; }
    internal ResidentKind Kind { get; }
    internal EntityId EntityId { get; }
    public ulong Entity => EntityId.Value;
    /// <summary>This resident's Engine stats, built from its kind's block.</summary>
    public ActorStats Stats { get; }
    /// <summary>This resident's action in progress and cooldowns.</summary>
    internal ActionUser User { get; } = new();
    /// <summary>What it has noticed and for how long it remembers; not saved.</summary>
    internal Awareness Awareness { get; } = new();
    /// <summary>The patrol point it walks toward and how long it still pauses there; not saved.</summary>
    internal int PatrolIndex;
    internal float PatrolPause, PatrolBlocked;
    internal Track Health => Stats.Track(HotelSupplies.HealthTrack);
    internal CharacterControllerConfig Controller { get; }
    internal Vector3 Spawn { get; }
    public Vector3 Position => scene.Entities.Get(EntityId, EngineComponentTypes.Transform).Translation;
    internal CharacterMotion Motion => scene.Entities.Get(EntityId, EngineComponentTypes.CharacterMotion);
    public Vector3 Eye => Position + new Vector3(0, Kind.EyeHeight, 0);
    public bool Alive => Health.Value > 0;
    // Residents wear nothing; their contributions come from the effects they bear.
    public IEnumerable<ActiveContribution> Contributions => Stats.Effects.HitContributions;
    public IEnumerable<string> HitEffects => [];
    /// <summary>What the resident is doing: fallen, held, or its action's phase.</summary>
    internal AttackPhase Phase => !Alive ? AttackPhase.Defeated : Stats.Effects.Held ? AttackPhase.Interrupted : User.Phase switch
    {
        ActionPhase.Windup => AttackPhase.Windup, ActionPhase.Commit => AttackPhase.Commit,
        ActionPhase.Recovery => AttackPhase.Recovery, _ => AttackPhase.Ready
    };
    internal float Yaw, BeamTime;
    internal Vector3 BeamEnd;
    internal ulong Sequence;
    private Vector3 Half => new(Kind.Radius, Kind.Height / 2, Kind.Radius);
    public SpatialEntityCollider Hitbox => new(Entity, Position - Half, Position + Half, 2, uint.MaxValue, true, false, false);
    internal CharacterObstacle Obstacle => new(Entity, new(Position, Quaternion.Identity, Vector3.One), -Half, Half, true, default, default);
    internal void Reset()
    {
        Stats.Reset(); User.Reset(); Awareness.Reset(); BeamTime = Yaw = PatrolPause = PatrolBlocked = 0; PatrolIndex = 0;
        scene.Entities.Set(EntityId, EngineComponentTypes.Transform, new(Spawn, Quaternion.Identity, Vector3.One));
        scene.Entities.Set(EntityId, EngineComponentTypes.CharacterMotion, new(Vector3.Zero, Vector3.Zero,
            false, CharacterStance.Standing, 0, 0, 0, false, 0, Vector3.Zero, Vector3.Zero, Quaternion.Identity,
            Vector3.Zero, Spawn.Y, Spawn.Y, 0, 0));
    }
}
