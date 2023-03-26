using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Xml.Serialization;
using SimpleMesh.Formats.Collada.Schema;
using SimpleMesh.Util;

namespace SimpleMesh.Formats.Collada
{
    static class ColladaLoader
    {

        public static Model Load(Stream stream, ModelLoadContext ctx)
        {
            COLLADA dae;
            using (var reader = new StreamReader(stream)) {
                dae = (COLLADA)ColladaXml.Xml.Deserialize(reader);
            }
            //Get libraries
            var geometrylib = dae.Items.OfType<library_geometries>().First();
            var scenelib = dae.Items.OfType<library_visual_scenes>().First();
            var matlib = dae.Items.FirstOfType<library_materials>();
            var fxlib = dae.Items.FirstOfType<library_effects>();
            var convGeo = new List<Geometry>();
            //Get main scene
            var urlscn = CheckURI(dae.scene.instance_visual_scene.url);
            var scene = scenelib.visual_scene.Where((x) => x.id == urlscn).First();
            var up = (UpAxis)dae.asset.up_axis;
            var model = new Model();
            var mataccess = new MaterialAccessor() {Model = model, context = ctx, matlib = matlib, fxlib = fxlib};
            List<ModelNode> Nodes = new List<ModelNode>();
            model.Materials = new Dictionary<string, Material>();
            foreach(var node in scene.node) {
                Nodes.Add(ProcessNode(up, geometrylib, convGeo, node, mataccess, ctx));
            }
            model.Roots = Nodes.ToArray();
            model.Geometries = convGeo.ToArray();
            return model;
        }
        
        static ModelNode ProcessNode(UpAxis up, library_geometries geom, List<Geometry> convGeo, node n, MaterialAccessor matlib, ModelLoadContext ctx)
        {
            var obj = new ModelNode();
            obj.Name = n.name;
            if(n.instance_geometry != null && n.instance_geometry.Length > 0) {
                //Geometry object
                if (n.instance_geometry.Length != 1) throw new ModelLoadException("Multiple geometries in node");
                var uri = CheckURI(n.instance_geometry[0].url);
                var g = geom.geometry.First((x) => x.id == uri);
                if(g.Item is mesh)
                {
                    obj.Geometry = GetGeometry(up, g, matlib, ctx);
                    convGeo.Add(obj.Geometry);
                } 
            }

            if (n.Items != null)
            {
                if (n.Items.OfType<matrix>().Any())
                {
                    var tr = n.Items.OfType<matrix>().First();
                    obj.Transform = GetMatrix(up, tr.Text);
                }
                else
                {
                    Matrix4x4 mat = Matrix4x4.Identity;
                    foreach (var item in n.Items)
                    {
                        if (item is TargetableFloat3)
                        {
                            throw new ModelLoadException("Non-matrix transforms not yet supported");
                        }
                    }

                    obj.Transform = mat;
                }
            }
            if(n.node1 != null && n.node1.Length > 0) {
                foreach(var node in n.node1) {
                    obj.Children.Add(ProcessNode(up, geom, convGeo, node,matlib, ctx));
                }
            }
            return obj;
        }
        
        static Matrix4x4 GetMatrix(UpAxis ax, string text)
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
            return Transform.ToYUp(ax, mat);
        }
        
        //collada semantic data streams
        const string SEM_VERTEX = "VERTEX";
        const string SEM_POSITION = "POSITION";
        const string SEM_COLOR = "COLOR";
        const string SEM_NORMAL = "NORMAL";
        const string SEM_TEXCOORD = "TEXCOORD";

        class MaterialAccessor
        {
            public Model Model;
            public ModelLoadContext context;
            public library_materials matlib;
            public library_effects fxlib;
            
            public Material GetMaterial(string id)
            {
                if (string.IsNullOrWhiteSpace(id)) id = "DEFAULT";
                var src = matlib?.material.FirstOrDefault(mat => mat.id.Equals(id, StringComparison.InvariantCulture));
                var name = id;
                if(src != null)
                    name = string.IsNullOrWhiteSpace(src.name) ? src.id : src.name;
                if (!Model.Materials.TryGetValue(name, out Material mat))
                {
                    mat = ParseMaterial(src, name);
                    Model.Materials.Add(name, mat);
                }
                return mat;
            }

