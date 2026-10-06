using System.Numerics;
using Hotel.Game.Actions;
using Hotel.Game.Combat;
using Hotel.Game.Scene;
using Rusty.Engine;
using Rusty.Engine.Entities;

namespace Hotel.Game.Residents;

/// <summary>
/// What a ready resident does with its step, from its kind's parts: with a target, turn to it, back away if it flees
/// and is hurt, start the first of its actions that is ready, affordable and in reach, or else close in within its
/// leash; without one, keep its post, walk its patrol, or walk back. Adapted from rusty-dagger's Pursuit and Wander
/// coordinators (docs/reuse.md): the decision is product policy, every step is an Engine character step on admitted time.
/// </summary>
internal sealed class ResidentConduct(IEngineContext engine, HotelScene scene, ActionCatalog actions)
{
    // Within this of a patrol point or its post, a resident has arrived; held this long by a wall, it gives the point up.
    private const float Arrived = .3f, Blocked = 1;

    /// <summary>Takes one resident's step; returns the action it started, if any.</summary>
    internal ActionDefinition? Step(HotelEnemy resident, float seconds)
    {
        if (resident.User.Busy) return null;
        ResidentMovement movement = resident.Kind.Movement;
        if (resident.Awareness.Target is not { } target) { Idle(resident, movement, seconds); return null; }
        Vector3 toward = target.Eye - resident.Eye;
        float distance = toward.Length();
        resident.Yaw = Yaw(toward);
        if (movement.Flee is { } flee && resident.Health.Value < resident.Health.MaximumValue * flee.Below)
        {
            if (Flat(toward).Length() < flee.Distance) Walk(resident, Yaw(-toward), seconds);
            return null;
        }
        foreach (ActionChoice choice in resident.Kind.Actions)
        {
            ActionDefinition action = actions.Action(choice.Action)!;
            if (distance < choice.Minimum || distance > HotelCombat.Reach(action) || resident.User.Readiness(action) != ActionRefusal.None ||
                action.Cost.Tracks.Any(c => resident.Stats.Track(c.Key).Value < c.Value)) continue;
            foreach (var (track, amount) in action.Cost.Tracks) resident.Stats.Track(track).TrySpend(amount);
            // A committed direction: strafing can evade it.
            resident.User.Begin(action, Vector3.Normalize(toward));
            return action;
        }
        // Close in only while no action's distance window holds the target, and only within the leash of the post.
        bool inReach = resident.Kind.Actions.Any(c => distance >= c.Minimum && distance <= HotelCombat.Reach(actions.Action(c.Action)!));
        if (movement.Post is null && !inReach && Flat(target.Position - resident.Spawn).Length() <= movement.Leash)
            Walk(resident, resident.Yaw, seconds);
        return null;
    }

    private void Idle(HotelEnemy resident, ResidentMovement movement, float seconds)
    {
        switch (movement.Kind)
        {
            case MovementKind.Patrol:
                PatrolMovement patrol = movement.Patrol!;
                if (resident.PatrolPause > 0) { resident.PatrolPause -= seconds; return; }
                float[] offset = patrol.Points[resident.PatrolIndex % patrol.Points.Length];
                Vector3 point = resident.Spawn + new Vector3(offset[0], 0, offset[1]);
                if (Flat(point - resident.Position).Length() <= Arrived || resident.PatrolBlocked >= Blocked)
                {
                    resident.PatrolIndex = (resident.PatrolIndex + 1) % patrol.Points.Length;
                    resident.PatrolPause = patrol.Pause;
                    resident.PatrolBlocked = 0;
                    return;
                }
                float before = Flat(point - resident.Position).Length();
                Walk(resident, Yaw(point - resident.Position), seconds);
                // A point behind a wall is given up rather than walked into for ever: progress toward it, not sliding, counts.
                bool held = before - Flat(point - resident.Position).Length() < resident.Kind.Movement.Speed * seconds * .2f;
                resident.PatrolBlocked = held ? resident.PatrolBlocked + seconds : 0;
                return;
            case MovementKind.Stalk:
            case MovementKind.Ambush:
                if (Flat(resident.Spawn - resident.Position).Length() > Arrived) Walk(resident, Yaw(resident.Spawn - resident.Position), seconds);
                return;
        }
    }

    // One Engine character step facing a yaw, at the resident's pace; only hotel geometry constrains it.
    private void Walk(HotelEnemy resident, float yaw, float seconds)
    {
        if (resident.Kind.Movement.Speed <= 0) return;
        resident.Yaw = yaw;
        CharacterStepReceipt move = engine.Spatial.ProposeCharacterStep(new(scene.Session,
            resident.Position, resident.Motion, default, ReadOnlyMemory<CharacterObstacle>.Empty, ReadOnlyMemory<CharacterMeshInstance>.Empty,
            resident.Controller, new(new(0, resident.Stats.Pace), yaw, false, false, false, default, default, seconds, ++resident.Sequence)));
        scene.Entities.Set(resident.EntityId, EngineComponentTypes.Transform, move.Transform);
        scene.Entities.Set(resident.EntityId, EngineComponentTypes.CharacterMotion, move.Motion);
    }

    private static Vector3 Flat(Vector3 v) => new(v.X, 0, v.Z);
    private static float Yaw(Vector3 toward) => MathF.Atan2(toward.X, -toward.Z);
}
