using System.Numerics;
using Hotel.Game.Player;
using Hotel.Game.Expedition;
using Hotel.Game.Content;
using Hotel.Game.Scene;
using Hotel.Game.Supplies;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;

namespace Hotel.Game.Combat;

internal enum AttackPhase { Ready, Windup, Commit, Recovery, Reloading, Interrupted, Defeated }

/// <summary>Encounter policy and admitted attack timing; Engine owns collision, ray hits and resource arithmetic.</summary>
internal sealed class HotelCombat
{
    private readonly IEngineContext engine;
    private readonly HotelScene scene;
    private readonly HotelPlayer player;
    private readonly HotelSupplies supplies;
    private readonly CombatDefinition definition;
    private int weaponIndex;
    private float remaining, noticeRemaining;
    private int reloadPocket;
    private ulong reloadRevision;
    private Vector3 attackDirection;

    internal HotelCombat(IEngineContext engine, HotelScene scene, HotelPlayer player, HotelSupplies supplies,
        CombatDefinition definition, ResidentKind[] kinds, ResidentPlacement[] residents)
    {
        this.engine = engine; this.scene = scene; this.player = player; this.supplies = supplies;
        this.definition = definition;
        Enemies = residents.Select(placed => new HotelEnemy(engine, scene, placed,
            kinds.Single(kind => kind.Id == placed.Kind), player.Tuning.Gravity)).ToArray();
    }

    internal HotelEnemy[] Enemies { get; }
    internal WeaponDefinition Weapon => definition.Weapons[weaponIndex];
    internal AttackPhase Phase { get; private set; }
    internal float PhaseProgress => 1 - remaining / Math.Max(.001f, Phase switch
    {
        AttackPhase.Windup => Weapon.Windup, AttackPhase.Commit => Weapon.Commit,
        AttackPhase.Recovery => Weapon.Recovery, AttackPhase.Reloading => definition.ReloadSeconds, _ => 1
    });
    internal string Notice { get; private set; } = "";
    internal float HurtFlash { get; private set; }
    internal float HitFlash { get; private set; }
    internal int AcceptedAttacks { get; private set; }
    internal int LandedHits { get; private set; }
    internal Vector3 ShotEnd { get; private set; }
    internal bool Defeated => supplies.Health == 0;
    internal string ActionText => Defeated ? "Overwhelmed · R to return to the refuge checkpoint" : Phase switch
    {
        AttackPhase.Windup => Weapon.AmmoCost == 0 ? "Drawing back" : "Steadying shot",
        AttackPhase.Commit => Weapon.AmmoCost == 0 ? "Swing" : "Fired",
        AttackPhase.Recovery => "Recovering", AttackPhase.Reloading => "Loading cartridges", _ => "Ready"
    };
    internal CharacterObstacle[] Obstacles => Enemies.Where(e => e.Alive).Select(e => e.Obstacle).ToArray();

    internal bool SelectWeapon(int index)
    {
        if (Defeated || Phase != AttackPhase.Ready || index < 0 || index >= definition.Weapons.Length) return false;
        weaponIndex = index;
        return true;
    }

    internal bool Attack()
    {
        if (Defeated || Phase != AttackPhase.Ready) return false;
        if (Weapon.AmmoCost > 0 && !supplies.SpendAmmo(Weapon.AmmoCost))
        { Announce("Empty pistol · R loads carried cartridges · 1 selects the pry bar"); return false; }
        // Accepted attack commits its cost once. Further presses during the sequence are discarded.
        AcceptedAttacks++;
        attackDirection = player.Forward;
        Phase = AttackPhase.Windup;
        remaining = Weapon.Windup;
        return true;
    }

    internal bool Reload()
    {
        if (Defeated || Phase != AttackPhase.Ready || Weapon.AmmoCost == 0) return false;
        reloadPocket = supplies.AmmoPocket;
        if (reloadPocket < 0) { Announce("No carried cartridges"); return false; }
        string reason = supplies.UseReason(reloadPocket);
        if (reason.Length != 0) { Announce(reason); return false; }
        reloadRevision = supplies.Revision;
        Phase = AttackPhase.Reloading;
        remaining = definition.ReloadSeconds;
        return true;
    }

