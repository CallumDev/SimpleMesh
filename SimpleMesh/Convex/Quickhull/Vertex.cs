using System.Numerics;

namespace SimpleMesh.Convex.Quickhull;

class Vertex(Vector3 point, int index)
{
    public Vector3 Point = point;
    public int Index = index;
    public Vertex Next = null;
    public Vertex Prev = null;
    public Face Face = null;
}
