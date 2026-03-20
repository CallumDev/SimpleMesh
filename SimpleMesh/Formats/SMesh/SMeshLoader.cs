using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using SimpleMesh.Util;

namespace SimpleMesh.Formats.SMesh
{
    public class SMeshLoader
    {
        static string? GetString(BinaryReader reader, string[] strings)
        {
            var i = (int)reader.ReadVarUInt32();
            if (i == 0) return null;
            if (i == 1) return "";
            return strings[i - 2];
        }

        record struct SkinRefs(int Root, int[] Joints);

        public static Model Load(Stream stream, ModelLoadContext ctx)
        {
            byte[] magic = new byte[4];
            int mlen = stream.Read(magic, 0, 4);
            if (mlen != 4 ||
                magic[0] != (byte)'S' ||
                magic[1] != (byte)'M' ||
                magic[2] != (byte)'S' ||
                magic[3] != (byte)'H')
                throw new ModelLoadException("Not a valid SMesh file");
            using var comp = new DeflateStream(stream, CompressionMode.Decompress);
            using var reader = new BinaryReader(comp);
            var model = new Model();
            var strCount = (int)reader.ReadVarUInt32();
            var strings = new string[strCount];
            for (int i = 0; i < strings.Length; i++)
                strings[i] = reader.ReadStringUTF8()!;

            model.Copyright = GetString(reader, strings);
            model.Generator = GetString(reader, strings);
            var matCount = (int)reader.ReadVarUInt32();
            model.Materials = new Dictionary<string, Material>(matCount);
            for (int i = 0; i < matCount; i++)
            {
                var mat = new Material()
                {
                    Name = GetString(reader, strings)!,
                    DiffuseTexture = ReadTexInfo(reader, strings),
                    DiffuseColor = reader.ReadLinearColor(),
                    EmissiveTexture = ReadTexInfo(reader, strings),
                    EmissiveColor = reader.ReadVector3(),
                    NormalTexture = ReadTexInfo(reader, strings),
                    MetallicRoughness = reader.ReadByte() != 0,
                    MetallicFactor = reader.ReadSingle(),
                    RoughnessFactor = reader.ReadSingle(),
                    MetallicRoughnessTexture = ReadTexInfo(reader, strings),
                };
                model.Materials.Add(mat.Name, mat);
            }

            model.Geometries = new Geometry[(int)reader.ReadVarUInt32()];
            for (int i = 0; i < model.Geometries.Length; i++)
            {
                model.Geometries[i] = ReadGeometry(reader, model.Materials, strings);
            }

            model.Skins = new Skin[(int)reader.ReadVarUInt32()];
            var refs = new SkinRefs[model.Skins.Length];
            for (int i = 0; i < model.Geometries.Length; i++)
            {
                // Concrete data
                model.Skins[i] = new();
                model.Skins[i].Name = GetString(reader, strings)!;
                var boneCount = (int)reader.ReadVarUInt32();
                model.Skins[i].InverseBindMatrices = new Matrix4x4[boneCount];
                bool hasInvBindPose = reader.ReadByte() != 0;
                if (hasInvBindPose)
                {
                    var f = new FloatBuffer(16, boneCount);
                    f.Read(reader);
                    for (int j = 0; j < boneCount; j++)
                    {
                        model.Skins[i].InverseBindMatrices[j] = new(
                            f[0, j], f[1, j], f[2, j], f[3, j],
                            f[4, j], f[5, j], f[6, j], f[7, j],
                            f[8, j], f[9, j], f[10, j], f[11, j],
                            f[12, j], f[13, j], f[14, j], f[15, j]
                        );
                    }
                }
                else
                {
                    for (int j = 0; j < boneCount; j++)
                        model.Skins[i].InverseBindMatrices[j] = Matrix4x4.Identity;
                }

                // References
                var r = (int)reader.ReadVarUInt32();
                var joints = new int[boneCount];
                for (int j = 0; j < joints.Length; j++)
                    joints[j] = (int)reader.ReadVarUInt32();
                refs[i] = new(r, joints);
            }

            List<ModelNode> allNodes = new();
            model.Roots = new ModelNode[(int)reader.ReadVarUInt32()];
            for (int i = 0; i < model.Roots.Length; i++)
            {
                model.Roots[i] = ReadNode(reader, model.Geometries, model.Skins, allNodes, strings);
            }

            // resolve references for skin
            for (int i = 0; i < model.Skins.Length; i++)
            {
                model.Skins[i].Root = refs[i].Root > 0
                    ? allNodes[refs[i].Root - 1]
                    : null;
                model.Skins[i].Bones = new ModelNode[refs[i].Joints.Length];
                for (int j = 0; j < model.Skins[i].Bones.Length; j++)
                {
                    model.Skins[i].Bones[j] = allNodes[refs[i].Joints[j]];
                }
            }

            var imageCount = (int)reader.ReadVarUInt32();
            if (imageCount != 0)
            {
                imageCount--;
                model.Images = new Dictionary<string, ImageData>();
                for (int i = 0; i < imageCount; i++)
                {
                    var name = GetString(reader, strings)!;
                    var mime = GetString(reader, strings)!;
                    var len = (int)reader.ReadVarUInt32();
                    var data = reader.ReadBytes(len);
                    model.Images[name] = new ImageData(name, data, mime);
                }
            }

            var animationCount = (int)reader.ReadVarUInt32();
            if (animationCount != 0)
            {
                animationCount--;
                model.Animations = new Animation[animationCount];
                for (int i = 0; i < animationCount; i++)
                {
                    var anm = new Animation();
                    anm.Name = GetString(reader, strings)!;
                    anm.Rotations = new RotationChannel[(int)reader.ReadVarUInt32()];
                    for (int j = 0; j < anm.Rotations.Length; j++)
                    {
                        var r = new RotationChannel();
                        r.Target = GetString(reader, strings)!;
                        r.Keyframes = new RotationKeyframe[(int)reader.ReadVarUInt32()];
                        for (int k = 0; k < r.Keyframes.Length; k++)
                        {
                            r.Keyframes[k].Time = reader.ReadSingle();
                            r.Keyframes[k].Rotation = new Quaternion(
                                reader.ReadSingle(), reader.ReadSingle(),
                                reader.ReadSingle(), reader.ReadSingle());
                        }

                        anm.Rotations[j] = r;
                    }

                    anm.Translations = new TranslationChannel[(int)reader.ReadVarUInt32()];
                    for (int j = 0; j < anm.Translations.Length; j++)
                    {
                        var t = new TranslationChannel();
                        t.Target = GetString(reader, strings)!;
                        t.Keyframes = new TranslationKeyframe[(int)reader.ReadVarUInt32()];
                        for (int k = 0; k < t.Keyframes.Length; k++)
                        {
                            t.Keyframes[k].Time = reader.ReadSingle();
                            t.Keyframes[k].Translation = new Vector3(reader.ReadSingle(), reader.ReadSingle(),
                                reader.ReadSingle());
                        }

                        anm.Translations[j] = t;
                    }

                    model.Animations[i] = anm;
                }
            }

            return model;
        }