            Material ParseMaterial(material src, string name)
            {
                var cmat = new Material() {Name = name, DiffuseColor = Vector4.One};
                if (matlib == null) return cmat;
                if (src == null) return cmat;
                if (src.instance_effect == null) return cmat;
                if (fxlib == null) return cmat;
                var fx = fxlib.effect.FirstOrDefault(fx =>
                    fx.id.Equals(CheckURI(src.instance_effect.url), StringComparison.InvariantCulture));
                if (fx != null)
                {
                    var profile = fx.Items.FirstOfType<effectFx_profile_abstractProfile_COMMON>();
                    if (profile == null) return cmat;
                    if (profile.technique == null) return cmat;
                    switch (profile.technique.Item)
                    {
                        case effectFx_profile_abstractProfile_COMMONTechniquePhong phong:
                            SetDc(cmat, phong.diffuse);
                            break;
                        case effectFx_profile_abstractProfile_COMMONTechniqueBlinn blinn:
                            SetDc(cmat, blinn.diffuse);
                            break;
                        /*case effectFx_profile_abstractProfile_COMMONTechniqueConstant constant:
                            break;*/
                        case effectFx_profile_abstractProfile_COMMONTechniqueLambert lambert:
                            SetDc(cmat, lambert.diffuse);
                            break;
                    }
                }
                return cmat;
            }
            static void SetDc(Material material, common_color_or_texture_type obj)
            {
                if (obj == null) return;
                if (obj.Item is common_color_or_texture_typeColor col)
                {
                    ParseHelpers.TryParseColor(col.Text, out material.DiffuseColor);
                }
                if (obj.Item is common_color_or_texture_typeTexture tex)
                {
                    material.DiffuseColor = Vector4.One;
                    material.DiffuseTexture = tex.texture;
                }
            }
        }

