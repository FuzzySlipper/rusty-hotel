using System.Text.Json.Serialization;
using Hotel.Game.Audio;
using Hotel.Game.Combat;
using Hotel.Game.Interface;
using Hotel.Game.Player;
using Hotel.Game.Route;
using Hotel.Game.Scene;
using Hotel.Game.Spirits;
using Hotel.Game.Supplies;

namespace Hotel.Game.Content;

// Missing constructor values, nulls in non-nullable fields and unknown members are errors, not defaults.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(PlayerTuning))]
[JsonSerializable(typeof(InteractionTuning))]
[JsonSerializable(typeof(InterfaceTuning))]
[JsonSerializable(typeof(SurfaceCatalog))]
[JsonSerializable(typeof(SupplyResources))]
[JsonSerializable(typeof(ItemCatalog))]
[JsonSerializable(typeof(CombatDefinition))]
[JsonSerializable(typeof(ResidentCatalog))]
[JsonSerializable(typeof(SpiritDefinition))]
[JsonSerializable(typeof(ExcursionGeometry))]
[JsonSerializable(typeof(ExcursionRoute))]
[JsonSerializable(typeof(ExcursionPlacements))]
[JsonSerializable(typeof(AmbienceDefinition))]
internal sealed partial class ContentJson : JsonSerializerContext;
