using System.Numerics;
using System.Text.Json;
using Hotel.Game.Actions;
using Hotel.Game.Combat;
using Hotel.Game.Content;
using Hotel.Game.Expedition;
using Hotel.Game.Mechanics;
using Hotel.Game.Player;
using Hotel.Game.Supplies;
using Rusty.Engine;

namespace Hotel.Game.Spirits;

internal enum ManifestationPhase { Absent, Arriving, Holding, Departing }

/// <summary>
/// The pacts: which spirits of the roster the investigator has freed from their bells, the one equipped in the pact
/// slot, and the brief visit a call brings. A call is the equipped spirit's action through Combat's pact slot; the
/// summon charges it costs are a Supplies track, and what it does to residents lands through the action resolution.
/// </summary>
internal sealed class HotelSpirit(SpiritDefinition[] roster, SpiritMessages text, SpiritBellPlacement[] bells,
    HotelSupplies supplies, HotelCombat combat, HotelPlayer player, ActionCatalog actions, MechanicsDefinition mechanics)
{
    private readonly HashSet<string> acquired = new(StringComparer.Ordinal);

    internal SpiritDefinition[] Roster => roster;
    /// <summary>Where this excursion keeps spirits' bells, each until its spirit is freed.</summary>
    internal SpiritBellPlacement[] Bells => bells;
    internal bool Acquired(string spirit) => acquired.Contains(spirit);
    internal bool AnyAcquired => acquired.Count > 0;
    /// <summary>The spirit in the pact slot, if any.</summary>
    internal SpiritDefinition? Equipped { get; private set; }
    /// <summary>The spirit visiting now, if a call has brought one.</summary>
    internal SpiritDefinition? Visiting { get; private set; }
    internal ulong Revision { get; private set; } = 1;
    internal ManifestationPhase Phase { get; private set; }
    internal float Elapsed { get; private set; }
    internal float IdleTime { get; private set; }
    internal int Calls { get; private set; }
    internal string Message { get; private set; } = "";
    internal string Notice => noticeTime > 0 ? Message : "";
    internal Vector3 Entrance { get; private set; }
    internal Vector3 Destination { get; private set; }
    internal Quaternion Facing { get; private set; } = Quaternion.Identity;
    private float noticeTime;
    /// <summary>A call is under way through the pact slot, or a spirit is here.</summary>
    internal bool Active => Phase != ManifestationPhase.Absent || combat.PactUser.Busy;
    internal string BellLabel(int bell) => Named(text.BellLabel, Spirit(bells[bell].Spirit));
    /// <summary>The HUD's held-spirit line.</summary>
    internal string HudLabel => Equipped?.Name ?? (AnyAcquired ? text.HudInCase : text.HudNone);
    internal string EquipReason => combat.Defeated ? text.EquipOverwhelmed : Active ? Named(text.EquipWhileActive, Visiting ?? Equipped!) : "";
    internal string Status
    {
        get
        {
            if (combat.Defeated) return text.StatusOverwhelmed;
            SpiritText? said = (Visiting ?? Equipped)?.Text;
            return Phase switch
            {
                ManifestationPhase.Arriving => said!.Arriving,
                ManifestationPhase.Holding => said!.Holding,
                ManifestationPhase.Departing => said!.Departing,
                _ => Equipped?.Text.CallHint ?? (AnyAcquired ? text.StatusInCase : text.StatusNone)
            };
        }
    }

    internal SpiritDefinition Spirit(string id) => roster.First(s => s.Id == id);
    internal ActionDefinition Call(SpiritDefinition spirit) => actions.Action(spirit.Action)!;

    /// <summary>A spirit's description with its call's reach, charge and the seconds of its first effect filled in.</summary>
    internal string Description(SpiritDefinition spirit)
    {
        ActionDefinition call = Call(spirit);
        string? first = call.Effects.Concat(call.SelfEffects).FirstOrDefault();
        return Template.Fill(spirit.Description, ("range", call.Delivery.Kind == DeliveryKind.Area ? call.Delivery.Range + call.Delivery.Radius : call.Delivery.Range),
            ("cost", Cost(spirit)), ("seconds", first is null ? 0 : mechanics.Effect(first)!.Duration));
    }

    /// <summary>Frees the spirit of one of this excursion's bells: a pact, and its welcome charges.</summary>
    internal bool Acquire(int bell)
    {
        if (bell < 0 || bell >= bells.Length || acquired.Contains(bells[bell].Spirit) || combat.Defeated) return Refuse(text.CannotMake);
        SpiritDefinition spirit = Spirit(bells[bell].Spirit);
        acquired.Add(spirit.Id);
        Revision++;
        supplies.RestoreSummon(spirit.WelcomeCharges);
        Announce(Template.Fill(text.Freed, ("spirit", spirit.Name), ("charges", spirit.WelcomeCharges)));
        return true;
    }

    /// <summary>Puts a freed spirit in the pact slot, or with null lets the equipped one rest; not while one visits.</summary>
    internal bool Equip(string? spirit, ulong revision)
    {
        if (revision != Revision) return Refuse(text.PactChanged);
        if (spirit is not null && !acquired.Contains(spirit) || spirit is null && Equipped is null)
            return Refuse(text.NoPactEquip);
        if (EquipReason.Length > 0) return Refuse(EquipReason);
        SpiritDefinition? resting = Equipped;
        Equipped = spirit is null ? null : Spirit(spirit);
        Revision++;
        Announce(Equipped is { } now ? Named(text.Equipped, now) : Named(text.Rests, resting!));
        return true;
    }

    // Direct UI claims use this rule both running and paused. No simulation or input clock here.
    internal void HandleIntents(ReadOnlySpan<ProductInputEvent> intents)
    {
        foreach (ProductInputEvent input in intents)
        {
            if (input.Kind != InputEventKind.DirectProductPayload || !input.Intent.Span.SequenceEqual("hotel.spirit.equip"u8)
                || !input.PayloadContract.Span.SequenceEqual("hotel.spirit.equip.v2"u8)) continue;
            try
            {
                using JsonDocument payload = JsonDocument.Parse(input.PayloadData);
                JsonElement root = payload.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("spirit", out var chosen)
                    || chosen.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)
                    || !root.TryGetProperty("revision", out var rev) || rev.ValueKind != JsonValueKind.Number || !rev.TryGetUInt64(out ulong revision))
                { Refuse(text.ChooseAgain); continue; }
                Equip(chosen.ValueKind == JsonValueKind.Null ? null : chosen.GetString(), revision);
            }
            catch (JsonException) { Refuse(text.Unreadable); }
        }
    }

    /// <summary>
    /// Calls the equipped spirit: the pact's own eligibility (overwhelmed, nothing equipped, already here, no charge, and
    /// for a call that reaches a resident, one under the reticle) is checked here, then Combat admits its action.
    /// </summary>
    internal bool Call()
    {
        if (combat.Defeated) return Refuse(text.CallOverwhelmed);
        if (Equipped is not { } spirit) return Refuse(AnyAcquired ? text.NotEquipped : text.NoPactCall);
        if (Active) return Refuse(Named(text.AlreadyHere, Visiting ?? spirit));
        if (supplies.Summon < Cost(spirit)) return Refuse(Named(text.NoCharge, spirit));
        ActionDefinition call = Call(spirit);
        if (call.Delivery.Kind is DeliveryKind.Hitscan or DeliveryKind.Melee or DeliveryKind.Projectile && combat.SpiritTarget(call.Delivery.Range) is null)
            return Refuse(Template.Fill(text.NoTarget, ("range", call.Delivery.Range)));
        if (!combat.CallPact(call, (landed, at) => Manifest(spirit, landed, at))) return false;
        Calls++;
        return true;
    }

    // The call has landed: the spirit arrives between the investigator and the resident it reached, or where its area
    // was centred, or beside the investigator for a call on themselves. A call that reached for a resident and met none
    // brings no one.
    private void Manifest(SpiritDefinition spirit, HotelEnemy? target, Vector3 at)
    {
        ActionDefinition call = Call(spirit);
        bool reaches = call.Delivery.Kind is DeliveryKind.Hitscan or DeliveryKind.Melee or DeliveryKind.Projectile;
        if (reaches && target is null) { Announce(Template.Fill(text.NoTarget, ("range", call.Delivery.Range))); return; }
        Visiting = spirit;
        Elapsed = 0;
        Phase = ManifestationPhase.Arriving;
        Vector3 focus = target?.Eye ?? (call.Delivery.Kind == DeliveryKind.Self ? player.Eye + player.Forward : at);
        Vector3 towardPlayer = Vector3.Distance(player.Eye, focus) > .01f ? Vector3.Normalize(player.Eye - focus) : -player.Forward;
        Vector3 side = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, towardPlayer));
        ManifestationTuning tuning = spirit.Manifestation;
        Destination = focus + towardPlayer * tuning.Approach + side * tuning.Side + new Vector3(0, tuning.Lift, 0);
        Entrance = Vector3.Lerp(player.Eye, Destination, tuning.EntranceFraction) - new Vector3(0, tuning.EntranceDrop, 0);
        Facing = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.Atan2(-towardPlayer.X, -towardPlayer.Z));
        Announce(Template.Fill(spirit.Text.CallResult, ("spirit", spirit.Name), ("resident", target?.Kind.Name ?? ""), ("cost", Cost(spirit))));
    }

    internal void Step(float seconds)
    {
        noticeTime = Math.Max(0, noticeTime - seconds);
        IdleTime += seconds;
        if (combat.Defeated)
        {
            if (Phase != ManifestationPhase.Absent) { Phase = ManifestationPhase.Absent; Announce(Named(text.Withdraws, Visiting!)); Visiting = null; }
            return;
        }
        if (Visiting is not { } spirit) return;
        Elapsed += seconds;
        Phase = Elapsed < spirit.Arrival ? ManifestationPhase.Arriving
            : Elapsed < spirit.Arrival + spirit.Hold ? ManifestationPhase.Holding
            : Elapsed < spirit.Visit ? ManifestationPhase.Departing
            : ManifestationPhase.Absent;
        if (Phase == ManifestationPhase.Absent) Visiting = null;
    }

    internal SpiritState Capture() => new(acquired.Order(StringComparer.Ordinal).ToArray(), Equipped?.Id);
    internal void Validate(SpiritState state)
    {
        if (state.Acquired is null || state.Acquired.Distinct().Count() != state.Acquired.Length ||
            state.Acquired.Any(id => roster.All(s => s.Id != id)) || state.Equipped is { } equipped && !state.Acquired.Contains(equipped))
            throw new InvalidOperationException("Checkpoint pacts are invalid.");
    }
    internal void Restore(SpiritState state)
    {
        Reset();
        acquired.UnionWith(state.Acquired);
        Equipped = state.Equipped is { } id ? Spirit(id) : null;
    }

    internal void Reset()
    {
        acquired.Clear(); Equipped = Visiting = null; Revision++; Phase = ManifestationPhase.Absent;
        Elapsed = IdleTime = noticeTime = 0; Calls = 0; Message = "";
    }

    private int Cost(SpiritDefinition spirit) => Call(spirit).Cost.Tracks.GetValueOrDefault(HotelSupplies.SummonTrack);
    private bool Refuse(string message) { Announce(message); return false; }
    private void Announce(string message) { Message = message; noticeTime = text.NoticeSeconds; }
    private static string Named(string template, SpiritDefinition spirit) => Template.Fill(template, ("spirit", spirit.Name));
}
