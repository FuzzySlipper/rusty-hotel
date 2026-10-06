using Hotel.Game.Content;
using Rusty.Engine;

namespace Hotel.Game.Spirits;

/// <summary>One spirit of the roster: its pact terms, look, call action, manifestation timing and its own wording.</summary>
/// <param name="Description">Field-case description; may use {range}, {cost} and {seconds} (its first effect's) from its action.</param>
/// <param name="Action">What calling it does, by id in the action catalog: costing only summon charge.</param>
internal sealed record SpiritDefinition(string Id, string Name, string Description, int WelcomeCharges,
    float Arrival, float Hold, float Departure, string Action, SpiritLook Look, SpiritText Text, ManifestationTuning Manifestation)
{
    internal static string Path(string id) => $"spirits/{id}.json";
    internal static SpiritDefinition Load(IEngineContext engine, string id, IReadOnlyDictionary<string, string> keys,
        Mechanics.MechanicsDefinition mechanics, Actions.ActionCatalog actions, string[] surfaces)
    {
        SpiritDefinition spirit = Authored.Read(engine, Path(id), ContentJson.Default.SpiritDefinition, keys);
        Authored.Require(spirit.Id == id, Path(id), "id", $"the file for '{id}' names '{spirit.Id}'.");
        string path = Path(id);
        Template.Plain(path, ("name", spirit.Name), ("text.callHint", spirit.Text.CallHint), ("text.arriving", spirit.Text.Arriving),
            ("text.holding", spirit.Text.Holding), ("text.departing", spirit.Text.Departing));
        Template.Check(path, "description", spirit.Description, "range", "cost", "seconds");
        Template.Check(path, "text.callResult", spirit.Text.CallResult, "spirit", "resident", "cost");
        Authored.AtLeast(path, "welcomeCharges", spirit.WelcomeCharges, 0);
        Authored.Positive(path, "arrival", spirit.Arrival);
        Authored.Positive(path, "hold", spirit.Hold);
        Authored.Positive(path, "departure", spirit.Departure);
        actions.Require(path, "action", spirit.Action);
        Actions.ActionDefinition call = actions.Action(spirit.Action)!;
        Authored.Require(call.Cost.Tracks.Count > 0 && call.Cost.Tracks.Keys.All(t => t == Supplies.HotelSupplies.SummonTrack) &&
            call.Cost.Item is null && call.Effects.Length + call.SelfEffects.Length > 0, path, "action",
            $"'{spirit.Action}' must cost only summon charge and bring an effect.");
        SpiritLook look = spirit.Look;
        foreach (var (field, surface) in new[] { ("look.body", look.Body), ("look.wing", look.Wing), ("look.inset", look.Inset),
            ("look.head", look.Head), ("look.eye", look.Eye) })
            Authored.Require(surfaces.Contains(surface), path, field, $"unknown surface '{surface}'.");
        Authored.Positive(path, "look.scale", look.Scale);
        ManifestationTuning at = spirit.Manifestation;
        Authored.Finite(path, "manifestation.approach", at.Approach);
        Authored.Finite(path, "manifestation.side", at.Side);
        Authored.Finite(path, "manifestation.lift", at.Lift);
        Authored.Within(path, "manifestation.entranceFraction", at.EntranceFraction, 0, 1);
        Authored.Finite(path, "manifestation.entranceDrop", at.EntranceDrop);
        return spirit;
    }

    /// <summary>How long a call keeps its visit: arrival, hold and departure.</summary>
    internal float Visit => Arrival + Hold + Departure;
}

/// <summary>Every spirit a pact can be made with, each in its own file.</summary>
internal sealed record SpiritRoster(string[] Spirits)
{
    internal const string Path = "spirits/roster.json";

    internal static SpiritDefinition[] Load(IEngineContext engine, IReadOnlyDictionary<string, string> keys,
        Mechanics.MechanicsDefinition mechanics, Actions.ActionCatalog actions, string[] surfaces)
    {
        SpiritRoster roster = Authored.Read(engine, Path, ContentJson.Default.SpiritRoster);
        Authored.Require(roster.Spirits.Length > 0 && roster.Spirits.Distinct().Count() == roster.Spirits.Length, Path, "spirits",
            "names each spirit once, and at least one.");
        return roster.Spirits.Select(id => SpiritDefinition.Load(engine, id, keys, mechanics, actions, surfaces)).ToArray();
    }
}

/// <summary>The surfaces a spirit's moth is drawn in, and its size: one articulated creature, many spirits.</summary>
internal sealed record SpiritLook(string Body, string Wing, string Inset, string Head, string Eye, float Scale);

/// <summary>Wording particular to this spirit: what calling it does and the names of its visit's phases.</summary>
internal sealed record SpiritText(string CallHint, string CallResult, string Arriving, string Holding, string Departing);

/// <summary>Where the manifestation appears relative to its target, in metres.</summary>
/// <param name="Approach">Distance in front of the resident's eye, toward the player.</param>
/// <param name="Side">Offset to the side, so it does not block the reticle.</param>
/// <param name="Lift">Height above the resident's eye.</param>
/// <param name="EntranceFraction">How far from the player toward the destination it first appears (0–1).</param>
/// <param name="EntranceDrop">How far below that line it first appears.</param>
internal sealed record ManifestationTuning(float Approach, float Side, float Lift, float EntranceFraction, float EntranceDrop);

/// <summary>Pact notices and refusals shared by every spirit; {spirit} names the particular one.</summary>
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
        Template.Plain(Path, ("noPactEquip", text.NoPactEquip), ("noPactCall", text.NoPactCall));
        Template.Check(Path, "noTarget", text.NoTarget, "range");
        return text;
    }
}

/// <summary>Where an excursion hides a spirit's bell, and how its text names that place.</summary>
internal sealed record SpiritBellPlacement(string Spirit, float[] Point, string Place);
