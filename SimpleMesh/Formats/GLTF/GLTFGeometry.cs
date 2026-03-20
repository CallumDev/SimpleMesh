using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using SimpleMesh.Util;

namespace SimpleMesh.Formats.GLTF
{
    static class GLTFGeometry
    {
        public static Geometry FromMesh(JsonElement element, JsonElement nodesElement, int geoIdx, Material[] materials,GLTFBufferAccessor[] accessors)
        {
            string GError(string error)
            {
                List<string> referencedBy = new List<string>();
                int k = 0;
                foreach (var n in nodesElement.EnumerateArray())
                {
                    if (!n.TryGetProperty("mesh", out var meshElement))
                        continue;
                    var idx = meshElement.GetInt32();
                    if (idx != geoIdx)
                        continue;
                    if (n.TryGetProperty("name", out var nameElement))
                        referencedBy.Add(nameElement.GetString() ?? $"NONAME (index {k})");
                    else
                        referencedBy.Add($"NONAME (index {k})");
                    k++;
                }
                return $"{error}\nReferenced By: {String.Join(',', referencedBy)}";
            }

            var geoName = "";
            if (element.TryGetProperty("name", out var nameProp))
                geoName = nameProp.GetString() ?? "";
            if (!element.TryGetProperty("primitives", out var primArray))
                throw new ModelLoadException(GError("mesh does not contain primitives"));
            int startIndex = 0;
            List<uint> indexArray = new List<uint>();
            List<TriangleGroup> tg = new List<TriangleGroup>();
            var attrs = VertexAttributes.Position;

            // Calculate attributes
            foreach (var prim in primArray.EnumerateArray())
            {
                if (!prim.TryGetProperty("attributes", out var attrArray))
                    throw new ModelLoadException(GError("mesh primitive does not contain attributes"));
                foreach (var elem in attrArray.EnumerateObject())
                {
                    switch (elem.Name)
                    {
                        case "NORMAL":
                            attrs |= VertexAttributes.Normal;
                            break;
                        case "TEXCOORD_0":
                            attrs |= VertexAttributes.Texture1;
                            break;
                        case "TEXCOORD_1":
                            attrs |= VertexAttributes.Texture2;
                            break;
                        case "TEXCOORD_2":
                            attrs |= VertexAttributes.Texture3;
                            break;
                        case "TEXCOORD_3":
                            attrs |= VertexAttributes.Texture4;
                            break;
                        case "COLOR_0":
                            attrs |= VertexAttributes.Diffuse;
                            break;
                        case "TANGENT":
                            attrs |= VertexAttributes.Tangent;
                            break;
                        case "JOINTS_0":
                        case "WEIGHTS_0":
                            attrs |= VertexAttributes.Joints;
                            break;
                    }
                }
            }

            VertexArrayBuilder vertexArray = new VertexArrayBuilder(attrs);

            int startMode = -1;
            foreach (var prim in primArray.EnumerateArray())
            {
                if (!prim.TryGetProperty("attributes", out var attrArray))
                    throw new ModelLoadException(GError("mesh primitive does not contain attributes"));

                if (!prim.TryGetProperty("mode", out var modeProp)
                    || !modeProp.TryGetInt32(out var mode))
                    mode = 4;

                string ModeName(int m) => m switch
                {
                    0 => "points",
                    1 => "lines",
                    4 => "triangles",
                    _ => $"unknown (mode {m})"
                };

                if (startMode != -1 && startMode != mode)
                {
                    throw new ModelLoadException(
                        GError($"mesh primitive has both {ModeName(startMode)} and {ModeName(mode)}"));
                }
                startMode = mode;

                int posIndex = -1,
                    normIndex = -1,
                    tex1Index = -1,
                    colIndex = -1,
                    tex2Index = -1,
                    tangentIndex = -1,
                    tex3Index = -1,
                    tex4Index = -1,
                    jointIndex = -1,
                    weightIndex = -1;
                foreach (var elem in attrArray.EnumerateObject())
                {
                    switch (elem.Name)
                    {
                        case "POSITION":
                            posIndex = elem.Value.GetInt32();
                            break;
                        case "NORMAL":
                            normIndex = elem.Value.GetInt32();
                            break;
                        case "TEXCOORD_0":
                            tex1Index = elem.Value.GetInt32();
                            break;
                        case "TEXCOORD_1":
                            tex2Index = elem.Value.GetInt32();
                            break;
                        case "TEXCOORD_2":
                            tex3Index = elem.Value.GetInt32();
                            break;
                        case "TEXCOORD_3":
                            tex4Index = elem.Value.GetInt32();
                            break;
                        case "COLOR_0":
                            colIndex = elem.Value.GetInt32();
                            break;
                        case "TANGENT":
                            tangentIndex = elem.Value.GetInt32();
                            break;
                        case "JOINTS_0":
                            jointIndex = elem.Value.GetInt32();
                            break;
                        case "WEIGHTS_0":
                            weightIndex = elem.Value.GetInt32();
                            break;
                    }
                }

                if ((jointIndex > -1 || weightIndex > -1) &&
                    (jointIndex == -1 || weightIndex == -1))
                {
                    throw new ModelLoadException(
                        "Geometry contains singular joints/weights without matching attribute");
                }

                if (prim.TryGetProperty("indices", out var indicesProp))
                {
                    var indices = accessors[indicesProp.GetInt32()];
                    for (int i = 0; i < indices.Count; i++)
                    {
                        var index = indices.GetIndex(i);
                        var v = new Vertex() {Diffuse = LinearColor.White};
                        v.Position = accessors[posIndex].GetVector3((int) index);
                        if (normIndex != -1)
                            v.Normal = accessors[normIndex].GetVector3((int) index);
                        if (tangentIndex != -1)
                            v.Tangent = accessors[tangentIndex].GetVector4((int)index);
                        if (tex1Index != -1)
                        {
                            v.Texture1 = accessors[tex1Index].GetVector2((int) index);
                        }
                        if (tex2Index != -1)
                        {
                            v.Texture2 = accessors[tex2Index].GetVector2((int) index);
                        }
                        if (tex3Index != -1)
                        {
                            v.Texture3 = accessors[tex3Index].GetVector2((int)index);
                        }
                        if (tex4Index != -1)
                        {
                            v.Texture4 = accessors[tex4Index].GetVector2((int)index);
                        }
                        if (colIndex != -1)
                        {
                            if (accessors[colIndex].Type == AccessorType.VEC3)
                                v.Diffuse = LinearColor.FromVector4(new Vector4(accessors[colIndex].GetVector3((int) index), 1.0f));
                            else
                                v.Diffuse = LinearColor.FromVector4(accessors[colIndex].GetVector4((int) index));
                        }
                        if (jointIndex != -1)
                        {
                            v.JointIndices = accessors[jointIndex].GetUShort4((int)index);
                            v.JointWeights = accessors[weightIndex].GetVector4((int)index);
                        }
                        int idx = vertexArray.Add(ref v) - vertexArray.BaseVertex;
                        indexArray.Add((uint) idx);
                    }
                }
                else
                {
                    var c = accessors[posIndex].Count;
                    for (int index = 0; index < c; index++)
                    {
                        var v = new Vertex() {Diffuse = LinearColor.White};
                        v.Position = accessors[posIndex].GetVector3(index);
                        if (normIndex != -1)
                            v.Normal = accessors[normIndex].GetVector3(index);
                        if (tangentIndex != -1)
                            v.Tangent = accessors[tangentIndex].GetVector4(index);
                        if (tex1Index != -1)
                        {
                            v.Texture1 = accessors[tex1Index].GetVector2(index);
                        }
                        if (tex2Index != -1)
                        {
                            v.Texture2 = accessors[tex2Index].GetVector2(index);
                        }
                        if (tex3Index != -1)
                        {
                            v.Texture3 = accessors[tex3Index].GetVector2(index);
                        }
                        if (tex4Index != -1)
                        {
                            v.Texture4 = accessors[tex4Index].GetVector2(index);
                        }
                        if (colIndex != -1)
                        {
                            if (accessors[colIndex].Type == AccessorType.VEC3)
                                v.Diffuse = LinearColor.FromVector4(new Vector4(accessors[colIndex].GetVector3(index), 1.0f));
                            else
                                v.Diffuse = LinearColor.FromVector4(accessors[colIndex].GetVector4(index));
                        }
                        if (jointIndex != -1)
                        {
                            v.JointIndices = accessors[jointIndex].GetUShort4(index);
                            v.JointWeights = accessors[weightIndex].GetVector4(index);
                        }
                        int idx = vertexArray.Add(ref v) - vertexArray.BaseVertex;
                        indexArray.Add((uint) idx);
                    }
                }

                tg.Add(new TriangleGroup(prim.TryGetProperty("material", out var matProp)
                    ? materials[matProp.GetInt32()] : materials[0])
                {
                    BaseVertex = vertexArray.BaseVertex,
                    IndexCount = indexArray.Count - startIndex,
                    StartIndex = startIndex,
                });
                vertexArray.Chunk();
                startIndex = indexArray.Count;
            }

            var g = new Geometry(vertexArray.Finish(), Indices.FromBuffer(indexArray.ToArray()));
            g.Kind = startMode switch
            {
                4 => GeometryKind.Triangles,
                1 => GeometryKind.Lines,
                _ => throw new Exception(GError("Unsupported primitive mode " + startMode))
            };
            g.Vertices = vertexArray.Finish();
            g.Groups = tg.ToArray();

            return g;
        }
    }

