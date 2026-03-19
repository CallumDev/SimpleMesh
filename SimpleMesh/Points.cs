using System.Runtime.InteropServices;

namespace SimpleMesh;

public record struct Point2<T>(T A, T B);
public record struct Point3<T>(T A, T B, T C);

[StructLayout(LayoutKind.Sequential)]
public record struct Point4<T>(T A, T B, T C, T D);