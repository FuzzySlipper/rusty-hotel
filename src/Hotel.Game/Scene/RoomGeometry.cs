using System.Numerics;
using Hotel.Game.Content;
using Rusty.Engine;

namespace Hotel.Game.Scene;

// Authored box geometry shared by presentation and Engine static collision.
internal static class RoomGeometry
{
    internal static MeshResource Box(IEngineContext engine, Material material, Vector3 min, Vector3 max, Vector2 tile)
    {
        Vector3[] points = [new(min.X,min.Y,min.Z),new(max.X,min.Y,min.Z),new(max.X,max.Y,min.Z),new(min.X,max.Y,min.Z),
            new(min.X,min.Y,max.Z),new(max.X,min.Y,max.Z),new(max.X,max.Y,max.Z),new(min.X,max.Y,max.Z)];
        return Mesh(engine, points, [(0,3,2),(0,2,1),(4,5,6),(4,6,7),(0,4,7),(0,7,3),
            (1,2,6),(1,6,5),(3,7,6),(3,6,2),(0,1,5),(0,5,4)], material, tile);
    }

    // Profile corners sharper than this keep a crease; gentler ones are shaded smooth.
    private const float CreaseCosine = .64f;

    /// <summary>A moulding's profile swept along its run, smooth across gentle curves, with flat end caps.</summary>
    internal static MeshResource Moulding(IEngineContext engine, Material material, Moulding moulding)
    {
        Vector3 start = Authored.Vector(moulding.Start), end = Authored.Vector(moulding.End), outward = Authored.Vector(moulding.Outward),
            across = Authored.Vector(moulding.Across);
        Vector3 along = Vector3.Normalize(end - start);
        float length = Vector3.Distance(start, end);
        Vector3[] offsets = moulding.Profile.Select(p => outward * p[0] + across * p[1]).ToArray();
        // Each profile segment faces away from the wall: its 2D normal is (across, -out) of its tangent.
        Vector3[] faces = new Vector3[offsets.Length - 1];
        for (int i = 0; i < faces.Length; i++)
        {
            float dOut = moulding.Profile[i + 1][0] - moulding.Profile[i][0], dUp = moulding.Profile[i + 1][1] - moulding.Profile[i][1];
            faces[i] = Vector3.Normalize(outward * dUp - across * dOut);
        }
        List<Vector3> vertices = [], normals = [];
        List<Vector2> uv = [];
        List<uint> indices = [];
        void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc, Vector2 ua, Vector2 ub, Vector2 uc)
        {
            // Wind each triangle to face its shading normal.
            bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), na + nb + nc) < 0;
            foreach (var (p, n, t) in flip ? new[] { (a, na, ua), (c, nc, uc), (b, nb, ub) } : new[] { (a, na, ua), (b, nb, ub), (c, nc, uc) })
            {
                indices.Add((uint)vertices.Count);
                vertices.Add(p);
                normals.Add(n);
                uv.Add(t);
            }
        }
        Vector3 Smooth(int segment, int vertex)
        {
            int other = vertex == segment ? segment - 1 : segment + 1;
            if (other < 0 || other >= faces.Length || Vector3.Dot(faces[segment], faces[other]) < CreaseCosine) return faces[segment];
            return Vector3.Normalize(faces[segment] + faces[other]);
        }
        float travelled = 0;
        for (int i = 0; i < faces.Length; i++)
        {
            float next = travelled + Vector3.Distance(offsets[i], offsets[i + 1]);
            Vector3 a = start + offsets[i], b = end + offsets[i], c = end + offsets[i + 1], d = start + offsets[i + 1];
            Vector3 na = Smooth(i, i), nb = Smooth(i, i + 1);
            Vector2 ua = new(0, travelled), ub = new(length, travelled), uc = new(length, next), ud = new(0, next);
            Triangle(a, b, c, na, na, nb, ua, ub, uc);
            Triangle(a, c, d, na, nb, nb, ua, uc, ud);
            travelled = next;
        }
        // End caps: a fan from the wall face at the profile's middle.
        Vector3 hub = across * moulding.Profile.Average(p => p[1]);
        foreach (var (origin, facing) in new[] { (start, -along), (end, along) })
            for (int i = 0; i < offsets.Length - 1; i++)
                Triangle(origin + hub, origin + offsets[i], origin + offsets[i + 1], facing, facing, facing, Vector2.Zero, Vector2.Zero, Vector2.Zero);
        return engine.Graphics.CreateMeshResource(new MeshResourceCreateRequest(vertices.ToArray(), normals.ToArray(),
            uv.ToArray(), indices.ToArray(), new[] { new MeshGroup(0, 0, (uint)indices.Count) },
            new[] { new MeshMaterialBinding(0, material) }));
    }

    internal static MeshResource Mesh(IEngineContext engine, Vector3[] points, (int A, int B, int C)[] faces, Material material, Vector2 tile)
    {
        List<Vector3> vertices = [], normals = [];
        List<Vector2> uv = [];
        List<uint> indices = [];
        foreach ((int a, int b, int c) in faces)
        {
            Vector3 normal = Vector3.Normalize(Vector3.Cross(points[b] - points[a], points[c] - points[a]));
            foreach (int index in new[] { a, b, c })
            {
                indices.Add((uint)vertices.Count);
                vertices.Add(points[index]);
                normals.Add(normal);
                // World-metre projection keeps adjacent wall sections at the same pattern phase.
                Vector3 point = points[index];
                Vector2 planar = Math.Abs(normal.Y) > .5f ? new(point.X, point.Z)
                    : Math.Abs(normal.X) > .5f ? new(point.Z, -point.Y) : new(point.X, -point.Y);
                uv.Add(planar / tile);
            }
        }
        return engine.Graphics.CreateMeshResource(new MeshResourceCreateRequest(vertices.ToArray(), normals.ToArray(),
            uv.ToArray(), indices.ToArray(), new[] { new MeshGroup(0, 0, (uint)indices.Count) },
            new[] { new MeshMaterialBinding(0, material) }));
    }
}
