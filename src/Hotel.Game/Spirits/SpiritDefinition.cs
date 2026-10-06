using Hotel.Game.Content;
using Rusty.Engine;

namespace Hotel.Game.Spirits;

/// <summary>One spirit's pact terms, manifestation timing and its own wording.</summary>
/// <param name="Description">Field-case description; may use {place}, and {range}, {cost} and {interrupt} (its hold's seconds) from its action.</param>
/// <param name="Action">What calling it does, by id in the action catalog: aimed at a resident, costing summon charge, putting a hold on it.</param>
internal sealed record SpiritDefinition(string Id, string Name, string Description, int WelcomeCharges,
    float Arrival, float Hold, float Departure, string Action, SpiritText Text, ManifestationTuning Manifestation)
{
    internal static string Path(string id) => $"spirits/{id}.json";
    internal static SpiritDefinition Load(IEngineContext engine, string id, IReadOnlyDictionary<string, string> keys,
        Mechanics.MechanicsDefinition mechanics, Actions.ActionCatalog actions)
    {
        SpiritDefinition spirit = Authored.Read(engine, Path(id), ContentJson.Default.SpiritDefinition, keys);
        Authored.Require(spirit.Id == id, Path(id), "id", $"the file for '{id}' names '{spirit.Id}'.");
        string path = Path(id);
        Template.Plain(path, ("name", spirit.Name), ("text.callHint", spirit.Text.CallHint), ("text.arriving", spirit.Text.Arriving),
            ("text.holding", spirit.Text.Holding), ("text.departing", spirit.Text.Departing));
        Template.Check(path, "description", spirit.Description, "place", "range", "cost", "interrupt");
        Template.Check(path, "text.callResult", spirit.Text.CallResult, "spirit", "resident", "cost");
        Authored.AtLeast(path, "welcomeCharges", spirit.WelcomeCharges, 0);
        Authored.Positive(path, "arrival", spirit.Arrival);
        Authored.Positive(path, "hold", spirit.Hold);
        Authored.Positive(path, "departure", spirit.Departure);
        actions.Require(path, "action", spirit.Action);
        Actions.ActionDefinition call = actions.Action(spirit.Action)!;
        Authored.Require(call.Delivery.Kind != Actions.DeliveryKind.Self && call.Effects.Any(e => mechanics.Effect(e)!.Hold is not null) &&
            call.Cost.Tracks.Keys.All(t => t == Supplies.HotelSupplies.SummonTrack), path, "action",
            $"'{spirit.Action}' must reach a resident, hold it, and cost only summon charge.");
        ManifestationTuning at = spirit.Manifestation;
        Authored.Finite(path, "manifestation.approach", at.Approach);
        Authored.Finite(path, "manifestation.side", at.Side);
        Authored.Finite(path, "manifestation.lift", at.Lift);
        Authored.Within(path, "manifestation.entranceFraction", at.EntranceFraction, 0, 1);
        Authored.Finite(path, "manifestation.entranceDrop", at.EntranceDrop);
        return spirit;
    }

    /// <summary>How long a call holds its resident: the whole visit, arrival to departure.</summary>
    internal float Interrupt => Arrival + Hold + Departure;
}

/// <summary>Wording particular to this spirit: what calling it does and the names of its visit's phases.</summary>
internal sealed record SpiritText(string CallHint, string CallResult, string Arriving, string Holding, string Departing);

/// <summary>Where the manifestation appears relative to its target, in metres.</summary>
/// <param name="Approach">Distance in front of the resident's eye, toward the player.</param>
/// <param name="Side">Offset to the side, so it does not block the reticle.</param>
/// <param name="Lift">Height above the resident's eye.</param>
/// <param name="EntranceFraction">How far from the player toward the destination it first appears (0–1).</param>
/// <param name="EntranceDrop">How far below that line it first appears.</param>
internal sealed record ManifestationTuning(float Approach, float Side, float Lift, float EntranceFraction, float EntranceDrop);

/// <summary>Pact notices and refusals shared by every spirit; {spirit} and {place} name the particular one.</summary>
internal sealed record SpiritMessages(float NoticeSeconds, string BellLabel, string HudNone, string HudInCase,
    string StatusOverwhelmed, string StatusInCase, string StatusNone, string EquipOverwhelmed, string EquipWhileActive,
    string CannotMake, string Freed, string PactChanged, string NoPactEquip, string Equipped, string Rests, string ChooseAgain,
    string Unreadable, string CallOverwhelmed, string NotEquipped, string NoPactCall, string AlreadyHere, string NoCharge,
    string NoTarget, string Withdraws)
{
    internal const string Path = "spirits/messages.json";
    internal static SpiritMessages Load(IEngineContext engine, IReadOnlyDictionary<string, string> keys)
    {
        SpiritMessages text = Authored.Read(engine, Path, ContentJson.Default.SpiritMessages, keys);
        Authored.Positive(Path, "noticeSeconds", text.NoticeSeconds);
        // Every field, with the placeholders its caller fills; any other placeholder is an authoring error.
        Template.Plain(Path, ("hudNone", text.HudNone), ("hudInCase", text.HudInCase), ("statusOverwhelmed", text.StatusOverwhelmed),
            ("statusInCase", text.StatusInCase), ("statusNone", text.StatusNone), ("equipOverwhelmed", text.EquipOverwhelmed),
            ("cannotMake", text.CannotMake), ("pactChanged", text.PactChanged), ("chooseAgain", text.ChooseAgain),
            ("unreadable", text.Unreadable), ("callOverwhelmed", text.CallOverwhelmed));
        string[] named = ["spirit"];
        foreach (var (field, value) in new[] { ("bellLabel", text.BellLabel), ("equipWhileActive", text.EquipWhileActive),
            ("equipped", text.Equipped), ("rests", text.Rests), ("notEquipped", text.NotEquipped), ("alreadyHere", text.AlreadyHere),
            ("noCharge", text.NoCharge), ("withdraws", text.Withdraws) })
            Template.Check(Path, field, value, named);
        Template.Check(Path, "freed", text.Freed, "spirit", "charges");
        Template.Check(Path, "noPactEquip", text.NoPactEquip, "place");
        Template.Check(Path, "noPactCall", text.NoPactCall, "place");
        Template.Check(Path, "noTarget", text.NoTarget, "range");
        return text;
    }
}

/// <summary>Where an excursion hides a spirit's bell, and how its text names that place.</summary>
internal sealed record SpiritBellPlacement(string Spirit, float[] Point, string Place);
