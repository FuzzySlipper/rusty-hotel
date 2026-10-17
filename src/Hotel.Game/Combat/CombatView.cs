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
    private ArmsView arms = null!;
    // The action whose motion is playing on a held look (no start for the viewer's), and the last action start seen.
    private (string Look, ulong Started, TweenHandle Tween)? moving;
    // The tween last started on a held look and the timeline it plays, so where it shows the model can be told.
    // With it, what closes the hands: the action it plays (at a speed), or for a settle, the closure it eases back from.
    private (string Look, TweenHandle Tween, TweenStartRequest Request, ActionDefinition? Action, float Speed, (float Main, float Off) From)? shown;
    private ulong startedSeen;
    private readonly Part muzzle;
    // Enough flare boxes for the shots one hand can have in flight at once.
    private const int ShownProjectiles = 6;
    private readonly Part[] flares;
    // Developer inspection of the held assembly: the stance guide (while guiding), contact probes and grip targets.
    private readonly HeldInspection inspection;
    private bool guiding;

    internal CombatView(IEngineContext engine, HotelScene scene, HotelPlayer player, HotelCombat combat, CombatDefinition definition)
    {
        this.engine = engine; this.scene = scene; this.player = player; this.combat = combat;
        try
        {
            residents = new ResidentView(engine, scene, combat, definition);
            heldLooks = definition.Held;
            arms = new ArmsView(engine, heldLooks.Arms, scene.Entities.Create().Value);
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
            inspection = new HeldInspection(engine, heldLooks);
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
            HeldPose pose = viewing is { } view && view.Look == kind && Held().Pose is { } shownPose ? shownPose : motion.Motions[model.Motion].Poses[HeldMotionCatalog.Rest];
            (Vector3 at, Quaternion turn) = pose.Place(model);
            facts.Add(new(entity, false, 0, new(at, turn, new(model.Scale)), appearance, !combat.Defeated && look == kind && ItemVisible(kind), RenderLayer.Viewmodel));
        }
        // A firearm's flash shows while it commits, where its muzzle is in the pose the action holds then.
        ActionDefinition? current = combat.User.Current;
        bool firing = combat.Phase == AttackPhase.Commit && current?.Delivery.Kind is DeliveryKind.Hitscan or DeliveryKind.Projectile;
        Vector3 flash = Vector3.Zero;
        if (firing && look is { } gun && heldLooks.Model(gun) is { Muzzle: { } muzzlePoint } firearm)
        {
            // The muzzle is a point on the model, where the motion shows the model now.
            (Vector3 at, Quaternion turn) = Shown(gun);
            flash = at + Vector3.Transform(Authored.Vector(muzzlePoint) * firearm.Scale, turn);
        }
        else firing = false;
        Place(muzzle, player.Eye, camera, !combat.Defeated && firing, flash);
        for (int i = 0; i < flares.Length; i++)
            Place(flares[i], i < combat.Projectiles.Count ? combat.Projectiles[i].Position : Vector3.Zero, Quaternion.Identity, i < combat.Projectiles.Count);
        facts.Add(arms.Fact(!combat.Defeated && look is not null));
        inspection.Guide(guiding && !combat.Defeated ? look : null, guiding && look is not null ? Shown(look) : default);
        inspection.Landmarks(combat.Defeated || look is null ? [] : Landmarks(look));
        scene.PublishCombat(facts.ToArray());
        if (viewing is null) Move(look);
        if (!combat.Defeated && look is not null) Reach(look);
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
            Begin(playing.Look, motion.Settle(Offset(playing.Look)), from: Closure(playing.Look));
            moving = null;
        }
        if (combat.User.Started == startedSeen) return;
        startedSeen = combat.User.Started;
        if (current is null || look is null) return;
        if (moving is { } previous && previous.Look != look) engine.Tween.Control(new TweenControlRequest(previous.Tween, TweenControl.Cancel));
        // From the pose shown, so an action straight after another (or after a settle) carries on without a jump.
        // As far along as the action already is: one update can admit several steps after it began.
        TweenHandle tween = Begin(look, heldLooks.Motion.Timeline(heldLooks.Model(look), current, from: Offset(look)), combat.User.Elapsed, current);
        moving = (look, combat.User.Started, tween);
    }

    // Starts a held look's motion tween from the offset it states (so what it shows can be sampled from it), replacing
    // the look's tweens.
    private TweenHandle Begin(string look, TweenSegment[] segments, float elapsed = 0, ActionDefinition? action = null, float speed = 1,
        (float, float) from = default)
    {
        TweenStartRequest request = new(held[look].Entity, segments);
        Motion = engine.Tween.Start(request with { ElapsedSeconds = elapsed }).Tween;
        shown = (look, Motion, request, action, speed, from);
        return Motion;
    }

    // How closed the hands are on a look now: the viewer's held key, the playing action's keys where the Engine reports
    // its tween has got to, or a settle easing back to the grip.
    private (float Main, float Off) Closure(string look)
    {
        if (viewing is { } view && view.Look == look && Held() is { Pose: not null } held) return held.Hands;
        if (shown is not { } playing || playing.Look != look) return (0, 0);
        TweenReadout readout = engine.Tween.Read(playing.Tween);
        if (readout.State == TweenState.Ended) return (0, 0);
        float elapsed = (float)readout.ElapsedSeconds * playing.Speed;
        if (playing.Action is { } action)
            return heldLooks.Motion.Hands(heldLooks.Model(look), action, elapsed, (curve, t) => engine.Tween.Evaluate(curve, t));
        float left = 1 - engine.Tween.Evaluate(heldLooks.Motion.SettleEase, Math.Clamp(elapsed / heldLooks.Motion.SettleSeconds, 0, 1));
        return (playing.From.Main * left, playing.From.Off * left);
    }

    private (float Main, float Off) viewingHands;

    // The same visibility keys apply to ordinary attacks, a held viewer key and seeking/playback. Arms stay drawn.
    private bool ItemVisible(string look)
    {
        HeldMotion motion = heldLooks.Motion.Motions[heldLooks.Model(look).Motion];
        if (viewing is { } view && view.Look == look && viewingKey is { } key)
            return motion.Track(key.Action.Id) is not { } track || key.Key >= track.Length || track[key.Key].Visible;
        if (shown is not { Action: { } action } playing || playing.Look != look) return true;
        TweenReadout readout = engine.Tween.Read(playing.Tween);
        return readout.State == TweenState.Ended || motion.Visible(action, (float)readout.ElapsedSeconds * playing.Speed);
    }

    // Ends the held models' motion: the viewer shows a pose or a look exactly.
    private void Stop()
    {
        if (moving is { } playing) engine.Tween.Control(new TweenControlRequest(playing.Tween, TweenControl.Cancel));
        if (Motion.Value != 0) engine.Tween.Control(new TweenControlRequest(Motion, TweenControl.Cancel));
        moving = null;
        shown = null;
    }

    // The offset a look's motion shows now (from its rest placement): the playing timeline where the Engine reports it
    // has got to, or none.
    private (Vector3 Move, Quaternion Turn) Offset(string look)
    {
        if (shown is not { } playing || playing.Look != look) return (Vector3.Zero, Quaternion.Identity);
        TweenReadout readout = engine.Tween.Read(playing.Tween);
        if (readout.State == TweenState.Ended) return (Vector3.Zero, Quaternion.Identity);
        TweenSample sample = engine.Tween.Sample(playing.Request, readout.ElapsedSeconds);
        return (sample.Translation, sample.Rotation);
    }

    // The developer motion viewer: the look it shows, and the pose it holds (null while playing or at rest).
    private (string Look, HeldPose? Pose)? viewing;
    // The key the viewer holds (an action and its key index), read from its track as it is now, so an edit to the
    // key (its pose, its hands, from the workbench or a reloaded file) shows at once; null when it holds a pose or none.
    private (ActionDefinition Action, int Key)? viewingKey;

    // The pose the viewer holds and how closed the hands are: the held key's, as its track says now (rest once the key
    // is gone), or the pose it was given.
    private (HeldPose? Pose, (float Main, float Off) Hands) Held()
    {
        if (viewing is not { } view) return (null, (0, 0));
        if (viewingKey is not { } key) return (view.Pose, viewingHands);
        HeldMotion motion = heldLooks.Motion.Motions[heldLooks.Model(view.Look).Motion];
        return motion.Track(key.Action.Id) is { } track && key.Key < track.Length && motion.Poses.TryGetValue(track[key.Key].Pose, out HeldPose? pose)
            ? (pose, (track[key.Key].Hands[0], track[key.Key].Hands[1]))
            : (null, (0, 0));
    }

    /// <summary>Developer motion viewer: holds the shown look exactly at one of an action's keys, as the key is now; -1 holds rest.</summary>
    internal void HoldKey(ActionDefinition action, int key)
    {
        if (viewing is not { } view) return;
        Stop();
        viewingKey = key < 0 ? null : (action, key);
        viewing = (view.Look, null);
        viewingHands = (0, 0);
        Publish();
    }

    /// <summary>Developer motion viewer: shows a look in place of what the hands hold, at rest; null returns to play.</summary>
    internal void View(string? look)
    {
        Stop();
        startedSeen = combat.User.Started;
        if (viewing?.Look != look) inspection.ClearContact();
        viewing = look is null ? null : (look, null);
        viewingKey = null;
        Publish();
    }

    /// <summary>
    /// Developer motion viewer: holds the shown look in a pose exactly (no tween), or at rest for null, with the hands
    /// closed as <paramref name="hands"/> says (a key's own closure; unchanged when null).
    /// </summary>
    internal void Hold(HeldPose? pose, (float Main, float Off)? hands = null)
    {
        if (viewing is not { } view) return;
        Stop();
        viewing = (view.Look, pose);
        viewingKey = null;
        viewingHands = pose is null ? (0, 0) : hands ?? viewingHands;
        Publish();
    }

    /// <summary>Developer motion viewer: plays an action's motion on the shown look from rest, at a speed.</summary>
    internal void Play(ActionDefinition action, float speed)
    {
        if (viewing is not { } view) return;
        viewing = (view.Look, null);
        viewingKey = null;
        Publish();
        Begin(view.Look, heldLooks.Motion.Timeline(heldLooks.Model(view.Look), action, speed), action: action, speed: speed);
        Publish();
    }

    /// <summary>
    /// Developer arms comparison: shows another arms model (a content path, rigged as the authored one is) in place of
    /// the authored one, with the same hold; the authored model returns when the floor is rebuilt.
    /// </summary>
    internal void ShowArms(string model)
    {
        // On an object of its own (an object keeps one animation instance), so the old arms leave the published scene
        // before they are released.
        ArmsView next = new(engine, heldLooks.Arms with { Model = model }, scene.Entities.Create().Value);
        ArmsView previous = arms;
        arms = next;
        Publish();
        previous.Dispose();
    }

    /// <summary>Developer stance guide: draws where the shown look's stance puts it beside where it is.</summary>
    internal bool Guiding => guiding;

    internal void ShowGuide(bool on)
    {
        guiding = on;
        Publish();
    }

    /// <summary>How far the drawn arms' wrists fell short of their grips (see <see cref="ArmsView.Shortfall"/>).</summary>
    internal (float Main, float Off) ArmsShortfall => arms.Shortfall;

    /// <summary>The look the hands show now: the motion viewer's, or what they hold.</summary>
    internal string? ShownLook => viewing?.Look ?? combat.Holding?.Item.Wear!.Look;

    /// <summary>The shown look's grips as Engine inspection targets (see <see cref="HeldInspection.Targets"/>); none while nothing is shown.</summary>
    internal IReadOnlyList<Rusty.Engine.Debugging.InspectionTarget> InspectionTargets() =>
        ShownLook is { } look && !combat.Defeated ? inspection.Targets(look, held[look].Entity, Shown(look)) : [];

    // The grip check's landmark pairs on a look the hands show (GripLandmarks): the off hand's only while it holds it.
    private LandmarkPair[] Landmarks(string look)
    {
        HeldModel model = heldLooks.Model(look);
        return GripLandmarks.Pairs(model, held[look].Entity, arms.Rig(false), model.Grips.Off is null ? null : arms.Rig(true), heldLooks.Arms.Hands);
    }

    /// <summary>The grip landmark pairs on the shown look (<see cref="GripLandmarks"/>) as the Engine last measured them on the drawn hands.</summary>
    internal LandmarkReadResult GripMeasures() => engine.Graphics.ReadLandmarks(HeldInspection.LandmarkSet);

    /// <summary>Publishes the hand fit's last report as contact markers (see <see cref="HeldInspection.Contact"/>).</summary>
    internal void ShowContact(List<OverlayItem> marks) => inspection.Contact(marks);

    /// <summary>Developer arms tuning: the main hand's turn from the held item and its palm offset, in the loaded content.</summary>
    internal ArmsDefinition Arms => heldLooks.Arms;

    /// <summary>Developer motion viewer: shows the shown look's action motion paused at a moment (seconds from its start).</summary>
    internal void At(ActionDefinition action, float seconds)
    {
        if (viewing is not { } view) return;
        viewing = (view.Look, null);
        viewingKey = null;
        Publish();
        Begin(view.Look, heldLooks.Motion.Timeline(heldLooks.Model(view.Look), action), action: action);
        engine.Tween.Control(new TweenControlRequest(Motion, TweenControl.Pause));
        engine.Tween.Control(TweenControlRequest.Seek(Motion, seconds));
        Publish();
    }

    /// <summary>The held model's latest motion tween: an action's timeline, or the settle after one was cut short.</summary>
    internal TweenHandle Motion { get; private set; }

    /// <summary>Steps what falls (the residents' ragdolls) by one admitted step.</summary>
    internal void Step(float seconds) => residents.Step(seconds);

    // The hands reach the shown look's grips, where its motion shows the model now.
    private void Reach(string look)
    {
        HeldModel model = heldLooks.Model(look);
        (Vector3 main, Vector3? off, Quaternion item, float[]? hand) = HeldGrip.At(heldLooks, model, Shown(look));
        arms.Reach(main, off, item, hand, model.Grips.OffHand, model.Shape, Closure(look));
    }

    /// <summary>
    /// Fits a hand's shape on a look to its model (<see cref="HandFitting"/>), in the loaded content only, and shows it;
    /// with <paramref name="apply"/> false, only reports how the hand meets the model now. Each digit's probes join
    /// <paramref name="marks"/>. Null when it cannot (see <see cref="HandHold"/>).
    /// </summary>
    internal List<string>? FitHand(string look, bool offHand, bool apply, List<OverlayItem> marks)
    {
        HeldModel model = heldLooks.Model(look);
        if (HandHold(look, offHand) is not { } hold || (offHand ? model.Shape?.Off : model.Shape?.Main) is not { } shape) return null;
        string side = offHand ? "off" : "main";
        // Drawn reaching for this hold (the look shown at rest), the drawn digits check the fit's posing.
        if (!apply) return HandFitting.Contact(engine, hold, shape, heldLooks.Arms.Hands, marks, side, ShownLook == look ? arms.Drawn(offHand, hold.Wrist) : null);
        List<string> lines = HandFitting.Fit(engine, hold, shape, heldLooks.Arms.Hands, marks, side);
        Publish();
        return lines;
    }

    /// <summary>
    /// How a hand holds a look at rest, for the developer hand fit: the look's model where it stands, the hand's wrist and
    /// turn on its grip, and its digits as the arms draw them. Null when the look has no grip for that hand or the arms
    /// have not yet been drawn holding still.
    /// </summary>
    private HandFitting.Hold? HandHold(string look, bool offHand)
    {
        HeldModel model = heldLooks.Model(look);
        (Vector3 At, Quaternion Turn) placed = heldLooks.Motion.Motions[model.Motion].Poses[HeldMotionCatalog.Rest].Place(model);
        (Vector3 main, Vector3? off, Quaternion item, float[]? hand) = HeldGrip.At(heldLooks, model, placed);
        if ((offHand ? off : main) is not { } grip || arms.Chains(offHand) is not { } chains) return null;
        (Vector3 wrist, Quaternion turn) = ArmsView.Wrist(grip, item, offHand ? heldLooks.Arms.OffHand : heldLooks.Arms.MainHand,
            offHand ? model.Grips.OffHand : hand);
        RenderResource resource = models[Array.IndexOf(heldLooks.Looks, model)];
        return new(resource, new(placed.At, placed.Turn, new(model.Scale)), wrist, turn, chains, offHand ? heldLooks.Arms.Off.Mirrored : heldLooks.Arms.Main.Mirrored);
    }

    /// <summary>
    /// Where a look's model is shown now (camera space): the viewer's held pose, or its rest placement under the offset
    /// its playing motion (an action, a settle, the viewer's playback or seek) shows.
    /// </summary>
    internal (Vector3 At, Quaternion Turn) Shown(string look)
    {
        HeldModel model = heldLooks.Model(look);
        if (viewing is { } view && view.Look == look && Held().Pose is { } pose) return pose.Place(model);
        (Vector3 at, Quaternion turn) = heldLooks.Motion.Motions[model.Motion].Poses[HeldMotionCatalog.Rest].Place(model);
        (Vector3 move, Quaternion offset) = Offset(look);
        return (at + move, Quaternion.Normalize(turn * offset));
    }

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
        arms?.Dispose();
        residents?.Dispose();
        foreach (Appearance appearance in appearances) appearance.Dispose();
        foreach (MeshResource mesh in meshes) mesh.Dispose();
        foreach (RenderResource model in models) model.Dispose();
    }
    private sealed record Part(ulong Entity, Appearance Appearance, Vector3 Offset, Vector3 Size);
}
