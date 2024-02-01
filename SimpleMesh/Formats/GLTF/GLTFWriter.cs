using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SimpleMesh.Formats.GLTF;

internal static class GLTFWriter
{
    private static JsonNode AssetNode()
    {
        return new JsonObject
        {
            {"generator", "SimpleMesh"},
            {"version", "2.0"}
        };
    }

    private static JsonNode FromMaterial(Material src)
    {
        return new JsonObject
        {
            {"name", src.Name},
            {
                "pbrMetallicRoughness", new JsonObject
                {
                    {
                        "baseColorFactor",
                        new JsonArray {src.DiffuseColor.X, src.DiffuseColor.Y, src.DiffuseColor.Z, src.DiffuseColor.W}
                    },
                    {"metallicFactor", 0f},
                    {"roughnessFactor", 0.5f}
                }
            }
        };
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
        if ((g.Attributes & VertexAttributes.Position) != 0)
            attributes.Add(("POSITION", ctx.AddVector3(g.Vertices.Select(x => x.Position).ToArray(), true, BufferTarget.Vertex)));
        if ((g.Attributes & VertexAttributes.Normal) != 0)
            attributes.Add(("NORMAL", ctx.AddVector3(g.Vertices.Select(x => x.Normal).ToArray(), false, BufferTarget.Vertex)));
        if ((g.Attributes & VertexAttributes.Texture1) != 0)
            attributes.Add(("TEXCOORD_0", ctx.AddVector2(g.Vertices.Select(x => x.Texture1).ToArray())));
        if ((g.Attributes & VertexAttributes.Texture2) != 0)
            attributes.Add(("TEXCOORD_1", ctx.AddVector2(g.Vertices.Select(x => x.Texture2).ToArray())));
        var groups = new List<JsonObject>();
        foreach (var tg in g.Groups)
        {
            var indices = new int[tg.IndexCount];
            for (var i = 0; i < tg.IndexCount; i++)
                if (g.Indices.Indices16 != null)
                    indices[i] = g.Indices.Indices16[tg.StartIndex + i] + tg.BaseVertex;
                else
                    indices[i] = (int) (g.Indices.Indices32[tg.StartIndex + i] + tg.BaseVertex);
            var attrObject = new JsonObject();
            foreach (var o in attributes)
                attrObject.Add(o.name, o.index);
            var prim = new JsonObject
            {
                {"attributes", attrObject},
                {"indices", ctx.AddIndices(indices)},
                {"material", ctx.MaterialIndices[tg.Material]}
            };
            if(g.Kind == GeometryKind.Lines) 
                prim.Add("mode", 1);
            groups.Add(prim);
        }

        ctx.Geometries.Add(new JsonObject
        {
            {"name", name + ".mesh"},
            {"primitives", new JsonArray(groups.ToArray())}
        });
        return ctx.Geometries.Count - 1;
    }

    private static void CreateNode(ModelNode n, GLTFContext ctx)
    {
        var json = new JsonObject
        {
            {"name", n.Name}
        };
        if (n.Transform != Matrix4x4.Identity)
        {
            Matrix4x4.Decompose(n.Transform, out var scale, out var rot, out var translation);
            if (rot != Quaternion.Identity) json.Add("rotation", new JsonArray(rot.X, rot.Y, rot.Z, rot.W));
            if (scale != Vector3.One) json.Add("scale", new JsonArray(scale.X, scale.Y, scale.Z));
            if (translation != Vector3.Zero) json.Add("translation", new JsonArray(translation.X, translation.Y, translation.Z));
        }

        if (n.Geometry != null)
            json.Add("mesh", CreateGeometry(n.Name, n.Geometry, ctx));
        if (n.Properties.Count > 0)
        {
            var props = new JsonObject();
            foreach (var kv in n.Properties)
                props.Add(kv.Key, FromProperty(kv.Value));
            json.Add("extras", props);
        }

        if (n.Children.Count > 0)
        {
            var children = n.Children.Select(x => (JsonNode) ctx.NodeIndices[x]);
            json.Add("children", new JsonArray(children.ToArray()));
        }

        ctx.Nodes[ctx.NodeIndices[n]] = json;
        foreach (var child in n.Children)
            CreateNode(child, ctx);
    }

    static JsonObject CreateAnimationNode(Animation anm, GLTFContext ctx)
    {
        var json = new JsonObject
        {
            {"name", anm.Name},
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
                {"input", times},
                {"interpolation", "LINEAR"},
                {"output", quats},
            });
            channels.Add(new JsonObject
            {
                {"sampler", (samplers.Count - 1)},
                {
                    "target", new JsonObject
                    {
                        {"node", mn.Value},
                        {"path", "rotation"}
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
                {"input", times},
                {"interpolation", "LINEAR"},
                {"output", vecs},
            });
            channels.Add(new JsonObject
            {
                {"sampler", (samplers.Count - 1)},
                {
                    "target", new JsonObject
                    {
                        {"node", mn.Value},
                        {"path", "translation"}
                    }
                }
            });
        }


        json.Add("samplers", new JsonArray(samplers.ToArray()));
        json.Add("channels", new JsonArray(channels.ToArray()));
        return json;
    }

