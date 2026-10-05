using System.Numerics;
using System.Text.Json;
using Hotel.Game.Combat;
using Hotel.Game.Expedition;
using Hotel.Game.Player;
using Hotel.Game.Scene;
using Hotel.Game.Supplies;
using Rusty.Engine;

namespace Hotel.Game.Spirits;

internal enum ManifestationPhase { Absent, Arriving, Hushing, Departing }

/// <summary>One pact, equipped choice and brief intervention; resources and combat keep their owners.</summary>
internal sealed class HotelSpirit(SpiritDefinition definition, Vector3 bell, HotelSupplies supplies, HotelCombat combat, HotelPlayer player)
{
    internal SpiritDefinition Definition => definition;
    /// <summary>Where this excursion keeps the spirit's bell before the pact.</summary>
    internal Vector3 Bell => bell;
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
    internal bool Active => Phase != ManifestationPhase.Absent;
    internal string EquipReason => combat.Defeated ? "Overwhelmed · return to the refuge checkpoint first." : Active ? "Wait for Hushwing to depart." : "";
    internal string Status => combat.Defeated ? "Unavailable while overwhelmed" : Active ? Phase.ToString() : Equipped ? "Q · Hush a visible resident" : Acquired ? "Equip in I · Spirits" : "No pact";

    internal bool Acquire()
    {
        if (Acquired || combat.Defeated) return Refuse("The pact cannot be made now.");
        Acquired = true;
        Revision++;
        supplies.RestoreSummon(definition.WelcomeCharges);
        Announce($"{definition.Name} is free · +{definition.WelcomeCharges} summon · I → Spirits to equip");
        return true;
    }

    internal bool Equip(bool equipped, ulong revision)
    {
        if (revision != Revision) return Refuse("The pact changed. Select it again.");
        if (!Acquired) return Refuse("No pact made. Find the bell in the linen room.");
        if (EquipReason.Length > 0) return Refuse(EquipReason);
        Equipped = equipped;
        Revision++;
        Announce(equipped ? $"{definition.Name} equipped · Q calls it toward the resident under your reticle" : $"{definition.Name} rests in the field case");
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
                { Refuse("Choose the spirit again."); continue; }
                Equip(equip.GetBoolean(), revision);
            }
            catch (JsonException) { Refuse("The spirit choice could not be read."); }
        }
    }

    internal bool Call()
    {
        if (combat.Defeated) return Refuse("Overwhelmed · spirits cannot answer · R to return to the refuge");
        if (!Equipped) return Refuse(Acquired ? "Equip Hushwing in I → Spirits first" : "No pact made · explore the linen room");
        if (Active) return Refuse("Hushwing is already here");
        if (supplies.Summon < definition.Cost) return Refuse("No summon charge · Hushwing cannot answer");
        HotelEnemy? target = combat.SpiritTarget(definition.Range);
        if (target is null) return Refuse("Aim at a visible resident within six paces · no charge spent");
        if (!supplies.SpendSummon(definition.Cost)) return false;
        combat.Interrupt(target, definition.Arrival + definition.Hold + definition.Departure);
        Calls++;
        Elapsed = 0;
        Phase = ManifestationPhase.Arriving;
        Vector3 towardPlayer = Vector3.Normalize(player.Eye - target.Eye);
        Vector3 side = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, towardPlayer));
        Destination = target.Eye + towardPlayer * .25f + side * .65f + new Vector3(0, .15f, 0);
        Entrance = Vector3.Lerp(player.Eye, Destination, .2f) + new Vector3(0, -.25f, 0);
        Facing = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.Atan2(-towardPlayer.X, -towardPlayer.Z));
        Announce($"{definition.Name} hushes {target.Kind.Name} · −{definition.Cost} summon");
        return true;
    }

    internal void Step(float seconds)
    {
        noticeTime = Math.Max(0, noticeTime - seconds);
        IdleTime += seconds;
        if (combat.Defeated)
        {
            if (Active) { Phase = ManifestationPhase.Absent; Announce("Hushwing withdraws · R to return to the refuge checkpoint"); }
            return;
        }
        if (!Active) return;
        Elapsed += seconds;
        Phase = Elapsed < definition.Arrival ? ManifestationPhase.Arriving
            : Elapsed < definition.Arrival + definition.Hold ? ManifestationPhase.Hushing
            : Elapsed < definition.Arrival + definition.Hold + definition.Departure ? ManifestationPhase.Departing
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
    private void Announce(string message) { Message = message; noticeTime = 5; }
}
