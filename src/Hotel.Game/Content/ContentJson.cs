using System.Text.Json.Serialization;
using Rusty.Engine;
using Hotel.Game.Audio;
using Hotel.Game.Combat;
using Hotel.Game.Expedition;
using Hotel.Game.Floors.Mission;
using Hotel.Game.Floors.Modules;
using Hotel.Game.Floors.Layout;
using Hotel.Game.Input;
using Hotel.Game.Interface;
using Hotel.Game.Player;
using Hotel.Game.Route;
using Hotel.Game.Scene;
using Hotel.Game.Scene.Kit;
using Hotel.Game.Spirits;
using Hotel.Game.Supplies;

namespace Hotel.Game.Content;

// Missing constructor values, nulls in non-nullable fields and unknown members are errors, not defaults.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    Converters = [typeof(JsonStringEnumConverter<KeyboardControl>), typeof(JsonStringEnumConverter<PointerButton>)])]
[JsonSerializable(typeof(ControlBindings))]
[JsonSerializable(typeof(PlayerTuning))]
[JsonSerializable(typeof(InteractionTuning))]
[JsonSerializable(typeof(InterfaceTuning))]
[JsonSerializable(typeof(SurfaceCatalog))]
[JsonSerializable(typeof(SupplyResources))]
[JsonSerializable(typeof(ItemCatalog))]
[JsonSerializable(typeof(SupplyMessages))]
[JsonSerializable(typeof(CombatTuning))]
[JsonSerializable(typeof(WeaponCatalog))]
[JsonSerializable(typeof(CombatMessages))]
[JsonSerializable(typeof(ResidentCatalog))]
[JsonSerializable(typeof(SpiritDefinition))]
[JsonSerializable(typeof(SpiritMessages))]
[JsonSerializable(typeof(RouteMessages))]
[JsonSerializable(typeof(ExpeditionMessages))]
[JsonSerializable(typeof(KitDefinition))]
[JsonSerializable(typeof(FixtureCatalog))]
[JsonSerializable(typeof(FloorPlan))]
[JsonSerializable(typeof(RoutePlan))]
[JsonSerializable(typeof(PlacementPlan))]
[JsonSerializable(typeof(AmbienceDefinition))]
[JsonSerializable(typeof(MissionTuning))]
[JsonSerializable(typeof(ModuleCatalogFile))]
[JsonSerializable(typeof(ModuleDefinition))]
[JsonSerializable(typeof(LayoutTuning))]
internal sealed partial class ContentJson : JsonSerializerContext;
