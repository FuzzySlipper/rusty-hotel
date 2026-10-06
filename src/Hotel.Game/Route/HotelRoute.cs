using System.Numerics;
using Hotel.Game.Content;
using Hotel.Game.Expedition;
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
    private readonly InteractionTuning interaction;
    private readonly RouteMessages text;
    private readonly ExcursionRoute layout;
    private readonly HotelSupplies supplies;
    private readonly HotelSpirit spirit;
    private readonly Func<bool> recordCheckpoint;
    private readonly Func<StairDirection, bool> travel;
    private readonly Action changed;
    private readonly DoorState[] doors;
    private readonly (FindDefinition Definition, ulong Entity)[] finds;
    private readonly HashSet<string> keys = new(StringComparer.Ordinal);
    private readonly List<Interactable> interactables = [];
    private readonly Dictionary<ulong, Interactable> byEntity = [];
    private ulong revision = 1;

    /// <param name="recordCheckpoint">Expedition's refuge return. It is resolved when the notebook is
    /// used, because Expedition itself captures and restores this route's door state.</param>
    /// <param name="changed">Publishes interface facts after a world action changes them.</param>
    /// <param name="travel">Takes the stairs: the product leaves this floor for the next one up or down.</param>
    /// <param name="shared">The product's one world interaction, which reads whichever route is current.</param>
    internal HotelRoute(IEngineContext engine, HotelScene scene, HotelPlayer player, HotelSupplies supplies,
        HotelSpirit spirit, RouteDefinition definition, ExcursionRoute layout, RefugeDefinition? refuge,
        Func<bool> recordCheckpoint, Action changed, Func<StairDirection, bool> travel, WorldInteraction? shared = null)
    {
        this.travel = travel;
        this.engine = engine;
        this.scene = scene;
        this.player = player;
        this.supplies = supplies;
        this.spirit = spirit;
        this.recordCheckpoint = recordCheckpoint;
        this.changed = changed;
        interaction = definition.Interaction;
        text = definition.Text;
        this.layout = layout;
        doors = layout.Doors.Select(d => new DoorState(d, scene.DoorEntity(d.Id))).ToArray();
        finds = supplies.Finds.Select(f => (f, scene.Entities.Create().Value)).ToArray();

        // Candidate order is stable: doors, readings, finds, the spirit bell, the refuge notebook, then the stairs.
        foreach (DoorState door in doors)
            Add(new(door.Entity, () => !door.Open, () => door.Definition.Label, () => DoorFocus(door),
                () => CanUnlatch(door), () => OpenDoor(door)));
        foreach (ReadingDefinition reading in layout.Readings)
        {
            Vector3 point = Authored.Vector(reading.Point);
            Add(new(scene.Entities.Create().Value, () => true, () => reading.Label, () => point,
                () => true, () => Read(reading)));
        }
        foreach (KeyDefinition key in layout.Keys)
        {
            Vector3 point = Authored.Vector(key.Point);
            Add(new(scene.Entities.Create().Value, () => !keys.Contains(key.Item), () => Template.Fill(text.Take, ("item", key.Name)),
                () => point, () => true, () => TakeKey(key)));
        }
        foreach (var find in finds)
        {
            Vector3 point = Authored.Vector(find.Definition.Point);
            Add(new(find.Entity, () => !supplies.Collected(find.Definition.Id),
                () => Template.Fill(text.Take, ("item", supplies.Item(find.Definition.Item).Name)), () => point, () => true, () => Take(find)));
        }
        for (int i = 0; i < spirit.Bells.Length; i++)
        {
            int bell = i;
            Vector3 point = Authored.Vector(spirit.Bells[bell].Point);
            Add(new(scene.Entities.Create().Value, () => !spirit.Acquired(spirit.Bells[bell].Spirit), () => spirit.BellLabel(bell),
                () => point, () => true, () => FreeSpirit(bell)));
        }
        if (refuge is not null)
        {
            Vector3 notebook = Authored.Vector(refuge.Point);
            Add(new(scene.Entities.Create().Value, () => true, () => text.RecordCheckpoint,
                () => notebook, () => true, RecordCheckpoint));
        }
        foreach (StairDefinition stair in layout.Stairs)
        {
            Vector3 point = Authored.Vector(stair.Point);
            Add(new(scene.Entities.Create().Value, () => true, () => stair.Direction == StairDirection.Up ? text.StairsUp : text.StairsDown,
                () => point, () => true, () => Climb(stair)));
        }
        Interaction = shared ?? new(this);
    }

    internal WorldInteraction Interaction { get; }
    internal string ReadingTitle { get; private set; } = "";
    internal string ReadingText { get; private set; } = "";
    internal ulong ReadingSequence { get; private set; }
    internal string Prompt { get; private set; } = "";
    internal string Location => layout.Rooms.FirstOrDefault(r =>
        player.Position.X >= r.Min[0] && player.Position.X <= r.Max[0] &&
        player.Position.Z >= r.Min[2] && player.Position.Z <= r.Max[2])?.Label ?? layout.FallbackLocation;
    internal string[] OpenDoors => doors.Where(d => d.Open).Select(d => d.Definition.Id).ToArray();
    /// <summary>The keys the player holds on this floor.</summary>
    internal string[] Keys => keys.Order(StringComparer.Ordinal).ToArray();

    internal void Update()
    {
        InteractionReadout readout = Interaction.Update();
        InteractionObservation? row = readout.Candidates.Where(c => c.Selected).Select(c => (InteractionObservation?)c).FirstOrDefault()
            ?? readout.Candidates.Where(c => c.WithinAcquisition &&
                c.Reason is InteractionReason.OutOfReach or InteractionReason.Locked).Select(c => (InteractionObservation?)c).FirstOrDefault();
        Prompt = row?.Reason switch
        {
            InteractionReason.Ready => Template.Fill(text.Ready, ("label", row.Value.Candidate.Label)),
            InteractionReason.OutOfReach => Template.Fill(text.OutOfReach, ("label", row.Value.Candidate.Label)),
            InteractionReason.Locked => doors.FirstOrDefault(d => d.Entity == row.Value.Candidate.Target.Id)?.Definition.LockedPrompt ?? "",
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
            candidates.Add(new(new(item.Entity, revision), item.Label(), point, interaction.Reach, visibility,
                item.Available() ? InteractionAvailability.Available : InteractionAvailability.Locked));
        }
        return new(new(player.Eye, player.Forward, interaction.AcquireAngle, interaction.ReleaseAngle,
            interaction.FocusDistance, interaction.FocusDistance + .5f), candidates.ToArray(), $"hotel-route:{revision}", "use");
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
    internal void Restore(string[] openDoors, string[]? heldKeys = null)
    {
        keys.Clear();
        keys.UnionWith((heldKeys ?? []).Where(k => layout.Keys.Any(d => d.Item == k) || layout.Doors.Any(d => d.Key == k)));
        foreach (KeyDefinition key in layout.Keys) scene.ShowFind(key.Id, !keys.Contains(key.Item));
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

    // The product replaces this route's world when the stairs are taken, so nothing here changes afterwards.
    private InteractionActionResult Climb(StairDefinition stair) =>
        travel(stair.Direction) ? new(true, "Took the stairs.") : new(false, "The stairs lead nowhere yet.");

    private InteractionActionResult FreeSpirit(int bell)
    {
        bool acquired = spirit.Acquire(bell);
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

    private InteractionActionResult TakeKey(KeyDefinition key)
    {
        keys.Add(key.Item);
        scene.ShowFind(key.Id, false);
        revision++;
        Update();
        changed();
        return new(true, "Key taken.");
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
        Vector3 center = Authored.Vector(door.Definition.FocusPoint);
        Vector3 normal = Vector3.Transform(Vector3.UnitZ, Quaternion.CreateFromAxisAngle(Vector3.UnitY,
            door.Definition.ClosedYaw * MathF.PI / 180));
        return center + normal * (Vector3.Dot(player.Eye - center, normal) >= 0 ? .07f : -.07f);
    }

    // A latched door opens from its far side; a locked one only for the holder of its key.
    private bool CanUnlatch(DoorState door) => (door.Definition.Key is not { } key || keys.Contains(key)) && (!door.Definition.FarSideLatch ||
        Vector3.Dot(player.Position - Authored.Vector(door.Definition.Hinge),
            Authored.Vector(door.Definition.UnlockDirection!)) > 0);

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
