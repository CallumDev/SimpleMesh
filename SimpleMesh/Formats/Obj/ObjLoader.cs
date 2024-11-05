using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using SimpleMesh.Util;

namespace SimpleMesh.Formats.Obj
{
    public static class ObjLoader
    {
        private const int STREAMREADER_BUFFER_SIZE = 32768;
        public static Model Load(Stream stream, ModelLoadContext ctx)
        {
            using var reader = new StreamReader(stream, null, true, STREAMREADER_BUFFER_SIZE);
            int lineNo = 1;

            List<Vector3> positions = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector2> texcoords = new List<Vector2>();

            Model model = new Model();
            model.Materials = new Dictionary<string, Material>();
            var geometries = new List<Geometry>();
            ModelNode rootNode = new ModelNode();
            model.Roots = new ModelNode[] {rootNode};
            
            rootNode.Name = "default";
            ModelNode currentNode = rootNode;
            VertexBufferBuilder currentVertex = new VertexBufferBuilder();
            List<uint> currentIndices = new List<uint>();
            List<TriangleGroup> currentGroups = new List<TriangleGroup>();
            VertexAttributes attributes = VertexAttributes.Position;
            string currentMaterial = null;
            bool warnedNonPolygonal = false;
            int lastIndex = 0;

            Material GetMaterial(string name) {
                if (!model.Materials.TryGetValue(name, out Material x))
                {
                    x = new Material() {DiffuseColor = LinearColor.White, Name = name};
                    model.Materials.Add(name, x);
                }
                return x;
            }

            string src_line;
            
            Span<float> pos = stackalloc float[3];
            Span<float> norm = stackalloc float[3];
            Span<float> tex = stackalloc float[2];

            Span<ParseHelpers.SplitElement> maxFacePoints = stackalloc ParseHelpers.SplitElement[512];
            Span<ObjVertex> faceElements = stackalloc ObjVertex[512];
            Span<ObjVertex> triangulatedElements = stackalloc ObjVertex[512];

            bool isL = false, isF = false;
            
            while ((src_line = reader.ReadLine()) != null)
            {
                var ln = src_line.AsSpan().Trim();
                //skip empty line
                if(ln.IsWhiteSpace()) continue;
                ReadOnlySpan<char> param;
                if (ln[0] == '#')
                {
                    //do nothing
                }
                else if (IsParam(ln, 'v') && GetParam(ln, out param))
                {
                    if(ParseHelpers.FloatSpan(param, pos) != 3)
                        throw new ModelLoadException($"Invalid vertex element at line {lineNo}");
                    positions.Add(new Vector3(pos[0], pos[1], pos[2]));
                }
                else if (IsParam(ln, 'v', 'n') && GetParam(ln, out param))
                {
                    if(ParseHelpers.FloatSpan(param, norm) != 3)
                        throw new ModelLoadException($"Invalid normal element at line {lineNo}");
                    normals.Add(new Vector3(norm[0], norm[1], norm[2]));
                }
                else if (IsParam(ln, 'v', 't') && GetParam(ln, out param))
                {
                    if(ParseHelpers.FloatSpan(param, tex) != 2)
                        throw new ModelLoadException($"Invalid texture element at line {lineNo}");
                    texcoords.Add(new Vector2(tex[0], tex[1]));
                }
                else if (IsParam(ln, 'v', 'p'))
                {
                    throw new ModelLoadException($"Non-polygon data not supported. (Line {lineNo})");
                }
                else if (IsParam(ln ,'l') && GetParam(ln, out param))
                {
                    if(isF)
                        throw new ModelLoadException($"Object '{currentNode.Name}' contains both lines and triangles (Line {lineNo})");
                    isL = true;
                    // Lines element
                    var lCount = ParseHelpers.FixedSplit(param, maxFacePoints);
                    if (lCount < 2)
                        throw new ModelLoadException($"Bad line element at line {lineNo}");
                    if (lCount == int.MaxValue)
                        throw new ModelLoadException($"Too many elements in l at line {lineNo}");
                    for (int i = 0; i < lCount; i++)
                    {
                        faceElements[i] = ParseVertex(param.Slice(maxFacePoints[i].Start, maxFacePoints[i].Length), lineNo);
                    }
                    Span<ObjVertex> vtx = faceElements.Slice(0, lCount);
                    if(vtx.Length < 2)
                        throw new ModelLoadException($"Bad line element at line {lineNo}");
                    for (int i = 0; i < vtx.Length; i++)
                    {
                        if(!ResolveVertex(ref vtx[i], positions.Count, normals.Count, texcoords.Count))
                            throw new ModelLoadException($"Bad line element at line {lineNo}");
                    }
                    if (vtx.Length > 2) {
                        int oidx = 0;
                        for (int i = 1; i < vtx.Length - 1; i++)
                        {
                            if (oidx + 2 >= triangulatedElements.Length)
                                throw new ModelLoadException($"Too many elements in l on line {lineNo}");
                            triangulatedElements[oidx++] = vtx[i - 1];
                            triangulatedElements[oidx++] = vtx[i];
                        }
                        vtx = triangulatedElements.Slice(0, oidx);
                    }
                    for (int i = 0; i < vtx.Length; i++)
                    {
                        var ov = vtx[i];
                        var v = new Vertex() {
                            Position = positions[ov.Position], Diffuse = LinearColor.White
                        };
                        if (ov.Normal != -1) {
                            attributes |= VertexAttributes.Normal;
                            v.Normal = normals[ov.Normal];
                        }
                        if (ov.TexCoord != -1) {
                            attributes |= VertexAttributes.Texture1;
                            v.Texture1 = texcoords[ov.TexCoord];
                        }
                        currentIndices.Add((uint)currentVertex.Add(ref v));
                    }
                }
                else if (IsParam(ln, 'f') && GetParam(ln, out param))
                {
                    if (isL)
                        throw new ModelLoadException($"Object '{currentNode.Name}' contains both lines and triangles (Line {lineNo})");
                    isF = true;
                    var fCount = ParseHelpers.FixedSplit(param, maxFacePoints);
                    if(fCount < 3)
                        throw new ModelLoadException($"Bad face element at line {lineNo}");
                    if (fCount == int.MaxValue)
                        throw new ModelLoadException($"Too many elements in f at line {lineNo}");
                    for (int i = 0; i < fCount; i++)
                    {
                        faceElements[i] = ParseVertex(param.Slice(maxFacePoints[i].Start, maxFacePoints[i].Length), lineNo);
                    }
                    Span<ObjVertex> vtx = faceElements.Slice(0, fCount);
                    if(vtx.Length < 3)
                        throw new ModelLoadException($"Bad face element at line {lineNo}");
                    for (int i = 0; i < vtx.Length; i++)
                    {
                        if(!ResolveVertex(ref vtx[i], positions.Count, normals.Count, texcoords.Count))
                            throw new ModelLoadException($"Bad face element at line {lineNo}");
                    }
                    if (vtx.Length > 3) {
                        if (!warnedNonPolygonal)
                        {
                            warnedNonPolygonal = true;
                            ctx.Warn("Obj", $"File contains non-triangle polys @ {lineNo}, triangulating - assumed coplanar.");
                        }
                        int oidx = 0;
                        for (int i = 1; i < vtx.Length - 1; i++)
                        {
                            if (oidx + 3 >= triangulatedElements.Length)
                                throw new ModelLoadException($"Too many elements for face f on line {lineNo}");
                            triangulatedElements[oidx++] = vtx[0];
                            triangulatedElements[oidx++] = vtx[i];
                            triangulatedElements[oidx++] = vtx[i + 1];
                        }
                        vtx = triangulatedElements.Slice(0, oidx);
                    }
                    for (int i = 0; i < vtx.Length; i++)
                    {
                        var ov = vtx[i];
                        var v = new Vertex() {
                            Position = positions[ov.Position], Diffuse = LinearColor.White
                        };
                        if (ov.Normal != -1) {
                            attributes |= VertexAttributes.Normal;
                            v.Normal = normals[ov.Normal];
                        }
                        if (ov.TexCoord != -1) {
                            attributes |= VertexAttributes.Texture1;
                            v.Texture1 = texcoords[ov.TexCoord];
                        }
                        currentIndices.Add((uint)currentVertex.Add(ref v));
                    }
                }
                else if (IsParam(ln, 'o') && GetParam(ln, out var objname))
                {
                    if (currentIndices.Count == 0 && (currentNode == rootNode)) 
                    {
                        rootNode.Name = objname.ToString();
                    }
                    else
                    {
                        if (currentIndices.Count != 0)
                        {
                            if (lastIndex != currentIndices.Count)
                            {
                                var tg = new TriangleGroup()
                                {
                                    BaseVertex = currentVertex.BaseVertex,
                                    IndexCount = (currentIndices.Count - lastIndex),
                                    StartIndex = lastIndex,
                                    Material = GetMaterial(currentMaterial ?? "default")
                                };
                                currentGroups.Add(tg);
                            }

                            currentNode.Geometry = new Geometry();
                            currentNode.Geometry.Attributes = attributes;
                            currentNode.Geometry.Vertices = currentVertex.Vertices.ToArray();
                            currentNode.Geometry.Indices = Indices.FromBuffer(currentIndices.ToArray());
                            currentNode.Geometry.Groups = currentGroups.ToArray();
                            currentNode.Geometry.Kind = isL ? GeometryKind.Lines : GeometryKind.Triangles;
                            geometries.Add(currentNode.Geometry);
                        }
                        if (currentNode == rootNode)
                        {
                            currentNode = new ModelNode() { Name = objname.ToString() };
                        }
                        else
                        {
                            rootNode.Children.Add(currentNode);
                            currentNode = new ModelNode() { Name = objname.ToString() };
                        }
                        isL = isF = false;
                        currentVertex = new VertexBufferBuilder();
                        currentIndices = new List<uint>();
                        currentGroups = new List<TriangleGroup>();
                        attributes = VertexAttributes.Position;
                        lastIndex = 0;
                    }
                }
                else if (IsParam(ln, 'g'))
                {
                    if (lastIndex != currentIndices.Count)
                    {
                        var tg = new TriangleGroup() {
                            BaseVertex = currentVertex.BaseVertex,
                            IndexCount = (currentIndices.Count - lastIndex),
                            StartIndex = lastIndex,
                            Material = GetMaterial(currentMaterial ?? "default")
                        };
                        currentGroups.Add(tg);
                        lastIndex = currentIndices.Count;
                        currentVertex.Chunk();
                    }
                }
                else if (StartsWithOrdinal(ln, "usemtl") && GetParam(ln, out param))
                {
                    currentMaterial = param.ToString();
                }
                else if (IsParam(ln, 's') ||
                         StartsWithOrdinal(ln, "mtllib") ||
                         StartsWithOrdinal(ln, "usemap"))
                {
                    //ignore
                }
                else
                {
                    throw new ModelLoadException($"Malformed line at line {lineNo}");
                }
                lineNo++;
            }
            if (lastIndex != currentIndices.Count)
            {
                var tg = new TriangleGroup() {
                    BaseVertex = 0,
                    IndexCount = (currentIndices.Count - lastIndex),
                    StartIndex = lastIndex,
                    Material = GetMaterial(currentMaterial ?? "default")
                };
                currentGroups.Add(tg);
            }
            
            if (currentIndices.Count > 0)
            {
                currentNode.Geometry = new Geometry();
                currentNode.Geometry.Attributes = attributes;
                currentNode.Geometry.Vertices = currentVertex.Vertices.ToArray();
                currentNode.Geometry.Indices = Indices.FromBuffer(currentIndices.ToArray());
                currentNode.Geometry.Groups = currentGroups.ToArray();
                currentNode.Geometry.Kind = isL ? GeometryKind.Lines : GeometryKind.Triangles;
                geometries.Add(currentNode.Geometry);
            }
            
            if(currentNode != rootNode)
                rootNode.Children.Add(currentNode);
            model.Geometries = geometries.ToArray();
            return model;
        }
        static bool ResolveVertex(ref ObjVertex vtx, int lnV, int lnVN, int lnVT)
        {
            static bool ResolveIdx(int ln, ref int idx)
            {
                if (idx == 0) {
                    idx = -1; 
                    return true;
                }
                if (idx > 0) {
                    if (idx > ln) {
                        return false;
                    }
                    idx--;
                    return true;
                } else {
                    if (ln == 0) return false;
                    idx = ln + idx;
                    if (idx < 0) return false;
                    return true;
                }
            }
            if (!ResolveIdx(lnV, ref vtx.Position)) return false;
            if (!ResolveIdx(lnVN, ref vtx.Normal)) return false;
            if (!ResolveIdx(lnVT, ref vtx.TexCoord)) return false;
            return true;
        }

