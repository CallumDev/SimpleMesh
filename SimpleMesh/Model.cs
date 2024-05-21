using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using SimpleMesh.Formats;
using SimpleMesh.Formats.Collada;
using SimpleMesh.Formats.GLTF;
using SimpleMesh.Formats.SMesh;

namespace SimpleMesh
{
    public class Model
    {
        public ModelNode[] Roots;
        public Geometry[] Geometries;
        public Dictionary<string, Material> Materials;
        public Dictionary<string, ImageData> Images;
        public Animation[] Animations;

        public static Model FromStream(Stream stream)
        {
            return Autodetect.Load(stream, new ModelLoadContext());
        }

        public Model AutoselectRoot(out bool success)
        {
            if (Roots.Length <= 1)
            {
                success = true;
                return this;
            }

            int withGeometry = -1;
            for (int i = 0; i < Roots.Length; i++) {
                if (HasGeometry(Roots[i]))
                {
                    if (withGeometry == -1) withGeometry = i;
                    else
                    {
                        success = false;
                        return this;
                    }
                }
            }
            if (withGeometry != -1) {
                Roots = new ModelNode[] {Roots[withGeometry]};
                success = true;
            }
            else {
                success = false;
            }
            return this;
        }

        public Model ApplyScale()
        {
            foreach (var m in Roots) {
                ApplyScale(m, Vector3.One);
            }
            return this;
        }

        static void ApplyScale(ModelNode node, Vector3 parentScale)
        {
            Matrix4x4.Decompose(node.Transform, out var scale, out var rotate, out var translate);
            var myScale = scale * parentScale;
            if (myScale != Vector3.One) {
                if (node.Geometry != null)
                {
                    for (int i = 0; i < node.Geometry.Vertices.Length; i++)
                    {
                        node.Geometry.Vertices[i].Position *= myScale;
                        node.Geometry.Vertices[i].Normal =
                            Vector3.Normalize(myScale * node.Geometry.Vertices[i].Normal);
                    }
                }

                node.Transform = Matrix4x4.CreateFromQuaternion(rotate) *
                                 Matrix4x4.CreateTranslation(translate * myScale);
            }
            foreach (var child in node.Children) {
                ApplyScale(child, myScale);
            }
        }
        
        public Model ApplyRootTransforms(bool translate)
        {
            foreach (var m in Roots) {
                if (m.Transform != Matrix4x4.Identity)
                {
                    var tr = m.Transform;
                    if (!translate) {
                        Matrix4x4.Decompose(tr, out _, out Quaternion rotq, out _);
                        tr = Matrix4x4.CreateFromQuaternion(rotq);
                    }
                    if (m.Geometry != null)
                    {
                        for (int i = 0; i < m.Geometry.Vertices.Length; i++)
                        {
                            m.Geometry.Vertices[i].Position = Vector3.Transform(m.Geometry.Vertices[i].Position, tr);
                            m.Geometry.Vertices[i].Normal = Vector3.TransformNormal(m.Geometry.Vertices[i].Normal, tr);
                        }
                    }
                    foreach (var child in m.Children)
                    {
                        child.Transform = child.Transform * tr;
                    }
                    m.Transform = Matrix4x4.Identity;
                }
            }
            return this;
        }

        static bool HasGeometry(ModelNode node)
        {
            if (node.Geometry != null) return true;
            foreach (var child in node.Children)
            {
                if (HasGeometry(child)) return true;
            }
            return false;
        }
       
        public Model CalculateBounds()
        {
            foreach(var node in AllNodes().Where(x => x.Geometry != null))
                node.Geometry.CalculateBounds();
            return this;
        }

        public Model MergeTriangleGroups(Predicate<Material> canMerge = null)
        {
            foreach(var node in AllNodes().Where(x => x.Geometry != null))
                Passes.MergeTriangleGroups.Apply(canMerge, node.Geometry);
            return this;
        }
        
        IEnumerable<ModelNode> AllNodes(ModelNode n = null)
        {
            if (n != null)
            {
                foreach (var child in n.Children)
                {
                    yield return child;
                    foreach (var x in AllNodes(child)) yield return x;
                }
            }
            else
            {
                foreach (var mn in Roots)
                {
                    yield return mn;
                    foreach (var x in AllNodes(mn)) yield return x;
                }
            }
        }

        public void SaveTo(Stream stream, ModelSaveFormat format = ModelSaveFormat.SMesh)
        {
            switch (format)
            {
                case ModelSaveFormat.SMesh:
                    SMeshWriter.Write(this, stream);
                    break;
                case ModelSaveFormat.GLTF2:
                    GLTFWriter.Write(this, stream, false);
                    break;
                case ModelSaveFormat.GLB:
                    GLTFWriter.Write(this, stream, true);
                    break;
                case ModelSaveFormat.Collada:
                    ColladaWriter.Write(this, stream);
                    break;
                default:
                    throw new ArgumentException( null, nameof(format));
            }
        }

        public Model Clone()
        {
            var m = new Model();
            var mats = new Dictionary<string, Material>();
            foreach (var kv in Materials)
                mats[kv.Key] = kv.Value.Clone();
            m.Materials = mats;
            m.Geometries = Geometries.Select(x => x.Clone(m)).ToArray();
            m.Roots = Roots.Select(x => x.Clone(m, this)).ToArray();
            if(Animations != null)
                m.Animations = Animations.Select(x => x.Clone()).ToArray();
            if (Images != null)
                m.Images = new Dictionary<string, ImageData>(Images);
            return m;
        }
        
    }
}
