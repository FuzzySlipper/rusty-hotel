using System.Text.Json;
using System.Text.Json.Serialization;
using Hotel.Game.Player;
using Hotel.Game.Expedition;
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
    private bool disposed;
    private ulong step;
    private double sampleTime;

    public HotelProduct(ProductCreateContext context)
    {
        scene = new HotelScene(context.Engine, HotelDefinition.Load(context.Engine));
        try
        {
            player = new HotelPlayer(context.Engine, scene);
            try
            {
                supplies = new HotelSupplies(scene.Definition.Supplies, scene.Definition.Interface.SupplyPockets, scene.PlayerEntity);
                combat = new HotelCombat(context.Engine, scene, player, supplies);
                spirit = new HotelSpirit(scene.Definition.Spirit, supplies, combat, player);
                route = new HotelRoute(context.Engine, scene, player, supplies, spirit);
                hud = new HotelHud(context.Engine, scene.Definition.Interface);
                try
                {
                    ambience = new HotelAmbience(context.Engine, scene.Definition.Ambience);
                    try
                    {
                        combatView = new CombatView(context.Engine, scene, player, combat);
                        try
                        {
                            spiritView = new SpiritView(context.Engine, scene, spirit);
                            try { expedition = new HotelExpedition(context.Engine, scene.Definition, player, supplies, combat, spirit, route); }
                            catch { spiritView.Dispose(); throw; }
                        }
                        catch { combatView.Dispose(); throw; }
                    }
                    catch { ambience.Dispose(); throw; }
                }
                catch { hud.Dispose(); throw; }
            }
            catch { player.Dispose(); throw; }
        }
        catch { scene.Dispose(); throw; }
        route.RecordCheckpoint = expedition.Return;
        route.Changed = () => hud.Publish(route, supplies, combat, spirit, expedition);
    }

    public void Start() { expedition.Start(); spiritView.Publish(); combatView.Publish(); player.Publish(0); hud.Publish(route, supplies, combat, spirit, expedition); ambience.Start(); }

    public ProductUpdateResult Update(ProductUpdate update)
    {
        supplies.HandleIntents(update.Input);
        spirit.HandleIntents(update.Input);
        foreach (ProductInputEvent item in update.Input)
            if (item.Kind == InputEventKind.Clear) ClearActions();
        FpsInputFrame input = player.ReadInput(update.Input,
            (float)(update.Facts.FixedDeltaSeconds * update.Facts.AdmittedStepCount));
        pendingUse |= input.UsePressed;
        pendingSummon |= player.Input.Physical.Pressed(KeyboardControl.KeyQ);
        pendingAttack |= player.Input.Physical.Pressed(PointerButton.Primary) || player.Input.Physical.Pressed(KeyboardControl.ControlLeft);
        pendingReload |= player.Input.Physical.Pressed(KeyboardControl.KeyR);
        if (player.Input.Physical.Pressed(KeyboardControl.Digit1)) pendingWeapon = 0;
        if (player.Input.Physical.Pressed(KeyboardControl.Digit2)) pendingWeapon = 1;
        if (player.Input.Physical.Pressed(KeyboardControl.Digit3)) pendingQuick = 0;
        if (player.Input.Physical.Pressed(KeyboardControl.Digit4)) pendingQuick = 1;
        if (player.Input.Physical.Pressed(KeyboardControl.Digit5)) pendingQuick = 2;
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
            route.Update();
            if (pendingUse) { pendingUse = false; if (!combat.Defeated) route.Use(); }
        }
        spiritView.Publish();
        combatView.Publish();
        hud.Publish(route, supplies, combat, spirit, expedition);
        step = checked(update.Facts.SimulationStep + update.Facts.AdmittedStepCount);
        sampleTime = step * update.Facts.FixedDeltaSeconds;
        player.Publish(sampleTime);
        return ProductUpdateResult.None;
    }

    private void ClearActions() { pendingUse = pendingAttack = pendingReload = pendingSummon = false; pendingWeapon = pendingQuick = -1; }

    public void HandlePausedIntents(ReadOnlySpan<ProductInputEvent> intents)
    {
        supplies.HandleIntents(intents);
        spirit.HandleIntents(intents);
        hud.Publish(route, supplies, combat, spirit, expedition);
    }

    public void Pause() { ClearActions(); player.ClearInput(); }
    public void Resume() { ClearActions(); player.ClearInput(); }
    public void Restart() { ClearActions(); expedition.Recover(); spiritView.Publish(); combatView.Publish(); player.Publish(sampleTime); hud.Publish(route, supplies, combat, spirit, expedition); }
    public void Shutdown() => Dispose();
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        expedition.Dispose();
        spiritView.Dispose();
        combatView.Dispose();
        ambience.Dispose();
        hud.Dispose();
        player.Dispose();
        scene.Dispose();
    }

    public void RegisterDebugCommands(IDebugCommandModuleRegistrar registrar)
    {
        registrar.Register(new PlaytestDebugModule(Observe, Action, ["forward", "back", "left", "right", "use", "attack", "melee", "pistol", "reload", "summon"], LookBy));
        registrar.Register(new InteractionDebugModule(route.Interaction));
        registrar.Register(new HotelDebugCommands(Observe, ResetExcursion));
        registrar.Register(new SuppliesDebugCommands(supplies, () => hud.Publish(route, supplies, combat, spirit, expedition)));
    }

    private void ResetExcursion()
    {
        ClearActions(); player.Reset(); supplies.Reset(); combat.Reset(); spirit.Reset(); route.Reset();
        spiritView.Publish(); combatView.Publish(); player.Publish(sampleTime); hud.Publish(route, supplies, combat, spirit, expedition);
    }

    private DebugCommandResult LookBy(double yaw, double pitch)
    {
        if (!double.IsFinite(yaw) || !double.IsFinite(pitch) || Math.Abs(yaw) > 360 || Math.Abs(pitch) > 180)
            return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, "Look degrees exceed bounds.");
        player.LookBy(yaw, pitch);
        spiritView.Publish();
        combatView.Publish();
        player.Publish(sampleTime);
        return Observe();
    }

    private static PlaytestAction Action(string id) => id switch
    {
        "summon" => new(id, "KeyQ", 60, true),
        "attack" => new(id, "ControlLeft", 60, true),
        "melee" => new(id, "Digit1", 60, true),
        "pistol" => new(id, "Digit2", 60, true),
        "reload" => new(id, "KeyR", 60, true),
        "use" => new(id, "KeyE", 60, true),
        "forward" => new(id, "KeyW", 200, true),
        "back" => new(id, "KeyS", 200, true),
        "left" => new(id, "KeyA", 200, true),
        "right" => new(id, "KeyD", 200, true),
        _ => new(id, "", 0, false, false, "Unknown walking action")
    };

    private DebugCommandResult Observe() => DebugCommandResult.Success(JsonSerializer.Serialize(new HotelObservation(
        step, [player.Position.X, player.Position.Y, player.Position.Z],
        [player.Eye.X, player.Eye.Y, player.Eye.Z], player.LookState.YawRadians,
        player.LookState.PitchRadians, player.Motion.Grounded, route.Location, route.Prompt, route.OpenDoors, route.ReadingSequence, supplies.Health, supplies.Ammo, supplies.Summon, supplies.Occupied, supplies.Revision, supplies.Message, combat.Weapon.Id, combat.Phase.ToString(), combat.AcceptedAttacks, combat.LandedHits, spirit.Acquired, spirit.Equipped, spirit.Revision, spirit.Phase.ToString(), spirit.Elapsed, spirit.Calls, spirit.Message, expedition.Returns, expedition.SecuredFinds, expedition.Status, combat.Enemies.Select(e => new EnemyObservation(e.Definition.Id, [e.Position.X, e.Position.Y, e.Position.Z], e.Health.ValueInt, e.Phase.ToString())).ToArray()), ObservationJson.Default.HotelObservation));
}

internal sealed record HotelObservation(ulong Step, float[] Position, float[] Eye, float Yaw, float Pitch, bool Grounded, string Location, string Prompt, string[] OpenDoors, ulong ReadingSequence, int Health, int Ammo, int Summon, int OccupiedPockets, ulong InventoryRevision, string SupplyMessage, string Weapon, string AttackPhase, int AcceptedAttacks, int LandedHits, bool SpiritAcquired, bool SpiritEquipped, ulong SpiritRevision, string SpiritPhase, float SpiritElapsed, int SpiritCalls, string SpiritMessage, int CheckpointReturns, string[] SecuredFinds, string CheckpointStatus, EnemyObservation[] Enemies);
internal sealed record EnemyObservation(string Id, float[] Position, int Health, string Phase);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(HotelObservation))]
internal sealed partial class ObservationJson : JsonSerializerContext;
