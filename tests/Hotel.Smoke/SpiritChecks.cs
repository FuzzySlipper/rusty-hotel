using System.Numerics;
using System.Text;
using Hotel.Game.Combat;
using Hotel.Game.Player;
using Hotel.Game.Route;
using Hotel.Game.Scene;
using Hotel.Game.Spirits;
using Hotel.Game.Supplies;
using Rusty.Engine;
using Rusty.Engine.Entities;

internal static class SpiritChecks
{
    internal static void Run(IEngineContext engine)
    {
        using HotelScene scene = new(engine, HotelDefinition.Load(engine));
        using HotelPlayer player = new(engine, scene);
        HotelSupplies supplies = new(scene.Definition.Supplies, 12, scene.PlayerEntity);
        HotelCombat combat = new(engine, scene, player, supplies);
        HotelSpirit spirit = new(scene.Definition.Spirit, supplies, combat, player);
        HotelRoute route = new(engine, scene, player, supplies, spirit);
        using SpiritView view = new(engine, scene, spirit);
        HotelEnemy lamp = combat.Enemies.Single(e => e.Definition.Id == "lamp");
        void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
        void At(float x, float z, Vector3 target)
        {
            player.Reset();
            scene.Entities.Set(scene.PlayerEntity, EngineComponentTypes.Transform,
                new Transform(new(x, .875f, z), Quaternion.Identity, Vector3.One));
            Vector3 delta = target - player.Eye;
            player.LookBy(Math.Atan2(delta.X, -delta.Z) * 180 / Math.PI,
                Math.Atan2(delta.Y, Math.Sqrt(delta.X * delta.X + delta.Z * delta.Z)) * 180 / Math.PI);
        }
        void Step(int count) { for (int i = 0; i < count; i++) { combat.Step(1f / 60); spirit.Step(1f / 60); } }
        Check(!spirit.Call() && !spirit.Equip(true, spirit.Revision) && supplies.Summon == 0, "unacquired spirit refuses call/equip");
        At(0, -8, HotelDefinition.Vector(spirit.Definition.Point));
        route.Update(); route.Use();
        Check(!spirit.Acquired, "wall and reach prevent remote pact acquisition");
        At(3.3f, -8, HotelDefinition.Vector(spirit.Definition.Point));
        route.Update();
        Check(route.Prompt.Contains("Hushwing"), "ordinary Engine focus exposes the pact");
        route.Use();
        Check(spirit.Acquired && !spirit.Equipped && supplies.Summon == 2, "ordinary focused use acquires once and grants two charges, without auto-equipping");
        Check(!spirit.Acquire() && supplies.Summon == 2, "duplicate acquisition cannot farm charges");
        Check(!spirit.Call() && supplies.Summon == 2, "unequipped call refuses without cost");
        ulong revision = spirit.Revision;
        spirit.HandleIntents([Claim($"{{\"equipped\":true,\"revision\":{revision}}}")]);
        Check(spirit.Equipped, "semantic field-case claim equips using the domain rule");
        spirit.HandleIntents([Claim($"{{\"equipped\":false,\"revision\":{revision}}}")]);
        Check(spirit.Equipped, "stale field-case action refuses");
        spirit.HandleIntents([Claim("{\"equipped\":true,\"revision\":\"bad\"}"), Claim("{"), Claim("[]")]);
        Check(spirit.Equipped, "malformed claims cannot fault or mutate equipped state");
        At(0, -13, lamp.Eye);
        Check(!spirit.Call() && supplies.Summon == 2, "closed door rejects spirit targeting without cost");
        scene.PlaceDoor("survey", true);
        At(1.55f, -14.8f, lamp.Eye); Step(1);
        Check(lamp.Phase == AttackPhase.Windup, "lamp starts a real attack tell");
        Check(spirit.Call() && supplies.Summon == 1 && lamp.Phase == AttackPhase.Interrupted, "accepted call spends once and cancels windup");
        Check(!spirit.Call() && !spirit.Equip(false, spirit.Revision) && supplies.Summon == 1, "active manifestation rejects duplicate call and unequip without cost");
        view.Publish(); Step(60); view.Publish();
        Check(spirit.Phase == ManifestationPhase.Hushing && supplies.Health == 70, "creature arrives and holds interruption across original attack deadline");
        Step(80); view.Publish();
        Check(spirit.Phase == ManifestationPhase.Departing, "creature has a departure phase");
        Step(45); view.Publish();
        Check(!spirit.Active && supplies.Health == 70, "manifestation ends without replaying the cancelled hit");
        Check(spirit.Call() && supplies.Summon == 0 && spirit.Calls == 2, "second accepted use consumes final charge exactly once");
        Step(190);
        Check(!spirit.Call() && supplies.Summon == 0 && spirit.Message.Contains("No summon"), "empty reserve refuses intelligibly");
        supplies.RestoreSummon(1);
        Check(spirit.Call(), "resource restoration permits another ordinary call");
        supplies.Damage(1000); spirit.Step(1f / 60);
        Check(!spirit.Active && !spirit.Call() && supplies.Summon == 0, "defeat ends manifestation and refuses further summons");
        spirit.Reset(); combat.Reset(); supplies.Reset(); view.Publish();
        Check(!spirit.Acquired && !spirit.Equipped && !spirit.Active && spirit.Calls == 0, "excursion reset clears pact and transient presence coherently");
        Console.WriteLine("Spirit checks passed: world acquisition, equip claims, stale/malformed refusal, occlusion, once-only cost, interruption, creature phases, exhaustion and defeat.");
    }
    internal static ProductInputEvent Claim(string json) => default(ProductInputEvent) with
    {
        Kind = InputEventKind.DirectProductPayload, ValueKind = InputValueKind.ProductPayload,
        Intent = Encoding.UTF8.GetBytes("hotel.spirit.equip"), PayloadContract = Encoding.UTF8.GetBytes("hotel.spirit.equip.v1"),
        PayloadData = Encoding.UTF8.GetBytes(json)
    };
}
