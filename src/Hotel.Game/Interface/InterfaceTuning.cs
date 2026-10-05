using Hotel.Game.Content;
using Rusty.Engine;

namespace Hotel.Game.Interface;

/// <summary>Field-case capacity and how many of its pockets the HUD offers as quick access.</summary>
internal sealed record InterfaceTuning(int SupplyPockets, int QuickPockets)
{
    internal const string Path = "interface/tuning.json";
    internal static InterfaceTuning Load(IEngineContext engine)
    {
        InterfaceTuning t = Authored.Read(engine, Path, ContentJson.Default.InterfaceTuning);
        Authored.AtLeast(Path, "supplyPockets", t.SupplyPockets, 1);
        Authored.Within(Path, "quickPockets", t.QuickPockets, 1, t.SupplyPockets);
        return t;
    }
}