        static TextureInfo? ReadTexInfo(BinaryReader reader, string[] strings)
        {
            var name = GetString(reader, strings);
            if (name == null) return null;
            return new TextureInfo(name, reader.ReadByte());
        }

        static PropertyValue ReadProperty(BinaryReader reader, string[] strings)
        {
            var d = reader.ReadByte();
            switch ((PropertyKind)(d & 0x7F))
            {
                case PropertyKind.Boolean:
                    return new PropertyValue((d & 0x80) != 0);
                case PropertyKind.Float:
                    return new PropertyValue(reader.ReadSingle());
                case PropertyKind.FloatArray:
                    var fa = new float[(int)reader.ReadVarUInt32()];
                    for (int i = 0; i < fa.Length; i++) fa[i] = reader.ReadSingle();
                    return new PropertyValue(fa);
                case PropertyKind.Int:
                    return new PropertyValue(reader.ReadInt32());
                case PropertyKind.IntArray:
                    var ia = new int[(int)reader.ReadVarUInt32()];
                    for (int i = 0; i < ia.Length; i++) ia[i] = reader.ReadInt32();
                    return new PropertyValue(ia);
                case PropertyKind.String:
                    return new PropertyValue(GetString(reader, strings)!);
                case PropertyKind.Vector3:
                    return new PropertyValue(reader.ReadVector3());
                default:
                    return new PropertyValue();
            }
        }

        static ModelNode ReadNode(BinaryReader reader, Geometry[] geometries, Skin[] skins, List<ModelNode> allNodes,
            string[] strings)
        {
            var node = new ModelNode();
            allNodes.Add(node); // index by write order
            node.Name = GetString(reader, strings)!;
            int propCount = (int)reader.ReadVarUInt32();
            node.Properties = new Dictionary<string, PropertyValue>(propCount);
            for (int i = 0; i < propCount; i++)
            {
                node.Properties[GetString(reader, strings)!] = ReadProperty(reader, strings);
            }

            var tr = reader.ReadByte();
            if (tr != 0) node.Transform = reader.ReadMatrix4x4();
            var g = (int)reader.ReadVarUInt32();
            if (g != 0)
            {
                node.Geometry = geometries[g - 1];
            }

            var s = (int)reader.ReadVarUInt32();
            if (s != 0)
            {
                node.Skin = skins[s - 1];
            }

            var childCount = (int)reader.ReadVarUInt32();
            node.Children = new List<ModelNode>(childCount);
            for (int i = 0; i < childCount; i++)
                node.Children.Add(ReadNode(reader, geometries, skins, allNodes, strings));
            return node;
        }

