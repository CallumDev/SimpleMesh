using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using SimpleMesh.Formats;
using SimpleMesh.Formats.SMesh;

namespace SimpleMesh
{
    public class Model
    {
        public ModelNode[] Roots;
        public Geometry[] Geometries;
        public Dictionary<string, Material> Materials;
        public Dictionary<string, ImageData> Images;

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

        public void SaveTo(Stream stream)
        {
            SMeshWriter.Write(this, stream);
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
            if (Images != null)
                m.Images = new Dictionary<string, ImageData>(Images);
            return m;
        }
        
    }
}
