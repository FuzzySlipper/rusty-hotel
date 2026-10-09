using System.Numerics;
using Hotel.Game.Actions;
using Hotel.Game.Player;
using Hotel.Game.Content;
using Hotel.Game.Residents;
using Hotel.Game.Scene;
using Hotel.Game.Supplies;
using Rusty.Engine;

namespace Hotel.Game.Combat;

/// <summary>The combat presentation in the existing scene snapshot: the residents (<see cref="ResidentView"/>), what the hands hold, and flares in flight.</summary>
internal sealed class CombatView : IDisposable
{
    private readonly IEngineContext engine;
    private readonly HotelScene scene;
    private readonly HotelPlayer player;
    private readonly HotelCombat combat;
    private readonly List<MeshResource> meshes = [];
    private readonly List<Appearance> appearances = [];
    private readonly Dictionary<string, MeshResource> boxes = [];
    private readonly ResidentView residents;
    // The model each held look shows, and the flash at a firearm's commit.
    private readonly List<RenderResource> models = [];
    private readonly Dictionary<string, (ulong Entity, Appearance Appearance, HeldModel Model)> held = new(StringComparer.Ordinal);
    private readonly HeldCatalog heldLooks;
    // The action whose motion is playing on a held look, and the last action start seen.
    private (string Look, ulong Started, TweenHandle Tween)? moving;
    private ulong startedSeen;
    private readonly Part muzzle;
    // Enough flare boxes for the shots one hand can have in flight at once.
    private const int ShownProjectiles = 6;
    private readonly Part[] flares;

