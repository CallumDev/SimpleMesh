using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Numerics;

namespace SimpleMesh.Formats.Collada;

using CL = Schema;

public class ColladaWriter
{
    private static CL.COLLADA NewCollada()
    {
        var dae = new CL.COLLADA();
        dae.asset = new CL.asset();
        dae.asset.created = dae.asset.modified = DateTime.UtcNow.ToString("s") + "Z";
        dae.asset.contributor = new[]
        {
            new CL.assetContributor()
            {
                author = "SimpleMesh",
                authoring_tool = "SimpleMesh"
            }
        };
        return dae;
    }

    public static void Write(Model model, Stream outStream)
    {
        var dae = NewCollada();
        var mats = new CL.library_materials();
        var efx = new CL.library_effects();
        var geos = new CL.library_geometries();
        var scenes = new CL.library_visual_scenes();
        var vscene = new CL.visual_scene();
        var geometries = new List<CL.geometry>();
        vscene.name = vscene.id = "main-scene";
        scenes.visual_scene = new[] {vscene};
        dae.scene = new CL.COLLADAScene();
        dae.scene.instance_visual_scene = new CL.InstanceWithExtra
        {
            url = "#main-scene"
        };
        mats.material = model.Materials.Select(x => new CL.material
        {
            name = x.Key,
            id = x.Key + "-material",
            instance_effect = new CL.instance_effect {url = "#" + x.Key + "-effect"}
        }).ToArray();
        efx.effect = model.Materials.Select(x => new CL.effect
        {
            id = x.Key + "-effect",
            Items = new[]
            {
                new CL.effectFx_profile_abstractProfile_COMMON
                {
                    technique = new CL.effectFx_profile_abstractProfile_COMMONTechnique
                    {
                        id = "common",
                        sid = "common",
                        Item = new CL.effectFx_profile_abstractProfile_COMMONTechniquePhong
                        {
                            ambient = ColladaColor("ambient", new Vector4(0, 0, 0, 1)),
                            emission = ColladaColor("emmision", new Vector4(0, 0, 0, 1)),
                            diffuse = ColladaColor("diffuse", x.Value.DiffuseColor),
                            specular = ColladaColor("specular", new Vector4(0.25f, 0.25f, 0.25f, 1f)),
                            shininess = ColladaFloat("shininess", 50),
                            index_of_refraction = ColladaFloat("index_of_refraction", 1)
                        }
                    }
                }
            }
        }).ToArray();
        vscene.node = model.Roots.Select(x => GetNode(x, geometries)).ToArray();
        geos.geometry = geometries.ToArray();
        dae.Items = new object[] {efx, mats, geos, scenes};
        ColladaXml.Xml.Serialize(outStream, dae);
    }

    static string MatrixText(Matrix4x4 t)
    {
        var floats = new float[]
        {
            t.M11, t.M21, t.M31, t.M41,
            t.M12, t.M22, t.M32, t.M42,
            t.M13, t.M23, t.M33, t.M43,
            t.M41, t.M42, t.M43, t.M44
        };
        return string.Join(" ", floats.Select((x) => x.ToString(CultureInfo.InvariantCulture)));
    }
    
    static CL.instance_material[] GetMaterials(CL.geometry g)
    {
        var materials = new List<CL.instance_material>();
        foreach (var item in ((CL.mesh) g.Item).Items)
        {
            string matref = ((CL.triangles) item).material;
            if (!materials.Any((m) => m.symbol == matref))
            {
                materials.Add(new CL.instance_material()
                {
                    symbol = matref,
                    target = "#" + matref
                });
            }
        }
        return materials.ToArray();
    }
    
    static CL.node GetNode(ModelNode mn, List<CL.geometry> geometries)
    {
        var n = new CL.node();
        n.name = n.id = mn.Name;
        n.Items = new object[]
        {
            new CL.matrix()
            {
                sid = "transform",
                Text = MatrixText(mn.Transform)
            }
        };
        n.ItemsElementName = new CL.ItemsChoiceType7[]
        {
            CL.ItemsChoiceType7.matrix
        };
        if (mn.Geometry != null)
        {
            var g = CreateGeometry(mn.Geometry);
            geometries.Add(g);
            n.instance_geometry = new CL.instance_geometry[]
            {
                new CL.instance_geometry
                {
                    url = "#" + mn.Geometry.Name,
                    bind_material = new CL.bind_material()
                    {
                        technique_common = GetMaterials(g)
                    }
                }
            };
        }
        if (mn.Children.Count > 0)
        {
            var children = new List<CL.node>();
            foreach (var child in mn.Children)
            {
                children.Add(GetNode(child, geometries));
            }
            n.node1 = children.ToArray();
        }
        return n;
    }

    private static CL.common_color_or_texture_type ColladaColor(string sid, Vector4 c)
    {
        var cl = new CL.common_color_or_texture_type();
        cl.Item = new CL.common_color_or_texture_typeColor
        {
            sid = sid, Text =
                string.Join(" ", new[] {c.X, c.Y, c.Z, c.W}.Select(x => x.ToString()))
        };
        return cl;
    }

    private static CL.common_float_or_param_type ColladaFloat(string sid, float f)
    {
        var cl = new CL.common_float_or_param_type();
        cl.Item = new CL.common_float_or_param_typeFloat {sid = sid, Value = f};
        return cl;
    }

