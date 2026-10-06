using System.Text.Json.Serialization;
using Hotel.Game.Content;
using Hotel.Game.Mechanics;
using Hotel.Game.Supplies;
using Rusty.Engine;

namespace Hotel.Game.Combat;

/// <summary>
/// The combat domain's authored files: timing, weapons, resident kinds and player-facing text, with the stat vocabulary
/// a resident's block and a hit's damage kind are written in.
/// </summary>
internal sealed record CombatDefinition(CombatTuning Tuning, WeaponDefinition[] Weapons, ResidentKind[] Residents, CombatMessages Text,
    MechanicsDefinition Mechanics)
{
    /// <summary>The health a resident of this kind starts and is bounded at, from its stat block.</summary>
    internal int MaximumHealth(ResidentKind kind) => (int)new ActorStats(Mechanics, kind.Stats, null).Track(HotelSupplies.HealthTrack).MaximumValue;

    internal static CombatDefinition Load(IEngineContext engine, IReadOnlyDictionary<string, string> keys, MechanicsDefinition mechanics)
    {
        CombatMessages text = Authored.Read(engine, CombatMessages.Path, ContentJson.Default.CombatMessages, keys);
        text.Validate();
        WeaponDefinition[] weapons = Authored.Read(engine, WeaponCatalog.Path, ContentJson.Default.WeaponCatalog).Weapons;
        Authored.Require(weapons.Any(w => w.AmmoCost == 0), WeaponCatalog.Path, "weapons", "one weapon must need no ammunition, as the fallback.");
        for (int i = 0; i < weapons.Length; i++)
        {
            WeaponDefinition w = weapons[i];
            Authored.AtLeast(WeaponCatalog.Path, $"weapons[{i}].damage", w.Damage, 0);
            Authored.Require(mechanics.DamageKind(w.DamageKind) is not null, WeaponCatalog.Path, $"weapons[{i}].damageKind",
                $"unknown damage kind '{w.DamageKind}'.");
            Authored.Positive(WeaponCatalog.Path, $"weapons[{i}].range", w.Range);
            Authored.AtLeast(WeaponCatalog.Path, $"weapons[{i}].windup", w.Windup, 0);
            Authored.Positive(WeaponCatalog.Path, $"weapons[{i}].commit", w.Commit);
            Authored.AtLeast(WeaponCatalog.Path, $"weapons[{i}].recovery", w.Recovery, 0);
            Authored.AtLeast(WeaponCatalog.Path, $"weapons[{i}].ammoCost", w.AmmoCost, 0);
        }
        for (int i = 0; i < weapons.Length; i++)
            Template.Plain(WeaponCatalog.Path, ($"weapons[{i}].name", weapons[i].Name), ($"weapons[{i}].shortName", weapons[i].ShortName),
                ($"weapons[{i}].windupLabel", weapons[i].WindupLabel), ($"weapons[{i}].commitLabel", weapons[i].CommitLabel));
        CombatTuning tuning = Authored.Read(engine, CombatTuning.Path, ContentJson.Default.CombatTuning);
        tuning.Validate();
        ResidentKind[] residents = Authored.Read(engine, ResidentCatalog.Path, ContentJson.Default.ResidentCatalog).Residents;
        for (int i = 0; i < residents.Length; i++)
        {
            ResidentKind r = residents[i];
            Template.Plain(ResidentCatalog.Path, ($"residents[{i}].name", r.Name));
            mechanics.Validate(ResidentCatalog.Path, $"residents[{i}].stats", r.Stats);
            Authored.AtLeast(ResidentCatalog.Path, $"residents[{i}].damage", r.Damage, 0);
            Authored.Require(mechanics.DamageKind(r.DamageKind) is not null, ResidentCatalog.Path, $"residents[{i}].damageKind",
                $"unknown damage kind '{r.DamageKind}'.");
            Authored.AtLeast(ResidentCatalog.Path, $"residents[{i}].speed", r.Speed, 0);
            Authored.Positive(ResidentCatalog.Path, $"residents[{i}].sightRange", r.SightRange);
            Authored.Positive(ResidentCatalog.Path, $"residents[{i}].attackRange", r.AttackRange);
            Authored.AtLeast(ResidentCatalog.Path, $"residents[{i}].windup", r.Windup, 0);
            Authored.Positive(ResidentCatalog.Path, $"residents[{i}].commit", r.Commit);
            Authored.AtLeast(ResidentCatalog.Path, $"residents[{i}].recovery", r.Recovery, 0);
            Authored.AtLeast(ResidentCatalog.Path, $"residents[{i}].leash", r.Leash, 0);
            Authored.Positive(ResidentCatalog.Path, $"residents[{i}].radius", r.Radius);
            Authored.Positive(ResidentCatalog.Path, $"residents[{i}].height", r.Height);
            // The eye sits within the upper half of the body, measured from its centre.
            Authored.Within(ResidentCatalog.Path, $"residents[{i}].eyeHeight", residents[i].EyeHeight, 0, residents[i].Height / 2);
        }
        CombatDefinition definition = new(tuning, weapons, residents, text, mechanics);
        for (int i = 0; i < residents.Length; i++)
            Authored.Require(definition.MaximumHealth(residents[i]) >= 1, ResidentCatalog.Path, $"residents[{i}].stats", "gives no health.");
        return definition;
    }
}

