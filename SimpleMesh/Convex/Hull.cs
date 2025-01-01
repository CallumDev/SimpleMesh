// Most logic here is ported from trimesh (https://github.com/mikedh/trimesh)

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;

namespace SimpleMesh.Convex;

[Flags]
public enum AppliedRepairs
{
    None,
    FixedWinding = (1 << 0),
    FlippedNormals = (1 << 1)
}

public class Hull
{
    
    private Vector3[] vertices;
    private int[] indices;
    public IReadOnlyList<Vector3> Vertices => vertices;
    public IReadOnlyList<int> Indices => indices;
    
    public int FaceCount => indices.Length / 3;
    
    public bool IsConvex { get; private set; }

    public bool IsWatertight { get; private set; }
    
    public bool Multibody { get; private set; }

    public bool DegenerateMesh { get; private set; }

    public AppliedRepairs Repairs { get; private set; }

    public Point3<int> GetFace(int faceIndex)
    {
        if(faceIndex < 0 || faceIndex >= FaceCount)
            throw new IndexOutOfRangeException();
        faceIndex *= 3;
        return new(indices[faceIndex], indices[faceIndex + 1], indices[faceIndex + 2]);
    }
    
    public Vector3 FaceNormal(int faceIndex)
    {
        if(faceIndex < 0 || faceIndex >= FaceCount)
            throw new IndexOutOfRangeException();
        faceIndex *= 3;

        var a = vertices[indices[faceIndex]];
        var b = vertices[indices[faceIndex + 1]];
        var c = vertices[indices[faceIndex + 2]];

        var x = c - b;
        var y = a - b;


        var normal = Vector3.Normalize(Vector3.Cross(x, y));
        return normal;
    }

    public Vector3 FaceCenter(int faceIndex)
    {
        if(faceIndex < 0 || faceIndex >= FaceCount)
            throw new IndexOutOfRangeException();
        faceIndex *= 3;

        var p1 = vertices[indices[faceIndex]];
        var p2 = vertices[indices[faceIndex + 1]];
        var p3 = vertices[indices[faceIndex + 2]];

        var center = (p1 + p2 + p3) / 3f;
        return center;
    }
    
    private Hull() { }


    private const float MergeThreshold = 0.0000001f;
    
    static bool BuildQH(Quickhull.QuickhullCS qh, out Vector3[] newVertices, out int[] newIndices)
    {
        qh.Build();
        var idx = qh.CollectFaces();
        var verts = new List<Vector3>();
        var indices = new List<int>();
        foreach (var i in idx)
        {
            var v = qh.Vertices[i].Point;
            var x = verts.IndexOf(v);
            if (x == -1)
            {
                indices.Add(verts.Count);
                verts.Add(v);
            }
            else
            {
                indices.Add(x);
            }
        }
        newVertices = verts.ToArray();
        newIndices = indices.ToArray();
        if (Quickhull.QuickhullCS.Verify(newVertices, newIndices))
        {
            return true;
        }
        else
        {
            newVertices = null;
            newIndices = null;
            return false;
        }
    }
    
    public bool MakeConvex(bool force = false)
    {
        if (IsConvex && !force)
            return true;
        var qh = new Quickhull.QuickhullCS(vertices);
        if (BuildQH(qh, out var nV, out var nI))
        {
            vertices = nV;
            indices = nI;
            Calculate();
            return true;
        }
        return false;
    }

    public static bool TryQuickhull(Vector3[] points, out Hull hull)
    {
        var qh = new Quickhull.QuickhullCS(points);
        if (BuildQH(qh, out var nV, out var nI))
        {
            hull = FromTriangles(nV, nI);
            return true;
        }
        hull = null;
        return false;
    }
    
    static int GetMergedIndex(Vector3 item, IList<Vector3> vecs)
    {
        for (int i = 0; i < vecs.Count; i++) {
            if (Math.Abs(vecs[i].X - item.X) < MergeThreshold &&
                Math.Abs(vecs[i].Y - item.Y) < MergeThreshold &&
                Math.Abs(vecs[i].Z - item.Z) < MergeThreshold)
            {
                return i;
            }
        }
        return -1;
    }
    
