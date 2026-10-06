using System.Numerics;
using Hotel.Game.Combat;
using Hotel.Game.Player;
using Hotel.Game.Scene;
using Hotel.Game.Supplies;
using Rusty.Engine;
using Rusty.Engine.Entities;

internal static class CombatChecks
{
    internal static void Run(IEngineContext engine)
    {
        var content = Owners.Content(engine);
        using HotelScene scene = Owners.Scene(engine, content);
        using HotelPlayer player = Owners.Player(engine, scene, content);
        HotelSupplies supplies = Owners.Supplies(content, scene.PlayerEntity, 12);
        HotelCombat combat = Owners.Combat(engine, scene, player, supplies, content);
        HotelEnemy porter = combat.Enemies.Single(e => e.Id == "porter");
        HotelEnemy lamp = combat.Enemies.Single(e => e.Id == "lamp");
        void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
        void Steps(int count) { for (int i = 0; i < count; i++) combat.Step(1f / 60); }
        void At(float x, float z, Vector3 target)
        {
            player.Reset();
            scene.Entities.Set(scene.PlayerEntity, EngineComponentTypes.Transform,
                new Transform(new(x, .875f, z), Quaternion.Identity, Vector3.One));
            Vector3 delta = target - player.Eye;
            player.LookBy(Math.Atan2(delta.X, -delta.Z) * 180 / Math.PI,
                Math.Atan2(delta.Y, Math.Sqrt(delta.X * delta.X + delta.Z * delta.Z)) * 180 / Math.PI);
        }
        At(-4.1f, -5.3f, porter.Eye);
        Check(combat.Attack() && !combat.Attack() && !combat.SelectWeapon(1), "committed attack rejects repeat fire and weapon switching");
        Steps(18);
        Check(porter.Health.ValueInt == 66 && combat.Phase == AttackPhase.Windup, "windup cannot deal early damage");
        Steps(3);
        Check(porter.Health.ValueInt == 42 && combat.LandedHits == 1, "Engine hit settles damage once at commitment");
        Steps(70);
        Check(porter.Health.ValueInt == 42, "commit and recovery do not replay hit");
        Check(supplies.Give("rounds", 1) && supplies.Use(supplies.AmmoPocket, supplies.Revision), "ammo fixture uses existing supply rules");
        Check(combat.SelectWeapon(1), "ready weapon can switch");
        At(0, -3.5f, porter.Eye); // Solid west wall is between player and porter.
        Check(combat.Attack() && supplies.Ammo == 5 && !combat.Attack(), "accepted ranged attack spends exactly one cartridge");
        Steps(80);
        Check(porter.Health.ValueInt == 42 && combat.LandedHits == 1 && supplies.Ammo == 5, "wall blocks ranged damage without refunding a committed shot");
        supplies.SpendAmmo(5);
        int accepted = combat.AcceptedAttacks;
        Check(!combat.Attack() && supplies.Ammo == 0 && combat.AcceptedAttacks == accepted, "empty ranged action spends nothing and stays ready");
        Check(combat.SelectWeapon(0), "zero-ammo melee fallback available");
        At(-4.1f, -5.3f, porter.Eye);
        Check(combat.Attack(), "melee costs no cartridge"); Steps(80);
        At(-4.1f, -5.3f, porter.Eye);
        Check(combat.Attack(), "fallback repeat after recovery"); Steps(80);
        Check(!porter.Alive && porter.Health.ValueInt == 0 && supplies.Ammo == 0, "melee can defeat pressure enemy at zero ammo");

        combat.Reset(); supplies.Reset();
        At(0, -13, lamp.Eye); Steps(100);
        Check(lamp.Phase == AttackPhase.Ready && supplies.Health == 70, "closed survey door prevents enemy acquisition and wall damage");
        scene.PlaceDoor("survey", true);
        At(1.55f, -14.7f, lamp.Eye); Steps(1);
        Check(lamp.Phase == AttackPhase.Windup && supplies.Health == 70, "lamp exposes a windup before firing");
        At(.2f, -14.7f, lamp.Eye); Steps(84);
        Check(supplies.Health == 70 && lamp.Phase is AttackPhase.Commit or AttackPhase.Recovery, "sidestep evades locked lamp aim");
        Steps(120);
        combat.Reset(); supplies.Reset();
        At(0, -12.7f, lamp.Eye); Steps(1);
        Check(lamp.Phase == AttackPhase.Windup, "open doorway admits lamp sight");
        scene.PlaceDoor("survey", false); Steps(84);
        Check(supplies.Health == 70, "closing real door blocks already committed enemy shot");
        scene.PlaceDoor("survey", true);
        combat.Reset();
        At(1.55f, -14.7f, lamp.Eye); Steps(84);
        Check(supplies.Health == 54, "uncovered locked shot damages the existing health owner once");
        Steps(8);
        Check(supplies.Health == 54, "enemy commit cannot double-hit");
        Check(supplies.Damage(new(1000, "blunt")) == 54 && supplies.Health == 0 && supplies.Damage(new(2, "blunt")) == 0 && supplies.Damage(new(-5, "blunt")) == 0,
            "damage saturates at zero and refuses invalid/defeated targets");
        Check(!combat.Attack() && !combat.Reload(), "defeat suppresses new combat actions");
        combat.Reset(); supplies.Reset(); player.Reset();
        Check(supplies.Pickup("refuge-rounds") && combat.SelectWeapon(1) && combat.Reload(), "normal pickup supplies ordinary reload");
        Check(!combat.Reload() && !combat.Attack(), "reload commitment rejects duplicate reload and fire");
        Steps(68);
        Check(supplies.Ammo == 6 && supplies.Occupied == 0, "reload consumes the packet once and publishes reserve");
        combat.Reset(); supplies.Reset();
        At(-4.1f, -5.3f, porter.Eye);
        var walk = player.ReadInput([default(ProductInputEvent) with
            { Kind = InputEventKind.Key, Edge = InputEdge.Pressed, Keyboard = KeyboardControl.KeyW, X = 1 }], 1);
        for (int i = 0; i < 100; i++)
        {
            player.Step(walk, 1f / 60, combat.Obstacles);
            combat.Step(1f / 60);
        }
        Check(Vector2.Distance(new(player.Position.X, player.Position.Z), new(porter.Position.X, porter.Position.Z)) >=
            player.Tuning.Radius + porter.Kind.Radius - .03f, "Engine body collision prevents walking through a live resident");
        combat.Reset(); supplies.Reset();
        At(1.55f, -14.7f, lamp.Eye);
        for (int i = 0; i < 3; i++) { Check(combat.Attack(), "melee lamp attack accepted"); Steps(80); }
        Check(!lamp.Alive && supplies.Ammo == 0, "stationary ranged resident is also defeatable with the zero-ammo fallback");
        Console.WriteLine("Combat checks passed: commitment, recovery, wall occlusion, once-only ammo/damage, zero-ammo fallback, lamp tell/dodge/cover, defeat and reload.");
    }
}
