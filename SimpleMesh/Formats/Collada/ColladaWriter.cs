using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace SimpleMesh.Formats.Collada;

static class ColladaWriter
{
    static string FloatStr(float f) => f.ToString(CultureInfo.InvariantCulture);

    static XElement FloatProp(string elementName, float f) =>
        new(elementName, new XElement("float", new XAttribute("sid", elementName), FloatStr(f)));

    static XElement ColorProp(string elementName, Vector4 f) =>
        new(elementName, new XElement("color",
            new XAttribute("sid", elementName),
            $"{FloatStr(f.X)} {FloatStr(f.Y)} {FloatStr(f.Z)} {FloatStr(f.W)}"));

    static void AddMaterial(XElement libraryEffect, XElement libraryMaterial, Material material)
    {
        libraryMaterial.Add(new XElement("material",
            new XAttribute("id", $"{material.Name}-material"),
            new XAttribute("name", material.Name),
            new XElement("instance", new XAttribute("url", $"#{material.Name}-effect"))));
        libraryEffect.Add(
            new XElement("effect",
                new XAttribute("id", $"{material.Name}-effect"),
                new XElement("profile_COMMON",
                    new XElement("technique",
                        new XAttribute("id", "common"),
                        new XAttribute("sid", "common"),
                        new XElement("phong",
                            ColorProp("emission", new Vector4(material.EmissiveColor, 1)),
                            ColorProp("ambient", new Vector4(0, 0, 0, 1)),
                            ColorProp("diffuse", material.DiffuseColor.ToSrgb()),
                            ColorProp("specular", new Vector4(0.25f, 0.25f, 0.25f, 1)),
                            FloatProp("shininess", 50),
                            FloatProp("index_of_refraction", 1))
                    )
                )
            )
        );
    }
    
    static XElement SrcParam(string name) =>
        new("param", new XAttribute("name", name), new XAttribute("type", "float"));

    static XElement Vec3Source(string name, IEnumerable<Vector3> vectors)
    {
        var fltArray = new StringBuilder();
        int count = 0;
        foreach (var v in vectors)
        {
            if (count > 0) fltArray.Append(" ");
            fltArray.Append(FloatStr(v.X))
                .Append(" ")
                .Append(FloatStr(v.Y))
                .Append(" ")
                .Append(FloatStr(v.Z));
            count++;
        }
        
        return new XElement("source",
            new XAttribute("id", name),
            new XElement("float_array",
                new XAttribute("id", $"{name}-array"),
                new XAttribute("count", count * 3),
                fltArray),
            new XElement("technique_common",
                new XElement("accessor",
                    new XAttribute("source", $"#{name}-array"),
                    new XAttribute("count", count),
                    new XAttribute("stride", "3"),
                    SrcParam("X"),
                    SrcParam("Y"),
                    SrcParam("Z"))));
    }
    
    static XElement ColorSource(string name, IEnumerable<LinearColor> vectors)
    {
        var fltArray = new StringBuilder();
        int count = 0;
        foreach (var v in vectors)
        {
            var c = v.ToSrgb();
            if (count > 0) fltArray.Append(" ");
            fltArray.Append(FloatStr(c.X))
                .Append(" ")
                .Append(FloatStr(c.Y))
                .Append(" ")
                .Append(FloatStr(c.Z))
                .Append(" ")
                .Append(FloatStr(c.W));
            count++;
        }
        
        return new XElement("source",
            new XAttribute("id", name),
            new XElement("float_array",
                new XAttribute("id", $"{name}-array"),
                new XAttribute("count", count * 4),
                fltArray),
            new XElement("technique_common",
                new XElement("accessor",
                    new XAttribute("source", $"#{name}-array"),
                    new XAttribute("count", count),
                    new XAttribute("stride", "4"),
                    SrcParam("R"),
                    SrcParam("G"),
                    SrcParam("B"),
                    SrcParam("A"))));
    }
    
    static XElement TexCoordSource(string name, IEnumerable<Vector2> vectors)
    {
        var fltArray = new StringBuilder();
        int count = 0;

        foreach (var v in vectors)
        {
            if (count > 0) fltArray.Append(" ");
            fltArray.Append(FloatStr(v.X))
                .Append(" ")
                .Append(FloatStr(1 - v.Y));
            count++;
        }
        
        return new XElement("source",
            new XAttribute("id", name),
            new XElement("float_array",
                new XAttribute("id", $"{name}-array"),
                new XAttribute("count", count * 2),
                fltArray),
            new XElement("technique_common",
                new XElement("accessor",
                    new XAttribute("source", $"#{name}-array"),
                    new XAttribute("count", count),
                    new XAttribute("stride", "2"),
                    SrcParam("U"),
                    SrcParam("V"))));
    }

