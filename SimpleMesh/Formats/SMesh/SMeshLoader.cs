using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using SimpleMesh.Util;

namespace SimpleMesh.Formats.SMesh
{
    public class SMeshLoader
    {

        static string GetString(BinaryReader reader, string[] strings)
        {
            var i = reader.Read7BitEncodedInt();
            if (i == 0) return null;
            if (i == 1) return "";
            return strings[i - 2];
        }
        
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
            var strCount = reader.Read7BitEncodedInt();
            var strings = new string[strCount];
            for (int i = 0; i < strings.Length; i++)
                strings[i] = reader.ReadStringUTF8();
            
            model.Copyright = GetString(reader, strings);
            model.Generator = GetString(reader, strings);
            var matCount = reader.Read7BitEncodedInt();
            model.Materials = new Dictionary<string, Material>(matCount);
            for (int i = 0; i < matCount; i++)
            {
                var mat = new Material()
                {
                    Name = GetString(reader, strings),
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
            model.Geometries = new Geometry[reader.Read7BitEncodedInt()];
            for (int i = 0; i < model.Geometries.Length; i++)
            {
                model.Geometries[i] = ReadGeometry(reader, model.Materials, strings);
            }
            model.Roots = new ModelNode[reader.Read7BitEncodedInt()];
            for (int i = 0; i < model.Roots.Length; i++)
            {
                model.Roots[i] = ReadNode(reader, model.Geometries, model.Materials, strings);
            }
            var imageCount = reader.Read7BitEncodedInt();
            if (imageCount != 0)
            {
                imageCount--;
                model.Images = new Dictionary<string, ImageData>();
                for (int i = 0; i < imageCount; i++)
                {
                    var name = GetString(reader, strings);
                    var mime = GetString(reader, strings);
                    var len = reader.Read7BitEncodedInt();
                    var data = reader.ReadBytes(len);
                    model.Images[name] = new ImageData(name, data, mime);
                }
            }
            var animationCount = reader.Read7BitEncodedInt();
            if (animationCount != 0)
            {
                animationCount--;
                model.Animations = new Animation[animationCount];
                for (int i = 0; i < animationCount; i++)
                {
                    var anm = new Animation();
                    anm.Name = GetString(reader, strings);
                    anm.Rotations = new RotationChannel[reader.Read7BitEncodedInt()];
                    for (int j = 0; j < anm.Rotations.Length; j++)
                    {
                        var r = new RotationChannel();
                        r.Target = GetString(reader, strings);
                        r.Keyframes = new RotationKeyframe[reader.Read7BitEncodedInt()];
                        for (int k = 0; k < r.Keyframes.Length; k++)
                        {
                            r.Keyframes[k].Time = reader.ReadSingle();
                            r.Keyframes[k].Rotation = new Quaternion(
                                reader.ReadSingle(), reader.ReadSingle(),
                                reader.ReadSingle(), reader.ReadSingle());
                        }
                        anm.Rotations[j] = r;
                    }
                    anm.Translations = new TranslationChannel[reader.Read7BitEncodedInt()];
                    for (int j = 0; j < anm.Translations.Length; j++)
                    {
                        var t = new TranslationChannel();
                        t.Target = GetString(reader, strings);
                        t.Keyframes = new TranslationKeyframe[reader.Read7BitEncodedInt()];
                        for (int k = 0; k < t.Keyframes.Length; k++)
                        {
                            t.Keyframes[k].Time = reader.ReadSingle();
                            t.Keyframes[k].Translation = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        }
                        anm.Translations[j] = t;
                    }
                    model.Animations[i] = anm;
                }
            }
            return model;
        }

        static TextureInfo ReadTexInfo(BinaryReader reader, string[] strings)
        {
            var name = GetString(reader, strings);
            if (name == null) return null;
            return new TextureInfo(name, reader.ReadByte());
        }

        static PropertyValue ReadProperty(BinaryReader reader, string[] strings)
        {
            var d = reader.ReadByte();
            switch ((PropertyKind)(d & 0x7F)) {
                case PropertyKind.Boolean:
                    return new PropertyValue((d & 0x80) != 0);
                case PropertyKind.Float:
                    return new PropertyValue(reader.ReadSingle());
                case PropertyKind.FloatArray:
                    var fa = new float[reader.Read7BitEncodedInt()];
                    for (int i = 0; i < fa.Length; i++) fa[i] = reader.ReadSingle();
                    return new PropertyValue(fa);
                case PropertyKind.Int:
                    return new PropertyValue(reader.ReadInt32());
                case PropertyKind.IntArray:
                    var ia = new int[reader.Read7BitEncodedInt()];
                    for (int i = 0; i < ia.Length; i++) ia[i] = reader.ReadInt32();
                    return new PropertyValue(ia);
                case PropertyKind.String:
                    return new PropertyValue(GetString(reader, strings));
                case PropertyKind.Vector3:
                    return new PropertyValue(reader.ReadVector3());
                default:
                    return new PropertyValue();
            }
        }

        static ModelNode ReadNode(BinaryReader reader, Geometry[] geometries, Dictionary<string, Material> mats, string[] strings)
        {
            var node = new ModelNode();
            node.Name = GetString(reader, strings);
            int propCount = reader.Read7BitEncodedInt();
            node.Properties = new Dictionary<string, PropertyValue>(propCount);
            for (int i = 0; i < propCount; i++)
            {
                node.Properties[GetString(reader, strings)] = ReadProperty(reader, strings);
            }
            var tr = reader.ReadByte();
            if (tr != 0) node.Transform = reader.ReadMatrix4x4();
            var g = reader.ReadUInt32();
            if (g != 0)
            {
                node.Geometry = geometries[(int)(g - 1)];
            }
            var childCount = reader.Read7BitEncodedInt();
            node.Children = new List<ModelNode>(childCount);
            for(int i = 0; i < childCount; i++)
                node.Children.Add(ReadNode(reader, geometries, mats, strings));
            return node;
        }

        static Geometry ReadGeometry(BinaryReader reader, Dictionary<string, Material> mats, string[] strings)
        {
            var g = new Geometry();
            g.Name = GetString(reader, strings);
            g.Kind = (GeometryKind) reader.ReadByte();
            g.Attributes = (VertexAttributes) reader.ReadUInt16();
            g.Center = reader.ReadVector3();
            g.Radius = reader.ReadSingle();
            g.Min = reader.ReadVector3();
            g.Max = reader.ReadVector3();
            g.Groups = new TriangleGroup[reader.Read7BitEncodedInt()];
            for (int i = 0; i < g.Groups.Length; i++)
            {
                g.Groups[i] = new TriangleGroup()
                {
                    BaseVertex = reader.ReadInt32(),
                    StartIndex = reader.ReadInt32(),
                    IndexCount = reader.ReadInt32()
                };
                var matname = GetString(reader, strings);
                if (!mats.TryGetValue(matname, out g.Groups[i].Material)) {
                    throw new ModelLoadException($"Undefined material referenced `{matname}`");
                }
            }
            g.Vertices = new Vertex[reader.Read7BitEncodedInt()];
            int channels = 3;
            if ((g.Attributes & VertexAttributes.Normal) == VertexAttributes.Normal)
                channels += 3;
            if ((g.Attributes & VertexAttributes.Diffuse) == VertexAttributes.Diffuse)
                channels += 4;
            if ((g.Attributes & VertexAttributes.Tangent) == VertexAttributes.Tangent)
                channels += 4;
            if ((g.Attributes & VertexAttributes.Texture1) == VertexAttributes.Texture1)
                channels += 2;
            if ((g.Attributes & VertexAttributes.Texture2) == VertexAttributes.Texture2)
                channels += 2;
            if ((g.Attributes & VertexAttributes.Texture3) == VertexAttributes.Texture3)
                channels += 2;
            if ((g.Attributes & VertexAttributes.Texture4) == VertexAttributes.Texture4)
                channels += 2;
            var f = new FloatBuffer(channels, g.Vertices.Length);
            f.Read(reader);
            for (int i = 0; i < g.Vertices.Length; i++)
            {
                g.Vertices[i].Position = new(
                    f.GetFloat(0, i),
                    f.GetFloat(1, i),
                    f.GetFloat(2, i)
                );
                int c = 3;
                if ((g.Attributes & VertexAttributes.Normal) == VertexAttributes.Normal)
                {
                    g.Vertices[i].Normal = new(
                        f.GetFloat(c++, i),
                        f.GetFloat(c++, i),
                        f.GetFloat(c++, i));
                }
                if ((g.Attributes & VertexAttributes.Diffuse) == VertexAttributes.Diffuse)
                {
                    g.Vertices[i].Diffuse = new(
                        f.GetFloat(c++, i),
                        f.GetFloat(c++, i),
                        f.GetFloat(c++, i),
                        f.GetFloat(c++, i)
                    );
                }
                else
                {
                    g.Vertices[i].Diffuse = LinearColor.White;
                }
                if ((g.Attributes & VertexAttributes.Tangent) == VertexAttributes.Tangent)
                {
                    g.Vertices[i].Tangent = new(
                        f.GetFloat(c++, i),
                        f.GetFloat(c++, i),
                        f.GetFloat(c++, i),
                        f.GetFloat(c++, i)
                    );
                }
                if ((g.Attributes & VertexAttributes.Texture1) == VertexAttributes.Texture1)
                {
                    g.Vertices[i].Texture1 = new(f.GetFloat(c++, i), f.GetFloat(c++, i));
                }
                if ((g.Attributes & VertexAttributes.Texture2) == VertexAttributes.Texture2)
                {
                    g.Vertices[i].Texture2 = new(f.GetFloat(c++, i), f.GetFloat(c++, i));
                }
                if ((g.Attributes & VertexAttributes.Texture3) == VertexAttributes.Texture3)
                {
                    g.Vertices[i].Texture3 = new(f.GetFloat(c++, i), f.GetFloat(c++, i));
                }
                if ((g.Attributes & VertexAttributes.Texture4) == VertexAttributes.Texture4)
                {
                    g.Vertices[i].Texture4 = new(f.GetFloat(c++, i), f.GetFloat(c++, i));
                }
            }

            var indexType = reader.ReadByte();
            if (indexType == 0) {
                g.Indices = new Indices() {Indices16 = IndexCoding.Decode16(reader)};
            }
            else {
                g.Indices = new Indices() {Indices32 = IndexCoding.Decode32(reader)};
            }
            return g;
        }
    }
}