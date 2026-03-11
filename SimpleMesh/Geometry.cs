using System;
using System.Collections.Generic;
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

        class UnweldedTriangles : ITangentGeometry
        {
            public record struct UnweldedGroup(int StartVertex, int VertexCount);

            public Vertex[] Vertices;
            public UnweldedGroup[] Groups;

            public UnweldedTriangles(Geometry g)
            {
                Groups = new UnweldedGroup[g.Groups.Length];
                int count = 0;
                for (int i = 0; i < g.Groups.Length; i++)
                {
                    Groups[i] = new(count, g.Groups[i].IndexCount);
                    count += g.Groups[i].IndexCount;
                }

                Vertices = new Vertex[count];
                int vtx = 0;
                for (int i = 0; i < g.Groups.Length; i++)
                {
                    for (int j = 0; j < g.Groups[i].IndexCount; j++)
                    {
                        var idx = g.Indices[g.Groups[i].StartIndex + j] + g.Groups[i].BaseVertex;
                        Vertices[vtx++] = g.Vertices[idx];
                    }
                }
            }

            public int GetNumFaces() => Vertices.Length / 3;

            public int GetNumVerticesOfFace(int index) => 3;
            
            int Index(int faceIndex, int faceVertex) => faceIndex * 3 + faceVertex;

            public Vector3 GetPosition(int faceIndex, int faceVertex) =>
                Vertices[Index(faceIndex, faceVertex)].Position;

            public Vector3 GetNormal(int faceIndex, int faceVertex) =>
                Vertices[Index(faceIndex, faceVertex)].Normal;

            public Vector2 GetTexCoord(int faceIndex, int faceVertex) =>
                Vertices[Index(faceIndex, faceVertex)].Texture1;

            public void SetTangent(Vector4 tangent, int faceIndex, int faceVertex) => 
                Vertices[Index(faceIndex, faceVertex)].Tangent = tangent;
        }
        
        public void CalculateTangents(bool overwrite = false)
        {
            if ((Has(VertexAttributes.Tangent) & !overwrite) || Kind == GeometryKind.Lines)
                return;
            var unwelded = new UnweldedTriangles(this);
            TangentGeneration.GenerateMikkTSpace(unwelded);

            List<uint> indexArray = new();
            int startIndex = 0;
            var vb = new VertexBufferBuilder(Attributes | VertexAttributes.Tangent);

            for (int i = 0; i < Groups.Length; i++)
            {
                for (int j = 0; j < unwelded.Groups[i].VertexCount; j++)
                {
                    var idx = vb.Add(ref unwelded.Vertices[j + unwelded.Groups[i].StartVertex]) -
                              vb.BaseVertex;
                    indexArray.Add((uint)idx);
                }
                Groups[i].StartIndex = startIndex;
                Groups[i].IndexCount = indexArray.Count - startIndex;
                Groups[i].BaseVertex = vb.BaseVertex;
                vb.Chunk();
            }
            Indices = Indices.FromBuffer(indexArray.ToArray());
            Vertices = vb.GetVertices();
        }
        
        public void CalculateNormals(bool overwrite = false)
        {
            if ((Has(VertexAttributes.Normal) && !overwrite) || Kind == GeometryKind.Lines)
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
        
    }
}