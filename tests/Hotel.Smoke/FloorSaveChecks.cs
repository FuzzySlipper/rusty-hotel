using System.Text.Json;
using Hotel.Game;
using Hotel.Game.Content;
using Hotel.Game.Expedition;
using Hotel.Game.Floors;
using Hotel.Game.Route;
using Hotel.Game.Scene;
using Rusty.Engine;
using Rusty.Engine.Persistence;
using Rusty.Engine.Testing;

// The checkpoint keeps the run's generated floors as resolved plans: a relaunched host rebuilds them identically
// without drawing, with what the player left them as, and a tampered plan or another generator's floor is refused
// without replacing the save. Expedition finds from generated floors are secured and named at the refuge.
internal static class FloorSaveChecks
{
    internal static void Run()
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "content");
        string root = Path.Combine(Path.GetTempPath(), "hotel-floorsave-" + Guid.NewGuid());
        EngineTestHostOptions options = new()
        {
            PersistenceRoot = root,
            Content = Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                .ToDictionary(p => Path.GetRelativePath(folder, p).Replace('\\', '/'), p => (ReadOnlyMemory<byte>)File.ReadAllBytes(p))
        };
        void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
        HotelProduct Product(IEngineContext engine) => new(new ProductCreateContext(engine, new ProductContent(default),
            new ProductInputConfiguration(default, default, default, default, InputCursorMode.PointerLock), default!));
        static string Geometry(ExcursionDefinition floor)
        {
            CanonicalText text = new();
            foreach (RoomBox b in floor.Geometry.Boxes) text.Line("box", b.Name, b.Material, string.Join(",", b.Min), string.Join(",", b.Max));
            return text.Hash(FloorSeed.Current(0, 0, 0));
        }
        string identity = "", geometry = "", key = "", door = "", page = "";
        string savedJson = "";
        try
        {
            using (EngineTestHost host = EngineTestHost.Create(options)) host.Call(engine =>
            {
                using HotelProduct product = Product(engine);
                CaptureCommands commands = new();
                product.RegisterDebugCommands(commands);
                product.Start();
                // A floor with a lock: take its key, open its door, and pick up its ledger page.
                ulong seed = 0;
                for (ulong s = 1; s < 40 && seed == 0; s++)
                {
                    product.Floors.Begin(s);
                    if (product.Floors.Floor(1) is { } f && f.Route.Keys.Length > 0) seed = s;
                }
                Check(seed != 0, "some run's first floor has a lock");
                Check(commands.Hotel!.Floor(seed, 1).Status == Rusty.Engine.Debugging.DebugCommandStatus.Success, "enter the floor");
                ExcursionDefinition floor = product.World.Excursion;
                KeyDefinition held = floor.Route.Keys[0];
                key = held.Item;
                door = floor.Route.Doors.First(d => d.Key == key).Id;
                page = floor.Placements.Finds.First(f => f.Item == "ledger-page").Id;
                product.World.Route.Restore([door], [key]);
                Check(product.World.Supplies.Pickup(page), "pick up the ledger page");
                identity = product.Floors.Current!.Identity.PlanHash;
                geometry = Geometry(floor);
                product.Enter(product.Content.Excursion, 0, StairDirection.Down);
                Check(product.World.Expedition.Return(), "record the refuge checkpoint");
                Check(product.World.Expedition.SecuredFinds.Contains(page) && product.World.Expedition.ReceiptText.Contains("Ledger page"),
                    "the ledger page from the generated floor is secured and named: " + product.World.Expedition.ReceiptText);
                using var store = new ProductStateStore<CheckpointState>(engine, HotelExpedition.Scope,
                    new JsonProductStateCodec<CheckpointState>(CheckpointJson.Default.CheckpointState));
                savedJson = JsonSerializer.Serialize(store.Load(HotelExpedition.Key).State, CheckpointJson.Default.CheckpointState);
                Check(!savedJson.Contains("\"boxes\"", StringComparison.Ordinal), "the checkpoint keeps plans, never boxes");
            });
            // A new Engine host restores the run from the checkpoint alone.
            using (EngineTestHost host = EngineTestHost.Create(options)) host.Call(engine =>
            {
                using (HotelProduct product = Product(engine))
                {
                product.Start();
                Check(product.Floors.Capture().Floors.Length == 1, "the visited floor is restored before it is entered");
                var (stored, storedFloor) = product.Floors.Stored(1)!.Value;
                Check(product.Floors.LastMilliseconds == 0 && Geometry(storedFloor) == geometry && stored.Identity.PlanHash == identity,
                    "the floor is rebuilt from its plan, identical in identity and geometry, without generating");
                WorldMemory left = product.Floors.Memory(FloorExcursion.Id(1))!;
                Check(left.Keys.Contains(key) && left.OpenDoors.Contains(door) && product.Floors.Collected.Contains(page) && product.Floors.ShiftDue(1),
                    "the key is still held, the door still open, the secured page still gone, and the floor due to shift on the next visit");
                // Climbing to it after the refuge return, it has shifted around its kept stair core and landmark.
                ExcursionDefinition shifted = product.Floors.Floor(1)!;
                GeneratedFloor next = product.Floors.Stored(1)!.Value.Floor;
                Check(next.Identity.Seed.Shift == 1 && next.Identity.PlanHash != identity && Geometry(shifted) != geometry, "the next visit finds the floor shifted");
                string Core(GeneratedFloor g) => g.Layout.Placements.First(p => p.Id == g.Layout.Places["arrival"]) is var c ? $"{c.Id} {c.Module} {c.X} {c.Z} {c.Turn}" : "";
                Check(Core(next) == Core(stored), "the stair core stays where it was");
                Check(product.Floors.Memory(FloorExcursion.Id(1)) is null || product.Floors.Memory(FloorExcursion.Id(1))!.Keys.Length == 0,
                    "the shifted floor's keys start fresh");
                }

                using var store = new ProductStateStore<CheckpointState>(engine, HotelExpedition.Scope,
                    new JsonProductStateCodec<CheckpointState>(CheckpointJson.Default.CheckpointState));
                CheckpointState saved = store.Load(HotelExpedition.Key).State!;
                FloorRecord record = saved.Floors!.Floors[0];
                foreach (CheckpointState invalid in new[] {
                    saved with { Floors = saved.Floors with { Floors = [record with { PlanHash = new string('0', 64) }] } },
                    saved with { Floors = saved.Floors with { Floors = [record with { Version = FloorSeed.CurrentVersion + 1 }] } },
                    saved with { Floors = saved.Floors with { Floors = [record with { Layout = record.Layout with { Placements = record.Layout.Placements[1..] } }] } },
                    saved with { Floors = saved.Floors with { Collected = [.. saved.Floors.Collected, "floor-9/p1/table"] } },
                    saved with { Floors = saved.Floors with { Floors = [record with { Memory = record.Memory! with { Keys = ["no-such-key"] } }] } },
                    saved with { Floors = saved.Floors with { Floors = [record with { Memory = record.Memory! with { OpenDoors = ["floor-1/no-such-door"] } }] } },
                    saved with { Floors = saved.Floors with { Floors = [record with { Memory = record.Memory! with { Residents = [] } }] } } })
                {
                    store.Save(HotelExpedition.Key, invalid);
                    string stored = JsonSerializer.Serialize(store.Load(HotelExpedition.Key).State, CheckpointJson.Default.CheckpointState);
                    bool rejected = false;
                    try { using HotelProduct again = Product(engine); again.Start(); }
                    catch (InvalidOperationException e) { rejected = e.Message.Contains("Cannot load"); }
                    Check(rejected && JsonSerializer.Serialize(store.Load(HotelExpedition.Key).State, CheckpointJson.Default.CheckpointState) == stored,
                        "a tampered plan, another generator's floor, an unknown find, or memory that does not fit the floor is refused and the save is not replaced");
                }
            });
        }
        finally { Directory.Delete(root, true); }
        Console.WriteLine("Floor save checks passed: a checkpoint with a generated floor visited relaunches into the same run and an identical floor (identity and geometry) rebuilt without generating, with its key, open door and secured ledger page; tampered plans, another generator version and unknown finds are refused without replacing the save.");
    }
}
