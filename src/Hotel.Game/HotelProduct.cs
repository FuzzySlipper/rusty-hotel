using Hotel.Game.Content;
using Hotel.Game.Floors;
using Hotel.Game.Input;
using Hotel.Game.Interface;
using Hotel.Game.Route;
using Hotel.Game.Combat;
using Hotel.Game.Expedition;
using Rusty.Engine.Interaction;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using Rusty.Engine.Input;

namespace Hotel.Game;

public sealed class HotelProduct : IEngineProduct, IDebugCommandModuleSource
{
    /// <summary>The authored hotel section a new game opens in: <c>content/excursions/west-wing/</c>.</summary>
    internal const string StartingExcursion = "west-wing";

    private readonly IEngineContext engine;
    private readonly HotelContent content;
    private readonly HotelHud hud;
    private readonly HotelControls controls;
    private readonly HotelFloors floors;
    private readonly CurrentRoute current = new();
    private readonly WorldInteraction interaction;
    private readonly HotelDeveloper developer;
    // Session memory of floors the player has left, and finds collected on floors other than the current one.
    private readonly Dictionary<string, WorldMemory> memory = new(StringComparer.Ordinal);
    private readonly HashSet<string> collected = new(StringComparer.Ordinal);
    private HotelWorld world;
    private (ExcursionDefinition Excursion, int Depth, StairDirection Way)? pendingTravel;
    private bool pendingUse, pendingAttack, pendingReload, pendingSummon;
    private int pendingWeapon = -1, pendingQuick = -1;
    private bool disposed;
    private ulong step;
    private double sampleTime;

    public HotelProduct(ProductCreateContext context)
    {
        engine = context.Engine;
        try
        {
            content = HotelContent.Load(engine, StartingExcursion);
            controls = new HotelControls(content.Controls, content.Combat.Weapons);
            hud = new HotelHud(engine, content.Interface);
            floors = new HotelFloors(engine, content);
            interaction = new WorldInteraction(current);
            world = Build(content.Excursion);
            developer = new HotelDeveloper(this);
        }
        catch { Dispose(); throw; }
    }

    internal HotelWorld World => world;
    internal HotelContent Content => content;
    internal HotelFloors Floors => floors;
    internal ulong Step => step;

    public void Start() { floors.Start(); world.Expedition.Start(); Publish(); world.Ambience.Start(); }

