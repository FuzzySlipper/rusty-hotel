using System.Buffers;
using System.Text.Json;
using Rusty.Engine.Persistence;

namespace Hotel.Game.Expedition;

/// <summary>
/// The checkpoint's codec: the current shape, read whole, after its version. A save of another version is refused as
/// such (<see cref="OlderSaveException"/>) before its shape is read, so the refusal names what it is.
/// </summary>
internal sealed class CheckpointCodec : IProductStateCodec<CheckpointState>
{
    private readonly JsonProductStateCodec<CheckpointState> current = new(CheckpointJson.Default.CheckpointState);

    public void Encode(in CheckpointState state, IBufferWriter<byte> destination) => current.Encode(in state, destination);

    public CheckpointState Decode(ReadOnlySpan<byte> payload)
    {
        if (Version(payload) is { } version && version != CheckpointState.CurrentVersion) throw new OlderSaveException(version);
        return current.Decode(payload);
    }

    // The top-level "version" number, if the payload is an object that has one.
    private static int? Version(ReadOnlySpan<byte> payload)
    {
        try
        {
            Utf8JsonReader reader = new(payload);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return null;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("version"u8))
                    return reader.Read() && reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int version) ? version : null;
                reader.Skip();
            }
        }
        catch (JsonException) { }
        return null;
    }
}

/// <summary>A save made in another version of the checkpoint's shape; it is never reinterpreted.</summary>
internal sealed class OlderSaveException(int version) : InvalidOperationException($"The save is checkpoint version {version}; this build reads version {CheckpointState.CurrentVersion}.")
{
    internal int Version { get; } = version;
}
