using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SimpleMesh.Formats.GLTF;

internal static class GLTFWriter
{
    private static JsonNode AssetNode(string? copyright, string? generator)
    {
        var node = new JsonObject
        {
            { "version", "2.0" }
        };
        if (copyright != null)
            node.Add("copyright", copyright);
        if (generator != null)
            node.Add("generator", generator);
        return node;
    }

    private static JsonNode FromMaterial(Material src, Dictionary<string, int> textureMap)
    {
        bool GetTextureJson(TextureInfo? info, [NotNullWhen(true)] out JsonObject? obj)
        {
            obj = null;
            if (info == null || string.IsNullOrEmpty(info.Name) ||
                !textureMap.TryGetValue(info.Name, out var index))
                return false;
            if (info.CoordinateIndex != 0)
                obj = new JsonObject { { "index", index }, { "texCoord", info.CoordinateIndex } };
            else
                obj = new JsonObject { { "index", index } };
            return true;
        }

        var pbrMetallicRoughness = new JsonObject
        {
            {
                "baseColorFactor",
                new JsonArray { src.DiffuseColor.R, src.DiffuseColor.G, src.DiffuseColor.B, src.DiffuseColor.A }
            },
        };
        if (GetTextureJson(src.DiffuseTexture, out var baseColorTexture))
            pbrMetallicRoughness.Add("baseColorTexture", baseColorTexture);
        if (src.MetallicRoughness)
        {
            pbrMetallicRoughness.Add("metallicFactor", src.MetallicFactor);
            pbrMetallicRoughness.Add("roughnessFactor", src.RoughnessFactor);
            if (GetTextureJson(src.MetallicRoughnessTexture, out var metallicRoughnessTexture))
                pbrMetallicRoughness.Add("metallicRoughnessTexture", metallicRoughnessTexture);
        }

        var mat = new JsonObject
        {
            { "name", src.Name },
            {
                "pbrMetallicRoughness", pbrMetallicRoughness
            }
        };
        if (src.EmissiveColor != Vector3.Zero)
            mat.Add("emissiveFactor",
                new JsonArray() { src.EmissiveColor.X, src.EmissiveColor.Y, src.EmissiveColor.Z });
        if (GetTextureJson(src.EmissiveTexture, out var emissiveTexture))
            mat.Add("emissiveTexture", emissiveTexture);
        return mat;
    }

    private static JsonNode FromProperty(PropertyValue pv)
    {
        switch (pv.Value)
        {
            case bool b:
                return JsonValue.Create(b);
            case float f:
                return JsonValue.Create(f);
            case int i:
                return JsonValue.Create(i);
            case int[] ia:
                return new JsonArray(ia.Select(x => JsonValue.Create(x)).ToArray());
            case float[] fa:
                return new JsonArray(fa.Select(x => JsonValue.Create(x)).ToArray());
            case Vector3 v3:
                return new JsonArray(JsonValue.Create(v3.X), JsonValue.Create(v3.Y), JsonValue.Create(v3.Z));
            case string s:
                return JsonValue.Create(s);
            default:
                return JsonValue.Create(false);
        }
    }

    private static void WalkNode(ModelNode n, Dictionary<ModelNode, int> index)
    {
        index[n] = index.Count;
        foreach (var c in n.Children)
            WalkNode(c, index);
    }

