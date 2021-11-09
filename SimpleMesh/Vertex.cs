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
        public Vector4 Diffuse;
        public Vector2 Texture1;
        public Vector2 Texture2;
        public Vertex(Vector3 pos, Vector3 norm, Vector4 diffuse, Vector2 tex1, Vector2 tex2)
        {
            Position = pos;
            Normal = norm;
            Diffuse = diffuse;
            Texture1 = tex1;
            Texture2 = tex2;
        }

        public bool Equals(Vertex other)
        {
            return Position.Equals(other.Position) && 
                   Normal.Equals(other.Normal) && 
                   Diffuse.Equals(other.Diffuse) && 
                   Texture1.Equals(other.Texture1) && 
                   Texture2.Equals(other.Texture2);
        }

        public override bool Equals(object obj)
        {
            return obj is Vertex other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Position, Normal, Diffuse, Texture1, Texture2);
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