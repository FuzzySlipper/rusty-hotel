using Hotel.Game.Floors.Content;
using Hotel.Game.Floors.Layout;
using Hotel.Game.Floors.Mission;

namespace Hotel.Game.Floors;

/// <summary>
/// One visited floor as the checkpoint keeps it: its identity and the compact resolved plan it was generated as
/// (mission graph, layout and content, never boxes), and what the player left it as (doors, residents, keys).
/// It is rebuilt from the plan without drawing; the identity hash proves it is the plan that was generated.
/// </summary>
/// <param name="ShiftDue">The player has been back to the refuge since last on this floor: it shifts when next entered.</param>
internal sealed record FloorRecord(uint Version, int Depth, int Shift, string PlanHash, int Candidate, int Attempt,
    MissionGraph Graph, FloorLayout Layout, FloorContent Content, WorldMemory? Memory, bool ShiftDue = false);

/// <summary>
/// The run's seed, every floor visited in it, the finds collected on those floors, and the expedition finds secured at
/// the refuge from them, kept by item because the rooms they lay in may since have shifted away.
/// </summary>
internal sealed record FloorsState(ulong Run, FloorRecord[] Floors, string[] Collected, SecuredFind[] Secured);

/// <summary>An expedition find from a generated floor, secured at the refuge.</summary>
internal sealed record SecuredFind(string Id, string Item);
