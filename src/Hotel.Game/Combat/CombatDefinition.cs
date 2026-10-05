using System.Text.Json.Serialization;
using Hotel.Game.Content;
using Rusty.Engine;

namespace Hotel.Game.Combat;

/// <summary>The combat domain's authored files: timing, weapons, resident kinds and player-facing text.</summary>
internal sealed record CombatDefinition(CombatTuning Tuning, WeaponDefinition[] Weapons, ResidentKind[] Residents, CombatMessages Text)
{
    internal static CombatDefinition Load(IEngineContext engine, IReadOnlyDictionary<string, string> keys)
    {
        CombatMessages text = Authored.Read(engine, CombatMessages.Path, ContentJson.Default.CombatMessages, keys);
        text.Validate();
        WeaponDefinition[] weapons = Authored.Read(engine, WeaponCatalog.Path, ContentJson.Default.WeaponCatalog).Weapons;
        Authored.Require(weapons.Any(w => w.AmmoCost == 0), WeaponCatalog.Path, "weapons", "one weapon must need no ammunition, as the fallback.");
        for (int i = 0; i < weapons.Length; i++)
            Template.Plain(WeaponCatalog.Path, ($"weapons[{i}].name", weapons[i].Name), ($"weapons[{i}].shortName", weapons[i].ShortName),
                ($"weapons[{i}].windupLabel", weapons[i].WindupLabel), ($"weapons[{i}].commitLabel", weapons[i].CommitLabel));
        CombatTuning tuning = Authored.Read(engine, CombatTuning.Path, ContentJson.Default.CombatTuning);
        tuning.Validate();
        ResidentKind[] residents = Authored.Read(engine, ResidentCatalog.Path, ContentJson.Default.ResidentCatalog).Residents;
        for (int i = 0; i < residents.Length; i++)
        {
            Template.Plain(ResidentCatalog.Path, ($"residents[{i}].name", residents[i].Name));
            // The eye sits within the upper half of the body, measured from its centre.
            Authored.Within(ResidentCatalog.Path, $"residents[{i}].eyeHeight", residents[i].EyeHeight, 0, residents[i].Height / 2);
        }
        return new(tuning, weapons, residents, text);
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
internal sealed record WeaponDefinition(string Id, string Name, string ShortName, int Damage, float Range,
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
internal sealed record ResidentKind(string Id, string Name, ResidentBehavior Behavior,
    int Health, int Damage, float Speed, float SightRange, float AttackRange, float Windup, float Commit,
    float Recovery, float Leash, float Radius, float Height, float EyeHeight);

/// <summary>Which authored silhouette and tell a resident presents. Shared approach/attack rules use its tuning.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ResidentBehavior>))]
internal enum ResidentBehavior { Porter, Lamp }

/// <summary>One resident in one excursion. Its id is the saved identity.</summary>
internal sealed record ResidentPlacement(string Id, string Kind, float[] Position);
