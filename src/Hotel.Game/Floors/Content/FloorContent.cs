namespace Hotel.Game.Floors.Content;

/// <summary>Why a find is on the floor: the floor's objective, a key for a lock, a resource stop, or loose supplies.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<FindRole>))]
internal enum FindRole { Objective, Key, Supplies, Loose }

/// <summary>
/// One find at a socket. Its id is derived from its placement and socket, so it is stable across loads. A key find
/// <see cref="Grants"/> the mission item its lock needs; other finds name an item and count.
/// </summary>
internal sealed record PlacedFind(string Id, FindRole Role, string Socket, string? Item, int Count, string? Grants);

/// <summary>A resident of a kind standing on a post, guarding the region it stands in.</summary>
internal sealed record PlacedResident(string Id, string Kind, string Socket, string Region);

/// <summary>A notice at a socket, with the authored reading it shows.</summary>
internal sealed record PlacedReading(string Id, string Socket, string Reading);

/// <summary>The spirit bell: which spirit, its socket, and the place its text names.</summary>
internal sealed record PlacedBell(string Spirit, string Socket, string Place);

/// <summary>
/// A floor's resolved content: where the player arrives and faces, every find, resident, reading and bell, and its
/// landmarks. Pure data, every choice made when it was placed.
/// </summary>
internal sealed record FloorContent(string Arrival, float ArrivalYaw, PlacedFind[] Finds, PlacedResident[] Residents,
    PlacedReading[] Readings, PlacedBell? Bell, string[] Landmarks)
{
    internal void Write(CanonicalText text)
    {
        text.Line("content.arrival", Arrival, CanonicalText.Number(ArrivalYaw));
        foreach (PlacedFind f in Finds.OrderBy(f => f.Id, StringComparer.Ordinal))
            text.Line("content.find", f.Id, f.Role.ToString(), f.Socket, f.Item ?? "", CanonicalText.Number(f.Count), f.Grants ?? "");
        foreach (PlacedResident r in Residents.OrderBy(r => r.Id, StringComparer.Ordinal)) text.Line("content.resident", r.Id, r.Kind, r.Socket, r.Region);
        foreach (PlacedReading r in Readings.OrderBy(r => r.Id, StringComparer.Ordinal)) text.Line("content.reading", r.Id, r.Socket, r.Reading);
        if (Bell is { } bell) text.Line("content.bell", bell.Spirit, bell.Socket, bell.Place);
        text.Set("content.landmark", Landmarks);
    }
}
