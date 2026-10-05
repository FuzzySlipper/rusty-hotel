using System.Diagnostics;
using Hotel.Game.Floors;
using Hotel.Game.Floors.Modules;
using Rusty.Engine;

// The seed census, off the default path: HOTEL_FLOOR_CENSUS=<seeds per depth> generates that many floors at each of
// four depths and reports success, first tries, generation time, rooms and refusal reasons; HOTEL_FLOOR_SVG=<folder>
// also writes each floor's plan for the floor bank.
internal static class CensusChecks
{
    internal static void Run(IEngineContext engine)
    {
        if (!int.TryParse(Environment.GetEnvironmentVariable("HOTEL_FLOOR_CENSUS"), out int seeds) || seeds <= 0) return;
        var content = Owners.Content(engine);
        HotelFloors floors = new(engine, content);
        for (int depth = 1; depth <= 4; depth++)
        {
            int made = 0, first = 0, rooms = 0;
            double total = 0, slowest = 0;
            Dictionary<string, int> refusals = new(StringComparer.Ordinal);
            for (ulong run = 0; run < (ulong)seeds; run++)
            {
                long started = Stopwatch.GetTimestamp();
                GenerationResult result = FloorGenerator.Generate(engine, FloorSeed.Current(run, depth, 0), floors.Tunings, floors.Sources);
                double ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                total += ms; slowest = Math.Max(slowest, ms);
                foreach (string why in result.Refusals)
                {
                    string[] words = why.Split(' ');
                    string key = words.Length > 2 ? $"{words[1].TrimEnd(':')} {words[2].TrimEnd(':')}" : why;
                    refusals[key] = refusals.GetValueOrDefault(key) + 1;
                }
                if (result.Floor is not { } floor) continue;
                made++;
                if (floor.Candidate == 0 && floor.Attempt == 0) first++;
                rooms += floor.Layout.Placements.Count(p => !content.Modules.Find(p.Module)!.Tags.Any(t => t is ModuleTag.Corridor or ModuleTag.StairCore));
                FloorSvg.Write($"census-d{depth}-r{run}", floor.Layout, floor.Plan);
            }
            Console.WriteLine($"Census depth {depth}: {made}/{seeds} floors ({first} first try), {rooms / Math.Max(1, made)} rooms on average, " +
                $"{total / seeds:0} ms average, {slowest:0} ms slowest; refusals {(refusals.Count == 0 ? "none" : string.Join(", ", refusals.OrderByDescending(r => r.Value).Select(r => $"{r.Key} ×{r.Value}")))}");
        }
    }
}
