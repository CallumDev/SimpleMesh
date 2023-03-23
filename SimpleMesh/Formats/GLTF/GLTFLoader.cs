using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;

namespace SimpleMesh.Formats.GLTF
{
    static class GLTFLoader
    {
        public static Model Load(Stream stream, ModelLoadContext ctx)
        {
            using var reader = new StreamReader(stream);
            return Load(reader.ReadToEnd(), null, ctx);
        }

        public static Model Load(string json, byte[] binchunk, ModelLoadContext ctx)
        {
            using var jsonObject = JsonDocument.Parse(json);
            var jsonRoot = jsonObject.RootElement;
            if (!jsonRoot.TryGetProperty("asset", out var assetElement))
                throw new ModelLoadException("Invalid glTF 2.0 JSON (missing asset element)");
            if (!assetElement.TryGetProperty("version", out var versionElement))
                throw new ModelLoadException("Invalid glTF 2.0 JSON (missing asset version)");
            if (versionElement.GetString() != "2.0")
                throw new ModelLoadException("Invalid glTF 2.0 JSON (asset version != 2.0)");

            //Load mesh resources
            if (!jsonRoot.TryGetProperty("buffers", out var buffersElement)) {
                throw new ModelLoadException("glTF file contains no buffers");
            }
            if (!jsonRoot.TryGetProperty("bufferViews", out var bufferViewsElement)) {
                throw new ModelLoadException("glTF file contains no buffer views");
            }
            if (!jsonRoot.TryGetProperty("accessors", out var accessorsElement)) {
                throw new ModelLoadException("glTF file contains no accessors");
            }
            if (!jsonRoot.TryGetProperty("meshes", out var meshesElement)) {
                throw new ModelLoadException("glTF file contains no meshes");
            }
            if (!jsonRoot.TryGetProperty("materials", out var materialsElement)) {
                throw new ModelLoadException("glTF file contains no materials");
            }
            var buffers = new GLTFBuffer[buffersElement.GetArrayLength()];
            int k = 0;
            foreach (var b in buffersElement.EnumerateArray())
            {
                buffers[k] = new GLTFBuffer(b, binchunk);
                k++;
            }
            var bufferViews = new GLTFBufferView[bufferViewsElement.GetArrayLength()];
            k = 0;
            foreach (var bv in bufferViewsElement.EnumerateArray())
            {
                bufferViews[k] = new GLTFBufferView(bv, buffers);
                k++;
            }
            var accessors = new GLTFBufferAccessor[accessorsElement.GetArrayLength()];
            k = 0;
            foreach (var ac in accessorsElement.EnumerateArray())
            {
                accessors[k] = new GLTFBufferAccessor(ac, bufferViews);
                k++;
            }
            //Load materials
            ImageData[] images = null;
            Dictionary<string, ImageData> referencedImages = null;
            if (jsonRoot.TryGetProperty("images", out var imagesElement))
            {
                k = 0;
                images = new ImageData[imagesElement.GetArrayLength()];
                referencedImages = new Dictionary<string, ImageData>();
                foreach (var i in imagesElement.EnumerateArray())
                {
                    string name = null;
                    string mimeType = null;
                    byte[] data = null;
                    if (i.TryGetProperty("name", out var nameElem))
                       name = nameElem.ToString();
                    if (i.TryGetProperty("mimeType", out var mimeTypeElem))
                        mimeType = mimeTypeElem.ToString();
                    if (i.TryGetProperty("bufferView", out var bufferViewElem))
                    {
                        var idx = bufferViewElem.GetInt32();
                        var bv = bufferViews[idx];
                        data = new byte[bv.ByteLength];
                        Array.Copy(bv.Buffer.Buffer, bv.ByteOffset, data, 0, bv.ByteLength);
                    }
                    images[k] = new ImageData(name, data, mimeType);
                    k++;
                }
            }

            int[] textureSources = null;
            if (jsonRoot.TryGetProperty("textures", out var texturesElement))
            {
                k = 0;
                textureSources = new int[texturesElement.GetArrayLength()];
                foreach (var i in texturesElement.EnumerateArray())
                {
                    if (i.TryGetProperty("source", out var sourceElem))
                        textureSources[k] = sourceElem.GetInt32();
                    k++;
                }
            }
            
            var materials = new Material[materialsElement.GetArrayLength()];
            k = 0;
            foreach (var m in materialsElement.EnumerateArray())
            {
                if (!m.TryGetProperty("name", out var matname))
                    throw new ModelLoadException("material missing name property");
                var mat= new Material {Name = matname.GetString()};
                if (m.TryGetProperty("pbrMetallicRoughness", out var pbr))
                {
                    if (pbr.TryGetProperty("baseColorFactor", out var baseCol))
                    {
                        if (GetFloatArray(baseCol, 4, out var colFactor))
                            mat.DiffuseColor = new Vector4(colFactor[0], colFactor[1], colFactor[2], colFactor[3]);
                        else if (TryGetVector3(baseCol, out var colRgb))
                            mat.DiffuseColor = new Vector4(colRgb, 1.0f);
                    }
                    if (pbr.TryGetProperty("baseColorTexture", out var texElem) 
                        && textureSources != null 
                        && images != null && texElem.TryGetProperty("index", out var tex))
                    {
                        var img = images[textureSources[tex.GetInt32()]];
                        mat.DiffuseTexture = img.Name;
                        if (img.Data != null) {
                            referencedImages[img.Name] = img;
                        }
                    }
                }
                materials[k++] = mat;
            }
            //Meshes
            var meshes = new Geometry[meshesElement.GetArrayLength()];
            k = 0;
            foreach (var m in meshesElement.EnumerateArray())
            {
                meshes[k] = GLTFGeometry.FromMesh(m, materials, accessors);
                k++;
            }
            
            //Load nodes and scene
            if (!jsonRoot.TryGetProperty("nodes", out var nodesElement)) {
                throw new ModelLoadException("glTF file contains no materials");
            }

            var nodes = new GLTFNode[nodesElement.GetArrayLength()];
            k = 0;
            foreach (var n in nodesElement.EnumerateArray())
            {
                nodes[k] = new GLTFNode();
                if (n.TryGetProperty("name", out var nameElement))
                    nodes[k].Name = nameElement.GetString();
                if (n.TryGetProperty("mesh", out var meshElement))
                    nodes[k].Geometry = meshes[meshElement.GetInt32()];
                Vector3 translation = Vector3.Zero;
                Quaternion rotation = Quaternion.Identity;
                if (n.TryGetProperty("translation", out var trElem) &&
                    !TryGetVector3(trElem, out translation))
                    throw new ModelLoadException("node has malformed translation element");
                if (n.TryGetProperty("rotation", out var rotElem) &&
                    !TryGetQuaternion(rotElem, out rotation))
                    throw new ModelLoadException("node has malformed rotation element");
                nodes[k].Transform = Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(translation);
                if (n.TryGetProperty("children", out var childElem))
                {
                    foreach(var child in childElem.EnumerateArray())
                        nodes[k].Children.Add(child.GetInt32());
                }
                if (n.TryGetProperty("extras", out var extrasElem))
                {
                    foreach(var prop in extrasElem.EnumerateObject())
                        nodes[k].Properties.Add(prop.Name, prop.Value.ToString());
                }
                k++;
            }

            if (!jsonRoot.TryGetProperty("scenes", out var scenesElement)) {
                throw new ModelLoadException("glTF file contains no scenes");
            }

            var scenes = new GLTFScene[scenesElement.GetArrayLength()];
            k = 0;
            foreach (var obj in scenesElement.EnumerateArray())
            {
                scenes[k] = new GLTFScene();
                if (obj.TryGetProperty("nodes", out var nodesElem))
                {
                    foreach(var n in nodesElem.EnumerateArray())
                        scenes[k].Nodes.Add(n.GetInt32());
                }
                k++;
            }
            //
            int sceneIndex = 0;
            if (jsonRoot.TryGetProperty("scene", out var sceneElement)) {
                sceneIndex = sceneElement.GetInt32();
            }

            List<Geometry> refGeometry = new List<Geometry>();
            List<Material> refMaterial = new List<Material>();
            var model = new Model();
            model.Roots = new ModelNode[scenes[sceneIndex].Nodes.Count];
            for (int i = 0; i < model.Roots.Length; i++)
                model.Roots[i] = GetNode(nodes, scenes[sceneIndex].Nodes[i], refGeometry, refMaterial);
            model.Geometries = refGeometry.ToArray();
            model.Materials = new Dictionary<string, Material>();
            foreach (var mat in refMaterial)
                model.Materials[mat.Name] = mat;
            if (referencedImages.Count > 0)
                model.Images = referencedImages;
            return model;
        }