    public static Hull FromGeometry(Geometry geometry)
    {
        if (geometry.Kind != GeometryKind.Triangles) {
            throw new InvalidOperationException("Geometry must be triangles");
        }
        List<Vector3> vertexArray = new List<Vector3>();
        List<int> indexArray = new List<int>();

        for (int i = 0; i < geometry.Indices.Length; i++)
        {
            var srcIndex = geometry.Indices.Indices32 != null
                ? geometry.Indices.Indices32[i]
                : geometry.Indices.Indices16[i];
            var idx = GetMergedIndex(geometry.Vertices[srcIndex].Position, vertexArray);
            if (idx == -1) {
                indexArray.Add(vertexArray.Count);
                vertexArray.Add(geometry.Vertices[srcIndex].Position);
            }
            else {
                indexArray.Add(idx);
            }
        }
        return CreateInternal(vertexArray.ToArray(), indexArray.ToArray());
    }
    
    public static Hull FromTriangles(IReadOnlyList<Vector3> vertices, IReadOnlyList<int> indices, bool merge = true)
    {
        if (indices.Count <= 0 || indices.Count % 3 != 0)
            throw new Exception("Indices must specify non-zero triangles");
        if (merge)
        {
            List<Vector3> vertexArray = new List<Vector3>();
            List<int> indexArray = new List<int>();
            for (int i = 0; i < indices.Count; i++)
            {
                var idx = GetMergedIndex(vertices[indices[i]], vertexArray);
                if (idx == -1)
                {
                    indexArray.Add(vertexArray.Count);
                    vertexArray.Add(vertices[indices[i]]);
                }
                else {
                    indexArray.Add(idx);
                }
            }
            return CreateInternal(vertexArray.ToArray(), indexArray.ToArray());
        }
        return CreateInternal(vertices.ToArray(), indices.ToArray());
    }

    static Hull CreateInternal(Vector3[] vertices, int[] indices)
    {
        var h = new Hull() { vertices = vertices, indices = indices };
        h.Calculate();
        return h;
    }
    
    const float ZeroArea = 1e-6f;
    const float Planar = 1e-5f;
    
    float CalculateVolume()
    {
        float totalVolume = 0.0f;
        for (int i = 0; i < indices.Length; i += 3)
        {
            Vector3 p1 = vertices[indices[i]];
            Vector3 p2 = vertices[indices[i + 1]];
            Vector3 p3 = vertices[indices[i + 2]];
            // Compute the signed volume of the tetrahedron formed by the triangle and the origin
            float tetrahedronVolume = Vector3.Dot(p1, Vector3.Cross(p2, p3)) / 6.0f;

            totalVolume += tetrahedronVolume;
        }
        return totalVolume;
    }

    // This method modifies the indices array 
    bool FixNormals(Edge[] edgesSorted, int[] edgeFaces, Point2<int>[] groups)
    {
        // Check for multibody and calculate adjacency
        var (adjacency, _) = CalculateAdjacency(edgesSorted, edgeFaces, groups);
        var graph = Graph.FromEdgelist(adjacency);
        if (graph.ConnectedComponents().Count > 1) {
            return false;
        }
        // Change all normals to same winding
        var faces = MemoryMarshal.Cast<int, Point3<int>>(indices);
        Span<Point3<int>> pair = stackalloc Point3<int>[2];
        int flipped = 0;
        Console.WriteLine(graph.Nodes().First());
        foreach (var p in graph.BfsEdges(graph.Nodes().First()))
        {
            pair[0] = faces[p.A];
            pair[1] = faces[p.B];
            var edges = Edge.ArrayFromFaces(pair, out _);
            var overlap = DuplicatePairIndices(edges.Select( x => x.Sorted()).ToArray());
            if (overlap.Length == 0)
                continue;
            var edgePairA = edges[overlap[0].A];
            var edgePairB = edges[overlap[0].B];
            if (edgePairA.A == edgePairB.A) {
                // if the edges aren't reversed, invert the order of one face
                flipped++;
                var f = faces[p.B];
                faces[p.B] = new(f.C, f.B, f.A);
            }
        }
        if (flipped > 0)
        {
            Repairs |= AppliedRepairs.FixedWinding;
        }
        if (CalculateVolume() < 0) {
            // Invert if needed
            for (int i = 0; i < faces.Length; i++)
            {
                var f = faces[i];
                faces[i] = new(f.C, f.B, f.A);
            }
            Repairs |= AppliedRepairs.FlippedNormals;
        }
        return true;
    }

