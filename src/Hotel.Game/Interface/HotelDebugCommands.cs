using Rusty.Engine.Debugging;

namespace Hotel.Game.Interface;

public sealed class HotelDebugCommands(Func<DebugCommandResult> inspect, Action returnToEntrance) : IDebugCommandModule
{
    [DebugCommand("hotel.inspect", Description = "Read the current Hotel player position, look, route, focus and reading state.")]
    public DebugCommandResult Inspect() => inspect();

    [DebugCommand("hotel.dev.return-to-entrance", Description = "Developer override: reset the route, return to the authored entrance and clear held input.")]
    public DebugCommandResult ReturnToEntrance()
    {
        returnToEntrance();
        return DebugCommandResult.Success("Route reset. Returned to the entrance. Held input cleared.");
    }
}
