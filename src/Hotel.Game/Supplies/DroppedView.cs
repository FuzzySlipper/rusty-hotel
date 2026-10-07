using System.Numerics;
using Hotel.Game.Scene;
using Rusty.Engine;

namespace Hotel.Game.Supplies;

/// <summary>
/// The stacks left on the floor, each drawn as the one dropped bag whatever it holds, lying where it was set down and
/// turned the way the investigator faced. One appearance per slot up to the floor's limit; empty slots are hidden.
/// </summary>
internal sealed class DroppedView : IDisposable
{
    private readonly HotelScene scene;
    private readonly HotelSupplies supplies;
    private readonly RenderResource model;
    private readonly List<(ulong Entity, Appearance Appearance)> bags = [];

    internal DroppedView(IEngineContext engine, HotelScene scene, HotelSupplies supplies)
    {
        this.scene = scene; this.supplies = supplies;
        DroppingTuning tuning = supplies.Definition.Dropping;
        using (ContentReference content = engine.Content.OpenReference(new(tuning.Model)))
            // The pinned SDK admits GLB through Animation even for a static, unrigged prop.
            model = engine.Animation.OpenAnimatedMeshFromContent(new(content));
        try
        {
            for (int i = 0; i < tuning.Limit; i++)
                bags.Add((scene.Entities.Create().Value, engine.Animation.CreateAnimatedMeshAppearance(new(model))));
        }
        catch { Dispose(); throw; }
    }

    internal void Publish()
    {
        IReadOnlyList<DroppedStack> lying = supplies.Dropped;
        float scale = supplies.Definition.Dropping.Scale;
        AppearanceFact[] facts = new AppearanceFact[bags.Count];
        for (int i = 0; i < bags.Count; i++)
        {
            DroppedStack? stack = i < lying.Count ? lying[i] : null;
            Transform pose = stack is null ? new(Vector3.Zero, Quaternion.Identity, Vector3.One)
                : new(new(stack.X, stack.Y, stack.Z), Quaternion.CreateFromAxisAngle(Vector3.UnitY, -stack.Yaw), new(scale));
            facts[i] = new(bags[i].Entity, false, 0, pose, bags[i].Appearance, stack is not null, RenderLayer.Scene);
        }
        scene.PublishDropped(facts);
    }

    public void Dispose()
    {
        scene.PublishDropped([]);
        foreach (var (_, appearance) in bags) appearance.Dispose();
        model.Dispose();
    }
}
