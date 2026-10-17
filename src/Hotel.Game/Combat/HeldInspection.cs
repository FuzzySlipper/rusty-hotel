using System.Numerics;
using Hotel.Game.Content;
using Rusty.Engine;
using Rusty.Engine.Debugging;

namespace Hotel.Game.Combat;

/// <summary>
/// Developer inspection of the held assembly, drawn as Engine overlay sets (camera space, shown only while inspection
/// shows them) and named as Engine inspection targets: the stance guide, the hand fit's contact probes, and the shown
/// look's grips for inspection views to frame.
/// </summary>
internal sealed class HeldInspection(IEngineContext engine, HeldCatalog held)
{
    /// <summary>The stance guide's overlay set.</summary>
    internal const string GuideSet = "held.guide";

    /// <summary>The hand fit's contact probes' overlay set.</summary>
    internal const string ContactSet = "hands.contact";

    /// <summary>The grip check's overlay set (<see cref="GripLandmarks"/>).</summary>
    internal const string LandmarkSet = "held.landmarks";

    // Developer drawing sizes (metres): the guide's grip marker and its length and up-side lines; how much around each
    // grip a framing shows; a landmark's marker and how much around it a framing shows.
    private const float Marker = .016f, Length = .25f, Across = .07f, MainFrame = .12f, OffFrame = .1f, LandmarkMarker = .008f, LandmarkFrame = .06f;
    private static readonly Color Target = new(1, .62f, .1f, 1), Actual = new(.15f, .85f, 1, 1),
        Clear = new(.3f, 1, .4f, 1), Into = new(1, .2f, .2f, 1), Text = new(1, 1, 1, 1);
    private bool guiding, contact, landmarks;

    /// <summary>
    /// Publishes the stance guide for a look placed so (camera space), or removes it for null: the stance's grip, length
    /// and up side (amber), and the look's own where it is shown (blue).
    /// </summary>
    internal void Guide(string? look, (Vector3 At, Quaternion Turn) placed)
    {
        if (look is null)
        {
            if (guiding) engine.Graphics.PublishOverlaySet(new(GuideSet, Array.Empty<OverlayItem>()));
            guiding = false;
            return;
        }
        HeldModel model = held.Model(look);
        HeldStance stance = held.Stances.Stances[model.Stance];
        Quaternion target = stance.Turn(model.Along);
        Vector3 grip = Authored.Vector(stance.Grip);
        Vector3 gripNow = placed.At + Vector3.Transform(Authored.Vector(model.Grips.Main) * model.Scale, placed.Turn);
        OverlayItem[] Lines(Vector3 at, Quaternion turn, float size, Color color) =>
        [
            OverlayItem.Marker(OverlayPlace.Camera, at, size, color, onTop: true),
            OverlayItem.Segment(OverlayPlace.Camera, at, at + Vector3.Transform(model.Along.TipVector, turn) * Length, color, onTop: true),
            OverlayItem.Segment(OverlayPlace.Camera, at, at + Vector3.Transform(model.Along.UpVector, turn) * Across, color, onTop: true),
        ];
        engine.Graphics.PublishOverlaySet(new(GuideSet, [.. Lines(grip, target, Marker, Target), .. Lines(gripNow, placed.Turn, Marker * .8f, Actual)]));
        guiding = true;
    }

    /// <summary>
    /// A digit's probes as the hand fit measured them (camera space): a wire sphere at the digit's radius each, red past
    /// the model's skin, and a label at the tip with the report's own words.
    /// </summary>
    internal static void Probes(List<OverlayItem> marks, ReadOnlySpan<Vector3> probes, ReadOnlySpan<float> skins, float radius, string label)
    {
        for (int p = 0; p < probes.Length; p++)
            marks.Add(OverlayItem.WireSphere(OverlayPlace.Camera, probes[p], radius, skins[p] < 0 ? Into : Clear, onTop: true));
        if (probes.Length > 0) marks.Add(OverlayItem.Label(OverlayPlace.Camera, probes[^1], label, Text, onTop: true));
    }

    /// <summary>Publishes the hand fit's last report as markers, replacing the one before.</summary>
    internal void Contact(List<OverlayItem> marks)
    {
        engine.Graphics.PublishOverlaySet(new(ContactSet, marks.ToArray()));
        contact = marks.Count > 0;
    }

    /// <summary>Removes the contact markers (what they measured is no longer shown).</summary>
    internal void ClearContact()
    {
        if (contact) Contact([]);
    }

    /// <summary>
    /// Publishes the grip check's pairings (camera space), or removes them when there are none: a segment from each hand
    /// point to its landmark (to where the palm's normal points, for a facing), green within tolerance and red outside,
    /// a marker on each landmark and a label with the measure.
    /// </summary>
    internal void Landmarks(List<GripLandmarks.Pairing> pairings)
    {
        if (pairings.Count == 0)
        {
            if (landmarks) engine.Graphics.PublishOverlaySet(new(LandmarkSet, Array.Empty<OverlayItem>()));
            landmarks = false;
            return;
        }
        List<OverlayItem> items = [];
        foreach (GripLandmarks.Pairing p in pairings)
        {
            Color color = p.Within ? Clear : Into;
            items.Add(OverlayItem.Segment(OverlayPlace.Camera, p.From, p.To, color, onTop: true));
            items.Add(OverlayItem.Marker(OverlayPlace.Camera, p.To, LandmarkMarker, color, onTop: true));
            items.Add(OverlayItem.Label(OverlayPlace.Camera, (p.From + p.To) / 2, p.Facing ? $"{p.Name} {p.Measure:0}°" : $"{p.Name} {p.Measure * 1000:0.0} mm", Text, onTop: true));
        }
        engine.Graphics.PublishOverlaySet(new(LandmarkSet, items.ToArray()));
        landmarks = true;
    }

    /// <summary>
    /// The shown look's grips as inspection targets (camera space, framed along the item's turn): the main grip from
    /// the side and the off grip from underneath.
    /// </summary>
    internal IReadOnlyList<InspectionTarget> Targets(string look, (Vector3 At, Quaternion Turn) placed)
    {
        HeldModel model = held.Model(look);
        (Vector3 main, Vector3? off, Quaternion item, float[]? _) = HeldGrip.At(held, model, placed);
        List<InspectionTarget> targets = [new("main-grip", OverlayPlace.Camera, main) { Rotation = item, Radius = MainFrame, Framing = "side" }];
        if (off is { } support)
            targets.Add(new("off-grip", OverlayPlace.Camera, support) { Rotation = item, Radius = OffFrame, Framing = "underside" });
        // The look's landmarks, each framed close from the side.
        foreach (var (field, at) in GripLandmarks.Placed(model, placed))
            targets.Add(new(field, OverlayPlace.Camera, at) { Rotation = item, Radius = LandmarkFrame, Framing = "side" });
        return targets;
    }
}