    private static int CreateGeometry(string name, Geometry g, GLTFContext ctx)
    {
        var attributes = new List<(string name, int index)>();
        if (g.Has(VertexAttributes.Position))
            attributes.Add(("POSITION", ctx.AddVector3(g.Vertices.Position, true, BufferTarget.Vertex)));
        if (g.Has(VertexAttributes.Normal))
            attributes.Add(("NORMAL", ctx.AddVector3(g.Vertices.Normal, false, BufferTarget.Vertex)));
        if (g.Has(VertexAttributes.Diffuse))
            attributes.Add(("COLOR_0", ctx.AddLinearColor(g.Vertices.Diffuse)));
        if (g.Has(VertexAttributes.Texture1))
            attributes.Add(("TEXCOORD_0", ctx.AddVector2(g.Vertices.Texture1)));
        if (g.Has(VertexAttributes.Texture2))
            attributes.Add(("TEXCOORD_1", ctx.AddVector2(g.Vertices.Texture2)));
        if (g.Has(VertexAttributes.Texture3))
            attributes.Add(("TEXCOORD_2", ctx.AddVector2(g.Vertices.Texture3)));
        if (g.Has(VertexAttributes.Texture4))
            attributes.Add(("TEXCOORD_3", ctx.AddVector2(g.Vertices.Texture4)));
        if (g.Has(VertexAttributes.Joints))
        {
            attributes.Add(("JOINTS_0", ctx.AddJoints(g.Vertices.JointIndices)));
            attributes.Add(("WEIGHTS_0", ctx.AddVector4(g.Vertices.JointWeights)));
        }
        var groups = new List<JsonObject>();
        foreach (var tg in g.Groups)
        {
            var indices = new uint[tg.IndexCount];
            for (var i = 0; i < tg.IndexCount; i++)
                indices[i] = (uint)(g.Indices[tg.StartIndex + i] + tg.BaseVertex);
            var attrObject = new JsonObject();
            foreach (var o in attributes)
                attrObject.Add(o.name, o.index);
            var prim = new JsonObject
            {
                { "attributes", attrObject },
                { "indices", ctx.AddIndices(indices) },
                { "material", ctx.MaterialIndices[tg.Material] }
            };
            if (g.Kind == GeometryKind.Lines)
                prim.Add("mode", 1);
            groups.Add(prim);
        }

        ctx.Geometries.Add(new JsonObject
        {
            { "name", name + ".mesh" },
            { "primitives", new JsonArray(groups.ToArray()) }
        });
        return ctx.Geometries.Count - 1;
    }

    private static void CreateNode(ModelNode n, Model m, GLTFContext ctx)
    {
        var json = new JsonObject
        {
            { "name", n.Name }
        };
        if (n.Transform != Matrix4x4.Identity)
        {
            Matrix4x4.Decompose(n.Transform, out var scale, out var rot, out var translation);
            if (rot != Quaternion.Identity) json.Add("rotation", new JsonArray(rot.X, rot.Y, rot.Z, rot.W));
            if (scale != Vector3.One) json.Add("scale", new JsonArray(scale.X, scale.Y, scale.Z));
            if (translation != Vector3.Zero)
                json.Add("translation", new JsonArray(translation.X, translation.Y, translation.Z));
        }

        if (n.Geometry != null)
            json.Add("mesh", CreateGeometry(n.Name, n.Geometry, ctx));
        if (n.Skin != null)
        {
            var idx = Array.IndexOf(m.Skins, n.Skin);
            if (idx == -1)
                throw new Exception("Skin not present in Skins[] array");
            json.Add("skin", idx);
        }
        if (n.Properties.Count > 0)
        {
            var props = new JsonObject();
            foreach (var kv in n.Properties)
                props.Add(kv.Key, FromProperty(kv.Value));
            json.Add("extras", props);
        }

        if (n.Children.Count > 0)
        {
            var children = n.Children.Select(x => (JsonNode)ctx.NodeIndices[x]);
            json.Add("children", new JsonArray(children.ToArray()));
        }

        ctx.Nodes[ctx.NodeIndices[n]] = json;
        foreach (var child in n.Children)
            CreateNode(child, m, ctx);
    }


    static JsonArray CreateSkins(Skin[] skins, GLTFContext ctx)
    {
        var nodes = new List<JsonNode>();
        foreach (var skin in skins)
        {
            var s = new JsonObject()
            {
                { "name", skin.Name }
            };
            if(skin.Root != null)
                s.Add("skeleton", ctx.NodeIndices[skin.Root]);
            var joints = skin.Bones.Select(x => (JsonNode)ctx.NodeIndices[x]);
            s.Add("joints", new JsonArray(joints.ToArray()));
            if (skin.InverseBindMatrices.Any(x => x != Matrix4x4.Identity))
            {
                s.Add("inverseBindMatrices", ctx.AddMatrices(skin.InverseBindMatrices));
            }
            nodes.Add(s);
        }
        return new JsonArray(nodes.ToArray());
    }

