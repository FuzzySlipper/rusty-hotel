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
        float[] prybarIndex = live.Model("prybar").Shape!.Main.Fingers[0];
        disk.Model("prybar").Shape!.Main.Fingers[0][1] = 30;
        disk.Model("prybar").Grips.Main[1] = -0.025f;
        disk.Model("coach-gun").Grips.Off![2] = -0.25f;
        disk.Model("coach-gun").Grips.OffHand![2] = 165;
        HeldPose raised = live.Motion.Motions["swing"].Poses["raised"];
        disk.Motion.Motions["swing"].Poses["raised"].Position[0] = 0.09f;
        disk.Motion.Motions["swing"].Tracks.Remove("prybar-shove");
        // Landmarks follow the file: an edited point in place, a removed point and a removed or added set taken too.
        float[] pistolTop = live.Model("survey-pistol").Landmarks!.BackstrapTop!;
        disk.Model("survey-pistol").Landmarks!.BackstrapTop![1] = 0.05f;
        disk.Model("survey-pistol").Landmarks!.Trigger = null;
        disk.Model("flare-pistol").Landmarks = null;
        HeldModel plain = disk.Looks.First(l => l.Landmarks is null && l.Look != "flare-pistol");
        plain.Landmarks = new(Butt: [0, -0.03f, 0]);
        disk = disk with { Motion = disk.Motion with { SettleSeconds = disk.Motion.SettleSeconds * 2 } };
        string[] restart = live.Refresh(disk, new HashSet<string> { HeldCatalog.Path, HeldMotionCatalog.Path });
        Check(live.Stances.Stances["melee"].Grip[0] == 0.5f, "a reload of held.json and held-motion.json keeps unsaved stance tuning");
        Check(live.Model("prybar").Adjust.Grip[0] == 0.01f, "a reload takes held.json's nudge");
        Check(live.Model("prybar").Grips.Main[1] == -0.025f && live.Model("coach-gun").Grips.Off![2] == -0.25f &&
            live.Model("coach-gun").Grips.OffHand![2] == 165, "grip points and support-hand turns reload live");
        Check(ReferenceEquals(live.Model("prybar").Shape!.Main.Fingers[0], prybarIndex) && prybarIndex[1] == 30, "a reload takes a look's finger shape in place");
        Check(ReferenceEquals(live.Motion.Motions["swing"].Poses["raised"], raised) && raised.Position[0] == 0.09f,
            "a reload updates a pose in place, so a held pose shows it");
        Check(!live.Motion.Motions["swing"].Tracks.ContainsKey("prybar-shove"), "a track removed from the file is removed live");
        Check(ReferenceEquals(live.Model("survey-pistol").Landmarks!.BackstrapTop, pistolTop) && pistolTop[1] == 0.05f
            && live.Model("survey-pistol").Landmarks!.Trigger is null, "a landmark reloads in place and a removed one is gone");
        Check(live.Model("flare-pistol").Landmarks is null && live.Model(plain.Look).Landmarks?.Butt is [0, -0.03f, 0],
            $"a look's landmarks removed or added in the file follow it live ({plain.Look})");
        Check(restart.Length == 1 && restart[0].Contains("settleSeconds"), $"a settle change is reported as needing a restart: {string.Join("; ", restart)}");
        HeldCatalog rigged = HeldCatalog.Load(engine);
        rigged = rigged with { Arms = rigged.Arms with { Hands = rigged.Arms.Hands with { Fingers = [.. rigged.Arms.Hands.Fingers.SkipLast(1)] } } };
        string[] digits = HeldCatalog.Load(engine).Refresh(rigged, new HashSet<string> { ArmsDefinition.Path });
        Check(digits.Length == 1 && digits[0].Contains("finger"), $"a change to the hands' finger joints is reported as needing a restart: {string.Join("; ", digits)}");

        // The viewer holds a key as its track is now: changing which pose the key reaches shows at once.
        CombatView combat = product.World.CombatView;
        HeldCatalog shown = product.Content.Combat.Held;
        HeldModel prybarLook = shown.Model("prybar");
        var swing = content.Combat.Actions.Action("prybar-swing")!;
        MotionKey[] track = shown.Motion.Motions[prybarLook.Motion].Track(swing.Id)!;
        MotionKey first = track[0];
        combat.View("prybar");
        combat.HoldKey(swing, 0);
        var before = combat.Shown("prybar").At;
        track[0] = first with { Pose = track[1].Pose };
        var after = combat.Shown("prybar").At;
        var expected = shown.Motion.Motions[prybarLook.Motion].Poses[track[1].Pose].Place(prybarLook).At;
        track[0] = first;
        combat.View(null);
        Check(System.Numerics.Vector3.Distance(after, expected) < 1e-4f && System.Numerics.Vector3.Distance(before, after) > 1e-3f,
            "a held key shows the pose its track now names, without reselecting it");
        Console.WriteLine($"Workbench checks passed: {controls} control sources across every look, action and key resolve in their content files; reload takes only changed files, in place.");
    }
}