        static Geometry ReadGeometry(BinaryReader reader, Dictionary<string, Material> mats, string[] strings)
        {
            var name = GetString(reader, strings)!;
            var kind = (GeometryKind)reader.ReadByte();
            var attrs = (VertexAttributes)reader.ReadUInt16();
            var center = reader.ReadVector3();
            var radius = reader.ReadSingle();
            var min = reader.ReadVector3();
            var max = reader.ReadVector3();
            var groups = new TriangleGroup[(int)reader.ReadVarUInt32()];
            for (int i = 0; i < groups.Length; i++)
            {
                var baseVertex = (int)reader.ReadVarUInt32();
                var startIndex = (int)reader.ReadVarUInt32();
                var indexCount = (int)reader.ReadVarUInt32();
                var matname = GetString(reader, strings)!;
                if (!mats.TryGetValue(matname, out var mat))
                {
                    throw new ModelLoadException($"Undefined material referenced `{matname}`");
                }
                groups[i] = new TriangleGroup(mat)
                {
                    BaseVertex = baseVertex,
                    StartIndex = startIndex,
                    IndexCount = indexCount
                };
            }

            var vertices = new VertexArray(attrs, (int)reader.ReadVarUInt32());
            int channels = 3;
            if ((attrs & VertexAttributes.Normal) == VertexAttributes.Normal)
                channels += 3;
            if ((attrs & VertexAttributes.Diffuse) == VertexAttributes.Diffuse)
                channels += 4;
            if ((attrs & VertexAttributes.Tangent) == VertexAttributes.Tangent)
                channels += 4;
            if ((attrs & VertexAttributes.Texture1) == VertexAttributes.Texture1)
                channels += 2;
            if ((attrs & VertexAttributes.Texture2) == VertexAttributes.Texture2)
                channels += 2;
            if ((attrs & VertexAttributes.Texture3) == VertexAttributes.Texture3)
                channels += 2;
            if ((attrs & VertexAttributes.Texture4) == VertexAttributes.Texture4)
                channels += 2;
            if ((attrs & VertexAttributes.Joints) == VertexAttributes.Joints)
                channels += 4;
            var f = new FloatBuffer(channels, vertices.Count);
            f.Read(reader);
            for (int i = 0; i < vertices.Count; i++)
            {
                vertices.Position[i] = new(f[0, i], f[1, i], f[2, i]);
                int c = 3;
                if ((attrs & VertexAttributes.Normal) == VertexAttributes.Normal)
                {
                    vertices.Normal[i] = new(f[c++, i], f[c++, i], f[c++, i]);
                }

                if ((attrs & VertexAttributes.Diffuse) == VertexAttributes.Diffuse)
                {
                    vertices.Diffuse[i] = new(f[c++, i], f[c++, i], f[c++, i], f[c++, i]);
                }

                if ((attrs & VertexAttributes.Tangent) == VertexAttributes.Tangent)
                {
                    vertices.Tangent[i] = new(f[c++, i], f[c++, i], f[c++, i], f[c++, i]);
                }

                if ((attrs & VertexAttributes.Texture1) == VertexAttributes.Texture1)
                {
                    vertices.Texture1[i] = new(f[c++, i], f[c++, i]);
                }

                if ((attrs & VertexAttributes.Texture2) == VertexAttributes.Texture2)
                {
                    vertices.Texture2[i] = new(f[c++, i], f[c++, i]);
                }

                if ((attrs & VertexAttributes.Texture3) == VertexAttributes.Texture3)
                {
                    vertices.Texture3[i] = new(f[c++, i], f[c++, i]);
                }

                if ((attrs & VertexAttributes.Texture4) == VertexAttributes.Texture4)
                {
                    vertices.Texture4[i] = new(f[c++, i], f[c++, i]);
                }
                if ((attrs & VertexAttributes.Joints) == VertexAttributes.Joints)
                {
                    vertices.JointWeights[i] = new(f[c++, i], f[c++, i], f[c++, i], f[c++, i]);
                }
            }

            if ((attrs & VertexAttributes.Joints) == VertexAttributes.Joints)
            {
                for (int i = 0; i < vertices.Count; i++)
                {
                    vertices.JointIndices[i] =
                        new(reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadUInt16());
                }
            }

            var indexType = reader.ReadByte();
            Indices indices = indexType == 0
                ? new Indices(IndexCoding.Decode16(reader))
                    :  new Indices(IndexCoding.Decode32(reader));
            var g = new Geometry(vertices, indices);
            g.Name = name;
            g.Kind = kind;
            g.Center = center;
            g.Radius = radius;
            g.Min = min;
            g.Max = max;
            g.Groups = groups;
            return g;
        }
    }
}
