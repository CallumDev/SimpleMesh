using System;
using System.Collections.Generic;
using System.Numerics;

namespace SimpleMesh
{
    public class Geometry
    {
        public string Name;
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
    }
}