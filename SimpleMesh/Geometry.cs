using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SimpleMesh
{
    public class Geometry
    {
        public string Name = "";
        public GeometryKind Kind;
        public VertexArray Vertices;
        public Indices Indices;
        public TriangleGroup[] Groups = [];

        public Vector3 Center;
        public Vector3 Min;
        public Vector3 Max;
        public float Radius;

        public Geometry(VertexArray vertices, Indices indices)
        {
            Vertices = vertices;
            Indices = indices;
        }


        /// <summary>
        /// Runtime tag reference, not saved to disk.
        /// </summary>
        public object? UserTag;

        public void CalculateBounds()
        {
            Vector3 min = new(float.MaxValue);
            Vector3 max = new(float.MinValue);
            Vector3 avg = new(0);
            for(int i = 0; i < Vertices.Count; i++)
            {
                min = Vector3.Min(min, Vertices.Position[i]);
                max = Vector3.Max(max, Vertices.Position[i]);
                avg += Vertices.Position[i];
            }

            Min = min;
            Max = max;
            Center = avg / Vertices.Count;
            Radius = Math.Max(
                Vector3.Distance(Center, Min),
                Vector3.Distance(Center, Max)
            );
        }

        class UnweldedTriangles : ITangentGeometry
        {
            public record struct UnweldedGroup(int StartVertex, int VertexCount);

            public Vector4[] Tangents;
            public UnweldedGroup[] Groups;
            public int[] Source;
            public VertexArray Original;

            public Vertex GetVertex(int index) =>
                Original.Vertices[Source[index]] with { Tangent = Tangents[index] };

            public UnweldedTriangles(Geometry g)
            {
                Original = g.Vertices;
                Groups = new UnweldedGroup[g.Groups.Length];
                int count = 0;
                for (int i = 0; i < g.Groups.Length; i++)
                {
                    Groups[i] = new(count, g.Groups[i].IndexCount);
                    count += g.Groups[i].IndexCount;
                }

                Tangents = new Vector4[count];
                Source = new int[count];

                int vtx = 0;
                for (int i = 0; i < g.Groups.Length; i++)
                {
                    for (int j = 0; j < g.Groups[i].IndexCount; j++)
                    {
                        var idx = (int)(g.Indices[g.Groups[i].StartIndex + j] + g.Groups[i].BaseVertex);
                        Source[vtx] = idx;
                    }
                }
            }

            public int GetNumFaces() => Original.Count / 3;

            public int GetNumVerticesOfFace(int index) => 3;

            int Index(int faceIndex, int faceVertex) => faceIndex * 3 + faceVertex;

            public Vector3 GetPosition(int faceIndex, int faceVertex) =>
                Original.Position[Source[Index(faceIndex, faceVertex)]];

            public Vector3 GetNormal(int faceIndex, int faceVertex) =>
                Original.Normal[Source[Index(faceIndex, faceVertex)]];

            public Vector2 GetTexCoord(int faceIndex, int faceVertex) =>
                Original.Texture1[Source[Index(faceIndex, faceVertex)]];

            public void SetTangent(Vector4 tangent, int faceIndex, int faceVertex) =>
                Tangents[Index(faceIndex, faceVertex)] = tangent;
        }

        public void CalculateTangents(bool overwrite = false)
        {
            if ((Has(VertexAttributes.Tangent) & !overwrite) || Kind == GeometryKind.Lines ||
                !Has(VertexAttributes.Normal) || !Has(VertexAttributes.Texture1))
                return;
            var unwelded = new UnweldedTriangles(this);
            TangentGeneration.GenerateMikkTSpace(unwelded);
            var vb = new GeometryBuilder(Vertices.Descriptor.Attributes | VertexAttributes.Tangent);
            for (int i = 0; i < Groups.Length; i++)
            {
                for (int j = 0; j < unwelded.Groups[i].VertexCount; j++)
                {
                    var v = unwelded.GetVertex(j + unwelded.Groups[i].StartVertex);
                    vb.Add(ref v);
                }
                vb.AddGroup(Groups[i].Material);
            }

            var welded = vb.Finish();
            Indices = welded.Indices;
            Vertices = welded.Vertices;
        }

        public void CalculateNormals(bool overwrite = false)
        {
            if ((Has(VertexAttributes.Normal) && !overwrite) || Kind == GeometryKind.Lines)
                return;

            Vertices.ChangeAttributes(Vertices.Descriptor.Attributes | VertexAttributes.Normal);
            for (int i = 0; i < Vertices.Count; i++)
            {
                Vertices.Normal[i] = Vector3.Zero;
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

                    Vector3 p0 = Vertices.Position[i0];
                    Vector3 p1 = Vertices.Position[i1];
                    Vector3 p2 = Vertices.Position[i2];

                    Vector3 u = p1 - p0;
                    Vector3 v = p2 - p0;

                    Vector3 faceNormal = Vector3.Cross(u, v);

                    Vertices.Normal[i0] += faceNormal;
                    Vertices.Normal[i1] += faceNormal;
                    Vertices.Normal[i2] += faceNormal;
                }
            }

            for (int i = 0; i < Vertices.Count; i++)
            {
                if (Vertices.Normal[i] != Vector3.Zero)
                {
                    Vertices.Normal[i] = Vector3.Normalize(Vertices.Normal[i]);
                }
            }
        }


        internal Geometry Clone(Model model) => new Geometry(Vertices.Clone(), Indices.Clone())
        {
            Name = Name,
            Kind = Kind,
            Groups = Groups.Select(x => x.Clone(model)).ToArray(),
            Center = Center,
            Min = Min,
            Max = Max,
            Radius = Radius,
            UserTag = UserTag
        };

        internal bool Has(VertexAttributes attributes) =>
            (Vertices.Descriptor.Attributes & attributes) == attributes;

    }
}