        static Geometry GetGeometry(UpAxis up, geometry geo, MaterialAccessor matlib, ModelLoadContext ctx)
        {
            var conv = new Geometry() {Attributes = VertexAttributes.Position};
            conv.Name = string.IsNullOrEmpty(geo.name) ? geo.id : geo.name;
            var msh = geo.Item as mesh;
            if (msh == null) return null;
            var vertices = new VertexBufferBuilder();
            var indices = new List<uint>();
            List<TriangleGroup> groups = new List<TriangleGroup>();
            Dictionary<string, GeometrySource> sources = new Dictionary<string, GeometrySource>();
            Dictionary<string, float[]> arrays = new Dictionary<string, float[]>();
            Dictionary<string, GeometrySource> verticesRefs = new Dictionary<string, GeometrySource>();
            
            //Get arrays
            foreach(var acc in msh.source) {
                var arr = acc.Item as float_array;
                arrays.Add(arr.id, ParseHelpers.FloatArray(arr.Text));
            }
            //Accessors
            foreach(var acc in msh.source) {
                sources.Add(acc.id, new GeometrySource(acc, arrays));
            }
            //Process geometry
            foreach(var item in msh.Items) {
                if(!(item is triangles || item is polylist || item is polygons)) {
                    ctx.Warn("Collada", "Ignoring " + item.GetType().Name + " element.");
                }
            }
            foreach(var item in msh.Items.Where(x => x is triangles || x is polylist || x is polygons)) {
                InputLocalOffset[] inputs;
                int[] pRefs;
                int indexCount;
                string materialRef;
                Material material;
                if(item is triangles) {
                    var triangles = (triangles)item;
                    indexCount = (int)(triangles.count * 3);
                    pRefs = ParseHelpers.IntArray(triangles.p);
                    inputs = triangles.input;
                    materialRef = triangles.material;
                } else if (item is polygons polygons)
                {
                    indexCount = (int)(polygons.count * 3);
                    int j = 0;
                    pRefs = new int[indexCount];
                    foreach (var arr in polygons.Items)
                    {
                        if(!(arr is string)) throw new ModelLoadException("Polygons: ph element unsupported");
                        var ints = ParseHelpers.IntArray((string) arr);
                        if (ints.Length != 3)
                            throw new ModelLoadException("Polygons: non-triangle geometry not supported");
                        pRefs[j] = ints[0];
                        pRefs[j + 1] = ints[1];
                        pRefs[j + 2] = ints[2];
                        j += 3;
                    }
                    inputs = polygons.input;
                    materialRef = polygons.material;
                } else  {
                    var plist = (polylist)item;
                    pRefs = ParseHelpers.IntArray(plist.p);
                    foreach(var c in ParseHelpers.IntArray(plist.vcount)) {
                        if(c != 3) {
                            throw new ModelLoadException("Polylist: non-triangle geometry");
                        }
                    }
                    materialRef = plist.material;
                    inputs = plist.input;
                    indexCount = (int)(plist.count * 3);
                }
                if (indexCount == 0) continue; //Skip empty
                material = matlib.GetMaterial(materialRef);
                int pStride = 0;
                foreach (var input in inputs)
                    pStride = Math.Max((int)input.offset, pStride);
                pStride++;
                GeometrySource sourceXYZ = null; int offXYZ = int.MinValue;
                GeometrySource sourceNORMAL = null; int offNORMAL = int.MinValue;
                GeometrySource sourceCOLOR = null; int offCOLOR = int.MinValue;
                GeometrySource sourceUV1 = null; int offUV1 = int.MinValue;
                GeometrySource sourceUV2 = null; int offUV2 = int.MinValue;
                int texCount = 0;
                int startIdx = indices.Count;
                foreach(var input in inputs) {
                    switch(input.semantic) {
                        case SEM_VERTEX:
                            if (CheckURI(input.source) != msh.vertices.id)
                                throw new ModelLoadException("VERTEX doesn't match mesh vertices");
                            foreach(var ip2 in msh.vertices.input) {
                                switch(ip2.semantic) {
                                    case SEM_POSITION:
                                        offXYZ = (int)input.offset;
                                        sourceXYZ = sources[CheckURI(ip2.source)];
                                        break;
                                    case SEM_NORMAL:
                                        offNORMAL = (int)input.offset;
                                        sourceNORMAL = sources[CheckURI(ip2.source)];
                                        conv.Attributes |= VertexAttributes.Normal;
                                        break;
                                    case SEM_COLOR:
                                        offCOLOR = (int)input.offset;
                                        sourceCOLOR = sources[CheckURI(ip2.source)];
                                        conv.Attributes |= VertexAttributes.Diffuse;
                                        break;
                                    case SEM_TEXCOORD:
                                        if (texCount == 2) throw new ModelLoadException("Too many texcoords! (Max supported: 2)");
                                        if (texCount == 1)
                                        {
                                            offUV2 = (int)input.offset;
                                            sourceUV2 = sources[CheckURI(ip2.source)];
                                            conv.Attributes |= VertexAttributes.Texture2;
                                        }
                                        else
                                        {
                                            offUV1 = (int)input.offset;
                                            sourceUV1 = sources[CheckURI(ip2.source)];
                                            conv.Attributes |= VertexAttributes.Texture1;
                                        }
                                        texCount++;
                                        break;
                                }
                            }
                            break;
                        case SEM_POSITION:
                            offXYZ = (int)input.offset;
                            sourceXYZ = sources[CheckURI(input.source)];
                            break;
                        case SEM_NORMAL:
                            offNORMAL = (int)input.offset;
                            sourceNORMAL = sources[CheckURI(input.source)];
                            conv.Attributes |= VertexAttributes.Normal;
                            break;
                        case SEM_COLOR:
                            offCOLOR = (int)input.offset;
                            sourceCOLOR = sources[CheckURI(input.source)];
                            conv.Attributes |= VertexAttributes.Diffuse;
                            break;
                        case SEM_TEXCOORD:
                            if (texCount == 2) throw new Exception("Too many texcoords!");
                            if(texCount == 1) {
                                offUV2 = (int)input.offset;
                                sourceUV2 = sources[CheckURI(input.source)];
                                conv.Attributes |= VertexAttributes.Texture2;
                            } else {
                                offUV1 = (int)input.offset;
                                sourceUV1 = sources[CheckURI(input.source)];
                                conv.Attributes |= VertexAttributes.Texture1;
                            }
                            texCount++;
                            break;
                    }
                }
                for (int i = 0; i <  indexCount; i++) {
                    int idx = i * pStride;
                    var vert = new Vertex(
                        Transform.ToYUp(up, sourceXYZ.GetXYZ(pRefs[idx + offXYZ])),
                        offNORMAL == int.MinValue ? Vector3.Zero : Transform.ToYUp(up, sourceNORMAL.GetXYZ(pRefs[idx + offNORMAL])),
                        offCOLOR == int.MinValue ? Vector4.One : sourceCOLOR.GetColor(pRefs[idx + offCOLOR]),
                        offUV1 == int.MinValue ? Vector2.Zero : sourceUV1.GetUV(pRefs[idx + offUV1]),
                        offUV2 == int.MinValue ? Vector2.Zero : sourceUV2.GetUV(pRefs[idx + offUV2])
                    );
                    indices.Add((uint)(vertices.Add(ref vert) - vertices.BaseVertex));
                }
                groups.Add(new TriangleGroup() { 
                    StartIndex = startIdx,
                    BaseVertex = vertices.BaseVertex,
                    IndexCount = indices.Count - startIdx,
                    Material = material
                });
                vertices.Chunk();
            }

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
            public GeometrySource(source src, Dictionary<string, float[]> arrays)
            {
                var acc = src.technique_common.accessor;
                array = arrays[CheckURI(acc.source)];
                stride = (int)acc.stride;
                offset = (int)acc.offset;
                Count = (int)acc.count;
            }
            public Vector4 GetColor(int index)
            {
                var i = offset + (index * stride);
                if (stride == 4)
                    return new Vector4(
                        array[i],
                        array[i + 1],
                        array[i + 2],
                        array[i + 3]
                    );
                else if (stride == 3)
                    return new Vector4(
                        array[i],
                        array[i + 1],
                        array[i + 2],
                        1
                    );
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
                    array[i + 1]
                );
            }
        }
        static string CheckURI(string s)
        {
            if (s[0] != '#') throw new ModelLoadException("Don't support external dae refs");
            return s.Substring(1);
        }
    }
}