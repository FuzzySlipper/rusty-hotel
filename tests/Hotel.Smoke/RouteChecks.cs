using System.Numerics;
using Hotel.Game.Combat;
using Hotel.Game.Player;
using Hotel.Game.Route;
using Hotel.Game.Scene;
using Hotel.Game.Spirits;
using Hotel.Game.Supplies;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Interaction;

internal static class RouteChecks
{
    internal static void Run(IEngineContext engine)
    {
        var content = Owners.Content(engine);
        using HotelScene scene = Owners.Scene(engine, content);
        using HotelPlayer player = Owners.Player(engine, scene, content);
        HotelSupplies supplies = Owners.Supplies(content, scene.PlayerEntity);
        HotelCombat combat = Owners.Combat(engine, scene, player, supplies, content);
        HotelSpirit spirit = Owners.Spirit(content, supplies, combat, player);
        HotelRoute route = Owners.Route(engine, scene, player, supplies, spirit, content);
        scene.Publish();
        void At(float x, float z, Vector3 target)
        {
            player.Reset();
            scene.Entities.Set(scene.PlayerEntity, EngineComponentTypes.Transform,
                new Transform(new(x, .875f, z), Quaternion.Identity, Vector3.One));
            Vector3 delta = target - player.Eye;
            player.LookBy(Math.Atan2(delta.X, -delta.Z) * 180 / Math.PI,
                Math.Atan2(delta.Y, Math.Sqrt(delta.X * delta.X + delta.Z * delta.Z)) * 180 / Math.PI);
            route.Update();
        }
        InteractionCandidate Candidate(string label) => route.ReadInteraction().Candidates.ToArray().Single(c => c.Label == label);
        void Check(bool value, string why) { if (!value) throw new InvalidOperationException(why); }

        At(0, -10, new(0, 1.3f, -13.94f));
        Check(route.Prompt.Contains("Move closer"), "door reports out of reach");
        Check(!route.Interaction.UseFocused().Performed, "out-of-reach use does not open");
        At(0, -13, new(-1.6f, .97f, -16.64f));
        Check(Candidate("Read field log").Visibility == InteractionVisibility.Occluded,
            "closed survey door occludes field log");
        At(0, -12.5f, new(0, 1.3f, -13.94f));
        InteractionTarget staleDoor = Candidate("Open survey room").Target;
        Check(route.Interaction.UseFocused().Performed && route.OpenDoors.Contains("survey"), "focused survey door opens");
        Check(!route.Interaction.UseTarget(staleDoor).Performed, "stale door target cannot repeat use");
        At(-2.8f, -13, new(-1.6f, .97f, -16.64f));
        InteractionCandidate hiddenLog = Candidate("Read field log");
        Check(hiddenLog.Visibility == InteractionVisibility.Occluded &&
            !route.Interaction.UseTarget(hiddenLog.Target).Performed, "solid wall blocks even targeted use with the door open");
        player.Reset();
        scene.Entities.Set(scene.PlayerEntity, EngineComponentTypes.Transform,
            new Transform(new(0, .875f, -12.5f), Quaternion.Identity, Vector3.One));
        var input = player.ReadInput([default(ProductInputEvent) with
            { Kind = InputEventKind.Key, Edge = InputEdge.Pressed, Keyboard = KeyboardControl.KeyW, X = 1 }], 1);
        for (int i = 0; i < 100; i++) player.Step(input, 1f / 60);
        Check(player.Position.Z < -15, "opened door collision allows walking into survey room");

        At(-1.6f, -15.4f, new(-1.6f, .97f, -16.64f));
        Check(route.Prompt == "E · Read field log", "log has ordinary focused prompt");
        InteractionTarget staleNote = Candidate("Read field log").Target;
        Check(route.Interaction.UseFocused().Performed, "field log can be read");
        Check(route.ReadingSequence == 1 && route.ReadingTitle.StartsWith("Field log"), "reading facts are published by route owner");
        Check(!route.Interaction.UseTarget(staleNote).Performed && route.ReadingSequence == 1, "stale reading target cannot replay");
        At(7, 3, new(5.59f, 1.09f, 3));
        InteractionTarget reel = Candidate("Take Survey reel").Target;
        Check(route.Interaction.UseFocused().Performed && supplies.Collected("survey-reel") && supplies.Occupied == 1,
            "expedition find moves into inventory through ordinary use");
        Check(!route.Interaction.UseTarget(reel).Performed && supplies.Occupied == 1, "stale pickup cannot duplicate find");
        At(0, 3, new(0, 1.6f, -1));
        At(7, 3, new(5.59f, 1.09f, 3));
        Check(!route.ReadInteraction().Candidates.ToArray().Any(c => c.Label == "Take Survey reel"), "leaving and returning cannot respawn a collected find");

        At(2.25f, 3.6f, new(2.25f, 1.3f, 5.1f));
        Check(route.Prompt.Contains("Latched from the service side"), "refuge side reports lock");
        Check(!route.Interaction.UseFocused().Performed, "refuge cannot unlatch shortcut");
        At(2.25f, 6.2f, new(2.25f, 1.3f, 5.1f));
        Check(route.Interaction.UseFocused().Performed && route.OpenDoors.Contains("return"), "service side unlatches shortcut");
        player.ClearInput();
        var returnInput = player.ReadInput([default(ProductInputEvent) with
            { Kind = InputEventKind.Key, Edge = InputEdge.Pressed, Keyboard = KeyboardControl.KeyW, X = 1 }], 1);
        for (int i = 0; i < 75; i++) player.Step(returnInput, 1f / 60);
        Check(player.Position.Z < 4.5f && route.Location == "Refuge", "opened shortcut collision permits return to refuge");
        route.Reset();
        Check(route.OpenDoors.Length == 0 && route.ReadingTitle == "", "restart closes route and clears reading");
        Console.WriteLine("Route checks passed: reach, occlusion, ordinary focus, stale targets, open collision, readings, far-side latch and reset.");
    }
}
