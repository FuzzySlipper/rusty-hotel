using Hotel.Game.Content;

namespace Hotel.Game.Audio;

/// <summary>One excursion's ambient loops.</summary>
internal sealed record AmbienceDefinition(AmbientDefinition[] Voices)
{
    internal void Validate(string path)
    {
        for (int i = 0; i < Voices.Length; i++)
        {
            Authored.Within(path, $"voices[{i}].volume", Voices[i].Volume, 0, 1);
            if (Voices[i].Position is { } position) Authored.Point(path, $"voices[{i}].position", position);
            Authored.Positive(path, $"voices[{i}].range", Voices[i].Range);
        }
    }
}
internal sealed record AmbientDefinition(string Path, float Volume, float[]? Position = null, float Range = 8);
