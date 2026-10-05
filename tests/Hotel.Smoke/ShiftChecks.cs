using Hotel.Game;
using Hotel.Game.Floors;
using Hotel.Game.Floors.Layout;
using Hotel.Game.Floors.Mission;
using Rusty.Engine;

// Floors shift between visits around what they keep: K successive shifts of each floor keep its stair core, its
// landmark and, once unlatched, its shortcut passage exactly where they were, re-roll the rest, and pass every check
// (mission graph, layout, content pacing and Engine confirmation, since every shift is generated in full).
internal static class ShiftChecks
{
    internal static void Run(IEngineContext engine)
    {
        void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
        var content = Owners.Content(engine);
        HotelFloors floors = new(engine, content);
        const int Shifts = 4;
        int floorsShifted = 0, shifts = 0, failed = 0, doorsKept = 0;
        string Placed(LayoutPlacement p) => $"{p.Id} {p.Module} {p.X} {p.Z} {p.Turn}";
        foreach (ulong run in new ulong[] { 11, 12, 13 })
        {
            floors.Begin(run);
            if (floors.Floor(1) is not { } first) continue;
            GeneratedFloor original = floors.Stored(1)!.Value.Floor;
            // The player unlatched the shortcut and opened a locked door on the first visit.
            string? opened = original.Layout.Locks.Select(l => KeptSet.LockDoor(first.Id, l)).FirstOrDefault();
            string[] open = [$"{first.Id}/latch", .. opened is null ? Array.Empty<string>() : [opened]];
            floors.Remember(first.Id, new(open, null, []));
            KeptSet kept = KeptSet.From(original, first.Id, open);
            if (opened is not null) doorsKept++;
            HashSet<string> previous = [original.Identity.PlanHash];
            floorsShifted++;
            for (int k = 1; k <= Shifts; k++)
            {
                // Back at the refuge: every visited floor is due to shift on the next visit.
                FloorsState state = floors.Capture();
                floors.Restore(state with { Floors = [.. state.Floors.Select(f => f with { ShiftDue = true })] });
                floors.Floor(1);
                GeneratedFloor shifted = floors.Stored(1)!.Value.Floor;
                if (shifted.Identity.Seed.Shift != original.Identity.Seed.Shift + k) { failed++; break; }
                shifts++;
                Check(previous.Add(shifted.Identity.PlanHash), $"run {run} shift {k} re-rolls the floor");
                foreach (LayoutPlacement p in kept.Placements)
                    Check(shifted.Layout.Placements.Any(q => Placed(q) == Placed(p)), $"run {run} shift {k} keeps {p.Id} ({p.Module}) where it was");
                Check(shifted.Layout.Places[MissionGraph.ArrivalId] == kept.Places[MissionNodeKind.Arrival], $"run {run} shift {k} keeps the stair core as the arrival");
                if (kept.Places.TryGetValue(MissionNodeKind.Landmark, out string? landmark))
                    Check(shifted.Layout.Places.Any(p => p.Value == landmark && shifted.Graph.Nodes.Any(n => n.Id == p.Key && n.Kind == MissionNodeKind.Landmark)),
                        $"run {run} shift {k} keeps its landmark");
                if (kept.Latch is not null)
                    Check(shifted.Layout.Latch == kept.Latch && shifted.Layout.Passage.Select(s => s.Id).SequenceEqual(kept.Passage.Select(s => s.Id)) &&
                        floors.Memory(first.Id)?.OpenDoors.Contains($"{first.Id}/latch") == true, $"run {run} shift {k} keeps its unlatched shortcut");
                Check(shifted.Confirmation is { Confirmed: true }, $"run {run} shift {k} is Engine-confirmed");
                string? keptDoor = opened is null ? null : kept.Doors.Single().Id;
                if (keptDoor is not null)
                    Check(shifted.Layout.KeptDoors.Any(d => d.Id == keptDoor) && floors.Memory(first.Id)?.OpenDoors.Contains(keptDoor) == true &&
                        floors.Stored(1)!.Value.Excursion.Route.Doors.Count(d => d.Id == keptDoor && d.Key is null) == 1,
                        $"run {run} shift {k} keeps the door the player opened, open and hung again");
                Check(shifted.Layout.Placements.Count(p => !kept.Placements.Any(q => q.Id == p.Id)) > 0, $"run {run} shift {k} has re-rolled rooms");
            }
        }
        Check(floorsShifted == 3 && failed == 0 && shifts == 3 * Shifts && doorsKept > 0, $"every floor shifts {Shifts} times; {shifts} shifts, {failed} failed, {doorsKept} opened doors");
        Console.WriteLine($"Shift checks passed: {floorsShifted} floors shifted {Shifts} times each, keeping their stair core, landmark, opened doors and unlatched shortcut in place, re-rolling the rest, every shift Engine-confirmed.");
    }
}
