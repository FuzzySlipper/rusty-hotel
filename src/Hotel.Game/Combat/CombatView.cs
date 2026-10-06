using System.Numerics;
using Hotel.Game.Player;
using Hotel.Game.Scene;
using Hotel.Game.Supplies;
using Rusty.Engine;

namespace Hotel.Game.Combat;

/// <summary>Authored low-poly silhouettes and attack poses in the existing scene snapshot.</summary>
internal sealed class CombatView : IDisposable
{
    private readonly IEngineContext engine;
    private readonly HotelScene scene;
    private readonly HotelPlayer player;
    private readonly HotelCombat combat;
    private readonly List<MeshResource> meshes = [];
    private readonly List<Appearance> appearances = [];
    private readonly Dictionary<string, MeshResource> boxes = [];
    private readonly Dictionary<string, Part[]> residents = [];
    // The look of each held item, and the flash at a firearm's commit.
    private readonly Dictionary<HeldLook, Part[]> held = [];
    private readonly Part muzzle;
    // Enough flare boxes for the shots one hand can have in flight at once.
    private const int ShownProjectiles = 6;
    private readonly Part[] flares;
    private readonly Dictionary<string, Part> beams = [];

    internal CombatView(IEngineContext engine, HotelScene scene, HotelPlayer player, HotelCombat combat)
    {
        this.engine = engine; this.scene = scene; this.player = player; this.combat = combat;
        try
        {
            foreach (HotelEnemy enemy in combat.Enemies)
            {
                residents[enemy.Id] = enemy.Kind.Behavior == ResidentBehavior.Porter
                    ? [Box("porter-coat", new(0, .06f, 0), new(.62f, .85f, .36f)),
                       Box("equipment", new(-.17f, -.61f, 0), new(.20f, .55f, .24f)),
                       Box("equipment", new(.17f, -.61f, 0), new(.20f, .55f, .24f)),
                       Box("resident-ivory", new(0, .66f, -.015f), new(.31f, .34f, .27f)),
                       Box("porter-coat", new(0, .86f, -.025f), new(.40f, .12f, .34f)),
                       Box("brass", new(0, .90f, -.025f), new(.24f, .03f, .26f)),
                       Box("equipment", new(0, .67f, -.16f), new(.22f, .07f, .035f)),
                       Box("brass", new(0, .28f, -.19f), new(.06f, .08f, .02f)),
                       Box("brass", new(0, .08f, -.19f), new(.06f, .08f, .02f)),
                       Box("porter-coat", new(-.43f, .12f, -.06f), new(.19f, .68f, .22f)),
                       Box("porter-coat", new(.43f, .12f, -.10f), new(.19f, .68f, .22f), "arm"),
                       Box("brass", new(.43f, -.15f, -.38f), new(.36f, .20f, .30f), "arm"),
                       Box("tell-amber", new(0, .67f, -.185f), new(.16f, .04f, .02f), "tell")]
                    : [Box("brass", new(0, -.78f, 0), new(.65f, .14f, .58f)),
                       Box("brass", new(0, -.12f, 0), new(.11f, 1.24f, .11f)),
                       Box("resident-ivory", new(0, .48f, 0), new(.74f, .13f, .61f)),
                       Box("resident-ivory", new(0, .60f, 0), new(.61f, .13f, .51f)),
                       Box("resident-ivory", new(0, .72f, 0), new(.46f, .13f, .40f)),
                       Box("equipment", new(0, .58f, -.275f), new(.32f, .16f, .10f)),
                       Box("tell-amber", new(0, .58f, -.34f), new(.22f, .08f, .035f), "tell"),
                       Box("brass", new(-.30f, -.22f, 0), new(.08f, .5f, .08f)),
                       Box("brass", new(.30f, -.22f, 0), new(.08f, .5f, .08f))];
                beams.Add(enemy.Id, Box("tell-amber", default, Vector3.One));
            }
            held[HeldLook.Bar] = [Box("equipment", new(0, 0, -.15f), new(.055f, .055f, .65f)),
                   Box("brass", new(0, .065f, -.48f), new(.065f, .16f, .06f)),
                   Box("canvas", new(0, -.015f, .03f), new(.12f, .14f, .17f))];
            held[HeldLook.Pistol] = [Box("equipment", new(0, .05f, -.12f), new(.13f, .13f, .32f)),
                   Box("equipment", new(0, .03f, -.34f), new(.07f, .08f, .17f)),
                   Box("trim", new(0, -.09f, 0), new(.11f, .26f, .13f)),
                   Box("canvas", new(0, -.07f, .06f), new(.14f, .17f, .15f)),
                   Box("brass", new(0, .13f, -.13f), new(.02f, .03f, .03f))];
            held[HeldLook.Bell] = [Box("brass", new(0, .02f, -.14f), new(.16f, .10f, .16f)),
                   Box("brass", new(0, .09f, -.14f), new(.03f, .05f, .03f)),
                   Box("trim", new(0, -.05f, -.14f), new(.2f, .03f, .2f))];
            held[HeldLook.Flare] = [Box("tell-amber", new(0, .05f, -.14f), new(.12f, .12f, .26f)),
                   Box("equipment", new(0, -.08f, -.02f), new(.10f, .22f, .12f))];
            muzzle = Box("tell-amber", new(0, .04f, -.48f), new(.15f, .15f, .19f));
            flares = Enumerable.Range(0, ShownProjectiles).Select(_ => Box("tell-amber", default, new(.16f))).ToArray();
        }
        catch { Dispose(); throw; }
    }

