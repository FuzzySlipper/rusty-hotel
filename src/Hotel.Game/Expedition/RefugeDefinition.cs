using Hotel.Game.Content;
using Rusty.Engine;

namespace Hotel.Game.Expedition;

/// <summary>The excursion's refuge: its saved identity and the notebook's interaction point.</summary>
internal sealed record RefugeDefinition(string Id, float[] Point);

/// <summary>Checkpoint receipts and status lines.</summary>
internal sealed record ExpeditionMessages(string Continued, string InitialReady, string Busy, string SaveFailed, string NothingSecured,
    string Secured, string Saved, string SavedTitle, string NotSavedTitle, string SavedStatus, string NotSavedStatus,
    string ReturnedInitial, string Returned, string RecoveredTitle, string Recovered)
{
    internal const string Path = "expedition/messages.json";
    internal static ExpeditionMessages Load(IEngineContext engine)
    {
        ExpeditionMessages text = Authored.Read(engine, Path, ContentJson.Default.ExpeditionMessages);
        Template.Check(Path, "saveFailed", text.SaveFailed, "error");
        Template.Check(Path, "secured", text.Secured, "finds");
        Template.Check(Path, "saved", text.Saved, "secured", "returns", "health", "ammo", "summon");
        Template.Check(Path, "savedStatus", text.SavedStatus, "returns", "count");
        Template.Check(Path, "returned", text.Returned, "returns");
        Template.Check(Path, "recovered", text.Recovered, "status");
        Template.Plain(Path, ("continued", text.Continued), ("initialReady", text.InitialReady), ("busy", text.Busy),
            ("nothingSecured", text.NothingSecured), ("savedTitle", text.SavedTitle), ("notSavedTitle", text.NotSavedTitle),
            ("notSavedStatus", text.NotSavedStatus), ("returnedInitial", text.ReturnedInitial), ("recoveredTitle", text.RecoveredTitle));
        return text;
    }
}
