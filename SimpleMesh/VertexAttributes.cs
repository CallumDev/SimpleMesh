using System;

namespace SimpleMesh
{
    [Flags]
    public enum VertexAttributes : ushort
    {
        Position = (1 << 0),
        Normal = (1 << 1),
        Diffuse = (1 << 2),
        Tangent = (1 << 3),
        Texture1 = (1 << 4),
        Texture2 = (1 << 5),
        Texture3 = (1 << 6),
        Texture4 = (1 << 7)
    }
}