using Hotel.Game.Residents;
using Hotel.Game.Combat;
using Hotel.Game.Content;
using Hotel.Game.Scene.Kit;
using Hotel.Game.Supplies;
using Rusty.Engine;

namespace Hotel.Game.Floors.Content;

/// <summary>An item a find may hold, how many, and how often it is chosen.</summary>
internal sealed record ItemWeight(string Item, int Count, int Weight);

/// <summary>A resident kind, the module tags it may stand in, and how often it is chosen.</summary>
internal sealed record ResidentWeight(string Kind, Floors.Modules.ModuleTag[] Tags, int Weight);

/// <summary>
/// The pacing budgets every generated floor must meet: the healing finds reachable before each hazard, the item that
/// counts as recovery, the ammunition the floor holds in all (scarce, with the no-ammunition weapon as the fallback),
/// and the clear ground around the arrival: no resident's sight or attack reaches within <see cref="ArrivalMargin"/>
/// metres of where the player steps off the stairs.
/// </summary>
internal sealed record PacingTuning(string RecoveryItem, int RecoveryBeforeHazard, DepthCurve AmmoMinimum, DepthCurve AmmoMaximum, float ArrivalMargin);

/// <summary>
/// How a generated floor is furnished with content: the spirit its bell calls, the items objectives, stops and loose
/// finds draw from, how many finds a stop and the floor's spare rooms get, which residents stand where and how many
/// beyond the hazards, and the pacing budgets.
/// </summary>
/// <param name="FallbackLocation">The location label where the player stands in no named space.</param>
/// <param name="Displays">The socket fixture that shows each item where it lies.</param>
/// <param name="Doors">How locked doors, the shortcut's latch and their keys look and read.</param>
internal sealed record ContentTuning(string Spirit, string FallbackLocation, ItemWeight[] Objective, ItemWeight[] Supplies, int SuppliesPerStop,
    DepthCurve LooseSupplies, ResidentWeight[] Residents, DepthCurve ExtraResidents, PacingTuning Pacing, ItemDisplay[] Displays, DoorTuning Doors)
{
    internal string Display(string item) => Displays.First(d => d.Item == item).Fixture;

    internal const string Path = "floors/content.json";

    internal static ContentTuning Load(IEngineContext engine, ItemDefinition[] items, ResidentKind[] residents, FixtureCatalog fixtures)
    {
        ContentTuning t = Authored.Read(engine, Path, ContentJson.Default.ContentTuning);
        Template.Plain(Path, ("fallbackLocation", t.FallbackLocation));
        for (int i = 0; i < t.Displays.Length; i++)
        {
            FixtureDefinition? shown = fixtures.Fixtures.FirstOrDefault(f => f.Id == t.Displays[i].Fixture);
            Authored.Require(shown is { Mount: FixtureMount.Socket } && shown.Parts.Any(p => p.Find) && shown.Sockets?.ContainsKey("focus") == true,
                Path, $"displays[{i}].fixture", $"'{t.Displays[i].Fixture}' must be a socket fixture with find parts and a focus socket.");
        }
        Template.Plain(Path, ("doors.lockedLabel", t.Doors.LockedLabel), ("doors.latchLabel", t.Doors.LatchLabel), ("doors.latchPrompt", t.Doors.LatchPrompt));
        Template.Check(Path, "doors.lockedPrompt", t.Doors.LockedPrompt, "key");
        Template.Check(Path, "doors.keyName", t.Doors.KeyName, "room");
        FixtureDefinition? keyShown = fixtures.Fixtures.FirstOrDefault(f => f.Id == t.Doors.KeyFixture);
        Authored.Require(keyShown is { Mount: FixtureMount.Socket } && keyShown.Parts.Any(p => p.Find) && keyShown.Sockets?.ContainsKey("focus") == true,
            Path, "doors.keyFixture", $"'{t.Doors.KeyFixture}' must be a socket fixture with find parts and a focus socket.");
        foreach (string item in t.Objective.Concat(t.Supplies).Select(w => w.Item).Distinct())
            Authored.Require(t.Displays.Count(d => d.Item == item) == 1, Path, "displays", $"'{item}' needs exactly one display fixture.");
        void Items(string field, ItemWeight[] weights, bool deposit)
        {
            Authored.Require(weights.Any(w => w.Weight > 0), Path, field, "needs an item with a positive weight.");
            for (int i = 0; i < weights.Length; i++)
            {
                ItemDefinition? item = items.FirstOrDefault(d => d.Id == weights[i].Item);
                Authored.Require(item is not null, Path, $"{field}[{i}].item", $"unknown item '{weights[i].Item}'.");
                Authored.Require(item!.Deposit == deposit, Path, $"{field}[{i}].item",
                    deposit ? $"'{item.Id}' is not kept for the refuge (deposit)." : $"'{item.Id}' is kept for the refuge, not a supply.");
                Authored.Within(Path, $"{field}[{i}].count", weights[i].Count, 1, item.StackLimit);
                Authored.AtLeast(Path, $"{field}[{i}].weight", weights[i].Weight, 0);
            }
        }
        Items("objective", t.Objective, true);
        Items("supplies", t.Supplies, false);
        Authored.AtLeast(Path, "suppliesPerStop", t.SuppliesPerStop, 1);
        t.LooseSupplies.Validate(Path, "looseSupplies");
        t.ExtraResidents.Validate(Path, "extraResidents");
        Authored.Require(t.Residents.Any(r => r.Weight > 0), Path, "residents", "needs a resident with a positive weight.");
        for (int i = 0; i < t.Residents.Length; i++)
        {
            Authored.Require(residents.Any(k => k.Id == t.Residents[i].Kind), Path, $"residents[{i}].kind", $"unknown resident kind '{t.Residents[i].Kind}'.");
            Authored.Require(t.Residents[i].Tags.Length > 0, Path, $"residents[{i}].tags", "names the module tags it may stand in.");
            Authored.AtLeast(Path, $"residents[{i}].weight", t.Residents[i].Weight, 0);
        }
        Authored.Require(items.Any(i => i.Id == t.Pacing.RecoveryItem && i.Restores(Hotel.Game.Supplies.HotelSupplies.HealthTrack) > 0), Path, "pacing.recoveryItem",
            $"'{t.Pacing.RecoveryItem}' must be a healing item.");
        Authored.AtLeast(Path, "pacing.recoveryBeforeHazard", t.Pacing.RecoveryBeforeHazard, 0);
        Authored.AtLeast(Path, "pacing.arrivalMargin", t.Pacing.ArrivalMargin, 0);
        t.Pacing.AmmoMinimum.Validate(Path, "pacing.ammoMinimum");
        t.Pacing.AmmoMaximum.Validate(Path, "pacing.ammoMaximum");
        Authored.Require(t.Pacing.AmmoMaximum.Base >= t.Pacing.AmmoMinimum.Base && t.Pacing.AmmoMaximum.Max >= t.Pacing.AmmoMinimum.Max,
            Path, "pacing.ammoMaximum", "never below the minimum.");
        return t;
    }
}

