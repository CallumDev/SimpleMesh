using System;

namespace SimpleMesh.Convex;

record struct Edge(int A, int B)
{
    public static Edge[] ArrayFromFaces(ReadOnlySpan<Point3<int>> faces, out int[] edgeFaces)
    {
        var edges = new Edge[faces.Length * 3];
        edgeFaces = new int[faces.Length * 3];
        for (int i = 0; i < faces.Length; i++)
        {
            edges[(i * 3)] = new(faces[i].A, faces[i].B);
            edges[(i * 3) + 1] = new Edge(faces[i].B, faces[i].C);
            edges[(i * 3) + 2] = new Edge(faces[i].C, faces[i].A);
            edgeFaces[(i * 3)] = i;
            edgeFaces[(i * 3) + 1] = i;
            edgeFaces[(i * 3) + 2] = i;
        }
        return edges;
    }
    
    

    public Edge Sorted() => A > B ? new(B, A) : new(A, B);
}