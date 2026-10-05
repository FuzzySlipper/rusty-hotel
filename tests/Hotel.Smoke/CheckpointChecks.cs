using System.Numerics;
using System.Text;
using Hotel.Game.Combat;
using Hotel.Game.Expedition;
using Hotel.Game.Player;
using Hotel.Game.Route;
using Hotel.Game.Scene;
using Hotel.Game.Spirits;
using Hotel.Game.Supplies;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Persistence;
using Rusty.Engine.Testing;

internal static class CheckpointChecks
{
    internal static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "hotel-checkpoint-" + Guid.NewGuid());
        var content = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "content"), "*", SearchOption.AllDirectories)
            .ToDictionary(p => Path.GetRelativePath(Path.Combine(AppContext.BaseDirectory, "content"), p).Replace('\\', '/'), p => (ReadOnlyMemory<byte>)File.ReadAllBytes(p));
        var options = new EngineTestHostOptions { PersistenceRoot = root, Content = content };
        CheckpointState saved = null!;
        try
        {
            using (EngineTestHost host = EngineTestHost.Create(options)) host.Call(engine =>
            {
                using Fixture f = new(engine);
                f.Expedition.Start();
                Check(f.Expedition.Returns == 0 && f.Supplies.Health == 70, "missing save establishes valid initial checkpoint");
                f.Supplies.Pickup("refuge-rounds"); f.Spirit.Acquire(); f.Supplies.Damage(1000);
                f.Route.Restore(["survey"]);
                f.Expedition.Recover();
                Check(f.Supplies.Health == 70 && f.Supplies.Occupied == 0 && f.Supplies.Ammo == 0 && f.Supplies.Summon == 0 &&
                    !f.Supplies.Collected("refuge-rounds") && !f.Spirit.Acquired && f.Route.OpenDoors.Length == 0, "pre-return defeat restores every initial domain");
                f.Supplies.Pickup("refuge-rounds"); f.Supplies.Use(0, f.Supplies.Revision);
                f.Supplies.Pickup("refuge-dressing"); f.Supplies.Move(0, 7, f.Supplies.Revision);
                f.Supplies.Pickup("survey-reel"); f.Spirit.Acquire(); f.Spirit.Equip(true, f.Spirit.Revision);
                f.Supplies.SpendAmmo(2); f.Supplies.SpendSummon(1); f.Supplies.Damage(13);
                f.Combat.SelectWeapon(1);
                f.Combat.Enemies[0].Health.SetCurrent(0);
                var lamp = f.Combat.Enemies[1]; lamp.Health.SetCurrent(36); lamp.Yaw = .4f;
                f.Route.Restore(["survey", "return"]);
                f.At(0, -8, new(-2.33f, .93f, 2.85f));
                f.Route.Use();
                Check(f.Expedition.Returns == 0, "checkpoint cannot be recorded remotely through world use");
                f.At(-1.1f, 2.85f, new(-2.33f, .93f, 2.85f));
                Check(f.Route.Prompt.Contains("Record refuge"), "notebook uses existing focus flow");
                f.Route.Use();
                Check(f.Expedition.Returns == 1 && f.Expedition.SecuredFinds.SequenceEqual(["survey-reel"]) &&
                    !Enumerable.Range(0, f.Supplies.Capacity).Any(i => f.Supplies.Slot(i)?.Item == "reel"), "ordinary notebook deposits reel and records checkpoint");
                using var store = new ProductStateStore<CheckpointState>(engine, HotelExpedition.Scope,
                    new JsonProductStateCodec<CheckpointState>(CheckpointJson.Default.CheckpointState));
                saved = store.Load(HotelExpedition.Key).State!;
                Check(saved.Supplies.Health == 57 && saved.Supplies.Ammo == 4 && saved.Supplies.Summon == 1 && saved.Supplies.Pockets[7]?.Item == "bandage", "durable checkpoint keeps resource and pocket values");
                string Stored() => System.Text.Json.JsonSerializer.Serialize(store.Load(HotelExpedition.Key).State, CheckpointJson.Default.CheckpointState);
                string good = Stored();
                HotelEnemy displaced = f.Combat.Enemies[1];
                Vector3 post = displaced.Position;
                f.Scene.Entities.Set(displaced.Entity, EngineComponentTypes.Transform, new(post + new Vector3(5, 0, 0), Quaternion.Identity, Vector3.One));
                f.At(-1.1f, 2.85f, new(-2.33f, .93f, 2.85f));
                f.Route.Use();
                Check(f.Expedition.Returns == 1 && f.Expedition.ReceiptTitle == "Checkpoint not saved" && Stored() == good,
                    "live state that fails validation is refused with a receipt and leaves the stored checkpoint intact");
                f.Scene.Entities.Set(displaced.Entity, EngineComponentTypes.Transform, new(post, Quaternion.Identity, Vector3.One));
                f.Supplies.Use(7, f.Supplies.Revision); f.Supplies.SpendAmmo(4); f.Supplies.SpendSummon(1);
                f.Supplies.Pickup("portrait-dressing"); f.Spirit.Equip(false, f.Spirit.Revision);
                f.Combat.Enemies[1].Health.SetCurrent(0); f.Supplies.Damage(1000); f.Route.Reset();
                f.Expedition.Recover(); Verify(f);
                Check(!f.Supplies.Pickup("survey-reel") && f.Supplies.Pickup("portrait-dressing"), "saved loot cannot duplicate; unsaved loot is restored");
                f.Expedition.Recover(); Verify(f);
                f.Expedition.Recover(); Verify(f);
            });
            // Dispose the first Engine entirely; reopen its persisted scope in a new Engine host.
            using (EngineTestHost host = EngineTestHost.Create(options)) host.Call(engine =>
            {
                using Fixture f = new(engine); f.Expedition.Start(); Verify(f);
                using var store = new ProductStateStore<CheckpointState>(engine, HotelExpedition.Scope,
                    new JsonProductStateCodec<CheckpointState>(CheckpointJson.Default.CheckpointState));
                string before = System.Text.Json.JsonSerializer.Serialize(saved, CheckpointJson.Default.CheckpointState);
                string after = System.Text.Json.JsonSerializer.Serialize(store.Load(HotelExpedition.Key).State, CheckpointJson.Default.CheckpointState);
                Check(before == after, "whole checkpoint round trips across Engine host shutdown/relaunch");
                foreach (CheckpointState invalid in new[] {
                    saved with { Version = 99 }, saved with { OpenDoors = ["unknown"] },
                    saved with { Supplies = saved.Supplies with { Health = 0 } },
                    saved with { Spirit = saved.Spirit with { Acquired = false, Equipped = true } },
                    saved with { Residents = [] }, saved with { SecuredFinds = [] } })
                {
                    store.Save(HotelExpedition.Key, invalid);
                    bool rejected = false;
                    try { f.Expedition.Start(); } catch (InvalidOperationException e) { rejected = e.Message.Contains("Cannot load"); }
                    Check(rejected && f.Supplies.Health == 57 && f.Expedition.Returns == 1, "invalid present values fail without partial restore or fallback");
                    Check(System.Text.Json.JsonSerializer.Serialize(store.Load(HotelExpedition.Key).State, CheckpointJson.Default.CheckpointState) ==
                        System.Text.Json.JsonSerializer.Serialize(invalid, CheckpointJson.Default.CheckpointState), "invalid save is not overwritten");
                }
                using PersistenceStore raw = engine.Persistence.OpenStore(new(HotelExpedition.Scope));
                foreach (string json in new[] { "{", "null", "{}" })
                {
                    engine.Persistence.Save(new(raw, HotelExpedition.Key, PersistenceRevisionGuard.Any, 0, Encoding.UTF8.GetBytes(json)));
                    bool rejected = false;
                    try { f.Expedition.Start(); } catch (InvalidOperationException e) { rejected = e.Message.Contains("Cannot load"); }
                    Check(rejected && f.Supplies.Health == 57, "malformed/null/incomplete present checkpoint reports an error");
                    using var blob = engine.Persistence.Load(new(raw, HotelExpedition.Key));
                    Check(engine.Persistence.DescribeBlob(blob).PayloadLen == (ulong)json.Length, "load error preserves invalid payload");
                }
            });
        }
        finally { Directory.Delete(root, true); }
        Console.WriteLine("Checkpoint checks passed: initial/repeated recovery, notebook deposit, resources/pockets/pact/world restoration, real host relaunch, invalid state and malformed save refusal.");
    }
    private static void Verify(Fixture f)
    {
        Check(f.Expedition.Returns == 1 && f.Expedition.SecuredFinds.SequenceEqual(["survey-reel"]) &&
            f.Supplies.Health == 57 && f.Supplies.Ammo == 4 && f.Supplies.Summon == 1 && f.Supplies.Slot(7)?.Count == 1 && f.Supplies.Occupied == 1 &&
            f.Supplies.Collected("survey-reel") && !f.Supplies.Collected("portrait-dressing") && f.Spirit.Acquired && f.Spirit.Equipped && !f.Spirit.Active &&
            f.Route.OpenDoors.Order().SequenceEqual(new[] { "return", "survey" }) && f.Combat.Weapon.Id == "pistol" &&
            !f.Combat.Enemies[0].Alive && f.Combat.Enemies[1].Health.ValueInt == 36 && f.Combat.Enemies[1].Phase == AttackPhase.Ready &&
            f.Combat.Phase == AttackPhase.Ready && Vector3.Distance(f.Player.Position, new(0, .875f, 3.5f)) < .01f,
            "checkpoint restores coherent inventory, resources, pact, refuge, weapon, loot, doors and residents");
    }
    private static void Check(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
    private sealed class Fixture : IDisposable
    {
        internal readonly HotelScene Scene;
        internal readonly HotelPlayer Player;
        internal readonly HotelSupplies Supplies;
        internal readonly HotelCombat Combat;
        internal readonly HotelSpirit Spirit;
        internal readonly HotelRoute Route;
        internal readonly HotelExpedition Expedition;
        internal Fixture(IEngineContext engine)
        {
            var content = Owners.Content(engine);
            Scene = Owners.Scene(engine, content); Player = Owners.Player(engine, Scene, content);
            Supplies = Owners.Supplies(content, Scene.PlayerEntity);
            Combat = Owners.Combat(engine, Scene, Player, Supplies, content); Spirit = Owners.Spirit(content, Supplies, Combat, Player);
            Route = Owners.Route(engine, Scene, Player, Supplies, Spirit, content, ReturnToRefuge);
            Expedition = new(engine, content.Excursion.Placements.Refuge, Player, Supplies, Combat, Spirit, Route);
        }
        private bool ReturnToRefuge() => Expedition.Return();
        internal void At(float x, float z, Vector3 target)
        {
            Player.Reset(); Scene.Entities.Set(Scene.PlayerEntity, EngineComponentTypes.Transform, new(new(x, .875f, z), Quaternion.Identity, Vector3.One));
            Vector3 delta = target - Player.Eye;
            Player.LookBy(Math.Atan2(delta.X, -delta.Z) * 180 / Math.PI, Math.Atan2(delta.Y, Math.Sqrt(delta.X * delta.X + delta.Z * delta.Z)) * 180 / Math.PI);
            Route.Update();
        }
        public void Dispose() { Expedition.Dispose(); Player.Dispose(); Scene.Dispose(); }
    }
}
