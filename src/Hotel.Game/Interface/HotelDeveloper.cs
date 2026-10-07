using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hotel.Game.Floors;
using Hotel.Game.Floors.Modules;
using Hotel.Game.Route;
using Rusty.Engine;
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
        // A space's first post is clear floor by authoring; its middle may hold furniture or a flight of stairs.
        float[] at = World.Excursion.Plan.Spaces.FirstOrDefault(s => s.Id == space)?.Posts?.Values.FirstOrDefault()
            ?? [(room.Min[0] + room.Max[0]) / 2, (room.Min[2] + room.Max[2]) / 2];
        // A resident may stand on the post: the body goes to the nearest clear floor within the room.
        if (ClearNear(new(at[0], at[1]), p => p.X > room.Min[0] && p.X < room.Max[0] && p.Y > room.Min[2] && p.Y < room.Max[2]) is not { } clear)
            return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, $"No clear floor for the player in '{space}'.");
        World.Player.Place(clear, World.Player.LookState.YawRadians * 180 / MathF.PI);
        product.Publish();
        return Observe();
    }

    internal DebugCommandResult View(float x, float z, float yaw, float pitch)
    {
        if (!float.IsFinite(x) || !float.IsFinite(z) || !float.IsFinite(yaw) || !float.IsFinite(pitch))
            return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, "The point and angles must be finite numbers.");
        // A point inside a wall, furniture or a resident moves to the nearest clear floor; none nearby is refused.
        if (ClearNear(new(x, z), _ => true) is not { } clear)
            return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, $"No clear floor for the player near [{x}, {z}].");
        product.ClearActions();
        World.Player.Place(clear, yaw);
        World.Player.LookBy(0, pitch);
        product.Publish();
        return Observe();
    }

    // How far from the asked-for point a developer placement looks for clear floor, and how finely, in metres.
    private const float SearchRadius = 2, SearchStep = .25f;

    /// <summary>
    /// The nearest point to <paramref name="at"/>, within the search radius and accepted by <paramref name="within"/>,
    /// where the player's body overlaps no world collision and no living resident (an Engine box overlap query): the
    /// body's centre there, or null. Placing a body into collision would leave the character controller unable to
    /// resolve its next step.
    /// </summary>
    private Vector3? ClearNear(Vector2 at, Func<Vector2, bool> within)
    {
        HotelWorld w = World;
        float half = w.Player.Tuning.Height / 2, radius = w.Player.Tuning.Radius;
        SpatialEntityCollider[] residents = w.Combat.Enemies.Where(e => e.Alive).Select(e => e.Hitbox).ToArray();
        bool Clear(Vector2 p)
        {
            // The body's box, lifted clear of the floor it stands on.
            Vector3 centre = new(p.X, half, p.Y), extent = new(radius, half - .05f, radius);
            return !product.Engine.Spatial.OverlapAabb(new(w.Scene.Session, centre - extent + new Vector3(0, .05f, 0), centre + extent,
                Vector3.Zero, new(0, uint.MaxValue), residents, new[] { w.Scene.PlayerEntity.Value })).Present;
        }
        if (within(at) && Clear(at)) return new(at.X, half, at.Y);
        for (float r = SearchStep; r <= SearchRadius + 1e-3f; r += SearchStep)
        {
            int steps = Math.Max(8, (int)MathF.Ceiling(2 * MathF.PI * r / SearchStep));
            for (int i = 0; i < steps; i++)
            {
                float a = i * 2 * MathF.PI / steps;
                Vector2 p = at + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
                if (within(p) && Clear(p)) return new(p.X, half, p.Y);
            }
        }
        return null;
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
        // A new run's floors are new: the old run's are forgotten, and what the player carries is kept.
        product.Floors.Begin(seed);
        if (product.Floors.Floor(depth) is not { } floor)
            return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, $"No floor at depth {depth}: {string.Join("; ", product.Floors.LastRefusals.Take(5))}");
        product.Enter(floor, depth, StairDirection.Up, newRun: true);
        return Inspect();
    }

    internal DebugCommandResult Inspect()
    {
        HotelFloors floors = product.Floors;
        GeneratedFloor? floor = floors.Current;
        // Each mission place: its module, the middle of its first space, and the middle of the doorway it is entered by.
        PlaceInspection[]? places = floor?.Layout.Places.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p =>
        {
            var space = floor.Plan.Spaces.First(s => s.Id.StartsWith(p.Value + "/", StringComparison.Ordinal));
            var door = floor.Floor.Openings.Values.Where(o => o.Link.Between.Count(b => b.StartsWith(p.Value + "/", StringComparison.Ordinal)) == 1)
                .OrderBy(o => o.Link.Id, StringComparer.Ordinal).FirstOrDefault();
            Vector3? at = door is null ? null : (door.Start + door.End) / 2;
            return new PlaceInspection(p.Key, p.Value, floor.Layout.Placements.First(l => l.Id == p.Value).Module,
                [(space.Min[0] + space.Max[0]) / 2, (space.Min[1] + space.Max[1]) / 2], at is { } d ? [d.X, d.Z] : null);
        }).ToArray();
        // The floor's walkable map for agents: every space, and every link with the middle of its opening or shared wall.
        SpaceInspection[]? spaces = floor?.Plan.Spaces.Select(s => new SpaceInspection(s.Id, s.Label, s.Min, s.Max)).ToArray();
        LinkInspection[]? links = floor?.Plan.Links.Select(l =>
        {
            if (floor.Floor.Openings.TryGetValue(l.Id, out var o)) { Vector3 c = (o.Start + o.End) / 2; return new LinkInspection(l.Id, l.Between, [c.X, c.Z]); }
            var a = floor.Plan.Spaces.First(s => s.Id == l.Between[0]); var b = floor.Plan.Spaces.First(s => s.Id == l.Between[1]);
            float x0 = Math.Max(a.Min[0], b.Min[0]), x1 = Math.Min(a.Max[0], b.Max[0]), z0 = Math.Max(a.Min[1], b.Min[1]), z1 = Math.Min(a.Max[1], b.Max[1]);
            return new LinkInspection(l.Id, l.Between, [(x0 + x1) / 2, (z0 + z1) / 2]);
        }).ToArray();
        return DebugCommandResult.Success(JsonSerializer.Serialize(new FloorInspection(floors.RunSeed, floors.Depth, World.Excursion.Id,
            World.Excursion.Plan.TrimStyle, World.Excursion.Plan.Decor, floor?.Identity.PlanHash, floor?.Candidate, floor?.Attempt, places, spaces, links,
            floor?.Layout.Placements.Length, floor?.Content.Finds.Length, floor?.Content.Residents.Length,
            floor?.Confirmation?.Routes.Length, floors.LastRefusals, floors.LastMilliseconds), DeveloperJson.Default.FloorInspection));
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
            w.Supplies.Health, w.Supplies.Ammo, w.Supplies.Summon, w.Supplies.Occupied, w.Supplies.Revision, w.Supplies.Message, w.Combat.Holding?.Item.Id ?? "",
            w.Combat.Phase.ToString(), w.Combat.AcceptedAttacks, w.Combat.LandedHits, w.Spirit.AnyAcquired, w.Spirit.Equipped is not null, w.Spirit.Revision,
            w.Spirit.Phase.ToString(), w.Spirit.Elapsed, w.Spirit.Calls, w.Spirit.Message, w.Expedition.Returns, w.Expedition.SecuredFinds,
            w.Expedition.Status, w.Combat.Enemies.Select(e => new EnemyObservation(e.Id, e.Kind.Id, [e.Position.X, e.Position.Y, e.Position.Z],
                e.Health.ValueInt, e.Phase.ToString(), e.Awareness.Target is not null)).ToArray(),
            new GrowthObservation(w.Supplies.Growth.Level, w.Supplies.Growth.Experience, w.Supplies.Growth.Capture().Uses,
                w.Supplies.Growth.Definition.Skills.ToDictionary(s => s.Id, s => w.Supplies.Growth.Rank(s.Id)), [.. w.Supplies.Growth.Relics])),
            DeveloperJson.Default.HotelObservation));
    }
}

