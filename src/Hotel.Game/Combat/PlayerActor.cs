using System.Numerics;
using Hotel.Game.Actions;
using Hotel.Game.Mechanics;
using Hotel.Game.Player;
using Hotel.Game.Scene;
using Hotel.Game.Supplies;
using Rusty.Engine;

namespace Hotel.Game.Combat;

/// <summary>The investigator as an action user and target: the player's body, the supplies' stats and what they wear.</summary>
internal sealed class PlayerActor(HotelScene scene, HotelPlayer player, HotelSupplies supplies) : IActionActor
{
    public ActorStats Stats => supplies.Stats;
    public ulong Entity => scene.PlayerEntity.Value;
    public Vector3 Eye => player.Eye;
    public Vector3 Position => player.Position;
    public bool Alive => supplies.Health > 0;
    public IEnumerable<ActiveContribution> Contributions => supplies.Worn.SelectMany(w => supplies.Loot.Contributions(w.Item, w.Roll))
        .Select(c => new ActiveContribution(c)).Concat(supplies.Growth.Contributions).Concat(supplies.Stats.Effects.HitContributions);
    // A generated held item's affixes may put effects on what its hits strike.
    public IEnumerable<string> HitEffects => supplies.Loot.OnHit((supplies.Held(Hand.Main) ?? supplies.Held(Hand.Off))?.Roll);
    internal ActionUser User { get; } = new();

    public SpatialEntityCollider Hitbox
    {
        get
        {
            PlayerTuning body = player.Tuning;
            Vector3 half = new(body.Radius, body.Height / 2, body.Radius);
            return new(Entity, player.Position - half, player.Position + half, 1, uint.MaxValue, true, false, false);
        }
    }
}
