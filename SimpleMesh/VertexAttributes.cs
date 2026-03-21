using System;

namespace SimpleMesh
{
    [Flags]
    public enum VertexAttributes : ushort
    {
        None = 0,
        Normal = (1 << 0),
        Diffuse = (1 << 1),
        Tangent = (1 << 2),
        Texture1 = (1 << 3),
        Texture2 = (1 << 4),
        Texture3 = (1 << 5),
        Texture4 = (1 << 6),
        Joints = (1 << 7)
    }
}
