using System;
using System.Linq;
using System.Numerics;

namespace SimpleMesh
{
    public class Geometry
    {
        public string Name;
        public GeometryKind Kind;
        public VertexAttributes Attributes;
        public Vertex[] Vertices;
        public Indices Indices;
        public TriangleGroup[] Groups;

        public Vector3 Center;
        public Vector3 Min;
        public Vector3 Max;
        public float Radius;

        /// <summary>
        /// Runtime tag reference, not saved to disk.
        /// </summary>
        public object UserTag;

        public void CalculateBounds()
        {
            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
            float avgX = 0, avgY = 0, avgZ = 0;
            foreach (var v in Vertices)
            {
                minX = Math.Min(minX, v.Position.X);
                minY = Math.Min(minY, v.Position.Y);
                minZ = Math.Min(minZ, v.Position.Z);

                maxX = Math.Max(maxX, v.Position.X);
                maxY = Math.Max(maxY, v.Position.Y);
                maxZ = Math.Max(maxZ, v.Position.Z);

                avgX += v.Position.X;
                avgY += v.Position.Y;
                avgZ += v.Position.Z;
            }

            Min = new Vector3(minX, minY, minZ);
            Max = new Vector3(maxX, maxY, maxZ);
            Center = new Vector3(avgX, avgY, avgZ) / Vertices.Length;
            Radius = Math.Max(
                Vector3.Distance(Center, Min),
                Vector3.Distance(Center, Max)
            );
        }
        
        public void CalculateNormals(bool overwrite = false)
        {
            if (Has(VertexAttributes.Normal) && !overwrite)
                return;

            for (int i = 0; i < Vertices.Length; i++)
            {
                Vertices[i].Normal = Vector3.Zero;
            }
            
            for (int i = 0; i < Groups.Length; i++)
            {
                var bv = Groups[i].BaseVertex;
                for (int j = 0; j < Groups[i].IndexCount / 3; j++)
                {
                    var indexArray = (j * 3) + Groups[i].StartIndex;

                    var i0 = (int)(Indices[indexArray] + bv);
                    var i1 = (int)(Indices[indexArray + 1] + bv);
                    var i2 = (int)(Indices[indexArray + 2] + bv);
                    
                    Vector3 p0 = Vertices[i0].Position;
                    Vector3 p1 = Vertices[i1].Position;
                    Vector3 p2 = Vertices[i2].Position;

                    Vector3 u = p1 - p0;
                    Vector3 v = p2 - p0;

                    Vector3 faceNormal = Vector3.Cross(u, v);

                    Vertices[i0].Normal += faceNormal;
                    Vertices[i1].Normal += faceNormal;
                    Vertices[i2].Normal += faceNormal;
                }
            }

            for (int i = 0; i < Vertices.Length; i++)
            {
                if (Vertices[i].Normal != Vector3.Zero)
                {
                    Vertices[i].Normal = Vector3.Normalize(Vertices[i].Normal);
                }
            }

            Attributes |= VertexAttributes.Normal;
        }
        

        internal Geometry Clone(Model model) => new Geometry()
        {
            Name = Name,
            Kind = Kind,
            Attributes = Attributes,
            Vertices = Vertices.ToArray(),
            Indices = Indices.Clone(),
            Groups = Groups.Select(x => x.Clone(model)).ToArray(),
            Center = Center,
            Min = Min,
            Max = Max,
            Radius = Radius,
            UserTag = UserTag
        };

        internal bool Has(VertexAttributes attributes) =>
            (Attributes & attributes) == attributes;

        public ITangentGeometry GetTangentInterface()
        {
            if (Indices == null || Groups.All(x => x.BaseVertex == 0))
                return new FlatTangentAccessor(this);
            return new TriGroupTangentAccessor(this);
        }

        class FlatTangentAccessor(Geometry g) : TangentAccessor(g)
        {
            public override int GetNumFaces() => g.Indices == null ? g.Vertices.Length / 3 : g.Indices.Length / 3;
            protected override int GetIndex(int iFace, int iVert) => g.Indices == null 
                ? (iFace * 3) + iVert : (int)g.Indices[(iFace * 3) + iVert];
        }

        class TriGroupTangentAccessor : TangentAccessor
        {
            private int[] starts;
            private int indexCount;

            private int grp = 0;
            
            private Geometry geometry;

            public TriGroupTangentAccessor(Geometry g) : base(g)
            {
                geometry = g;
                starts = new int[g.Groups.Length];
                int count = 0;
                for (int i = 0; i < g.Groups.Length; i++)
                {
                    starts[i] = count;
                    count += g.Groups[i].IndexCount;
                }

                indexCount = count;
            }
            
            public override int GetNumFaces() => indexCount / 3;

            protected override int GetIndex(int iFace, int iVert)
            {
                int x = (iFace * 3) + iVert;
                int grp = FindGroup(x);
                int local = x - starts[grp];

                return (int)(
                    geometry.Indices[geometry.Groups[grp].StartIndex + local] +
                    geometry.Groups[grp].BaseVertex
                );
            }

            int FindGroup(int x)
            {
                int lo = 0;
                int hi = starts.Length - 1;

                while (lo <= hi)
                {
                    int mid = (lo + hi) >> 1;

                    if (x < starts[mid])
                        hi = mid - 1;
                    else if (mid + 1 < starts.Length && x >= starts[mid + 1])
                        lo = mid + 1;
                    else
                        return mid;
                }

                return starts.Length - 1;
            }
        }

        abstract class TangentAccessor(Geometry g) : ITangentGeometry
        {
            protected abstract int GetIndex(int iFace, int iVert);
            
            public int GetNumVerticesOfFace(int index) => 3;

            public abstract int GetNumFaces();

            public Vector3 GetPosition(int faceIndex, int faceVertex) => g.Vertices[GetIndex(faceIndex, faceVertex)].Position;

            public Vector3 GetNormal(int faceIndex, int faceVertex) => g.Vertices[GetIndex(faceIndex, faceVertex)].Normal;

            public Vector2 GetTexCoord(int faceIndex, int faceVertex) => g.Vertices[GetIndex(faceIndex, faceVertex)].Texture1;

            public void SetTangent(Vector4 tangent, int faceIndex, int faceVertex) => g.Vertices[GetIndex(faceIndex, faceVertex)].Tangent = tangent;
        }
    }
}