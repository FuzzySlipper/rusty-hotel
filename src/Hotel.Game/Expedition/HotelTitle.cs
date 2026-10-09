using System.Text.Json;
using Hotel.Game.Content;
using Rusty.Engine;

namespace Hotel.Game.Expedition;

/// <summary>A title-menu choice: continue the save, begin a new expedition, delete the save, or leave play for the menu.</summary>
internal enum TitleChoice { Continue, New, Delete, Leave }

/// <summary>
/// The title menu's state: whether it is showing, what is saved and what it says about it, and the choices the DOM
/// companion claims through the paused <c>hotel.title</c> intent. The product carries a choice out across the world and
/// the expedition; replacing or deleting a save needs the claim's confirmation.
/// </summary>
internal sealed class HotelTitle(TitleDefinition definition)
{

    internal TitleDefinition Definition => definition;
    internal bool Active { get; private set; }
    internal SaveProbe Save { get; private set; } = new(SaveCondition.None);
    internal string Message { get; private set; } = "";
    /// <summary>Changes whenever the menu's state does, so a screen can tell a settled choice from a pending one.</summary>
    internal ulong Revision { get; private set; }

    /// <summary>Shows the menu over what the save now holds.</summary>
    internal void Open(SaveProbe save, string? note = null)
    {
        Active = true;
        Save = save;
        Message = note ?? Describe(save);
        Revision++;
    }

    internal void Close() { Active = false; Revision++; }

    /// <summary>The choices claimed in these intents that the menu admits now; a refused one says why.</summary>
    internal IEnumerable<TitleChoice> Read(ReadOnlySpan<ProductInputEvent> intents)
    {
        List<TitleChoice> chosen = [];
        foreach (ProductInputEvent input in intents)
        {
            if (input.Kind != InputEventKind.DirectProductPayload || !input.Intent.Span.SequenceEqual("hotel.title"u8)
                || !input.PayloadContract.Span.SequenceEqual("hotel.title.v1"u8)) continue;
            if (Parse(input.PayloadData) is not var (choice, confirmed)) continue;
            if (Admit(choice, confirmed)) chosen.Add(choice);
            else { Message = definition.Text.ConfirmFirst; Revision++; }
        }
        return chosen;
    }

    private bool Admit(TitleChoice choice, bool confirmed) => choice switch
    {
        TitleChoice.Continue => Active && Save.Condition == SaveCondition.Ready,
        TitleChoice.New => Active && (Save.Condition == SaveCondition.None || confirmed),
        TitleChoice.Delete => Active && Save.Condition != SaveCondition.None && confirmed,
        _ => !Active
    };

    private static (TitleChoice, bool)? Parse(ReadOnlyMemory<byte> data)
    {
        try
        {
            using JsonDocument payload = JsonDocument.Parse(data);
            JsonElement root = payload.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("choice", out JsonElement name) || name.ValueKind != JsonValueKind.String
                || !Enum.TryParse(name.GetString(), ignoreCase: true, out TitleChoice choice) || !Enum.IsDefined(choice)) return null;
            bool confirmed = root.TryGetProperty("confirm", out JsonElement confirm) && confirm.ValueKind == JsonValueKind.True;
            return (choice, confirmed);
        }
        catch (JsonException) { return null; }
    }

    private string Describe(SaveProbe save) => save.Condition switch
    {
        SaveCondition.Ready when save.State!.Returns == 0 => definition.Text.SavedNew,
        SaveCondition.Ready => Template.Fill(definition.Text.Saved, ("returns", save.State!.Returns), ("secured", save.State.SecuredFinds.Length)),
        SaveCondition.Older => Template.Fill(definition.Text.Older, ("found", save.Version), ("current", CheckpointState.CurrentVersion)),
        SaveCondition.Damaged => Template.Fill(definition.Text.Damaged, ("error", save.Error)),
        _ => definition.Text.NoSave
    };
}
