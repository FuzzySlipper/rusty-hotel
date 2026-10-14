using System.Numerics;
using Rusty.Engine;

namespace Hotel.Game.Combat;

/// <summary>
/// One digit of the grip clip as the arms draw it: each of its three joints' turn and offset from its parent (the hand
/// for the first), without the turns Hotel adds over the clip.
/// </summary>
internal sealed record DigitChain(Quaternion[] Turn, Vector3[] Offset);

/// <summary>A hand's digit chains: its fingers (in the arms' finger order) and its thumb.</summary>
internal sealed record HandChains(DigitChain[] Fingers, DigitChain Thumb);

/// <summary>
/// Fits a hand's shape on a held look to the look's model: each digit's joints curl together from open until a segment
/// meets the model's surface (the Engine's model surface query); that joint and those before it stop a step short, and
/// the joints past it curl on. Probes already on the model (where the hand rests on it) do not stop a joint; a digit
/// that meets nothing keeps the grip clip. The hand stands where it
/// holds the look; digits are posed here from the clip's chains, so the fit needs no drawn frame of its own.
/// </summary>
internal static class HandFitting
{
    private const float Radians = MathF.PI / 180;
    private static readonly string[] Joints = ["base", "middle", "tip"];

    /// <summary>Where the held model stands, the hand's wrist and turn on it, and what the hand's digits are.</summary>
    internal readonly record struct Hold(RenderResource Model, Transform Placement, Vector3 Wrist, Quaternion Turn, HandChains Chains, bool Mirrored);

    /// <summary>Fits <paramref name="shape"/> in place and returns a line per digit: its angles and how it meets the model.</summary>
    internal static List<string> Fit(IEngineContext engine, Hold hold, HandShape shape, ArmDigits digits)
    {
        HandFit fit = digits.Fit;
        List<string> lines = [];
        for (int f = 0; f < shape.Fingers.Length; f++)
            lines.Add(FitDigit(engine, hold, hold.Chains.Fingers[f], shape.Fingers[f], null, digits.Open.Fingers, fit.FingerRadius, fit, digits.Fingers[f]));
        float[] curls = [.. shape.Thumb.Select(j => j[0])], swings = [.. shape.Thumb.Select(j => j[1])];
        lines.Add(FitDigit(engine, hold, hold.Chains.Thumb, curls, swings, [.. digits.Open.Thumb.Select(j => j[0])], fit.ThumbRadius, fit, digits.Thumb));
        for (int i = 0; i < 3; i++) shape.Thumb[i][0] = curls[i];
        return lines;
    }

    /// <summary>A line per digit of the hand as <paramref name="shape"/> holds it now: how near the model each comes, and which joints pass into it.</summary>
    /// <param name="drawn">Where the digits' joints were drawn, when the look is shown at rest: the report then tells how far the fit's posing lies from them.</param>
    internal static List<string> Contact(IEngineContext engine, Hold hold, HandShape shape, ArmDigits digits, Vector3[][]? drawn = null)
    {
        HandFit fit = digits.Fit;
        List<string> lines = [];
        if (drawn is not null)
        {
            float worst = 0;
            for (int d = 0; d <= shape.Fingers.Length; d++)
            {
                bool thumb = d == shape.Fingers.Length;
                Vector3[] posed = Pose(hold, thumb ? hold.Chains.Thumb : hold.Chains.Fingers[d],
                    thumb ? [.. shape.Thumb.Select(j => j[0])] : shape.Fingers[d], thumb ? [.. shape.Thumb.Select(j => j[1])] : null, fit.Tip);
                for (int j = 0; j < 3; j++) worst = MathF.Max(worst, Vector3.Distance(posed[j], drawn[d][j]));
            }
            lines.Add($"posed digits lie within {worst * 1000:0.0} mm of the drawn ones");
        }
        for (int f = 0; f < shape.Fingers.Length; f++)
            lines.Add($"{digits.Fingers[f]}: curl [{Angles(shape.Fingers[f])}]; {Meets(engine, hold, hold.Chains.Fingers[f], shape.Fingers[f], null, fit.FingerRadius, fit)}");
        lines.Add($"{digits.Thumb}: curl [{Angles(shape.Thumb.Select(j => j[0]))}], swing [{Angles(shape.Thumb.Select(j => j[1]))}]; {Meets(engine, hold, hold.Chains.Thumb, [.. shape.Thumb.Select(j => j[0])], [.. shape.Thumb.Select(j => j[1])], fit.ThumbRadius, fit)}");
        return lines;
    }