    internal void Step(float delta)
    {
        HurtFlash = Math.Max(0, HurtFlash - delta);
        HitFlash = Math.Max(0, HitFlash - delta);
        noticeRemaining = Math.Max(0, noticeRemaining - delta);
        if (noticeRemaining == 0) Notice = "";
        if (Defeated) return;
        if (Phase != AttackPhase.Ready)
        {
            remaining -= delta;
            if (remaining <= 0)
            {
                switch (Phase)
                {
                    case AttackPhase.Windup:
                        ResolveAttack(); Phase = AttackPhase.Commit; remaining += Weapon.Commit; break;
                    case AttackPhase.Commit:
                        Phase = AttackPhase.Recovery; remaining += Weapon.Recovery; break;
                    case AttackPhase.Reloading:
                        bool loaded = supplies.Use(reloadPocket, reloadRevision);
                        Announce(loaded ? "Cartridges loaded" : supplies.Message);
                        Phase = AttackPhase.Ready; break;
                    case AttackPhase.Recovery: Phase = AttackPhase.Ready; break;
                }
            }
        }
        foreach (HotelEnemy enemy in Enemies) StepEnemy(enemy, delta);
    }

    internal HotelEnemy? SpiritTarget(float range)
    {
        SpatialEntityCollider[] targets = Enemies.Where(e => e.Alive).Select(e => e.Hitbox).ToArray();
        SpatialHit hit = engine.Spatial.CastRay(new(scene.Session, player.Eye, player.Forward, range,
            new(1, uint.MaxValue), targets, new[] { scene.PlayerEntity.Value }, targets));
        return hit.Present && hit.Kind == SpatialHitKind.Entity
            ? Enemies.FirstOrDefault(e => e.Entity.Value == hit.Entity && e.Alive) : null;
    }

    internal void Interrupt(HotelEnemy enemy, float duration)
    {
        enemy.Phase = AttackPhase.Interrupted;
        enemy.Remaining = duration;
        enemy.BeamTime = 0;
    }

    private void ResolveAttack()
    {
        SpatialEntityCollider[] targets = Enemies.Where(e => e.Alive).Select(e => e.Hitbox).ToArray();
        SpatialHit hit = engine.Spatial.CastRay(new(scene.Session, player.Eye, attackDirection, Weapon.Range,
            new(1, uint.MaxValue), targets, new[] { scene.PlayerEntity.Value }, targets));
        ShotEnd = hit.Present ? hit.Point : player.Eye + attackDirection * Weapon.Range;
        HotelEnemy? victim = hit.Present && hit.Kind == SpatialHitKind.Entity
            ? Enemies.FirstOrDefault(e => e.Entity.Value == hit.Entity && e.Alive) : null;
        if (victim is null) { Announce(hit.Present ? "Struck the surroundings" : "Miss"); return; }
        victim.Health.SetCurrent(Math.Max(0, victim.Health.ValueInt - Weapon.Damage));
        LandedHits++;
        HitFlash = .22f;
        Announce(victim.Alive ? $"Hit · {victim.Kind.Name}" : $"{victim.Kind.Name} falls still");
        if (!victim.Alive) { victim.Phase = AttackPhase.Defeated; victim.BeamTime = 0; }
    }