    static XElement Input(string semantic, string source, int offset, string? set = null)
    {
        var xe = new XElement("input",
            new XAttribute("semantic", semantic),
            new XAttribute("source", source),
            new XAttribute("offset", offset));
        if(set != null)
            xe.Add(new XAttribute("set", set));
        return xe;
    }

    record struct GeometryInfo(string Id, string[] Materials);

    static (XElement Node, GeometryInfo Info) WriteGeometry(Geometry geo, string name)
    {
        string id = $"{name}-mesh";
        var mesh = new XElement("mesh");
        
        mesh.Add(Vec3Source($"{id}-positions", geo.Vertices.Select(x => x.Position)));
        
        if (geo.Has(VertexAttributes.Normal))
            mesh.Add(Vec3Source($"{id}-normals", geo.Vertices.Select(x => x.Normal)));
        if(geo.Has(VertexAttributes.Diffuse))
            mesh.Add(ColorSource($"{id}-diffuse", geo.Vertices.Select(x => x.Diffuse)));
        if (geo.Has(VertexAttributes.Texture1))
            mesh.Add(TexCoordSource($"{id}-tex1", geo.Vertices.Select(x => x.Texture1)));
        if (geo.Has(VertexAttributes.Texture2))
            mesh.Add(TexCoordSource($"{id}-tex2", geo.Vertices.Select(x => x.Texture2)));
        if (geo.Has(VertexAttributes.Texture3))
            mesh.Add(TexCoordSource($"{id}-tex3", geo.Vertices.Select(x => x.Texture3)));
        if (geo.Has(VertexAttributes.Texture4))
            mesh.Add(TexCoordSource($"{id}-tex4", geo.Vertices.Select(x => x.Texture4)));

        mesh.Add(new XElement("vertices",
            new XAttribute("id", $"{id}-vertices"),
            new XElement("input",
                new XAttribute("semantic", "POSITION"),
                new XAttribute("source", $"#{id}-positions"))));
        
        HashSet<string> materials = new();
        
        foreach (var dc in geo.Groups)
        {
            int attr = 0;
            materials.Add($"{dc.Material.Name}-material");
            var cgroup = new XElement(geo.Kind == GeometryKind.Triangles ? "triangles" : "lines",
                new XAttribute("material", $"{dc.Material.Name}-material"),
                new XAttribute("count", geo.Kind == GeometryKind.Triangles ? dc.IndexCount / 3 : dc.IndexCount / 2),
                Input("VERTEX", $"#{id}-vertices", attr++)
            );
            if(geo.Has(VertexAttributes.Normal))
                cgroup.Add(Input("NORMAL", $"#{id}-normals", attr++));
            if(geo.Has(VertexAttributes.Diffuse))
                cgroup.Add(Input("COLOR", $"#{id}-diffuse", attr++));
            if(geo.Has(VertexAttributes.Texture1))
                cgroup.Add(Input("TEXCOORD", $"#{id}-tex1", attr++, "0"));
            if(geo.Has(VertexAttributes.Texture2))
                cgroup.Add(Input("TEXCOORD", $"#{id}-tex2", attr++, "1"));
            if(geo.Has(VertexAttributes.Texture3))
                cgroup.Add(Input("TEXCOORD", $"#{id}-tex3", attr++, "2"));
            if(geo.Has(VertexAttributes.Texture4))
                cgroup.Add(Input("TEXCOORD", $"#{id}-tex3", attr++, "3"));
            var pRefs = new StringBuilder();
            bool first = true;
            for (int i = dc.StartIndex; i < dc.StartIndex + dc.IndexCount; i++)
            {
                for (int j = 0; j < attr; j++)
                {
                    var idx = geo.Indices.Indices16 != null ? geo.Indices.Indices16[i] : geo.Indices.Indices32[i];
                    if (!first) pRefs.Append(" ");
                    pRefs.Append(dc.BaseVertex + idx);
                    first = false;
                }
            }
            
            cgroup.Add(new XElement("p", pRefs));
            mesh.Add(cgroup);
        }
        
        var gn = new XElement("geometry",
            new XAttribute("id", id),
            new XAttribute("name", name),
            mesh);

        return (gn, new GeometryInfo($"{name}-mesh", materials.ToArray()));
    }

