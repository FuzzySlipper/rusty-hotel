using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hotel.Game.Floors;
using Hotel.Game.Floors.Modules;
using Hotel.Game.Route;
using Rusty.Engine.Debugging;

namespace Hotel.Game.Interface;

/// <summary>
/// The developer and agent access behind the generated command catalog: observation, look, explicit overrides that
/// move the player or change floor, and floor inspection. Every change goes through the product's existing owners.
/// </summary>
internal sealed class HotelDeveloper(HotelProduct product)
{
    // Far enough beside the hotel that a previewed module never touches it.
    private static readonly Vector2 PreviewCorner = new(100, 0);

    private HotelWorld World => product.World;

    internal DebugCommandResult GoTo(string space)
    {
        RoomDefinition[] rooms = World.Excursion.Route.Rooms;
        RoomDefinition? room = rooms.FirstOrDefault(r => r.Id == space);
        if (room is null)
            return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, $"Unknown space '{space}'. Spaces: {string.Join(", ", rooms.Select(r => r.Id))}.");
        product.ClearActions();
        World.Player.Place(new((room.Min[0] + room.Max[0]) / 2, World.Player.Tuning.Height / 2, (room.Min[2] + room.Max[2]) / 2),
            World.Player.LookState.YawRadians * 180 / MathF.PI);
        product.Publish();
        return Observe();
    }

    internal DebugCommandResult ShowModule(string id, int turn)
    {
        ModuleCatalog modules = product.Content.Modules;
        if (modules.Find(id) is not { } module)
            return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments,
                $"Unknown module '{id}'. Modules: {string.Join(", ", modules.Modules.Select(m => m.Id))}.");
        if (turn is < 0 or > 3) return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, "Turn is 0 to 3 quarter turns.");
        var (floor, porches) = ModuleCheck.Realize(module, ModuleCatalog.ModulePath(id), modules, product.Content.Kit, product.Content.Fixtures, turn, PreviewCorner);
        World.Scene.ShowPreview(floor.Boxes, floor.Lights);
        // Stand on the first doorway's porch, facing through it.
        var (doorway, stand) = porches[0];
        Vector2 toward = doorway.Point - stand;
        product.ClearActions();
        World.Player.Place(new(stand.X, World.Player.Tuning.Height / 2, stand.Y), MathF.Atan2(toward.X, -toward.Y) * 180 / MathF.PI);
        product.Publish();
        return Observe();
    }

    internal void ReturnToEntrance()
    {
        product.ClearActions();
        product.ReturnToRefugeFloor();
        World.Expedition.ApplyInitial();
        product.Publish();
    }

    /// <summary>Begins a run from a seed and stands the player on its floor at a depth, arriving up its stairs.</summary>
    internal DebugCommandResult Floor(ulong seed, int depth)
    {
        if (depth is < 1 or > 99) return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, "Depth is 1 to 99; depth 0 is the west wing.");
        product.ReturnToRefugeFloor();
        product.Floors.Begin(seed);
        if (product.Floors.Floor(depth) is not { } floor)
            return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, $"No floor at depth {depth}: {string.Join("; ", product.Floors.LastRefusals.Take(5))}");
        product.Enter(floor, depth, StairDirection.Up);
        return Inspect();
    }

    internal DebugCommandResult Inspect()
    {
        HotelFloors floors = product.Floors;
        GeneratedFloor? floor = floors.Current;
        return DebugCommandResult.Success(JsonSerializer.Serialize(new FloorInspection(floors.RunSeed, floors.Depth, World.Excursion.Id,
            floor?.Identity.PlanHash, floor?.Candidate, floor?.Attempt, floor?.Layout.Places.ToDictionary(p => p.Key, p => p.Value),
            floor?.Layout.Placements.Length, floor?.Content.Finds.Length, floor?.Content.Residents.Length,
            floor?.Confirmation.Routes.Length, floors.LastRefusals, floors.LastMilliseconds), DeveloperJson.Default.FloorInspection));
    }

    internal DebugCommandResult LookBy(double yaw, double pitch)
    {
        if (!double.IsFinite(yaw) || !double.IsFinite(pitch) || Math.Abs(yaw) > 360 || Math.Abs(pitch) > 180)
            return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, "Look degrees exceed bounds.");
        World.Player.LookBy(yaw, pitch);
        product.Publish();
        return Observe();
    }

    internal DebugCommandResult Observe()
    {
        HotelWorld w = World;
        var player = w.Player;
        return DebugCommandResult.Success(JsonSerializer.Serialize(new HotelObservation(
            product.Step, product.Floors.Depth, [player.Position.X, player.Position.Y, player.Position.Z],
            [player.Eye.X, player.Eye.Y, player.Eye.Z], player.LookState.YawRadians,
            player.LookState.PitchRadians, player.Motion.Grounded, w.Route.Location, w.Route.Prompt, w.Route.OpenDoors, w.Route.ReadingSequence,
            w.Supplies.Health, w.Supplies.Ammo, w.Supplies.Summon, w.Supplies.Occupied, w.Supplies.Revision, w.Supplies.Message, w.Combat.Weapon.Id,
            w.Combat.Phase.ToString(), w.Combat.AcceptedAttacks, w.Combat.LandedHits, w.Spirit.Acquired, w.Spirit.Equipped, w.Spirit.Revision,
            w.Spirit.Phase.ToString(), w.Spirit.Elapsed, w.Spirit.Calls, w.Spirit.Message, w.Expedition.Returns, w.Expedition.SecuredFinds,
            w.Expedition.Status, w.Combat.Enemies.Select(e => new EnemyObservation(e.Id, [e.Position.X, e.Position.Y, e.Position.Z],
                e.Health.ValueInt, e.Phase.ToString())).ToArray()), DeveloperJson.Default.HotelObservation));
    }
}

internal sealed record HotelObservation(ulong Step, int Depth, float[] Position, float[] Eye, float Yaw, float Pitch, bool Grounded, string Location, string Prompt,
    string[] OpenDoors, ulong ReadingSequence, int Health, int Ammo, int Summon, int OccupiedPockets, ulong InventoryRevision, string SupplyMessage,
    string Weapon, string AttackPhase, int AcceptedAttacks, int LandedHits, bool SpiritAcquired, bool SpiritEquipped, ulong SpiritRevision,
    string SpiritPhase, float SpiritElapsed, int SpiritCalls, string SpiritMessage, int CheckpointReturns, string[] SecuredFinds,
    string CheckpointStatus, EnemyObservation[] Enemies);
internal sealed record EnemyObservation(string Id, float[] Position, int Health, string Phase);
internal sealed record FloorInspection(ulong RunSeed, int Depth, string Excursion, string? PlanHash, int? Candidate, int? Attempt,
    Dictionary<string, string>? Places, int? Modules, int? Finds, int? Residents, int? PromisedRoutes, string[] LastRefusals, double LastMilliseconds);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(HotelObservation))]
[JsonSerializable(typeof(FloorInspection))]
internal sealed partial class DeveloperJson : JsonSerializerContext;
