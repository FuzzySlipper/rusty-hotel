using Hotel.Game.Content;
using Rusty.Engine;

namespace Hotel.Game.Spirits;

/// <summary>One spirit's pact terms, manifestation timing and its own wording.</summary>
/// <param name="Description">Field-case description; may use {place}, {range}, {cost} and {interrupt}.</param>
internal sealed record SpiritDefinition(string Id, string Name, string Description, int WelcomeCharges,
    int Cost, float Range, float Arrival, float Hold, float Departure, SpiritText Text, ManifestationTuning Manifestation)
{
    internal static string Path(string id) => $"spirits/{id}.json";
    internal static SpiritDefinition Load(IEngineContext engine, string id, IReadOnlyDictionary<string, string> keys)
    {
        SpiritDefinition spirit = Authored.Read(engine, Path(id), ContentJson.Default.SpiritDefinition, keys);
        Authored.Require(spirit.Id == id, Path(id), "id", $"the file for '{id}' names '{spirit.Id}'.");
        Template.Check(Path(id), "description", spirit.Description, "place", "range", "cost", "interrupt");
        Template.Check(Path(id), "text.callResult", spirit.Text.CallResult, "spirit", "resident", "cost");
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
