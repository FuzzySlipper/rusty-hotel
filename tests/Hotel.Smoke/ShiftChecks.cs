using Hotel.Game;
using Hotel.Game.Floors;
using Hotel.Game.Floors.Layout;
using Hotel.Game.Floors.Mission;
using Rusty.Engine;

// Floors shift between visits around what they keep: K successive shifts of each floor keep its stair core, its
// landmark, the door the player opened or still holds the key to and, once unlatched, its shortcut passage exactly where
// they were, re-roll the rest, and pass every check
// (mission graph, layout, content pacing and Engine confirmation, since every shift is generated in full).
internal static class ShiftChecks
{
    internal static void Run(IEngineContext engine)
    {
        void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
        var content = Owners.Content(engine);
        HotelFloors floors = new(engine, content);
        const int Shifts = 4;
        ulong[] runs = [11, 12, 13, 14, 15, 16, 17, 18, 31];
        int floorsShifted = 0, shifts = 0, failed = 0, doorsKept = 0, keysKept = 0;
        string Placed(LayoutPlacement p) => $"{p.Id} {p.Module} {p.X} {p.Z} {p.Turn}";
        foreach (ulong run in runs)
        {
            floors.Begin(run);
            if (floors.Floor(1) is not { } first) continue;
            GeneratedFloor original = floors.Stored(1)!.Value.Floor;
            // The player unlatched the shortcut on the first visit and, run by run, took a lock's key and left its door
            // shut, opened the door with the key, or found the door open without holding its key.
            LayoutLock? lck = original.Layout.Locks.FirstOrDefault();
            string? opened = lck is null || run % 3 == 0 ? null : KeptSet.LockDoor(first.Id, lck);
            string[] held = lck is not null && run % 3 != 2 ? [lck.Item] : [];
            string[] open = [$"{first.Id}/latch", .. opened is null ? Array.Empty<string>() : [opened]];
            floors.Remember(first.Id, new(open, null, held));
            KeptSet kept = KeptSet.From(original, first.Id, open, held);
            if (opened is not null) doorsKept++;
            if (held.Length > 0) keysKept++;
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
                Check(shifted.Layout.TrimStyle == original.Layout.TrimStyle, $"run {run} shift {k} keeps the floor's trim style");
                if (held.Length > 0)
                {
                    KeptDoor locked = kept.Doors.Single();
                    Check(locked.Locked is { } still && shifted.Layout.KeptDoors.Contains(locked) && floors.Memory(first.Id)?.Keys.SequenceEqual([still.Item]) == true &&
                        shifted.Layout.Locks.All(l => l.Item != still.Item) && floors.Stored(1)!.Value.Excursion.Route.Doors.Count(d => d.Id == locked.Id && d.Key == still.Item) == 1 &&
                        floors.Memory(first.Id)!.OpenDoors.Contains(locked.Id) == (opened is not null),
                        $"run {run} shift {k} keeps the door whose key the player holds, {(opened is null ? "shut" : "open")} and locked to that key alone");
                }
                string? keptDoor = opened is null ? null : kept.Doors.Single().Id;
                if (keptDoor is not null)
                    Check(shifted.Layout.KeptDoors.Any(d => d.Id == keptDoor) && floors.Memory(first.Id)?.OpenDoors.Contains(keptDoor) == true &&
                        floors.Stored(1)!.Value.Excursion.Route.Doors.Count(d => d.Id == keptDoor && (d.Key is null) == (held.Length == 0)) == 1,
                        $"run {run} shift {k} keeps the door the player opened, open and hung again");
                Check(shifted.Layout.Placements.Count(p => !kept.Placements.Any(q => q.Id == p.Id)) > 0, $"run {run} shift {k} has re-rolled rooms");
            }
        }
        Check(floorsShifted == runs.Length && failed == 0 && shifts == runs.Length * Shifts && doorsKept > 0 && keysKept > 0,
            $"every floor shifts {Shifts} times; {shifts} shifts, {failed} failed, {doorsKept} opened doors, {keysKept} held keys");
        Console.WriteLine($"Shift checks passed: {floorsShifted} floors shifted {Shifts} times each, keeping their stair core, landmark, {doorsKept} opened doors, {keysKept} doors locked to a held key (open or shut) and the unlatched shortcut in place, re-rolling the rest, every shift Engine-confirmed.");
    }
}
