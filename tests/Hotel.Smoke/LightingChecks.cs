using System.Numerics;
using Rusty.Engine;

// The shadow budget goes to the lamps of the eye's own room before nearer lamps through a wall, never exceeds its
// size, and holds its choice against small moves.
internal static class LightingChecks
{
    internal static void Run(IEngineContext engine)
    {
        void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
        var content = Owners.Content(engine);
        using var scene = Owners.Scene(engine, content);
        var rooms = content.Excursion.Route.Rooms;
        var focus = content.Look.Shadows;
        bool In(Vector3 p, string room) => rooms.First(r => r.Id == room) is var r && p.X >= r.Min[0] && p.X <= r.Max[0] && p.Z >= r.Min[2] && p.Z <= r.Max[2];

        // Standing in the linen room just inside its door, beside the corridor's lamps on the other side of the wall.
        Vector3 eye = new(2.3f, 1.6f, -6.4f);
        scene.CastShadowsNear(eye, rooms, focus);
        Vector3[] casting = scene.CastingLights;
        Check(casting.Length == focus.Budget, $"the budget is spent in full: {casting.Length} of {focus.Budget}");
        int inRoom = content.Excursion.Geometry.Lighting.Points.Count(p => p.Shadow && In(new(p.Position[0], p.Position[1], p.Position[2]), "east"));
        Check(casting.Count(p => In(p, "east")) == Math.Min(inRoom, focus.Budget), "the room's own lamps cast before nearer lamps through the wall");
        // A small step keeps the same lamps.
        scene.CastShadowsNear(eye + new Vector3(0.3f, 0, -0.3f), rooms, focus);
        Check(scene.CastingLights.OrderBy(p => p.X).ThenBy(p => p.Z).SequenceEqual(casting.OrderBy(p => p.X).ThenBy(p => p.Z)), "a small step does not swap casting lamps");
        Console.WriteLine($"Lighting checks passed: a {focus.Budget}-lamp shadow budget goes to the eye's own room first ({inRoom} lamps in the linen room) and holds against small moves.");
    }
}
