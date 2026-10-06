using Hotel.Game.Combat;
using Hotel.Game.Content;
using Hotel.Game.Floors;
using Hotel.Game.Player;
using Hotel.Game.Route;
using Hotel.Game.Spirits;
using Hotel.Game.Supplies;
using Rusty.Engine;
using Rusty.Engine.Persistence;

namespace Hotel.Game.Expedition;

/// <summary>Refuge policy and one complete durable checkpoint across the existing domain owners.</summary>
internal sealed class HotelExpedition : IDisposable
{
    internal const string Scope = "hotel.checkpoints";
    internal const string Key = "refuge/current";
    private readonly ProductStateStore<CheckpointState> store;
    private readonly RefugeDefinition refuge;
    private readonly ExpeditionMessages text;
    private readonly HotelPlayer player;
    private readonly HotelSupplies supplies;
    private readonly HotelCombat combat;
    private readonly HotelSpirit spirit;
    private readonly HotelRoute route;
    private readonly Floors.HotelFloors floors;
    // Captured at construction, while every owner still holds its authored starting values.
    private readonly CheckpointState initial;
    private CheckpointState? checkpoint;
    /// <summary>The checkpoint last stored or loaded.</summary>
    internal CheckpointState? Checkpoint => checkpoint;

    internal HotelExpedition(IEngineContext engine, RefugeDefinition refuge, ExpeditionMessages text, HotelPlayer player,
        HotelSupplies supplies, HotelCombat combat, HotelSpirit spirit, HotelRoute route, Floors.HotelFloors floors)
    {
        this.refuge = refuge; this.text = text; this.player = player; this.supplies = supplies;
        this.combat = combat; this.spirit = spirit; this.route = route; this.floors = floors;
        store = new(engine, Scope, new JsonProductStateCodec<CheckpointState>(CheckpointJson.Default.CheckpointState));
        // The authored start has no run yet; a new game begins one when it first establishes its checkpoint.
        initial = Capture(0) with { Floors = null };
    }

    internal int Returns => checkpoint?.Returns ?? 0;
    internal string[] SecuredFinds => checkpoint?.SecuredFinds ?? [];
    internal ulong ReceiptSequence { get; private set; }
    internal string ReceiptTitle { get; private set; } = "";
    internal string ReceiptText { get; private set; } = "";
    internal string Status { get; private set; } = "";

    internal void Start()
    {
        // Missing is a new excursion. Present-but-invalid fails before any owner is restored or saved.
        try
        {
            ProductStateLoad<CheckpointState> loaded = store.Load(Key);
            CheckpointState state;
            if (loaded.Present) state = loaded.State ?? throw new InvalidOperationException("Checkpoint has no state.");
            else
            {
                floors.BeginNew();
                state = initial with { Floors = floors.Capture() };
            }
            Validate(state);
            if (!loaded.Present) Write(state);
            checkpoint = state;
            Apply(state);
            Status = loaded.Present ? text.Continued : text.InitialReady;
        }
        catch (Exception error)
        {
            throw new InvalidOperationException("Cannot load the Hotel refuge checkpoint. The existing save has not been replaced. " + error.Message, error);
        }
    }

    /// <summary>
    /// Takes over the stored checkpoint and the last receipt from the world the player just left, applying nothing:
    /// changing floor neither saves nor restores.
    /// </summary>
    internal void Adopt(HotelExpedition previous)
    {
        checkpoint = previous.checkpoint;
        ReceiptSequence = previous.ReceiptSequence;
        ReceiptTitle = previous.ReceiptTitle;
        ReceiptText = previous.ReceiptText;
        Status = previous.Status;
    }

    // Called only through the route's Engine-admitted, in-reach notebook interaction.
    internal bool Return()
    {
        if (combat.Defeated || combat.Phase != AttackPhase.Ready || spirit.Active)
            return Receipt(false, text.Busy);
        Progression.GrowthState? before = checkpoint?.Supplies.Growth;
        CheckpointState next = Capture(checked(Returns + 1));
        // The run's floors shift while the player is away: each is due to shift when the player next climbs to it.
        next = next with { Floors = next.Floors! with { Floors = [.. next.Floors.Floors.Select(f => f with { ShiftDue = true })] } };
        // Live state that would not load again is refused here, before it can replace a good save.
        try { Validate(next); Write(next); }
        catch (Exception error) when (error is PersistenceStorageException or InvalidOperationException)
        {
            return Receipt(false, Template.Fill(text.SaveFailed, ("error", error.Message)));
        }
        // Settle the deposit and the coming shift only after Engine confirms the whole record is durable.
        checkpoint = next;
        supplies.Restore(next.Supplies);
        floors.Restore(next.Floors!);
        string secured = next.SecuredFinds.Length == 0 ? text.NothingSecured : Template.Fill(text.Secured,
            ("finds", string.Join(", ", next.SecuredFinds.Select(id => (supplies.Finds.FirstOrDefault(f => f.Id == id) is { } here
                ? supplies.Item(here.Item) : floors.FindItem(id)!).Name))));
        return Receipt(true, Template.Fill(text.Saved, ("secured", secured), ("grown", Grown(before, next.Supplies.Growth)), ("returns", Returns),
            ("health", supplies.Health), ("ammo", supplies.Ammo), ("summon", supplies.Summon)));
    }