    private void StepEnemy(HotelEnemy enemy, float delta)
    {
        enemy.BeamTime = Math.Max(0, enemy.BeamTime - delta);
        if (!enemy.Alive || Defeated) return;
        Vector3 toPlayer = player.Eye - enemy.Eye;
        float distance = toPlayer.Length();
        bool visible = distance <= enemy.Kind.SightRange && ClearLine(enemy.Eye, player.Eye);
        bool withinLeash = Vector3.Distance(new(player.Position.X, 0, player.Position.Z),
            new(enemy.Spawn.X, 0, enemy.Spawn.Z)) <= enemy.Kind.Leash;
        if (enemy.Phase == AttackPhase.Ready)
        {
            if (!visible) return;
            enemy.Yaw = MathF.Atan2(toPlayer.X, -toPlayer.Z);
            if (distance <= enemy.Kind.AttackRange)
            {
                enemy.Phase = AttackPhase.Windup;
                enemy.Remaining = enemy.Kind.Windup;
                enemy.Aim = Vector3.Normalize(toPlayer); // A committed direction: strafing can evade it.
            }
            else if (enemy.Kind.Speed > 0 && withinLeash)
            {
                // This resident stops to attack before body contact; only hotel geometry constrains its approach.
                CharacterStepReceipt move = engine.Spatial.ProposeCharacterStep(new(scene.Session,
                    enemy.Position, enemy.Motion, default, ReadOnlyMemory<CharacterObstacle>.Empty, ReadOnlyMemory<CharacterMeshInstance>.Empty,
                    enemy.Controller, new(new(0, 1), enemy.Yaw, false, false, false, default, default, delta, ++enemy.Sequence)));
                scene.Entities.Set(enemy.Entity, EngineComponentTypes.Transform, move.Transform);
                scene.Entities.Set(enemy.Entity, EngineComponentTypes.CharacterMotion, move.Motion);
            }
            return;
        }
        enemy.Remaining -= delta;
        if (enemy.Remaining > 0) return;
        switch (enemy.Phase)
        {
            case AttackPhase.Windup:
                SpatialEntityCollider body = PlayerHitbox();
                SpatialHit hit = engine.Spatial.CastRay(new(scene.Session, enemy.Eye, enemy.Aim,
                    enemy.Kind.AttackRange, new(2, uint.MaxValue), new[] { body }, new[] { enemy.Entity.Value }, new[] { body }));
                enemy.BeamEnd = hit.Present ? hit.Point : enemy.Eye + enemy.Aim * enemy.Kind.AttackRange;
                enemy.BeamTime = enemy.Kind.Commit;
                if (hit.Present && hit.Kind == SpatialHitKind.Entity && hit.Entity == scene.PlayerEntity.Value)
                {
                    supplies.Damage(enemy.Kind.Damage);
                    HurtFlash = .4f;
                    Announce($"{enemy.Kind.Name} hits · −{enemy.Kind.Damage} health");
                }
                enemy.Phase = AttackPhase.Commit; enemy.Remaining += enemy.Kind.Commit; break;
            case AttackPhase.Commit:
                enemy.Phase = AttackPhase.Recovery; enemy.Remaining += enemy.Kind.Recovery; break;
            case AttackPhase.Recovery:
            case AttackPhase.Interrupted: enemy.Phase = AttackPhase.Ready; break;
        }
    }

    private SpatialEntityCollider PlayerHitbox()
    {
        PlayerTuning body = player.Tuning;
        Vector3 half = new(body.Radius, body.Height / 2, body.Radius);
        return new(scene.PlayerEntity.Value, player.Position - half, player.Position + half, 1, uint.MaxValue, true, false, false);
    }
    private bool ClearLine(Vector3 start, Vector3 end) => !engine.Spatial.CastSegment(new(scene.Session, start, end,
        new(0, uint.MaxValue), ReadOnlyMemory<SpatialEntityCollider>.Empty, ReadOnlyMemory<ulong>.Empty,
        ReadOnlyMemory<SpatialEntityCollider>.Empty)).Present;

    internal ResidentState[] Capture() => Enemies.Select(e => new ResidentState(e.Id,
        e.Health.ValueInt, e.Position.X, e.Position.Y, e.Position.Z, e.Yaw)).ToArray();

    internal void Validate(string weapon, ResidentState[] residents)
    {
        if (!definition.Weapons.Any(w => w.Id == weapon) || residents is null || residents.Length != Enemies.Length ||
            residents.Select(e => e?.Id).Distinct().Count() != Enemies.Length)
            throw new InvalidOperationException("Checkpoint weapon or resident roster is invalid.");
        foreach (ResidentState? state in residents)
        {
            HotelEnemy? enemy = Enemies.FirstOrDefault(e => e.Id == state?.Id);
            if (state is null || enemy is null || state.Health < 0 || state.Health > enemy.Kind.Health ||
                !float.IsFinite(state.X) || !float.IsFinite(state.Y) || !float.IsFinite(state.Z) || !float.IsFinite(state.Yaw) ||
                Vector3.Distance(new(state.X, state.Y, state.Z), enemy.Spawn) > enemy.Kind.Leash + 1)
                throw new InvalidOperationException("Checkpoint resident values are invalid.");
        }
    }

