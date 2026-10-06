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
            Authored.Require(fixture.Parts.Length > 0 || (fixture.Models ?? []).Length > 0, Path, $"{at}.parts", "a fixture needs at least one part or model.");
            for (int p = 0; p < fixture.Parts.Length; p++)
            {
                FixturePart part = fixture.Parts[p];
                Authored.Point(Path, $"{at}.parts[{p}].min", part.Min);
                Authored.Point(Path, $"{at}.parts[{p}].max", part.Max);
                Authored.Require(part.Max[0] > part.Min[0] && part.Max[1] > part.Min[1] && part.Max[2] > part.Min[2],
                    Path, $"{at}.parts[{p}].max", "must be larger than min on every axis.");
                Authored.Require(!part.Collider || (part.Solid && !part.Find), Path, $"{at}.parts[{p}].collider",
                    "a collider is solid and shows no find.");
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
            for (int m = 0; m < (fixture.Models ?? []).Length; m++)
            {
                FixtureModel model = fixture.Models![m];
                Authored.Require(model.Path.EndsWith(".glb", StringComparison.Ordinal), Path, $"{at}.models[{m}].path", "must be a GLB content path.");
                Authored.Point(Path, $"{at}.models[{m}].offset", model.Offset);
                Authored.Positive(Path, $"{at}.models[{m}].scale", model.Scale);
            }
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
    FixtureLight[]? Lights = null, Dictionary<string, float[]>? Sockets = null, FixtureModel[]? Models = null);

/// <summary>
/// An authored mesh (GLB content path) shown at <see cref="Offset"/> in the fixture's frame, turned with it. Presentation
/// only: a model never collides, so a fixture that blocks keeps a solid part for that.
/// </summary>
internal sealed record FixtureModel(string Path, float[] Offset, float Scale);

/// <param name="Find">The part shows a collectable find and disappears when it is taken.</param>
/// <param name="Collider">The part is solid but never drawn: the collision of a fixture whose look is a model.</param>
internal sealed record FixturePart(string Name, string Material, float[] Min, float[] Max, bool Solid = false, bool Find = false,
    bool Collider = false);
/// <param name="Shadow">The light casts shadows. Each shadowed point light renders the scene six more times a frame.</param>
internal sealed record FixtureLight(float[] Offset, float[] Color, float Intensity, float Range, bool Shadow);