    // Growth by use is told here, at the refuge: the level and skill ranks reached since the last checkpoint.
    private string Grown(Progression.GrowthState? before, Progression.GrowthState now)
    {
        Progression.GrowthDefinition growth = supplies.Growth.Definition;
        Progression.GrowthMessages say = growth.Text;
        List<string> changes = [];
        int level = growth.LevelAt(now.Experience);
        if (level > growth.LevelAt(before?.Experience ?? 0)) changes.Add(Template.Fill(say.GrownLevel, ("level", level)));
        foreach (Progression.SkillDefinition skill in growth.Skills)
        {
            int rank = skill.RankAt(now.Uses.GetValueOrDefault(skill.Id));
            if (rank > skill.RankAt(before?.Uses.GetValueOrDefault(skill.Id) ?? 0))
                changes.Add(Template.Fill(say.GrownSkill, ("skill", skill.Name), ("rank", rank)));
        }
        return changes.Count == 0 ? "" : Template.Fill(say.Grown, ("changes", string.Join(say.GrownJoin, changes)));
    }

    internal void Recover()
    {
        Apply(checkpoint ?? throw new InvalidOperationException("No refuge checkpoint was established."));
        Status = Returns == 0 ? text.ReturnedInitial : Template.Fill(text.Returned, ("returns", Returns));
        ReceiptTitle = text.RecoveredTitle;
        ReceiptText = Template.Fill(text.Recovered, ("status", Status));
        ReceiptSequence++;
    }

    /// <summary>Developer override: live state returns to the excursion's start; the stored checkpoint is kept.</summary>
    internal void ApplyInitial() => Apply(initial);

    private bool Receipt(bool saved, string body)
    {
        ReceiptTitle = saved ? text.SavedTitle : text.NotSavedTitle;
        ReceiptText = body;
        Status = saved ? Template.Fill(text.SavedStatus, ("returns", Returns), ("count", SecuredFinds.Length)) : text.NotSavedStatus;
        ReceiptSequence++;
        return saved;
    }

    private CheckpointState Capture(int returns)
    {
        SuppliesState carried = supplies.Capture();
        // Expedition finds from this floor and from the run's generated floors are secured together.
        FloorsState run = floors.Capture();
        string[] secured = [.. carried.Collected.Where(supplies.IsExpeditionFind), .. Floors.HotelFloors.ExpeditionFinds(run)];
        // Expedition finds move into the refuge ledger, retaining their collected identity.
        SuppliesState deposited = carried with { Pockets = carried.Pockets.Select(s => s is { } item && supplies.Item(item.Item).Deposit ? null : s).ToArray() };
        return new(CheckpointState.CurrentVersion, returns, refuge.Id, deposited, route.OpenDoors,
            spirit.Capture(), combat.Capture(), secured, run);
    }

    internal void Validate(CheckpointState state)
    {
        if (state.Version != CheckpointState.CurrentVersion || state.Returns < 0 || state.Refuge != refuge.Id || state.Supplies is null ||
            state.Spirit is null || state.Floors is null)
            throw new InvalidOperationException("Checkpoint version or refuge is invalid.");
        if (state.Floors is { } run) floors.Validate(run);
        supplies.Validate(state.Supplies);
        route.Validate(state.OpenDoors);
        combat.Validate(state.Residents);
        spirit.Validate(state.Spirit);
        string[] expected = state.Supplies.Collected.Where(supplies.IsExpeditionFind)
            .Concat(state.Floors is { } stored ? Floors.HotelFloors.ExpeditionFinds(stored) : []).Order().ToArray();
        if (state.SecuredFinds is null || !state.SecuredFinds.Order().SequenceEqual(expected) ||
            state.Supplies.Pockets.Any(s => s is { } item && supplies.Item(item.Item).Deposit))
            throw new InvalidOperationException("Checkpoint expedition deposit is inconsistent.");
    }

    // The one restore path. Order matters: route restores find visibility from the supplies'
    // collected set, so it runs after supplies; the player returns to the arrival point last of the bodies.
    private void Apply(CheckpointState state)
    {
        if (state.Floors is { } run) floors.Restore(run);
        else floors.BeginNew();
        supplies.Restore(state.Supplies);
        combat.Restore(state.Residents);
        spirit.Restore(state.Spirit);
        player.Reset();
        route.Restore(state.OpenDoors);
    }
    private void Write(CheckpointState state)
    {
        PersistenceSaveReceipt result = store.Save(Key, state);
        if (result.Outcome != PersistenceSaveOutcome.Saved) throw new InvalidOperationException("Engine refused the checkpoint write: " + result.Outcome);
    }
    public void Dispose() => store.Dispose();
}
