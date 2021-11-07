using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace SimpleMesh
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Vertex
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
    }
}