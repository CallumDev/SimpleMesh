using System.Numerics;

namespace SimpleMesh;

public class Skin
{
    public string Name = "";
    public ModelNode? Root;
    public ModelNode[] Bones = [];
    public Matrix4x4[] InverseBindMatrices = [];
}