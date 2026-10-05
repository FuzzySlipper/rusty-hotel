using System.Text;
using Rusty.Engine;
using Rusty.Engine.Testing;

// Authored content errors name the file and field instead of falling back to defaults.
internal static class ContentChecks
{
    internal static void Run()
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "content");
        Dictionary<string, ReadOnlyMemory<byte>> Authored() => Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
            .ToDictionary(p => Path.GetRelativePath(folder, p).Replace('\\', '/'), p => (ReadOnlyMemory<byte>)File.ReadAllBytes(p));
        string Edit(string file, Func<string, string> change, out Dictionary<string, ReadOnlyMemory<byte>> content)
        {
            content = Authored();
            string before = Encoding.UTF8.GetString(content[file].Span);
            string after = change(before);
            if (after == before) throw new InvalidOperationException($"Content check edit did not apply to {file}.");
            content[file] = Encoding.UTF8.GetBytes(after);
            return file;
        }
        string Failure(Dictionary<string, ReadOnlyMemory<byte>> content)
        {
            using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions
            { PersistenceRoot = Path.Combine(Path.GetTempPath(), "hotel-content-" + Guid.NewGuid()), Content = content });
            string message = "";
            host.Call(engine =>
            {
                try { Owners.Content(engine); }
                catch (InvalidOperationException error) { message = error.Message; }
            });
            return message;
        }
        void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }

        Edit("combat/weapons.json", s => s.Replace("\"ammoCost\": 1", "\"ammoCost\": 1, \"ammoCots\": 2"), out var typo);
        string message = Failure(typo);
        Check(message.Contains("content/combat/weapons.json") && message.Contains("ammoCots"), "unknown field names its file: " + message);

        Edit("combat/weapons.json", s => s.Replace("\"damage\": 24,", ""), out var missing);
        message = Failure(missing);
        Check(message.Contains("content/combat/weapons.json") && message.Contains("damage"), "missing field names its file: " + message);

        Edit("excursions/west-wing/geometry.json", s => s.Replace("\"material\": \"carpet\"", "\"material\": \"carpte\""), out var reference);
        message = Failure(reference);
        Check(message.Contains("content/excursions/west-wing/geometry.json boxes[") && message.Contains("carpte"),
            "broken cross-file reference names file, field and value: " + message);
        Console.WriteLine("Content checks passed: unknown fields, missing values and cross-file references name their file and field.");
    }
}
