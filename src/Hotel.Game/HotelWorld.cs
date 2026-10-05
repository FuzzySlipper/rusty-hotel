using Hotel.Game.Audio;
using Hotel.Game.Combat;
using Hotel.Game.Content;
using Hotel.Game.Expedition;
using Hotel.Game.Player;
using Hotel.Game.Route;
using Hotel.Game.Scene;
using Hotel.Game.Spirits;
using Hotel.Game.Supplies;
using Rusty.Engine;
using Rusty.Engine.Interaction;

namespace Hotel.Game;

/// <summary>What the player carries from one floor to the next: supplies and resources, the pact, and the weapon in hand.</summary>
internal sealed record WorldCarry(SuppliesState Supplies, SpiritState Spirit, string Weapon);

/// <summary>What a floor keeps while the player is elsewhere in the session: its opened doors and its residents.</summary>
/// <param name="Residents">The residents as left, or null for a floor whose residents start fresh.</param>
internal sealed record WorldMemory(string[] OpenDoors, ResidentState[]? Residents, string[] Keys);

/// <summary>
/// The owners of one excursion's world, built together for the floor the player stands on and disposed together when
/// they take the stairs. Product-wide owners (content, HUD, controls, floors and the one world interaction) outlive it.
/// </summary>
internal sealed class HotelWorld : IDisposable
{
    private readonly List<IDisposable> owned = [];

    internal HotelWorld(IEngineContext engine, HotelContent content, Floors.HotelFloors floors, ExcursionDefinition excursion, WorldInteraction interaction,
        Func<bool> returnToRefuge, Action publishInterface, Func<StairDirection, bool> travel)
    {
        Excursion = excursion;
        try
        {
            Scene = Own(new HotelScene(engine, content.Surfaces, excursion.Geometry, excursion.Route.Doors));
            Player = Own(new HotelPlayer(engine, Scene, content.Player, excursion.Placements.Arrival, content.Controls));
            Supplies = new HotelSupplies(content.Supplies, excursion.Placements.Finds, content.Interface.SupplyPockets, Scene.PlayerEntity);
            Combat = new HotelCombat(engine, Scene, Player, Supplies, content.Combat, excursion.Placements.Residents,
                content.Controls.Weapons.Select(w => w.Label).ToArray());
            Spirit = new HotelSpirit(content.Spirit, content.SpiritText, excursion.Placements.SpiritBells[0], Supplies, Combat, Player);
            Route = new HotelRoute(engine, Scene, Player, Supplies, Spirit, content.Route, excursion.Route,
                excursion.Placements.Refuge, returnToRefuge, publishInterface, travel, interaction);
            Ambience = Own(new HotelAmbience(engine, excursion.Ambience));
            CombatView = Own(new CombatView(engine, Scene, Player, Combat));
            SpiritView = Own(new SpiritView(engine, Scene, Spirit));
            // The checkpoint always belongs to the authored refuge, whichever floor this world is.
            Expedition = Own(new HotelExpedition(engine, content.Excursion.Placements.Refuge!, content.ExpeditionText,
                Player, Supplies, Combat, Spirit, Route, floors));
        }
        catch { Dispose(); throw; }
    }

    internal ExcursionDefinition Excursion { get; }
    internal HotelScene Scene { get; } = null!;
    internal HotelPlayer Player { get; } = null!;
    internal HotelSupplies Supplies { get; } = null!;
    internal HotelCombat Combat { get; } = null!;
    internal HotelSpirit Spirit { get; } = null!;
    internal HotelRoute Route { get; } = null!;
    internal HotelAmbience Ambience { get; } = null!;
    internal CombatView CombatView { get; } = null!;
    internal SpiritView SpiritView { get; } = null!;
    internal HotelExpedition Expedition { get; } = null!;
    internal bool HasRefuge => Excursion.Placements.Refuge is not null;

    internal WorldCarry Carry() => new(Supplies.Capture(), Spirit.Capture(), Combat.Weapon.Id);
    internal WorldMemory Remember() => new(Route.OpenDoors, Combat.Capture(), Route.Keys);

    /// <summary>
    /// Brings the player's carried values into this freshly built world, with what it remembered from an earlier visit
    /// this session. Collected finds belonging to this floor stay collected; others are the product's to keep.
    /// </summary>
    internal void Enter(WorldCarry carry, IReadOnlySet<string> collected, WorldMemory? memory)
    {
        Supplies.Restore(carry.Supplies with { Collected = carry.Supplies.Collected.Concat(collected).Distinct()
            .Where(id => Supplies.Finds.Any(f => f.Id == id)).Order(StringComparer.Ordinal).ToArray() });
        Spirit.Restore(carry.Spirit);
        if (memory?.Residents is { } left) Combat.Validate(carry.Weapon, left);
        Combat.Restore(carry.Weapon, memory?.Residents ?? []);
        Route.Restore(memory?.OpenDoors ?? [], memory?.Keys);
    }

    private T Own<T>(T resource) where T : IDisposable
    {
        owned.Add(resource);
        return resource;
    }

    public void Dispose()
    {
        // Reverse construction order: dependents release before the scene they draw into.
        for (int i = owned.Count - 1; i >= 0; i--) owned[i].Dispose();
        owned.Clear();
    }
}
