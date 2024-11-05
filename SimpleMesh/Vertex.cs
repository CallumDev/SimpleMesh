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
        
        public Vertex(
            Vector3 pos, 
            Vector3 norm, 
            LinearColor diffuse,
            Vector4 tangent,
            Vector2 tex1, 
            Vector2 tex2,
            Vector2 tex3,
            Vector2 tex4)
        {
            Position = pos;
            Normal = norm;
            Diffuse = diffuse;
            Tangent = tangent;
            Texture1 = tex1;
            Texture2 = tex2;
            Texture3 = tex3;
            Texture4 = tex4;
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
                   Texture4.Equals(other.Texture4);
        }

        public override bool Equals(object obj)
        {
            return obj is Vertex other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Position, Normal, Diffuse, Tangent, Texture1, Texture2, Texture3, Texture4);
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