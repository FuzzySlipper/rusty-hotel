using System.Text.Json.Serialization;

namespace Hotel.Game.Floors.Mission;

/// <summary>
/// What a place on a floor is for. Arrival is the stair landing the player enters by; the objective is what the floor
/// asks of them. Keys grant an item a locked edge needs; a gate is the place in front of a locked door; supplies are
/// resource stops; hazards are encounters; the shortcut is the place a latch opens back toward arrival from. A hall is
/// a place on the way with no role of its own.
/// </summary>
internal enum MissionNodeKind { Arrival, Objective, Hall, Key, Gate, Bell, Landmark, Supplies, Hazard, Shortcut }

/// <summary>
/// How an edge may be crossed. Open: either way. Locked: either way while carrying its item. OneWay: from
/// <see cref="MissionEdge.From"/> to <see cref="MissionEdge.To"/> only. Latch: from From always, and back from To once
/// the player has stood at From and opened it.
/// </summary>
internal enum MissionEdgeKind { Open, Locked, OneWay, Latch }

/// <summary>A place on the floor's mission graph. A key <see cref="Grants"/> the item its lock needs.</summary>
internal sealed record MissionNode(string Id, MissionNodeKind Kind, string? Grants = null);

/// <summary>A way between two places. Open and locked edges are symmetric; one-way edges and latches run From to To.</summary>
internal sealed record MissionEdge(string Id, string From, string To, MissionEdgeKind Kind, string? Item = null);

/// <summary>The rules that grow a graph. Each is fail-atomic: a proposal that fails validation leaves the graph unchanged.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MissionRule>))]
internal enum MissionRule { LockKeyLoop, DetourLoop, GatedBranch, ResourceBeforeHazard, Bell, Landmark, Shortcut }

/// <summary>One accepted rule application: which rule, the stable key its draws used, and what it did.</summary>
internal sealed record MissionStep(MissionRule Rule, string Key, string Summary);

/// <summary>A floor's mission graph and how it was grown.</summary>
internal sealed record MissionGraph(MissionNode[] Nodes, MissionEdge[] Edges, MissionStep[] Steps)
{
    internal const string ArrivalId = "arrival";
    internal const string ObjectiveId = "objective";
    internal const string HallId = "hall";

    /// <summary>The graph every floor grows from: the stair landing, a hall, and the objective beyond it.</summary>
    internal static MissionGraph Initial() => new(
        [new(ArrivalId, MissionNodeKind.Arrival), new(HallId, MissionNodeKind.Hall), new(ObjectiveId, MissionNodeKind.Objective)],
        [new($"{ArrivalId}~{HallId}", ArrivalId, HallId, MissionEdgeKind.Open),
            new($"{HallId}~{ObjectiveId}", HallId, ObjectiveId, MissionEdgeKind.Open)], []);

    internal int Applied(MissionRule rule) => Steps.Count(s => s.Rule == rule);

    /// <summary>Writes the graph into a plan's canonical text, in id order whatever order it was built in.</summary>
    internal void Write(CanonicalText text)
    {
        foreach (MissionNode node in Nodes.OrderBy(n => n.Id, StringComparer.Ordinal))
            text.Line("mission.node", node.Id, node.Kind.ToString(), node.Grants ?? "");
        foreach (MissionEdge edge in Edges.OrderBy(e => e.Id, StringComparer.Ordinal))
            text.Line("mission.edge", edge.Id, edge.From, edge.To, edge.Kind.ToString(), edge.Item ?? "");
        for (int i = 0; i < Steps.Length; i++)
            text.Line("mission.step", CanonicalText.Number(i), Steps[i].Rule.ToString(), Steps[i].Key, Steps[i].Summary);
    }
}

/// <summary>Why a graph or a proposal was refused, with the node or edge concerned.</summary>
internal sealed record MissionProblem(string Code, string Detail, string? At = null)
{
    public override string ToString() => At is null ? $"{Code}: {Detail}" : $"{Code} ({At}): {Detail}";
}
