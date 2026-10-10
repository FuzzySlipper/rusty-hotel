using System.Numerics;
using System.Text.Json.Serialization;
using Hotel.Game.Content;
using Rusty.Engine;

namespace Hotel.Game.Combat;

/// <summary>
/// How felled residents fall: a ragdoll description per rig (bones on named joints, and the limited joints between
/// them), the bodies' surface and damping, and how hard the felling blow throws them. A look names its rig; a look
/// without one plays its fall clip instead.
/// </summary>
/// <param name="Slump">The impulse (newton seconds) a resident felled by no blow (a burn, say) slumps with, straight down.</param>
internal sealed record RagdollCatalog(Dictionary<string, RagdollRig> Rigs, float Friction, float LinearDamping, float AngularDamping,
    float Gravity, RagdollHit Hit, float Slump)
{
    internal const string Path = "combat/ragdolls.json";

    internal static RagdollCatalog Load(IEngineContext engine)
    {
        RagdollCatalog catalog = Authored.Read(engine, Path, ContentJson.Default.RagdollCatalog);
        Authored.Within(Path, "friction", catalog.Friction, 0, 2);
        Authored.AtLeast(Path, "linearDamping", catalog.LinearDamping, 0);
        Authored.AtLeast(Path, "angularDamping", catalog.AngularDamping, 0);
        Authored.Positive(Path, "gravity", catalog.Gravity);
        Authored.AtLeast(Path, "hit.impulse", catalog.Hit.Impulse, 0);
        Authored.AtLeast(Path, "hit.perDamage", catalog.Hit.PerDamage, 0);
        Authored.Within(Path, "hit.lift", catalog.Hit.Lift, 0, 1);
        Authored.AtLeast(Path, "slump", catalog.Slump, 0);
        foreach (var (id, rig) in catalog.Rigs) rig.Validate($"rigs.{id}");
        foreach (var (id, rig) in catalog.Rigs)
            Authored.Require(rig.Bones.Any(b => b.Joint == catalog.Hit.Bone), Path, "hit.bone", $"rig '{id}' has no bone on '{catalog.Hit.Bone}'.");
        return catalog;
    }
}

/// <summary>One rig's ragdoll: its bones (the first is the root every other descends from) and the links between them.</summary>
internal sealed record RagdollRig(RagdollBone[] Bones, RagdollLink[] Links, float JointDamping)
{
    internal void Validate(string at)
    {
        string path = RagdollCatalog.Path;
        Authored.Require(Bones.Length > 0, path, $"{at}.bones", "needs at least one bone.");
        Authored.Require(Bones.Select(b => b.Joint).Distinct().Count() == Bones.Length, path, $"{at}.bones", "names a joint twice.");
        Authored.AtLeast(path, $"{at}.jointDamping", JointDamping, 0);
        for (int i = 0; i < Bones.Length; i++)
        {
            RagdollBone b = Bones[i];
            Authored.Positive(path, $"{at}.bones[{i}].radius", b.Radius);
            Authored.Positive(path, $"{at}.bones[{i}].mass", b.Mass);
            if (b.End is null) Authored.Positive(path, $"{at}.bones[{i}].length", b.Length);
            if (b.Shape == DynamicsRagdollShape.Box) Authored.Positive(path, $"{at}.bones[{i}].halfDepth", b.HalfDepth);
        }
        for (int i = 0; i < Links.Length; i++)
        {
            RagdollLink l = Links[i];
            Authored.Require(Bones.Any(b => b.Joint == l.Parent), path, $"{at}.links[{i}].parent", $"'{l.Parent}' is not one of the rig's bones.");
            Authored.Require(Bones.Any(b => b.Joint == l.Child), path, $"{at}.links[{i}].child", $"'{l.Child}' is not one of the rig's bones.");
            Authored.Point(path, $"{at}.links[{i}].axis", l.Axis);
            Authored.Require((l.Cone is null) != (l.Hinge is null), path, $"{at}.links[{i}]", "needs exactly one of cone or hinge.");
            if (l.Hinge is { } h) Authored.Require(h.Min < h.Max, path, $"{at}.links[{i}].hinge", "min must be below max.");
            if (l.Cone is { } c)
            {
                Authored.Positive(path, $"{at}.links[{i}].cone.swing", c.Swing);
                Authored.Require(c.TwistMin <= c.TwistMax, path, $"{at}.links[{i}].cone", "twistMin must not be above twistMax.");
            }
        }
    }