    static (JsonArray images, JsonArray textures, Dictionary<string, int> index) CreateImages(
        Dictionary<string, ImageData> images, GLTFContext ctx)
    {
        var imgs = new List<JsonNode>();
        var texs = new List<JsonNode>();
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int idx = 0;
        foreach (var kv in images)
        {
            var image = new JsonObject()
            {
                { "name", kv.Key },
                { "mimeType", kv.Value.MimeType ?? "image/png" },
                { "bufferView", ctx.AddImageView(kv.Value.Data) }
            };
            imgs.Add(image);
            var tex = new JsonObject()
            {
                { "source", idx },
            };
            texs.Add(tex);
            index[kv.Key] = idx;
            idx++;
        }

        return (new JsonArray(imgs.ToArray()), new JsonArray(texs.ToArray()), index);
    }


    static JsonObject CreateAnimationNode(Animation anm, GLTFContext ctx)
    {
        var json = new JsonObject
        {
            { "name", anm.Name },
        };
        List<JsonNode> samplers = new List<JsonNode>();
        List<JsonNode> channels = new List<JsonNode>();
        foreach (var ch in anm.Rotations)
        {
            var times = ctx.AddFloats(ch.Keyframes.Select(x => x.Time).ToArray());
            var quats = ctx.AddQuaternions(ch.Keyframes.Select(x => x.Rotation).ToArray());
            var mn = ctx.NodeIndices.FirstOrDefault(x => x.Key.Name == ch.Target);
            if (mn.Key == null) continue;
            samplers.Add(new JsonObject
            {
                { "input", times },
                { "interpolation", "LINEAR" },
                { "output", quats },
            });
            channels.Add(new JsonObject
            {
                { "sampler", (samplers.Count - 1) },
                {
                    "target", new JsonObject
                    {
                        { "node", mn.Value },
                        { "path", "rotation" }
                    }
                }
            });
        }

        foreach (var ch in anm.Translations)
        {
            var times = ctx.AddFloats(ch.Keyframes.Select(x => x.Time).ToArray());
            var vecs = ctx.AddVector3(ch.Keyframes.Select(x => x.Translation).ToArray(), true, BufferTarget.Animation);
            var mn = ctx.NodeIndices.FirstOrDefault(x => x.Key.Name == ch.Target);
            if (mn.Key == null) continue;
            samplers.Add(new JsonObject
            {
                { "input", times },
                { "interpolation", "LINEAR" },
                { "output", vecs },
            });
            channels.Add(new JsonObject
            {
                { "sampler", (samplers.Count - 1) },
                {
                    "target", new JsonObject
                    {
                        { "node", mn.Value },
                        { "path", "translation" }
                    }
                }
            });
        }
        json.Add("samplers", new JsonArray(samplers.ToArray()));
        json.Add("channels", new JsonArray(channels.ToArray()));
        return json;
    }

