using System.Text.Json.Serialization;
using Hotel.Game.Supplies;

namespace Hotel.Game.Expedition;

// Meaningful product values only. Refuge recovery deliberately clears transient attacks/motion.
internal sealed record CheckpointState(int Version, int Returns, string Refuge, string Weapon,
    SuppliesState Supplies, string[] OpenDoors, SpiritState Spirit, ResidentState[] Residents, string[] SecuredFinds);
internal sealed record SuppliesState(int Health, int Ammo, int Summon, ItemStack?[] Pockets, string[] Collected);
internal sealed record SpiritState(string Id, bool Acquired, bool Equipped);
internal sealed record ResidentState(string Id, int Health, float X, float Y, float Z, float Yaw);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true)]
[JsonSerializable(typeof(CheckpointState))]
internal sealed partial class CheckpointJson : JsonSerializerContext;
