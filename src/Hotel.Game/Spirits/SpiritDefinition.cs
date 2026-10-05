using Hotel.Game.Content;
using Rusty.Engine;

namespace Hotel.Game.Spirits;

/// <summary>One spirit's pact terms and manifestation timing.</summary>
internal sealed record SpiritDefinition(string Id, string Name, string Description, int WelcomeCharges,
    int Cost, float Range, float Arrival, float Hold, float Departure)
{
    internal static string Path(string id) => $"spirits/{id}.json";
    internal static SpiritDefinition Load(IEngineContext engine, string id)
    {
        SpiritDefinition spirit = Authored.Read(engine, Path(id), ContentJson.Default.SpiritDefinition);
        Authored.Require(spirit.Id == id, Path(id), "id", $"the file for '{id}' names '{spirit.Id}'.");
        return spirit;
    }
}

/// <summary>Where an excursion hides a spirit's bell.</summary>
internal sealed record SpiritBellPlacement(string Spirit, float[] Point);
