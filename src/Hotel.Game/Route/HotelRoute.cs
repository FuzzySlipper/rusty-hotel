using System.Numerics;
using Hotel.Game.Player;
using Hotel.Game.Scene;
using Hotel.Game.Supplies;
using Hotel.Game.Spirits;
using Rusty.Engine;
using Rusty.Engine.Interaction;

namespace Hotel.Game.Route;

/// <summary>Authored route policy; Engine owns focus, visibility and use admission.</summary>
internal sealed class HotelRoute : IWorldInteractionScene
{
    private readonly IEngineContext engine;
    private readonly HotelScene scene;
    private readonly HotelPlayer player;
    private readonly RouteDefinition tuning;
    private readonly HotelSupplies supplies;
    private readonly (FindDefinition Definition, ulong Entity)[] finds;
    private readonly DoorState[] doors;
    private readonly (ReadingDefinition Definition, ulong Entity)[] readings;
    private readonly HotelSpirit? spirit;
    private readonly ulong spiritEntity;
    private readonly ulong refugeEntity;
    private ulong revision = 1;

    internal HotelRoute(IEngineContext engine, HotelScene scene, HotelPlayer player, HotelSupplies supplies, HotelSpirit? spirit = null)
    {
        this.engine = engine;
        this.scene = scene;
        this.player = player;
        this.supplies = supplies;
        this.spirit = spirit;
        spiritEntity = scene.Entities.Create().Value;
        refugeEntity = scene.Entities.Create().Value;
        tuning = scene.Definition.Route;
        doors = tuning.Doors.Select(d => new DoorState(d, scene.DoorEntity(d.Id))).ToArray();
        readings = tuning.Readings.Select(r => (r, scene.Entities.Create().Value)).ToArray();
        finds = scene.Definition.Supplies.Finds.Select(f => (f, scene.Entities.Create().Value)).ToArray();
        Interaction = new(this);
    }

    internal WorldInteraction Interaction { get; }
    internal Action? Changed { get; set; }
    internal Func<bool>? RecordCheckpoint { get; set; }
    internal string ReadingTitle { get; private set; } = "";
    internal string ReadingText { get; private set; } = "";
    internal ulong ReadingSequence { get; private set; }
    internal string Prompt { get; private set; } = "";
    internal string Location => tuning.Rooms.FirstOrDefault(r =>
        player.Position.X >= r.Min[0] && player.Position.X <= r.Max[0] &&
        player.Position.Z >= r.Min[2] && player.Position.Z <= r.Max[2])?.Label ?? "West wing corridor";
    internal string[] OpenDoors => doors.Where(d => d.Open).Select(d => d.Definition.Id).ToArray();

    internal void Update()
    {
        InteractionReadout readout = Interaction.Update();
        InteractionObservation? row = readout.Candidates.Where(c => c.Selected).Select(c => (InteractionObservation?)c).FirstOrDefault()
            ?? readout.Candidates.Where(c => c.WithinAcquisition &&
                c.Reason is InteractionReason.OutOfReach or InteractionReason.Locked).Select(c => (InteractionObservation?)c).FirstOrDefault();
        Prompt = row?.Reason switch
        {
            InteractionReason.Ready => $"E · {row.Value.Candidate.Label}",
            InteractionReason.OutOfReach => $"{row.Value.Candidate.Label} · Move closer",
            InteractionReason.Locked => "Return door · Latched from the service side",
            _ => ""
        };
    }

    internal void Use() { Interaction.UseFocused(); Update(); }

