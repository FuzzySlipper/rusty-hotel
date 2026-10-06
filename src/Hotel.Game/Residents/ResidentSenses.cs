using System.Numerics;
using Hotel.Game.Actions;
using Hotel.Game.Combat;
using Rusty.Engine;

namespace Hotel.Game.Residents;

/// <summary>What one resident is aware of: the hostile body it last noticed, where, and for how much longer.</summary>
internal sealed class Awareness
{
    internal IActionActor? Target { get; private set; }
    internal Vector3 LastSeen { get; private set; }
    internal float Remaining { get; private set; }
    /// <summary>An ambusher has sprung: from now on it perceives like any other resident.</summary>
    internal bool Sprung { get; set; }

    internal void Notice(IActionActor target, float memory)
    {
        Target = target;
        LastSeen = target.Position;
        Remaining = memory;
    }

    /// <summary>Lets awareness fade by admitted seconds; a target out of mind, or fallen, is forgotten.</summary>
    internal void Fade(float seconds)
    {
        Remaining -= seconds;
        if (Remaining <= 0 || Target is { Alive: false }) Forget();
    }

    internal void Forget() { Target = null; Remaining = 0; }
    internal void Reset() { Forget(); Sprung = false; LastSeen = default; }
}

/// <summary>
/// Residents' senses, one Engine visibility query per admitted step for every resident at once: each observer's cone
/// (range and field of view, from where it faces) against every living body, occluded by the hotel's collision. A hostile
/// body within near sense with a clear line is noticed whichever way the resident faces. The nearest noticed hostile
/// becomes the resident's target; awareness of it lasts the kind's memory after the last sighting.
/// </summary>
internal sealed class ResidentSenses(IEngineContext engine, SpatialSession session, ActionResolution resolution, FactionCatalog factions)
{
    // Pairs read per query page; a floor holds few bodies, so one page is the usual case.
    private const uint PageSize = 64;

    internal void Update(IReadOnlyList<HotelEnemy> residents, IReadOnlyList<IActionActor> bodies, Func<IActionActor, string> faction, float seconds)
    {
        HotelEnemy[] watching = residents.Where(r => r.Alive && !r.Stats.Effects.Held).ToArray();
        IActionActor[] living = bodies.Where(b => b.Alive).ToArray();
        HashSet<(ulong Observer, ulong Target)> seen = Visible(watching, living);
        foreach (HotelEnemy resident in residents)
        {
            resident.Awareness.Fade(seconds);
            if (!watching.Contains(resident)) continue;
            ResidentKind kind = resident.Kind;
            IActionActor? noticed = living
                .Where(b => b.Entity != resident.Entity && factions.AreHostile(kind.Faction, faction(b)))
                .Where(b => seen.Contains((resident.Entity, b.Entity)) || Near(resident, b, kind.Perception.NearSense))
                .Where(b => kind.Movement.Ambush is not { } ambush || resident.Awareness.Sprung || Near(resident, b, ambush.Trigger))
                .MinBy(b => Vector3.Distance(b.Position, resident.Position));
            if (noticed is null) continue;
            if (kind.Movement.Ambush is not null) resident.Awareness.Sprung = true;
            resident.Awareness.Notice(noticed, kind.Perception.Memory);
        }
    }

    private bool Near(HotelEnemy resident, IActionActor body, float radius) =>
        radius > 0 && Vector3.Distance(body.Position, resident.Position) <= radius && resolution.Clear(resident.Eye, body.Eye);

    // Every observer-target pair the Engine finds visible, read page by page.
    private HashSet<(ulong, ulong)> Visible(HotelEnemy[] watching, IActionActor[] living)
    {
        HashSet<(ulong, ulong)> visible = [];
        if (watching.Length == 0 || living.Length == 0) return visible;
        PerceptionObserver[] observers = watching.Select(r => new PerceptionObserver(r.Entity, r.Eye,
            new Vector3(MathF.Sin(r.Yaw), 0, -MathF.Cos(r.Yaw)), r.Kind.Perception.Range,
            r.Kind.Perception.FieldOfView >= 360 ? -1 : Math.Cos(r.Kind.Perception.FieldOfView * Math.PI / 360), 1)).ToArray();
        PerceptionTarget[] targets = living.Select(b => new PerceptionTarget(b.Entity, b.Eye)).ToArray();
        PerceptionQueryRequest query = new(session, observers, targets, ReadOnlyMemory<SpatialEntityCollider>.Empty, 0, 0, PageSize);
        PerceptionReadoutResult receipt;
        do
        {
            receipt = engine.Perception.QueryVisibility(query);
            foreach (PerceptionPair pair in receipt.Pairs.Span)
                if (pair.Kind == PerceptionPairKind.Visible && pair.Observer != pair.Target) visible.Add((pair.Observer, pair.Target));
            if (receipt.HasNextPairCursor)
                query = query with { PairCursor = receipt.NextPairCursor, ExpectedProjectionIdentity = receipt.ProjectionIdentity };
        } while (receipt.HasNextPairCursor);
        return visible;
    }
}
