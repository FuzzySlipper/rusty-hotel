using System.Text;
using Hotel.Game;
using Hotel.Game.Content;
using Hotel.Game.Expedition;
using Rusty.Engine;
using Rusty.Engine.Input;
using Rusty.Engine.Persistence;
using Rusty.Engine.Testing;

// The title menu: a session opens at it, its choices come through the hotel.title intent, replacing or deleting a save
// needs confirmation, an older or unreadable save is described and can be deleted, and play can leave for it.
internal static class TitleChecks
{
    /// <summary>Opens a product past its title menu: continuing the save, or beginning when nothing is saved.</summary>
    internal static void Begin(HotelProduct product)
    {
        product.Start();
        if (!product.Title.Active) return;
        product.HandlePausedIntents([Claim(product.Title.Save.Condition == SaveCondition.Ready ? "continue" : "new", confirm: true)]);
        if (product.Title.Active) throw new InvalidOperationException("The title menu would not open the expedition: " + product.Title.Message);
    }

    internal static ProductInputEvent Claim(string choice, bool confirm = false) => default(ProductInputEvent) with
    {
        Kind = InputEventKind.DirectProductPayload, ValueKind = InputValueKind.ProductPayload,
        Intent = Encoding.UTF8.GetBytes("hotel.title"), PayloadContract = Encoding.UTF8.GetBytes("hotel.title.v1"),
        PayloadData = Encoding.UTF8.GetBytes($$"""{"choice":"{{choice}}","confirm":{{(confirm ? "true" : "false")}}}""")
    };

    internal static void Run()
    {
        void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
        string root = Path.Combine(Path.GetTempPath(), "hotel-title-" + Guid.NewGuid());
        var content = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "content"), "*", SearchOption.AllDirectories)
            .ToDictionary(p => Path.GetRelativePath(Path.Combine(AppContext.BaseDirectory, "content"), p).Replace('\\', '/'), p => (ReadOnlyMemory<byte>)File.ReadAllBytes(p));
        try
        {
            using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions { PersistenceRoot = root, Content = content });
            host.Call(engine =>
            {
                HotelProduct Product() => new(new ProductCreateContext(engine, new ProductContent(default),
                    new ProductInputConfiguration(default, default, default, default, InputCursorMode.PointerLock), default!));
                TitleMessages text = Owners.Content(engine).Title.Text;
                using var store = new ProductStateStore<CheckpointState>(engine, HotelExpedition.Scope, new CheckpointCodec());
                using PersistenceStore raw = engine.Persistence.OpenStore(new(HotelExpedition.Scope));
                bool Saved() { using PersistenceBlob blob = engine.Persistence.Load(new(raw, HotelExpedition.Key)); return engine.Persistence.DescribeBlob(blob).Present; }

                // Nothing saved: the menu says so and a new expedition begins without asking.
                using (HotelProduct product = Product())
                {
                    product.Start();
                    Check(product.Title.Active && product.Title.Save.Condition == SaveCondition.None && product.Title.Message == text.NoSave && !Saved(),
                        "a first session opens at the title menu with nothing saved");
                    product.HandlePausedIntents([Claim("continue")]);
                    Check(product.Title.Active, "there is nothing to continue");
                    product.HandlePausedIntents([Claim("new")]);
                    Check(!product.Title.Active && Saved(), "a new expedition begins and stores its starting checkpoint");

                    // Leaving play for the menu: the save is offered, and replacing it asks first.
                    int healthy = product.World.Supplies.Health;
                    product.World.Supplies.Damage(new(5, "blunt"));
                    product.HandlePausedIntents([Claim("leave")]);
                    Check(product.Title.Active && product.Title.Save.Condition == SaveCondition.Ready, "play leaves for the title menu, which offers the save");
                    product.HandlePausedIntents([Claim("new")]);
                    Check(product.Title.Active && product.Title.Message == text.ConfirmFirst, "a new expedition over a save needs confirmation");
                    product.HandlePausedIntents([Claim("continue")]);
                    Check(!product.Title.Active && product.World.Supplies.Health == healthy,
                        $"continuing restores the saved expedition, not what was left unsaved: {product.World.Supplies.Health}");
                }

                // An older version's save: described as such, kept until the player deletes it, then a new one begins.
                engine.Persistence.Save(new(raw, HotelExpedition.Key, PersistenceRevisionGuard.Any, 0,
                    Encoding.UTF8.GetBytes("""{"version":10,"returns":2,"refuge":"west-wing","supplies":{"pockets":[]}}""")));
                using (HotelProduct product = Product())
                {
                    product.Start();
                    Check(product.Title.Active && product.Title.Save.Condition == SaveCondition.Older &&
                        product.Title.Message == Template.Fill(text.Older, ("found", 10), ("current", CheckpointState.CurrentVersion)),
                        $"an older save is named as one: '{product.Title.Message}'");
                    product.HandlePausedIntents([Claim("continue"), Claim("delete")]);
                    Check(product.Title.Active && Saved(), "an older save cannot be continued, and is not deleted unconfirmed");
                    product.HandlePausedIntents([Claim("delete", confirm: true)]);
                    Check(product.Title.Active && !Saved() && product.Title.Save.Condition == SaveCondition.None && product.Title.Message == text.Deleted,
                        "a confirmed delete removes it");
                    product.HandlePausedIntents([Claim("new")]);
                    Check(!product.Title.Active && store.Load(HotelExpedition.Key).State!.Version == CheckpointState.CurrentVersion, "and a new expedition begins");
                }

                // A current-version save whose shape reads but whose contents cannot (a null among the run's floors) is damaged.
                string current;
                using (HotelProduct product = Product())
                {
                    Begin(product);
                    current = System.Text.Json.JsonSerializer.Serialize(product.World.Expedition.Checkpoint!, CheckpointJson.Default.CheckpointState);
                }
                var damaged = System.Text.Json.Nodes.JsonNode.Parse(current)!;
                damaged["floors"]!["floors"] = new System.Text.Json.Nodes.JsonArray((System.Text.Json.Nodes.JsonNode?)null);
                engine.Persistence.Save(new(raw, HotelExpedition.Key, PersistenceRevisionGuard.Any, 0, Encoding.UTF8.GetBytes(damaged.ToJsonString())));
                using (HotelProduct product = Product())
                {
                    product.Start();
                    Check(product.Title.Active && product.Title.Save.Condition == SaveCondition.Damaged, $"a null entry in a saved collection opens the menu as damaged: '{product.Title.Message}'");
                }

                // A save that cannot be read at all is described and left as it is.
                engine.Persistence.Save(new(raw, HotelExpedition.Key, PersistenceRevisionGuard.Any, 0, Encoding.UTF8.GetBytes("{")));
                using (HotelProduct product = Product())
                {
                    product.Start();
                    Check(product.Title.Active && product.Title.Save.Condition == SaveCondition.Damaged && !product.Title.Save.Error.Contains("version"),
                        $"an unreadable save is described as damaged: '{product.Title.Message}'");
                    using PersistenceBlob blob = engine.Persistence.Load(new(raw, HotelExpedition.Key));
                    Check(engine.Persistence.DescribeBlob(blob).PayloadLen == 1, "and is not replaced");
                }
            });
        }
        finally { Directory.Delete(root, true); }
        Console.WriteLine("Title checks passed: opening at the menu, new and continue, leaving play for it, confirmation before replacing or deleting, and older and unreadable saves described and deletable.");
    }
}
