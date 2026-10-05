using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hotel.Game;
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
        static ReadOnlyMemory<byte> Json(ReadOnlyMemory<byte> file, Action<JsonNode> change)
        {
            JsonNode root = JsonNode.Parse(file.Span)!;
            change(root);
            return Encoding.UTF8.GetBytes(root.ToJsonString());
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
        Edit("combat/messages.json", s => s.Replace("\"Hit · {resident}\"", "\"Hit · {resdent}\""), out var placeholder);
        message = Failure(placeholder);
        Check(message.Contains("content/combat/messages.json hit") && message.Contains("{resdent}") && message.Contains("{resident}"),
            "unknown template placeholder names file, field and the allowed placeholders: " + message);
        Edit("combat/messages.json", s => s.Replace("{key.reload} to return", "{key.reloda} to return"), out var control);
        message = Failure(control);
        Check(message.Contains("content/combat/messages.json") && message.Contains("{key.reloda}"), "unknown control reference names its file: " + message);

        var unbound = Authored();
        unbound["input/bindings.json"] = Json(unbound["input/bindings.json"], root => root["weapons"]!.AsArray().RemoveAt(1));
        message = Failure(unbound);
        Check(message.Contains("content/input/bindings.json weapons"), "a weapon without a binding is an authoring error: " + message);

        // Moved tuning is range-checked, and every text field (not only templated ones) rejects unknown placeholders.
        foreach (var (file, change, expected) in new (string, Action<JsonNode>, string)[] {
            ("combat/tuning.json", root => root["reloadSeconds"] = -1, "content/combat/tuning.json reloadSeconds"),
            ("spirits/hushwing.json", root => root["manifestation"]!["entranceFraction"] = 2, "content/spirits/hushwing.json manifestation.entranceFraction"),
            ("supplies/messages.json", root => root["noticeSeconds"] = 0, "content/supplies/messages.json noticeSeconds"),
            ("combat/residents.json", root => root["residents"]![0]!["eyeHeight"] = 5, "content/combat/residents.json residents[0].eyeHeight"),
            ("combat/messages.json", root => root["ready"] = "Ready {resident}", "content/combat/messages.json ready"),
            ("supplies/messages.json", root => root["emptyPocket"] = "Empty {pocket}.", "content/supplies/messages.json emptyPocket"),
            ("spirits/hushwing.json", root => root["text"]!["callHint"] = "Q · {verb}", "content/spirits/hushwing.json text.callHint"),
            // Shapes and ranges local to one domain's file are rejected before any owner sees them.
            ("excursions/west-wing/placements.json", root => root["arrival"]!["position"] = new JsonArray(0, 1),
                "content/excursions/west-wing/placements.json arrival.position"),
            ("excursions/west-wing/placements.json", root => root["finds"]![0]!["count"] = 0,
                "content/excursions/west-wing/placements.json finds[0].count"),
            ("excursions/west-wing/placements.json", root => root["finds"]![0]!["count"] = 1000,
                "content/excursions/west-wing/placements.json finds[0].count"),
            ("excursions/west-wing/geometry.json", root => root["boxes"]![0]!["max"] = new JsonArray(-5, 0, 1),
                "content/excursions/west-wing/geometry.json boxes[0].max"),
            ("excursions/west-wing/route.json", root => root["doors"]![0]!["hinge"] = new JsonArray(1, 2, 3, 4),
                "content/excursions/west-wing/route.json doors[0].hinge"),
            ("excursions/west-wing/route.json", root => root["rooms"]![0]!["max"]![0] = root["rooms"]![0]!["min"]![0]!.GetValue<float>(),
                "content/excursions/west-wing/route.json rooms[0].max"),
            ("excursions/west-wing/route.json", root => root["rooms"]![0]!["max"]![2] = root["rooms"]![0]!["min"]![2]!.GetValue<float>(),
                "content/excursions/west-wing/route.json rooms[0].max"),
            ("excursions/west-wing/ambience.json", root => root["voices"]![0]!["volume"] = 3,
                "content/excursions/west-wing/ambience.json voices[0].volume"),
            ("player/tuning.json", root => root["radius"] = 2, "content/player/tuning.json radius"),
            ("supplies/resources.json", root => root["initialHealth"] = 500, "content/supplies/resources.json initialHealth"),
            ("supplies/items.json", root => root["items"]![0]!["stackLimit"] = 0, "content/supplies/items.json items[0].stackLimit"),
            ("combat/weapons.json", root => root["weapons"]![0]!["range"] = 0, "content/combat/weapons.json weapons[0].range"),
            ("combat/residents.json", root => root["residents"]![0]!["health"] = 0, "content/combat/residents.json residents[0].health"),
            ("spirits/hushwing.json", root => root["range"] = -1, "content/spirits/hushwing.json range"),
            ("scene/surfaces.json", root => root["surfaces"]![0]!["roughness"] = 2, "content/scene/surfaces.json surfaces[0].roughness"),
            ("route/interaction.json", root => root["releaseAngle"] = 0.01, "content/route/interaction.json releaseAngle"),
            ("interface/tuning.json", root => root["supplyPockets"] = 0, "content/interface/tuning.json supplyPockets") })
        {
            var invalid = Authored();
            invalid[file] = Json(invalid[file], change);
            message = Failure(invalid);
            Check(message.Contains(expected), $"invalid {file} is rejected at {expected}: " + message);
        }

        // A fourth quick pocket is two content edits: the pocket count and its binding.
        var fourth = Authored();
        fourth["interface/tuning.json"] = Json(fourth["interface/tuning.json"], root => root["quickPockets"] = 4);
        fourth["input/bindings.json"] = Json(fourth["input/bindings.json"], root =>
            root["quickPockets"]!.AsArray().Add(new JsonObject { ["label"] = "6", ["keys"] = new JsonArray("Digit6") }));
        using (EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions
            { PersistenceRoot = Path.Combine(Path.GetTempPath(), "hotel-content-" + Guid.NewGuid()), Content = fourth }))
            host.Call(engine =>
            {
                using HotelProduct product = new(new ProductCreateContext(engine, new ProductContent(default),
                    new ProductInputConfiguration(default, default, default, default, InputCursorMode.PointerLock), default!));
                CaptureCommands commands = new();
                product.RegisterDebugCommands(commands);
                product.Start();
                string Supply()
                {
                    using JsonDocument json = JsonDocument.Parse(commands.Module!.Observe().Message);
                    return json.RootElement.GetProperty("supplyMessage").GetString()!;
                }
                ProductUpdate Step(ulong at, params ProductInputEvent[] input) => new(new ProductUpdateFacts(ProductUpdateMode.Realtime,
                    ProductLifecycleState.Running, 1, 1, 0, at, 60, 1, 0, 1d / 60), input);
                product.Update(Step(0));
                Check(Supply() == "", "no quick supply before the new key");
                product.Update(Step(1, default(ProductInputEvent) with
                    { Kind = InputEventKind.Key, Edge = InputEdge.Pressed, Keyboard = KeyboardControl.Digit6, X = 1 }));
                Check(Supply() == Owners.Content(engine).Supplies.Text.EmptyPocket, "the authored fourth quick key reaches the fourth pocket");
            });
        Console.WriteLine("Content checks passed: unknown fields, missing values, cross-file references, template placeholders in every text field, moved tuning ranges, local shapes and ranges in every domain file, and control references name their file and field; a quick pocket is added by content alone.");
    }
}
