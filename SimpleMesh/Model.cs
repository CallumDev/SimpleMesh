using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SimpleMesh.Formats;
using SimpleMesh.Formats.SMesh;

namespace SimpleMesh
{
    public class Model
    {
        public ModelNode[] Roots;
        public Geometry[] Geometries;
        public Dictionary<string, Material> Materials;

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
    }
}