    private static string Angles(IEnumerable<float> values) => string.Join(", ", values.Select(v => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)));

    // The digit's free joints close together from open; when a segment meets the model, that joint and those before it
    // stop a step short, and the joints past it close on from there. Joints that meet nothing stop with the last that did.
    private static string FitDigit(IEngineContext engine, Hold hold, DigitChain chain, float[] curls, float[]? swings, float[] open, float radius, HandFit fit, string name)
    {
        // The thumb's root is already open in the grip clip. A shared negative sweep must not hyperextend it (or
        // a finger's tip) past the rig's authored open pose just to obtain an earlier surface contact.
        float Curl(int joint, int step) => MathF.Max(open[joint], fit.Open + step * fit.Step);
        for (int i = 0; i < 3; i++) curls[i] = Curl(i, 0);
        int steps = (int)MathF.Floor((fit.Closed - fit.Open) / fit.Step) + 1, free = 0, start = 0, per = 3 * fit.Samples;
        List<string> how = [];
        while (free < 3 && start < steps - 1)
        {
            // Every step left, posed and probed in one query.
            List<Vector3> probes = [];
            for (int s = start; s < steps; s++)
            {
                for (int j = free; j < 3; j++) curls[j] = Curl(j, s);
                probes.AddRange(Probes(Pose(hold, chain, curls, swings, fit.Tip), 0, fit.Samples));
            }
            ReadOnlySpan<ModelSurfacePoint> found = engine.Animation.QueryModelSurface(new(hold.Model, hold.Placement, probes.ToArray(), fit.Reach)).Span;
            // A probe already on the model is where the hand rests on it (the bar in the palm): it does not stop a joint.
            bool[] resting = new bool[per];
            for (int p = 0; p < per; p++) resting[p] = Touches(found[p], radius, fit.Gap);
            int meets = -1, segment = -1;
            for (int s = 1; s < steps - start && meets < 0; s++)
                for (int p = free * fit.Samples; p < per; p++)
                {
                    bool touching = Touches(found[s * per + p], radius, fit.Gap);
                    // A probe resting on the handle may move away as it curls. Its next contact then counts too;
                    // ignoring it for the whole sweep lets it curl straight back through the model.
                    if (!touching) resting[p] = false;
                    else if (!resting[p]) { meets = s; segment = p / fit.Samples; break; }
                }
            if (meets < 0)
            {
                for (int j = free; j < 3; j++) curls[j] = Curl(j, start);
                break;
            }
            start += meets - 1;
            for (int j = free; j < 3; j++) curls[j] = Curl(j, start);
            how.Add($"{string.Join(", ", Joints[free..(segment + 1)])} stop at [{Angles(curls[free..(segment + 1)])}] on the {Joints[segment]}");
            free = segment + 1;
        }
        if (how.Count == 0)
        {
            for (int i = 0; i < 3; i++) curls[i] = 0;
            return $"{name}: meets nothing, the clip's kept; {Meets(engine, hold, chain, curls, swings, radius, fit)}";
        }
        if (free < 3) how.Add($"{string.Join(", ", Joints[free..])} meet nothing more");
        return $"{name}: {string.Join(", ", curls.Select(c => c.ToString("0")))} ({string.Join("; ", how)}); {Meets(engine, hold, chain, curls, swings, radius, fit)}";
    }

    // How near the model the digit posed so comes (its skin, so negative is into it), and which joints' segments pass into it.
    private static string Meets(IEngineContext engine, Hold hold, DigitChain chain, float[] curls, float[]? swings, float radius, HandFit fit)
    {
        Vector3[] probes = Probes(Pose(hold, chain, curls, swings, fit.Tip), 0, fit.Samples);
        ReadOnlySpan<ModelSurfacePoint> found = engine.Animation.QueryModelSurface(new(hold.Model, hold.Placement, probes, fit.Reach)).Span;
        float nearest = float.PositiveInfinity;
        SortedSet<string> into = [];
        for (int p = 0; p < found.Length; p++)
        {
            if (!found[p].Found) continue;
            float skin = found[p].Inside ? -found[p].Distance - radius : found[p].Distance - radius;
            nearest = MathF.Min(nearest, skin);
            if (skin < 0) into.Add(Joints[p / fit.Samples]);
        }
        string near = float.IsPositiveInfinity(nearest) ? $"nothing within {fit.Reach * 100:0} cm" : $"nearest {nearest * 1000:0.0} mm";
        return into.Count == 0 ? near : $"{near}, into the model at the {string.Join(", ", into)}";
    }

    private static bool Touches(ModelSurfacePoint point, float radius, float gap) => point.Found && (point.Inside || point.Distance <= radius + gap);

    // The digit's joints and tip where the hand holds: each joint turns by the clip, then by its curl (and a thumb's swing).
    private static Vector3[] Pose(Hold hold, DigitChain chain, float[] curls, float[]? swings, float tip)
    {
        Vector3[] at = new Vector3[4];
        Vector3 position = hold.Wrist;
        Quaternion turn = hold.Turn;
        float side = hold.Mirrored ? -1 : 1;
        for (int i = 0; i < 3; i++)
        {
            position += Vector3.Transform(chain.Offset[i], turn);
            Quaternion added = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -curls[i] * Radians);
            if (swings is not null) added = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, swings[i] * side * Radians) * added;
            turn = Quaternion.Normalize(turn * chain.Turn[i] * added);
            at[i] = position;
        }
        // The tip lies along the last joint as the joints before it lie along theirs.
        at[3] = position + Vector3.Transform(chain.Offset[2] * tip, turn);
        return at;
    }

    // Probes along each segment from the given joint's on (the segments that joint moves).
    private static Vector3[] Probes(Vector3[] joints, int from, int samples) =>
        [.. Enumerable.Range(from, 3 - from).SelectMany(s => Enumerable.Range(1, samples).Select(k => Vector3.Lerp(joints[s], joints[s + 1], k / (float)samples)))];
}
