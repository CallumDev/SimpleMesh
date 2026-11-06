using System;
using System.Collections.Generic;
using System.Numerics;
using SimpleMesh;

namespace SampleOpenTK;

public record SkinInstance(Skin Skin, InstanceNode? Root, InstanceNode[] Bones, Matrix4x4[] Matrices);

public class ModelInstance
{
    public AnimationHandler Animator = new();
    public InstanceNode[] Roots;
    public SkinInstance[] Skins;
    public InstanceNode[] Cameras;

    // Scale component
    public bool HasScale = false;

    public ModelInstance(Model model)
    {
        Dictionary<ModelNode, InstanceNode> instances = new();
        Dictionary<Skin, SkinInstance> skinInstances = new();
        List<InstanceNode> cams = new();
        Roots = new InstanceNode[model.Roots.Length];
        for (int i = 0; i < Roots.Length; i++)
        {
            Roots[i] = AddNode(model.Roots[i], instances, cams);
        }

        Cameras = cams.ToArray();
        Skins = new SkinInstance[model.Skins.Length];
        for (int i = 0; i < model.Skins.Length; i++)
        {
            var si = new SkinInstance(
                model.Skins[i],
                model.Skins[i].Root == null ? null : instances[model.Skins[i].Root!],
                new InstanceNode[model.Skins[i].Bones.Length],
                new Matrix4x4[model.Skins[i].Bones.Length]);
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
        var (animTranslate, animRotate, animScale) =
            Animator.GetAnimated(node.Node.Name, out var tr, out var rot, out var sc);
        Matrix4x4 selfTransform;
        if (animTranslate || animRotate || animScale)
        {
            Matrix4x4.Decompose(node.Node.Transform, out var nodeScale, out var nodeRotate, out var nodeTranslate);
            selfTransform =
                Matrix4x4.CreateScale(animScale ? sc : nodeScale) *
                Matrix4x4.CreateFromQuaternion(animRotate ? rot : nodeRotate) *
                Matrix4x4.CreateTranslation(animTranslate ? tr : nodeTranslate);
        }
        else
        {
            selfTransform = node.Node.Transform;
        }

        node.Transform = selfTransform * parent;
        foreach (var c in node.Children)
        {
            Transform(c, node.Transform);
        }
    }

    InstanceNode AddNode(ModelNode node, Dictionary<ModelNode, InstanceNode> instances, List<InstanceNode> cams)
    {
        if (Matrix4x4.Decompose(node.Transform, out var scale, out _, out _) &&
            Math.Abs(scale.X - 1) > 1e-6 &&
            Math.Abs(scale.Y - 1) > 1e-6 &&
            Math.Abs(scale.Z - 1) > 1e-6)
        {
            HasScale = true;
        }

        var n = new InstanceNode(node);
        instances[node] = n;
        if (node.Camera != null)
            cams.Add(n);
        foreach (var c in node.Children)
            n.Children.Add(AddNode(c, instances, cams));
        return n;
    }
}

public class InstanceNode(ModelNode node)
{
    public Matrix4x4 Transform = node.Transform;
    public ModelNode Node = node;
    public SkinInstance? Skin;
    public List<InstanceNode> Children = [];

    public void PrintCamera()
    {
        if(node.Camera == null)
            Console.WriteLine("Null Camera");
        Console.WriteLine($"{node.Camera.Kind} X: {node.Camera.X} Y: {node.Camera.Y} Aspect: {node.Camera.AspectRatio} Near: {node.Camera.ZNear} Far: {node.Camera.ZFar}");
        var m = Matrix4x4.Decompose(Transform, out _, out var r, out var t);
        Console.WriteLine($"Translation: {t}, Rotation: {r}");
    }

    public Matrix4x4 ViewMatrix()
    {
        var success = Matrix4x4.Invert(Transform, out var inv);
        return success ? inv : Matrix4x4.Identity;
    }

    public Matrix4x4 ProjectionMatrix(float width, float height)
    {
        return node.Camera?.CreateProjectionMatrix(width, height)
               ?? Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3f, width / height,
                   0.01f, 100);
    }

    /// <summary>
    /// Calculates an orthographic projection matrix.
    /// </summary>
    /// <param name="xmag">Magnification in the X axis.</param>
    /// <param name="ymag">Magnification in the Y axis.</param>
    /// <param name="znear">Distance to the near pane in the Z axis.</param>
    /// <param name="zfar">Distance to the far plane in the Z axis.</param>
    /// <returns>A projection matrix</returns>
    static Matrix4x4 CreateOrthographicMatrix(float xmag, float ymag, float znear, float zfar)
    {
        var d = znear - zfar;

        var x = 1 / xmag;
        var y = 1 / ymag;
        var z = 2 / d;
        var w = (zfar + znear) / d;

        return new Matrix4x4
        (
            x, 0, 0, 0,
            0, y, 0, 0,
            0, 0, z, 0,
            0, 0, w, 1
        );
    }

    /// <summary>
    /// Calculates a perspective projection matrix.
    /// </summary>
    /// <param name="aspectRatio">The aspect ratio between horizontal and vertical. (optional)</param>
    /// <param name="yfov">The vertical field of view, in radians.</param>
    /// <param name="znear">Distance to the near pane in the Z axis.</param>
    /// <param name="zfar">Distance to the far plane in the Z axis. Optionally, this value can be positive infinity</param>
    /// <returns>A projection matrix</returns>
    static Matrix4x4 CreatePerspectiveMatrix(float aspectRatio, float yfov, float znear,
        float zfar = float.PositiveInfinity)
    {
       var v = (float)Math.Tan(0.5 * yfov);
       var h = aspectRatio * v;

       var x = 1 / h;
       var y = 1 / v;
       var z = -1f;
       var w = -2 * znear;

       if (!float.IsPositiveInfinity(zfar))
       {
           var d = znear - zfar;
           z = (zfar + znear) / d;
           w = (2f * zfar * znear) / d;
       }

       return new Matrix4x4
       (
           x, 0, 0, 0,
           0, y, 0, 0,
           0, 0, z, -1,
           0, 0, w, 0
       );
    }
}