    enum AccessorType
    {
        SCALAR,
        VEC2,
        VEC3,
        VEC4,
        MAT2,
        MAT3,
        MAT4
    }

    enum ComponentType
    {
        Int8 = 5120,
        UInt8 = 5121,
        Int16 = 5122,
        UInt16 = 5123,
        UInt32 = 5125,
        Float = 5126
    }

    class GLTFBufferAccessor
    {
        public GLTFBufferView BufferView;
        public int ByteOffset;
        public ComponentType ComponentType;
        public AccessorType Type;
        public bool Normalized;
        public int Count;

        public GLTFBufferAccessor(JsonElement element, GLTFBufferView[] views)
        {
            if (!element.TryGetProperty("bufferView", out var viewIdx))
            {
                throw new ModelLoadException("accessor must have bufferView");
            }

            BufferView = views[viewIdx.GetInt32()];
            if (!element.TryGetProperty("componentType", out var compType))
            {
                throw new ModelLoadException("accessor must have componentType");
            }

            if (!element.TryGetProperty("count", out var countProp))
            {
                throw new ModelLoadException("accessor must have count");
            }

            if (!element.TryGetStringProperty("type", out var typeProp))
            {
                throw new ModelLoadException("accessor must have type");
            }
            if (element.TryGetProperty("byteOffset", out var offProp))
            {
                ByteOffset = offProp.GetInt32();
            }
            if (element.TryGetProperty("normalized", out var normprop))
            {
                Normalized = normprop.GetBoolean();
            }

            Count = countProp.GetInt32();
            ComponentType = (ComponentType) compType.GetInt32();
            Type = Enum.Parse<AccessorType>(typeProp, true);
        }

