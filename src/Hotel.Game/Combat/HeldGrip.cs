using System.Numerics;
using Hotel.Game.Content;

namespace Hotel.Game.Combat;

/// <summary>Where the hands hold a look placed somewhere (camera space), and whether the arms reach it there.</summary>
internal static class HeldGrip
{
    /// <summary>
    /// A look's grips where its model is placed, the turn the hands follow (the look's axis frame, so one hold suits
    /// every look of a kind) and the look's or its stance's own hold.
    /// </summary>
    internal static (Vector3 Main, Vector3? Off, Quaternion Item, float[]? Hand) At(HeldCatalog held, HeldModel model, (Vector3 At, Quaternion Turn) placed)
    {
        Vector3 Grip(float[] point) => placed.At + Vector3.Transform(Authored.Vector(point) * model.Scale, placed.Turn);
        Quaternion along = Frames.Between(HeldAxis.Default.TipVector, HeldAxis.Default.UpVector, model.Along.TipVector, model.Along.UpVector);
        return (Grip(model.Grips.Main), model.Grips.Off is { } off ? Grip(off) : null, Quaternion.Normalize(placed.Turn * along),
            model.Grips.Hand ?? held.Stances.Stances[model.Stance].Hand);
    }

    /// <summary>
    /// How far past the arms' reach each holding wrist is with the look placed so (metres; 0 or less is within reach,
    /// the off hand's -inf when it holds nothing).
    /// </summary>
    internal static (float Main, float Off) Beyond(HeldCatalog held, HeldModel model, (Vector3 At, Quaternion Turn) placed)
    {
        ArmsDefinition arms = held.Arms;
        (Vector3 main, Vector3? off, Quaternion item, float[]? hand) = At(held, model, placed);
        Vector3 shoulder = Authored.Vector(arms.Shoulder);
        float mainBeyond = Vector3.Distance(ArmsView.Wrist(main, item, arms.MainHand, hand).Wrist, shoulder) - arms.Reach.Length;
        if (off is not { } offGrip) return (mainBeyond, float.NegativeInfinity);
        Vector3 offShoulder = shoulder + Authored.Vector(arms.Reach.OffShoulder);
        return (mainBeyond, Vector3.Distance(ArmsView.Wrist(offGrip, item, arms.OffHand, model.Grips.OffHand).Wrist, offShoulder) - arms.Reach.Length);
    }
}
