using Hotel.Game.Content;
using Hotel.Game.Mechanics;
using Rusty.Engine;

namespace Hotel.Game.Player;

/// <summary>The investigator's stat block: attributes, any derived bases of their own, resistances and starting tracks.</summary>
internal static class PlayerStats
{
    internal const string Path = "player/stats.json";

    internal static ActorStatBlock Load(IEngineContext engine, MechanicsDefinition mechanics)
    {
        ActorStatBlock block = Authored.Read(engine, Path, ContentJson.Default.ActorStatBlock);
        mechanics.Validate(Path, "stats", block);
        return block;
    }
}
