using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Hotel.Game.Floors;

/// <summary>
/// The canonical text of a resolved plan, hashed into its identity. Each line is a kind and its quoted, escaped parts,
/// so no part can run into the next. It deliberately avoids serializer output and collection insertion order:
/// writers add records in a stable order, and <see cref="Set"/> sorts an unordered collection itself.
/// </summary>
/// <remarks>Adapted from Rifles' canonical identity; see docs/reuse.md.</remarks>
internal sealed class CanonicalText
{
    private readonly StringBuilder text = new(1024);

    internal CanonicalText Line(string kind, params string[] parts)
    {
        Quote(kind);
        foreach (string part in parts) Quote(part);
        text.Append('\n');
        return this;
    }

    /// <summary>One line per value, in ordinal order whatever order they were gathered in.</summary>
    internal CanonicalText Set(string kind, IEnumerable<string> values)
    {
        foreach (string value in values.Order(StringComparer.Ordinal)) Line(kind, value);
        return this;
    }

    internal static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A float's shortest round-trip text, so equal values always write the same characters.</summary>
    internal static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>The SHA-256 of the seed's lines followed by this text, as lower-case hex.</summary>
    internal string Hash(FloorSeed seed)
    {
        CanonicalText whole = new CanonicalText()
            .Line("version", Number(seed.Version)).Line("run", seed.Run.ToString(CultureInfo.InvariantCulture))
            .Line("depth", Number(seed.Depth)).Line("shift", Number(seed.Shift));
        whole.text.Append(text);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(whole.text.ToString()))).ToLowerInvariant();
    }

    public override string ToString() => text.ToString();

    private void Quote(string part) => text.Append('"').Append(JsonEncodedText.Encode(part).ToString()).Append("\"|");
}