        int GetNumComponents()
        {
            switch (Type)
            {
                case AccessorType.SCALAR: return 1;
                case AccessorType.VEC2: return 2;
                case AccessorType.VEC3: return 3;
                case AccessorType.VEC4: return 4;
                case AccessorType.MAT2: return 4;
                case AccessorType.MAT3: return 9;
                case AccessorType.MAT4: return 16;
            }

            throw new ModelLoadException("Invalid accessor type");
        }

        int GetComponentSize()
        {
            switch (ComponentType)
            {
                case ComponentType.Int8:
                case ComponentType.UInt8:
                    return 1;
                case ComponentType.Int16:
                case ComponentType.UInt16:
                    return 2;
                case ComponentType.UInt32:
                case ComponentType.Float:
                    return 4;
            }

            throw new ModelLoadException("Invalid accessor component type");
        }

        int GetStride()
        {
            if (BufferView.ByteStride != 0)
                return BufferView.ByteStride;
            return GetComponentSize() * GetNumComponents();
        }

        float GetComponent(int offset, int x)
        {
            var off = offset + (x * GetComponentSize());
            var buf = BufferView.Buffer.Buffer;
            if (ComponentType == ComponentType.Float)
                return BitConverter.ToSingle(buf, off);
            switch (ComponentType)
            {
                case ComponentType.Int8:
                {
                    var v = (sbyte) buf[off];
                    if (Normalized) return v / 127f;
                    else return v;
                }
                case ComponentType.UInt8:
                {
                    if (Normalized) return buf[off] / 255f;
                    return buf[off];
                }
                case ComponentType.Int16:
                {
                    var v = BitConverter.ToInt16(buf, off);
                    if (Normalized) return v / 32767f;
                    return v;
                }
                case ComponentType.UInt16:
                {
                    var v = BitConverter.ToUInt16(buf, off);
                    if (Normalized) return v / 65535f;
                    return v;
                }
                default:
                    throw new ModelLoadException("Unsupported accessor component format");
            }
        }