    /// <summary>The Engine description against a model's joints; a joint the model lacks fails naming the rig.</summary>
    internal (DynamicsRagdollBone[] Bones, DynamicsRagdollLink[] Links) Build(string rig, ReadOnlySpan<AnimationJointInfo> joints, string model)
    {
        uint[] index = new uint[Bones.Length];
        for (int i = 0; i < Bones.Length; i++) index[i] = Joint(joints, Bones[i].Joint, rig, model);
        DynamicsRagdollBone[] bones = new DynamicsRagdollBone[Bones.Length];
        for (int i = 0; i < Bones.Length; i++)
        {
            RagdollBone b = Bones[i];
            uint end = b.End is null ? DynamicsRagdollBone.NoEndJoint : Joint(joints, b.End, rig, model);
            bones[i] = new(index[i], end, b.Length, b.Shape, b.Radius, b.HalfDepth, b.Mass);
        }
        int Bone(string joint) => Array.FindIndex(Bones, b => b.Joint == joint);
        DynamicsRagdollLink[] links = [.. Links.Select(l => new DynamicsRagdollLink((uint)Bone(l.Parent), (uint)Bone(l.Child), Authored.Vector(l.Axis),
            l.Hinge is { } h ? DynamicsJointLimits.Hinge(h.Min, h.Max, JointDamping) : DynamicsJointLimits.Cone(l.Cone!.Swing, l.Cone.TwistMin, l.Cone.TwistMax, JointDamping)))];
        return (bones, links);
    }

    internal int BoneOf(string joint) => Array.FindIndex(Bones, b => b.Joint == joint);

    private static uint Joint(ReadOnlySpan<AnimationJointInfo> joints, string name, string rig, string model)
    {
        for (int i = 0; i < joints.Length; i++)
            if (joints[i].Id == name) return (uint)i;
        throw new InvalidDataException($"Resident model '{model}' has no joint '{name}' that ragdoll rig '{rig}' (content/{RagdollCatalog.Path}) hangs a bone on.");
    }
}

/// <summary>
/// A body on a rig joint, spanning to where <see cref="End"/> is at rest (or <see cref="Length"/> metres along the
/// joint's +Y when it has none): a capsule of <see cref="Radius"/>, or a box <see cref="Radius"/> half wide and
/// <see cref="HalfDepth"/> half deep, of <see cref="Mass"/> kilograms.
/// </summary>
internal sealed record RagdollBone(string Joint, DynamicsRagdollShape Shape, float Radius, float Mass, string? End = null, float Length = 0,
    float HalfDepth = 0);

/// <summary>A limited joint between two bones at the child's joint: a cone (swing and twist, radians) or a hinge about <see cref="Axis"/> in the child joint's rest frame.</summary>
internal sealed record RagdollLink(string Parent, string Child, float[] Axis, RagdollCone? Cone = null, RagdollHinge? Hinge = null);

internal sealed record RagdollCone(float Swing, float TwistMin, float TwistMax);

internal sealed record RagdollHinge(float Min, float Max);

/// <summary>
/// The felling blow's push, on <see cref="Bone"/> where it struck: <see cref="Impulse"/> plus <see cref="PerDamage"/>
/// for each point of damage (newton seconds), along the blow, tipped up by <see cref="Lift"/>.
/// </summary>
internal sealed record RagdollHit(string Bone, float Impulse, float PerDamage, float Lift)
{
    internal Vector3 Push(Vector3 direction, float damage)
    {
        Vector3 flat = new(direction.X, 0, direction.Z);
        Vector3 along = flat.LengthSquared() > 1e-6f ? Vector3.Normalize(flat) : Vector3.UnitZ;
        return Vector3.Normalize(along + Vector3.UnitY * Lift) * (Impulse + PerDamage * damage);
    }
}
