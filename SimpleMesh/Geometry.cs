using System;
using System.Linq;
using System.Numerics;

namespace SimpleMesh
{
    public class Geometry : ITangentGeometry
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


        int GetIndex(int iFace, int iVert)
        {
            var idxBuffer = (iFace * 3) + iVert;
            if (Indices == null)
                return idxBuffer;
            return (int)(Indices.Indices16 != null
                ? Indices.Indices16[idxBuffer]
                : Indices.Indices32[idxBuffer]);
        }


        int ITangentGeometry.GetNumFaces()
        {
            if (Indices == null) return Vertices.Length / 3;
            return Indices.Length / 3;
        }

        int ITangentGeometry.GetNumVerticesOfFace(int index) => 3;

        Vector3 ITangentGeometry.GetPosition(int faceIndex, int faceVertex) =>
            Vertices[GetIndex(faceIndex, faceVertex)].Position;

        Vector3 ITangentGeometry.GetNormal(int faceIndex, int faceVertex) =>
            Vertices[GetIndex(faceIndex, faceVertex)].Normal;

        Vector2 ITangentGeometry.GetTexCoord(int faceIndex, int faceVertex) =>
            Vertices[GetIndex(faceIndex, faceVertex)].Texture1;

        void ITangentGeometry.SetTangent(Vector4 tangent, int faceIndex, int faceVertex) =>
            Vertices[GetIndex(faceIndex, faceVertex)].Tangent = tangent;
    }
}