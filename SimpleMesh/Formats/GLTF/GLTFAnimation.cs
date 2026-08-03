using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using SimpleMesh.Util;

namespace SimpleMesh.Formats.GLTF;

static class GLTFAnimation
{
    enum Interp
    {
        LINEAR,
        CUBICSPLINE,
        STEP
    }

    record struct Sampler(GLTFBufferAccessor Time, GLTFBufferAccessor Value, AnimationInterpolation Interpolation);

    static VectorChannel GetVectorChannel(string target, Sampler s)
    {
        var tr = new VectorChannel();
        tr.Target = target;
        tr.Interpolation = s.Interpolation;
        tr.Keyframes = new VectorKeyframe[s.Time.Count];
        if (tr.Interpolation == AnimationInterpolation.CubicSpline)
        {
            tr.Tangents = new VectorTangent[s.Time.Count];
            for (int i = 0; i < s.Time.Count; i++)
            {
                tr.Keyframes[i] = new VectorKeyframe(
                    s.Time.GetFloat(i),
                    s.Value.GetVector3(i * 3 + 1)
                );
                tr.Tangents[i] = new(s.Value.GetVector3(i * 3), s.Value.GetVector3(i * 3 + 2));
            }
        }
        else
        {
            for (int i = 0; i < s.Time.Count; i++)
                tr.Keyframes[i] = new VectorKeyframe(
                    s.Time.GetFloat(i),
                    s.Value.GetVector3(i)
                );
        }
        return tr;
    }

    public static Animation FromGLTF(JsonElement element, GLTFBufferAccessor[] accessors, string[] names, int index)
    {
        var a = new Animation();
        if (element.TryGetStringProperty("name", out var nameProp))
            a.Name = nameProp;
        else
            a.Name = $"animation#{index}";
        var smp = new List<Sampler>();
        if (element.TryGetProperty("samplers", out var samplers))
        {
            foreach (var s in samplers.EnumerateArray())
            {
                var input = s.GetProperty("input").GetInt32();
                var output = s.GetProperty("output").GetInt32();
                Interp interp = Interp.LINEAR;
                if (s.TryGetStringProperty("interpolation", out var iProp))
                    interp = Enum.Parse<Interp>(iProp);
                smp.Add(new Sampler(accessors[input], accessors[output], interp switch
                {
                    Interp.CUBICSPLINE => AnimationInterpolation.CubicSpline,
                    Interp.STEP => AnimationInterpolation.Step,
                    _ => AnimationInterpolation.Linear,
                }));
            }
        }
        else
        {
            a.Rotations = Array.Empty<RotationChannel>();
            a.Translations = Array.Empty<VectorChannel>();
            a.Scales = Array.Empty<VectorChannel>();
            return a;
        }

        if (element.TryGetProperty("channels", out var channels))
        {
            var translations = new List<VectorChannel>();
            var rotations = new List<RotationChannel>();
            var scales = new List<VectorChannel>();

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
                    translations.Add(GetVectorChannel(names[tgtNode.GetInt32()], smp[sampler.GetInt32()]));
                }
                if ("scale".Equals(tgtPath.GetString(), StringComparison.OrdinalIgnoreCase))
                {
                    scales.Add(GetVectorChannel(names[tgtNode.GetInt32()], smp[sampler.GetInt32()]));
                }
                if ("rotation".Equals(tgtPath.GetString(), StringComparison.OrdinalIgnoreCase))
                {
                    var rc = new RotationChannel();
                    rc.Target = names[tgtNode.GetInt32()];
                    var s = smp[sampler.GetInt32()];
                    rc.Interpolation = s.Interpolation;
                    rc.Keyframes = new RotationKeyframe[s.Time.Count];
                    if (rc.Interpolation == AnimationInterpolation.CubicSpline)
                    {
                        rc.Tangents = new RotationTangent[s.Time.Count];
                        for (int i = 0; i < s.Time.Count; i++)
                        {
                            var v4 = s.Value.GetVector4(i * 3 + 1);
                            rc.Keyframes[i] = new RotationKeyframe(
                                s.Time.GetFloat(i),
                                new Quaternion(v4.X, v4.Y, v4.Z, v4.W)
                            );
                            rc.Tangents[i] = new RotationTangent(
                                s.Value.GetVector4(i * 3),
                                s.Value.GetVector4(i * 3 + 2)
                            );
                        }
                    }
                    else
                    {
                        for (int i = 0; i < s.Time.Count; i++)
                        {
                            var v4 = s.Value.GetVector4(i);
                            rc.Keyframes[i] = new RotationKeyframe(
                                s.Time.GetFloat(i),
                                new Quaternion(v4.X, v4.Y, v4.Z, v4.W)
                            );
                        }
                    }

                    rotations.Add(rc);
                }
                a.Translations = translations.ToArray();
                a.Rotations = rotations.ToArray();
                a.Scales = scales.ToArray();
            }
        }
        return a;
    }

}
