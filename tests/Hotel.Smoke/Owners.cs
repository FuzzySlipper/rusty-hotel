using Hotel.Game;
using Hotel.Game.Combat;
using Hotel.Game.Content;
using Hotel.Game.Player;
using Hotel.Game.Route;
using Hotel.Game.Scene;
using Hotel.Game.Spirits;
using Hotel.Game.Supplies;
using Rusty.Engine;
using Rusty.Engine.Entities;

// Composes real owners from the real authored content, the same way HotelProduct does.
internal static class Owners
{
    internal static HotelContent Content(IEngineContext engine) => HotelContent.Load(engine, HotelProduct.StartingExcursion);
    internal static HotelScene Scene(IEngineContext engine, HotelContent content) =>
        new(engine, content.Surfaces, content.Excursion.Geometry, content.Excursion.Route.Doors);
    internal static HotelPlayer Player(IEngineContext engine, HotelScene scene, HotelContent content) =>
        new(engine, scene, content.Player, content.Excursion.Placements.Arrival, content.Controls);
    internal static HotelSupplies Supplies(HotelContent content, EntityId owner, int? capacity = null) =>
        new(content.Supplies, content.Excursion.Placements.Finds, capacity ?? content.Interface.SupplyPockets, owner,
            content.Mechanics, content.PlayerStats);
    internal static HotelCombat Combat(IEngineContext engine, HotelScene scene, HotelPlayer player, HotelSupplies supplies, HotelContent content) =>
        new(engine, scene, player, supplies, content.Combat, content.Excursion.Placements.Residents);
    internal static HotelSpirit Spirit(HotelContent content, HotelSupplies supplies, HotelCombat combat, HotelPlayer player) =>
        new(content.Spirit, content.SpiritText, content.SpiritBell, supplies, combat, player, content.Mechanics.Effect(content.Spirit.Effect)!);
    internal static HotelRoute Route(IEngineContext engine, HotelScene scene, HotelPlayer player, HotelSupplies supplies,
        HotelSpirit spirit, HotelContent content, Func<bool>? recordCheckpoint = null) =>
        new(engine, scene, player, supplies, spirit, content.Route, content.Excursion.Route,
            content.Excursion.Placements.Refuge, recordCheckpoint ?? (() => false), () => { }, _ => false);
}
