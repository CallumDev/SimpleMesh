using System;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace SimpleMesh;

public record struct RotationKeyframe(float Time, Quaternion Rotation);
public record struct RotationTangent(Vector4 In, Vector4 Out);

public class RotationChannel
{
    public AnimationInterpolation Interpolation = AnimationInterpolation.Linear;
    public RotationKeyframe[] Keyframes = [];
    public RotationTangent[]? Tangents;
    public string Target = "";

    public RotationChannel Clone()
    {
        var ch = new RotationChannel();
        ch.Target = Target;
        ch.Keyframes = Keyframes.ToArray();
        return ch;
    }

    Quaternion SampleCubicSpline(int index, float time)
    {
        var k0 = Keyframes[index];
        var k1 = Keyframes[index + 1];

        float dt = k1.Time - k0.Time;
        if (dt <= 0)
            return k0.Rotation;

        float t = (time - k0.Time) / dt;
        t = Math.Clamp(t, 0f, 1f);

        var q0 = Unsafe.BitCast<Quaternion, Vector4>(k0.Rotation);
        var q1 = Unsafe.BitCast<Quaternion, Vector4>(k1.Rotation);

        Vector4 m0 = Tangents![index].Out * dt;
        Vector4 m1 = Tangents[index + 1].In * dt;

        float t2 = t * t;
        float t3 = t2 * t;

        float h00 = 2f * t3 - 3f * t2 + 1f;
        float h10 = t3 - 2f * t2 + t;
        float h01 = -2f * t3 + 3f * t2;
        float h11 = t3 - t2;

        Vector4 result =
            h00 * q0 +
            h10 * m0 +
            h01 * q1 +
            h11 * m1;

        result = Vector4.Normalize(result);

        return Unsafe.BitCast<Vector4, Quaternion>(result);
    }

    public Quaternion GetAtTime(float time)
    {
        if (Interpolation == AnimationInterpolation.CubicSpline &&
            (Tangents == null || Tangents.Length != Keyframes.Length))
        {
            throw new Exception("Tangents array not valid for CubicSpline curve");
        }
        if (time <= Keyframes[0].Time)
            return Keyframes[0].Rotation;
        if (time >= Keyframes[^1].Time)
            return Keyframes[^1].Rotation;
        for (int i = 0; i < Keyframes.Length - 1; i++)
        {
            if (time >= Keyframes[i].Time && time <= Keyframes[i + 1].Time)
            {
                return Interpolation switch
                {
                    AnimationInterpolation.Step => Keyframes[i].Rotation,
                    AnimationInterpolation.CubicSpline => SampleCubicSpline(i, time),
                    // Linear
                    _=> Quaternion.Slerp(Keyframes[i].Rotation, Keyframes[i + 1].Rotation,
                        (time - Keyframes[i].Time) / (Keyframes[i + 1].Time - Keyframes[i].Time))
                };
            }
        }
        return Keyframes[0].Rotation;
    }
}
