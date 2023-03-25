namespace SimpleMesh.Formats.SMesh
{
    enum PropertyKind : byte
    {
        String = 0,
        Int = 1,
        Float = 2,
        Boolean = 3,
        IntArray = 4,
        FloatArray = 5,
        Vector3 = 6,
        Invalid = 0xFF
    }
}