    static XElement Transform(Matrix4x4 m)
    {
        var s = $"{FloatStr(m.M11)} {FloatStr(m.M21)} {FloatStr(m.M31)} {FloatStr(m.M41)} " +
                $"{FloatStr(m.M12)} {FloatStr(m.M22)} {FloatStr(m.M32)} {FloatStr(m.M42)} " +
                $"{FloatStr(m.M13)} {FloatStr(m.M23)} {FloatStr(m.M33)} {FloatStr(m.M43)} " +
                $"{FloatStr(m.M14)} {FloatStr(m.M24)} {FloatStr(m.M34)} {FloatStr(m.M44)}";
        return new("matrix", new XAttribute("sid", "transform"), s);
    }
    
    static XElement ColladaNode(ModelNode n, XElement geolib, Dictionary<Geometry, GeometryInfo> usedGeos)
    {
        var collada = new XElement("node",
            new XAttribute("id", n.Name),
            new XAttribute("name", n.Name),
            Transform(n.Transform));
        if (n.Geometry != null)
        {
            GeometryInfo info;
            if (!usedGeos.TryGetValue(n.Geometry, out info))
            {
                var name = n.Geometry.Name ?? n.Name;
                var (node, ni) = WriteGeometry(n.Geometry, name);
                geolib.Add(node);
                info = ni;
                usedGeos[n.Geometry] = ni;
            }
            var i = new XElement("instance_geometry",
                new XAttribute("url", $"#{info.Id}"),
                new XAttribute("name", n.Name));
            if (info.Materials.Length > 0)
            {
                var t = new XElement("technique_common");
                foreach (var m in info.Materials)
                {
                    t.Add(new XElement("instance_material",
                        new XAttribute("symbol", m),
                        new XAttribute("target", $"#{m}")));
                }
                i.Add(new XElement("bind_material", t));
            }
            collada.Add(i);
        }
        foreach (var child in n.Children)
        {
            collada.Add(ColladaNode(child, geolib, usedGeos));
        }
        return collada;
    }


    public static void Write(Model model, Stream outStream)
    {
        var nowstr = DateTime.UtcNow.ToString("s") + "Z";
        var root = new XElement("COLLADA",
            new XAttribute(XNamespace.Xmlns + "xsi", "http://www.w3.org/2001/XMLSchema-instance"),
            new XAttribute(XNamespace.Xmlns + "xsd", "http://www.w3.org/2001/XMLSchema"),
            new XAttribute("version", "1.4.0"),
            new XElement("asset",
                new XElement("contributor",
                    new XElement("author", "SimpleMesh"),
                    new XElement("authoring_tool", "SimpleMesh")
                    ),
                new XElement("created", nowstr),
                new XElement("modified", nowstr)
                )
        );

        var fxlib = new XElement("library_effects");
        var matlib = new XElement("library_materials");

        foreach (var mat in model.Materials)
        {
            AddMaterial(fxlib, matlib, mat.Value);
        }
        
        root.Add(fxlib);
        root.Add(matlib);

        var geos = new XElement("library_geometries");
        Dictionary<Geometry, GeometryInfo> usedGeos = new();
        
        var scene = new XElement("visual_scene",
            new XAttribute("id", "main-scene"),
            new XAttribute("name", "main-scene"));

        foreach (var n in model.Roots)
        {
            scene.Add(ColladaNode(n, geos, usedGeos));
        }
        
        root.Add(geos);

        root.Add(new XElement("library_visual_scenes", scene));
        
        root.Add(new XElement("scene",
            new XElement("instance_visual_scene",
                new XAttribute("url", "#main-scene"))));

        SetDefaultXmlNamespace(root, "http://www.collada.org/2005/11/COLLADASchema");
        var doc = new XDocument(root);
        var settings = new XmlWriterSettings();
        settings.Indent = true;
        using var xwriter = XmlWriter.Create(outStream, settings);
        doc.Save(xwriter);
    }

    static void SetDefaultXmlNamespace(XElement xelem, XNamespace xmlns)
    {
        if (xelem.Name.NamespaceName == string.Empty)
            xelem.Name = xmlns + xelem.Name.LocalName;
        foreach (var e in xelem.Elements())
            SetDefaultXmlNamespace(e, xmlns);
    }
}