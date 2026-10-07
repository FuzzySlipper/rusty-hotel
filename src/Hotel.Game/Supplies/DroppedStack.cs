namespace Hotel.Game.Supplies;

/// <summary>
/// A stack the investigator left on a floor: what it is (with a generated item's resolved roll) and where it lies, its
/// feet on the floor. Kept with the floor like its opened doors, and taken back whole with the use key.
/// </summary>
/// <param name="Id">Unique on its floor ("dropped/1", "dropped/2"…).</param>
internal sealed record DroppedStack(string Id, ItemStack Stack, float X, float Y, float Z, float Yaw);
