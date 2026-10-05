using Rusty.Engine.Debugging;

namespace Hotel.Game.Interface;

internal sealed class HotelDebugCommands(HotelDeveloper developer) : IDebugCommandModule
{
    [DebugCommand("hotel.dev.floor.module", Description = "Developer override: build one room module alone beside the hotel, at a quarter turn (0-3), and stand at its first doorway. hotel.dev.goto returns.")]
    public DebugCommandResult ShowModule(string id, int turn) => developer.ShowModule(id, turn);

    [DebugCommand("hotel.dev.floor", Description = "Developer override: begin a run from a seed and stand on its generated floor at a depth (1 and up), arriving by its stairs. Carries the current supplies.")]
    public DebugCommandResult Floor(ulong seed, int depth) => developer.Floor(seed, depth);

    [DebugCommand("hotel.floor.inspect", Description = "Read the run seed, current depth and the current generated floor's identity, places, sizes and generation refusals.")]
    public DebugCommandResult InspectFloor() => developer.Inspect();

    [DebugCommand("hotel.dev.goto", Description = "Developer override: stand at the centre of a named floor-plan space (for inspecting the built floor).")]
    public DebugCommandResult GoTo(string space) => developer.GoTo(space);

    [DebugCommand("hotel.inspect", Description = "Read the current Hotel player position, look, route, focus and reading state.")]
    public DebugCommandResult Inspect() => developer.Observe();

    [DebugCommand("hotel.dev.return-to-entrance", Description = "Developer override: return to the refuge's floor, reset the route to the authored entrance and clear held input.")]
    public DebugCommandResult ReturnToEntrance()
    {
        developer.ReturnToEntrance();
        return DebugCommandResult.Success("Route reset. Returned to the entrance. Held input cleared.");
    }
}
