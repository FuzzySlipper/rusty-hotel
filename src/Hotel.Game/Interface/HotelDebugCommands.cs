using Rusty.Engine.Debugging;

namespace Hotel.Game.Interface;

public sealed class HotelDebugCommands(Func<DebugCommandResult> inspect, Action returnToEntrance,
    Func<string, DebugCommandResult> goTo) : IDebugCommandModule
{
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
