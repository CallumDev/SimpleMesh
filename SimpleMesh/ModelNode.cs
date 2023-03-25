using System;
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
        public Dictionary<string, PropertyValue> Properties = new Dictionary<string, PropertyValue>();

        internal ModelNode Clone(Model newModel, Model existingModel)
        {
            var mn = new ModelNode();
            mn.Name = Name;
            mn.Transform = Transform;
            if (Geometry != null)
            {
                var x = Array.IndexOf(existingModel.Geometries, Geometry);
                if (x != -1)
                    mn.Geometry = newModel.Geometries[x];
                else
                    mn.Geometry = Geometry.Clone(newModel);
            }
            mn.Children = new List<ModelNode>();
            foreach(var c in Children)
                mn.Children.Add(c.Clone(newModel, existingModel));
            mn.Properties = new Dictionary<string, PropertyValue>(Properties);
            return mn;
        }
    }
}
