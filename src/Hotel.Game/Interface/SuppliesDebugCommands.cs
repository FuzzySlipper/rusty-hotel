using Hotel.Game.Supplies;
using Rusty.Engine.Debugging;

namespace Hotel.Game.Interface;

/// <param name="current">The current floor's supplies owner; the product replaces it when the player changes floor.</param>
internal sealed class SuppliesDebugCommands(Func<HotelSupplies> current, Action publish) : IDebugCommandModule
{
    private HotelSupplies supplies => current();

    [DebugCommand("hotel.dev.give-supply", Description = "Developer fixture: add carried supplies through inventory capacity rules.")]
    public DebugCommandResult Give(string item, int count) => Result(supplies.Give(item, count));

    [DebugCommand("hotel.dev.set-health", Description = "Developer fixture: set health within its authored bounds.")]
    public DebugCommandResult Health(int value) => Result(supplies.SetHealth(value));

    [DebugCommand("hotel.dev.use-supply", Description = "Developer fixture: consume a zero-based pocket through ordinary resource eligibility. Requires current inventory revision.")]
    public DebugCommandResult Use(int slot, ulong revision) => Result(supplies.Use(slot, revision));

    [DebugCommand("hotel.dev.apply-effect", Description = "Developer fixture: apply an effect from the effect catalog to the investigator, as from a named source.")]
    public DebugCommandResult Effect(string effect, string source)
    {
        if (supplies.Stats.Mechanics.Effect(effect) is null)
            return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, $"Unknown effect '{effect}'.");
        bool applied = supplies.Afflict(effect, $"developer.{source}");
        publish();
        return applied ? DebugCommandResult.Success($"Applied {effect}.")
            : DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, $"{effect} was refused (defeated, or its group is full).");
    }

    [DebugCommand("hotel.dev.effects", Description = "Inspect the investigator's active effects: stacks, seconds left, tick progress and ward.")]
    public DebugCommandResult Effects() => DebugCommandResult.Success(string.Join("; ", supplies.Stats.Effects.Active.Select(e =>
        $"{e.Definition.Id} from {e.Source} ×{e.Stacks} {e.Remaining:F2}s tick {e.SinceTick:F2} ward {e.WardLeft}")) is { Length: > 0 } list
        ? list : "No active effects.");

    private DebugCommandResult Result(bool accepted)
    {
        publish();
        return accepted ? DebugCommandResult.Success(supplies.Message)
            : DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, supplies.Message);
    }
}
