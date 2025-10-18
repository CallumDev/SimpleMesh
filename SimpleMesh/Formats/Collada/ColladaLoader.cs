using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Xml.Linq;
using SimpleMesh.Util;

namespace SimpleMesh.Formats.Collada;

static class ColladaLoader
{
    enum UpAxisType
    {
        X_UP,
        Y_UP,
        Z_UP,
    }

    public static Model Load(Stream stream, ModelLoadContext ctx)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(stream);
        }
        catch (Exception e)
        {
            throw new ModelLoadException("XML parse failed", e);
        }

        if (document.Root is not { Name.LocalName: "COLLADA" })
        {
            throw new ModelLoadException("Root node of XML is not COLLADA");
        }

        var root = document.Root!;

        var upAxis = Enum.Parse<UpAxisType>(root.Child("asset")?.Child("up_axis")?.Value ?? "Y_UP", true);

        var defSceneUri = root.Child("scene")?.Child("instance_visual_scene")?.Attribute("url")?.Value;
        if (string.IsNullOrWhiteSpace(defSceneUri))
            throw new ModelLoadException("Could not determine default scene");
        var defSceneId = CheckURI(defSceneUri);
        var defaultScene = root.Child("library_visual_scenes")?.IdLookup("visual_scene", defSceneId);
        if (defaultScene == null)
        {
            throw new ModelLoadException($"Could not find default scene {defSceneId}");
        }

        var mdl = new Model() { Materials = new() };

        var matAccess = new MaterialAccessor(mdl, root.Child("library_materials"), root.Child("library_effects"));
        var geoAccess = new GeometryAccessor(mdl, ctx, upAxis, root.Child("library_geometries"), matAccess);

        var roots = new List<ModelNode>();
        foreach (var n in defaultScene.Elements().Where(x => x.Name.LocalName == "node"))
        {
            roots.Add(ProcessNode(upAxis, n, geoAccess));
        }

        mdl.Roots = roots.ToArray();
        geoAccess.Set();

        return mdl;
    }
    
    static ModelNode ProcessNode(UpAxisType up, XElement n, GeometryAccessor geolib)
    {
        var obj = new ModelNode();
        obj.Name = n.Attribute("name")?.Value ?? "node";
        var gref = n.Child("instance_geometry");
        if (gref != null)
        {
            var id = CheckURI(gref.Attribute("url")!.Value);
            obj.Geometry = geolib.GetGeometry(id);
        }

        var mat = n.SidLookup("matrix", "transform");
        if (mat != null)
        {
            obj.Transform = GetMatrix(up, mat.Value);
        }

        foreach (var node in n.Elements().Where(x => x.Name.LocalName == "node"))
        {
            obj.Children.Add(ProcessNode(up, node, geolib));
        }
        return obj;
    }

    static Matrix4x4 GetMatrix(UpAxisType ax, string text)
    {
        var floats = ParseHelpers.FloatArray(text);
        Matrix4x4 mat;
        if (floats.Length == 16)
            mat = new Matrix4x4(
                floats[0], floats[4], floats[8], floats[12],
                floats[1], floats[5], floats[9], floats[13],
                floats[2], floats[6], floats[10], floats[14],
                floats[3], floats[7], floats[11], floats[15]
            );
        else if (floats.Length == 9)
            mat = new Matrix4x4(
                floats[0], floats[1], floats[2], 0,
                floats[3], floats[4], floats[5], 0,
                floats[6], floats[7], floats[8], 0,
                0, 0, 0, 1
            );
        else
            throw new Exception("Invalid Matrix: " + floats.Length + " elements");
        return Transform.ToYUp((UpAxis)(int)ax, mat);
    }

    class GeometryAccessor(Model model, ModelLoadContext ctx, UpAxisType up, XElement geolib, MaterialAccessor matlib)
    {
        private Dictionary<string, Geometry> geometries = new();
        public Geometry GetGeometry(string id)
        {
            if (!geometries.TryGetValue(id, out var g))
            {
                var src = geolib!.IdLookup("geometry", id)!;
                g = ParseGeometry(up, src, matlib, ctx);
                geometries[id] = g;
            }
            return g;
        }

        public void Set()
        {
            model.Geometries = geometries.Values.ToArray();
        }
    }

    class MaterialAccessor(Model model, XElement matlib, XElement fxlib)
    {
        public Material GetMaterial(string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) id = "DEFAULT";
            var src = matlib?.IdLookup("material", "id");
            var name = id;
            if (src != null)
                name = string.IsNullOrWhiteSpace(src.Attribute("name")?.Value) ? id : src.Attribute("name")!.Value;
            if (!model.Materials.TryGetValue(name, out Material mat))
            {
                mat = ParseMaterial(src, name);
                model.Materials.Add(name, mat);
            }

            return mat;
        }

        Material ParseMaterial(XElement src, string name)
        {
            var cmat = new Material() { Name = name, DiffuseColor = LinearColor.White };
            if (matlib == null || fxlib == null || src == null) return cmat;
            var instanceEffect = src.Child("instance_effect");
            if (instanceEffect == null) return cmat;
            var fx = fxlib.IdLookup("effect", CheckURI(instanceEffect.Attribute("url")!.Value));
            if (fx != null)
            {
                var technique = fx.Child("profile_COMMON")?.SidLookup("technique", "common");
                if (technique == null) return cmat;
                var fxp = technique.Elements().FirstOrDefault(x =>
                    x.Name.LocalName == "phong" || x.Name.LocalName == "blinn" || x.Name.LocalName == "lambert");
                var diffuse = fxp?.Child("diffuse");
                if (diffuse != null)
                {
                    SetDc(cmat, diffuse);
                }
            }

            return cmat;
        }

        static void SetDc(Material material, XElement obj)
        {
            if (obj == null) return;
            if (obj.Name.LocalName == "color")
            {
                if (ParseHelpers.TryParseColor(obj.Value!, out var srgb))
                    material.DiffuseColor = LinearColor.FromSrgb(srgb);
            }
            // Don't support textures.
        }
    }

    const string SEM_VERTEX = "VERTEX";
    const string SEM_POSITION = "POSITION";
    const string SEM_COLOR = "COLOR";
    const string SEM_NORMAL = "NORMAL";
    const string SEM_TEXCOORD = "TEXCOORD";

    static IEnumerable<XElement> GetFloatArrays(XElement geo)
    {
        foreach (var s in geo.Elements().Where(x => x.Name.LocalName == "source"))
        {
            foreach (var a in s.Elements().Where(x => x.Name.LocalName == "float_array"))
            {
                yield return a;
            }
        }
    }

    record struct Input(string Semantic, string SourceUrl, int Offset, int Set);

    static Input[] GetInputs(XElement geo)
    {
        var l = new List<Input>();
        foreach (var input in geo.Elements().Where(x => x.Name.LocalName == "input"))
        {
            l.Add(new Input(
                input.Attribute("semantic")!.Value,
                input.Attribute("source")!.Value,
                input.IntAttribute("offset"),
                input.IntAttribute("set")));
        }
        return l.ToArray();
    }

    static (string? materialRef, Input[] inputs, int count) GetShapeInfo(XElement geo)
    {
        var material = geo.Attribute("material")?.Value;
        int count = geo.IntAttribute("count");
        return (material, GetInputs(geo), count);
    }

    static int[] GetPRefs(XElement elem) => ParseHelpers.IntArray(elem.Child("p")!.Value);

    static Geometry ParseGeometry(UpAxisType up, XElement geo,  MaterialAccessor matlib, ModelLoadContext ctx)
    {
        var conv = new Geometry();
        conv.Name = string.IsNullOrEmpty(geo.Attribute("name")?.Value)
            ? geo.Attribute("id")!.Value
            : geo.Attribute("name")!.Value;

        var vertices = new VertexBufferBuilder();
        var indices = new List<uint>();
        List<TriangleGroup> groups = new List<TriangleGroup>();

        Dictionary<string, float[]> arrays = new();
        Dictionary<string, GeometrySource> sources = new();

        var mesh = geo.Child("mesh");
        if (mesh == null)
            return null;

        foreach (var arr in GetFloatArrays(mesh))
        {
            var id = arr.Attribute("id")!.Value!;
            arrays[id] = ParseHelpers.FloatArray(arr.Value);
        }

        foreach (var s in mesh.Elements().Where(x => x.Name.LocalName == "source"))
        {
            var src = new GeometrySource(s, arrays);
            sources[src.Id] = src;
        }

        XElement[] polys = mesh.Elements().Where(x =>
            x.Name.LocalName == "triangles" ||
            x.Name.LocalName == "polylist" ||
            x.Name.LocalName == "polygons" ||
            x.Name.LocalName == "lines").ToArray();

        var verticesElem = mesh.Child("vertices");
        var verticesId = verticesElem?.Attribute("id")?.Value;
        Input[] verticesInput = verticesElem != null
            ? GetInputs(verticesElem)
            : [];

        GeometryKind kind = GeometryKind.Triangles;
        bool set = false;

        foreach (var elem in polys)
        {
            var (materialRef, inputs, indexCount) = GetShapeInfo(elem);
            int[] pRefs;
            switch (elem.Name.LocalName)
            {
                case "triangles":
                    if (set && kind != GeometryKind.Triangles)
                    {
                        ctx.Warn("Collada", $"Ignoring {elem.Name.LocalName} element.");
                        continue;
                    }

                    indexCount *= 3;
                    pRefs = GetPRefs(elem);
                    break;
                case "polylist":
                    if (set && kind != GeometryKind.Triangles)
                    {
                        ctx.Warn("Collada", $"Ignoring {elem.Name.LocalName} element.");
                        continue;
                    }

                    if (ParseHelpers.IntArray(elem.Child("vcount")!.Value).Any(x => x != 3))
                    {
                        throw new ModelLoadException("Polylist: non-triangle geometry");
                    }

                    pRefs = GetPRefs(elem);
                    indexCount *= 3;
                    break;
                case "polygons":
                    if (set && kind != GeometryKind.Triangles)
                    {
                        ctx.Warn("Collada", $"Ignoring {elem.Name.LocalName} element.");
                        continue;
                    }

                    throw new NotImplementedException("polygons");
                case "lines":
                    if (set && kind != GeometryKind.Lines)
                    {
                        ctx.Warn("Collada", $"Ignoring {elem.Name.LocalName} element.");
                        continue;
                    }

                    kind = GeometryKind.Lines;
                    indexCount *= 2;
                    pRefs = GetPRefs(elem);
                    break;
                default:
                    throw new InvalidOperationException();
            }

            if (indexCount == 0) continue; //Skip empty
            set = true;
            var material = matlib.GetMaterial(materialRef);
            int pStride = 0;
            foreach (var input in inputs)
                pStride = Math.Max((int)input.Offset, pStride);
            pStride++;
            GeometrySource sourceXYZ = null;
            int offXYZ = int.MinValue;
            GeometrySource sourceNORMAL = null;
            int offNORMAL = int.MinValue;
            GeometrySource sourceCOLOR = null;
            int offCOLOR = int.MinValue;
            GeometrySource sourceUV1 = null;
            int offUV1 = int.MinValue;
            GeometrySource sourceUV2 = null;
            int offUV2 = int.MinValue;
            int texCount = 0;
            int startIdx = indices.Count;
            foreach (var input in inputs)
            {
                switch (input.Semantic)
                {
                    case SEM_VERTEX:
                        if (CheckURI(input.SourceUrl) != verticesId)
                            throw new ModelLoadException("VERTEX doesn't match mesh vertices");
                        foreach (var ip2 in verticesInput)
                        {
                            switch (ip2.Semantic)
                            {
                                case SEM_POSITION:
                                    offXYZ = (int)input.Offset;
                                    sourceXYZ = sources[CheckURI(ip2.SourceUrl)];
                                    break;
                                case SEM_NORMAL:
                                    offNORMAL = (int)input.Offset;
                                    sourceNORMAL = sources[CheckURI(ip2.SourceUrl)];
                                    conv.Attributes |= VertexAttributes.Normal;
                                    break;
                                case SEM_COLOR:
                                    offCOLOR = (int)input.Offset;
                                    sourceCOLOR = sources[CheckURI(ip2.SourceUrl)];
                                    conv.Attributes |= VertexAttributes.Diffuse;
                                    break;
                                case SEM_TEXCOORD:
                                    if (texCount == 2)
                                        throw new ModelLoadException("Too many texcoords! (Max supported: 2)");
                                    if (texCount == 1)
                                    {
                                        offUV2 = (int)input.Offset;
                                        sourceUV2 = sources[CheckURI(ip2.SourceUrl)];
                                        conv.Attributes |= VertexAttributes.Texture2;
                                    }
                                    else
                                    {
                                        offUV1 = (int)input.Offset;
                                        sourceUV1 = sources[CheckURI(ip2.SourceUrl)];
                                        conv.Attributes |= VertexAttributes.Texture1;
                                    }

                                    texCount++;
                                    break;
                            }
                        }

                        break;
                    case SEM_POSITION:
                        offXYZ = (int)input.Offset;
                        sourceXYZ = sources[CheckURI(input.SourceUrl)];
                        break;
                    case SEM_NORMAL:
                        offNORMAL = (int)input.Offset;
                        sourceNORMAL = sources[CheckURI(input.SourceUrl)];
                        conv.Attributes |= VertexAttributes.Normal;
                        break;
                    case SEM_COLOR:
                        offCOLOR = (int)input.Offset;
                        sourceCOLOR = sources[CheckURI(input.SourceUrl)];
                        conv.Attributes |= VertexAttributes.Diffuse;
                        break;
                    case SEM_TEXCOORD:
                        if (texCount == 2) throw new Exception("Too many texcoords!");
                        if (texCount == 1)
                        {
                            offUV2 = (int)input.Offset;
                            sourceUV2 = sources[CheckURI(input.SourceUrl)];
                            conv.Attributes |= VertexAttributes.Texture2;
                        }
                        else
                        {
                            offUV1 = (int)input.Offset;
                            sourceUV1 = sources[CheckURI(input.SourceUrl)];
                            conv.Attributes |= VertexAttributes.Texture1;
                        }

                        texCount++;
                        break;
                }
            }

            for (int i = 0; i < indexCount; i++)
            {
                int idx = i * pStride;
                if (idx >= pRefs.Length)
                {
                    Console.WriteLine();
                }
                var pos = Transform.ToYUp((UpAxis)(int)up, sourceXYZ.GetXYZ(pRefs[idx + offXYZ]));
                var normal = offNORMAL == int.MinValue
                    ? Vector3.Zero
                    : Transform.ToYUp((UpAxis)(int)up, sourceNORMAL.GetXYZ(pRefs[idx + offNORMAL]));
                var color = offCOLOR == int.MinValue ? LinearColor.White : sourceCOLOR.GetColor(pRefs[idx + offCOLOR]);
                var uv1 = offUV1 == int.MinValue ? Vector2.Zero : sourceUV1.GetUV(pRefs[idx + offUV1]);
                var uv2 = offUV2 == int.MinValue ? Vector2.Zero : sourceUV2.GetUV(pRefs[idx + offUV2]);
                var vert = new Vertex(pos, normal, color, Vector4.Zero, uv1,uv2, Vector2.Zero, Vector2.Zero);
                indices.Add((uint)(vertices.Add(ref vert) - vertices.BaseVertex));
            }

            groups.Add(new TriangleGroup()
            {
                StartIndex = startIdx,
                BaseVertex = vertices.BaseVertex,
                IndexCount = indices.Count - startIdx,
                Material = material
            });
            vertices.Chunk();
        }

        conv.Kind = kind;
        conv.Vertices = vertices.Vertices.ToArray();
        conv.Indices = Indices.FromBuffer(indices.ToArray());
        conv.Groups = groups.ToArray();
        return conv;
    }


    class GeometrySource
    {
        float[] array;
        int stride;
        int offset;
        public int Count { get; private set; }
        public string Id { get; private set; }

        public GeometrySource(XElement src, Dictionary<string, float[]> arrays)
        {
            Id = src.Attribute("id")!.Value!;
            var acc = src.Child("technique_common")?.Child("accessor");
            array = arrays[CheckURI(acc.Attribute("source")?.Value)];
            stride = acc.IntAttribute("stride", 1);
            offset = acc.IntAttribute("offset");
            Count = acc.IntAttribute("count");
        }

        public LinearColor GetColor(int index)
        {
            var i = offset + (index * stride);
            if (stride == 4)
                return LinearColor.FromSrgb(array[i], array[i + 1], array[i + 2], array[i + 3]);
            else if (stride == 3)
                return LinearColor.FromSrgb(array[i], array[i + 1], array[i + 2], 1);
            else
                throw new Exception("Color Unhandled stride " + stride);
        }

        public Vector3 GetXYZ(int index)
        {
            if (stride != 3) throw new Exception("Vec3 Unhandled stride " + stride);
            var i = offset + (index * stride);
            return new Vector3(
                array[i],
                array[i + 1],
                array[i + 2]
            );
        }

        public Vector2 GetUV(int index)
        {
            if (stride != 2) throw new Exception("Vec2 Unhandled stride " + stride);
            var i = offset + (index * stride);
            return new Vector2(
                array[i],
                1 - array[i + 1]
            );
        }
    }

    static string CheckURI(string s)
    {
        if (s[0] != '#') throw new ModelLoadException("External references in COLLADA are not supported");
        return s.Substring(1);
    }
}

static class XExtensions
{
    public static XElement? IdLookup(this XElement e, string name, string id)
        => e.Elements().FirstOrDefault(x => x.Name.LocalName == name &&
                                               x.Attribute("id")?.Value == id);

    public static XElement? SidLookup(this XElement e, string name, string id)
        => e.Elements().FirstOrDefault(x => x.Name.LocalName == name &&
                                               x.Attribute("sid")?.Value == id);
    public static XElement? Child(this XElement e, string name)
        => e.Elements().FirstOrDefault(x => x.Name.LocalName == name);

    public static int IntAttribute(this XElement e, string name, int def = 0)
    {
        var x = e.Attribute(name);
        if (x == null) return def;
        return int.Parse(x.Value);
    }
}