    internal CombatView(IEngineContext engine, HotelScene scene, HotelPlayer player, HotelCombat combat, CombatDefinition definition)
    {
        this.engine = engine; this.scene = scene; this.player = player; this.combat = combat;
        try
        {
            residents = new ResidentView(engine, scene, combat, definition);
            heldLooks = definition.Held;
            foreach (HeldModel look in heldLooks.Looks)
            {
                using ContentReference content = engine.Content.OpenReference(new(look.Model));
                // The pinned SDK admits GLB through Animation even for a static, unrigged prop.
                RenderResource model = engine.Animation.OpenAnimatedMeshFromContent(new(content));
                models.Add(model);
                Appearance appearance = engine.Animation.CreateAnimatedMeshAppearance(new(model));
                appearances.Add(appearance);
                held[look.Look] = (scene.Entities.Create().Value, appearance, look);
            }
            muzzle = Box("tell-amber", default, Authored.Vector(heldLooks.FlashSize));
            flares = Enumerable.Range(0, ShownProjectiles).Select(_ => Box("tell-amber", default, new(.16f))).ToArray();
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

    internal void Publish()
    {
        List<AppearanceFact> facts = [];
        void Place(Part part, Vector3 origin, Quaternion rotation, bool visible, Vector3? offset = null, Vector3? size = null)
            => facts.Add(new(part.Entity, false, 0,
                new(origin + Vector3.Transform(offset ?? part.Offset, rotation), rotation, size ?? part.Size), part.Appearance, visible, RenderLayer.Scene));
        residents.Publish(facts);
        Quaternion camera = Facing(player.Forward);
        HeldMotionCatalog motion = heldLooks.Motion;
        // The developer motion viewer shows its look in place of what the hands hold.
        string? look = viewing?.Look ?? combat.Holding?.Item.Wear!.Look;
        foreach (var (kind, (entity, appearance, model)) in held)
        {
            // Published at rest: an action's motion is the Engine tween over it. The viewmodel layer is drawn in camera
            // space (right, up, back) under its own light rig. The viewer may hold a key's pose instead.
            HeldPose pose = viewing is { Pose: { } shownPose } && viewing.Value.Look == kind ? shownPose : motion.Motions[model.Motion].Poses[HeldMotionCatalog.Rest];
            (Vector3 at, Quaternion turn) = pose.Place(model);
            facts.Add(new(entity, false, 0, new(at, turn, new(model.Scale)), appearance, !combat.Defeated && look == kind, RenderLayer.Viewmodel));
        }
        // A firearm's flash shows while it commits, where its muzzle is in the pose the action holds then.
        ActionDefinition? current = combat.User.Current;
        bool firing = combat.Phase == AttackPhase.Commit && current?.Delivery.Kind is DeliveryKind.Hitscan or DeliveryKind.Projectile;
        Vector3 flash = Vector3.Zero;
        if (firing && look is { } shown && heldLooks.Model(shown) is { Muzzle: { } muzzlePoint } firearm)
        {
            HeldPose pose = motion.PoseAt(firearm, current!, current!.Timing.Windup);
            flash = Authored.Vector(pose.Position) + Vector3.Transform(Authored.Vector(muzzlePoint), pose.Turn);
        }
        else firing = false;
        Place(muzzle, player.Eye, camera, !combat.Defeated && firing, flash);
        for (int i = 0; i < flares.Length; i++)
            Place(flares[i], i < combat.Projectiles.Count ? combat.Projectiles[i].Position : Vector3.Zero, Quaternion.Identity, i < combat.Projectiles.Count);
        scene.PublishCombat(facts.ToArray());
        if (viewing is null) Move(look);
        residents.Animate();
    }

    // Starts the Engine tween that carries the held model through an action as the action starts, and eases it back to
    // rest when the action is cut short (interrupted, held, overwhelmed) or the hands change what they hold.
    private void Move(string? look)
    {
        HeldMotionCatalog motion = heldLooks.Motion;
        ActionDefinition? current = combat.User.Current;
        if (moving is { } playing && (current is null || playing.Look != look) && playing.Started == combat.User.Started && Running(playing.Tween))
        {
            Motion = engine.Tween.Start(new TweenStartRequest(held[playing.Look].Entity, motion.Settle()) with { Start = TweenStart.FromPresented }).Tween;
            moving = null;
        }
        if (combat.User.Started == startedSeen) return;
        startedSeen = combat.User.Started;
        if (current is null || look is null) return;
        if (moving is { } previous && previous.Look != look) engine.Tween.Control(new TweenControlRequest(previous.Tween, TweenControl.Cancel));
        // From the pose shown, so an action straight after another (or after a settle) carries on without a jump.
        // As far along as the action already is: one update can admit several steps after it began.
        TweenHandle tween = engine.Tween.Start(new TweenStartRequest(held[look].Entity, motion.Timeline(heldLooks.Model(look), current, combat.User.Elapsed))
            with { Start = TweenStart.FromPresented }).Tween;
        moving = (look, combat.User.Started, tween);
        Motion = tween;
    }

    // The developer motion viewer: the look it shows, and the pose it holds (null while playing or at rest).
    private (string Look, HeldPose? Pose)? viewing;

    /// <summary>Developer motion viewer: shows a look in place of what the hands hold, at rest; null returns to play.</summary>
    internal void View(string? look)
    {
        if (moving is { } playing) engine.Tween.Control(new TweenControlRequest(playing.Tween, TweenControl.Cancel));
        if (Motion.Value != 0) engine.Tween.Control(new TweenControlRequest(Motion, TweenControl.Cancel));
        moving = null;
        startedSeen = combat.User.Started;
        viewing = look is null ? null : (look, null);
        Publish();
    }

    /// <summary>Developer motion viewer: holds the shown look in a pose exactly (no tween), or at rest for null.</summary>
    internal void Hold(HeldPose? pose)
    {
        if (viewing is not { } view) return;
        if (Motion.Value != 0) engine.Tween.Control(new TweenControlRequest(Motion, TweenControl.Cancel));
        viewing = (view.Look, pose);
        Publish();
    }

    /// <summary>Developer motion viewer: plays an action's motion on the shown look from rest, at a speed.</summary>
    internal void Play(ActionDefinition action, float speed)
    {
        if (viewing is not { } view) return;
        viewing = (view.Look, null);
        Publish();
        Motion = engine.Tween.Start(new TweenStartRequest(held[view.Look].Entity,
            heldLooks.Motion.Timeline(heldLooks.Model(view.Look), action, 0, speed))).Tween;
    }

    /// <summary>The held model's latest motion tween: an action's timeline, or the settle after one was cut short.</summary>
    internal TweenHandle Motion { get; private set; }

    private bool Running(TweenHandle tween) => engine.Tween.Read(tween).State != TweenState.Ended;

    private static Quaternion Facing(Vector3 forward)
    {
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        Vector3 up = Vector3.Cross(right, forward);
        return Quaternion.CreateFromRotationMatrix(new(right.X, right.Y, right.Z, 0, up.X, up.Y, up.Z, 0,
            -forward.X, -forward.Y, -forward.Z, 0, 0, 0, 0, 1));
    }
    public void Dispose()
    {
        scene.PublishCombat([]);
        residents?.Dispose();
        foreach (Appearance appearance in appearances) appearance.Dispose();
        foreach (MeshResource mesh in meshes) mesh.Dispose();
        foreach (RenderResource model in models) model.Dispose();
    }
    private sealed record Part(ulong Entity, Appearance Appearance, Vector3 Offset, Vector3 Size);
}
