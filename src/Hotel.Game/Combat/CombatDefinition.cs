using System.Text.Json.Serialization;
using Hotel.Game.Content;
using Rusty.Engine;

namespace Hotel.Game.Combat;

/// <summary>The investigator's weapons and reload timing.</summary>
internal sealed record CombatDefinition(float ReloadSeconds, WeaponDefinition[] Weapons)
{
    internal const string Path = "combat/weapons.json";
    internal static CombatDefinition Load(IEngineContext engine) => Authored.Read(engine, Path, ContentJson.Default.CombatDefinition);
}
internal sealed record WeaponDefinition(string Id, string Name, int Damage, float Range, float Windup, float Commit, float Recovery, int AmmoCost);

/// <summary>Resident kinds: their tells, timing and body, independent of where an excursion places them.</summary>
internal sealed record ResidentCatalog(ResidentKind[] Residents)
{
    internal const string Path = "combat/residents.json";
    internal static ResidentCatalog Load(IEngineContext engine) => Authored.Read(engine, Path, ContentJson.Default.ResidentCatalog);
}
internal sealed record ResidentKind(string Id, string Name, ResidentBehavior Behavior,
    int Health, int Damage, float Speed, float SightRange, float AttackRange, float Windup, float Commit,
    float Recovery, float Leash, float Radius, float Height);

/// <summary>Which authored silhouette and tell a resident presents. Shared approach/attack rules use its tuning.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ResidentBehavior>))]
internal enum ResidentBehavior { Porter, Lamp }

/// <summary>One resident in one excursion. Its id is the saved identity.</summary>
internal sealed record ResidentPlacement(string Id, string Kind, float[] Position);
