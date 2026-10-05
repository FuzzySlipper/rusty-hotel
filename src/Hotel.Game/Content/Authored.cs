using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization.Metadata;
using Rusty.Engine;

namespace Hotel.Game.Content;

/// <summary>Reads one authored content file into its typed record; errors name the file.</summary>
internal static partial class Authored
{
    /// <param name="keys">Control labels by action. When given, each <c>{key.action}</c> in the file becomes that
    /// action's label before parsing, so authored text never spells out a key.</param>
    internal static T Read<T>(IEngineContext engine, string path, JsonTypeInfo<T> type,
        IReadOnlyDictionary<string, string>? keys = null) where T : class
    {
        try
        {
            using ContentReference content = engine.Content.OpenReference(new ContentOpenRequest(path));
            ulong length = engine.Content.ReadReferenceInfo(content).Span[0].ByteLength;
            ReadOnlyMemory<byte> bytes = engine.Content.ReadBytes(new ContentReadBytesRequest(content, 0, checked((uint)length)));
            if (keys is not null) bytes = Encoding.UTF8.GetBytes(KeyReference().Replace(Encoding.UTF8.GetString(bytes.Span), match =>
                keys.TryGetValue(match.Groups[1].Value, out string? label)
                    ? JsonEncodedText.Encode(label).ToString()
                    : throw new JsonException($"unknown control '{match.Value}'; see {"input/bindings.json"}.")));
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

    [GeneratedRegex(@"\{key\.([A-Za-z]+)\}")]
    private static partial Regex KeyReference();

    internal static void Positive(string path, string field, float value) =>
        Require(float.IsFinite(value) && value > 0, path, field, $"must be a positive number of seconds or metres; found {value}.");

    internal static void Within(string path, string field, float value, float minimum, float maximum) =>
        Require(float.IsFinite(value) && value >= minimum && value <= maximum, path, field,
            $"must be between {minimum} and {maximum}; found {value}.");

    internal static void AtLeast(string path, string field, float value, float minimum) =>
        Require(float.IsFinite(value) && value >= minimum, path, field, $"must be at least {minimum}; found {value}.");

    /// <summary>A world position or direction: exactly three finite components.</summary>
    internal static void Point(string path, string field, float[] value) =>
        Require(value.Length == 3 && value.All(float.IsFinite), path, field,
            $"must be [x, y, z] with three finite numbers; found {value.Length} component(s).");

    /// <summary>A linear RGB colour: three finite, non-negative components.</summary>
    internal static void Colour(string path, string field, float[] value) =>
        Require(value.Length == 3 && value.All(c => float.IsFinite(c) && c >= 0), path, field,
            "must be [r, g, b] with three non-negative numbers.");

    internal static void Finite(string path, string field, float value) =>
        Require(float.IsFinite(value), path, field, $"must be a finite number; found {value}.");

    internal static Vector3 Vector(float[] value) => value.Length == 3
        ? new(value[0], value[1], value[2])
        : throw new InvalidOperationException("Hotel coordinates must contain three components.");
}