        public uint GetIndex(int i)
        {
            var off = ByteOffset + BufferView.ByteOffset + (GetStride() * i);
            var buf = BufferView.Buffer.Buffer;
            if (ComponentType == ComponentType.UInt16)
                return BitConverter.ToUInt16(buf, off);
            else if (ComponentType == ComponentType.UInt32)
                return BitConverter.ToUInt32(buf, off);
            throw new ModelLoadException("Indices can only be unsigned int or unsigned short");
        }

        public float GetFloat(int i)
        {
            var off = ByteOffset + BufferView.ByteOffset + (GetStride() * i);
            return GetComponent(off, 0);
        }
        public Vector2 GetVector2(int i)
        {
            var off = ByteOffset + BufferView.ByteOffset + (GetStride() * i);
            return new Vector2(GetComponent(off, 0), GetComponent(off, 1));
        }

        public Vector3 GetVector3(int i)
        {
            var off = ByteOffset + BufferView.ByteOffset + (GetStride() * i);
            return new Vector3(GetComponent(off, 0), GetComponent(off, 1), GetComponent(off, 2));
        }

        public Vector4 GetVector4(int i)
        {
            var off = ByteOffset + BufferView.ByteOffset + (GetStride() * i);
            return new Vector4(GetComponent(off, 0), GetComponent(off, 1), GetComponent(off, 2), GetComponent(off, 3));
        }

        public Point4<ushort> GetUShort4(int i)
        {
            var off = ByteOffset + BufferView.ByteOffset + (GetStride() * i);
            return new Point4<ushort>((ushort)GetComponent(off, 0), (ushort)GetComponent(off, 1), (ushort)GetComponent(off, 2),
                (ushort)GetComponent(off, 3));
        }

        public Matrix4x4 GetMatrix4x4(int i)
        {
            var off = ByteOffset + BufferView.ByteOffset + (GetStride() * i);
            return new Matrix4x4(
                GetComponent(off, 0), GetComponent(off, 1), GetComponent(off, 2), GetComponent(off, 3),
                GetComponent(off, 4), GetComponent(off, 5), GetComponent(off, 6), GetComponent(off, 7),
                GetComponent(off, 8), GetComponent(off, 9), GetComponent(off, 10), GetComponent(off, 11),
                GetComponent(off, 12), GetComponent(off, 13), GetComponent(off, 14), GetComponent(off, 15));
        }
    }

    class GLTFBufferView
    {
        public GLTFBuffer Buffer;
        public int ByteOffset;
        public int ByteLength;
        public int ByteStride;

        public GLTFBufferView(JsonElement element, GLTFBuffer[] buffers)
        {
            if (!element.TryGetProperty("buffer", out var buffProp))
                throw new ModelLoadException("bufferView must have buffer property");
            Buffer = buffers[buffProp.GetInt32()];
            if (element.TryGetProperty("byteLength", out var byteLengthProp))
                ByteLength = byteLengthProp.GetInt32();
            if (element.TryGetProperty("byteStride", out var byteStrideProp))
                ByteStride = byteStrideProp.GetInt32();
            if (element.TryGetProperty("byteOffset", out var byteOffsetProp))
                ByteOffset = byteOffsetProp.GetInt32();
        }
    }

    class GLTFBuffer
    {
        public byte[] Buffer;

        public GLTFBuffer(JsonElement element, byte[]? binchunk, IExternalResources external)
        {
            if (!element.TryGetProperty("uri", out var uriProperty))
            {
                Buffer = binchunk ?? throw new ModelLoadException("glTF buffer has no valid uri or GLB binary chunk");
            }
            else
            {
                var str = uriProperty.GetString();
                if (str == null)
                    throw new ModelLoadException("Unsupported glTF uri");
                Buffer = UriTools.BytesFromUri(str, external);
            }
        }

    }
}
