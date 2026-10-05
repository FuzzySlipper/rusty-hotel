using Hotel.Game.Combat;
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
    private readonly HotelPlayer player;
    private readonly HotelSupplies supplies;
    private readonly HotelCombat combat;
    private readonly HotelSpirit spirit;
    private readonly HotelRoute route;
    private CheckpointState? checkpoint;

    internal HotelExpedition(IEngineContext engine, RefugeDefinition refuge, HotelPlayer player,
        HotelSupplies supplies, HotelCombat combat, HotelSpirit spirit, HotelRoute route)
    {
        this.refuge = refuge; this.player = player; this.supplies = supplies;
        this.combat = combat; this.spirit = spirit; this.route = route;
        store = new(engine, Scope, new JsonProductStateCodec<CheckpointState>(CheckpointJson.Default.CheckpointState));
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
            CheckpointState state = loaded.Present
                ? loaded.State ?? throw new InvalidOperationException("Checkpoint has no state.")
                : Capture(0);
            Validate(state);
            if (!loaded.Present) Write(state);
            checkpoint = state;
            Apply(state);
            Status = loaded.Present ? "Continued from the refuge checkpoint." : "Initial refuge checkpoint ready.";
        }
        catch (Exception error)
        {
            throw new InvalidOperationException("Cannot load the Hotel refuge checkpoint. The existing save has not been replaced. " + error.Message, error);
        }
    }

    // Called only through the route's Engine-admitted, in-reach notebook interaction.
    internal bool Return()
    {
        if (combat.Defeated || combat.Phase != AttackPhase.Ready || spirit.Active)
            return Receipt(false, "Finish your action before recording a checkpoint.");
        CheckpointState next = Capture(checked(Returns + 1));
        Validate(next);
        try { Write(next); }
        catch (Exception error) when (error is PersistenceStorageException or InvalidOperationException)
        {
            return Receipt(false, "The checkpoint could not be saved. Your carried finds and previous checkpoint are unchanged.\n\n" + error.Message);
        }
        // Settle the deposit only after Engine confirms the whole record is durable.
        checkpoint = next;
        supplies.Restore(next.Supplies);
        string secured = next.SecuredFinds.Length == 0 ? "No expedition finds secured yet." :
            "Secured: " + string.Join(", ", next.SecuredFinds.Select(id => supplies.Item(supplies.Finds.Single(f => f.Id == id).Item).Name)) + ".";
        return Receipt(true, secured + $"\n\nCheckpoint {Returns} saved. Health {supplies.Health}; ammunition {supplies.Ammo}; summon charges {supplies.Summon}." +
            "\n\nYour supplies, pact, opened doors, collected finds and residents are recorded together. Defeat or reopening the hotel returns you to this refuge checkpoint. Changes made after this return are not saved.");
    }

    internal void Recover()
    {
        Apply(checkpoint ?? throw new InvalidOperationException("No refuge checkpoint was established."));
        Status = Returns == 0 ? "Returned to the initial refuge checkpoint." : $"Returned to refuge checkpoint {Returns}.";
        ReceiptTitle = "Back at the refuge";
        ReceiptText = Status + "\n\nYour supplies, pact, doors, finds and residents have all been restored. Nothing spent after this checkpoint is permanently lost.";
        ReceiptSequence++;
    }

    private bool Receipt(bool saved, string text)
    {
        ReceiptTitle = saved ? "Return recorded" : "Checkpoint not saved";
        ReceiptText = text;
        Status = saved ? $"Refuge checkpoint {Returns} saved · {SecuredFinds.Length} expedition find secured" : "Checkpoint not saved";
        ReceiptSequence++;
        return saved;
    }

    private CheckpointState Capture(int returns)
    {
        SuppliesState carried = supplies.Capture();
        string[] secured = carried.Collected.Where(supplies.IsExpeditionFind).ToArray();
        // Expedition finds move into the refuge ledger, retaining their collected identity.
        SuppliesState deposited = carried with { Pockets = carried.Pockets.Select(s => s is { } item && supplies.Item(item.Item).Kind == SupplyKind.Expedition ? null : s).ToArray() };
        return new(1, returns, refuge.Id, combat.Weapon.Id, deposited, route.OpenDoors,
            spirit.Capture(), combat.Capture(), secured);
    }

    internal void Validate(CheckpointState state)
    {
        if (state.Version != 1 || state.Returns < 0 || state.Refuge != refuge.Id || state.Supplies is null || state.Spirit is null)
            throw new InvalidOperationException("Checkpoint version or refuge is invalid.");
        supplies.Validate(state.Supplies);
        route.Validate(state.OpenDoors);
        combat.Validate(state.Weapon, state.Residents);
        spirit.Validate(state.Spirit);
        string[] expected = state.Supplies.Collected.Where(supplies.IsExpeditionFind).Order().ToArray();
        if (state.SecuredFinds is null || !state.SecuredFinds.Order().SequenceEqual(expected) ||
            state.Supplies.Pockets.Any(s => s is { } item && supplies.Item(item.Item).Kind == SupplyKind.Expedition))
            throw new InvalidOperationException("Checkpoint expedition deposit is inconsistent.");
    }

    private void Apply(CheckpointState state)
    {
        supplies.Restore(state.Supplies);
        combat.Restore(state.Weapon, state.Residents);
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
