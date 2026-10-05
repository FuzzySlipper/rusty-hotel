using System.Numerics;
using Hotel.Game.Content;
using Rusty.Engine;

namespace Hotel.Game.Audio;

/// <summary>Authored ambient beds. Engine owns decoding, mixing, spatialization and playback time.</summary>
internal sealed class HotelAmbience : IDisposable
{
    private readonly IAudioService audio;
    private readonly List<(AudioClip Clip, AmbientDefinition Definition)> clips = [];
    private readonly List<AudioVoice> voices = [];

    internal HotelAmbience(IEngineContext engine, AmbientDefinition[] definitions)
    {
        audio = engine.Audio;
        try
        {
            foreach (AmbientDefinition definition in definitions)
            {
                using ContentReference source = engine.Content.OpenReference(new(definition.Path));
                clips.Add((audio.OpenClipFromContent(new(source)), definition));
            }
        }
        catch { Dispose(); throw; }
    }

    internal void Start()
    {
        foreach (var (clip, definition) in clips)
        {
            bool placed = definition.Position is not null;
            voices.Add(audio.CreateVoice(new(clip, AudioBus.Ambient, definition.Volume, 1, true,
                placed ? 1 : 0, definition.Range, AudioRolloff.Linear, 0,
                placed ? AudioEmitterKind.World3d : AudioEmitterKind.Global2d,
                placed ? Authored.Vector(definition.Position!) : Vector3.Zero, 0, Vector3.Zero)));
        }
    }

    public void Dispose()
    {
        foreach (AudioVoice voice in voices) voice.Dispose();
        foreach (var (clip, _) in clips) clip.Dispose();
    }
}
