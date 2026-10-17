using System.Numerics;
using Hotel.Game.Content;

namespace Hotel.Game.Combat;

/// <summary>
/// The developer grip check: where the drawn hands' own points lie against the points a correct hold puts them on (a
/// look's <see cref="HeldLandmarks"/>). Contact only says whether digits clear the model; this says whether the hold is
/// the right one: the index pad on the trigger, the web up under the back strap, the pinky at the butt, the off palm
/// under the fore-end, and each palm facing what it holds. Measured on the hands as drawn, wherever the look is shown.
/// </summary>
internal static class GripLandmarks
{
    /// <summary>
    /// One pairing: a point of a hand and the look's landmark (camera space), how far apart they are (metres, or degrees
    /// for a palm's <see cref="Facing"/>, then <see cref="To"/> is where the palm's normal points), the tolerance and the
    /// report line.
    /// </summary>
    internal readonly record struct Pairing(string Name, Vector3 From, Vector3 To, float Measure, float Tolerance, string Text, bool Facing = false)
    {
        internal bool Within => Measure <= Tolerance;
    }

    /// <summary>The look's landmarks as placed (camera space), by field name.</summary>
    internal static IEnumerable<(string Field, Vector3 At)> Placed(HeldModel model, (Vector3 At, Quaternion Turn) placed) =>
        model.Landmarks?.Named.Select(n => (n.Field, placed.At + Vector3.Transform(Authored.Vector(n.Point) * model.Scale, placed.Turn))) ?? [];

    /// <summary>Each pairing the look's landmarks call for, measured on the hands as drawn; none before they are drawn.</summary>
    internal static List<Pairing> Measure(HeldModel model, (Vector3 At, Quaternion Turn) placed, ArmsDefinition arms,
        (Vector3 Wrist, Vector3[][] Digits)? main, (Vector3 Wrist, Vector3[][] Digits)? off)
    {
        List<Pairing> pairings = [];
        if (model.Landmarks is not { } marks) return pairings;
        HandLandmarks tuning = arms.Hands.Landmarks;
        Quaternion turn = placed.Turn;
        Vector3 right = Vector3.Transform(Vector3.UnitX, turn), up = Vector3.Transform(Vector3.UnitY, turn), back = Vector3.Transform(Vector3.UnitZ, turn);
        Vector3 Look(float[] point) => placed.At + Vector3.Transform(Authored.Vector(point) * model.Scale, turn);
        // Where a hand point lies from the landmark, along the look's own axes, in millimetres.
        string Where(Vector3 hand, Vector3 look)
        {
            Vector3 d = hand - look;
            static string Part(float v, string plus, string minus) => $"{MathF.Abs(v) * 1000:0} {(v >= 0 ? plus : minus)}";
            return $"{Part(Vector3.Dot(d, up), "above", "below")}, {Part(Vector3.Dot(d, back), "behind", "ahead")}, {Part(Vector3.Dot(d, right), "right", "left")}";
        }
        void Distance(string name, string hand, Vector3 from, string landmark, float[]? point, float tolerance)
        {
            if (point is null) return;
            Vector3 to = Look(point);
            float d = Vector3.Distance(from, to);
            pairings.Add(new(name, from, to, d, tolerance,
                $"{hand} {d * 1000:0.0} mm from the {landmark} (within {tolerance * 1000:0}): {Where(from, to)}"));
        }
        // A palm faces what it holds: the angle between its normal and the way to the nearest point of what it holds.
        void Facing(string name, string hand, Hand at, Vector3 target, string what)
        {
            if (Vector3.DistanceSquared(target, at.Palm) < 1e-10f) return;
            Vector3 toward = Vector3.Normalize(target - at.Palm);
            float angle = MathF.Acos(Math.Clamp(Vector3.Dot(at.Normal, toward), -1, 1)) * (180 / MathF.PI);
            pairings.Add(new(name, at.Palm, at.Palm + at.Normal * 0.05f, angle, tuning.Facing,
                $"{hand} palm faces {what} {angle:0}° off (within {tuning.Facing:0})", Facing: true));
        }
        if (main is { } m && Points(m, arms, arms.Main.Mirrored) is { } mainHand)
        {
            Distance("trigger", "main index pad", mainHand.IndexPad, "trigger", marks.Trigger, tuning.Trigger);
            Distance("backstrap", "main web", mainHand.Web, "back strap's top", marks.BackstrapTop, tuning.Backstrap);
            Distance("butt", "main pinky", mainHand.Pinky, "butt", marks.Butt, tuning.Butt);
            // The handle runs through the main grip (its middle) from the back strap's top toward the butt; the palm
            // wraps it, facing the nearest point of that line.
            if (marks.BackstrapTop is { } top)
            {
                Vector3 grip = Look(model.Grips.Main), along = marks.Butt is { } butt ? Look(butt) - Look(top) : -up;
                if (along.LengthSquared() > 1e-10f)
                {
                    along = Vector3.Normalize(along);
                    Facing("main-facing", "main", mainHand, grip + along * Vector3.Dot(mainHand.Palm - grip, along), "the handle");
                }
            }
        }
        if (off is { } o && marks.ForeEnd is not null && Points(o, arms, arms.Off.Mirrored) is { } offHand)
        {
            Distance("fore-end", "off palm", offHand.Palm, "fore-end", marks.ForeEnd, tuning.ForeEnd);
            Facing("off-facing", "off", offHand, Look(marks.ForeEnd), "the fore-end");
        }
        return pairings;
    }

    // A drawn hand's own points: the index pad (past the last joint as the fit's tip runs), the web, the pinky's middle
    // joint, the palm's centre and its normal (out of the palm).
    private readonly record struct Hand(Vector3 IndexPad, Vector3 Web, Vector3 Pinky, Vector3 Palm, Vector3 Normal);

    private static Hand? Points((Vector3 Wrist, Vector3[][] Digits) drawn, ArmsDefinition arms, bool mirrored)
    {
        HandLandmarks tuning = arms.Hands.Landmarks;
        Vector3[] Finger(string name) => drawn.Digits[Array.IndexOf(arms.Hands.Fingers, name)];
        Vector3[] index = Finger(tuning.Index), middle = Finger(tuning.Middle), pinky = Finger(tuning.Pinky), thumb = drawn.Digits[^1];
        Vector3 across = Vector3.Cross(index[0] - drawn.Wrist, pinky[0] - drawn.Wrist);
        if (across.LengthSquared() < 1e-12f) return null;
        // Index-to-pinky across the knuckles turns the other way on a mirrored hand.
        Vector3 normal = Vector3.Normalize(across) * (mirrored ? -1 : 1);
        Vector3 pad = index[2] + (index[2] - index[1]) * arms.Hands.Fit.Tip;
        Vector3 palm = Vector3.Lerp(drawn.Wrist, middle[0], tuning.Palm) + normal * tuning.PalmDepth;
        return new(pad, Vector3.Lerp(thumb[0], index[0], tuning.Web), pinky[1], palm, normal);
    }
}
