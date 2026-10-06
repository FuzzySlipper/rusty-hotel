using System.Text.Json.Serialization;
using Hotel.Game.Content;
using Rusty.Engine;

namespace Hotel.Game.Scene.Kit;

/// <summary>Reusable furnishings and fittings, each a small set of boxes in its own frame.</summary>
internal sealed record FixtureCatalog(FixtureDefinition[] Fixtures)
{
    internal const string Path = "scene/fixtures.json";

    internal static FixtureCatalog Load(IEngineContext engine)
    {
        FixtureCatalog catalog = Authored.Read(engine, Path, ContentJson.Default.FixtureCatalog);
        for (int i = 0; i < catalog.Fixtures.Length; i++)
        {
            FixtureDefinition fixture = catalog.Fixtures[i];
            string at = $"fixtures[{i}]";
            Authored.Require(fixture.Parts.Length > 0, Path, $"{at}.parts", "a fixture needs at least one part.");
            for (int p = 0; p < fixture.Parts.Length; p++)
            {
                FixturePart part = fixture.Parts[p];
                Authored.Point(Path, $"{at}.parts[{p}].min", part.Min);
                Authored.Point(Path, $"{at}.parts[{p}].max", part.Max);
                Authored.Require(part.Max[0] > part.Min[0] && part.Max[1] > part.Min[1] && part.Max[2] > part.Min[2],
                    Path, $"{at}.parts[{p}].max", "must be larger than min on every axis.");
            }
            for (int l = 0; l < (fixture.Lights ?? []).Length; l++)
            {
                FixtureLight light = fixture.Lights![l];
                Authored.Point(Path, $"{at}.lights[{l}].offset", light.Offset);
                Authored.Colour(Path, $"{at}.lights[{l}].color", light.Color);
                Authored.AtLeast(Path, $"{at}.lights[{l}].intensity", light.Intensity, 0);
                Authored.Positive(Path, $"{at}.lights[{l}].range", light.Range);
            }
            foreach (var (name, point) in fixture.Sockets ?? []) Authored.Point(Path, $"{at}.sockets.{name}", point);
        }
        string? repeated = catalog.Fixtures.GroupBy(f => f.Id).FirstOrDefault(g => g.Count() > 1)?.Key;
        Authored.Require(repeated is null, Path, "fixtures", $"id '{repeated}' appears more than once.");
        return catalog;
    }
}

/// <summary>Where a fixture's frame sits: on the floor, on a wall face, under the ceiling, or on another fixture's socket.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FixtureMount>))]
internal enum FixtureMount { Floor, Wall, Ceiling, Socket }

/// <summary>
/// One fixture. Coordinates are in its own frame: +y up from its origin; for a wall fixture +z points out of the wall
/// into the room. <see cref="Sockets"/> name points other fixtures and placements can attach to.
/// </summary>
internal sealed record FixtureDefinition(string Id, FixtureMount Mount, FixturePart[] Parts,
    FixtureLight[]? Lights = null, Dictionary<string, float[]>? Sockets = null);

/// <param name="Find">The part shows a collectable find and disappears when it is taken.</param>
internal sealed record FixturePart(string Name, string Material, float[] Min, float[] Max, bool Solid = false, bool Find = false);
/// <param name="Shadow">The light casts shadows. Each shadowed point light renders the scene six more times a frame.</param>
internal sealed record FixtureLight(float[] Offset, float[] Color, float Intensity, float Range, bool Shadow);
