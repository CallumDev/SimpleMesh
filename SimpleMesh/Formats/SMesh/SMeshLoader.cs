using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using SimpleMesh.Util;

namespace SimpleMesh.Formats.SMesh
{
    public class SMeshLoader
    {
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
            var matCount = reader.Read7BitEncodedInt();
            model.Materials = new Dictionary<string, Material>(matCount);
            for (int i = 0; i < matCount; i++)
            {
                var mat = new Material()
                {
                    Name = reader.ReadStringUTF8(),
                    DiffuseTexture = ReadTexInfo(reader),
                    DiffuseColor = reader.ReadLinearColor(),
                    EmissiveTexture = ReadTexInfo(reader),
                    EmissiveColor = reader.ReadVector3(),
                    NormalTexture = ReadTexInfo(reader),
                    MetallicRoughness = reader.ReadByte() != 0,
                    MetallicFactor = reader.ReadSingle(),
                    RoughnessFactor = reader.ReadSingle(),
                    MetallicRoughnessTexture = ReadTexInfo(reader),
                };
                model.Materials.Add(mat.Name, mat);
            }
            model.Geometries = new Geometry[reader.Read7BitEncodedInt()];
            for (int i = 0; i < model.Geometries.Length; i++)
            {
                model.Geometries[i] = ReadGeometry(reader, model.Materials);
            }
            model.Roots = new ModelNode[reader.Read7BitEncodedInt()];
            for (int i = 0; i < model.Roots.Length; i++)
            {
                model.Roots[i] = ReadNode(reader, model.Geometries, model.Materials);
            }
            var imageCount = reader.Read7BitEncodedInt();
            if (imageCount != 0)
            {
                imageCount--;
                model.Images = new Dictionary<string, ImageData>();
                for (int i = 0; i < imageCount; i++)
                {
                    var name = reader.ReadStringUTF8();
                    var mime = reader.ReadStringUTF8();
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
                    anm.Name = reader.ReadStringUTF8();
                    anm.Rotations = new RotationChannel[reader.Read7BitEncodedInt()];
                    for (int j = 0; j < anm.Rotations.Length; j++)
                    {
                        var r = new RotationChannel();
                        r.Target = reader.ReadStringUTF8();
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
                        t.Target = reader.ReadStringUTF8();
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

        static TextureInfo ReadTexInfo(BinaryReader reader)
        {
            var name = reader.ReadStringUTF8();
            if (name == null) return null;
            return new TextureInfo(name, reader.ReadByte());
        }

        static PropertyValue ReadProperty(BinaryReader reader)
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
                    return new PropertyValue(reader.ReadStringUTF8());
                case PropertyKind.Vector3:
                    return new PropertyValue(reader.ReadVector3());
                default:
                    return new PropertyValue();
            }
        }

        static ModelNode ReadNode(BinaryReader reader, Geometry[] geometries, Dictionary<string, Material> mats)
        {
            var node = new ModelNode();
            node.Name = reader.ReadStringUTF8();
            int propCount = reader.Read7BitEncodedInt();
            node.Properties = new Dictionary<string, PropertyValue>(propCount);
            for (int i = 0; i < propCount; i++)
            {
                node.Properties[reader.ReadStringUTF8()] = ReadProperty(reader);
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
                node.Children.Add(ReadNode(reader, geometries, mats));
            return node;
        }

        static Geometry ReadGeometry(BinaryReader reader, Dictionary<string, Material> mats)
        {
            var g = new Geometry();
            g.Name = reader.ReadStringUTF8();
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
                var matname = reader.ReadStringUTF8();
                if (!mats.TryGetValue(matname, out g.Groups[i].Material)) {
                    throw new ModelLoadException($"Undefined material referenced `{matname}`");
                }
            }
            g.Vertices = new Vertex[reader.Read7BitEncodedInt()];
            for (int i = 0; i < g.Vertices.Length; i++)
            {
                g.Vertices[i].Position = reader.ReadVector3();
                if ((g.Attributes & VertexAttributes.Normal) == VertexAttributes.Normal)
                    g.Vertices[i].Normal = reader.ReadVector3();
                if ((g.Attributes & VertexAttributes.Diffuse) == VertexAttributes.Diffuse)
                    g.Vertices[i].Diffuse = reader.ReadLinearColor();
                else
                    g.Vertices[i].Diffuse = LinearColor.White;
                if ((g.Attributes & VertexAttributes.Tangent) == VertexAttributes.Tangent)
                    g.Vertices[i].Tangent = reader.ReadVector4();
                if ((g.Attributes & VertexAttributes.Texture1) == VertexAttributes.Texture1)
                    g.Vertices[i].Texture1 = reader.ReadVector2();
                if ((g.Attributes & VertexAttributes.Texture2) == VertexAttributes.Texture2)
                    g.Vertices[i].Texture2 = reader.ReadVector2();
                if ((g.Attributes & VertexAttributes.Texture3) == VertexAttributes.Texture3)
                    g.Vertices[i].Texture3 = reader.ReadVector2();
                if ((g.Attributes & VertexAttributes.Texture4) == VertexAttributes.Texture4)
                    g.Vertices[i].Texture4 = reader.ReadVector2();
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