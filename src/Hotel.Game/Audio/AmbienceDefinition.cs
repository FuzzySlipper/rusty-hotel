namespace Hotel.Game.Audio;

/// <summary>One excursion's ambient loops.</summary>
internal sealed record AmbienceDefinition(AmbientDefinition[] Voices);
internal sealed record AmbientDefinition(string Path, float Volume, float[]? Position = null, float Range = 8);
