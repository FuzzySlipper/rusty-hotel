using System.Numerics;
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
