using System.Numerics;
using Hotel.Game.Content;
using Hotel.Game.Route;
using Rusty.Engine;
using Rusty.Engine.Entities;

namespace Hotel.Game.Scene;

internal sealed class HotelScene : IDisposable
{
    private readonly IEngineContext engine;
    private readonly List<Material> materials = [];
    private readonly Dictionary<string, Material> surfaces = new(StringComparer.Ordinal);
    private readonly List<MeshResource> meshes = [];
    private readonly List<Appearance> appearances = [];
    private readonly List<RenderResource> textures = [];
    private readonly List<RenderResource> models = [];
    private readonly List<Light> lights = [];
    private readonly List<StaticMeshAsset> assets = [];
    private readonly List<StaticMeshInstance> instances = [];
    private readonly Dictionary<string, DoorView> doors = new(StringComparer.Ordinal);
    private AppearanceFact[] facts = [];
    private AppearanceFact[] combatFacts = [];
    private AppearanceFact[] spiritFacts = [];
    private readonly Dictionary<string, List<int>> findFacts = new(StringComparer.Ordinal);

    internal HotelScene(IEngineContext engine, SurfaceDefinition[] surfaceDefinitions, ExcursionGeometry geometry, DoorDefinition[] doorDefinitions)
    {
        this.engine = engine;
        Entities = new EntityStore([EngineComponentTypes.Transform, EngineComponentTypes.CharacterMotion]);
        PlayerEntity = Entities.Create();
        try { Session = engine.Spatial.CreateSession(new SpatialSessionConfig(.25f, 16, VoxelSurfaceMode.GreedyCubes)); }
        catch { Entities.Dispose(); throw; }
        try
        {
            List<AppearanceFact> placed = [];
            Dictionary<string, RenderResourceReference> texturePaths = new(StringComparer.Ordinal);
            foreach (SurfaceDefinition surface in surfaceDefinitions)
            {
                Vector3 rgb = Authored.Vector(surface.Color);
                RenderResourceReference texture = default;
                if (surface.Texture is string path)
                {
                    if (!texturePaths.TryGetValue(path, out texture))
                    {
                        using ContentReference content = engine.Content.OpenReference(new(path));
                        RenderResourceInfo resource = engine.Graphics.OpenResourceFromContent(new(content, TextureFilter.Linear, TextureWrap.Repeat));
                        textures.Add(resource.Handle);
                        texture = new RenderResourceReference(resource.Handle.Handle.Value);
                        texturePaths.Add(path, texture);
                    }
                }
                Material material = engine.Graphics.CreateMaterial(new MaterialRequest(
                    new Color(rgb.X, rgb.Y, rgb.Z, 1), texture, surface.Roughness, new Color(1, 1, 1, 1), rgb, surface.Emission, false));
                materials.Add(material);
                surfaces.Add(surface.Id, material);
            }
            foreach (RoomBox box in geometry.Boxes)
            {
                // Shape and size were validated when the geometry file loaded.
                Vector3 min = Authored.Vector(box.Min), max = Authored.Vector(box.Max);
                SurfaceDefinition surface = surfaceDefinitions.First(surface => surface.Id == box.Material);
                MeshResource mesh = RoomGeometry.Box(engine, surfaces[box.Material], min, max, new(surface.TileWidth, surface.TileHeight));
                meshes.Add(mesh);
                Appearance appearance = engine.Graphics.CreateMeshAppearance(mesh);
                appearances.Add(appearance);
                EntityId entity = Entities.Create();
                Transform pose = new(Vector3.Zero, Quaternion.Identity, Vector3.One);
                if (box.Find is string find)
                {
                    if (!findFacts.TryGetValue(find, out List<int>? indices)) findFacts.Add(find, indices = []);
                    indices.Add(placed.Count);
                }
                placed.Add(new AppearanceFact(entity.Value, false, 0, pose, appearance, true, RenderLayer.Scene));
                if (box.Solid)
                {
                    ulong asset = checked((ulong)assets.Count + 1);
                    assets.Add(new StaticMeshAsset(asset, new MeshResourceReference(mesh), 0, 0, 0, 0));
                    instances.Add(new StaticMeshInstance(entity.Value, asset, pose));
                }
            }
            foreach (ModelDefinition model in geometry.Models)
            {
                using ContentReference content = engine.Content.OpenReference(new(model.Path));
                // The pinned SDK admits GLB through Animation even for a static, unrigged prop.
                RenderResource resource = engine.Animation.OpenAnimatedMeshFromContent(new(content));
                models.Add(resource);
                Appearance appearance = engine.Animation.CreateAnimatedMeshAppearance(new(resource));
                appearances.Add(appearance);
                EntityId entity = Entities.Create();
                placed.Add(new AppearanceFact(entity.Value, false, 0,
                    new Transform(Authored.Vector(model.Position), Quaternion.CreateFromAxisAngle(Vector3.UnitY, model.YawDegrees * MathF.PI / 180), new Vector3(model.Scale)),
                    appearance, true, RenderLayer.Scene));
            }
            foreach (DoorDefinition door in doorDefinitions)
            {
                EntityId entity = Entities.Create();
                Transform pose = DoorPose(door, false);
                int first = placed.Count;
                MeshResource leaf = RoomGeometry.Box(engine, surfaces[door.Material], Vector3.Zero,
                    new(door.Width, door.Height, door.Thickness), Vector2.One);
                meshes.Add(leaf);
                Appearance appearance = engine.Graphics.CreateMeshAppearance(leaf);
                appearances.Add(appearance);
                placed.Add(new(entity.Value, false, 0, pose, appearance, true, RenderLayer.Scene));
                // Both handles use the same door pose; only the leaf is collision geometry.
                MeshResource handle = RoomGeometry.Box(engine, surfaces[door.HandleMaterial], new(door.Width - .25f, .97f, -.055f),
                    new(door.Width - .1f, 1.04f, door.Thickness + .055f), Vector2.One);
                meshes.Add(handle);
                Appearance handleAppearance = engine.Graphics.CreateMeshAppearance(handle);
                appearances.Add(handleAppearance);
                placed.Add(new(Entities.Create().Value, false, 0, pose, handleAppearance, true, RenderLayer.Scene));
                ulong asset = checked((ulong)assets.Count + 1);
                assets.Add(new(asset, new MeshResourceReference(leaf), 0, 0, 0, 0));
                int collider = instances.Count;
                instances.Add(new(entity.Value, asset, pose));
                doors.Add(door.Id, new(door, entity.Value, first, collider, asset));
            }
            LightingDefinition lighting = geometry.Lighting;
            lights.Add(engine.Graphics.CreateLight(new(1, false, 0, new(LightKind.Ambient,
                Authored.Vector(lighting.AmbientColor), lighting.AmbientIntensity, true,
                Vector3.Zero, -Vector3.UnitY, false, 0, 0, 0, 0, LightShadowIntent.Disabled))));
            foreach (PointLightDefinition light in lighting.Points)
                lights.Add(engine.Graphics.CreateLight(new((ulong)lights.Count + 1, false, 0,
                    new(LightKind.Point, Authored.Vector(light.Color), light.Intensity, true,
                    Authored.Vector(light.Position), -Vector3.UnitY, true, light.Range, 2, 0, 0, LightShadowIntent.Requested))));
            ReplaceCollision();
            facts = placed.ToArray();
        }
        catch { Dispose(); throw; }
    }

