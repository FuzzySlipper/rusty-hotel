using System.Numerics;
using Hotel.Game.Actions;
using Hotel.Game.Player;
using Hotel.Game.Expedition;
using Hotel.Game.Content;
using Hotel.Game.Mechanics;
using Hotel.Game.Residents;
using Hotel.Game.Scene;
using Hotel.Game.Supplies;
using Rusty.Engine;
using Rusty.Engine.Entities;

namespace Hotel.Game.Combat;

internal enum AttackPhase { Ready, Windup, Commit, Recovery, Interrupted, Defeated }

/// <summary>
/// Encounter policy over the one action pipeline: the investigator acts through what their hands hold, residents
/// attack with their kind's action, and both land through <see cref="ActionResolution"/>. Engine owns collision,
/// queries and the stats; Supplies owns what is carried and worn.
/// </summary>
internal sealed class HotelCombat
{
    private readonly IEngineContext engine;
    private readonly HotelScene scene;
    private readonly HotelPlayer player;
    private readonly HotelSupplies supplies;
    private readonly CombatDefinition definition;
    private readonly CombatMessages text;
    private readonly ActionResolution resolution;
    private readonly PlayerActor investigator;
    private readonly ResidentSenses perception;
    private readonly ResidentConduct conduct;
    private float noticeRemaining;

    internal HotelCombat(IEngineContext engine, HotelScene scene, HotelPlayer player, HotelSupplies supplies,
        CombatDefinition definition, ResidentPlacement[] residents)
    {
        this.engine = engine; this.scene = scene; this.player = player; this.supplies = supplies;
        this.definition = definition;
        text = definition.Text;
        resolution = new(engine, scene.Session, definition.Mechanics);
        investigator = new(scene, player, supplies);
        perception = new(engine, scene.Session, resolution, definition.Factions);
        conduct = new(engine, scene, definition.Actions);
        Enemies = residents.Select(placed => new HotelEnemy(engine, scene, placed,
            definition.Residents.Single(kind => kind.Id == placed.Kind), player.Tuning.Gravity, definition.Mechanics)).ToArray();
    }

    internal HotelEnemy[] Enemies { get; }
    internal IReadOnlyList<Projectile> Projectiles => resolution.Projectiles;
    /// <summary>The investigator's action in progress and cooldowns.</summary>
    internal ActionUser User => investigator.User;
    internal AttackPhase Phase => User.Phase switch
    {
        ActionPhase.Windup => AttackPhase.Windup, ActionPhase.Commit => AttackPhase.Commit,
        ActionPhase.Recovery => AttackPhase.Recovery, _ => AttackPhase.Ready
    };
    internal float PhaseProgress => User.PhaseProgress;
    internal string Notice { get; private set; } = "";
    internal float HurtFlash { get; private set; }
    internal float HitFlash { get; private set; }
    internal int AcceptedAttacks { get; private set; }
    internal int LandedHits { get; private set; }
    internal bool Defeated => supplies.Health == 0;
    /// <summary>The item in the main hand, or else the off hand: the one primary and secondary act through.</summary>
    internal WornItem? Holding => supplies.Held(Hand.Main) ?? supplies.Held(Hand.Off);
    internal string HandsText => Holding?.Item.Name ?? text.EmptyHands;
    internal string ActionText => Defeated ? text.Overwhelmed : Phase switch
    {
        AttackPhase.Windup => User.Current!.WindupLabel,
        AttackPhase.Commit => User.Current!.CommitLabel,
        AttackPhase.Recovery => text.Recovering, _ => text.Ready
    };
    internal CharacterObstacle[] Obstacles => Enemies.Where(e => e.Alive).Select(e => e.Obstacle).ToArray();

    /// <summary>
    /// The action the primary or secondary control uses now: the held item's first or second action; an item with one
    /// action in the main hand leaves the secondary to what the off hand holds.
    /// </summary>
    internal ActionDefinition? HandAction(bool secondary)
    {
        if (Holding is not { } holding) return null;
        string[] granted = holding.Item.Wear!.Actions;
        string? id = !secondary ? granted[0] : granted.Length > 1 ? granted[1]
            : holding == supplies.Held(Hand.Main) ? supplies.Held(Hand.Off)?.Item.Wear!.Actions[0] : null;
        return id is null ? null : definition.Actions.Action(id);
    }

