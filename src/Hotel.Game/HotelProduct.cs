using System.Text.Json;
using System.Text.Json.Serialization;
using Hotel.Game.Player;
using Hotel.Game.Expedition;
using Hotel.Game.Input;
using Hotel.Game.Content;
using Hotel.Game.Scene;
using Hotel.Game.Interface;
using Hotel.Game.Audio;
using Hotel.Game.Route;
using Hotel.Game.Supplies;
using Hotel.Game.Combat;
using Hotel.Game.Spirits;
using Rusty.Engine.Interaction;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using Rusty.Engine.Input;

namespace Hotel.Game;

public sealed class HotelProduct : IEngineProduct, IDebugCommandModuleSource
{
    /// <summary>The authored hotel section a new game opens in: <c>content/excursions/west-wing/</c>.</summary>
    internal const string StartingExcursion = "west-wing";

    private readonly HotelExpedition expedition;
    private readonly HotelScene scene;
    private readonly HotelPlayer player;
    private readonly HotelHud hud;
    private readonly HotelRoute route;
    private readonly HotelSupplies supplies;
    private bool pendingUse, pendingAttack, pendingReload, pendingSummon;
    private readonly HotelSpirit spirit;
    private readonly SpiritView spiritView;
    private int pendingWeapon = -1, pendingQuick = -1;
    private readonly HotelCombat combat;
    private readonly CombatView combatView;
    private readonly HotelAmbience ambience;
    private readonly HotelControls controls;
    private readonly List<IDisposable> owned = [];
    private bool disposed;
    private ulong step;
    private double sampleTime;

    public HotelProduct(ProductCreateContext context)
    {
        IEngineContext engine = context.Engine;
        try
        {
            HotelContent content = HotelContent.Load(engine, StartingExcursion);
            ExcursionDefinition excursion = content.Excursion;
            scene = Own(new HotelScene(engine, content.Surfaces, excursion.Geometry, excursion.Route.Doors));
            controls = new HotelControls(content.Controls, content.Combat.Weapons);
            player = Own(new HotelPlayer(engine, scene, content.Player, excursion.Placements.Arrival, content.Controls));
            supplies = new HotelSupplies(content.Supplies, excursion.Placements.Finds, content.Interface.SupplyPockets, scene.PlayerEntity);
            combat = new HotelCombat(engine, scene, player, supplies, content.Combat, excursion.Placements.Residents,
                content.Controls.Weapons.Select(w => w.Label).ToArray());
            spirit = new HotelSpirit(content.Spirit, content.SpiritText, content.SpiritBell, supplies, combat, player);
            route = new HotelRoute(engine, scene, player, supplies, spirit, content.Route, excursion.Route,
                excursion.Placements.Refuge, ReturnToRefuge, PublishInterface);
            hud = Own(new HotelHud(engine, content.Interface));
            ambience = Own(new HotelAmbience(engine, excursion.Ambience));
            combatView = Own(new CombatView(engine, scene, player, combat));
            spiritView = Own(new SpiritView(engine, scene, spirit));
            expedition = Own(new HotelExpedition(engine, excursion.Placements.Refuge, content.ExpeditionText, player, supplies, combat, spirit, route));
        }
        catch { Dispose(); throw; }
    }

    public void Start() { expedition.Start(); Publish(); ambience.Start(); }

    public ProductUpdateResult Update(ProductUpdate update)
    {
        supplies.HandleIntents(update.Input);
        spirit.HandleIntents(update.Input);
        foreach (ProductInputEvent item in update.Input)
            if (item.Kind == InputEventKind.Clear) ClearActions();
        FpsInputFrame input = player.ReadInput(update.Input,
            (float)(update.Facts.FixedDeltaSeconds * update.Facts.AdmittedStepCount));
        PhysicalInputState physical = player.Input.Physical;
        ControlBindings bound = controls.Bindings;
        pendingUse |= input.UsePressed;
        pendingSummon |= HotelControls.Pressed(physical, bound.Summon);
        pendingAttack |= HotelControls.Pressed(physical, bound.Attack);
        pendingReload |= HotelControls.Pressed(physical, bound.Reload);
        for (int i = 0; i < bound.Weapons.Length; i++)
            if (HotelControls.Pressed(physical, bound.Weapons[i])) pendingWeapon = i;
        for (int i = 0; i < bound.QuickPockets.Length; i++)
            if (HotelControls.Pressed(physical, bound.QuickPockets[i])) pendingQuick = i;
        for (uint i = 0; i < update.Facts.AdmittedStepCount; i++)
        {
            if (combat.Defeated && pendingReload) { Restart(); break; }
            if (pendingQuick >= 0) { supplies.Use(pendingQuick, supplies.Revision); pendingQuick = -1; }
            if (pendingWeapon >= 0) { combat.SelectWeapon(pendingWeapon); pendingWeapon = -1; }
            if (pendingAttack) { pendingAttack = false; combat.Attack(); }
            if (pendingReload) { pendingReload = false; combat.Reload(); }
            if (pendingSummon) { pendingSummon = false; spirit.Call(); }
            player.Step(combat.Defeated ? input with { Movement = System.Numerics.Vector2.Zero } : input,
                (float)update.Facts.FixedDeltaSeconds, combat.Obstacles);
            combat.Step((float)update.Facts.FixedDeltaSeconds);
            spirit.Step((float)update.Facts.FixedDeltaSeconds);
            supplies.Step((float)update.Facts.FixedDeltaSeconds);
            controls.Step((float)update.Facts.FixedDeltaSeconds);
            route.Update();
            if (pendingUse) { pendingUse = false; if (!combat.Defeated) route.Use(); }
        }
        step = checked(update.Facts.SimulationStep + update.Facts.AdmittedStepCount);
        sampleTime = step * update.Facts.FixedDeltaSeconds;
        Publish();
        return ProductUpdateResult.None;
    }

