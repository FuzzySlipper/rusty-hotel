using System.Numerics;
using Hotel.Game.Content;
using Hotel.Game.Residents;
using Hotel.Game.Scene;
using Rusty.Engine;

namespace Hotel.Game.Combat;

/// <summary>
/// The residents as rigged models in the combat snapshot: each plays its look's clip for what it is doing (standing,
/// walking, recoiling, fallen), and its attack clip is sampled at the action's progress, so the windup runs up to the
/// strike as the action commits. Its tell light flares at its eye during windup and commit, and a hitscan's beam shows
/// while it lasts.
/// Animation runs on the Engine's world time, so it holds with the world. A resident whose look names a ragdoll rig
/// falls as a ragdoll when felled (an Engine Dynamics world over the floor's collision, pushed along the felling blow),
/// and its remains move to where it comes to rest; one already fallen when the view is built (a restored floor) lies in
/// its fall clip's last frame.
/// </summary>
internal sealed class ResidentView : IDisposable
{
    // How fast a resident must be moving for its walk to play, metres per second.
    private const float Walking = .15f;
    // Crossfade between looping clips, seconds.
    private const float Fade = .18f;
    // How much of the attack clip after the strike belongs to the commit; the rest is recovery.
    private const float CommitShare = .25f;
    private readonly IEngineContext engine;
    private readonly HotelScene scene;
    private readonly HotelCombat combat;
    private readonly Dictionary<string, RenderResource> models = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MeshResource> boxes = [];
    private readonly List<MeshResource> meshes = [];
    private readonly List<Appearance> appearances = [];
    private readonly Dictionary<string, Body> bodies = [];
    private readonly RagdollCatalog ragdolls;
    // Each model's ragdoll description against its joints, by model path.
    private readonly Dictionary<string, (DynamicsRagdollBone[] Bones, DynamicsRagdollLink[] Links, int Hit)> rigs = new(StringComparer.Ordinal);
    // Made at the first fall, over the floor's collision; ragdolls are presentation, so nothing else steps or reads it.
    private DynamicsWorld? world;

    internal ResidentView(IEngineContext engine, HotelScene scene, HotelCombat combat, CombatDefinition definition)
    {
        this.engine = engine; this.scene = scene; this.combat = combat;
        ragdolls = definition.Ragdolls;
        try
        {
            foreach (HotelEnemy enemy in combat.Enemies)
            {
                ResidentLook look = definition.Look(enemy.Kind);
                if (!models.TryGetValue(look.Model, out RenderResource? model))
                {
                    using ContentReference content = engine.Content.OpenReference(new(look.Model));
                    model = engine.Animation.OpenAnimatedMeshFromContent(new(content));
                    models.Add(look.Model, model);
                    string[] clips = engine.Animation.ReadClips(model).ToArray().Select(c => c.Id).ToArray();
                    foreach (string clip in look.Clips.All)
                        if (!clips.Contains(clip))
                            throw new InvalidDataException($"Resident model '{look.Model}' has no clip '{clip}' (content/{LookCatalog.Path} " +
                                $"'{look.Id}'). Its clips: {string.Join(", ", clips)}.");
                    if (look.Ragdoll is { } rigId)
                    {
                        RagdollRig rig = ragdolls.Rigs[rigId];
                        var (rigBones, rigLinks) = rig.Build(rigId, engine.Animation.ReadJoints(model).Span, look.Model);
                        rigs[look.Model] = (rigBones, rigLinks, rig.BoneOf(ragdolls.Hit.Bone));
                    }
                }
                Appearance appearance = engine.Animation.CreateAnimatedMeshAppearance(new(model));
                appearances.Add(appearance);
                bodies.Add(enemy.Id, new(scene.Entities.Create().Value, appearance, look, Box("tell-amber", default, Vector3.One),
                    HotelScene.ResidentLightIds + (ulong)bodies.Count));
            }
        }
        catch { Dispose(); throw; }
    }

    private Part Box(string material, Vector3 offset, Vector3 size)
    {
        if (!boxes.TryGetValue(material, out MeshResource? mesh))
        {
            mesh = RoomGeometry.Box(engine, scene.Surface(material), new(-.5f), new(.5f), Vector2.One);
            meshes.Add(mesh); boxes.Add(material, mesh);
        }
        Appearance appearance = engine.Graphics.CreateMeshAppearance(mesh);
        appearances.Add(appearance);
        return new(scene.Entities.Create().Value, appearance, offset, size);
    }

