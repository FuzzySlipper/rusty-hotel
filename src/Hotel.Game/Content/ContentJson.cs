using System.Text.Json.Serialization;
using Rusty.Engine;
using Hotel.Game.Audio;
using Hotel.Game.Combat;
using Hotel.Game.Expedition;
using Hotel.Game.Floors.Mission;
using Hotel.Game.Floors.Modules;
using Hotel.Game.Floors.Layout;
using Hotel.Game.Floors;
using Hotel.Game.Floors.Content;
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
    Converters = [typeof(JsonStringEnumConverter<KeyboardControl>), typeof(JsonStringEnumConverter<PointerButton>),
        typeof(JsonStringEnumConverter<ToneMappingOperator>), typeof(JsonStringEnumConverter<FogMode>),
        typeof(JsonStringEnumConverter<Rusty.Engine.Mechanics.ItemKind>), typeof(JsonStringEnumConverter<TweenEasingKind>), typeof(JsonStringEnumConverter<DynamicsRagdollShape>)])]
[JsonSerializable(typeof(ControlBindings))]
[JsonSerializable(typeof(PlayerTuning))]
[JsonSerializable(typeof(InteractionTuning))]
[JsonSerializable(typeof(InterfaceTuning))]
[JsonSerializable(typeof(SurfaceCatalog))]
[JsonSerializable(typeof(AgingCatalog))]
[JsonSerializable(typeof(Hotel.Game.Mechanics.StatVocabulary))]
[JsonSerializable(typeof(Hotel.Game.Mechanics.DamageVocabulary))]
[JsonSerializable(typeof(Hotel.Game.Mechanics.EffectCatalog))]
[JsonSerializable(typeof(Hotel.Game.Mechanics.MechanicsMessages))]
[JsonSerializable(typeof(Hotel.Game.Mechanics.ActorStatBlock))]
[JsonSerializable(typeof(SceneLook))]
[JsonSerializable(typeof(ItemCatalog))]
[JsonSerializable(typeof(EquipmentCatalog))]
[JsonSerializable(typeof(CapacityCatalog))]
[JsonSerializable(typeof(StartingKit))]
[JsonSerializable(typeof(Hotel.Game.Loot.QualityCatalog))]
[JsonSerializable(typeof(Hotel.Game.Loot.AffixCatalog))]
[JsonSerializable(typeof(Hotel.Game.Loot.LootTableCatalog))]
[JsonSerializable(typeof(Hotel.Game.Actions.ActionCatalog))]
[JsonSerializable(typeof(SupplyMessages))]
[JsonSerializable(typeof(DroppingTuning))]
[JsonSerializable(typeof(Hotel.Game.Progression.GrowthCatalog))]
[JsonSerializable(typeof(Hotel.Game.Progression.GrowthMessages))]
[JsonSerializable(typeof(CombatTuning))]
[JsonSerializable(typeof(HeldCatalog))]
[JsonSerializable(typeof(CombatMessages))]
[JsonSerializable(typeof(Hotel.Game.Residents.ResidentCatalog))]
[JsonSerializable(typeof(Hotel.Game.Residents.FactionCatalog))]
[JsonSerializable(typeof(Hotel.Game.Residents.LookCatalog))]
[JsonSerializable(typeof(SpiritDefinition))]
[JsonSerializable(typeof(SpiritRoster))]
[JsonSerializable(typeof(SpiritMessages))]
[JsonSerializable(typeof(RouteMessages))]
[JsonSerializable(typeof(ExpeditionMessages))]
[JsonSerializable(typeof(TitleDefinition))]
[JsonSerializable(typeof(Hotel.Game.Combat.HeldMotionCatalog))]
[JsonSerializable(typeof(Hotel.Game.Combat.HeldPose))]
[JsonSerializable(typeof(Hotel.Game.Combat.RagdollCatalog))]
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
[JsonSerializable(typeof(GenerationTuning))]
[JsonSerializable(typeof(ContentTuning))]
[JsonSerializable(typeof(FloorReadings))]
internal sealed partial class ContentJson : JsonSerializerContext;
