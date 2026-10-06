using System.Text.Json.Serialization;
using Hotel.Game.Supplies;

namespace Hotel.Game.Expedition;

// Meaningful product values only. Refuge recovery deliberately clears transient attacks/motion.
// A checkpoint of another version is refused like any invalid save; it is never reinterpreted.
internal sealed record CheckpointState(int Version, int Returns, string Refuge, string Weapon,
    SuppliesState Supplies, string[] OpenDoors, SpiritState Spirit, ResidentState[] Residents, string[] SecuredFinds,
    Floors.FloorsState? Floors = null)
{
    /// <summary>Version 3 keeps the investigator's resources as Engine stats: stat bases and track currents.</summary>
    internal const int CurrentVersion = 3;
}
internal sealed record SuppliesState(Mechanics.ActorStatsState Stats, ItemStack?[] Pockets, string[] Collected);
internal sealed record SpiritState(string Id, bool Acquired, bool Equipped);
internal sealed record ResidentState(string Id, int Health, float X, float Y, float Z, float Yaw);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true)]
[JsonSerializable(typeof(CheckpointState))]
internal sealed partial class CheckpointJson : JsonSerializerContext;