        static ModelNode GetNode(GLTFNode[] nodes, int index, List<Geometry> refGeometry, List<Material> refMaterial)
        {
            var m = new ModelNode();
            m.Name = nodes[index].Name;
            m.Geometry = nodes[index].Geometry;
            if (m.Geometry != null && !refGeometry.Contains(m.Geometry))
            {
                refGeometry.Add(m.Geometry);
                foreach (var tg in m.Geometry.Groups)
                {
                    if(!refMaterial.Contains(tg.Material)) refMaterial.Add(tg.Material);
                }
            }
            m.Properties = nodes[index].Properties;
            m.Transform = nodes[index].Transform;
            foreach(var child in nodes[index].Children)
            {
                m.Children.Add(GetNode(nodes, child, refGeometry, refMaterial));
            }
            return m;
        }

        class GLTFScene
        {
            public List<int> Nodes = new List<int>();
        }
        class GLTFNode
        {
            public string Name;
            public Matrix4x4 Transform;
            public Geometry Geometry;
            public List<int> Children = new List<int>();
            public Dictionary<string, string> Properties = new Dictionary<string, string>();
        }
        
        
        static bool TryGetVector3(JsonElement element, out Vector3 v)
        {
            if (!GetFloatArray(element, 3, out float[] floats))
            {
                v = Vector3.Zero;
                return false;
            }
            v = new Vector3(floats[0], floats[1], floats[2]);
            return true;
        }
        
        static bool TryGetQuaternion(JsonElement element, out Quaternion q)
        {
            if (!GetFloatArray(element, 4, out float[] floats))
            {
                q = Quaternion.Identity;
                return false;
            }
            q = new Quaternion(floats[0], floats[1], floats[2], floats[3]);
            return true;
        }

        static bool GetFloatArray(JsonElement element, int expected, out float[] floats)
        {
            if (element.GetArrayLength() != expected)
            {
                floats = null;
                return false;
            }
            floats = new float[expected];
            int i = 0;
            foreach (var item in element.EnumerateArray()) {
                if (!item.TryGetSingle(out floats[i]))
                {
                    floats = null;
                    return false;
                }
                i++;
            }
            return true;
        }
    }
}