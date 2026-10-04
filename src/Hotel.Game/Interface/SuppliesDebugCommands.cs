using Hotel.Game.Supplies;
using Rusty.Engine.Debugging;

namespace Hotel.Game.Interface;

internal sealed class SuppliesDebugCommands(HotelSupplies supplies, Action publish) : IDebugCommandModule
{
    [DebugCommand("hotel.dev.give-supply", Description = "Developer fixture: add carried supplies through inventory capacity rules.")]
    public DebugCommandResult Give(string item, int count) => Result(supplies.Give(item, count));

    [DebugCommand("hotel.dev.set-health", Description = "Developer fixture: set health within its authored bounds.")]
    public DebugCommandResult Health(int value) => Result(supplies.SetHealth(value));

    [DebugCommand("hotel.dev.use-supply", Description = "Developer fixture: consume a zero-based pocket through ordinary resource eligibility. Requires current inventory revision.")]
    public DebugCommandResult Use(int slot, ulong revision) => Result(supplies.Use(slot, revision));

    private DebugCommandResult Result(bool accepted)
    {
        publish();
        return accepted ? DebugCommandResult.Success(supplies.Message)
            : DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, supplies.Message);
    }
}
