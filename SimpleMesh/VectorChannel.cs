using System;
using System.Linq;
using System.Numerics;

namespace SimpleMesh;

public record struct VectorKeyframe(float Time, Vector3 Vector);

public record struct VectorTangent(Vector3 In, Vector3 Out);

public class VectorChannel
{
    public AnimationInterpolation Interpolation = AnimationInterpolation.Linear;
    public VectorKeyframe[] Keyframes = [];
    public VectorTangent[]? Tangents;
    public string Target = "";

    public VectorChannel Clone()
    {
        var ch = new VectorChannel();
        ch.Target = Target;
        ch.Keyframes = Keyframes.ToArray();
        return ch;
    }

    Vector3 SampleCubicSpline(int index, float time)
    {
        var k0 = Keyframes[index];
        var k1 = Keyframes[index + 1];

        float dt = k1.Time - k0.Time;
        if (dt <= 0)
            return k0.Vector;

        float t = (time - k0.Time) / dt;
        t = Math.Clamp(t, 0f, 1f);

        var m0 = Tangents![index].Out * dt;
        var m1 = Tangents[index + 1].In * dt;

        float t2 = t * t;
        float t3 = t2 * t;

        float h00 = 2f * t3 - 3f * t2 + 1f;
        float h10 = t3 - 2f * t2 + t;
        float h01 = -2f * t3 + 3f * t2;
        float h11 = t3 - t2;

        return h00 * k0.Vector +
               h10 * m0 +
               h01 * k1.Vector +
               h11 * m1;
    }

    public Vector3 GetAtTime(float time)
    {
        if (Interpolation == AnimationInterpolation.CubicSpline &&
            (Tangents == null || Tangents.Length != Keyframes.Length))
        {
            throw new Exception("Tangents array not valid for CubicSpline curve");
        }
        if (time <= Keyframes[0].Time)
            return Keyframes[0].Vector;
        if (time >= Keyframes[^1].Time)
            return Keyframes[^1].Vector;
        for (int i = 0; i < Keyframes.Length - 1; i++)
        {
            if (time >= Keyframes[i].Time && time <= Keyframes[i + 1].Time)
            {
                return Interpolation switch
                {
                    AnimationInterpolation.Step => Keyframes[i].Vector,
                    AnimationInterpolation.CubicSpline => SampleCubicSpline(i, time),
                    // Linear
                    _=> Vector3.Lerp(Keyframes[i].Vector, Keyframes[i + 1].Vector,
                        (time - Keyframes[i].Time) / (Keyframes[i + 1].Time - Keyframes[i].Time))
                };
            }
        }
        return Keyframes[0].Vector;
    }
}
