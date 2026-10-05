using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Rusty.Engine;

namespace Hotel.Game.Content;

/// <summary>Reads one authored content file into its typed record; errors name the file.</summary>
internal static class Authored
{
    internal static T Read<T>(IEngineContext engine, string path, JsonTypeInfo<T> type) where T : class
    {
        try
        {
            using ContentReference content = engine.Content.OpenReference(new ContentOpenRequest(path));
            ulong length = engine.Content.ReadReferenceInfo(content).Span[0].ByteLength;
            ReadOnlyMemory<byte> bytes = engine.Content.ReadBytes(new ContentReadBytesRequest(content, 0, checked((uint)length)));
            return JsonSerializer.Deserialize(bytes.Span, type) ?? throw new JsonException("The file holds no value.");
        }
        catch (Exception error)
        {
            throw new InvalidOperationException($"content/{path}: {error.Message}", error);
        }
    }

    /// <summary>Fails authored data that parses but contradicts another definition.</summary>
    internal static void Require(bool condition, string path, string field, string problem)
    {
        if (!condition) throw new InvalidOperationException($"content/{path} {field}: {problem}");
    }

    internal static Vector3 Vector(float[] value) => value.Length == 3
        ? new(value[0], value[1], value[2])
        : throw new InvalidOperationException("Hotel coordinates must contain three components.");
}
