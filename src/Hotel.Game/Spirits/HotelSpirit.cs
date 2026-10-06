using System.Numerics;
using System.Text.Json;
using Hotel.Game.Combat;
using Hotel.Game.Content;
using Hotel.Game.Expedition;
using Hotel.Game.Player;
using Hotel.Game.Scene;
using Hotel.Game.Supplies;
using Rusty.Engine;

namespace Hotel.Game.Spirits;

internal enum ManifestationPhase { Absent, Arriving, Holding, Departing }

/// <summary>One pact, equipped choice and brief intervention; resources and combat keep their owners.</summary>
internal sealed class HotelSpirit(SpiritDefinition definition, SpiritMessages text, SpiritBellPlacement placement,
    HotelSupplies supplies, HotelCombat combat, HotelPlayer player, Actions.ActionDefinition call, Mechanics.MechanicsDefinition mechanics)
{
    // The call's reach, its charge, and how long its hold keeps a resident still.
    private float Range => call.Delivery.Range;
    private int Cost => call.Cost.Tracks.GetValueOrDefault(HotelSupplies.SummonTrack);
    private float Interrupt => call.Effects.Select(e => mechanics.Effect(e)!).First(e => e.Hold is not null).Duration;
    internal SpiritDefinition Definition => definition;
    /// <summary>Where this excursion keeps the spirit's bell before the pact.</summary>
    internal Vector3 Bell { get; } = Authored.Vector(placement.Point);
    internal string BellLabel => Named(text.BellLabel);
    internal string Description => Template.Fill(definition.Description, ("place", placement.Place),
        ("range", Range), ("cost", Cost), ("interrupt", Interrupt));
    /// <summary>The HUD's held-spirit line.</summary>
    internal string HudLabel => Equipped ? definition.Name : Acquired ? text.HudInCase : text.HudNone;
    internal bool Acquired { get; private set; }
    internal bool Equipped { get; private set; }
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
    /// <summary>A call is under way through the pact slot, or the creature is here.</summary>
    internal bool Active => Phase != ManifestationPhase.Absent || combat.PactUser.Busy;
    internal string EquipReason => combat.Defeated ? text.EquipOverwhelmed : Active ? Named(text.EquipWhileActive) : "";
    internal string Status => combat.Defeated ? text.StatusOverwhelmed : Phase switch
    {
        ManifestationPhase.Arriving => definition.Text.Arriving,
        ManifestationPhase.Holding => definition.Text.Holding,
        ManifestationPhase.Departing => definition.Text.Departing,
        _ => Equipped ? definition.Text.CallHint : Acquired ? text.StatusInCase : text.StatusNone
    };

    internal bool Acquire()
    {
        if (Acquired || combat.Defeated) return Refuse(text.CannotMake);
        Acquired = true;
        Revision++;
        supplies.RestoreSummon(definition.WelcomeCharges);
        Announce(Template.Fill(text.Freed, ("spirit", definition.Name), ("charges", definition.WelcomeCharges)));
        return true;
    }

    internal bool Equip(bool equipped, ulong revision)
    {
        if (revision != Revision) return Refuse(text.PactChanged);
        if (!Acquired) return Refuse(Template.Fill(text.NoPactEquip, ("place", placement.Place)));
        if (EquipReason.Length > 0) return Refuse(EquipReason);
        Equipped = equipped;
        Revision++;
        Announce(Named(equipped ? text.Equipped : text.Rests));
        return true;
    }

    // Direct UI claims use this rule both running and paused. No simulation or input clock here.
    internal void HandleIntents(ReadOnlySpan<ProductInputEvent> intents)
    {
        foreach (ProductInputEvent input in intents)
        {
            if (input.Kind != InputEventKind.DirectProductPayload || !input.Intent.Span.SequenceEqual("hotel.spirit.equip"u8)
                || !input.PayloadContract.Span.SequenceEqual("hotel.spirit.equip.v1"u8)) continue;
            try
            {
                using JsonDocument payload = JsonDocument.Parse(input.PayloadData);
                JsonElement root = payload.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("equipped", out var equip)
                    || equip.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                    || !root.TryGetProperty("revision", out var rev) || rev.ValueKind != JsonValueKind.Number || !rev.TryGetUInt64(out ulong revision))
                { Refuse(text.ChooseAgain); continue; }
                Equip(equip.GetBoolean(), revision);
            }
            catch (JsonException) { Refuse(text.Unreadable); }
        }
    }

    internal bool Call()
    {
        if (combat.Defeated) return Refuse(text.CallOverwhelmed);
        if (!Equipped) return Refuse(Acquired ? Named(text.NotEquipped) : Template.Fill(text.NoPactCall, ("place", placement.Place)));
        if (Active) return Refuse(Named(text.AlreadyHere));
        if (supplies.Summon < Cost) return Refuse(Named(text.NoCharge));
        if (combat.SpiritTarget(Range) is null) return Refuse(Template.Fill(text.NoTarget, ("range", Range)));
        // The call is an action through the pact: Combat admits and times it and lands its hold; the visit follows.
        if (!combat.CallPact(call, Manifest)) return false;
        Calls++;
        return true;
    }

    // The call has landed: on a resident, the creature arrives between it and the investigator; on nothing, it does not come.
    private void Manifest(HotelEnemy? target)
    {
        if (target is null) { Announce(Template.Fill(text.NoTarget, ("range", Range))); return; }
        Elapsed = 0;
        Phase = ManifestationPhase.Arriving;
        Vector3 towardPlayer = Vector3.Normalize(player.Eye - target.Eye);
        Vector3 side = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, towardPlayer));
        ManifestationTuning at = definition.Manifestation;
        Destination = target.Eye + towardPlayer * at.Approach + side * at.Side + new Vector3(0, at.Lift, 0);
        Entrance = Vector3.Lerp(player.Eye, Destination, at.EntranceFraction) - new Vector3(0, at.EntranceDrop, 0);
        Facing = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.Atan2(-towardPlayer.X, -towardPlayer.Z));
        Announce(Template.Fill(definition.Text.CallResult, ("spirit", definition.Name), ("resident", target.Kind.Name), ("cost", Cost)));
    }

    internal void Step(float seconds)
    {
        noticeTime = Math.Max(0, noticeTime - seconds);
        IdleTime += seconds;
        if (combat.Defeated)
        {
            if (Active) { Phase = ManifestationPhase.Absent; Announce(Named(text.Withdraws)); }
            return;
        }
        if (!Active) return;
        Elapsed += seconds;
        Phase = Elapsed < definition.Arrival ? ManifestationPhase.Arriving
            : Elapsed < definition.Arrival + definition.Hold ? ManifestationPhase.Holding
            : Elapsed < definition.Interrupt ? ManifestationPhase.Departing
            : ManifestationPhase.Absent;
    }

    internal SpiritState Capture() => new(definition.Id, Acquired, Equipped);
    internal void Validate(SpiritState state)
    {
        if (state.Id != definition.Id || (state.Equipped && !state.Acquired))
            throw new InvalidOperationException("Checkpoint pact is invalid.");
    }
    internal void Restore(SpiritState state)
    {
        Reset();
        Acquired = state.Acquired; Equipped = state.Equipped;
    }

    internal void Reset()
    {
        Acquired = Equipped = false; Revision++; Phase = ManifestationPhase.Absent;
        Elapsed = IdleTime = noticeTime = 0; Calls = 0; Message = "";
    }
    private bool Refuse(string message) { Announce(message); return false; }
    private void Announce(string message) { Message = message; noticeTime = text.NoticeSeconds; }
    private string Named(string template) => Template.Fill(template, ("spirit", definition.Name));
}
