using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using SimpleMesh.Util;

namespace SimpleMesh.Formats.SMesh
{
    static class SMeshWriter
    {
        class StringBufferBuilder
        {
            private Dictionary<string, int> strings = new();
            private MemoryStream stream = new();
            private BinaryWriter streamWriter;

            public StringBufferBuilder()
            {
                streamWriter = new BinaryWriter(stream);
            }

            public void AddString(string str)
            {
                if (string.IsNullOrEmpty(str))
                    return;
                if (!strings.ContainsKey(str))
                {
                    var pos = strings.Count;
                    strings[str] = pos;
                    streamWriter.WriteStringUTF8(str);
                }
            }

            public void WriteStringPos(BinaryWriter writer, string str)
            {
                if (str == null)
                {
                    writer.Write7BitEncodedInt(0);
                }
                else if (str == "") 
                {
                    writer.Write7BitEncodedInt(1);
                }
                else
                {
                    writer.Write7BitEncodedInt(strings[str] + 2);
                }
            }

            public void WriteBuffer(BinaryWriter writer)
            {
                writer.Write7BitEncodedInt(strings.Count);
                writer.Write(stream.ToArray());
            }
        }

        static void GetNodeStrings(StringBufferBuilder builder, ModelNode node)
        {
            builder.AddString(node.Name);
            foreach (var kv in node.Properties) {
                builder.AddString(kv.Key);
                if(kv.Value.Value is string s)
                    builder.AddString(s);
            }
            foreach (var c in node.Children)
            {
                GetNodeStrings(builder, c);
            }
        }
        
        public static void Write(Model model, Stream stream)
        {
            stream.WriteByte((byte)'S');
            stream.WriteByte((byte)'M');
            stream.WriteByte((byte)'S');
            stream.WriteByte((byte)'H');
            using var comp = new DeflateStream(stream, CompressionLevel.SmallestSize);
            var ms = new MemoryStream();
            var writer = new BinaryWriter(ms);

            var strBuffer = new StringBufferBuilder();
            strBuffer.AddString(model.Copyright);
            strBuffer.AddString(model.Generator);
            foreach (var m in model.Materials.Values)
            {
                strBuffer.AddString(m.Name);
                strBuffer.AddString(m.DiffuseTexture?.Name);
                strBuffer.AddString(m.EmissiveTexture?.Name);
                strBuffer.AddString(m.MetallicRoughnessTexture?.Name);
            }
            foreach (var geo in model.Geometries)
            {
                strBuffer.AddString(geo.Name);
                foreach (var g in geo.Groups)
                {
                    strBuffer.AddString(g.Material?.Name);
                }
            }
            foreach (var n in model.Roots)
            {
                GetNodeStrings(strBuffer, n);
            }
            if (model.Images != null) {
                foreach (var kv in model.Images) {
                    strBuffer.AddString(kv.Key);
                    strBuffer.AddString(kv.Value.MimeType);
                }
            }
            
            strBuffer.WriteBuffer(writer);

            strBuffer.WriteStringPos(writer, model.Copyright);
            strBuffer.WriteStringPos(writer, model.Generator);
            writer.Write7BitEncodedInt(model.Materials.Count);
            foreach (var m in model.Materials.Values)
            {
                strBuffer.WriteStringPos(writer, m.Name);
                WriteTexInfo(writer, strBuffer, m.DiffuseTexture);
                writer.Write(m.DiffuseColor);
                WriteTexInfo(writer, strBuffer, m.EmissiveTexture);
                writer.Write(m.EmissiveColor);
                WriteTexInfo(writer, strBuffer, m.NormalTexture);
                writer.Write(m.MetallicRoughness ? (byte)1 : (byte)0);
                writer.Write(m.MetallicFactor);
                writer.Write(m.RoughnessFactor);
                WriteTexInfo(writer, strBuffer, m.MetallicRoughnessTexture);
            }
            writer.Write7BitEncodedInt(model.Geometries.Length);
            foreach (var g in model.Geometries)
            {
                WriteGeometry(g, writer, strBuffer);
            }
            writer.Write7BitEncodedInt(model.Roots.Length);
            foreach (var n in model.Roots)
            {
                WriteNode(n, model.Geometries, writer, strBuffer);
            }
            if (model.Images != null) {
                writer.Write7BitEncodedInt(1 + model.Images.Count);
                foreach (var kv in model.Images) {
                    strBuffer.WriteStringPos(writer, kv.Key);
                    strBuffer.WriteStringPos(writer, kv.Value.MimeType);
                    writer.Write7BitEncodedInt(kv.Value.Data.Length);
                    writer.Write(kv.Value.Data);
                }
            }
            else
            {
                writer.Write7BitEncodedInt(0);
            }

            if (model.Animations != null)
            {
                writer.Write7BitEncodedInt(1 + model.Animations.Length);
                foreach (var anim in model.Animations)
                {
                    strBuffer.WriteStringPos(writer, anim.Name);
                    writer.Write7BitEncodedInt(anim.Rotations.Length);
                    foreach (var rot in anim.Rotations) {
                        strBuffer.WriteStringPos(writer, rot.Target);
                        writer.Write7BitEncodedInt(rot.Keyframes.Length);
                        foreach (var kf in rot.Keyframes) {
                            writer.Write(kf.Time);
                            writer.Write(kf.Rotation.X);
                            writer.Write(kf.Rotation.Y);
                            writer.Write(kf.Rotation.Z);
                            writer.Write(kf.Rotation.W);
                        }
                    }
                    writer.Write7BitEncodedInt(anim.Translations.Length);
                    foreach (var tr in anim.Translations) {
                        strBuffer.WriteStringPos(writer, tr.Target);
                        writer.Write7BitEncodedInt(tr.Keyframes.Length);
                        foreach (var kf in tr.Keyframes) {
                            writer.Write(kf.Time);
                            writer.Write(kf.Translation.X);
                            writer.Write(kf.Translation.Y);
                            writer.Write(kf.Translation.Z);
                        }
                    }
                }
            }
            else
            {
                writer.Write7BitEncodedInt(0);
            }

            ms.Position = 0;
            ms.CopyTo(comp);
        }