internal sealed record HotelObservation(ulong Step, int Depth, float[] Position, float[] Eye, float Yaw, float Pitch, bool Grounded, string Location, string Prompt,
    string[] OpenDoors, ulong ReadingSequence, int Health, int Ammo, int Summon, int OccupiedPockets, ulong InventoryRevision, string SupplyMessage,
    string Weapon, string AttackPhase, int AcceptedAttacks, int LandedHits, bool SpiritAcquired, bool SpiritEquipped, ulong SpiritRevision,
    string SpiritPhase, float SpiritElapsed, int SpiritCalls, string SpiritMessage, int CheckpointReturns, string[] SecuredFinds,
    string CheckpointStatus, EnemyObservation[] Enemies, GrowthObservation Growth);
internal sealed record GrowthObservation(int Level, int Experience, Dictionary<string, int> Uses, Dictionary<string, int> Ranks, string[] Relics);
internal sealed record EnemyObservation(string Id, string Kind, float[] Position, int Health, string Phase, bool Aware);
internal sealed record PlaceInspection(string Place, string Placement, string Module, float[] Centre, float[]? Door);
internal sealed record SpaceInspection(string Id, string Label, float[] Min, float[] Max);
internal sealed record LinkInspection(string Id, string[] Between, float[] Point);
internal sealed record FloorInspection(ulong RunSeed, int Depth, string Excursion, string TrimStyle, string? Decor, string? PlanHash, int? Candidate, int? Attempt,
    PlaceInspection[]? Places, SpaceInspection[]? Spaces, LinkInspection[]? Links, int? Modules, int? Finds, int? Residents, int? PromisedRoutes, string[] LastRefusals, double LastMilliseconds);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(HotelObservation))]
[JsonSerializable(typeof(FloorInspection))]
internal sealed partial class DeveloperJson : JsonSerializerContext;
