using System.Collections.Generic;
using System.Numerics;

namespace SimpleMesh
{
    public class ModelNode
    {
        public string Name;
        public Matrix4x4 Transform = Matrix4x4.Identity;
        public Geometry Geometry;
        public List<ModelNode> Children = new List<ModelNode>();
        public Dictionary<string, string> Properties = new Dictionary<string, string>();
    }
}