    public static void Write(Model model, Stream outStream, bool isGLB)
    {
        var json = new JsonObject();
        using var bufferStream = new MemoryStream();
        var bufferWriter = new BinaryWriter(bufferStream);

        var nodeIndices = new Dictionary<ModelNode, int>();
        foreach (var r in model.Roots) WalkNode(r, nodeIndices);
        var allNodes = new JsonObject[nodeIndices.Count];

        var ctx = new GLTFContext(bufferWriter, allNodes, nodeIndices);

        json.Add("asset", AssetNode(model.Copyright, model.Generator));


        Dictionary<string, int> textureMap = new Dictionary<string, int>();
        if (model.Images.Count > 0)
        {
            var (images, textures, index) = CreateImages(model.Images, ctx);
            json.Add("images", images);
            json.Add("textures", textures);
            textureMap = index;
        }

        if (model.Skins.Length > 0)
        {
            json.Add("skins", CreateSkins(model.Skins, ctx));
        }

        var jsonMats = new List<JsonNode>();
        foreach (var m in model.Materials.Values)
        {
            ctx.MaterialIndices[m] = jsonMats.Count;
            jsonMats.Add(FromMaterial(m, textureMap));
        }

        json.Add("scene", 0);
        json.Add("scenes", new JsonArray(new JsonObject
        {
            { "name", "scene" },
            { "nodes", new JsonArray(model.Roots.Select(x => (JsonNode)JsonValue.Create(ctx.NodeIndices[x])).ToArray()) }
        }));
        foreach (var r in model.Roots)
            CreateNode(r, model, ctx);
        json.Add("nodes", new JsonArray(ctx.Nodes));
        json.Add("materials", new JsonArray(jsonMats.ToArray()));
        json.Add("meshes", new JsonArray(ctx.Geometries.ToArray()));
        if (model.Animations != null)
        {
            var anms = new JsonArray(model.Animations.Select(x => CreateAnimationNode(x, ctx)).ToArray());
            json.Add("animations", anms);
        }

        json.Add("accessors", new JsonArray(ctx.Accessors.ToArray()));
        json.Add("bufferViews", new JsonArray(ctx.BufferViews.ToArray()));
        if (isGLB)
        {
            //Prepare data
            ctx.PadToFour();
            var buffer = bufferStream.ToArray();
            var bufferNode = new JsonObject
            {
                { "byteLength", buffer.Length },
            };
            json.Add("buffers", new JsonArray(bufferNode));
            var jsonStream = new MemoryStream();
            var jsonWriter = new Utf8JsonWriter(jsonStream);
            json.WriteTo(jsonWriter);
            jsonWriter.Flush();
            while ((jsonStream.Position % 4) != 0) //Pad with space
                jsonStream.WriteByte(0x20);

            var jsonBuffer = jsonStream.ToArray();
            using var glbWriter = new BinaryWriter(outStream);
            glbWriter.Write(GLBLoader.GLTF_MAGIC);
            glbWriter.Write(2U);
            //header + chunk0 header + chunk1 header + chunk0 + chunk1
            glbWriter.Write(12U + 8U + 8U + (uint)jsonBuffer.Length + (uint)buffer.LongLength);
            glbWriter.Write((uint)jsonBuffer.Length);
            glbWriter.Write(GLBLoader.CHUNK_JSON);
            glbWriter.Write(jsonBuffer);
            glbWriter.Write((uint)buffer.LongLength);
            glbWriter.Write(GLBLoader.CHUNK_BIN);
            glbWriter.Write(buffer);
        }
        else
        {
            var buffer = bufferStream.ToArray();
            var bufferNode = new JsonObject
            {
                { "byteLength", buffer.Length },
                { "uri", "data:application/octet-stream;base64," + Convert.ToBase64String(buffer) }
            };
            json.Add("buffers", new JsonArray(bufferNode));
            using var jsonWriter = new Utf8JsonWriter(outStream, new JsonWriterOptions { Indented = true });
            json.WriteTo(jsonWriter);
        }
    }

    enum BufferTarget
    {
        Vertex,
        Index,
        Animation,
        Texture
    }

    private class GLTFContext(BinaryWriter bufferWriter, JsonObject[] nodes, Dictionary<ModelNode, int> nodeIndices)
    {
        public readonly List<JsonObject> Accessors = new();
        public readonly List<JsonObject> BufferViews = new();
        public BinaryWriter BufferWriter = bufferWriter;
        public readonly List<JsonObject> Geometries = new();
        public readonly Dictionary<Material, int> MaterialIndices = new();
        public readonly Dictionary<ModelNode, int> NodeIndices = nodeIndices;
        public JsonObject[] Nodes = nodes;


        private int CreateBufferView(int start, int length, BufferTarget target)
        {
            var obj = new JsonObject
            {
                { "buffer", 0 },
                { "byteLength", length },
                { "byteOffset", start },
            };
            switch (target)
            {
                case BufferTarget.Vertex:
                    obj.Add("target", 34962);
                    break;
                case BufferTarget.Index:
                    obj.Add("target", 34963);
                    break;
                case BufferTarget.Animation:
                case BufferTarget.Texture:
                    //No Target
                    break;
                default:
                    throw new ArgumentException();
            }

            BufferViews.Add(obj);
            return BufferViews.Count - 1;
        }

