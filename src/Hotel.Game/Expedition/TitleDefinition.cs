using System.Text.Json.Serialization;
using Hotel.Game.Content;
using Rusty.Engine;

namespace Hotel.Game.Expedition;

/// <summary>Where a session opens: the title menu, or straight into the saved expedition when it can be continued.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TitleStart>))]
internal enum TitleStart { Title, Continue }

/// <summary>The title menu: how a session opens, and what it says about the save.</summary>
internal sealed record TitleDefinition(TitleStart Start, TitleMessages Text)
{
    internal const string Path = "expedition/title.json";

    internal static TitleDefinition Load(IEngineContext engine)
    {
        TitleDefinition title = Authored.Read(engine, Path, ContentJson.Default.TitleDefinition);
        TitleMessages text = title.Text;
        Template.Check(Path, "text.saved", text.Saved, "returns", "secured");
        Template.Check(Path, "text.older", text.Older, "found", "current");
        Template.Check(Path, "text.damaged", text.Damaged, "error");
        Template.Plain(Path, ("text.noSave", text.NoSave), ("text.savedNew", text.SavedNew), ("text.replaceWarning", text.ReplaceWarning),
            ("text.deleteWarning", text.DeleteWarning), ("text.deleted", text.Deleted), ("text.confirmFirst", text.ConfirmFirst));
        return title;
    }
}

/// <param name="SavedNew">A readable save not yet returned to the refuge.</param>
/// <param name="Saved">A readable save: its returns and secured finds.</param>
/// <param name="Older">A save made by another version of the hotel: the version found, and the one this build reads.</param>
/// <param name="Damaged">A save that cannot be read for another reason.</param>
/// <param name="ConfirmFirst">A replacing or deleting choice sent without its confirmation.</param>
internal sealed record TitleMessages(string NoSave, string SavedNew, string Saved, string Older, string Damaged, string ReplaceWarning,
    string DeleteWarning, string Deleted, string ConfirmFirst);
