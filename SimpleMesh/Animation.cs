using System;
using System.Linq;

namespace SimpleMesh;

public class Animation
{
    public string Name = "";
    public RotationChannel[] Rotations = [];
    public VectorChannel[] Translations = [];
    public VectorChannel[] Scales = [];

    public Animation Clone()
    {
        var anm = new Animation
        {
            Name = Name,
            Rotations = Rotations.Select(x => x.Clone()).ToArray(),
            Translations = Translations.Select(x => x.Clone()).ToArray(),
            Scales = Scales.Select(x => x.Clone()).ToArray()
        };
        return anm;
    }

    public float CalculateDuration()
    {
        float max = 0;
        foreach (var r in Rotations)
        {
            if(r.Keyframes.Length > 0)
                max = Math.Max(max, r.Keyframes[^1].Time);
        }
        foreach (var t in Translations)
        {
            if(t.Keyframes.Length > 0)
                max = Math.Max(max, t.Keyframes[^1].Time);
        }
        foreach (var s in Scales)
        {
            if(s.Keyframes.Length > 0)
                max = Math.Max(max, s.Keyframes[^1].Time);
        }
        return max;
    }
}