    public static void Write(Model model, Stream outStream)
    {
        var json = new JsonObject();
        using var bufferStream = new MemoryStream();
        var bufferWriter = new BinaryWriter(bufferStream);

        var ctx = new GLTFContext {Json = json, BufferWriter = bufferWriter};
        foreach (var r in model.Roots) WalkNode(r, ctx.NodeIndices);
        var jsonMats = new List<JsonNode>();
        foreach (var m in model.Materials.Values)
        {
            ctx.MaterialIndices[m] = jsonMats.Count;
            jsonMats.Add(FromMaterial(m));
        }

        ctx.Nodes = new JsonObject[ctx.NodeIndices.Count];
        json.Add("asset", AssetNode());
        json.Add("scene", 0);
        json.Add("scenes", new JsonArray(new JsonObject
        {
            {"name", "scene"},
            {"nodes", new JsonArray(model.Roots.Select(x => JsonValue.Create(ctx.NodeIndices[x])).ToArray())}
        }));
        foreach (var r in model.Roots)
            CreateNode(r, ctx);
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
        var buffer = bufferStream.ToArray();
        var bufferNode = new JsonObject
        {
            {"byteLength", buffer.Length},
            {"uri", "data:application/octet-stream;base64," + Convert.ToBase64String(buffer)}
        };
        json.Add("buffers", new JsonArray(bufferNode));
        using var jsonWriter = new Utf8JsonWriter(outStream, new JsonWriterOptions {Indented = true});
        json.WriteTo(jsonWriter);
    }

    enum BufferTarget
    {
        Vertex,
        Index,
        Animation
    }
    
    private class GLTFContext
    {
        public readonly List<JsonObject> Accessors = new();
        public readonly List<JsonObject> BufferViews = new();
        public BinaryWriter BufferWriter;
        public readonly List<JsonObject> Geometries = new();
        public JsonObject Json;
        public readonly Dictionary<Material, int> MaterialIndices = new();
        public readonly Dictionary<ModelNode, int> NodeIndices = new();
        public JsonObject[] Nodes;
        

        private int CreateBufferView(int start, int length, BufferTarget target)
        {
            var obj = new JsonObject
            {
                { "buffer", 0 },
                { "byteLength", length },
                { "byteOffset", start },
            };
            switch (target) {
                case BufferTarget.Vertex:
                    obj.Add("target", 34962);
                    break;
                case BufferTarget.Index:
                    obj.Add("target", 34963);
                    break;
                case BufferTarget.Animation:
                    //No Target
                    break;
                default:
                    throw new ArgumentException();
            }
            BufferViews.Add(obj);
            return BufferViews.Count - 1;
        }

        public int AddVector3(Vector3[] source, bool position, BufferTarget target)
        {
            var byteStart = (int) BufferWriter.BaseStream.Position;
            var byteLength = source.Length * 12;
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            foreach (var v in source)
            {
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
                BufferWriter.Write(v.X);
                BufferWriter.Write(v.Y);
                BufferWriter.Write(v.Z);
            }

            var access = new JsonObject
            {
                {"bufferView", CreateBufferView(byteStart, byteLength, target)},
                {"componentType", 5126}, //float
                {"count", source.Length},
                {"type", "VEC3"}
            };
            if (position)
            {
                access.Add("max", new JsonArray(max.X, max.Y, max.Z));
                access.Add("min", new JsonArray(min.X, min.Y, min.Z));
            }

            Accessors.Add(access);
            return Accessors.Count - 1;
        }

        public int AddVector2(Vector2[] source)
        {
            var byteStart = (int) BufferWriter.BaseStream.Position;
            var byteLength = source.Length * 8;
            foreach (var v in source)
            {
                BufferWriter.Write(v.X);
                BufferWriter.Write(v.Y);
            }

            Accessors.Add(new JsonObject
            {
                {"bufferView", CreateBufferView(byteStart, byteLength, BufferTarget.Vertex)},
                {"componentType", 5126}, //float
                {"count", source.Length},
                {"type", "VEC2"}
            });
            return Accessors.Count - 1;
        }

        public int AddQuaternions(Quaternion[] source)
        {
            var byteStart = (int) BufferWriter.BaseStream.Position;
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
                {"bufferView", CreateBufferView(byteStart, byteLength, BufferTarget.Animation)},
                {"componentType", 5126}, //float
                {"count", source.Length},
                {"type", "VEC4"}
            });
            return Accessors.Count - 1;
        }
        public int AddFloats(float[] source)
        {
            var byteStart = (int) BufferWriter.BaseStream.Position;
            var byteLength = source.Length * 4;
            foreach (var f in source)
                BufferWriter.Write(f);
            var min = source.Min();
            var max = source.Max();
            Accessors.Add(new JsonObject
            {
                {"bufferView", CreateBufferView(byteStart, byteLength, BufferTarget.Animation)},
                {"componentType", 5126}, //float
                {"count", source.Length},
                {"type", "SCALAR"},
                {"min", new JsonArray(min)},
                {"max", new JsonArray(max)}
            });
            return Accessors.Count - 1;
        }

        public int AddIndices(int[] source)
        {
            var useShort = source.All(x => x <= ushort.MaxValue);
            var byteStart = (int) BufferWriter.BaseStream.Position;
            var byteLength = source.Length * (useShort ? 2 : 4);
            foreach (var i in source)
                if (useShort) BufferWriter.Write((ushort) i);
                else BufferWriter.Write(i);
            if (useShort && byteLength % 4 != 0)
                BufferWriter.Write((ushort) 0);
            Accessors.Add(new JsonObject
            {
                {"bufferView", CreateBufferView(byteStart, byteLength, BufferTarget.Index)},
                {"componentType", useShort ? 5123 : 5125}, //integer
                {"count", source.Length},
                {"type", "SCALAR"}
            });
            return Accessors.Count - 1;
        }
    }
}