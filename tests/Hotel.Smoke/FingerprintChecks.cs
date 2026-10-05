using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hotel.Game.Floors;
using Rusty.Engine;

// Golden floors: named seeds' plan hashes for the current generator version, and a stamp of the generator's source
// and authored floor content. Generation whose output changes without a FloorSeed.CurrentVersion bump fails, because
// a save made by the old output would then be rebuilt as a different floor. Record new goldens with
// HOTEL_FLOOR_GOLDENS=write after bumping the version.
internal static class FingerprintChecks
{
    private static readonly (ulong Run, int Depth)[] Named = [(101, 1), (202, 2), (303, 3), (404, 1), (505, 2), (606, 4)];

    internal static void Run(IEngineContext engine)
    {
        var content = Owners.Content(engine);
        HotelFloors floors = new(engine, content);
        JsonArray found = [];
        foreach (var (run, depth) in Named)
        {
            GenerationResult result = FloorGenerator.Generate(engine, FloorSeed.Current(run, depth, 0), floors.Tunings, floors.Sources);
            found.Add(new JsonObject { ["run"] = run, ["depth"] = depth, ["plan"] = result.Floor?.Identity.PlanHash ?? "failed" });
        }
        string stamp = SourceStamp();
        string path = Path.Combine(Repository(), "tests", "Hotel.Smoke", "goldens", "floors.json");
        if (Environment.GetEnvironmentVariable("HOTEL_FLOOR_GOLDENS") == "write")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            JsonObject golden = new() { ["version"] = FloorSeed.CurrentVersion, ["sourceStamp"] = stamp, ["floors"] = found };
            File.WriteAllText(path, golden.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
            Console.WriteLine($"Floor goldens written for generator version {FloorSeed.CurrentVersion}: {path}");
            return;
        }
        JsonNode recorded = JsonNode.Parse(File.ReadAllText(path))!;
        Compare(recorded, found, stamp);
        // A focused mismatch: the same floors under another source stamp are refused.
        bool refused = false;
        try { Compare(recorded, found, new string('0', 64)); } catch (InvalidOperationException) { refused = true; }
        if (!refused) throw new InvalidOperationException("A generator source change without a version bump must fail the fingerprints.");
        Console.WriteLine($"Fingerprint checks passed: {found.Count} named floors and the generator source match generator version {FloorSeed.CurrentVersion}'s goldens; a changed source stamp is refused.");
    }

    // Goldens hold for one generator version, one source stamp and the named floors' plans; any difference fails.
    private static void Compare(JsonNode recorded, JsonArray found, string stamp)
    {
        uint version = recorded["version"]!.GetValue<uint>();
        if (version != FloorSeed.CurrentVersion)
            throw new InvalidOperationException($"Floor goldens are for generator version {version}; this is {FloorSeed.CurrentVersion}. Record them with HOTEL_FLOOR_GOLDENS=write.");
        string[] Lines(JsonNode list) => list.AsArray().Select(f => $"{f!["run"]}/{f["depth"]} {f["plan"]}").ToArray();
        string[] expected = Lines(recorded["floors"]!), actual = Lines(found);
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException("Floor generation output changed under the same generator version. Bump FloorSeed.CurrentVersion " +
                "(saves of the old floors are then refused, not reinterpreted) and record new goldens with HOTEL_FLOOR_GOLDENS=write. Changed: " +
                string.Join("; ", expected.Zip(actual).Where(p => p.First != p.Second).Select(p => $"{p.First} -> {p.Second}")));
        if (recorded["sourceStamp"]!.GetValue<string>() != stamp)
            throw new InvalidOperationException("The floor generator's source or content changed under the same generator version. Bump " +
                "FloorSeed.CurrentVersion and record new goldens with HOTEL_FLOOR_GOLDENS=write: floors outside the named seeds may have changed, " +
                "and a save must not rebuild one as a different floor.");
    }

    // The generator's code and the authored content it reads, with line endings normalized so Windows and Linux agree.
    private static string SourceStamp()
    {
        string root = Repository();
        IEnumerable<string> Files(string folder, string pattern) => Directory.Exists(Path.Combine(root, folder))
            ? Directory.GetFiles(Path.Combine(root, folder), pattern, SearchOption.AllDirectories) : [];
        string[] files = [.. Files("src/Hotel.Game/Floors", "*.cs"), .. Files("src/Hotel.Game/Scene/Kit", "*.cs"), .. Files("content/floors", "*.json"),
            Path.Combine(root, "content/scene/kit.json"), Path.Combine(root, "content/scene/fixtures.json")];
        StringBuilder all = new();
        foreach (string file in files.Select(f => Path.GetRelativePath(root, f).Replace('\\', '/')).Order(StringComparer.Ordinal))
            all.Append(file).Append('\n').Append(File.ReadAllText(Path.Combine(root, file)).Replace("\r\n", "\n")).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(all.ToString()))).ToLowerInvariant();
    }

    private static string Repository()
    {
        for (DirectoryInfo? at = new(AppContext.BaseDirectory); at is not null; at = at.Parent)
            if (File.Exists(Path.Combine(at.FullName, "tests", "Hotel.Smoke", "Hotel.Smoke.csproj"))) return at.FullName;
        throw new InvalidOperationException("Floor fingerprints need the repository checkout (tests/Hotel.Smoke).");
    }
}