/// <summary>
/// The look and wording of a generated floor's locked doors, its shortcut latch, and the keys: a key is named for the
/// room its door guards.
/// </summary>
internal sealed record DoorTuning(string Material, string HandleMaterial, string LockedLabel, string LockedPrompt, string LatchLabel,
    string LatchPrompt, string KeyName, string KeyFixture);

/// <summary>The fixture an item is shown with where it lies.</summary>
internal sealed record ItemDisplay(string Item, string Fixture);

/// <summary>Notices generated floors may show, each used at most once per floor.</summary>
internal sealed record FloorReadings(FloorReading[] Readings)
{
    internal const string Path = "floors/readings.json";

    internal static FloorReadings Load(IEngineContext engine)
    {
        FloorReadings readings = Authored.Read(engine, Path, ContentJson.Default.FloorReadings);
        for (int i = 0; i < readings.Readings.Length; i++)
        {
            FloorReading r = readings.Readings[i];
            Template.Plain(Path, ($"readings[{i}].label", r.Label), ($"readings[{i}].title", r.Title), ($"readings[{i}].text", r.Text));
        }
        string? repeated = readings.Readings.GroupBy(r => r.Id).FirstOrDefault(g => g.Count() > 1)?.Key;
        Authored.Require(repeated is null, Path, "readings", $"id '{repeated}' appears more than once.");
        return readings;
    }
}

internal sealed record FloorReading(string Id, string Label, string Title, string Text);
