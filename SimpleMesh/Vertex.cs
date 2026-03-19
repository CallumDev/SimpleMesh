using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace SimpleMesh
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Vertex : IEquatable<Vertex>
    {
        public Vector3 Position;
        public Vector3 Normal;
        public LinearColor Diffuse;
        public Vector4 Tangent;
        public Vector2 Texture1;
        public Vector2 Texture2;
        public Vector2 Texture3;
        public Vector2 Texture4;
        public Point4<ushort> JointIndices;
        public Vector4 JointWeights;
        
        public Vertex(
            Vector3 pos, 
            Vector3 norm, 
            LinearColor diffuse,
            Vector4 tangent,
            Vector2 tex1, 
            Vector2 tex2,
            Vector2 tex3,
            Vector2 tex4,
            Point4<ushort> jointIndices,
            Vector4 jointWeights)
        {
            Position = pos;
            Normal = norm;
            Diffuse = diffuse;
            Tangent = tangent;
            Texture1 = tex1;
            Texture2 = tex2;
            Texture3 = tex3;
            Texture4 = tex4;
            JointIndices = jointIndices;
            JointWeights = jointWeights;
        }

        public bool Equals(Vertex other)
        {
            return Position.Equals(other.Position) &&
                   Normal.Equals(other.Normal) &&
                   Diffuse.Equals(other.Diffuse) &&
                   Tangent.Equals(other.Tangent) &&
                   Texture1.Equals(other.Texture1) &&
                   Texture2.Equals(other.Texture2) &&
                   Texture3.Equals(other.Texture3) &&
                   Texture4.Equals(other.Texture4) &&
                   JointIndices.Equals(other.JointIndices) &&
                   JointWeights.Equals(other.JointWeights);
        }

        public override bool Equals(object obj)
        {
            return obj is Vertex other && Equals(other);
        }

        public override int GetHashCode()
        {
            var self = new Span<Vertex>(ref this);
            var uints = MemoryMarshal.Cast<Vertex, uint>(self);
            uint k = 1535517821;
            for (int i = 0; i < uints.Length; i++)
            {
                k = BitOperations.RotateLeft(
                    k ^ BitOperations.RotateLeft(
                        uints[i] * 3432918353U, 15
                        ) * 461845907U,
                    13
                );
            }
            k = (uint)((k ^ (k >> 16)) * -2048144789);
            k = (uint)((k ^ (k >> 13)) * -1028477387);
            return (int)(k ^ k >> 16);
        }

        public static bool operator ==(Vertex left, Vertex right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(Vertex left, Vertex right)
        {
            return !left.Equals(right);
        }
    }
}