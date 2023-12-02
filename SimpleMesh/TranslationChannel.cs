using System.Linq;
using System.Numerics;

namespace SimpleMesh;

public record struct TranslationKeyframe(float Time, Vector3 Translation);

public class TranslationChannel
{
    public TranslationKeyframe[] Keyframes;
    public string Target;

    public TranslationChannel Clone()
    {
        var ch = new TranslationChannel();
        ch.Target = Target;
        ch.Keyframes = Keyframes.ToArray();
        return ch;
    }

    public Vector3 GetAtTime(float time)
    {
        if (time <= Keyframes[0].Time)
            return Keyframes[0].Translation;
        if (time >= Keyframes[^1].Time)
            return Keyframes[^1].Translation;
        for (int i = 0; i < Keyframes.Length - 1; i++)
        {
            if (time >= Keyframes[i].Time && time <= Keyframes[i + 1].Time)
            {
                var amount = (time - Keyframes[i].Time) / (Keyframes[i + 1].Time - Keyframes[i].Time);
                return Keyframes[i].Translation + (Keyframes[i + 1].Translation - Keyframes[i].Translation) * amount;
            }
        }
        return Keyframes[0].Translation;
    }
}