using Hotel.Game.Mechanics;
using System.Text.Json.Serialization;
using Hotel.Game.Supplies;

namespace Hotel.Game.Expedition;

// Meaningful product values only. Refuge recovery deliberately clears transient attacks/motion.
// A checkpoint of another version is refused like any invalid save; it is never reinterpreted.
internal sealed record CheckpointState(int Version, int Returns, string Refuge,
    SuppliesState Supplies, string[] OpenDoors, SpiritState Spirit, ResidentState[] Residents, string[] SecuredFinds,
    Floors.FloorsState? Floors = null)
{
    /// <summary>Each version's additions are listed in docs/authoring.md; version 7 holds weapons as worn items, not a weapon id.</summary>
    internal const int CurrentVersion = 7;
}
internal sealed record SuppliesState(Mechanics.ActorStatsState Stats, ItemStack?[] Pockets, WornState[] Worn, string[] Collected);
internal sealed record SpiritState(string Id, bool Acquired, bool Equipped);
internal sealed record ResidentState(string Id, ActorStatsState Stats, float X, float Y, float Z, float Yaw);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true)]
[JsonSerializable(typeof(CheckpointState))]
internal sealed partial class CheckpointJson : JsonSerializerContext;
