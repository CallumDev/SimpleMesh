using System;
using System.Collections.Generic;
using System.Numerics;
using SimpleMesh;

namespace SampleOpenTK;

public class SkinInstance
{
    public Skin Skin;
    public InstanceNode? Root;
    public InstanceNode[] Bones;
    public Matrix4x4[] Matrices;
}

public class ModelInstance
{
    public AnimationHandler Animator = new();
    public InstanceNode[] Roots;
    public SkinInstance[] Skins;
    
    public ModelInstance(Model model)
    {
        Dictionary<ModelNode, InstanceNode> instances = new();
        Dictionary<Skin, SkinInstance> skinInstances = new();
        Roots = new InstanceNode[model.Roots.Length];
        for (int i = 0; i < Roots.Length; i++)
        {
            Roots[i] = AddNode(model.Roots[i], instances);
        }
        Skins = new SkinInstance[model.Skins.Length];
        for (int i = 0; i < model.Skins.Length; i++)
        {
            var si = new SkinInstance()
            {
                Skin = model.Skins[i],
                Root = model.Skins[i].Root == null ? null : instances[model.Skins[i].Root],
                Bones = new InstanceNode[model.Skins[i].Bones.Length],
                Matrices = new Matrix4x4[model.Skins[i].Bones.Length]
            };
            for (int j = 0; j < si.Bones.Length; j++)
            {
                si.Bones[j] = instances[model.Skins[i].Bones[j]];
            }
            skinInstances[model.Skins[i]] = si;
            Skins[i] = si;
        }
        foreach (var kv in instances)
        {
            if (kv.Key.Skin != null)
            {
                kv.Value.Skin = skinInstances[kv.Key.Skin];
            }
        }
        UpdateTransforms();
    }

    public void UpdateTransforms()
    {
        foreach (var node in Roots)
        {
            Transform(node, Matrix4x4.Identity);
        }

        foreach (var sk in Skins)
        {
            Matrix4x4 inv = Matrix4x4.Identity;
            if (sk.Root != null)
                Matrix4x4.Invert(sk.Root.Transform, out inv);
            for (int i = 0; i < sk.Matrices.Length; i++)
            {
                sk.Matrices[i] = sk.Skin.InverseBindMatrices[i] * sk.Bones[i].Transform * inv;
            }
        }
    }

    void Transform(InstanceNode node, Matrix4x4 parent)
    {
        var (animTranslate, animRotate) = Animator.GetAnimated(node.Node.Name, out var tr, out var rot);
        Matrix4x4 selfTransform;
        Matrix4x4.Decompose(node.Node.Transform, out _, out var nodeRotate, out var nodeTranslate);
        if (animTranslate && !animRotate)
            selfTransform = Matrix4x4.CreateFromQuaternion(nodeRotate) * Matrix4x4.CreateTranslation(tr);
        else if (animRotate && !animTranslate)
            selfTransform = Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(nodeTranslate);
        else if (animTranslate && animRotate)
            selfTransform = Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(tr);
        else
            selfTransform = node.Node.Transform;
        node.Transform = selfTransform * parent;
        foreach (var c in node.Children)
        {
            Transform(c, node.Transform);
        }
    }

    InstanceNode AddNode(ModelNode node, Dictionary<ModelNode, InstanceNode> instances)
    {
        var n = new InstanceNode() { Node = node, Transform = node.Transform };
        instances[node] = n;
        foreach(var c in node.Children)
            n.Children.Add(AddNode(c, instances));
        return n;
    }
}

public class InstanceNode
{
    public Matrix4x4 Transform;
    public ModelNode Node;
    public SkinInstance? Skin;
    public List<InstanceNode> Children = [];
}