    /// <summary>
    /// Starts the held item's primary or secondary action: refused while busy, defeated or held, on cooldown, or when
    /// its cost cannot be met. Track costs are spent here, once; an item cost is used when the action lands.
    /// </summary>
    internal bool Act(bool secondary)
    {
        if (Defeated || supplies.Stats.Effects.Held) return false;
        if (HandAction(secondary) is not { } action) { Announce(text.EmptyHands); return false; }
        switch (User.Readiness(action))
        {
            case ActionRefusal.Busy: return false;
            case ActionRefusal.Cooldown: Announce(Template.Fill(text.NotReady, ("action", action.Name))); return false;
        }
        if (CostRefusal(action) is { } refusal) { Announce(refusal); return false; }
        foreach (var (track, amount) in action.Cost.Tracks) supplies.Stats.Track(track).TrySpend(amount);
        supplies.Changed();
        AcceptedAttacks++;
        User.Begin(action, player.Forward);
        return true;
    }

    /// <summary>Trades what the hands hold, while no action is under way.</summary>
    internal bool SwapHands()
    {
        if (Defeated || User.Busy || !supplies.SwapHands()) return false;
        Announce(Template.Fill(text.Swapped, ("item", HandsText)));
        return true;
    }

    internal void Step(float delta)
    {
        HurtFlash = Math.Max(0, HurtFlash - delta);
        HitFlash = Math.Max(0, HitFlash - delta);
        noticeRemaining = Math.Max(0, noticeRemaining - delta);
        if (noticeRemaining == 0) Notice = "";
        if (Defeated) { User.Interrupt(); resolution.Clear(); return; }
        if (supplies.Stats.Effects.Held) User.Interrupt();
        if (User.Step(delta) is { } landed) Land(landed, User.Aim);
        perception.Update(Enemies, Bodies, Faction, delta);
        foreach (HotelEnemy enemy in Enemies) StepEnemy(enemy, delta);
        foreach (ActionImpact impact in resolution.Step(delta, Bodies)) Settle(impact);
    }

    internal HotelEnemy? SpiritTarget(float range)
    {
        SpatialEntityCollider[] targets = Enemies.Where(e => e.Alive).Select(e => e.Hitbox).ToArray();
        SpatialHit hit = engine.Spatial.CastRay(new(scene.Session, player.Eye, player.Forward, range,
            new(1, uint.MaxValue), targets, new[] { scene.PlayerEntity.Value }, targets));
        return hit.Present && hit.Kind == SpatialHitKind.Entity
            ? Enemies.FirstOrDefault(e => e.Entity == hit.Entity && e.Alive) : null;
    }

    /// <summary>Living residents within a radius of the investigator, walls or not: what a reveal senses.</summary>
    internal int Sensed(float radius) => Enemies.Count(e => e.Alive && Vector3.Distance(e.Position, player.Position) <= radius);

    /// <summary>Holds a resident with a hold effect, cancelling whatever it was doing; it stands interrupted while held.</summary>
    internal void Interrupt(HotelEnemy enemy, EffectDefinition hold, string source)
    {
        enemy.Stats.Effects.Apply(hold, source);
        enemy.User.Interrupt();
        enemy.BeamTime = 0;
    }

    private string? CostRefusal(ActionDefinition action)
    {
        foreach (var (track, amount) in action.Cost.Tracks)
            if (supplies.Stats.Track(track).Value < amount)
                return Template.Fill(text.NotEnough, ("track", definition.Mechanics.Tracks.First(t => t.Id == track).Name), ("action", action.Name));
        if (action.Cost.Item is not { } classification) return null;
        int pocket = supplies.PocketOf(classification);
        if (pocket < 0)
            return Template.Fill(text.NeedsItem, ("action", action.Name),
                ("classification", supplies.Definition.Classifications.First(c => c.Id == classification).Name));
        string reason = supplies.UseReason(pocket);
        return reason.Length == 0 ? null : reason;
    }

    // The investigator's action lands: an item cost is used now, then the delivery resolves against the residents.
    private void Land(ActionDefinition action, Vector3 aim)
    {
        if (action.Cost.Item is { } classification)
        {
            int pocket = supplies.PocketOf(classification);
            bool used = pocket >= 0 && supplies.Use(pocket, supplies.Revision);
            Announce(supplies.Message);
            if (!used) return;
        }
        foreach (ActionImpact impact in resolution.Land(action, investigator, aim, Enemies)) Settle(impact);
    }

