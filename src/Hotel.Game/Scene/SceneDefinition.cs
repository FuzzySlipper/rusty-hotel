using Hotel.Game.Content;
using Rusty.Engine;

namespace Hotel.Game.Scene;

/// <summary>Reusable hotel surfaces: wallpaper, carpet, wood, paint and trim.</summary>
internal sealed record SurfaceCatalog(SurfaceDefinition[] Surfaces)
{
    internal const string Path = "scene/surfaces.json";
    internal static SurfaceCatalog Load(IEngineContext engine)
    {
        SurfaceCatalog catalog = Authored.Read(engine, Path, ContentJson.Default.SurfaceCatalog);
        for (int i = 0; i < catalog.Surfaces.Length; i++)
        {
            SurfaceDefinition s = catalog.Surfaces[i];
            Authored.Colour(Path, $"surfaces[{i}].color", s.Color);
            Authored.Positive(Path, $"surfaces[{i}].tileWidth", s.TileWidth);
            Authored.Positive(Path, $"surfaces[{i}].tileHeight", s.TileHeight);
            Authored.Within(Path, $"surfaces[{i}].roughness", s.Roughness, 0, 1);
            Authored.AtLeast(Path, $"surfaces[{i}].emission", s.Emission, 0);
        }
        return catalog;
    }
}
internal sealed record SurfaceDefinition(string Id, float[] Color, string? Texture = null,
    float TileWidth = 1, float TileHeight = 1, float Roughness = .9f, float Emission = 0);

/// <summary>One excursion's static boxes, props and lights.</summary>
internal sealed record ExcursionGeometry(RoomBox[] Boxes, ModelDefinition[] Models, LightingDefinition Lighting)
{
    internal void Validate(string path)
    {
        for (int i = 0; i < Boxes.Length; i++)
        {
            RoomBox box = Boxes[i];
            Authored.Point(path, $"boxes[{i}].min", box.Min);
            Authored.Point(path, $"boxes[{i}].max", box.Max);
            Authored.Require(box.Max[0] > box.Min[0] && box.Max[1] > box.Min[1] && box.Max[2] > box.Min[2], path, $"boxes[{i}].max",
                $"box '{box.Name}' must be larger than its min on every axis.");
        }
        for (int i = 0; i < Models.Length; i++)
        {
            Authored.Point(path, $"models[{i}].position", Models[i].Position);
            Authored.Positive(path, $"models[{i}].scale", Models[i].Scale);
            Authored.Finite(path, $"models[{i}].yawDegrees", Models[i].YawDegrees);
        }
        Authored.Colour(path, "lighting.ambientColor", Lighting.AmbientColor);
        Authored.AtLeast(path, "lighting.ambientIntensity", Lighting.AmbientIntensity, 0);
        for (int i = 0; i < Lighting.Points.Length; i++)
        {
            PointLightDefinition light = Lighting.Points[i];
            Authored.Point(path, $"lighting.points[{i}].position", light.Position);
            Authored.Colour(path, $"lighting.points[{i}].color", light.Color);
            Authored.AtLeast(path, $"lighting.points[{i}].intensity", light.Intensity, 0);
            Authored.Positive(path, $"lighting.points[{i}].range", light.Range);
        }
    }
}
internal sealed record RoomBox(string Name, float[] Min, float[] Max, string Material, bool Solid = true, string? Find = null);
internal sealed record LightingDefinition(float[] AmbientColor, float AmbientIntensity, PointLightDefinition[] Points);
internal sealed record PointLightDefinition(float[] Position, float[] Color, float Intensity, float Range);
internal sealed record ModelDefinition(string Path, float[] Position, float Scale, float YawDegrees);
