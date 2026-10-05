using System.Numerics;
using Hotel.Game.Player;
using Hotel.Game.Scene;
using Hotel.Game.Supplies;
using Hotel.Game.Spirits;
using Rusty.Engine;
using Rusty.Engine.Interaction;

namespace Hotel.Game.Route;

/// <summary>Authored route policy; Engine owns focus, visibility and use admission.</summary>
/// <remarks>
/// Every world interactable is one <see cref="Interactable"/> entry built from authored definitions.
/// A new kind adds its entries and one use handler; focus, visibility and staleness stay shared.
/// </remarks>
internal sealed class HotelRoute : IWorldInteractionScene
{
    private readonly IEngineContext engine;
    private readonly HotelScene scene;
    private readonly HotelPlayer player;
    private readonly RouteDefinition tuning;
    private readonly HotelSupplies supplies;
    private readonly HotelSpirit spirit;
    private readonly Func<bool> recordCheckpoint;
    private readonly Action changed;
    private readonly DoorState[] doors;
    private readonly (FindDefinition Definition, ulong Entity)[] finds;
    private readonly List<Interactable> interactables = [];
    private readonly Dictionary<ulong, Interactable> byEntity = [];
    private ulong revision = 1;

    /// <param name="recordCheckpoint">Expedition's refuge return. It is resolved when the notebook is
    /// used, because Expedition itself captures and restores this route's door state.</param>
    /// <param name="changed">Publishes interface facts after a world action changes them.</param>
    internal HotelRoute(IEngineContext engine, HotelScene scene, HotelPlayer player, HotelSupplies supplies,
        HotelSpirit spirit, Func<bool> recordCheckpoint, Action changed)
    {
        this.engine = engine;
        this.scene = scene;
        this.player = player;
        this.supplies = supplies;
        this.spirit = spirit;
        this.recordCheckpoint = recordCheckpoint;
        this.changed = changed;
        tuning = scene.Definition.Route;
        doors = tuning.Doors.Select(d => new DoorState(d, scene.DoorEntity(d.Id))).ToArray();
        finds = scene.Definition.Supplies.Finds.Select(f => (f, scene.Entities.Create().Value)).ToArray();

        // Candidate order is stable: doors, readings, finds, the spirit bell, then the refuge notebook.
        foreach (DoorState door in doors)
            Add(new(door.Entity, () => !door.Open, () => door.Definition.Label, () => DoorFocus(door),
                () => CanUnlatch(door), () => OpenDoor(door)));
        foreach (ReadingDefinition reading in tuning.Readings)
        {
            Vector3 point = HotelDefinition.Vector(reading.Point);
            Add(new(scene.Entities.Create().Value, () => true, () => reading.Label, () => point,
                () => true, () => Read(reading)));
        }
        foreach (var find in finds)
        {
            Vector3 point = HotelDefinition.Vector(find.Definition.Point);
            Add(new(find.Entity, () => !supplies.Collected(find.Definition.Id),
                () => $"Take {supplies.Item(find.Definition.Item).Name}", () => point, () => true, () => Take(find)));
        }
        Vector3 bell = HotelDefinition.Vector(spirit.Definition.Point);
        Add(new(scene.Entities.Create().Value, () => !spirit.Acquired, () => "Lift the bell · free Hushwing",
            () => bell, () => true, FreeSpirit));
        Vector3 notebook = HotelDefinition.Vector(scene.Definition.Refuge.Point);
        Add(new(scene.Entities.Create().Value, () => true, () => "Record refuge checkpoint",
            () => notebook, () => true, RecordCheckpoint));
        Interaction = new(this);
    }

    internal WorldInteraction Interaction { get; }
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
        foreach (Interactable item in interactables)
        {
            if (!item.Present()) continue;
            Vector3 point = item.Point();
            InteractionVisibility visibility = InteractionVisibilityQuery.Cast(
                engine.Spatial, scene.Session, player.Eye, point, new(uint.MaxValue, uint.MaxValue),
                ReadOnlyMemory<SpatialEntityCollider>.Empty, new[] { item.Entity }, .02f);
            candidates.Add(new(new(item.Entity, revision), item.Label(), point, tuning.Reach, visibility,
                item.Available() ? InteractionAvailability.Available : InteractionAvailability.Locked));
        }
        return new(new(player.Eye, player.Forward, tuning.AcquireAngle, tuning.ReleaseAngle,
            tuning.FocusDistance, tuning.FocusDistance + .5f), candidates.ToArray(), $"hotel-route:{revision}", "use");
    }

    public InteractionActionResult UseInteraction(InteractionTarget target)
    {
        if (target.Revision != revision) return new(false, "The target has changed.");
        return byEntity.TryGetValue(target.Id, out Interactable? item) ? item.Use() : new(false, "Unknown hotel target.");
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

    private void Add(Interactable item)
    {
        interactables.Add(item);
        byEntity.Add(item.Entity, item);
    }

    private InteractionActionResult RecordCheckpoint()
    {
        bool recorded = recordCheckpoint();
        revision++;
        changed();
        return new(recorded, recorded ? "Refuge checkpoint recorded." : "Checkpoint not saved.");
    }

    private InteractionActionResult FreeSpirit()
    {
        bool acquired = spirit.Acquire();
        if (acquired) { revision++; Update(); }
        changed();
        return new(acquired, spirit.Message);
    }

    private InteractionActionResult OpenDoor(DoorState door)
    {
        if (door.Open || !CanUnlatch(door)) return new(false, "The door cannot be opened from here.");
        scene.PlaceDoor(door.Definition.Id, true);
        door.Open = true;
        revision++;
        Update();
        changed();
        return new(true, "Door opened.");
    }

    private InteractionActionResult Read(ReadingDefinition reading)
    {
        ReadingTitle = reading.Title;
        ReadingText = reading.Text;
        ReadingSequence++;
        revision++;
        changed();
        return new(true, "Opened for reading.");
    }

    private InteractionActionResult Take((FindDefinition Definition, ulong Entity) find)
    {
        bool pickedUp = supplies.Pickup(find.Definition.Id);
        if (pickedUp)
        {
            scene.ShowFind(find.Definition.Id, false);
            revision++;
            Update();
        }
        changed();
        return new(pickedUp, supplies.Message);
    }

    // Aim just outside the visible face, on whichever side of the closed leaf the player stands.
    private Vector3 DoorFocus(DoorState door)
    {
        Vector3 center = HotelDefinition.Vector(door.Definition.FocusPoint);
        Vector3 normal = Vector3.Transform(Vector3.UnitZ, Quaternion.CreateFromAxisAngle(Vector3.UnitY,
            door.Definition.ClosedYaw * MathF.PI / 180));
        return center + normal * (Vector3.Dot(player.Eye - center, normal) >= 0 ? .07f : -.07f);
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

    /// <summary>One focusable world object: whether it is currently offered, how it reads, and its use.</summary>
    private sealed record Interactable(ulong Entity, Func<bool> Present, Func<string> Label, Func<Vector3> Point,
        Func<bool> Available, Func<InteractionActionResult> Use);
}