    internal EntityStore Entities { get; }
    internal EntityId PlayerEntity { get; }
    internal SpatialSession Session { get; }
    internal Material Surface(string id) => surfaces[id];
    internal void Publish() => engine.Graphics.PublishSnapshot([.. facts, .. combatFacts, .. spiritFacts]);
    internal void PublishSpirit(AppearanceFact[] appearances) { spiritFacts = appearances; Publish(); }
    internal void PublishCombat(AppearanceFact[] appearances) { combatFacts = appearances; Publish(); }
    internal void ShowFind(string id, bool visible)
    {
        if (findFacts.TryGetValue(id, out List<int>? indices))
            foreach (int index in indices) facts[index] = facts[index] with { Visible = visible };
        Publish();
    }

    internal ulong DoorEntity(string id) => doors[id].Entity;

    // Route policy owns open/closed state. Visuals and collision receive the same authored pose.
    internal void PlaceDoor(string id, bool open)
    {
        DoorView view = doors[id];
        Transform pose = DoorPose(view.Definition, open);
        for (int i = view.FirstFact; i < view.FirstFact + 2; i++) facts[i] = facts[i] with { Transform = pose };
        instances[view.Collider] = new(view.Entity, view.Asset, pose);
        ReplaceCollision();
        Publish();
    }

    private static Transform DoorPose(DoorDefinition door, bool open) => new(Authored.Vector(door.Hinge),
        Quaternion.CreateFromAxisAngle(Vector3.UnitY, (open ? door.OpenYaw : door.ClosedYaw) * MathF.PI / 180), Vector3.One);
    private void ReplaceCollision() => engine.Spatial.ReplaceCollision(new(Session, assets.ToArray(),
        ReadOnlyMemory<Vector3>.Empty, ReadOnlyMemory<Triangle>.Empty, instances.ToArray()));
    private sealed record DoorView(DoorDefinition Definition, ulong Entity, int FirstFact, int Collider, ulong Asset);

    public void Dispose()
    {
        engine.Graphics.PublishSnapshot([]);
        // Collision retains the mesh resources until its session is released.
        Session.Dispose();
        foreach (Light light in lights) light.Dispose();
        foreach (Appearance appearance in appearances) appearance.Dispose();
        foreach (MeshResource mesh in meshes) mesh.Dispose();
        foreach (Material material in materials) material.Dispose();
        foreach (RenderResource model in models) model.Dispose();
        foreach (RenderResource texture in textures) texture.Dispose();
        Entities.Dispose();
    }
}
