using System.Numerics;
using System.Text;
using Hotel.Game.Combat;
using Hotel.Game.Content;
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
        var content = Owners.Content(engine);
        using HotelScene scene = Owners.Scene(engine, content);
        using HotelPlayer player = Owners.Player(engine, scene, content);
        HotelSupplies supplies = Owners.Supplies(content, scene.PlayerEntity, 12);
        HotelCombat combat = Owners.Combat(engine, scene, player, supplies, content);
        HotelSpirit spirit = Owners.Spirit(content, supplies, combat, player);
        HotelRoute route = Owners.Route(engine, scene, player, supplies, spirit, content);
        using SpiritView view = new(engine, scene, spirit);
        HotelEnemy lamp = combat.Enemies.Single(e => e.Id == "lamp");
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
        At(0, -8, spirit.Bell);
        route.Update(); route.Use();
        Check(!spirit.Acquired, "wall and reach prevent remote pact acquisition");
        At(3.3f, -8, spirit.Bell);
        route.Update();
        Check(route.Prompt == Template.Fill(content.Route.Text.Ready, ("label", spirit.BellLabel)) && spirit.BellLabel.Contains(spirit.Definition.Name),
            "ordinary Engine focus exposes the pact by the spirit's authored name");
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
        Check(spirit.Call() && supplies.Summon == 1 && combat.PactUser.Current?.Id == spirit.Definition.Action, "an accepted call is the pact's action, spent once");
        Check(!spirit.Call() && !spirit.Equip(false, spirit.Revision) && supplies.Summon == 1, "a call under way rejects another call and unequip");
        Step(1);
        Check(lamp.Phase == AttackPhase.Interrupted && lamp.Stats.Effects.Held && spirit.Phase == ManifestationPhase.Arriving,
            "the call lands its hold through the one resolution, cancelling the windup, and the creature arrives");
        Check(!spirit.Call() && !spirit.Equip(false, spirit.Revision) && supplies.Summon == 1, "active manifestation rejects duplicate call and unequip without cost");
        view.Publish(); Step(60); view.Publish();
        Check(spirit.Phase == ManifestationPhase.Holding && supplies.Health == 70, "creature arrives and holds interruption across original attack deadline");
        Step(80); view.Publish();
        Check(spirit.Phase == ManifestationPhase.Departing, "creature has a departure phase");
        Step(45); view.Publish();
        Check(!spirit.Active && supplies.Health == 70, "manifestation ends without replaying the cancelled hit");
        Check(spirit.Call() && supplies.Summon == 0 && spirit.Calls == 2, "second accepted use consumes final charge exactly once");
        Step(190);
        Check(!spirit.Call() && supplies.Summon == 0 && spirit.Message == Template.Fill(content.SpiritText.NoCharge, ("spirit", spirit.Definition.Name)), "empty reserve refuses intelligibly");
        supplies.RestoreSummon(1);
        Check(spirit.Call(), "resource restoration permits another ordinary call");
        supplies.Damage(new(1000, "blunt")); Step(1);
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