    private static CL.geometry CreateGeometry(Geometry src)
    {
        var geo = new CL.geometry();
        geo.name = geo.id = src.Name;
        var mesh = new CL.mesh();
        geo.Item = mesh;
        CL.source positions;
        CL.source normals = null;
        CL.source colors = null;
        CL.source tex1 = null;
        CL.source tex2 = null;
        var idxC = 1;
        positions = CreateSource(
            geo.name + "-positions",
            k => new Vector4(src.Vertices[k].Position, 0),
            3, src.Vertices.Length);
        mesh.vertices = new CL.vertices
        {
            id = geo.name + "-vertices",
            input = new[]
            {
                new CL.InputLocal()
                {
                    semantic = "POSITION", source = "#" + positions.id
                }
            }
        };
        var sources = new List<CL.source> {positions};
        if ((src.Attributes & VertexAttributes.Normal) == VertexAttributes.Normal)
        {
            normals = CreateSource(
                geo.name + "-normals",
                k => new Vector4(src.Vertices[k].Normal, 0),
                3, src.Vertices.Length);
            sources.Add(normals);
            idxC++;
        }
        if ((src.Attributes & VertexAttributes.Diffuse) == VertexAttributes.Diffuse)
        {
            colors = CreateSource(
                geo.name + "-color",
                k => src.Vertices[k].Diffuse, 
                4, src.Vertices.Length);
            sources.Add(colors);
            idxC++;
        }
        if ((src.Attributes & VertexAttributes.Texture1) == VertexAttributes.Texture1)
        {
            tex1 = CreateSource(
                geo.name + "-tex1",
                k => new Vector4(src.Vertices[k].Texture1.X, 1 - src.Vertices[k].Texture1.Y, 0, 0), 
                2, src.Vertices.Length);
            sources.Add(tex1);
            idxC++;
        }
        if ((src.Attributes & VertexAttributes.Texture2) == VertexAttributes.Texture2)
        {
            tex2 = CreateSource(
                geo.name + "-tex2",
                k => new Vector4(src.Vertices[k].Texture2.X, 1 - src.Vertices[k].Texture2.Y, 0, 0), 
                2, src.Vertices.Length);
            sources.Add(tex2);
            idxC++;
        }
        mesh.source = sources.ToArray();
        var items = new List<object>();
        foreach (var dc in src.Groups)
        {
            var trs = new CL.triangles();
            trs.count = (ulong) (dc.IndexCount / 3);
            if(dc.Material?.Name != null)
                trs.material = dc.Material.Name + "-material";
            List<int> pRefs = new List<int>(dc.IndexCount * idxC);
            List<CL.InputLocalOffset> inputs = new List<CL.InputLocalOffset>()
            {
                new CL.InputLocalOffset()
                {
                    semantic = "VERTEX", source = "#" + geo.id + "-vertices", offset = 0
                }
            };
            ulong off = 1;
            if (normals != null)
                inputs.Add(new CL.InputLocalOffset()
                {
                    semantic = "NORMAL",
                    source = "#" + normals.id,
                    offset = off++
                });
            if (colors != null)
                inputs.Add(new CL.InputLocalOffset()
                {
                    semantic = "COLOR",
                    source = "#" + colors.id,
                    offset = off++
                });
            if (tex1 != null)
                inputs.Add(new CL.InputLocalOffset()
                {
                    semantic = "TEXCOORD",
                    source = "#" + tex1.id,
                    offset = off++
                });
            if (tex2 != null)
                inputs.Add(new CL.InputLocalOffset()
                {
                    semantic = "TEXCOORD",
                    source = "#" + tex2.id,
                    offset = off
                });
            trs.input = inputs.ToArray();
            for (int i = dc.StartIndex; i < dc.StartIndex + dc.IndexCount; i++)
            {
                for (int j = 0; j < idxC; j++)
                {
                    var idx = src.Indices.Indices16 != null ? src.Indices.Indices16[i] : src.Indices.Indices32[i];
                    pRefs.Add((int)(dc.BaseVertex + idx));
                }
            }
            trs.p = string.Join(" ", pRefs.ToArray());
            items.Add(trs);
        }
        mesh.Items = items.ToArray();
        return geo;
    }

    private static CL.source CreateSource(string id, Func<int, Vector4> get, int components, int len)
    {
        var src = new CL.source();
        src.id = id;
        var floats = new float[len * components];
        for (var i = 0; i < len; i++)
        {
            var v4 = get(i);
            floats[i * components] = v4.X;
            floats[i * components + 1] = v4.Y;
            if (components > 2)
                floats[i * components + 2] = v4.Z;
            if (components > 3)
                floats[i * components + 3] = v4.W;
        }

        var arrId = id + "-array";
        src.Item = new CL.float_array
        {
            id = arrId,
            Text = string.Join(" ", floats.Select(x => x.ToString(CultureInfo.InvariantCulture)))
        };
        src.technique_common = new CL.sourceTechnique_common();
        var acc = new CL.accessor
        {
            source = "#" + arrId,
            count = (ulong) len,
            stride = (ulong) components
        };
        src.technique_common.accessor = acc;
        if (components == 2)
            acc.param = new[]
            {
                new() {name = "U", type = "float"},
                new CL.param {name = "V", type = "float"}
            };
        else if (components == 3)
            acc.param = new[]
            {
                new() {name = "X", type = "float"},
                new CL.param {name = "Y", type = "float"},
                new CL.param {name = "Z", type = "float"}
            };
        else if (components == 4)
            acc.param = new[]
            {
                new() {name = "R", type = "float"},
                new CL.param {name = "G", type = "float"},
                new CL.param {name = "B", type = "float"},
                new CL.param {name = "A", type = "float"}
            };

        return src;
    }
}