using System;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using SimpleMesh.Util;

namespace SimpleMesh.Formats.SMesh
{
    static class SMeshWriter
    {
        public static void Write(Model model, Stream stream)
        {
            stream.WriteByte((byte)'S');
            stream.WriteByte((byte)'M');
            stream.WriteByte((byte)'S');
            stream.WriteByte((byte)'H');
            using var comp = new DeflateStream(stream, CompressionLevel.Optimal);
            using var writer = new BinaryWriter(comp);
            writer.Write7BitEncodedInt(model.Materials.Count);
            foreach (var m in model.Materials.Values)
            {
                writer.WriteStringUTF8(m.Name);
                writer.WriteStringUTF8(m.DiffuseTexture);
                writer.Write(m.DiffuseColor);
            }
            writer.Write7BitEncodedInt(model.Geometries.Length);
            foreach (var g in model.Geometries)
            {
                WriteGeometry(g, writer);
            }
            writer.Write7BitEncodedInt(model.Roots.Length);
            foreach (var n in model.Roots)
            {
                WriteNode(n, model.Geometries, writer);
            }
        }

        static void WriteNode(ModelNode n, Geometry[] geometries, BinaryWriter writer)
        {
            writer.WriteStringUTF8(n.Name);
            writer.Write7BitEncodedInt(n.Properties.Count);
            foreach (var kv in n.Properties) {
                writer.WriteStringUTF8(kv.Key);
                writer.WriteStringUTF8(kv.Value);
            }
            if (n.Transform == Matrix4x4.Identity) {
                writer.Write((byte)0);
            } else {
                writer.Write((byte)1);
                writer.Write(n.Transform);
            }
            if (n.Geometry == null)
            {
                writer.Write((uint) 0);
            } else
            {
                var idx = -1;
                for (int i = 0; i < geometries.Length; i++)
                {
                    if (geometries[i] == n.Geometry) {
                        idx = i;
                        break;
                    }
                }
                if (idx == -1)
                {
                    throw new Exception("All ModelNode geometries must be present in model geometry array");
                }
                else {
                    writer.Write((uint)(idx + 1));
                }
            }
            writer.Write7BitEncodedInt(n.Children.Count);
            foreach (var child in n.Children) {
                WriteNode(child, geometries, writer);
            }
        }

        static void WriteGeometry(Geometry g, BinaryWriter writer)
        {
            writer.WriteStringUTF8(g.Name);
            writer.Write((ushort)g.Attributes);
            writer.Write(g.Center);
            writer.Write(g.Radius);
            writer.Write(g.Min);
            writer.Write(g.Max);
            writer.Write7BitEncodedInt(g.Groups.Length);
            foreach (var tg in g.Groups)
            {
                writer.Write(tg.BaseVertex);
                writer.Write(tg.StartIndex);
                writer.Write(tg.IndexCount);
                writer.WriteStringUTF8(tg.Material.Name);
            }
            writer.Write7BitEncodedInt(g.Vertices.Length);
            foreach (var v in g.Vertices) {
                writer.Write(v.Position);
                if((g.Attributes & VertexAttributes.Normal) == VertexAttributes.Normal)
                    writer.Write(v.Normal);
                if((g.Attributes & VertexAttributes.Diffuse) == VertexAttributes.Diffuse)
                    writer.Write(v.Diffuse);
                if((g.Attributes & VertexAttributes.Texture1) == VertexAttributes.Texture1)
                    writer.Write(v.Texture1);
                if((g.Attributes & VertexAttributes.Texture2) == VertexAttributes.Texture2)
                    writer.Write(v.Texture2);
            }
            if (g.Indices.Indices16 != null) {
                writer.Write((byte)0);
                IndexCoding.Encode16(g.Indices.Indices16, writer);
            }
            else {
                writer.Write((byte)1);
                IndexCoding.Encode32(g.Indices.Indices32, writer);
            }
        }
    }
}