    public ProductUpdateResult Update(ProductUpdate update)
    {
        world.Supplies.HandleIntents(update.Input);
        world.Spirit.HandleIntents(update.Input);
        foreach (ProductInputEvent item in update.Input)
            if (item.Kind == InputEventKind.Clear) ClearActions();
        FpsInputFrame input = world.Player.ReadInput(update.Input,
            (float)(update.Facts.FixedDeltaSeconds * update.Facts.AdmittedStepCount));
        PhysicalInputState physical = world.Player.Input.Physical;
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
            HotelWorld w = world;
            if (w.Combat.Defeated && pendingReload) { Restart(); break; }
            if (pendingQuick >= 0) { w.Supplies.Use(pendingQuick, w.Supplies.Revision); pendingQuick = -1; }
            if (pendingWeapon >= 0) { w.Combat.SelectWeapon(pendingWeapon); pendingWeapon = -1; }
            if (pendingAttack) { pendingAttack = false; w.Combat.Attack(); }
            if (pendingReload) { pendingReload = false; w.Combat.Reload(); }
            if (pendingSummon) { pendingSummon = false; w.Spirit.Call(); }
            w.Player.Step(w.Combat.Defeated ? input with { Movement = System.Numerics.Vector2.Zero } : input,
                (float)update.Facts.FixedDeltaSeconds, w.Combat.Obstacles);
            w.Combat.Step((float)update.Facts.FixedDeltaSeconds);
            w.Spirit.Step((float)update.Facts.FixedDeltaSeconds);
            w.Supplies.Step((float)update.Facts.FixedDeltaSeconds);
            controls.Step((float)update.Facts.FixedDeltaSeconds);
            w.Route.Update();
            if (pendingUse) { pendingUse = false; if (!w.Combat.Defeated) w.Route.Use(); }
            // The stairs were taken during this step's use: change floor once the route has finished with its world.
            if (pendingTravel is { } travel) { pendingTravel = null; Enter(travel.Excursion, travel.Depth, travel.Way); break; }
        }
        step = checked(update.Facts.SimulationStep + update.Facts.AdmittedStepCount);
        sampleTime = step * update.Facts.FixedDeltaSeconds;
        Publish();
        return ProductUpdateResult.None;
    }

    internal void ClearActions() { pendingUse = pendingAttack = pendingReload = pendingSummon = false; pendingWeapon = pendingQuick = -1; }

    public void HandlePausedIntents(ReadOnlySpan<ProductInputEvent> intents)
    {
        world.Supplies.HandleIntents(intents);
        world.Spirit.HandleIntents(intents);
        PublishInterface();
    }

    public void Pause() { ClearActions(); world.Player.ClearInput(); }
    public void Resume() { ClearActions(); world.Player.ClearInput(); }

    /// <summary>Defeat or a restart returns to the refuge's floor and its checkpoint, whichever floor the player was on.</summary>
    public void Restart()
    {
        ClearActions();
        ReturnToRefugeFloor();
        world.Expedition.Recover();
        Publish();
    }

    public void Shutdown() => Dispose();
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        world?.Dispose();
        floors?.Dispose();
        hud?.Dispose();
    }

    private HotelWorld Build(ExcursionDefinition excursion)
    {
        HotelWorld built = new(engine, content, excursion, interaction, ReturnToRefuge, PublishInterface, Travel);
        current.Route = built.Route;
        interaction.Focus.Clear();
        return built;
    }

    // The notebook's use handler; Expedition is created after Route because it captures route state.
    private bool ReturnToRefuge() => world.Expedition.Return();

    /// <summary>
    /// The stairs' use handler. The floor is generated now, so a floor that cannot be made leaves the player where they
    /// are; the world itself changes after the route's use returns.
    /// </summary>
    private bool Travel(StairDirection way)
    {
        int depth = floors.Depth + (way == StairDirection.Up ? 1 : -1);
        if (depth < 0) return false;
        ExcursionDefinition? next = depth == 0 ? content.Excursion : floors.Floor(depth);
        if (next is null) return false;
        pendingTravel = (next, depth, way);
        return true;
    }

    /// <summary>
    /// Leaves this floor for another: what the player carries comes along, this floor is remembered for the session,
    /// and the new world is built and entered at its stairs.
    /// </summary>
    internal void Enter(ExcursionDefinition excursion, int depth, StairDirection way)
    {
        WorldCarry carry = world.Carry();
        foreach (string id in carry.Supplies.Collected) collected.Add(id);
        memory[world.Excursion.Id] = world.Remember();
        // The old world releases its scene, lights and collision before the next claims theirs.
        HotelExpedition previous = world.Expedition;
        world.Dispose();
        world = Build(excursion);
        world.Expedition.Adopt(previous);
        floors.Arrive(depth);
        world.Enter(carry, collected, memory.GetValueOrDefault(excursion.Id));
        var arrival = way == StairDirection.Up ? excursion.Placements.Arrival : excursion.Placements.FromAbove;
        world.Player.Place(Authored.Vector(arrival.Position), arrival.YawDegrees);
        ClearActions();
        world.Ambience.Start();
        Publish();
    }

    /// <summary>Rebuilds the refuge's floor when the player is elsewhere; the session's floor memory is forgotten.</summary>
    internal void ReturnToRefugeFloor()
    {
        memory.Clear();
        collected.Clear();
        if (world.HasRefuge) return;
        HotelExpedition previous = world.Expedition;
        world.Dispose();
        world = Build(content.Excursion);
        world.Expedition.Adopt(previous);
        floors.Arrive(0);
        world.Ambience.Start();
    }

    /// <summary>Publishes every presentation of the current domain state: scene views, camera and HUD.</summary>
    internal void Publish()
    {
        world.SpiritView.Publish();
        world.CombatView.Publish();
        world.Player.Publish(sampleTime);
        PublishInterface();
    }

    // Paused claims, route results and developer fixtures change only UI facts; the camera
    // sample and scene snapshot stay at the last admitted simulation step.
    internal void PublishInterface() => hud.Publish(world.Route, world.Supplies, world.Combat, world.Spirit, world.Expedition, controls);

    public void RegisterDebugCommands(IDebugCommandModuleRegistrar registrar)
    {
        registrar.Register(new PlaytestDebugModule(developer.Observe, controls.Action, controls.ActionIds, developer.LookBy));
        registrar.Register(new InteractionDebugModule(interaction));
        registrar.Register(new HotelDebugCommands(developer));
        registrar.Register(new SuppliesDebugCommands(() => world.Supplies, PublishInterface));
    }

    /// <summary>The world interaction reads whichever floor's route is current.</summary>
    private sealed class CurrentRoute : IWorldInteractionScene
    {
        internal HotelRoute? Route { get; set; }
        public InteractionSceneSnapshot ReadInteraction() => Route!.ReadInteraction();
        public InteractionActionResult UseInteraction(InteractionTarget target) => Route!.UseInteraction(target);
    }
}