    private Part Box(string material, Vector3 offset, Vector3 size, string role = "")
    {
        if (!boxes.TryGetValue(material, out MeshResource? mesh))
        {
            mesh = RoomGeometry.Box(engine, scene.Surface(material), new(-.5f), new(.5f), Vector2.One);
            meshes.Add(mesh); boxes.Add(material, mesh);
        }
        Appearance appearance = engine.Graphics.CreateMeshAppearance(mesh);
        appearances.Add(appearance);
        return new(scene.Entities.Create().Value, appearance, offset, size, role);
    }

    internal void Publish()
    {
        List<AppearanceFact> facts = [];
        void Place(Part part, Vector3 origin, Quaternion rotation, bool visible, Vector3? offset = null, Vector3? size = null)
            => facts.Add(new(part.Entity, false, 0,
                new(origin + Vector3.Transform(offset ?? part.Offset, rotation), rotation, size ?? part.Size), part.Appearance, visible, RenderLayer.Scene));
        foreach (HotelEnemy enemy in combat.Enemies)
        {
            Quaternion facing = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -enemy.Yaw);
            bool winding = enemy.Phase == AttackPhase.Windup, committed = enemy.Phase == AttackPhase.Commit;
            foreach (Part part in residents[enemy.Id])
            {
                Vector3 offset = part.Offset;
                if (part.Role == "arm") offset += winding ? new Vector3(0, .55f, .18f) : committed ? new Vector3(0, -.04f, -.60f) : Vector3.Zero;
                if (enemy.Phase is AttackPhase.Recovery or AttackPhase.Interrupted) offset.Y -= .10f;
                Place(part, enemy.Position, facing, enemy.Alive && (part.Role != "tell" || winding || committed), offset);
            }
            Part beam = beams[enemy.Id];
            bool showBeam = enemy.Alive && enemy.BeamTime > 0;
            Vector3 delta = enemy.BeamEnd - enemy.Eye;
            float length = delta.Length();
            Quaternion beamRotation = length > .001f ? Facing(delta / length) : Quaternion.Identity;
            Place(beam, (enemy.Eye + enemy.BeamEnd) / 2, beamRotation, showBeam, Vector3.Zero, new(.045f, .045f, Math.Max(.001f, length)));
        }
        Quaternion camera = Facing(player.Forward);
        Vector3 hand = new(.28f, -.31f, -.53f);
        if (combat.Phase == AttackPhase.Windup) hand += new Vector3(.07f, .12f, .09f) * combat.PhaseProgress;
        if (combat.Phase == AttackPhase.Commit) hand += new Vector3(-.12f, .06f, -.25f);
        if (combat.Phase is AttackPhase.Recovery) hand.Y -= .12f;
        Vector3 handWorld = player.Eye + Vector3.Transform(hand, camera);
        HeldLook? look = combat.Holding?.Item.Wear!.Look;
        foreach (var (kind, parts) in held)
            foreach (Part part in parts) Place(part, handWorld, camera, !combat.Defeated && look == kind);
        bool firing = combat.Phase == AttackPhase.Commit && combat.User.Current?.Delivery.Kind is Actions.DeliveryKind.Hitscan or Actions.DeliveryKind.Projectile;
        Place(muzzle, handWorld, camera, !combat.Defeated && firing);
        for (int i = 0; i < flares.Length; i++)
            Place(flares[i], i < combat.Projectiles.Count ? combat.Projectiles[i].Position : Vector3.Zero, Quaternion.Identity, i < combat.Projectiles.Count);
        scene.PublishCombat(facts.ToArray());
    }

    private static Quaternion Facing(Vector3 forward)
    {
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        Vector3 up = Vector3.Cross(right, forward);
        return Quaternion.CreateFromRotationMatrix(new(right.X, right.Y, right.Z, 0, up.X, up.Y, up.Z, 0,
            -forward.X, -forward.Y, -forward.Z, 0, 0, 0, 0, 1));
    }
    public void Dispose()
    {
        scene.PublishCombat([]);
        foreach (Appearance appearance in appearances) appearance.Dispose();
        foreach (MeshResource mesh in meshes) mesh.Dispose();
    }
    private sealed record Part(ulong Entity, Appearance Appearance, Vector3 Offset, Vector3 Size, string Role);
}