    private void ClearActions() { pendingUse = pendingAttack = pendingReload = pendingSummon = false; pendingWeapon = pendingQuick = -1; }

    public void HandlePausedIntents(ReadOnlySpan<ProductInputEvent> intents)
    {
        supplies.HandleIntents(intents);
        spirit.HandleIntents(intents);
        PublishInterface();
    }

    public void Pause() { ClearActions(); player.ClearInput(); }
    public void Resume() { ClearActions(); player.ClearInput(); }
    public void Restart() { ClearActions(); expedition.Recover(); Publish(); }
    public void Shutdown() => Dispose();
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        // Reverse construction order: dependents release before the scene they draw into.
        for (int i = owned.Count - 1; i >= 0; i--) owned[i].Dispose();
        owned.Clear();
    }

    // The notebook's use handler; Expedition is created after Route because it captures route state.
    private bool ReturnToRefuge() => expedition.Return();

    private T Own<T>(T resource) where T : IDisposable
    {
        owned.Add(resource);
        return resource;
    }

    /// <summary>Publishes every presentation of the current domain state: scene views, camera and HUD.</summary>
    private void Publish()
    {
        spiritView.Publish();
        combatView.Publish();
        player.Publish(sampleTime);
        PublishInterface();
    }

    // Paused claims, route results and developer fixtures change only UI facts; the camera
    // sample and scene snapshot stay at the last admitted simulation step.
    private void PublishInterface() => hud.Publish(route, supplies, combat, spirit, expedition, controls);

    public void RegisterDebugCommands(IDebugCommandModuleRegistrar registrar)
    {
        registrar.Register(new PlaytestDebugModule(Observe, controls.Action, controls.ActionIds, LookBy));
        registrar.Register(new InteractionDebugModule(route.Interaction));
        registrar.Register(new HotelDebugCommands(Observe, ResetExcursion));
        registrar.Register(new SuppliesDebugCommands(supplies, PublishInterface));
    }

    private void ResetExcursion()
    {
        ClearActions();
        expedition.ApplyInitial();
        Publish();
    }

    private DebugCommandResult LookBy(double yaw, double pitch)
    {
        if (!double.IsFinite(yaw) || !double.IsFinite(pitch) || Math.Abs(yaw) > 360 || Math.Abs(pitch) > 180)
            return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, "Look degrees exceed bounds.");
        player.LookBy(yaw, pitch);
        Publish();
        return Observe();
    }

    private DebugCommandResult Observe() => DebugCommandResult.Success(JsonSerializer.Serialize(new HotelObservation(
        step, [player.Position.X, player.Position.Y, player.Position.Z],
        [player.Eye.X, player.Eye.Y, player.Eye.Z], player.LookState.YawRadians,
        player.LookState.PitchRadians, player.Motion.Grounded, route.Location, route.Prompt, route.OpenDoors, route.ReadingSequence, supplies.Health, supplies.Ammo, supplies.Summon, supplies.Occupied, supplies.Revision, supplies.Message, combat.Weapon.Id, combat.Phase.ToString(), combat.AcceptedAttacks, combat.LandedHits, spirit.Acquired, spirit.Equipped, spirit.Revision, spirit.Phase.ToString(), spirit.Elapsed, spirit.Calls, spirit.Message, expedition.Returns, expedition.SecuredFinds, expedition.Status, combat.Enemies.Select(e => new EnemyObservation(e.Id, [e.Position.X, e.Position.Y, e.Position.Z], e.Health.ValueInt, e.Phase.ToString())).ToArray()), ObservationJson.Default.HotelObservation));
}

internal sealed record HotelObservation(ulong Step, float[] Position, float[] Eye, float Yaw, float Pitch, bool Grounded, string Location, string Prompt, string[] OpenDoors, ulong ReadingSequence, int Health, int Ammo, int Summon, int OccupiedPockets, ulong InventoryRevision, string SupplyMessage, string Weapon, string AttackPhase, int AcceptedAttacks, int LandedHits, bool SpiritAcquired, bool SpiritEquipped, ulong SpiritRevision, string SpiritPhase, float SpiritElapsed, int SpiritCalls, string SpiritMessage, int CheckpointReturns, string[] SecuredFinds, string CheckpointStatus, EnemyObservation[] Enemies);
internal sealed record EnemyObservation(string Id, float[] Position, int Health, string Phase);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(HotelObservation))]
internal sealed partial class ObservationJson : JsonSerializerContext;