        static void WriteTexInfo(BinaryWriter writer, StringBufferBuilder strBuffer, TextureInfo tex)
        {
            if (tex?.Name == null)
            {
                strBuffer.WriteStringPos(writer, null);
            }
            else
            {
                strBuffer.WriteStringPos(writer, tex.Name);
                writer.Write((byte)tex.CoordinateIndex);
            }
        }

        static void WriteProperty(PropertyValue prop, StringBufferBuilder strBuffer, BinaryWriter writer)
        {
            switch (prop.Value)
            {
                case string s:
                    writer.Write((byte)PropertyKind.String);
                    strBuffer.WriteStringPos(writer, s);
                    break;
                case int i:
                    writer.Write((byte)PropertyKind.Int);
                    writer.Write(i);
                    break;
                case float f:
                    writer.Write((byte)PropertyKind.Float);
                    writer.Write(f);
                    break;
                case bool b:
                    writer.Write((byte)PropertyKind.Boolean | (b ? 0x80: 0));
                    break;
                case int[] ia:
                    writer.Write((byte) PropertyKind.IntArray);
                    writer.Write7BitEncodedInt(ia.Length);
                    foreach(var i in ia) writer.Write(i);
                    break;
                case float[] fa:
                    writer.Write((byte)PropertyKind.FloatArray);
                    writer.Write7BitEncodedInt(fa.Length);
                    foreach(var f in fa) writer.Write(f);
                    break;
                case Vector3 v3:
                    writer.Write((byte)PropertyKind.Vector3);
                    writer.Write(v3);
                    break;
                default:
                    writer.Write((byte)PropertyKind.Invalid);
                    break;
            }
        }

        static void WriteNode(ModelNode n, Geometry[] geometries, BinaryWriter writer, StringBufferBuilder strBuffer)
        {
            strBuffer.WriteStringPos(writer, n.Name);
            writer.Write7BitEncodedInt(n.Properties.Count);
            foreach (var kv in n.Properties) {
                strBuffer.WriteStringPos(writer, kv.Key);
                WriteProperty(kv.Value, strBuffer, writer);
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
            foreach (var child in n.Children)
            {
                WriteNode(child, geometries, writer, strBuffer);
            }
        }

        static void WriteGeometry(Geometry g, BinaryWriter writer, StringBufferBuilder strBuffer)
        {
            strBuffer.WriteStringPos(writer, g.Name);
            writer.Write((byte)g.Kind);
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
                strBuffer.WriteStringPos(writer, tg.Material.Name);
            }
            writer.Write7BitEncodedInt(g.Vertices.Length);
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
            FloatBuffer f = new FloatBuffer(channels, g.Vertices.Length);
            for(int i = 0; i < g.Vertices.Length; i++)
            {
                f.SetFloat(g.Vertices[i].Position.X, 0, i);
                f.SetFloat(g.Vertices[i].Position.Y, 1, i);
                f.SetFloat(g.Vertices[i].Position.Z, 2, i);
                int c = 3;
                if ((g.Attributes & VertexAttributes.Normal) == VertexAttributes.Normal)
                {
                    f.SetFloat(g.Vertices[i].Normal.X, c++, i);
                    f.SetFloat(g.Vertices[i].Normal.Y, c++, i);
                    f.SetFloat(g.Vertices[i].Normal.Z, c++, i);
                }
                if ((g.Attributes & VertexAttributes.Diffuse) == VertexAttributes.Diffuse)
                {
                    f.SetFloat(g.Vertices[i].Diffuse.R, c++, i);
                    f.SetFloat(g.Vertices[i].Diffuse.G, c++, i);
                    f.SetFloat(g.Vertices[i].Diffuse.B, c++, i);
                    f.SetFloat(g.Vertices[i].Diffuse.A, c++, i);
                }
                if ((g.Attributes & VertexAttributes.Tangent) == VertexAttributes.Tangent)
                {
                    f.SetFloat(g.Vertices[i].Tangent.X, c++, i);
                    f.SetFloat(g.Vertices[i].Tangent.Y, c++, i);
                    f.SetFloat(g.Vertices[i].Tangent.Z, c++, i);
                    f.SetFloat(g.Vertices[i].Tangent.W, c++, i);
                }
                if ((g.Attributes & VertexAttributes.Texture1) == VertexAttributes.Texture1)
                {
                    f.SetFloat(g.Vertices[i].Texture1.X, c++, i);
                    f.SetFloat(g.Vertices[i].Texture1.Y, c++, i);
                }
                if ((g.Attributes & VertexAttributes.Texture2) == VertexAttributes.Texture2)
                {
                    f.SetFloat(g.Vertices[i].Texture2.X, c++, i);
                    f.SetFloat(g.Vertices[i].Texture2.Y, c++, i);
                }
                if ((g.Attributes & VertexAttributes.Texture3) == VertexAttributes.Texture3)
                {
                    f.SetFloat(g.Vertices[i].Texture3.X, c++, i);
                    f.SetFloat(g.Vertices[i].Texture3.Y, c++, i);
                }
                if ((g.Attributes & VertexAttributes.Texture4) == VertexAttributes.Texture4)
                {
                    f.SetFloat(g.Vertices[i].Texture4.X, c++, i);
                    f.SetFloat(g.Vertices[i].Texture4.Y, c++, i);
                }
            }
            f.Write(writer);
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