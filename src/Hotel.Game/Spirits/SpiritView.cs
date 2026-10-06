using System.Numerics;
using Hotel.Game.Content;
using Hotel.Game.Scene;
using Rusty.Engine;

namespace Hotel.Game.Spirits;

/// <summary>
/// The spirits as small articulated moths, one built for each spirit of the roster from its look: a body model and two
/// wing models that flap about their hinges. A spirit is drawn idle at its bell until it is freed, and at its visit when
/// called.
/// </summary>
internal sealed class SpiritView : IDisposable
{
    private readonly HotelScene scene;
    private readonly HotelSpirit spirit;
    private readonly Dictionary<string, RenderResource> models = new(StringComparer.Ordinal);
    private readonly List<Appearance> appearances = [];
    private readonly Dictionary<string, List<Part>> creatures = new(StringComparer.Ordinal);

    internal SpiritView(IEngineContext engine, HotelScene scene, HotelSpirit spirit)
    {
        this.scene = scene; this.spirit = spirit;
        try
        {
            foreach (SpiritDefinition definition in spirit.Roster) creatures[definition.Id] = Build(engine, definition.Look);
        }
        catch { Dispose(); throw; }
    }

    private List<Part> Build(IEngineContext engine, SpiritLook look)
    {
        List<Part> parts = [];
        void Add(string path, Vector3 hinge, int wing)
        {
            if (!models.TryGetValue(path, out RenderResource? model))
            {
                using ContentReference content = engine.Content.OpenReference(new(path));
                // The pinned SDK admits GLB through Animation even for a static, unrigged prop.
                model = engine.Animation.OpenAnimatedMeshFromContent(new(content));
                models.Add(path, model);
            }
            Appearance appearance = engine.Animation.CreateAnimatedMeshAppearance(new(model));
            appearances.Add(appearance);
            parts.Add(new(scene.Entities.Create().Value, appearance, hinge, wing));
        }
        Add(look.Body, Vector3.Zero, 0);
        // Left, then right: each wing turns about its own hinge, mirrored.
        Add(look.Wings[0], Authored.Vector(look.Hinges[0]), -1);
        Add(look.Wings[1], Authored.Vector(look.Hinges[1]), 1);
        return parts;
    }

    internal void Publish()
    {
        List<AppearanceFact> facts = [];
        foreach (SpiritDefinition definition in spirit.Roster)
        {
            SpiritBellPlacement? bell = spirit.Bells.FirstOrDefault(b => b.Spirit == definition.Id);
            bool visiting = spirit.Visiting == definition;
            bool idle = !visiting && bell is not null && !spirit.Acquired(definition.Id);
            float scale = definition.Look.Scale;
            Vector3 origin = bell is null ? Vector3.Zero : Authored.Vector(bell.Point);
            Quaternion facing = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
            float flap = .22f + MathF.Sin(spirit.IdleTime * 7) * .25f;
            if (visiting)
            {
                facing = spirit.Facing;
                float arrival = Math.Clamp(spirit.Elapsed / definition.Arrival, 0, 1);
                origin = Vector3.Lerp(spirit.Entrance, spirit.Destination, arrival);
                scale *= .25f + .75f * arrival;
                flap = spirit.Phase == ManifestationPhase.Holding ? .15f + MathF.Sin(spirit.Elapsed * 12) * .22f : .8f * MathF.Sin(spirit.Elapsed * 24);
                if (spirit.Phase == ManifestationPhase.Departing)
                {
                    float leave = (spirit.Elapsed - definition.Arrival - definition.Hold) / definition.Departure;
                    origin.Y += leave * .45f;
                    scale *= 1 - leave;
                    flap = leave * 1.45f;
                }
            }
            else origin.Y += MathF.Sin(spirit.IdleTime * 2) * .045f;
            foreach (Part part in creatures[definition.Id])
            {
                Quaternion local = Quaternion.CreateFromAxisAngle(Vector3.UnitY, part.Wing * flap);
                facts.Add(new(part.Entity, false, 0, new(origin + Vector3.Transform(part.Hinge * scale, facing),
                    facing * local, new(Math.Max(.001f, scale))), part.Appearance, visiting || idle, RenderLayer.Scene));
            }
        }
        scene.PublishSpirit(facts.ToArray());
    }
    public void Dispose()
    {
        scene.PublishSpirit([]);
        foreach (Appearance appearance in appearances) appearance.Dispose();
        foreach (RenderResource model in models.Values) model.Dispose();
    }
    private sealed record Part(ulong Entity, Appearance Appearance, Vector3 Hinge, int Wing);
}
