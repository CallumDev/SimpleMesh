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
            attributes.Add(("POSITION", ctx.AddVector3(g.Vertices.Select(x => x.Position).ToArray(), true)));
        if ((g.Attributes & VertexAttributes.Normal) != 0)
            attributes.Add(("NORMAL", ctx.AddVector3(g.Vertices.Select(x => x.Normal).ToArray(), false)));
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
            Matrix4x4.Decompose(n.Transform, out _, out var rot, out _);
            if (rot != Quaternion.Identity) json.Add("rotation", new JsonArray(rot.X, rot.Y, rot.Z, rot.W));
            var t = Vector3.Transform(Vector3.Zero, n.Transform);
            if (t != Vector3.Zero) json.Add("translation", new JsonArray(t.X, t.Y, t.Z));
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

        private int CreateBufferView(int start, int length, bool vertex)
        {
            BufferViews.Add(new JsonObject
            {
                {"buffer", 0},
                {"byteLength", length},
                {"byteOffset", start},
                {"target", vertex ? 34962 : 34963}
            });
            return BufferViews.Count - 1;
        }

        public int AddVector3(Vector3[] source, bool position)
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
                {"bufferView", CreateBufferView(byteStart, byteLength, true)},
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
                {"bufferView", CreateBufferView(byteStart, byteLength, true)},
                {"componentType", 5126}, //float
                {"count", source.Length},
                {"type", "VEC2"}
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
                {"bufferView", CreateBufferView(byteStart, byteLength, false)},
                {"componentType", useShort ? 5123 : 5125}, //integer
                {"count", source.Length},
                {"type", "SCALAR"}
            });
            return Accessors.Count - 1;
        }
    }
}