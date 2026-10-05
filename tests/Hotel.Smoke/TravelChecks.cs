using System.Numerics;
using System.Text.Json;
using Hotel.Game;
using Hotel.Game.Floors;
using Hotel.Game.Route;
using Rusty.Engine;
using Rusty.Engine.Testing;

// Taking the stairs rebuilds the world for the next floor: what the player carries comes along, the floor below is
// remembered, the same floor is found again on the way back up, and defeat anywhere returns to the refuge's floor.
internal static class TravelChecks
{
    internal static void Run()
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "content");
        using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions
        {
            PersistenceRoot = Path.Combine(Path.GetTempPath(), "hotel-travel-" + Guid.NewGuid()),
            Content = Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                .ToDictionary(p => Path.GetRelativePath(folder, p).Replace('\\', '/'), p => (ReadOnlyMemory<byte>)File.ReadAllBytes(p))
        });
        host.Call(engine =>
        {
            void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
            using HotelProduct product = new(new ProductCreateContext(engine, new ProductContent(default),
                new ProductInputConfiguration(default, default, default, default, InputCursorMode.PointerLock), default!));
            CaptureCommands commands = new();
            product.RegisterDebugCommands(commands);
            product.Start();
            ulong step = 0;
            void Advance(uint count, params ProductInputEvent[] input)
            {
                product.Update(new ProductUpdate(new ProductUpdateFacts(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 1, 1, 0, step, 60, count, 0, 1d / 60), input));
                step += count;
            }
            ProductInputEvent use = default(ProductInputEvent) with { Kind = InputEventKind.Key, Edge = InputEdge.Pressed, Keyboard = KeyboardControl.KeyE, X = 1 };
            JsonElement Observe() => JsonDocument.Parse(commands.Module!.Observe().Message).RootElement;
            // Stand a pace in front of a flight, facing it, and use it with the ordinary use key.
            void Climb(StairDirection way)
            {
                StairDefinition stair = product.World.Excursion.Route.Stairs.First(s => s.Direction == way);
                Vector3 point = new(stair.Point[0], 0, stair.Point[2]);
                float[] landing = product.World.Excursion.Placements.FromAbove.Position;
                Vector3 back = new Vector3(landing[0], 0, landing[2]) - point;
                // From the landing side of the flight, a little over a metre out.
                Vector3 stand = point + Vector3.Normalize(back.LengthSquared() > 0 ? back : Vector3.UnitZ) * 1.2f;
                Vector3 toward = point - stand;
                product.World.Player.Place(stand with { Y = product.World.Player.Tuning.Height / 2 }, MathF.Atan2(toward.X, -toward.Z) * 180 / MathF.PI);
                product.World.Player.LookBy(0, -20);
                Advance(2);
                Advance(1, use);
                Advance(1);
            }

            Check(commands.Supplies!.Give("bandage", 2).Status == Rusty.Engine.Debugging.DebugCommandStatus.Success, "carry two dressings up");
            HotelWorld refuge = product.World;
            Climb(StairDirection.Up);
            Check(Observe().GetProperty("depth").GetInt32() == 1 && product.World != refuge && product.World.Excursion.Id == FloorExcursion.Id(1),
                "the service stairs lead up to a generated floor: " + Observe().GetProperty("prompt"));
            Check(Observe().GetProperty("occupiedPockets").GetInt32() == 1 && product.World.Supplies.Slot(0) is { Item: "bandage", Count: 2 }, "supplies come along");
            Check(product.World.Excursion.Route.Stairs.Length == 2 && product.World.Route.Location.Length > 0, "the floor has its stairs and named rooms");
            string identity = product.Floors.Current!.Identity.PlanHash;
            Vector3 arrived = product.World.Player.Position;
            Check(Vector3.Distance(arrived, new(product.World.Excursion.Placements.Arrival.Position[0], arrived.Y, product.World.Excursion.Placements.Arrival.Position[2])) < .2f,
                "the player arrives on the stair landing");

            Climb(StairDirection.Down);
            Check(Observe().GetProperty("depth").GetInt32() == 0 && product.World.HasRefuge, "the stairs lead back down to the refuge's floor");
            Vector3 below = product.World.Player.Position;
            Check(Vector3.Distance(below with { Y = 0 }, new(8, 0, 7.7f)) < .2f, "the player comes down at the foot of the service stairs");
            Climb(StairDirection.Up);
            Check(product.Floors.Current!.Identity.PlanHash == identity, "going up again finds the same floor");

            Check(commands.Hotel!.Floor(77, 2).Status == Rusty.Engine.Debugging.DebugCommandStatus.Success && Observe().GetProperty("depth").GetInt32() == 2,
                "the developer floor command stands the player on a generated floor");
            Check(JsonDocument.Parse(commands.Hotel!.InspectFloor().Message).RootElement.GetProperty("runSeed").GetUInt64() == 77, "floor inspection reports the run");
            product.Restart();
            Check(Observe().GetProperty("depth").GetInt32() == 0 && product.World.HasRefuge && Observe().GetProperty("occupiedPockets").GetInt32() == 0,
                "defeat or restart on a generated floor returns to the refuge's checkpoint");
            Console.WriteLine("Travel checks passed: stairs up and down with the use key, carried supplies, the landing and the foot of the stairs, the same floor again, developer floor entry and inspection, and restart to the refuge.");
        });
    }
}
