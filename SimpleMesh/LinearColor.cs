using System;
using System.Numerics;

namespace SimpleMesh;

public record struct LinearColor(float R, float G, float B, float A)
{
    public static LinearColor White => new(1, 1, 1, 1);
    public static LinearColor Black => new(0, 0, 0, 1);
    
    public static LinearColor FromSrgb(Vector4 srgb) =>
        FromSrgb(srgb.X, srgb.Y, srgb.Z, srgb.W);

    public static LinearColor FromSrgb(float r, float g, float b, float a) =>
        new(SrgbToLinear(r), SrgbToLinear(g), SrgbToLinear(b), a);
    
    public Vector4 ToSrgb() =>
        new(LinearToSrgb(R), LinearToSrgb(G), LinearToSrgb(B), LinearToSrgb(A));
    
    static float LinearToSrgb(float v) => v <= 0.0031308f
        ? v * 12.92f
        : 1.055f * MathF.Pow( v, 1.0f / 2.4f) - 0.055f;
    static float SrgbToLinear(float v) => v <= 0.04045f
        ? v * (1.0f / 12.92f)
        : MathF.Pow( (v + 0.055f) * (1.0f / 1.055f), 2.4f);
}