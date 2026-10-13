using System.Text.Json.Nodes;
using Hotel.Game;
using Hotel.Game.Combat;
using Hotel.Game.Interface.Workbench;
using Rusty.Engine;
using Rusty.Engine.Input;

// The first-person workbench: every control, for every look, action and held key, names a source the panel can save
// it to (its pointer resolves in that content file, to a value of the control's own kind).
internal static class WorkbenchChecks
{
    internal static void Run(IEngineContext engine)
    {
        void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
        using HotelProduct product = new(new ProductCreateContext(engine, new ProductContent(default),
            new ProductInputConfiguration(default, default, default, default, InputCursorMode.PointerLock), default!));
        TitleChecks.Begin(product);
        var content = Owners.Content(engine);
        HeldCatalog held = content.Combat.Held;
        string root = Path.Combine(AppContext.BaseDirectory, "content");
        Dictionary<string, JsonNode> files = [];
        JsonNode File(string path) => files.TryGetValue(path, out JsonNode? node) ? node : files[path] = JsonNode.Parse(System.IO.File.ReadAllText(Path.Combine(root, path)))!;
        int controls = 0;
        foreach (var item in content.Supplies.Items.Where(i => i.Wear?.Look is not null))
            foreach (string id in item.Wear!.Actions)
            {
                var action = content.Combat.Actions.Action(id)!;
                HeldModel look = held.Model(item.Wear.Look!);
                int keys = held.Motion.Motions[look.Motion].Track(id)?.Length ?? 0;
                for (int key = -1; key < keys; key++)
                    foreach (var tunable in ViewmodelTunables.For(product.World.CombatView, held, look.Look, action, key).SelectMany(g => g.Tunables))
                    {
                        if (tunable.Control.Source is not { } source) continue;
                        JsonNode? at = File(source.Path);
                        foreach (string token in source.Pointer.Split('/').Skip(1).Select(t => t.Replace("~1", "/").Replace("~0", "~")))
                            at = at is JsonArray array && int.TryParse(token, out int index) ? (index < array.Count ? array[index] : null) : at?[token];
                        Check(at is JsonValue, $"{look.Look} · {id}: control '{tunable.Control.Id}' saves to {source.Path} {source.Pointer}, which holds no value");
                        controls++;
                    }
            }
        // A reload takes only the changed files: unsaved live tuning elsewhere stays, records keep their identity (the
        // motion viewer may hold a pose), tracks follow the file, and what cannot be taken live is reported.
        HeldCatalog live = HeldCatalog.Load(engine), disk = HeldCatalog.Load(engine);
        live.Stances.Stances["melee"].Grip[0] = 0.5f;
        disk.Model("prybar").Adjust.Grip[0] = 0.01f;
        HeldPose raised = live.Motion.Motions["swing"].Poses["raised"];
        disk.Motion.Motions["swing"].Poses["raised"].Position[0] = 0.09f;
        disk.Motion.Motions["swing"].Tracks.Remove("prybar-shove");
        disk = disk with { Motion = disk.Motion with { SettleSeconds = disk.Motion.SettleSeconds * 2 } };
        string[] restart = live.Refresh(disk, new HashSet<string> { HeldCatalog.Path, HeldMotionCatalog.Path });
        Check(live.Stances.Stances["melee"].Grip[0] == 0.5f, "a reload of held.json and held-motion.json keeps unsaved stance tuning");
        Check(live.Model("prybar").Adjust.Grip[0] == 0.01f, "a reload takes held.json's nudge");
        Check(ReferenceEquals(live.Motion.Motions["swing"].Poses["raised"], raised) && raised.Position[0] == 0.09f,
            "a reload updates a pose in place, so a held pose shows it");
        Check(!live.Motion.Motions["swing"].Tracks.ContainsKey("prybar-shove"), "a track removed from the file is removed live");
        Check(restart.Length == 1 && restart[0].Contains("settleSeconds"), $"a settle change is reported as needing a restart: {string.Join("; ", restart)}");
        Console.WriteLine($"Workbench checks passed: {controls} control sources across every look, action and key resolve in their content files; reload takes only changed files, in place.");
    }
}
