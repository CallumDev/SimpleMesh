using System.Linq;
using System.Numerics;

namespace SimpleMesh;

public record struct RotationKeyframe(float Time, Quaternion Rotation);

public class RotationChannel
{
    public RotationKeyframe[] Keyframes;
    public string Target;

    public RotationChannel Clone()
    {
        var ch = new RotationChannel();
        ch.Target = Target;
        ch.Keyframes = Keyframes.ToArray();
        return ch;
    }
    
    public Quaternion GetAtTime(float time)
    {
        if (time <= Keyframes[0].Time)
            return Keyframes[0].Rotation;
        if (time >= Keyframes[^1].Time)
            return Keyframes[^1].Rotation;
        for (int i = 0; i < Keyframes.Length - 1; i++)
        {
            if (time >= Keyframes[i].Time && time <= Keyframes[i + 1].Time)
            {
                var amount = (time - Keyframes[i].Time) / (Keyframes[i + 1].Time - Keyframes[i].Time);
                return Quaternion.Slerp(Keyframes[i].Rotation, Keyframes[i + 1].Rotation, amount);
            }
        }
        return Keyframes[0].Rotation;
    }
}