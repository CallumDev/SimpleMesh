using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;

namespace SimpleMesh.Formats.GLTF;

static class GLTFAnimation
{
    enum Interp
    {
        LINEAR,
        CUBICSPLINE,
        STEP
    }

    record struct Sampler(GLTFBufferAccessor Time, GLTFBufferAccessor Value, Interp Interpolation);
    public static Animation FromGLTF(JsonElement element, GLTFBufferAccessor[] accessors, string[] names)
    {
        var a = new Animation();
        if (element.TryGetProperty("name", out var nameProp))
            a.Name = nameProp.GetString();
        var smp = new List<Sampler>();
        if (element.TryGetProperty("samplers", out var samplers))
        {
            foreach (var s in samplers.EnumerateArray())
            {
                var input = s.GetProperty("input").GetInt32();
                var output = s.GetProperty("output").GetInt32();
                var interp = Enum.Parse<Interp>(s.GetProperty("interpolation").GetString());
                smp.Add(new Sampler(accessors[input], accessors[output], interp));
            }
        }
        else
        {
            a.Rotations = Array.Empty<RotationChannel>();
            a.Translations = Array.Empty<TranslationChannel>();
            return a;
        }

        if (element.TryGetProperty("channels", out var channels))
        {
            var translations = new List<TranslationChannel>();
            var rotations = new List<RotationChannel>();
            foreach (var ch in channels.EnumerateArray())
            {
                if (!ch.TryGetProperty("target", out var tgt))
                    continue;
                if (!tgt.TryGetProperty("node", out var tgtNode))
                    continue;
                if (!tgt.TryGetProperty("path", out var tgtPath))
                    continue;
                if (!ch.TryGetProperty("sampler", out var sampler))
                    continue;
                if ("translation".Equals(tgtPath.GetString(), StringComparison.OrdinalIgnoreCase))
                {
                    var tr = new TranslationChannel();
                    tr.Target = names[tgtNode.GetInt32()];
                    var s = smp[sampler.GetInt32()];
                    tr.Keyframes = new TranslationKeyframe[s.Time.Count];
                    for (int i = 0; i < s.Time.Count; i++)
                        tr.Keyframes[i] = new TranslationKeyframe(
                            s.Time.GetFloat(i),
                            s.Value.GetVector3(i)
                        );
                    translations.Add(tr);
                }
                if ("rotation".Equals(tgtPath.GetString(), StringComparison.OrdinalIgnoreCase))
                {
                    var rc = new RotationChannel();
                    rc.Target = names[tgtNode.GetInt32()];
                    var s = smp[sampler.GetInt32()];
                    rc.Keyframes = new RotationKeyframe[s.Time.Count];
                    for (int i = 0; i < s.Time.Count; i++)
                    {
                        var v4 = s.Value.GetVector4(i);
                        rc.Keyframes[i] = new RotationKeyframe(
                            s.Time.GetFloat(i),
                            new Quaternion(v4.X, v4.Y, v4.Z, v4.W)
                        );
                    }
                    rotations.Add(rc);
                }
                a.Translations = translations.ToArray();
                a.Rotations = rotations.ToArray();
            }
        }
        return a;
    }
    
}