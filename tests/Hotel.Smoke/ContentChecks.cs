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

        Edit("combat/residents.json", s => s.Replace("\"range\": 3.6,", "\"range\": 3.6, \"rnage\": 2,"), out var typo);
        string message = Failure(typo);
        Check(message.Contains("content/combat/residents.json") && message.Contains("rnage"), "unknown field names its file: " + message);

        Edit("combat/residents.json", s => s.Replace("\"speed\": 0.85,", ""), out var missing);
        message = Failure(missing);
        Check(message.Contains("content/combat/residents.json") && message.Contains("speed"), "missing field names its file: " + message);

        Edit("scene/kit.json", s => s.Replace("\"floor\": \"carpet\"", "\"floor\": \"carpte\""), out var reference);
        message = Failure(reference);
        Check(message.Contains("content/scene/kit.json styles.corridor.floor") && message.Contains("carpte"),
            "broken cross-file reference names file, field and value: " + message);
        Edit("combat/messages.json", s => s.Replace("\"Hit · {resident}\"", "\"Hit · {resdent}\""), out var placeholder);
        message = Failure(placeholder);
        Check(message.Contains("content/combat/messages.json hit") && message.Contains("{resdent}") && message.Contains("{resident}"),
            "unknown template placeholder names file, field and the allowed placeholders: " + message);
        Edit("combat/messages.json", s => s.Replace("{key.secondary} to return", "{key.secondry} to return"), out var control);
        message = Failure(control);
        Check(message.Contains("content/combat/messages.json") && message.Contains("{key.secondry}"), "unknown control reference names its file: " + message);


        // Moved tuning is range-checked, and every text field (not only templated ones) rejects unknown placeholders.
        foreach (var (file, change, expected) in new (string, Action<JsonNode>, string)[] {
            ("combat/tuning.json", root => root["noticeSeconds"] = -1, "content/combat/tuning.json noticeSeconds"),
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
            // The floor plan: spaces, links, fixtures and the sockets the route and placements use.
            ("excursions/west-wing/plan.json", root => root["spaces"]![0]!["max"]![0] = root["spaces"]![0]!["min"]![0]!.GetValue<float>(),
                "content/excursions/west-wing/plan.json spaces[0].max"),
            ("excursions/west-wing/plan.json", root => root["spaces"]![2]!["max"] = new JsonArray(0.5f, -1.9f),
                "content/excursions/west-wing/plan.json spaces[2]"),
            ("excursions/west-wing/plan.json", root => root["spaces"]![0]!["style"] = "ballroom",
                "content/excursions/west-wing/plan.json spaces[0].style"),
            ("excursions/west-wing/plan.json", root => root["links"]![1]!["between"]![1] = "workroom",
                "content/excursions/west-wing/plan.json links[1].between"),
            ("scene/surfaces.json", root => root["surfaces"]![0]!.AsObject().Remove("normalScale"),
                "content/scene/surfaces.json surfaces[0].normalScale"),
            ("scene/surfaces.json", root => root["surfaces"]![0]!.AsObject().Remove("normalMap"),
                "content/scene/surfaces.json surfaces[0].normalMap"),
            ("excursions/west-wing/plan.json", root => root["links"]![1]!["at"] = -8.0f,
                "content/excursions/west-wing/plan.json links[1].at"),
            ("excursions/west-wing/plan.json", root => root["links"]![1]!["height"] = 9,
                "content/excursions/west-wing/plan.json links[1].height"),
            ("excursions/west-wing/plan.json", root => root["fixtures"]![0]!["at"] = new JsonArray(40, -2),
                "content/excursions/west-wing/plan.json fixtures[0].at"),
            // The refuge desk's origin is inside the room, but the desk itself would pass through the west wall.
            ("excursions/west-wing/plan.json", root => root["fixtures"]![13]!["at"] = new JsonArray(-3.4f, 2.75f),
                "content/excursions/west-wing/plan.json fixtures[13].at: 'writing-desk' part"),
            ("excursions/west-wing/plan.json", root => root["spaces"]![2]!["posts"]!["porter"] = new JsonArray(-7f, -3.5f),
                "content/excursions/west-wing/plan.json spaces[2].posts.porter"),
            ("excursions/west-wing/placements.json", root => root["residents"]![0]!["socket"] = "west.nowhere",
                "content/excursions/west-wing/placements.json residents[0].socket"),
            ("excursions/west-wing/plan.json", root => root["links"]![1]!["sill"] = 0.8f,
                "content/excursions/west-wing/plan.json links[1].sill"),
            ("excursions/west-wing/plan.json", root => root["fixtures"]![4]!["along"] = 3,
                "content/excursions/west-wing/plan.json fixtures[4].along"),
            ("excursions/west-wing/plan.json", root => root["fixtures"]![0]!["kind"] = "chandelier",
                "content/excursions/west-wing/plan.json fixtures[0].kind"),
            ("excursions/west-wing/plan.json", root => root["fixtures"]!.AsArray().First(f => f!["find"] is not null)!.AsObject().Remove("find"),
                "content/excursions/west-wing/plan.json fixtures["),
            ("scene/fixtures.json", root => root["fixtures"]![1]!["parts"]![0]!["max"] = new JsonArray(-1, 2, 0.08f),
                "content/scene/fixtures.json fixtures[1].parts[0].max"),
            ("excursions/west-wing/route.json", root => root["doors"]![0]!["link"] = "refuge-corridor",
                "content/excursions/west-wing/route.json doors[0].link"),
            ("excursions/west-wing/route.json", root => root["doors"]![0]!["opensInto"] = "refuge",
                "content/excursions/west-wing/route.json doors[0].opensInto"),
            ("excursions/west-wing/route.json", root => root["readings"]![0]!["socket"] = "refuge-notice.nowhere",
                "content/excursions/west-wing/route.json readings[0].socket"),
            ("excursions/west-wing/placements.json", root => root["finds"]![0]!["socket"] = "missing.focus",
                "content/excursions/west-wing/placements.json finds[0].socket"),
            // Room modules: a fixture across a corridor blocks its far doorway; shapes and sockets name the module file.
            ("floors/modules/corridor-straight.json", root => root["fixtures"]!.AsArray().Add(new JsonObject { ["kind"] = "guest-bench", ["space"] = "hall", ["at"] = new JsonArray(1.5f, 2f) }),
                "content/floors/modules/corridor-straight.json doorways[1]"),
            ("floors/modules/guest-room-a.json", root => { root["spaces"]![0]!["max"]![0] = 3.5f; root["doorways"]![0]!["edge"] = "East"; },
                "content/floors/modules/guest-room-a.json doorways[0].edge"),
            ("floors/modules/guest-room-a.json", root => root["sockets"]![0]!["socket"] = "table.drawer",
                "content/floors/modules/guest-room-a.json sockets[0].socket"),
            ("floors/modules/guest-room-a.json", root => root["fixtures"]![0]!["find"] = "loot",
                "content/floors/modules/guest-room-a.json fixtures[0].find"),
            ("floors/modules/guest-room-a.json", root => root["size"] = new JsonArray(4.2f, 6),
                "content/floors/modules/guest-room-a.json size"),
            ("floors/modules.json", root => root["doorways"]!.AsArray().RemoveAt(0), "content/floors/modules.json doorways"),
            ("floors/modules/guest-room-a.json", root => root["spaces"]![0]!["min"] = new JsonArray(0), "content/floors/modules/guest-room-a.json spaces[0].min"),
            ("floors/modules/guest-room-a.json", root => root["fixtures"]![0]!["at"] = new JsonArray(2), "content/floors/modules/guest-room-a.json fixtures[0].at"),
            ("floors/modules/guest-room-a.json", root => root["fixtures"]![0]!["space"] = "attic", "content/floors/modules/guest-room-a.json fixtures[0].space"),
            ("floors/modules/corridor-fire-door.json", root => root["links"]![0]!["between"]![1] = "attic",
                "content/floors/modules/corridor-fire-door.json links[0].between[1]"),
            ("excursions/west-wing/ambience.json", root => root["voices"]![0]!["volume"] = 3,
                "content/excursions/west-wing/ambience.json voices[0].volume"),
            ("player/tuning.json", root => root["radius"] = 2, "content/player/tuning.json radius"),
            ("player/stats.json", root => root["attributes"]!["might"] = 500, "content/player/stats.json stats.attributes.might"),
            ("player/stats.json", root => root["attributes"]!.AsObject().Remove("nerve"), "content/player/stats.json stats.attributes.nerve"),
            ("mechanics/stats.json", root => root["tracks"]![0]!["maximum"] = "no-such-stat", "content/mechanics/stats.json tracks[0].maximum"),
            ("mechanics/stats.json", root => root["derived"]![0]!["from"]![0]!["attribute"] = "luck", "content/mechanics/stats.json derived[0].from[0].attribute"),
            ("mechanics/damage.json", root => root["kinds"]![0]!["maximumResistance"] = 2, "content/mechanics/damage.json kinds[0].maximumResistance"),
            ("mechanics/effects.json", root => root["effects"]![0]!["ward"] = JsonNode.Parse("{\"absorb\":5,\"damageKinds\":[\"fire\"]}"), "content/mechanics/effects.json effects[0]: must set exactly one"),
            ("mechanics/effects.json", root => root["effects"]![1]!["maximumStacks"] = 3, "content/mechanics/effects.json effects[1].maximumStacks"),
            ("mechanics/effects.json", root => root["effects"]![0]!["restore"]!["interval"] = 60, "content/mechanics/effects.json effects[0].restore.interval"),
            ("mechanics/effects.json", root => root["effects"]![7]!["stat"]!["stat"] = "pace", "content/mechanics/effects.json effects[7].stat.stat"),
            ("mechanics/messages.json", root => root["effectSeconds"] = "{minutes}", "content/mechanics/messages.json effectSeconds"),
            ("supplies/items.json", root => root["items"]![4]!["use"]!["effects"]![0] = "cursed", "content/supplies/items.json items[4].use.effects[0]"),
            ("supplies/items.json", root => root["items"]![4]!["deposit"] = true, "content/supplies/items.json items[4]: must be used"),
            ("supplies/items.json", root => root["items"]![4]!["costs"]!.AsObject().Remove("weight"), "content/supplies/items.json items[4].costs.weight"),
            ("supplies/items.json", root => root["items"]![11]!["wear"]!["stats"]![0]!["stat"] = "luck", "content/supplies/items.json items[11].wear.stats[0].stat"),
            ("supplies/items.json", root => root["items"]![11]!["form"] = "Fungible", "content/supplies/items.json items[11].form"),
            ("supplies/items.json", root => root["items"]![0]!["wear"]!["actions"]![0] = "juggle", "content/supplies/items.json items[0].wear.actions[0]"),
            ("supplies/items.json", root => root["items"]![11]!["wear"]!["actions"] = JsonNode.Parse("[\"prybar-swing\"]"), "content/supplies/items.json items[11].wear"),
            ("supplies/items.json", root => root["items"]![12]!["wear"]!["contributions"]![0]!["kinds"]![0] = "acid", "content/supplies/items.json items[12].wear.contributions[0].kinds[0]"),
            ("player/kit.json", root => root["worn"]![0]!["slots"]![0] = "head", "content/player/kit.json worn"),
            ("actions/actions.json", root => root["actions"]![0]!["delivery"]!["width"] = 0, "content/actions/actions.json actions[0].delivery.width"),
            ("actions/actions.json", root => root["actions"]![2]!["cost"]!["tracks"]!["courage"] = 1, "content/actions/actions.json actions[2].cost.tracks.courage"),
            ("actions/actions.json", root => root["actions"]![3]!["damage"] = JsonNode.Parse("[{\"kind\":\"fire\",\"amount\":1,\"scaling\":[]}]"), "content/actions/actions.json actions[3]: a self action"),
            ("actions/actions.json", root => root["actions"]![0]!["damage"]![0]!["kind"] = "acid", "content/actions/actions.json actions[0].damage[0].kind"),
            ("actions/actions.json", root => root["actions"]![3]!["cost"]!["item"] = "potions", "content/actions/actions.json actions[3].cost.item"),
            ("combat/residents.json", root => root["residents"]![0]!["actions"]![0]!["action"] = "juggle", "content/combat/residents.json residents[0].actions[0]"),
            ("combat/residents.json", root => root["residents"]![0]!["movement"]!["post"] = new JsonObject(), "content/combat/residents.json residents[0].movement: must set exactly one"),
            ("combat/residents.json", root => root["residents"]![0]!["look"] = "ghost", "content/combat/residents.json residents[0].look"),
            ("combat/residents.json", root => root["residents"]![0]!["faction"] = "investigator", "content/combat/residents.json residents[0].faction"),
            ("combat/residents.json", root => root["residents"]![2]!["movement"]!["patrol"]!["points"] = JsonNode.Parse("[[0,0]]"), "content/combat/residents.json residents[2].movement.patrol.points"),
            ("combat/residents.json", root => root["residents"]![0]!["perception"]!["fieldOfView"] = 400, "content/combat/residents.json residents[0].perception.fieldOfView"),
            ("combat/factions.json", root => root["hostile"]![0]![1] = "ghosts", "content/combat/factions.json hostile[0][1]"),
            ("combat/looks.json", root => root["looks"]![0]!["parts"]![0]!["material"] = "felt", "content/combat/looks.json looks[0].parts[0].material"),
            ("supplies/equipment.json", root => root["slots"]![0]!["accepts"]![0] = "hats", "content/supplies/equipment.json slots[0].accepts[0]"),
            ("supplies/capacity.json", root => root["metrics"]![0]!["limit"] = 0, "content/supplies/capacity.json metrics[0].limit"),
            ("spirits/hushwing.json", root => root["action"] = "prybar-swing", "content/spirits/hushwing.json action"),
            ("supplies/items.json", root => root["items"]![4]!["use"]!["action"] = "prybar-swing", "content/supplies/items.json items[4].use.action"),
            ("mechanics/effects.json", root => root["effects"]![8]!["guard"]!["retain"] = 0, "content/mechanics/effects.json effects[8].guard[0]"),
            ("supplies/items.json", root => root["items"]![12]!["wear"]!["contributions"]![0]!["stage"] = "Defeating", "content/supplies/items.json items[12].wear.contributions[0]"),
            ("supplies/items.json", root => root["items"]![4]!["stackLimit"] = 0, "content/supplies/items.json items[4].stackLimit"),
            ("combat/residents.json", root => root["residents"]![0]!["stats"]!["resistances"]!["poison"] = 0.1, "content/combat/residents.json residents[0].stats.resistances.poison"),
            ("spirits/hushwing.json", root => root["arrival"] = -1, "content/spirits/hushwing.json arrival"),
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
