using System;

namespace SimpleMesh
{
    [Flags]
    public enum VertexAttributes : ushort
    {
        Position = 0x1,
        Normal = 0x2,
        Texture1 = 0x4,
        Texture2 = 0x8,
        Diffuse = 0x10
    }
}