    void Calculate()
    {
        // Get extents
        Vector3 min = new Vector3(float.MaxValue);
        Vector3 max = new Vector3(float.MinValue);
        for (int i = 0; i < indices.Length; i++)
        {
            min = Vector3.Min(min, vertices[indices[i]]);
            max = Vector3.Max(max, vertices[indices[i]]);
        }
        var scale = (max - min).Length();
        if (scale < ZeroArea)
            scale = 1.0f;
        // Get edges and faces
        var faces = MemoryMarshal.Cast<int, Point3<int>>(indices);
        var edges = Edge.ArrayFromFaces(faces, out var edgeFaces);
        var edgesSorted = edges.Select(x => x.Sorted()).ToArray();
        var groups = DuplicatePairIndices(edgesSorted);
        
        IsWatertight = groups.Length * 2 == edges.Length;

        if (!IsWatertight) {
            IsConvex = false;
            return;
        }
        
        if (!IsWindingConsistent(edges, groups))
        {
            if (!FixNormals(edgesSorted, edgeFaces, groups))  {
                Multibody = true;
                IsConvex = false;
                return;
            }
            // Reset arrays
            edges = Edge.ArrayFromFaces(faces, out edgeFaces);
            edgesSorted = edges.Select(x => x.Sorted()).ToArray();
            groups = DuplicatePairIndices(edgesSorted);
        } 
        else if (CalculateVolume() < 0) 
        {
            // Invert if needed
            for (int i = 0; i < faces.Length; i++)
            {
                var f = faces[i];
                faces[i] = new(f.C, f.B, f.A);
            }
            // Reset arrays
            edges = Edge.ArrayFromFaces(faces, out edgeFaces);
            edgesSorted = edges.Select(x => x.Sorted()).ToArray();
            groups = DuplicatePairIndices(edgesSorted);
            Repairs |= AppliedRepairs.FlippedNormals;
        }
        
        // is_winding_consistent is considered to be true now

        BitArray nonzero = new BitArray(faces.Length);
        for (int i = 0; i < faces.Length; i++)
        {
            nonzero[i] = TriangleArea(vertices[faces[i].A], vertices[faces[i].B], vertices[faces[i].C])
                         > ZeroArea;
        }

        if (!nonzero.HasAnySet()) {
            IsConvex = false;
            DegenerateMesh = true;
            return;
        }

        var (adjacency, adjacencyEdges) = CalculateAdjacency(edgesSorted, edgeFaces, groups);

        // Check if the mesh is multiple bodies
        if (Graph.FromEdgelist(adjacency).ConnectedComponents().Count > 1) {
            Multibody = true;
            IsConvex = false;
            return;
        }
        
        BitArray adj_ok = new BitArray(adjacency.Length);
        for (int i = 0; i < adjacency.Length; i++) {
            adj_ok[i] = nonzero[adjacency[i].A] && nonzero[adjacency[i].B];
        }

        if (!adj_ok.HasAnySet()) {
            IsConvex = false;
            DegenerateMesh = true;
            return;
        }
        
        // project faces
        var unshared = AdjacencyUnshared(adjacency, adjacencyEdges, faces);

        IsConvex = true;
        for (int i = 0; i < adjacencyEdges.Length; i++)
        {
            var normal = FaceNormal(adjacency[i].A);
            var origin = vertices[adjacencyEdges[i].A];
            var other = vertices[unshared[i].B] - origin;

            var d = Vector3.Dot(other, normal);
            if (d > Planar * scale)
            {
                IsConvex = false;
                break;
            }
        }
    }

