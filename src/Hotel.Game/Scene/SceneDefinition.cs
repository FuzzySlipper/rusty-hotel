using Hotel.Game.Content;
using Rusty.Engine;

namespace Hotel.Game.Scene;

/// <summary>Reusable hotel surfaces: wallpaper, carpet, wood, paint and trim.</summary>
internal sealed record SurfaceCatalog(SurfaceDefinition[] Surfaces)
{
    internal const string Path = "scene/surfaces.json";
    internal static SurfaceCatalog Load(IEngineContext engine) => Authored.Read(engine, Path, ContentJson.Default.SurfaceCatalog);
}
internal sealed record SurfaceDefinition(string Id, float[] Color, string? Texture = null,
    float TileWidth = 1, float TileHeight = 1, float Roughness = .9f, float Emission = 0);

/// <summary>One excursion's static boxes, props and lights.</summary>
internal sealed record ExcursionGeometry(RoomBox[] Boxes, ModelDefinition[] Models, LightingDefinition Lighting);
internal sealed record RoomBox(string Name, float[] Min, float[] Max, string Material, bool Solid = true, string? Find = null);
internal sealed record LightingDefinition(float[] AmbientColor, float AmbientIntensity, PointLightDefinition[] Points);
internal sealed record PointLightDefinition(float[] Position, float[] Color, float Intensity, float Range);
internal sealed record ModelDefinition(string Path, float[] Position, float Scale, float YawDegrees);