    // One place for what an impact means: hit feedback, a fallen resident, a hurt investigator, a resident's beam.
    private void Settle(ActionImpact impact)
    {
        if (impact.User == investigator)
        {
            if (impact.Action.Delivery.Kind == DeliveryKind.Self) return;
            if (impact.Target is HotelEnemy victim)
            {
                LandedHits++;
                HitFlash = definition.Tuning.HitFlashSeconds;
                Announce(Template.Fill(victim.Alive ? text.Hit : text.ResidentFalls, ("resident", victim.Kind.Name)));
                if (!victim.Alive) { victim.User.Interrupt(); victim.BeamTime = 0; }
            }
            else Announce(impact.Surface ? text.StruckSurroundings : text.Miss);
            return;
        }
        if (impact.User is not HotelEnemy enemy) return;
        if (impact.Action.Delivery.Kind == DeliveryKind.Hitscan) { enemy.BeamEnd = impact.End; enemy.BeamTime = impact.Action.Timing.Commit; }
        if (impact.Target == investigator)
        {
            supplies.Changed();
            HurtFlash = definition.Tuning.HurtFlashSeconds;
            Announce(Template.Fill(text.ResidentHits, ("resident", enemy.Kind.Name), ("damage", impact.Damage)));
        }
    }

    private void StepEnemy(HotelEnemy enemy, float delta)
    {
        enemy.BeamTime = Math.Max(0, enemy.BeamTime - delta);
        if (!enemy.Alive || Defeated) return;
        enemy.Stats.Effects.Advance(delta);
        enemy.Stats.Regenerate(delta);
        if (!enemy.Alive || enemy.Stats.Effects.Held) { enemy.User.Interrupt(); return; }
        if (enemy.User.Step(delta) is { } landed)
            foreach (ActionImpact impact in resolution.Land(landed, enemy, enemy.User.Aim, Hostiles(enemy))) Settle(impact);
        conduct.Step(enemy, delta);
    }

    /// <summary>The faction a body belongs to: the investigator's, or its resident kind's.</summary>
    internal string Faction(IActionActor body) => body is HotelEnemy resident ? resident.Kind.Faction : definition.Factions.Investigator;

    /// <summary>Every living body hostile to a resident: what its actions may hit.</summary>
    private IActionActor[] Hostiles(HotelEnemy enemy) => Bodies.Where(b => b.Alive && b != enemy &&
        definition.Factions.AreHostile(enemy.Kind.Faction, Faction(b))).ToArray();

    private IActionActor[] Bodies => [investigator, .. Enemies];

    /// <summary>How close a user must be for an action to reach: its range, or an area's range and radius.</summary>
    internal static float Reach(ActionDefinition action) =>
        action.Delivery.Kind == DeliveryKind.Area ? action.Delivery.Range + action.Delivery.Radius : action.Delivery.Range;

    internal ResidentState[] Capture() => Enemies.Select(e => new ResidentState(e.Id,
        e.Stats.Capture(), e.Position.X, e.Position.Y, e.Position.Z, e.Yaw)).ToArray();

    internal void Validate(ResidentState[] residents)
    {
        if (residents is null || residents.Length != Enemies.Length || residents.Select(e => e?.Id).Distinct().Count() != Enemies.Length)
            throw new InvalidOperationException("Checkpoint resident roster is invalid.");
        foreach (ResidentState? state in residents)
        {
            HotelEnemy? enemy = Enemies.FirstOrDefault(e => e.Id == state?.Id);
            if (state is null || enemy is null || state.Stats is null ||
                !float.IsFinite(state.X) || !float.IsFinite(state.Y) || !float.IsFinite(state.Z) || !float.IsFinite(state.Yaw) ||
                Vector3.Distance(new(state.X, state.Y, state.Z), enemy.Spawn) > enemy.Kind.Movement.Range + 1)
                throw new InvalidOperationException("Checkpoint resident values are invalid.");
            enemy.Stats.Validate(state.Stats);
        }
    }

    /// <summary>Restores residents as saved. Actions in progress and cooldowns are not saved: every actor stands ready.</summary>
    internal void Restore(ResidentState[] residents)
    {
        Reset();
        foreach (ResidentState state in residents)
        {
            HotelEnemy enemy = Enemies.Single(e => e.Id == state.Id);
            enemy.Stats.Restore(state.Stats);
            enemy.Yaw = state.Yaw;
            scene.Entities.Set(enemy.EntityId, EngineComponentTypes.Transform,
                new(new(state.X, state.Y, state.Z), Quaternion.Identity, Vector3.One));
        }
    }

    internal void Reset()
    {
        foreach (HotelEnemy enemy in Enemies) enemy.Reset();
        User.Reset(); resolution.Clear();
        noticeRemaining = HurtFlash = HitFlash = 0;
        AcceptedAttacks = LandedHits = 0; Notice = "";
    }
    private void Announce(string message) { Notice = message; noticeRemaining = definition.Tuning.NoticeSeconds; }
}
