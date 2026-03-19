using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
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

        static void TraverseNodeTree(StringBufferBuilder builder, Dictionary<ModelNode, int> indices, ModelNode node)
        {
            var pos = indices.Count;
            indices[node] = pos;
            builder.AddString(node.Name);
            foreach (var kv in node.Properties) {
                builder.AddString(kv.Key);
                if(kv.Value.Value is string s)
                    builder.AddString(s);
            }
            foreach (var c in node.Children)
            {
                TraverseNodeTree(builder, indices, c);
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

            Dictionary<ModelNode, int> nodeIndices = new();
            
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
            foreach (var sk in model.Skins)
                strBuffer.AddString(sk.Name);
            foreach (var n in model.Roots)
            {
                TraverseNodeTree(strBuffer, nodeIndices, n);
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
            writer.Write7BitEncodedInt(model.Skins.Length);
            foreach (var s in model.Skins)
            {
                strBuffer.WriteStringPos(writer, s.Name);
                writer.Write7BitEncodedInt(s.Bones.Length);
                // matrices
                if (s.InverseBindMatrices.All(x => x == Matrix4x4.Identity))
                    writer.Write((byte)0);
                else
                {
                    writer.Write((byte)1);
                    var f = new FloatBuffer(16, s.Bones.Length);
                    for (int i = 0; i < s.InverseBindMatrices.Length; i++)
                    {
                        f[0, i] = s.InverseBindMatrices[i].M11;
                        f[1, i] = s.InverseBindMatrices[i].M12;
                        f[2, i] = s.InverseBindMatrices[i].M13;
                        f[3, i] = s.InverseBindMatrices[i].M14;

                        f[4, i] = s.InverseBindMatrices[i].M21;
                        f[5, i] = s.InverseBindMatrices[i].M22;
                        f[6, i] = s.InverseBindMatrices[i].M23;
                        f[7, i] = s.InverseBindMatrices[i].M24;

                        f[8, i] = s.InverseBindMatrices[i].M31;
                        f[9, i] = s.InverseBindMatrices[i].M32;
                        f[10, i] = s.InverseBindMatrices[i].M33;
                        f[11, i] = s.InverseBindMatrices[i].M34;

                        f[12, i] = s.InverseBindMatrices[i].M41;
                        f[13, i] = s.InverseBindMatrices[i].M42;
                        f[14, i] = s.InverseBindMatrices[i].M43;
                        f[15, i] = s.InverseBindMatrices[i].M44;
                    }
                    f.Write(writer);
                }
                // references
                if(s.Root == null)
                    writer.Write7BitEncodedInt(0);
                else
                    writer.Write7BitEncodedInt(nodeIndices[s.Root] + 1);
                foreach(var b in s.Bones)
                    writer.Write7BitEncodedInt(nodeIndices[b]);
            }
            writer.Write7BitEncodedInt(model.Roots.Length);
            foreach (var n in model.Roots)
            {
                WriteNode(n, model.Geometries, model.Skins, writer, strBuffer);
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

        static void WriteNode(ModelNode n, Geometry[] geometries, Skin[] skins, BinaryWriter writer, StringBufferBuilder strBuffer)
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
                writer.Write7BitEncodedInt(0);
            } 
            else
            {
                var idx = Array.IndexOf(geometries, n.Geometry);
                if (idx == -1)
                {
                    throw new Exception("All ModelNode geometries must be present in model geometry array");
                }
                else {
                    writer.Write7BitEncodedInt(idx + 1);
                }
            }

            if (n.Skin == null)
            {
                writer.Write7BitEncodedInt(0);
            }
            else
            {
                var idx = Array.IndexOf(skins, n.Skin);
                if (idx == -1)
                {
                    throw new Exception("All ModelNode skins must be present in model skins array");
                }
                else {
                    writer.Write7BitEncodedInt(idx + 1);
                }
            }
            
            writer.Write7BitEncodedInt(n.Children.Count);
            foreach (var child in n.Children)
            {
                WriteNode(child, geometries, skins, writer, strBuffer);
            }
        }

        static void WriteGeometry(Geometry g, BinaryWriter writer, StringBufferBuilder strBuffer)
        {
            strBuffer.WriteStringPos(writer, g.Name);
            writer.Write((byte)g.Kind);
            writer.Write((ushort)g.Vertices.Descriptor.Attributes);
            writer.Write(g.Center);
            writer.Write(g.Radius);
            writer.Write(g.Min);
            writer.Write(g.Max);
            writer.Write7BitEncodedInt(g.Groups.Length);
            foreach (var tg in g.Groups)
            {
                writer.Write7BitEncodedInt(tg.BaseVertex);
                writer.Write7BitEncodedInt(tg.StartIndex);
                writer.Write7BitEncodedInt(tg.IndexCount);
                strBuffer.WriteStringPos(writer, tg.Material.Name);
            }
            writer.Write7BitEncodedInt(g.Vertices.Count);
            int channels = 3;
            if ((g.Vertices.Descriptor.Attributes & VertexAttributes.Normal) == VertexAttributes.Normal)
                channels += 3;
            if ((g.Vertices.Descriptor.Attributes & VertexAttributes.Diffuse) == VertexAttributes.Diffuse)
                channels += 4;
            if ((g.Vertices.Descriptor.Attributes & VertexAttributes.Tangent) == VertexAttributes.Tangent)
                channels += 4;
            if ((g.Vertices.Descriptor.Attributes & VertexAttributes.Texture1) == VertexAttributes.Texture1)
                channels += 2;
            if ((g.Vertices.Descriptor.Attributes & VertexAttributes.Texture2) == VertexAttributes.Texture2)
                channels += 2;
            if ((g.Vertices.Descriptor.Attributes & VertexAttributes.Texture3) == VertexAttributes.Texture3)
                channels += 2;
            if ((g.Vertices.Descriptor.Attributes & VertexAttributes.Texture4) == VertexAttributes.Texture4)
                channels += 2;
            if ((g.Vertices.Descriptor.Attributes & VertexAttributes.Joints) == VertexAttributes.Joints)
                channels += 4;
            FloatBuffer f = new FloatBuffer(channels, g.Vertices.Count);
            for(int i = 0; i < g.Vertices.Count; i++)
            {
                f.SetFloat(g.Vertices.Position[i].X, 0, i);
                f.SetFloat(g.Vertices.Position[i].Y, 1, i);
                f.SetFloat(g.Vertices.Position[i].Z, 2, i);
                int c = 3;
                if ((g.Vertices.Descriptor.Attributes & VertexAttributes.Normal) == VertexAttributes.Normal)
                {
                    f.SetFloat(g.Vertices.Normal[i].X, c++, i);
                    f.SetFloat(g.Vertices.Normal[i].Y, c++, i);
                    f.SetFloat(g.Vertices.Normal[i].Z, c++, i);
                }
                if ((g.Vertices.Descriptor.Attributes & VertexAttributes.Diffuse) == VertexAttributes.Diffuse)
                {
                    f.SetFloat(g.Vertices.Diffuse[i].R, c++, i);
                    f.SetFloat(g.Vertices.Diffuse[i].G, c++, i);
                    f.SetFloat(g.Vertices.Diffuse[i].B, c++, i);
                    f.SetFloat(g.Vertices.Diffuse[i].A, c++, i);
                }
                if ((g.Vertices.Descriptor.Attributes & VertexAttributes.Tangent) == VertexAttributes.Tangent)
                {
                    f.SetFloat(g.Vertices.Tangent[i].X, c++, i);
                    f.SetFloat(g.Vertices.Tangent[i].Y, c++, i);
                    f.SetFloat(g.Vertices.Tangent[i].Z, c++, i);
                    f.SetFloat(g.Vertices.Tangent[i].W, c++, i);
                }
                if ((g.Vertices.Descriptor.Attributes & VertexAttributes.Texture1) == VertexAttributes.Texture1)
                {
                    f.SetFloat(g.Vertices.Texture1[i].X, c++, i);
                    f.SetFloat(g.Vertices.Texture1[i].Y, c++, i);
                }
                if ((g.Vertices.Descriptor.Attributes & VertexAttributes.Texture2) == VertexAttributes.Texture2)
                {
                    f.SetFloat(g.Vertices.Texture2[i].X, c++, i);
                    f.SetFloat(g.Vertices.Texture2[i].Y, c++, i);
                }
                if ((g.Vertices.Descriptor.Attributes & VertexAttributes.Texture3) == VertexAttributes.Texture3)
                {
                    f.SetFloat(g.Vertices.Texture3[i].X, c++, i);
                    f.SetFloat(g.Vertices.Texture3[i].Y, c++, i);
                }
                if ((g.Vertices.Descriptor.Attributes & VertexAttributes.Texture4) == VertexAttributes.Texture4)
                {
                    f.SetFloat(g.Vertices.Texture4[i].X, c++, i);
                    f.SetFloat(g.Vertices.Texture4[i].Y, c++, i);
                }
                if ((g.Vertices.Descriptor.Attributes & VertexAttributes.Joints) == VertexAttributes.Joints)
                {
                    f.SetFloat(g.Vertices.JointWeights[i].X, c++, i);
                    f.SetFloat(g.Vertices.JointWeights[i].Y, c++, i);
                    f.SetFloat(g.Vertices.JointWeights[i].Z, c++, i);
                    f.SetFloat(g.Vertices.JointWeights[i].W, c++, i);
                }
            }
            f.Write(writer);
            if ((g.Vertices.Descriptor.Attributes & VertexAttributes.Joints) == VertexAttributes.Joints)
            {
                for (int i = 0; i < g.Vertices.Count; i++)
                {
                    var idx = g.Vertices.JointIndices[i];
                    writer.Write(idx.A);
                    writer.Write(idx.B);
                    writer.Write(idx.C);
                    writer.Write(idx.D);
                }
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