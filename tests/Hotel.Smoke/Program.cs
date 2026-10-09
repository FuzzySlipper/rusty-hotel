using System.Numerics;
using System.Text.Json;
using Hotel.Game;
using Hotel.Game.Content;
using Hotel.Game.Interface;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using Rusty.Engine.Testing;

// Product callbacks and the real pinned Engine collision, without a browser.
// This checks callback policy, not host lifecycle admission or pointer lock.
using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions
{
    PersistenceRoot = Path.Combine(Path.GetTempPath(), "hotel-smoke-" + Guid.NewGuid()),
    Content = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "content"), "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(Path.Combine(AppContext.BaseDirectory, "content"), path).Replace('\\', '/'),
            path => (ReadOnlyMemory<byte>)File.ReadAllBytes(path))
});
host.Call(engine =>
{
    using HotelProduct product = new(new ProductCreateContext(engine, new ProductContent(default),
        new ProductInputConfiguration(default, default, default, default, InputCursorMode.PointerLock), default!));
    var content = Owners.Content(engine);
    CaptureCommands commands = new();
    product.RegisterDebugCommands(commands);
    TitleChecks.Begin(product);
    ulong step = 0;

    void Advance(uint count, params ProductInputEvent[] input)
    {
        product.Update(new ProductUpdate(new ProductUpdateFacts(
            ProductLifecycleState.Running, 1, 1, 0, step, 60, count, 0, 1d / 60), input));
        step += count;
    }
    Vector3 Position()
    {
        using JsonDocument json = JsonDocument.Parse(commands.Module!.Observe().Message);
        JsonElement p = json.RootElement.GetProperty("position");
        return new(p[0].GetSingle(), p[1].GetSingle(), p[2].GetSingle());
    }
    float Yaw()
    {
        using JsonDocument json = JsonDocument.Parse(commands.Module!.Observe().Message);
        return json.RootElement.GetProperty("yaw").GetSingle();
    }
    void Stationary(string reason)
    {
        Advance(30); // Allow ordinary ground braking after a clear.
        Vector3 atRest = Position();
        Advance(60);
        Check(Vector3.Distance(atRest, Position()) < .01f, reason);
    }

    Advance(60);
    Vector3 spawn = Position();
    Check(MathF.Abs(spawn.Y - .875f) < .03f, "player remains on the floor");
    Advance(60, Key(KeyboardControl.KeyW));
    Check(spawn.Z - Position().Z > 2 && spawn.Z - Position().Z < 2.4f, "authored walking pace");
    Advance(0, default(ProductInputEvent) with { Kind = InputEventKind.Clear });
    Stationary("input clear releases held walk");

    // Running: a faster stride that spends stamina; jumping lifts the feet off the floor and lands them again.
    var body = content.Player;
    int rested = product.World.Supplies.Stamina;
    Vector3 start = Position();
    Advance(60, Key(KeyboardControl.KeyW), Key(KeyboardControl.ShiftLeft));
    float ran = start.Z - Position().Z;
    Check(ran > body.Speed * 1.3f && ran < body.RunSpeed * 1.05f && product.World.Supplies.Stamina < rested,
        $"running covers more ground than walking and spends stamina: {ran:F2} m, stamina {rested} to {product.World.Supplies.Stamina}");
    Advance(0, default(ProductInputEvent) with { Kind = InputEventKind.Clear });
    Stationary("input clear releases a run");
    float floor = Position().Y, peak = floor;
    Advance(1, Key(KeyboardControl.Space));
    for (int i = 0; i < 90; i++) { Advance(1); peak = MathF.Max(peak, Position().Y); }
    Check(peak - floor > body.JumpHeight * .8f && peak - floor < body.JumpHeight * 1.2f && MathF.Abs(Position().Y - floor) < .02f,
        $"a jump rises about its authored height and lands: {peak - floor:F2} m");
    Advance(0, default(ProductInputEvent) with { Kind = InputEventKind.Clear });
    // A jump costs its whole stamina: short of it, the investigator stays on the floor and keeps what stamina they have.
    product.World.Supplies.Stats.Track("stamina").SetCurrent(body.JumpStamina - 1);
    Advance(1, Key(KeyboardControl.Space));
    peak = floor;
    for (int i = 0; i < 30; i++) { Advance(1); peak = MathF.Max(peak, Position().Y); }
    Check(peak - floor < .02f && product.World.Supplies.Stamina >= body.JumpStamina - 1, $"a jump short of its stamina is refused: rose {peak - floor:F2} m");
    Advance(0, default(ProductInputEvent) with { Kind = InputEventKind.Clear });
    var hint = new Hotel.Game.Input.HotelControls(content.Controls).Hint;
    Check(hint.Any(h => h.Contains(content.Controls.Run.Label)) && hint.Any(h => h.Contains(content.Controls.Jump.Label)), "the opening hint names run and jump");

    Advance(0, Key(KeyboardControl.KeyW));
    Vector3 beforePause = Position();
    product.Pause();
    product.Resume();
    Advance(60);
    Check(Vector3.Distance(beforePause, Position()) < .01f, "pause/resume clears zero-step pending walk");

    Advance(30, Key(KeyboardControl.KeyW));
    product.Pause();
    product.Resume();
    Stationary("pause/resume releases active walk");

    Advance(0, default(ProductInputEvent) with { Kind = InputEventKind.PointerDelta, X = 20, Y = 5 });
    float turned = Yaw();
    Check(MathF.Abs(turned) > .01f, "pointer delta changes look");
    Advance(60);
    Check(MathF.Abs(Yaw() - turned) < .00001f, "pointer delta is consumed once");

    product.Restart();
    Advance(600, Key(KeyboardControl.KeyW));
    Check(Position().Z > -13.8f && Position().Z < -13.6f, "closed survey door blocks walking");
    Check(MathF.Abs(Position().Y - spawn.Y) < .03f, "floor supports full corridor traversal");
    Advance(120, Key(KeyboardControl.KeyD));
    Check(Position().X < 1.6f, "corridor side wall blocks diagonal walking");

    product.Restart();
    Advance(405, Key(KeyboardControl.KeyW));
    Advance(30, default(ProductInputEvent) with { Kind = InputEventKind.Clear });
    bool SurveyOpen()
    {
        using JsonDocument json = JsonDocument.Parse(commands.Module!.Observe().Message);
        return json.RootElement.GetProperty("openDoors").EnumerateArray().Any(d => d.GetString() == "survey");
    }
    Advance(0, Key(KeyboardControl.KeyE));
    Advance(0, default(ProductInputEvent) with { Kind = InputEventKind.Clear });
    Advance(1);
    Check(!SurveyOpen(), "clear cancels pending zero-step use");
    Advance(0, Key(KeyboardControl.KeyE));
    product.Pause();
    product.Resume();
    Advance(1);
    Check(!SurveyOpen(), "pause/resume cancels pending zero-step use");
    Advance(0, Key(KeyboardControl.KeyE));
    Check(!SurveyOpen(), "zero-step use waits for admission");
    Advance(1);
    Check(SurveyOpen(), "pending use opens focused door at the next admitted step");

    product.Restart();
    int Attacks()
    {
        using JsonDocument json = JsonDocument.Parse(commands.Module!.Observe().Message);
        return json.RootElement.GetProperty("acceptedAttacks").GetInt32();
    }
    Advance(0, Key(KeyboardControl.ControlLeft));
    Advance(0, default(ProductInputEvent) with { Kind = InputEventKind.Clear });
    Advance(2);
    Check(Attacks() == 0, "clear cancels a zero-step pending attack");
    Advance(0, Key(KeyboardControl.ControlLeft));
    product.Pause(); product.Resume(); Advance(2);
    Check(Attacks() == 0, "pause clears a queued attack without replay");
    Advance(0, Key(KeyboardControl.ControlLeft)); Advance(1);
    Check(Attacks() == 1, "ordinary attack press waits for Engine admission");
    Advance(120);
    Check(Attacks() == 1, "held attack does not auto-repeat after recovery");
    product.Restart();
    string SpiritMessage()
    {
        using JsonDocument json = JsonDocument.Parse(commands.Module!.Observe().Message);
        return json.RootElement.GetProperty("spiritMessage").GetString()!;
    }
    Advance(0, Key(KeyboardControl.KeyQ));
    Advance(0, default(ProductInputEvent) with { Kind = InputEventKind.Clear });
    Advance(1);
    Check(SpiritMessage() == "", "clear discards pending summon before admission");
    Advance(0, Key(KeyboardControl.KeyQ)); product.Pause(); product.Resume(); Advance(1);
    Check(SpiritMessage() == "", "pause/resume discards pending summon");
    Advance(0, Key(KeyboardControl.KeyQ));
    Check(SpiritMessage() == "", "zero-step summon waits for admission");
    Advance(1);
    Check(SpiritMessage() == content.SpiritText.NoPactCall, "admitted ordinary Q reaches the spirit owner");
    product.Pause();
    product.HandlePausedIntents([SpiritChecks.Claim("{\"spirit\":\"hushwing\",\"revision\":0}")]);
    Check(SpiritMessage() == content.SpiritText.PactChanged, "paused semantic callback applies the same stale-choice rule and publishes");
    product.HandlePausedIntents([SuppliesChecks.Claim("{\"action\":\"use\",\"from\":0,\"revision\":0}")]);
    using (JsonDocument paused = JsonDocument.Parse(commands.Module!.Observe().Message))
        Check(paused.RootElement.GetProperty("supplyMessage").GetString() == content.Supplies.Text.CaseChanged,
            "paused supply callback routes the same revision rule");
    product.Resume();
    product.Restart();
    Advance(0, Key(KeyboardControl.Digit3)); product.Pause(); product.Resume(); Advance(1);
    using (JsonDocument cleared = JsonDocument.Parse(commands.Module!.Observe().Message))
        Check(cleared.RootElement.GetProperty("supplyMessage").GetString() == "", "pause cancels queued quick supply");
    Advance(0, Key(KeyboardControl.Digit3)); Advance(1);
    using (JsonDocument quick = JsonDocument.Parse(commands.Module!.Observe().Message))
        Check(quick.RootElement.GetProperty("supplyMessage").GetString() == content.Supplies.Text.EmptyPocket, "admitted quick key uses the supplies rule");
    product.Restart();
    Vector3 reset = Position();
    Advance(60);
    Check(MathF.Abs(reset.Z - 3.5f) < .01f && MathF.Abs(Position().Z - reset.Z) < .01f,
        "restart restores spawn and clears held input");
    Check(commands.Hotel!.ShowModule("guest-room-a", 1).Status == DebugCommandStatus.Success && Position().X > 90,
        "the developer module viewer stands the player at a module built beside the hotel");
    Check(commands.Hotel!.ShowModule("ballroom", 0).Status == DebugCommandStatus.InvalidArguments, "an unknown module is refused");
    // Developer placements land on clear floor: onto an occupied post, or into a wall, the player is moved aside and
    // the next steps run.
    product.Restart();
    Vector3 lamp = product.World.Combat.Enemies.Single(e => e.Kind.Id == "lamp").Position;
    Check(commands.Hotel!.GoTo("survey").Status == DebugCommandStatus.Success &&
        new Vector2(Position().X - lamp.X, Position().Z - lamp.Z).Length() > content.Player.Radius, "goto onto a resident's post stands the player beside it");
    Advance(30, Key(KeyboardControl.KeyW));
    Check(commands.Hotel!.View(-1.9f, -3, 90, 0).Status == DebugCommandStatus.Success && MathF.Abs(Position().X + 1.9f) > content.Player.Radius,
        "a view point inside a partition moves to clear floor");
    Advance(30, Key(KeyboardControl.KeyD));
    product.Restart();
    commands.Supplies!.Give("bandage", 1);
    Advance(60, Key(KeyboardControl.KeyW));
    commands.Hotel!.ReturnToEntrance();
    using (JsonDocument fresh = JsonDocument.Parse(commands.Module!.Observe().Message))
        Check(fresh.RootElement.GetProperty("occupiedPockets").GetInt32() == 0 && fresh.RootElement.GetProperty("health").GetInt32() == 70 &&
            MathF.Abs(Position().Z - 3.5f) < .01f, "developer reset applies the initial excursion through the recovery path");
});
host.Call(MechanicsChecks.Run);
host.Call(EffectChecks.Run);
host.Call(EquipmentChecks.Run);
host.Call(ActionChecks.Run);
host.Call(ResidentChecks.Run);
host.Call(KitChecks.Run);
host.Call(MissionChecks.Run);
host.Call(ModuleChecks.Run);
host.Call(LayoutChecks.Run);
host.Call(GenerationChecks.Run);
host.Call(ShiftChecks.Run);
host.Call(FingerprintChecks.Run);
host.Call(CensusChecks.Run);
host.Call(RouteChecks.Run);
host.Call(KeyChecks.Run);
host.Call(SuppliesChecks.Run);
host.Call(DroppingChecks.Run);
host.Call(ItemIconChecks.Run);
host.Call(CombatChecks.Run);
host.Call(SpiritChecks.Run);
host.Call(PactChecks.Run);
host.Call(LootChecks.Run);
host.Call(ProgressionChecks.Run);
TitleChecks.Run();
CheckpointChecks.Run();
ContentChecks.Run();
FloorChecks.Run();
TravelChecks.Run();
FloorSaveChecks.Run();
Console.WriteLine("Hotel smoke passed: floor/walls, walking pace, clear, pause/resume callbacks, pointer consumption and restart.");

static ProductInputEvent Key(KeyboardControl key) => default(ProductInputEvent) with
{ Kind = InputEventKind.Key, Edge = InputEdge.Pressed, Keyboard = key, X = 1 };
static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class CaptureCommands : IDebugCommandModuleRegistrar
{
    internal PlaytestDebugModule? Module { get; private set; }
    internal HotelDebugCommands? Hotel { get; private set; }
    internal SuppliesDebugCommands? Supplies { get; private set; }
    DebugCommandRegistrationResult IDebugCommandModuleRegistrar.Register<TModule>(TModule module)
    {
        if (module is PlaytestDebugModule playtest) Module = playtest;
        if (module is HotelDebugCommands hotel) Hotel = hotel;
        if (module is SuppliesDebugCommands supplies) Supplies = supplies;
        return new(DebugCommandRegistrationStatus.Registered, "Captured for product callback test");
    }
}