        public int AddImageView(ReadOnlySpan<byte> data)
        {
            var byteStart = (int)BufferWriter.BaseStream.Position;
            var byteLength = data.Length;
            BufferWriter.Write(data);
            //Satisfy alignment
            PadToFour();
            return CreateBufferView(byteStart, byteLength, BufferTarget.Texture);
        }

        public void PadToFour()
        {
            while (BufferWriter.BaseStream.Position % 4 != 0)
                BufferWriter.Write((byte)0);
        }

        public int AddVector3(Vector3[] source, bool position, BufferTarget target)
        {
            var byteStart = (int)BufferWriter.BaseStream.Position;
            var byteLength = source.Length * 12;
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            for (int i = 0; i < source.Length; i++)
            {
                var v = source[i];
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
                BufferWriter.Write(v.X);
                BufferWriter.Write(v.Y);
                BufferWriter.Write(v.Z);
            }

            var access = new JsonObject
            {
                { "bufferView", CreateBufferView(byteStart, byteLength, target) },
                { "componentType", 5126 }, //float
                { "count", source.Length },
                { "type", "VEC3" }
            };
            if (position)
            {
                access.Add("max", new JsonArray(max.X, max.Y, max.Z));
                access.Add("min", new JsonArray(min.X, min.Y, min.Z));
            }

            Accessors.Add(access);
            return Accessors.Count - 1;
        }

        public int AddVector3(VertexArray.Accessor<Vector3> source, bool position, BufferTarget target)
        {
            var byteStart = (int)BufferWriter.BaseStream.Position;
            var byteLength = source.Count * 12;
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            for (int i = 0; i < source.Count; i++)
            {
                var v = source[i];
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
                BufferWriter.Write(v.X);
                BufferWriter.Write(v.Y);
                BufferWriter.Write(v.Z);
            }

            var access = new JsonObject
            {
                { "bufferView", CreateBufferView(byteStart, byteLength, target) },
                { "componentType", 5126 }, //float
                { "count", source.Count },
                { "type", "VEC3" }
            };
            if (position)
            {
                access.Add("max", new JsonArray(max.X, max.Y, max.Z));
                access.Add("min", new JsonArray(min.X, min.Y, min.Z));
            }

            Accessors.Add(access);
            return Accessors.Count - 1;
        }

        public int AddJoints(VertexArray.Accessor<Point4<ushort>> source)
        {
            var byteStart = (int)BufferWriter.BaseStream.Position;
            var byteLength = source.Count * 8;
            for (int i = 0; i < source.Count; i++)
            {
                var v = source[i];
                BufferWriter.Write(v.A);
                BufferWriter.Write(v.B);
                BufferWriter.Write(v.C);
                BufferWriter.Write(v.D);
            }

            var access = new JsonObject
            {
                { "bufferView", CreateBufferView(byteStart, byteLength, BufferTarget.Vertex) },
                { "componentType", 5123 }, //ushort
                { "count", source.Count },
                { "type", "VEC4" }
            };
            PadToFour();

            Accessors.Add(access);
            return Accessors.Count - 1;
        }

        public int AddVector4(VertexArray.Accessor<Vector4> source)
        {
            var byteStart = (int)BufferWriter.BaseStream.Position;
            var byteLength = source.Count * 16;
            for (int i = 0; i < source.Count; i++)
            {
                var v = source[i];

                BufferWriter.Write(v.X);
                BufferWriter.Write(v.Y);
                BufferWriter.Write(v.Z);
                BufferWriter.Write(v.W);
            }

            var access = new JsonObject
            {
                { "bufferView", CreateBufferView(byteStart, byteLength, BufferTarget.Vertex) },
                { "componentType", 5126 }, //float
                { "count", source.Count },
                { "type", "VEC4" }
            };

            Accessors.Add(access);
            return Accessors.Count - 1;
        }

