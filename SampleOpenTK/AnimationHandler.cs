using System.Collections.Generic;
using System.Numerics;
using SimpleMesh;

namespace SampleOpenTK;

public class AnimationInstance
{
    public float Time;
    public Animation Animation;
}

public class AnimationHandler
{
    public List<AnimationInstance> Instances = new List<AnimationInstance>();

    public void Update(float dt)
    {
        for (int i = 0; i < Instances.Count; i++)
            Instances[i].Time += dt;
    }
    
    public (bool Translation, bool Rotation) GetAnimated(string node, out Vector3 translated, out Quaternion rotated)
    {
        Vector3? tr = null;
        Quaternion? q = null;
        for (int i = Instances.Count - 1; i >= 0; i--)
        {
            if (tr == null)
            {
                for (int j = 0; j < Instances[i].Animation.Translations.Length; j++)
                {
                    var tch = Instances[i].Animation.Translations[j];
                    if (tch.Target != node) continue;
                    tr = tch.GetAtTime(Instances[i].Time);
                    break;
                }
            }
            if (q == null)
            {
                for (int j = 0; j < Instances[i].Animation.Rotations.Length; j++)
                {
                    var rch = Instances[i].Animation.Rotations[j];
                    if (rch.Target != node) continue;
                    q = rch.GetAtTime(Instances[i].Time);
                    break;
                }
            }
            if (tr != null && q != null)
                break;
        }
        translated = tr ?? Vector3.Zero;
        rotated = q ?? Quaternion.Identity;
        return (tr != null, q != null);
    }
}