using System.Text.Json.Serialization;
using Hotel.Game.Actions;
using Hotel.Game.Content;
using Hotel.Game.Mechanics;
using Hotel.Game.Residents;
using Hotel.Game.Supplies;
using Rusty.Engine;

namespace Hotel.Game.Combat;

/// <summary>
/// The combat domain's authored files: timing, weapons, resident kinds and player-facing text, with the stat vocabulary
/// a resident's block and a hit's damage kind are written in.
/// </summary>
internal sealed record CombatDefinition(CombatTuning Tuning, ResidentKind[] Residents, CombatMessages Text,
    MechanicsDefinition Mechanics, ActionCatalog Actions, FactionCatalog Factions, ResidentLook[] Looks, HeldCatalog Held, RagdollCatalog Ragdolls)
{
    /// <summary>The health a resident of this kind starts and is bounded at, from its stat block.</summary>
    internal int MaximumHealth(ResidentKind kind) => (int)new ActorStats(Mechanics, kind.Stats, null).Track(HotelSupplies.HealthTrack).MaximumValue;

    internal ResidentLook Look(ResidentKind kind) => Looks.Single(l => l.Id == kind.Look);

    internal static CombatDefinition Load(IEngineContext engine, IReadOnlyDictionary<string, string> keys, MechanicsDefinition mechanics,
        ActionCatalog actions, string[] surfaces, Loot.LootCatalog loot)
    {
        CombatMessages text = Authored.Read(engine, CombatMessages.Path, ContentJson.Default.CombatMessages, keys);
        text.Validate();
        CombatTuning tuning = Authored.Read(engine, CombatTuning.Path, ContentJson.Default.CombatTuning);
        tuning.Validate();
        FactionCatalog factions = Authored.Read(engine, FactionCatalog.Path, ContentJson.Default.FactionCatalog);
        factions.Validate();
        LookCatalog looks = Authored.Read(engine, LookCatalog.Path, ContentJson.Default.LookCatalog);
        Authored.Require(looks.Looks.Select(l => l.Id).Distinct().Count() == looks.Looks.Length, LookCatalog.Path, "looks", "an id appears more than once.");
        for (int i = 0; i < looks.Looks.Length; i++) looks.Looks[i].Validate(i, surfaces);
        ResidentKind[] residents = Authored.Read(engine, ResidentCatalog.Path, ContentJson.Default.ResidentCatalog).Residents;
        for (int i = 0; i < residents.Length; i++)
        {
            residents[i].Validate(i, mechanics, actions, factions, looks);
            Authored.AtLeast(ResidentCatalog.Path, $"residents[{i}].experience", residents[i].Experience, 0);
            Authored.Require(loot.Table(residents[i].Loot) is not null, ResidentCatalog.Path, $"residents[{i}].loot",
                $"unknown loot table '{residents[i].Loot}'; see content/{Loot.LootCatalog.TablesPath}.");
            residents[i] = residents[i] with { AttackReach = residents[i].Actions.Max(a => HotelCombat.Reach(actions.Action(a.Action)!)) };
        }
        RagdollCatalog ragdolls = RagdollCatalog.Load(engine);
        for (int i = 0; i < looks.Looks.Length; i++)
            if (looks.Looks[i].Ragdoll is { } rig)
                Authored.Require(ragdolls.Rigs.ContainsKey(rig), LookCatalog.Path, $"looks[{i}].ragdoll", $"unknown rig '{rig}' in content/{RagdollCatalog.Path}.");
        CombatDefinition definition = new(tuning, residents, text, mechanics, actions, factions, looks.Looks, HeldCatalog.Load(engine), ragdolls);
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
    string ResidentFalls, string ResidentHits, string ResidentTurnsAside, string BlowTurnedAside)
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
        Template.Check(Path, "residentTurnsAside", ResidentTurnsAside, "resident");
        Template.Check(Path, "blowTurnedAside", BlowTurnedAside, "resident");
    }
}

/// <summary>One resident in one excursion. Its id is the saved identity.</summary>
internal sealed record ResidentPlacement(string Id, string Kind, float[] Position);
