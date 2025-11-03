using System.Numerics;
using System.Runtime.InteropServices;
using SimpleMesh.Convex;

namespace ConvexHullSample;

[StructLayout(LayoutKind.Sequential)]
public record struct DisplayVertex(Vector3 Position, Vector3 Normal)
{
    public static (DisplayVertex[], ushort[]) FromHull(Hull hull)
    {
        List<DisplayVertex> vertices = new();
        List<ushort> indexArray = new();

        Dictionary<DisplayVertex, int> indices = new Dictionary<DisplayVertex, int>();
        int Add(DisplayVertex vert)
        {
            if (!indices.TryGetValue(vert, out int idx))
            {
                idx = vertices.Count;
                vertices.Add(vert);
                indices.Add(vert, idx);
            }
            return idx;
        }

        for (int i = 0; i < hull.FaceCount; i++)
        {
            var f = hull.GetFace(i);
            var faceNormal = hull.FaceNormal(i);
            indexArray.Add((ushort)Add(new(hull.Vertices[f.A], faceNormal)));
            indexArray.Add((ushort)Add(new(hull.Vertices[f.B], faceNormal)));
            indexArray.Add((ushort)Add(new(hull.Vertices[f.C], faceNormal)));
        }

        return (vertices.ToArray(), indexArray.ToArray());
    }
}