    static Point2<int>[] AdjacencyUnshared(Point2<int>[] adjacency, Edge[] adjacencyEdges, ReadOnlySpan<Point3<int>> faces)
    {
        // Init array to -1, -1
        var vidUnshared = new Point2<int>[adjacency.Length];
        for (int i = 0; i < vidUnshared.Length; i++)
        {
            vidUnshared[i] = new(-1, -1);
        }


        var edgesA = adjacencyEdges.Select(x => x.A).ToArray();
        var edgesB = adjacencyEdges.Select(x => x.B).ToArray();
        
        for (int i = 0; i < 2; i++)
        {
            var fid = adjacency.Select(x => i == 0 ? x.A : x.B).ToArray();
            var faceRows = new Point3<int>[fid.Length];
            for (int j = 0; j < fid.Length; j++) {
                faceRows[j] = faces[fid[j]];
            }

            BitArray rowsOk = new BitArray(faceRows.Length);

            Point3<bool>[] unshared = new Point3<bool>[faceRows.Length];

            for (int j = 0; j < faceRows.Length; j++)
            {
                // Should have one true per row of 3
                var a1 = edgesA[j] == faceRows[j].A;
                var b1 = edgesA[j] == faceRows[j].B;
                var c1 = edgesA[j] == faceRows[j].C;

                var a2 = edgesB[j] == faceRows[j].A;
                var b2 = edgesB[j] == faceRows[j].B;
                var c2 = edgesB[j] == faceRows[j].C;

                var a = !(a1 || a2);
                var b = !(b1 || b2);
                var c = !(c1 || c2);
                var ok = (a ? 1 : 0) + (b ? 1 : 0) + (c ? 1 : 0) == 1;
                rowsOk[j] = ok;
                unshared[j] = ok ? new(a, b, c) : new(false, false, false);
            }

            var facesFlat = MemoryMarshal.Cast<Point3<int>, int>(faceRows);
            var unsharedFlat = MemoryMarshal.Cast<Point3<bool>, bool>(unshared);
            var facesUnshared = new List<int>();
            for (int j = 0; j < unsharedFlat.Length; j++) {
                if(unsharedFlat[j])
                    facesUnshared.Add(facesFlat[j]);
            }
            int k = 0;
            for (int j = 0; j < rowsOk.Length; j++) {
                if (rowsOk[j]) {
                    if (i == 0) {
                        vidUnshared[j] = new(facesUnshared[k++], vidUnshared[j].B);
                    }
                    else {
                        vidUnshared[j] = new(vidUnshared[j].A, facesUnshared[k++]);
                    }
                }
            }
        }

        return vidUnshared;
    }
    
    static (Point2<int>[] adjacency, Edge[] adjacencyEdges) CalculateAdjacency(
        Edge[] edgesSorted,
        int[] edgeFaces,
        Point2<int>[] groups)
    {
        var adjacency = new List<Point2<int>>();
        var adjacencyEdges = new List<Edge>();
        for (int i = 0; i < groups.Length; i++)
        {
            var a = edgeFaces[groups[i].A];
            var b = edgeFaces[groups[i].B];
            if (a == b) // Skip degenerate
                continue;
            if (b < a) // Sort
                (a, b) = (b, a);
            adjacency.Add(new (a, b));
            adjacencyEdges.Add(edgesSorted[groups[i].A]);
        }
        return (adjacency.ToArray(), adjacencyEdges.ToArray());
    }

    static float TriangleArea(Vector3 v1, Vector3 v2, Vector3 v3)
        => Vector3.Cross(v2 - v1, v3 - v1).Length() / 2.0f;

    static bool IsWindingConsistent(Edge[] edges, Point2<int>[] groups)
    {
        for (int i = 0; i < groups.Length; i++)
        {
            var edgeA = edges[groups[i].A];
            var edgeB = edges[groups[i].B];

            if (edgeA.B != edgeB.A) {
                return false;
            }
        }
        return true;
    }

    static Point2<int>[] DuplicatePairIndices<T>(T[] array)
    {
        var duplicates = new List<Point2<int>>();
        var seenIndices = new Dictionary<T, List<int>>();
        for (int i = 0; i < array.Length; i++)
        {
            T item = array[i];

            if (seenIndices.TryGetValue(item, out var indices))
            {
                duplicates.AddRange(indices.Select(index => new Point2<int>(index, i)));
                indices.Add(i);
            }
            else
            {
                seenIndices[item] = new List<int> { i };
            }
        }
        return duplicates.ToArray();
    }
}