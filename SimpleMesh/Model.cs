using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
        public ModelNode[] Roots = [];
        public Geometry[] Geometries = [];
        public Dictionary<string, Material> Materials = new();
        public Dictionary<string, ImageData> Images = new();
        public Animation[] Animations = [];
        public Skin[] Skins = [];

        public string? Copyright;
        public string? Generator;

        public static Model FromStream(Stream stream, IExternalResources? resources = null, List<string>? warnings = null)
        {
            return Autodetect.Load(stream, new ModelLoadContext(warnings ?? [])
            {
                ExternalResources = resources ?? new DisallowedResources()
            });
        }
        public static Model FromFile(string filename, List<string>? warnings = null)
        {
            using var stream = File.OpenRead(filename);
            return FromStream(stream, new FileResources(filename), warnings);
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

        static bool AlmostOne(Vector3 x)
        {
            const float TOLERANCE = 0.000001f;
            return Math.Abs(x.X - 1) < TOLERANCE &&
                   Math.Abs(x.Y - 1) < TOLERANCE &&
                   Math.Abs(x.Z - 1) < TOLERANCE;
        }

        static void ApplyScale(ModelNode node, Vector3 parentScale)
        {
            Matrix4x4.Decompose(node.Transform, out var scale, out var rotate, out var translate);
            var myScale = scale * parentScale;
            if (!AlmostOne(myScale))
            {
                if (node.Geometry != null)
                {
                    for (int i = 0; i < node.Geometry.Vertices.Count; i++)
                    {
                        node.Geometry.Vertices.Position[i] *= myScale;

                    }
                    if (node.Geometry.Has(VertexAttributes.Normal))
                    {
                        for (int i = 0; i < node.Geometry.Vertices.Count; i++)
                        {
                            node.Geometry.Vertices.Normal[i] =
                                Vector3.Normalize(myScale * node.Geometry.Vertices.Normal[i]);
                        }
                    }
                }
                node.Transform = Matrix4x4.CreateFromQuaternion(rotate) *
                                 Matrix4x4.CreateTranslation(translate * parentScale);
            }
            else if (!AlmostOne(parentScale))
            {
                node.Transform = Matrix4x4.CreateFromQuaternion(rotate) *
                                 Matrix4x4.CreateTranslation(translate * parentScale);
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
                        for (int i = 0; i < m.Geometry.Vertices.Count; i++)
                        {
                            m.Geometry.Vertices.Position[i] = Vector3.Transform(m.Geometry.Vertices.Position[i], tr);
                        }
                        if (m.Geometry.Has(VertexAttributes.Normal))
                        {
                            for (int i = 0; i < m.Geometry.Vertices.Count; i++)
                            {
                                m.Geometry.Vertices.Normal[i] = Vector3.TransformNormal(m.Geometry.Vertices.Normal[i], tr);
                            }
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
                node.Geometry!.CalculateBounds();
            return this;
        }

        public Model CalculateNormals(bool overwrite = false)
        {
            foreach(var n in AllNodes().Where(x => x.Geometry != null))
                n.Geometry!.CalculateNormals(overwrite);
            return this;
        }

        public unsafe Model CalculateTangents(bool overwrite, bool normalMapped)
        {
            foreach (var g in Geometries)
            {
                if (normalMapped && g.Groups.All(x => x.Material?.NormalTexture == null))
                    continue; //Don't calculate tangents on non-normal mapped model
                g.CalculateTangents(overwrite);
            }
            return this;
        }

        public Model MergeTriangleGroups(Predicate<Material>? canMerge = null)
        {
            foreach(var node in AllNodes().Where(x => x.Geometry != null))
                Passes.MergeTriangleGroups.Apply(canMerge, node.Geometry!);
            return this;
        }

        IEnumerable<ModelNode> AllNodes(ModelNode? n = null)
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
