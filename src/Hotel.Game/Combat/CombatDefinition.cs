using System.Text.Json.Serialization;
using Hotel.Game.Actions;
using Hotel.Game.Content;
using Hotel.Game.Mechanics;
using Hotel.Game.Supplies;
using Rusty.Engine;

namespace Hotel.Game.Combat;

/// <summary>
/// The combat domain's authored files: timing, weapons, resident kinds and player-facing text, with the stat vocabulary
/// a resident's block and a hit's damage kind are written in.
/// </summary>
internal sealed record CombatDefinition(CombatTuning Tuning, ResidentKind[] Residents, CombatMessages Text,
    MechanicsDefinition Mechanics, ActionCatalog Actions)
{
    /// <summary>The health a resident of this kind starts and is bounded at, from its stat block.</summary>
    internal int MaximumHealth(ResidentKind kind) => (int)new ActorStats(Mechanics, kind.Stats, null).Track(HotelSupplies.HealthTrack).MaximumValue;

    /// <summary>The action a resident of this kind attacks with.</summary>
    internal ActionDefinition Attack(ResidentKind kind) => Actions.Action(kind.Attack)!;

    internal static CombatDefinition Load(IEngineContext engine, IReadOnlyDictionary<string, string> keys, MechanicsDefinition mechanics,
        ActionCatalog actions)
    {
        CombatMessages text = Authored.Read(engine, CombatMessages.Path, ContentJson.Default.CombatMessages, keys);
        text.Validate();
        CombatTuning tuning = Authored.Read(engine, CombatTuning.Path, ContentJson.Default.CombatTuning);
        tuning.Validate();
        ResidentKind[] residents = Authored.Read(engine, ResidentCatalog.Path, ContentJson.Default.ResidentCatalog).Residents;
        for (int i = 0; i < residents.Length; i++)
        {
            ResidentKind r = residents[i];
            Template.Plain(ResidentCatalog.Path, ($"residents[{i}].name", r.Name));
            mechanics.Validate(ResidentCatalog.Path, $"residents[{i}].stats", r.Stats);
            actions.Require(ResidentCatalog.Path, $"residents[{i}].attack", r.Attack);
            Authored.Require(actions.Action(r.Attack)!.Delivery.Kind != DeliveryKind.Self, ResidentCatalog.Path, $"residents[{i}].attack",
                "a resident attacks with an action that reaches the investigator.");
            Authored.AtLeast(ResidentCatalog.Path, $"residents[{i}].speed", r.Speed, 0);
            Authored.Positive(ResidentCatalog.Path, $"residents[{i}].sightRange", r.SightRange);
            Authored.AtLeast(ResidentCatalog.Path, $"residents[{i}].leash", r.Leash, 0);
            Authored.Positive(ResidentCatalog.Path, $"residents[{i}].radius", r.Radius);
            Authored.Positive(ResidentCatalog.Path, $"residents[{i}].height", r.Height);
            // The eye sits within the upper half of the body, measured from its centre.
            Authored.Within(ResidentCatalog.Path, $"residents[{i}].eyeHeight", residents[i].EyeHeight, 0, residents[i].Height / 2);
            residents[i] = r with { AttackReach = HotelCombat.Reach(actions.Action(r.Attack)!) };
        }
        CombatDefinition definition = new(tuning, residents, text, mechanics, actions);
        for (int i = 0; i < residents.Length; i++)
            Authored.Require(definition.MaximumHealth(residents[i]) >= 1, ResidentCatalog.Path, $"residents[{i}].stats", "gives no health.");
        return definition;
    }
}

/// <summary>How long combat feedback stays visible.</summary>
internal sealed record CombatTuning(float NoticeSeconds, float HitFlashSeconds, float HurtFlashSeconds)
{
    internal const string Path = "combat/tuning.json";

    internal void Validate()
    {
        Authored.Positive(Path, "noticeSeconds", NoticeSeconds);
        Authored.Positive(Path, "hitFlashSeconds", HitFlashSeconds);
        Authored.Positive(Path, "hurtFlashSeconds", HurtFlashSeconds);
    }
}

/// <summary>Combat notices and HUD action states.</summary>
internal sealed record CombatMessages(string Overwhelmed, string Recovering, string Ready, string EmptyHands, string NotEnough,
    string NeedsItem, string NotReady, string Swapped, string StruckSurroundings, string Miss, string Hit,
    string ResidentFalls, string ResidentHits)
{
    internal const string Path = "combat/messages.json";

    // Every field, with the placeholders its caller fills; any other placeholder is an authoring error.
    internal void Validate()
    {
        Template.Plain(Path, ("overwhelmed", Overwhelmed), ("recovering", Recovering), ("ready", Ready), ("emptyHands", EmptyHands),
            ("struckSurroundings", StruckSurroundings), ("miss", Miss));
        Template.Check(Path, "notEnough", NotEnough, "track", "action");
        Template.Check(Path, "needsItem", NeedsItem, "action", "classification");
        Template.Check(Path, "notReady", NotReady, "action");
        Template.Check(Path, "swapped", Swapped, "item");
        Template.Check(Path, "hit", Hit, "resident");
        Template.Check(Path, "residentFalls", ResidentFalls, "resident");
        Template.Check(Path, "residentHits", ResidentHits, "resident", "damage");
    }
}

/// <summary>Resident kinds: their attack, approach, sight and body, independent of where an excursion places them.</summary>
internal sealed record ResidentCatalog(ResidentKind[] Residents)
{
    internal const string Path = "combat/residents.json";
}
/// <param name="EyeHeight">Height of the resident's eye above its body centre: sight lines, beams and summon targets start here.</param>
/// <param name="Stats">The kind's stat block in the mechanics vocabulary: its health, resistances and attributes.</param>
/// <param name="Attack">The action it attacks with, by id in the action catalog; its reach is how close the resident comes.</param>
internal sealed record ResidentKind(string Id, string Name, ResidentBehavior Behavior,
    ActorStatBlock Stats, string Attack, float Speed, float SightRange, float Leash, float Radius, float Height, float EyeHeight)
{
    /// <summary>How close its attack reaches, resolved from the action when combat content loads.</summary>
    [JsonIgnore] internal float AttackReach { get; init; }
}

/// <summary>Which authored silhouette and tell a resident presents. Shared approach/attack rules use its tuning.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ResidentBehavior>))]
internal enum ResidentBehavior { Porter, Lamp }

/// <summary>One resident in one excursion. Its id is the saved identity.</summary>
internal sealed record ResidentPlacement(string Id, string Kind, float[] Position);
