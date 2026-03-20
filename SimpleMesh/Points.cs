using System.Runtime.InteropServices;

namespace SimpleMesh;

public record struct Point2<T>(T A, T B) where T : unmanaged;

public record struct Point3<T>(T A, T B, T C) where T : unmanaged;

[StructLayout(LayoutKind.Sequential)]
public record struct Point4<T>(T A, T B, T C, T D) where T: unmanaged;