        static int IndexOrLength(ReadOnlySpan<char> x, char c) {
            var index = x.IndexOf(c);
            return index == -1 ? x.Length : index;
        }
        
        static ObjVertex ParseVertex(ReadOnlySpan<char> x, int ln)
        {
            ObjVertex vtx = new ObjVertex();
            //Position
            var index = IndexOrLength(x, '/');
            if(!int.TryParse(x.Slice(0, index), out vtx.Position))
                throw new ModelLoadException($"Bad face element at line {ln}");
            if (index == x.Length)
                return vtx;
            x = x.Slice(index + 1);
            //TexCoord
            index = IndexOrLength(x, '/');
            var e = x.Slice(0, index);
            if(e.Length != 0 && !e.IsWhiteSpace() && !int.TryParse(e, out vtx.TexCoord))
                throw new ModelLoadException($"Bad face element at line {ln}");
            if (index == x.Length)
                return vtx;
            x = x.Slice(index + 1);
            //Normal
            if (x.Length != 0 && !x.IsWhiteSpace() && !int.TryParse(x, out vtx.Normal))
                throw new ModelLoadException($"Bad face element at line {ln}");
            return vtx;
        }

        struct ObjVertex
        {
            public int Position;
            public int Normal;
            public int TexCoord;
        }

        static bool GetParam(ReadOnlySpan<char> s, out ReadOnlySpan<Char> param)
        {
            var x = s.IndexOfAny(' ', '\t');
            if (x == -1) {
                param = default;
                return false;
            }
            param = s.Slice(x + 1);
            return true;
        }

        static bool StartsWithOrdinal(ReadOnlySpan<char> s, string val) => s.StartsWith(val, StringComparison.Ordinal);

        static bool IsParam(ReadOnlySpan<char> s, char c1, char c2)
        {
            if (s.Length < 3) return false;
            return s[0] == c1 && s[1] == c2 && char.IsWhiteSpace(s[2]);
        }
        static bool IsParam(ReadOnlySpan<char> s, char c)
        {
            if (s.Length < 2) return false;
            return s[0] == c && char.IsWhiteSpace(s[1]);
        }
    }
}