using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rusty.Engine;

namespace Hotel.Game.Scene;

internal sealed record HotelDefinition(float[] Spawn, float SpawnYawDegrees,
    PlayerTuning Player, SurfaceDefinition[] Materials, RoomBox[] Boxes, InterfaceTuning Interface,
    LightingDefinition Lighting, ModelDefinition[] Models, AmbientDefinition[] Ambience, RouteDefinition Route, SuppliesDefinition Supplies,
    CombatDefinition Combat, SpiritDefinition Spirit, RefugeDefinition Refuge)
{
    internal static HotelDefinition Load(IEngineContext engine)
    {
        using ContentReference content = engine.Content.OpenReference(new ContentOpenRequest("hotel.json"));
        ulong length = engine.Content.ReadReferenceInfo(content).Span[0].ByteLength;
        ReadOnlyMemory<byte> bytes = engine.Content.ReadBytes(new ContentReadBytesRequest(content, 0, checked((uint)length)));
        return JsonSerializer.Deserialize(bytes.Span, HotelJson.Default.HotelDefinition)
            ?? throw new InvalidOperationException("hotel.json must contain the hotel definition.");
    }

    internal static Vector3 Vector(float[] value) => value.Length == 3
        ? new(value[0], value[1], value[2])
        : throw new InvalidOperationException("Hotel coordinates must contain three components.");
}

internal sealed record RefugeDefinition(string Id, float[] Point);

internal sealed record PlayerTuning(float Height, float Radius, float EyeHeight, float Speed,
    float Gravity, float MaximumStepHeight, float MaximumSlopeDegrees, float PointerRadiansPerUnit,
    float FieldOfViewDegrees);
internal sealed record SurfaceDefinition(string Id, float[] Color, string? Texture = null,
    float TileWidth = 1, float TileHeight = 1, float Roughness = .9f, float Emission = 0);
internal sealed record RoomBox(string Name, float[] Min, float[] Max, string Material, bool Solid = true, string? Find = null);
internal sealed record InterfaceTuning(string Location, int SupplyPockets, int QuickPockets);
internal sealed record LightingDefinition(float[] AmbientColor, float AmbientIntensity, PointLightDefinition[] Points);
internal sealed record PointLightDefinition(float[] Position, float[] Color, float Intensity, float Range);
internal sealed record ModelDefinition(string Path, float[] Position, float Scale, float YawDegrees);
internal sealed record AmbientDefinition(string Path, float Volume, float[]? Position = null, float Range = 8);
internal sealed record RouteDefinition(float Reach, float FocusDistance, float AcquireAngle, float ReleaseAngle,
    DoorDefinition[] Doors, ReadingDefinition[] Readings, RoomDefinition[] Rooms);
internal sealed record DoorDefinition(string Id, string Label, float[] Hinge, float Width, float Height,
    float ClosedYaw, float OpenYaw, string Material, float[] FocusPoint, bool FarSideLatch = false,
    float[]? UnlockDirection = null);
internal sealed record ReadingDefinition(string Id, string Label, float[] Point, string Title, string Text);
internal sealed record RoomDefinition(string Id, string Label, float[] Min, float[] Max);

internal sealed record SuppliesDefinition(int InitialHealth, int MaximumHealth, int MaximumAmmo, int MaximumSummon,
    ItemDefinition[] Items, FindDefinition[] Finds);
internal sealed record ItemDefinition(string Id, string Name, string Description, SupplyKind Kind, int StackLimit, int Amount, string Mark);
internal sealed record FindDefinition(string Id, string Item, int Count, float[] Point);
[JsonConverter(typeof(JsonStringEnumConverter<SupplyKind>))]
internal enum SupplyKind { Healing, Ammo, Summon, Expedition }

internal sealed record CombatDefinition(WeaponDefinition[] Weapons, EnemyDefinition[] Enemies, float ReloadSeconds);
internal sealed record WeaponDefinition(string Id, string Name, int Damage, float Range, float Windup, float Commit, float Recovery, int AmmoCost);
internal sealed record EnemyDefinition(string Id, string Name, string Behavior, float[] Position,
    int Health, int Damage, float Speed, float SightRange, float AttackRange, float Windup, float Commit,
    float Recovery, float Leash, float Radius, float Height);

internal sealed record SpiritDefinition(string Id, string Name, string Description, float[] Point, int WelcomeCharges,
    int Cost, float Range, float Arrival, float Hold, float Departure);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(HotelDefinition))]
internal sealed partial class HotelJson : JsonSerializerContext;