/// <summary>Reload time and how long combat feedback stays visible.</summary>
internal sealed record CombatTuning(float ReloadSeconds, float NoticeSeconds, float HitFlashSeconds, float HurtFlashSeconds)
{
    internal const string Path = "combat/tuning.json";

    internal void Validate()
    {
        Authored.Positive(Path, "reloadSeconds", ReloadSeconds);
        Authored.Positive(Path, "noticeSeconds", NoticeSeconds);
        Authored.Positive(Path, "hitFlashSeconds", HitFlashSeconds);
        Authored.Positive(Path, "hurtFlashSeconds", HurtFlashSeconds);
    }
}

/// <summary>The investigator's weapons.</summary>
internal sealed record WeaponCatalog(WeaponDefinition[] Weapons)
{
    internal const string Path = "combat/weapons.json";
}
/// <param name="ShortName">How notices name the weapon in a sentence ("pistol").</param>
/// <param name="WindupLabel">The HUD action while drawing back or steadying.</param>
/// <param name="CommitLabel">The HUD action at the moment of the strike or shot.</param>
/// <param name="DamageKind">The kind of damage a hit deals, against the target's resistance to it.</param>
internal sealed record WeaponDefinition(string Id, string Name, string ShortName, int Damage, string DamageKind, float Range,
    float Windup, float Commit, float Recovery, int AmmoCost, string WindupLabel, string CommitLabel);

/// <summary>Combat notices and HUD action states.</summary>
internal sealed record CombatMessages(string Overwhelmed, string Recovering, string Reloading, string Ready, string EmptyWeapon,
    string NoCartridges, string Loaded, string StruckSurroundings, string Miss, string Hit, string ResidentFalls, string ResidentHits)
{
    internal const string Path = "combat/messages.json";

    // Every field, with the placeholders its caller fills; any other placeholder is an authoring error.
    internal void Validate()
    {
        Template.Plain(Path, ("overwhelmed", Overwhelmed), ("recovering", Recovering), ("reloading", Reloading), ("ready", Ready),
            ("noCartridges", NoCartridges), ("loaded", Loaded), ("struckSurroundings", StruckSurroundings), ("miss", Miss));
        Template.Check(Path, "emptyWeapon", EmptyWeapon, "weapon", "fallback", "fallbackKey");
        Template.Check(Path, "hit", Hit, "resident");
        Template.Check(Path, "residentFalls", ResidentFalls, "resident");
        Template.Check(Path, "residentHits", ResidentHits, "resident", "damage");
    }
}

/// <summary>Resident kinds: their tells, timing and body, independent of where an excursion places them.</summary>
internal sealed record ResidentCatalog(ResidentKind[] Residents)
{
    internal const string Path = "combat/residents.json";
}
/// <param name="EyeHeight">Height of the resident's eye above its body centre: sight lines, beams and summon targets start here.</param>
/// <param name="Stats">The kind's stat block in the mechanics vocabulary: its health, resistances and attributes.</param>
internal sealed record ResidentKind(string Id, string Name, ResidentBehavior Behavior,
    ActorStatBlock Stats, int Damage, string DamageKind, float Speed, float SightRange, float AttackRange, float Windup, float Commit,
    float Recovery, float Leash, float Radius, float Height, float EyeHeight);

/// <summary>Which authored silhouette and tell a resident presents. Shared approach/attack rules use its tuning.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ResidentBehavior>))]
internal enum ResidentBehavior { Porter, Lamp }

/// <summary>One resident in one excursion. Its id is the saved identity.</summary>
internal sealed record ResidentPlacement(string Id, string Kind, float[] Position);