    internal void Restore(string weapon, ResidentState[] residents)
    {
        Reset();
        weaponIndex = Array.FindIndex(definition.Weapons, w => w.Id == weapon);
        foreach (ResidentState state in residents)
        {
            HotelEnemy enemy = Enemies.Single(e => e.Id == state.Id);
            enemy.Health.SetCurrent(state.Health);
            enemy.Yaw = state.Yaw;
            enemy.Phase = enemy.Alive ? AttackPhase.Ready : AttackPhase.Defeated;
            scene.Entities.Set(enemy.Entity, EngineComponentTypes.Transform,
                new(new(state.X, state.Y, state.Z), Quaternion.Identity, Vector3.One));
        }
    }

    internal void Reset()
    {
        foreach (HotelEnemy enemy in Enemies) enemy.Reset();
        Phase = AttackPhase.Ready; weaponIndex = 0; remaining = noticeRemaining = HurtFlash = HitFlash = 0;
        AcceptedAttacks = LandedHits = 0; Notice = "";
    }
    private void Announce(string message) { Notice = message; noticeRemaining = 2.5f; }
}

internal sealed class HotelEnemy
{
    private readonly HotelScene scene;
    internal HotelEnemy(IEngineContext engine, HotelScene scene, ResidentPlacement placement, ResidentKind kind, float gravity)
    {
        this.scene = scene; Kind = kind; Id = placement.Id; Spawn = Authored.Vector(placement.Position);
        Entity = scene.Entities.Create();
        Health = new(kind.Health);
        CharacterControllerConfig baseline = engine.Spatial.DefaultCharacterControllerConfig();
        Controller = baseline with
        {
            Shape = baseline.Shape with { StandingHeight = kind.Height, Radius = kind.Radius },
            Ground = baseline.Ground with { ForwardSpeed = kind.Speed, BackwardSpeed = kind.Speed, StrafeSpeed = kind.Speed },
            Vertical = baseline.Vertical with { Gravity = gravity }
        };
        engine.Spatial.ValidateCharacterControllerConfig(Controller);
        Reset();
    }
    /// <summary>Saved identity of this placed resident.</summary>
    internal string Id { get; }
    internal ResidentKind Kind { get; }
    internal EntityId Entity { get; }
    internal Track Health { get; }
    internal CharacterControllerConfig Controller { get; }
    internal Vector3 Spawn { get; }
    internal Vector3 Position => scene.Entities.Get(Entity, EngineComponentTypes.Transform).Translation;
    internal CharacterMotion Motion => scene.Entities.Get(Entity, EngineComponentTypes.CharacterMotion);
    internal Vector3 Eye => Position + new Vector3(0, Kind.Height * .32f, 0);
    internal bool Alive => Health.Value > 0;
    internal AttackPhase Phase;
    internal float Remaining, Yaw, BeamTime;
    internal Vector3 Aim, BeamEnd;
    internal ulong Sequence;
    private Vector3 Half => new(Kind.Radius, Kind.Height / 2, Kind.Radius);
    internal SpatialEntityCollider Hitbox => new(Entity.Value, Position - Half, Position + Half, 2, uint.MaxValue, true, false, false);
    internal CharacterObstacle Obstacle => new(Entity.Value, new(Position, Quaternion.Identity, Vector3.One), -Half, Half, true, default, default);
    internal void Reset()
    {
        Health.SetCurrent(Kind.Health); Phase = AttackPhase.Ready; Remaining = BeamTime = Yaw = 0;
        scene.Entities.Set(Entity, EngineComponentTypes.Transform, new(Spawn, Quaternion.Identity, Vector3.One));
        scene.Entities.Set(Entity, EngineComponentTypes.CharacterMotion, new(Vector3.Zero, Vector3.Zero,
            false, CharacterStance.Standing, 0, 0, 0, false, 0, Vector3.Zero, Vector3.Zero, Quaternion.Identity,
            Vector3.Zero, Spawn.Y, Spawn.Y, 0, 0));
    }
}
