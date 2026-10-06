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
    // The floor's point lights with their logical ids, and which of them cast shadows now.
    private readonly List<(Light Light, ulong Id, PointLightDefinition Definition)> points = [];
    private readonly HashSet<ulong> casting = [];

    /// <summary>The floor's point lights casting shadows now, as their positions.</summary>
    internal Vector3[] CastingLights => points.Where(p => casting.Contains(p.Id)).Select(p => Authored.Vector(p.Definition.Position)).ToArray();
    // Each light's intensity as last sent, and the scene's admitted time for flickering lamps.
    private readonly Dictionary<ulong, float> sent = [];
    private double elapsed;
    private readonly List<StaticMeshAsset> assets = [];
    private readonly List<StaticMeshInstance> instances = [];
    private readonly Dictionary<string, DoorView> doors = new(StringComparer.Ordinal);
    private AppearanceFact[] facts = [];
    private AppearanceFact[] combatFacts = [];
    private AppearanceFact[] spiritFacts = [];
    private readonly Dictionary<string, List<int>> findFacts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SurfaceDefinition> surfaceDefinitions = new(StringComparer.Ordinal);
    private Preview? preview;

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
                RenderResourceReference Texture(string path, TextureColorSpace space)
                {
                    if (texturePaths.TryGetValue(path, out RenderResourceReference known)) return known;
                    using ContentReference content = engine.Content.OpenReference(new(path));
                    RenderResourceInfo resource = engine.Graphics.OpenResourceFromContent(new(content, TextureFilter.Linear, TextureWrap.Repeat, space));
                    textures.Add(resource.Handle);
                    RenderResourceReference opened = new(resource.Handle.Handle.Value);
                    texturePaths.Add(path, opened);
                    return opened;
                }
                RenderResourceReference texture = surface.Texture is string path ? Texture(path, TextureColorSpace.Srgb) : default;
                RenderResourceReference normal = surface.NormalMap is string map ? Texture(map, TextureColorSpace.Linear) : default;
                Material material = engine.Graphics.CreateMaterial(new MaterialRequest(
                    new Color(rgb.X, rgb.Y, rgb.Z, 1), texture, surface.Roughness, new Color(1, 1, 1, 1), surface.Emission > 0 ? rgb : Vector3.Zero,
                    surface.Emission, false, MaterialAlphaMode.Opaque, 0, 0, normal, surface.NormalScale ?? 0));
                materials.Add(material);
                surfaces.Add(surface.Id, material);
                this.surfaceDefinitions.Add(surface.Id, surface);
            }
            AddBoxes(geometry.Boxes, geometry.Mouldings, 0, meshes, appearances, placed, assets, instances, findFacts);
            Dictionary<string, RenderResource> opened = new(StringComparer.Ordinal);
            foreach (ModelDefinition model in geometry.Models)
            {
                if (!opened.TryGetValue(model.Path, out RenderResource? resource))
                {
                    using ContentReference content = engine.Content.OpenReference(new(model.Path));
                    // The pinned SDK admits GLB through Animation even for a static, unrigged prop.
                    resource = engine.Animation.OpenAnimatedMeshFromContent(new(content));
                    models.Add(resource);
                    opened.Add(model.Path, resource);
                }
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
            lights.AddRange(PointLights(lighting.Points, 2, points));
            ReplaceCollision();
            facts = placed.ToArray();
        }
        catch { Dispose(); throw; }
    }

    // Static geometry is drawn in batches: one mesh per surface per square of this many metres, so the shell of a whole
    // generated floor is a few dozen draws (and a few dozen per shadow face) rather than one per box.
    private const float BatchCell = 8;

    // Meshes, appearances and solid collision for authored boxes and mouldings. Shape and size were validated when the
    // floor built. Collision keeps one mesh per solid box; a box that shows a find is drawn alone so it can be hidden.
    private void AddBoxes(RoomBox[] boxes, Moulding[] mouldings, ulong assetBase, List<MeshResource> meshList, List<Appearance> appearanceList,
        List<AppearanceFact> placed, List<StaticMeshAsset> assetList, List<StaticMeshInstance> instanceList, Dictionary<string, List<int>>? finds)
    {
        Dictionary<(string Material, int X, int Z), MeshBatch> batches = [];
        MeshBatch Batch(string material, Vector3 at)
        {
            var key = (material, (int)MathF.Floor(at.X / BatchCell), (int)MathF.Floor(at.Z / BatchCell));
            if (!batches.TryGetValue(key, out MeshBatch? batch)) batches.Add(key, batch = new());
            return batch;
        }
        Transform pose = new(Vector3.Zero, Quaternion.Identity, Vector3.One);
        void Show(MeshResource mesh, List<int>? indices)
        {
            Appearance appearance = engine.Graphics.CreateMeshAppearance(mesh);
            appearanceList.Add(appearance);
            indices?.Add(placed.Count);
            placed.Add(new AppearanceFact(Entities.Create().Value, false, 0, pose, appearance, true, RenderLayer.Scene));
        }
        foreach (RoomBox box in boxes)
        {
            Vector3 min = Authored.Vector(box.Min), max = Authored.Vector(box.Max);
            SurfaceDefinition surface = surfaceDefinitions[box.Material];
            Vector2 tile = new(surface.TileWidth, surface.TileHeight);
            MeshResource? solid = null;
            if (box.Solid)
            {
                solid = RoomGeometry.Box(engine, surfaces[box.Material], min, max, tile);
                meshList.Add(solid);
                ulong asset = checked(assetBase + (ulong)assetList.Count + 1);
                assetList.Add(new StaticMeshAsset(asset, new MeshResourceReference(solid), 0, 0, 0, 0));
                instanceList.Add(new StaticMeshInstance(Entities.Create().Value, asset, pose));
            }
            if (box.Hidden) continue;
            if (finds is not null && box.Find is string find)
            {
                if (!finds.TryGetValue(find, out List<int>? indices)) finds.Add(find, indices = []);
                MeshResource mesh = solid ?? RoomGeometry.Box(engine, surfaces[box.Material], min, max, tile);
                if (solid is null) meshList.Add(mesh);
                Show(mesh, indices);
                continue;
            }
            Batch(box.Material, (min + max) / 2).Box(min, max, tile);
        }
        foreach (Moulding moulding in mouldings)
            Batch(moulding.Material, Authored.Vector(moulding.Start)).Moulding(moulding);
        foreach (var ((material, _, _), batch) in batches)
        {
            MeshResource mesh = batch.Create(engine, surfaces[material]);
            meshList.Add(mesh);
            Show(mesh, null);
        }
    }

    private List<Light> PointLights(PointLightDefinition[] definitions, ulong firstId,
        List<(Light Light, ulong Id, PointLightDefinition Definition)>? kept = null)
    {
        List<Light> created = [];
        ulong id = firstId;
        foreach (PointLightDefinition light in definitions)
        {
            // Shadows start off; CastShadowsNear grants them to the nearest shadowed lights.
            Light made = engine.Graphics.CreateLight(new(id, false, 0, PointLight(light, light.Intensity, kept is null && light.Shadow)));
            if (kept is not null) sent[id] = light.Intensity;
            created.Add(made);
            kept?.Add((made, id, light));
            id++;
        }
        return created;
    }

    private static LightDescriptor PointLight(PointLightDefinition light, float intensity, bool shadow) =>
        new(LightKind.Point, Authored.Vector(light.Color), intensity, true, Authored.Vector(light.Position), -Vector3.UnitY,
            true, light.Range, 2, 0, 0, shadow ? LightShadowIntent.Requested : LightShadowIntent.Disabled);

    /// <summary>
    /// Grants shadows to the shadowed lights best placed for <paramref name="eye"/>, within the focus's budget: lamps in
    /// the eye's own room first, then by distance, keeping the lamps already casting unless another is clearly better.
    /// </summary>
    internal void CastShadowsNear(Vector3 eye, RoomDefinition[] rooms, ShadowFocus focus)
    {
        RoomDefinition? Room(Vector3 p) => rooms.FirstOrDefault(r => p.X >= r.Min[0] && p.X <= r.Max[0] && p.Z >= r.Min[2] && p.Z <= r.Max[2]);
        RoomDefinition? here = Room(eye);
        float Score((Light Light, ulong Id, PointLightDefinition Definition) p)
        {
            Vector3 at = Authored.Vector(p.Definition.Position);
            float score = Vector3.Distance(at, eye);
            if (Room(at) != here) score += focus.OtherRoomPenalty;
            if (casting.Contains(p.Id)) score -= focus.Hysteresis;
            return score;
        }
        HashSet<ulong> nearest = points.Where(p => p.Definition.Shadow).OrderBy(Score).ThenBy(p => p.Id)
            .Take(focus.Budget).Select(p => p.Id).ToHashSet();
        if (nearest.SetEquals(casting)) return;
        foreach (var (light, id, definition) in points)
            if (nearest.Contains(id) != casting.Contains(id))
                engine.Graphics.UpdateLight(new(light, new(id, false, 0, PointLight(definition, sent[id], nearest.Contains(id)))));
        casting.Clear();
        casting.UnionWith(nearest);
    }

    /// <summary>Advances failing lamps by one admitted step, sending a light only when it visibly changes.</summary>
    internal void Animate(float delta)
    {
        elapsed += delta;
        foreach (var (light, id, definition) in points)
        {
            if (definition.Flicker is not { } flicker) continue;
            float intensity = definition.Intensity * (1 - flicker.Depth * Sag(elapsed / flicker.Seconds, id));
            if (MathF.Abs(intensity - sent[id]) < definition.Intensity * .01f) continue;
            sent[id] = intensity;
            engine.Graphics.UpdateLight(new(light, new(id, false, 0, PointLight(definition, intensity, casting.Contains(id)))));
        }
    }

    // A sparse sag in [0, 1]: smooth value noise over moments, near zero most of the time.
    private static float Sag(double moment, ulong seed)
    {
        static float Hash(long cell, ulong seed)
        {
            ulong h = (ulong)cell * 0x9E3779B97F4A7C15UL ^ seed * 0xC2B2AE3D27D4EB4FUL;
            h ^= h >> 31; h *= 0xBF58476D1CE4E5B9UL; h ^= h >> 29;
            return (h >> 40) / (float)(1UL << 24);
        }
        long cell = (long)Math.Floor(moment);
        float t = (float)(moment - cell), s = t * t * (3 - 2 * t);
        float value = Hash(cell, seed) * (1 - s) + Hash(cell + 1, seed) * s;
        float x = Math.Clamp((value - .72f) / .2f, 0, 1);
        return x * x * (3 - 2 * x);
    }

    /// <summary>
    /// Developer viewing: shows one extra built floor beside the hotel, with its lights and collision, replacing any
    /// previous one. It is presentation for inspection only; no domain owner reads it.
    /// </summary>
    internal void ShowPreview(RoomBox[] boxes, PointLightDefinition[] points)
    {
        ClearPreview();
        Preview shown = new();
        preview = shown;
        List<AppearanceFact> placed = [];
        AddBoxes(boxes, [], PreviewIds, shown.Meshes, shown.Appearances, placed, shown.Assets, shown.Instances, null);
        shown.Facts = [.. placed];
        shown.Lights.AddRange(PointLights(points, PreviewIds));
        ReplaceCollision();
        Publish();
    }

    internal void ClearPreview()
    {
        if (preview is not { } shown) return;
        preview = null;
        ReplaceCollision();
        Publish();
        shown.Dispose();
    }

    // Preview collision assets and lights are numbered apart from the hotel's own.
    private const ulong PreviewIds = 1UL << 32;

    private sealed class Preview : IDisposable
    {
        internal List<MeshResource> Meshes { get; } = [];
        internal List<Appearance> Appearances { get; } = [];
        internal List<StaticMeshAsset> Assets { get; } = [];
        internal List<StaticMeshInstance> Instances { get; } = [];
        internal List<Light> Lights { get; } = [];
        internal AppearanceFact[] Facts { get; set; } = [];

        public void Dispose()
        {
            foreach (Light light in Lights) light.Dispose();
            foreach (Appearance appearance in Appearances) appearance.Dispose();
            foreach (MeshResource mesh in Meshes) mesh.Dispose();
        }
    }

    internal EntityStore Entities { get; }
    internal EntityId PlayerEntity { get; }
    internal SpatialSession Session { get; }
    internal Material Surface(string id) => surfaces[id];
    internal void Publish() => engine.Graphics.PublishSnapshot([.. facts, .. preview?.Facts ?? [], .. combatFacts, .. spiritFacts]);
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
    private void ReplaceCollision() => engine.Spatial.ReplaceCollision(new(Session, assets.Concat(preview?.Assets ?? []).ToArray(),
        ReadOnlyMemory<Vector3>.Empty, ReadOnlyMemory<Triangle>.Empty, instances.Concat(preview?.Instances ?? []).ToArray()));
    private sealed record DoorView(DoorDefinition Definition, ulong Entity, int FirstFact, int Collider, ulong Asset);

    public void Dispose()
    {
        engine.Graphics.PublishSnapshot([]);
        // Collision retains the mesh resources until its session is released.
        Session.Dispose();
        preview?.Dispose();
        foreach (Light light in lights) light.Dispose();
        foreach (Appearance appearance in appearances) appearance.Dispose();
        foreach (MeshResource mesh in meshes) mesh.Dispose();
        foreach (Material material in materials) material.Dispose();
        foreach (RenderResource model in models) model.Dispose();
        foreach (RenderResource texture in textures) texture.Dispose();
        Entities.Dispose();
    }
}
