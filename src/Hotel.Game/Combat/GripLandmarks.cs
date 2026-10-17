using System.Numerics;
using Hotel.Game.Content;
using Rusty.Engine;

namespace Hotel.Game.Combat;

/// <summary>
/// The developer grip check: which of the drawn hands' own points must meet which of a look's landmarks
/// (<see cref="HeldLandmarks"/>) for the hold to be the right one, as Engine landmark pairs. Contact only says whether
/// digits clear the model; these say whether the index pad is on the trigger, the web up under the back strap, the pinky
/// at the butt, the off palm under the fore-end, and each palm facing what it holds. The Engine measures them on the
/// poses it drew (<c>engine.inspect.landmarks held.landmarks</c>), wherever the look is shown.
/// </summary>
internal static class GripLandmarks
{
    /// <summary>
    /// The pairs a look's landmarks call for: on the held model's node (<paramref name="held"/>, in the model's own
    /// space) against the main hand and, while it holds the look, the off hand. None for a look without landmarks.
    /// </summary>
    internal static LandmarkPair[] Pairs(HeldModel model, ulong held, HandRig main, HandRig? off, ArmDigits digits)
    {
        if (model.Landmarks is not { } marks) return [];
        HandLandmarks tuning = digits.Landmarks;
        OverlayPlace place = OverlayPlace.Model(held);
        Landmark At(float[] point, Vector3 axis = default) => Landmark.At(place, Authored.Vector(point), axis);
        Landmark Palm(HandRig hand) => hand.Palm(tuning.Palm, tuning.PalmDepth);
        List<LandmarkPair> pairs = [];
        if (marks.Trigger is { } trigger)
            pairs.Add(LandmarkPair.Distance("trigger", main.Pad(Digit.Index, digits.Fit.Tip), At(trigger), tuning.Trigger));
        if (marks.BackstrapTop is { } top)
            pairs.Add(LandmarkPair.Distance("backstrap", main.Web(tuning.Web), At(top), tuning.Backstrap));
        if (marks.Butt is { } butt)
            pairs.Add(LandmarkPair.Distance("butt", main.Joint(Digit.Pinky, 1), At(butt), tuning.Butt));
        // The handle runs through the main grip from the back strap's top toward the butt (down the model without a
        // butt); the palm wraps it, facing the nearest point of that line.
        if (marks.BackstrapTop is { } from)
        {
            Vector3 along = marks.Butt is { } toward ? Authored.Vector(toward) - Authored.Vector(from) : -Vector3.UnitY;
            if (along.LengthSquared() > 1e-10f)
                pairs.Add(LandmarkPair.Facing("main-facing", Palm(main), At(model.Grips.Main, Vector3.Normalize(along)), LandmarkAim.Line, tuning.Facing));
        }
        if (off is { } support && marks.ForeEnd is { } foreEnd)
        {
            pairs.Add(LandmarkPair.Distance("fore-end", Palm(support), At(foreEnd), tuning.ForeEnd));
            pairs.Add(LandmarkPair.Facing("off-facing", Palm(support), At(foreEnd), LandmarkAim.Point, tuning.Facing));
        }
        return [.. pairs];
    }
}
