using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace SimpleMesh.Formats.GLTF;

[StructLayout(LayoutKind.Sequential)]
struct GLTFMatrix
{
    public Vector4 Row0;
    public Vector4 Row1;
    public Vector4 Row2;
    public Vector4 Row3;

    public GLTFMatrix(float f)
    {
        Row0 = new(f);
        Row1 = new(f);
        Row2 = new(f);
        Row3 = new(f);
    }

    public static ref GLTFMatrix Cast(ref Matrix4x4 mat) =>
        ref Unsafe.As<Matrix4x4, GLTFMatrix>(ref mat);

    public static GLTFMatrix Min(GLTFMatrix a, GLTFMatrix b) => new()
    {
        Row0 = Vector4.Min(a.Row0, b.Row0),
        Row1 = Vector4.Min(a.Row1, b.Row1),
        Row2 = Vector4.Min(a.Row2, b.Row2),
        Row3 = Vector4.Min(a.Row3, b.Row3)
    };

    public static GLTFMatrix Max(GLTFMatrix a, GLTFMatrix b) => new()
    {
        Row0 = Vector4.Max(a.Row0, b.Row0),
        Row1 = Vector4.Max(a.Row1, b.Row1),
        Row2 = Vector4.Max(a.Row2, b.Row2),
        Row3 = Vector4.Max(a.Row3, b.Row3)
    };

    public JsonArray ToJsonArray() => new(
        Row0.X, Row0.Y, Row0.Z, Row0.W,
        Row1.X, Row1.Y, Row1.Z, Row1.W,
        Row2.X, Row2.Y, Row2.Z, Row2.W,
        Row3.X, Row3.Y, Row3.Z, Row3.W
    );
}
