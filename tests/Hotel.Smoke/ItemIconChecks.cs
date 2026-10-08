using Rusty.Engine;

// Every item has its line icon in the UI beside the items (content/supplies/icons/<item>.svg, opened to the UI): a 24-unit drawing in the current text colour.
internal static class ItemIconChecks
{
    internal static void Run(IEngineContext engine)
    {
        var content = Owners.Content(engine);
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Directory.Build.props"))) root = Path.GetDirectoryName(root) ?? throw new InvalidOperationException("No repository root above the smoke build.");
        string icons = Path.Combine(root, "content", "supplies", "icons");
        foreach (var item in content.Supplies.Items)
        {
            string path = Path.Combine(icons, item.Id + ".svg");
            if (!File.Exists(path)) throw new InvalidOperationException($"Item '{item.Id}' has no icon at content/supplies/icons/{item.Id}.svg.");
            string svg = File.ReadAllText(path);
            if (!svg.Contains("viewBox=\"0 0 24 24\"") || !svg.Contains("currentColor") || svg.Contains("#"))
                throw new InvalidOperationException($"content/supplies/icons/{item.Id}.svg must be a 24-unit drawing in currentColor alone.");
        }
        string[] stray = Directory.GetFiles(icons, "*.svg").Select(Path.GetFileNameWithoutExtension).Except(content.Supplies.Items.Select(i => i.Id)).ToArray()!;
        if (stray.Length > 0) throw new InvalidOperationException("Icons for no item: " + string.Join(", ", stray));
        Console.WriteLine($"Item icon checks passed: {content.Supplies.Items.Length} items, each with its line icon.");
    }
}
