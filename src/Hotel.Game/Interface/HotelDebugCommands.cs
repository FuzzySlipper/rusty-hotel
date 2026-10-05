using Rusty.Engine.Debugging;

namespace Hotel.Game.Interface;

public sealed class HotelDebugCommands(Func<DebugCommandResult> inspect, Action returnToEntrance,
    Func<string, DebugCommandResult> goTo, Func<string, int, DebugCommandResult> showModule) : IDebugCommandModule
{
    [DebugCommand("hotel.dev.floor.module", Description = "Developer override: build one room module alone beside the hotel, at a quarter turn (0-3), and stand at its first doorway. hotel.dev.goto returns.")]
    public DebugCommandResult ShowModule(string id, int turn) => showModule(id, turn);

    [DebugCommand("hotel.dev.goto", Description = "Developer override: stand at the centre of a named floor-plan space (for inspecting the built floor).")]
    public DebugCommandResult GoTo(string space) => goTo(space);

    [DebugCommand("hotel.inspect", Description = "Read the current Hotel player position, look, route, focus and reading state.")]
    public DebugCommandResult Inspect() => inspect();

    [DebugCommand("hotel.dev.return-to-entrance", Description = "Developer override: reset the route, return to the authored entrance and clear held input.")]
    public DebugCommandResult ReturnToEntrance()
    {
        returnToEntrance();
        return DebugCommandResult.Success("Route reset. Returned to the entrance. Held input cleared.");
    }
}
