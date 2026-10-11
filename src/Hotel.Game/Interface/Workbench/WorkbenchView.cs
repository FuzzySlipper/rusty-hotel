using System.Text.Json.Serialization;

namespace Hotel.Game.Interface.Workbench;

/// <summary>
/// What a developer workbench panel draws, as the product describes it (contract <c>tunables.v1</c>): choosers
/// (<see cref="Fields"/>), a timeline of keys over <see cref="Duration"/> seconds, groups of slider and toggle controls,
/// and the console command templates the panel sends (<c>{id}</c>, <c>{value}</c>, <c>{seconds}</c>, <c>{speed}</c>,
/// <c>{key}</c> and each field's id are filled in). The panel knows nothing else about the product.
/// </summary>
internal sealed record WorkbenchView(string Contract, string Title, WorkbenchField[] Fields, WorkbenchKey[] Keys, float Duration, int Key,
    WorkbenchGroup[] Groups, Dictionary<string, string> Commands);

/// <summary>A chooser: its current value among its options.</summary>
internal sealed record WorkbenchField(string Id, string Label, string Value, string[] Options);

/// <summary>A key on the timeline: its index, name and moment (seconds from the start).</summary>
internal sealed record WorkbenchKey(int Index, string Label, float Time);

/// <summary>A titled group of controls.</summary>
internal sealed record WorkbenchGroup(string Id, string Title, WorkbenchControl[] Controls);

/// <summary>One control: a <c>slider</c> between <see cref="Min"/> and <see cref="Max"/> by <see cref="Step"/>, or a <c>toggle</c> (0 or 1).</summary>
internal sealed record WorkbenchControl(string Id, string Label, string Kind, float Min, float Max, float Step, float Value);

/// <summary>What has changed since the content loaded, per content file: pasteable JSON fragments, each with where it goes.</summary>
internal sealed record WorkbenchChanges(Dictionary<string, string[]> Files);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WorkbenchView))]
[JsonSerializable(typeof(WorkbenchChanges))]
internal sealed partial class WorkbenchJson : JsonSerializerContext;
