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
        uint version = recorded["version"]!.GetValue<uint>();
        if (version != FloorSeed.CurrentVersion)
            throw new InvalidOperationException($"Floor goldens are for generator version {version}; this is {FloorSeed.CurrentVersion}. Record them with HOTEL_FLOOR_GOLDENS=write.");
        string[] Lines(JsonNode list) => list.AsArray().Select(f => $"{f!["run"]}/{f["depth"]} {f["plan"]}").ToArray();
        string[] expected = Lines(recorded["floors"]!), actual = Lines(found);
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException("Floor generation output changed under the same generator version. Bump FloorSeed.CurrentVersion " +
                "(saves of the old floors are then refused, not reinterpreted) and record new goldens with HOTEL_FLOOR_GOLDENS=write. Changed: " +
                string.Join("; ", expected.Zip(actual).Where(p => p.First != p.Second).Select(p => $"{p.First} -> {p.Second}")));
        bool sourceSame = recorded["sourceStamp"]!.GetValue<string>() == stamp;
        Console.WriteLine($"Fingerprint checks passed: {actual.Length} named floors match generator version {version}'s goldens" +
            (sourceSame ? "; generator source unchanged." : "; generator source changed without changing output (record a fresh stamp when convenient)."));
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