        public int AddLinearColor(VertexArray.Accessor<LinearColor> source)
        {
            var byteStart = (int)BufferWriter.BaseStream.Position;
            bool alpha = false;
            for (int i = 0; i < source.Count; i++)
            {
                // ReSharper disable once CompareOfFloatsByEqualityOperator
                if (source[i].A != 1f)
                {
                    alpha = true;
                    break;
                }
            }

            var byteLength = source.Count * (alpha ? 16 : 12);

            for (int i = 0; i < source.Count; i++)
            {
                var v = source[i];
                BufferWriter.Write(v.R);
                BufferWriter.Write(v.G);
                BufferWriter.Write(v.B);
                if (alpha)
                    BufferWriter.Write(v.A);
            }

            Accessors.Add(new JsonObject
            {
                { "bufferView", CreateBufferView(byteStart, byteLength, BufferTarget.Vertex) },
                { "componentType", 5126 }, //float
                { "count", source.Count },
                { "type", alpha ? "VEC4" : "VEC3" }
            });
            return Accessors.Count - 1;
        }

        public int AddVector2(VertexArray.Accessor<Vector2> source)
        {
            var byteStart = (int)BufferWriter.BaseStream.Position;
            var byteLength = source.Count * 8;
            for (int i = 0; i < source.Count; i++)
            {
                var v = source[i];
                BufferWriter.Write(v.X);
                BufferWriter.Write(v.Y);
            }

            Accessors.Add(new JsonObject
            {
                { "bufferView", CreateBufferView(byteStart, byteLength, BufferTarget.Vertex) },
                { "componentType", 5126 }, //float
                { "count", source.Count },
                { "type", "VEC2" }
            });
            return Accessors.Count - 1;
        }

        public int AddMatrices(Matrix4x4[] source)
        {
            var byteStart = (int)BufferWriter.BaseStream.Position;
            var byteLength = source.Length * 64;
            for (int i = 0; i < source.Length; i++)
            {
                BufferWriter.Write(MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref source[i], 1)));
            }
            Accessors.Add(new JsonObject
            {
                { "bufferView", CreateBufferView(byteStart, byteLength, BufferTarget.Animation) },
                { "componentType", 5126 }, //float
                { "count", source.Length },
                { "type", "MAT4" },
            });
            return Accessors.Count - 1;
        }

        public int AddQuaternions(Quaternion[] source)
        {
            var byteStart = (int)BufferWriter.BaseStream.Position;
            var byteLength = source.Length * 16;
            foreach (var q in source)
            {
                BufferWriter.Write(q.X);
                BufferWriter.Write(q.Y);
                BufferWriter.Write(q.Z);
                BufferWriter.Write(q.W);
            }

            Accessors.Add(new JsonObject
            {
                { "bufferView", CreateBufferView(byteStart, byteLength, BufferTarget.Animation) },
                { "componentType", 5126 }, //float
                { "count", source.Length },
                { "type", "VEC4" }
            });
            return Accessors.Count - 1;
        }

        public int AddFloats(float[] source)
        {
            var byteStart = (int)BufferWriter.BaseStream.Position;
            var byteLength = source.Length * 4;
            foreach (var f in source)
                BufferWriter.Write(f);
            var min = source.Min();
            var max = source.Max();
            Accessors.Add(new JsonObject
            {
                { "bufferView", CreateBufferView(byteStart, byteLength, BufferTarget.Animation) },
                { "componentType", 5126 }, //float
                { "count", source.Length },
                { "type", "SCALAR" },
                { "min", new JsonArray(min) },
                { "max", new JsonArray(max) }
            });
            return Accessors.Count - 1;
        }

        public int AddIndices(uint[] source)
        {
            var useShort = source.All(x => x <= ushort.MaxValue);
            var byteStart = (int)BufferWriter.BaseStream.Position;
            var byteLength = source.Length * (useShort ? 2 : 4);
            foreach (var i in source)
                if (useShort) BufferWriter.Write((ushort)i);
                else BufferWriter.Write(i);
            if (useShort && byteLength % 4 != 0)
                BufferWriter.Write((ushort)0);
            Accessors.Add(new JsonObject
            {
                { "bufferView", CreateBufferView(byteStart, byteLength, BufferTarget.Index) },
                { "componentType", useShort ? 5123 : 5125 }, //integer
                { "count", source.Length },
                { "type", "SCALAR" }
            });
            return Accessors.Count - 1;
        }
    }
}
