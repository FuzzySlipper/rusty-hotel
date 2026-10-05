using System.Numerics;
using Hotel.Game.Scene;
using Rusty.Engine;

namespace Hotel.Game.Spirits;

/// <summary>A small articulated moth: bell head, eyes, antennae and scalloped velvet wings.</summary>
internal sealed class SpiritView : IDisposable
{
    private readonly HotelScene scene;
    private readonly HotelSpirit spirit;
    private readonly List<MeshResource> meshes = [];
    private readonly List<Appearance> appearances = [];
    private readonly List<Part> parts = [];

    internal SpiritView(IEngineContext engine, HotelScene scene, HotelSpirit spirit)
    {
        this.scene = scene; this.spirit = spirit;
        try
        {
            MeshResource Box(string surface)
            {
                MeshResource mesh = RoomGeometry.Box(engine, scene.Surface(surface), new(-.5f), new(.5f), Vector2.One);
                meshes.Add(mesh); return mesh;
            }
            void Add(MeshResource mesh, Vector3 offset, Vector3 size, int wing = 0, float tilt = 0)
            {
                Appearance appearance = engine.Graphics.CreateMeshAppearance(mesh);
                appearances.Add(appearance);
                parts.Add(new(scene.Entities.Create().Value, appearance, offset, size, wing, tilt));
            }
            MeshResource velvet = Box("porter-coat"), brass = Box("brass"), eye = Box("tell-amber"), dark = Box("equipment");
            Add(velvet, new(0, -.05f, 0), new(.20f, .44f, .22f));
            // Stepped bell head and clapper give the resident its hotel-service silhouette.
            Add(brass, new(0, .22f, -.01f), new(.29f, .13f, .25f));
            Add(brass, new(0, .32f, 0), new(.21f, .13f, .20f));
            Add(brass, new(0, .40f, 0), new(.10f, .07f, .10f));
            Add(dark, new(0, .17f, -.04f), new(.25f, .035f, .23f));
            Add(brass, new(0, .10f, -.04f), new(.065f, .11f, .06f));
            foreach (int side in new[] { -1, 1 })
            {
                Add(dark, new(side * .085f, .28f, -.139f), new(.10f, .10f, .035f));
                Add(eye, new(side * .085f, .28f, -.162f), new(.06f, .06f, .022f));
                Add(brass, new(side * .16f, .48f, 0), new(.028f, .26f, .028f), tilt: -side * .55f);
                Add(brass, new(side * .23f, .59f, 0), new(.07f, .055f, .045f));
                Add(brass, new(side * .17f, -.16f, -.12f), new(.035f, .27f, .04f), tilt: side * .65f);
                MeshResource wing = Wing(engine, scene.Surface("porter-coat"), side);
                MeshResource inset = Wing(engine, scene.Surface("resident-ivory"), side);
                meshes.Add(wing); meshes.Add(inset);
                Add(wing, new(side * .08f, .06f, .02f), Vector3.One, side);
                Add(inset, new(side * .11f, .08f, -.045f), new(.77f, .73f, .7f), side);
                Add(wing, new(side * .08f, -.14f, .02f), new(.70f, -.73f, 1), side);
                // Paired eye spots sit on the wing, rather than a screen-space flash.
                Add(dark, new(side * .43f, .23f, -.095f), new(.15f, .17f, .025f), side);
                Add(brass, new(side * .43f, .23f, -.113f), new(.075f, .09f, .02f), side);
            }
        }
        catch { Dispose(); throw; }
    }

    private static MeshResource Wing(IEngineContext engine, Material surface, int side)
    {
        Vector2[] outline = [new(0, 0), new(.22f, .43f), new(.62f, .50f), new(.83f, .24f),
            new(.68f, .09f), new(.64f, -.08f), new(.27f, -.14f)];
        Vector3[] points = outline.Select(p => new Vector3(side * p.X, p.Y, -.018f))
            .Concat(outline.Select(p => new Vector3(side * p.X, p.Y, .018f))).ToArray();
        List<(int, int, int)> faces = [];
        for (int i = 1; i < outline.Length - 1; i++) { faces.Add((0, i, i + 1)); faces.Add((7, i + 8, i + 7)); }
        for (int i = 0; i < outline.Length; i++)
        { int next = (i + 1) % 7; faces.Add((i, i + 7, next)); faces.Add((next, i + 7, next + 7)); }
        if (side > 0) faces = faces.Select(f => (f.Item1, f.Item3, f.Item2)).ToList();
        return RoomGeometry.Mesh(engine, points, faces.ToArray(), surface, Vector2.One);
    }

    internal void Publish()
    {
        bool visible = !spirit.Acquired || spirit.Active;
        float scale = .65f;
        Vector3 origin = spirit.Bell;
        Quaternion facing = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
        float flap = .22f + MathF.Sin(spirit.IdleTime * 7) * .25f;
        if (spirit.Active)
        {
            facing = spirit.Facing;
            float arrival = Math.Clamp(spirit.Elapsed / spirit.Definition.Arrival, 0, 1);
            origin = Vector3.Lerp(spirit.Entrance, spirit.Destination, arrival);
            scale = .65f * (.25f + .75f * arrival);
            flap = spirit.Phase == ManifestationPhase.Holding ? .15f + MathF.Sin(spirit.Elapsed * 12) * .22f : .8f * MathF.Sin(spirit.Elapsed * 24);
            if (spirit.Phase == ManifestationPhase.Departing)
            {
                float leave = (spirit.Elapsed - spirit.Definition.Arrival - spirit.Definition.Hold) / spirit.Definition.Departure;
                origin.Y += leave * .45f;
                scale *= 1 - leave;
                flap = leave * 1.45f;
            }
        }
        else origin.Y += MathF.Sin(spirit.IdleTime * 2) * .045f;
        List<AppearanceFact> facts = [];
        foreach (Part part in parts)
        {
            Quaternion local = Quaternion.CreateFromAxisAngle(Vector3.UnitY, part.Wing * flap)
                * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, part.Tilt);
            Vector3 offset = part.Wing == 0 ? part.Offset : Vector3.Transform(part.Offset, local);
            facts.Add(new(part.Entity, false, 0, new(origin + Vector3.Transform(offset * scale, facing),
                facing * local, part.Size * Math.Max(.001f, scale)), part.Appearance, visible, RenderLayer.Scene));
        }
        scene.PublishSpirit(facts.ToArray());
    }
    public void Dispose()
    {
        scene.PublishSpirit([]);
        foreach (Appearance appearance in appearances) appearance.Dispose();
        foreach (MeshResource mesh in meshes) mesh.Dispose();
    }
    private sealed record Part(ulong Entity, Appearance Appearance, Vector3 Offset, Vector3 Size, int Wing, float Tilt);
}