    /// <summary>Adds each resident's body, tells and beam to the combat snapshot.</summary>
    internal void Publish(List<AppearanceFact> facts)
    {
        foreach (HotelEnemy enemy in combat.Enemies)
        {
            Body body = bodies[enemy.Id];
            Quaternion facing = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -enemy.Yaw);
            Vector3 feet = enemy.Position - new Vector3(0, enemy.Kind.Height / 2, 0);
            Quaternion turned = facing * Quaternion.CreateFromAxisAngle(Vector3.UnitY, body.Look.YawDegrees * MathF.PI / 180);
            facts.Add(new(body.Entity, false, 0, new(feet, turned, new(body.Look.Scale)), body.Appearance, true, RenderLayer.Scene));
            bool showBeam = enemy.Alive && enemy.BeamTime > 0;
            Vector3 delta = enemy.BeamEnd - enemy.Eye;
            float length = delta.Length();
            Quaternion beamRotation = length > .001f ? Facing(delta / length) : Quaternion.Identity;
            facts.Add(new(body.Beam.Entity, false, 0, new((enemy.Eye + enemy.BeamEnd) / 2, beamRotation,
                new(.045f, .045f, Math.Max(.001f, length))), body.Beam.Appearance, showBeam, RenderLayer.Scene));
        }
    }

    /// <summary>
    /// Sets each resident's clip once its body is in the published snapshot: a looping clip when its state changes, the
    /// attack clip sampled at the action's progress, and the fall played once to its last frame.
    /// </summary>
    internal void Animate()
    {
        foreach (HotelEnemy enemy in combat.Enemies)
        {
            Body body = bodies[enemy.Id];
            if (body.Instance is null)
            {
                body.Instance = engine.Animation.CreateInstance(new(body.Appearance, body.Entity));
                // A resident that can fall as a ragdoll reports its joints, so the ragdoll spawns in the pose it was drawn in.
                if (body.Look.Ragdoll is not null && enemy.Alive) engine.Animation.SetPose(AnimationPoseRequest.ReportOnly(body.Instance));
            }
            // A resident restored to life (a recovered checkpoint) leaves its ragdoll for its clips.
            if (enemy.Alive && body.Ragdoll is not null) { body.Ragdoll.Dispose(); body.Ragdoll = null; body.Clip = null; }
            // Felled while this view watched: it falls as a ragdoll; one restored fallen plays its fall clip below.
            if (!enemy.Alive && body.Ragdoll is null && body.Clip is not null && body.Clip != body.Look.Clips.Fall && Fall(enemy, body))
            {
                body.Clip = body.Look.Clips.Fall;
                continue;
            }
            Tell(enemy, body);
            ResidentClips clips = body.Look.Clips;
            float strike = body.Look.StrikeAt, progress = enemy.User.PhaseProgress;
            float? sample = enemy.Phase switch
            {
                AttackPhase.Windup => strike * progress,
                AttackPhase.Commit => strike + (1 - strike) * CommitShare * progress,
                AttackPhase.Recovery => strike + (1 - strike) * (CommitShare + (1 - CommitShare) * progress),
                _ => null
            };
            if (sample is { } at)
            {
                // The attack clip is started once, then held at the action's progress.
                if (body.Clip != clips.Attack)
                    engine.Animation.SetPlayback(new(body.Instance, AnimationPlaybackKind.Play, clips.Attack, AnimationLoopMode.Once, 1, 1, true, Fade, true, 0));
                engine.Animation.SetPlayback(new(body.Instance, AnimationPlaybackKind.Sample, clips.Attack, AnimationLoopMode.Once, 1, 1, false, 0, false,
                    Math.Clamp(at, 0, 1)));
                body.Clip = clips.Attack;
                continue;
            }
            Vector3 velocity = enemy.Motion.ControlledVelocity;
            bool walking = new Vector2(velocity.X, velocity.Z).Length() > Walking;
            (string clip, AnimationLoopMode loop) = enemy.Phase switch
            {
                AttackPhase.Defeated => (clips.Fall, AnimationLoopMode.Once),
                AttackPhase.Interrupted => (clips.Hurt, AnimationLoopMode.Once),
                _ => walking ? (clips.Walk, AnimationLoopMode.Repeat) : (clips.Idle, AnimationLoopMode.Repeat)
            };
            if (clip == body.Clip) continue;
            engine.Animation.SetPlayback(new(body.Instance, AnimationPlaybackKind.Play, clip, loop, 1, 1, true, Fade, true, 0));
            body.Clip = clip;
        }
    }

    // Spawns a felled resident's ragdoll where it was drawn and pushes it along the blow (or slumps it); false leaves it to
    // its fall clip.
    private bool Fall(HotelEnemy enemy, Body body)
    {
        if (!rigs.TryGetValue(body.Look.Model, out var rig)) return false;
        if (world is null)
        {
            world = engine.Dynamics.CreateWorld(new DynamicsWorldConfig(new Vector3(0, -ragdolls.Gravity, 0)));
            engine.Dynamics.BindWorldCollision(new DynamicsWorldCollisionBindingRequest(world, scene.Session));
        }
        try
        {
            body.Ragdoll = engine.Dynamics.CreateRagdoll(new(world, body.Instance!, rig.Bones, rig.Links, RagdollGroup, ~RagdollGroup,
                ragdolls.Friction, 0, ragdolls.LinearDamping, ragdolls.AngularDamping, 1));
        }
        catch (EngineCallException) { return false; }
        Transform struck = engine.Dynamics.ReadRagdoll(body.Ragdoll).Bones.Span[rig.Hit];
        (Vector3 at, Vector3 push) = enemy.FelledBy is { } blow
            ? (blow.Point, ragdolls.Hit.Push(blow.Direction, blow.Damage))
            : (struck.Translation, -Vector3.UnitY * ragdolls.Slump);
        engine.Dynamics.ApplyRagdollImpulse(new(body.Ragdoll, (uint)rig.Hit, at, push));
        return true;
    }

    /// <summary>
    /// Steps the falling bodies by admitted time; a body that has come to rest moves its resident's remains to where its
    /// hips lie (kept within reach of where it stood, so its saved position stays one it could have walked to).
    /// </summary>
    internal void Step(float seconds)
    {
        if (world is null || !bodies.Values.Any(b => b.Ragdoll is not null && !b.Rested)) return;
        engine.Dynamics.Step(new(world, seconds, 1, ReadOnlyMemory<DynamicsAction>.Empty));
        foreach (HotelEnemy enemy in combat.Enemies)
        {
            Body body = bodies[enemy.Id];
            if (body.Ragdoll is null || body.Rested) continue;
            DynamicsRagdollResult lying = engine.Dynamics.ReadRagdoll(body.Ragdoll);
            if (!lying.Resting) continue;
            body.Rested = true;
            Vector3 hips = lying.Bones.Span[0].Translation;
            if (Vector2.Distance(new(hips.X, hips.Z), new(enemy.Spawn.X, enemy.Spawn.Z)) < enemy.Kind.Movement.Range + .9f) enemy.Lie(hips);
        }
    }

    // Ragdolls collide with the floor and other bodies, not with each other.
    private const uint RagdollGroup = 1u << 7;

    // The rotation that turns -z to look along a direction.
    private static Quaternion Facing(Vector3 forward)
    {
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        Vector3 up = Vector3.Cross(right, forward);
        return Quaternion.CreateFromRotationMatrix(new(right.X, right.Y, right.Z, 0, up.X, up.Y, up.Z, 0,
            -forward.X, -forward.Y, -forward.Z, 0, 0, 0, 0, 1));
    }

    // The tell light: lit at the eye while the resident winds up and commits, put out otherwise.
    private void Tell(HotelEnemy enemy, Body body)
    {
        bool telling = enemy.Alive && enemy.Phase is AttackPhase.Windup or AttackPhase.Commit;
        if (!telling) { body.Light?.Dispose(); body.Light = null; return; }
        TellGlow tell = body.Look.Tell;
        LightDescriptor descriptor = new(LightKind.Point, Authored.Vector(tell.Colour), tell.Intensity, true,
            enemy.Eye + new Vector3(0, tell.Lift, 0), -Vector3.UnitY, true, tell.Range, 2, 0, 0, LightShadowIntent.Disabled);
        if (body.Light is null) body.Light = engine.Graphics.CreateLight(new(body.LightId, false, 0, descriptor));
        else engine.Graphics.UpdateLight(new(body.Light, new(body.LightId, false, 0, descriptor)));
    }

    public void Dispose()
    {
        foreach (Body body in bodies.Values) body.Light?.Dispose();
        foreach (Body body in bodies.Values) body.Ragdoll?.Dispose();
        foreach (Body body in bodies.Values) body.Instance?.Dispose();
        world?.Dispose();
        foreach (Appearance appearance in appearances) appearance.Dispose();
        foreach (MeshResource mesh in meshes) mesh.Dispose();
        foreach (RenderResource model in models.Values) model.Dispose();
    }

    private sealed record Part(ulong Entity, Appearance Appearance, Vector3 Offset, Vector3 Size);

    private sealed class Body(ulong entity, Appearance appearance, ResidentLook look, Part beam, ulong lightId)
    {
        internal ulong Entity { get; } = entity;
        internal Appearance Appearance { get; } = appearance;
        internal ResidentLook Look { get; } = look;
        internal Part Beam { get; } = beam;
        internal AnimationInstance? Instance { get; set; }
        internal string? Clip { get; set; }
        internal ulong LightId { get; } = lightId;
        internal DynamicsRagdoll? Ragdoll { get; set; }
        internal bool Rested { get; set; }
        internal Light? Light { get; set; }
    }
}