    public InteractionSceneSnapshot ReadInteraction()
    {
        List<InteractionCandidate> candidates = [];
        InteractionVisibility Visible(Vector3 point, ulong entity) => InteractionVisibilityQuery.Cast(
            engine.Spatial, scene.Session, player.Eye, point, new(uint.MaxValue, uint.MaxValue),
            ReadOnlyMemory<SpatialEntityCollider>.Empty, new[] { entity }, .02f);
        foreach (DoorState door in doors)
        {
            if (door.Open) continue;
            Vector3 center = HotelDefinition.Vector(door.Definition.FocusPoint);
            Vector3 normal = Vector3.Transform(Vector3.UnitZ, Quaternion.CreateFromAxisAngle(Vector3.UnitY,
                door.Definition.ClosedYaw * MathF.PI / 180));
            // Aim just outside the visible face, on either side of the closed leaf.
            Vector3 point = center + normal * (Vector3.Dot(player.Eye - center, normal) >= 0 ? .07f : -.07f);
            candidates.Add(new(new(door.Entity, revision), door.Definition.Label, point, tuning.Reach,
                Visible(point, door.Entity), CanUnlatch(door) ? InteractionAvailability.Available : InteractionAvailability.Locked));
        }
        foreach (var reading in readings)
        {
            Vector3 point = HotelDefinition.Vector(reading.Definition.Point);
            candidates.Add(new(new(reading.Entity, revision), reading.Definition.Label, point, tuning.Reach, Visible(point, reading.Entity), InteractionAvailability.Available));
        }
        foreach (var find in finds)
        {
            if (supplies.Collected(find.Definition.Id)) continue;
            Vector3 point = HotelDefinition.Vector(find.Definition.Point);
            candidates.Add(new(new(find.Entity, revision), $"Take {supplies.Item(find.Definition.Item).Name}", point,
                tuning.Reach, Visible(point, find.Entity), InteractionAvailability.Available));
        }
        if (spirit is { Acquired: false })
        {
            Vector3 point = HotelDefinition.Vector(spirit.Definition.Point);
            candidates.Add(new(new(spiritEntity, revision), "Lift the bell · free Hushwing", point,
                tuning.Reach, Visible(point, spiritEntity), InteractionAvailability.Available));
        }
        if (RecordCheckpoint is not null)
        {
            Vector3 point = HotelDefinition.Vector(scene.Definition.Refuge.Point);
            candidates.Add(new(new(refugeEntity, revision), "Record refuge checkpoint", point,
                tuning.Reach, Visible(point, refugeEntity), InteractionAvailability.Available));
        }
        return new(new(player.Eye, player.Forward, tuning.AcquireAngle, tuning.ReleaseAngle,
            tuning.FocusDistance, tuning.FocusDistance + .5f), candidates.ToArray(), $"hotel-route:{revision}", "use");
    }

    public InteractionActionResult UseInteraction(InteractionTarget target)
    {
        if (target.Revision != revision) return new(false, "The target has changed.");
        if (target.Id == refugeEntity && RecordCheckpoint is not null)
        {
            bool recorded = RecordCheckpoint();
            revision++;
            Changed?.Invoke();
            return new(recorded, recorded ? "Refuge checkpoint recorded." : "Checkpoint not saved.");
        }
        if (target.Id == spiritEntity && spirit is not null)
        {
            bool acquired = spirit.Acquire();
            if (acquired) { revision++; Update(); }
            Changed?.Invoke();
            return new(acquired, spirit.Message);
        }
        DoorState? door = doors.FirstOrDefault(d => d.Entity == target.Id);
        if (door is not null)
        {
            if (door.Open || !CanUnlatch(door)) return new(false, "The door cannot be opened from here.");
            scene.PlaceDoor(door.Definition.Id, true);
            door.Open = true;
            revision++;
            Update();
            Changed?.Invoke();
            return new(true, "Door opened.");
        }
        foreach (var reading in readings)
        {
            if (reading.Entity != target.Id) continue;
            ReadingTitle = reading.Definition.Title;
            ReadingText = reading.Definition.Text;
            ReadingSequence++;
            revision++;
            Changed?.Invoke();
            return new(true, "Opened for reading.");
        }
        foreach (var find in finds)
        {
            if (find.Entity != target.Id) continue;
            bool pickedUp = supplies.Pickup(find.Definition.Id);
            if (pickedUp)
            {
                scene.ShowFind(find.Definition.Id, false);
                revision++;
                Update();
            }
            Changed?.Invoke();
            return new(pickedUp, supplies.Message);
        }
        return new(false, "Unknown hotel target.");
    }

    internal void Validate(string[] openDoors)
    {
        if (openDoors is null || openDoors.Distinct().Count() != openDoors.Length ||
            openDoors.Any(id => !doors.Any(d => d.Definition.Id == id)))
            throw new InvalidOperationException("Checkpoint door state is invalid.");
    }

    internal void Reset() => Restore([]);
    internal void Restore(string[] openDoors)
    {
        foreach (DoorState door in doors)
        {
            bool open = openDoors.Contains(door.Definition.Id);
            scene.PlaceDoor(door.Definition.Id, open);
            door.Open = open;
        }
        foreach (var find in finds) scene.ShowFind(find.Definition.Id, !supplies.Collected(find.Definition.Id));
        revision++;
        ReadingSequence++;
        ReadingTitle = ReadingText = Prompt = "";
        Interaction.Focus.Clear();
        Update();
    }

    private bool CanUnlatch(DoorState door) => !door.Definition.FarSideLatch ||
        Vector3.Dot(player.Position - HotelDefinition.Vector(door.Definition.Hinge),
            HotelDefinition.Vector(door.Definition.UnlockDirection!)) > 0;
    private sealed class DoorState(DoorDefinition definition, ulong entity)
    {
        internal DoorDefinition Definition { get; } = definition;
        internal ulong Entity { get; } = entity;
        internal bool Open { get; set; }
    }
}
