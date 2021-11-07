using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using SimpleMesh.Util;

namespace SimpleMesh.Formats.Obj
{
    public static class ObjLoader
    {
        public static Model Load(Stream stream, ModelLoadContext ctx)
        {
            using var reader = new StreamReader(stream);
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
                    x = new Material() {DiffuseColor = Vector4.One, Name = name};
                    model.Materials.Add(name, x);
                }
                return x;
            }
            
            while (!reader.EndOfStream)
            {
                var ln = reader.ReadLine().Trim();
                string param;
                if (ln[0] == '#')
                {
                    //do nothing
                }
                else if (IsParam(ln, 'v') && GetParam(ln, out param))
                {
                    var pos = ParseHelpers.FloatArray(param);
                    if(pos.Length != 3)
                        throw new ModelLoadException($"Invalid vertex element at line {lineNo}");
                    positions.Add(new Vector3(pos[0], pos[1], pos[2]));
                }
                else if (IsParam(ln, 'v', 'n') && GetParam(ln, out param))
                {
                    var n = ParseHelpers.FloatArray(param);
                    if(n.Length != 3)
                        throw new ModelLoadException($"Invalid normal element at line {lineNo}");
                    normals.Add(new Vector3(n[0], n[1], n[2]));
                }
                else if (IsParam(ln, 'v', 't') && GetParam(ln, out param))
                {
                    var t = ParseHelpers.FloatArray(param);
                    if(t.Length != 2)
                        throw new ModelLoadException($"Invalid texture element at line {lineNo}");
                    texcoords.Add(new Vector2(t[0], t[1]));
                }
                else if (IsParam(ln, 'v', 'p'))
                {
                    throw new ModelLoadException($"Non-polygon data not supported. (Line {lineNo})");
                }
                else if (IsParam(ln, 'f') && GetParam(ln, out param))
                {
                    var vtx = ParseHelpers.Tokens(param).Select(x => ParseVertex(x, lineNo)).ToArray();
                    if(vtx.Length < 3)
                        throw new ModelLoadException($"Bad face element at line {ln}");
                    for (int i = 0; i < vtx.Length; i++)
                    {
                        if(!ResolveVertex(ref vtx[i], positions.Count, normals.Count, texcoords.Count))
                            throw new ModelLoadException($"Bad face element at line {ln}");
                    }
                    if (vtx.Length > 3) {
                        if (!warnedNonPolygonal)
                        {
                            warnedNonPolygonal = true;
                            ctx.Warn("Obj", $"File contains non-triangle polys @ {lineNo}, triangulating - assumed coplanar.");
                        }
                        vtx = Triangulate(vtx);
                    }
                    for (int i = 0; i < vtx.Length; i++)
                    {
                        var ov = vtx[i];
                        var v = new Vertex() {
                            Position = positions[ov.Position], Diffuse = Vector4.One
                        };
                        if (ov.Normal != 0) {
                            attributes |= VertexAttributes.Normal;
                            v.Normal = normals[ov.Normal];
                        }
                        if (ov.TexCoord != 0) {
                            attributes |= VertexAttributes.Texture1;
                            v.Texture1 = texcoords[ov.TexCoord];
                        }
                        currentIndices.Add((uint)currentVertex.Add(ref v, 0));
                    }
                }
                else if (IsParam(ln, 'o') && GetParam(ln, out string objname))
                {
                    if (currentIndices.Count == 0 && (currentNode == rootNode)) 
                    {
                        rootNode.Name = objname;
                    }
                    else
                    {
                        if (lastIndex != currentIndices.Count)
                        {
                            var tg = new TriangleGroup()
                            {
                                BaseVertex = 0,
                                IndexCount = (currentIndices.Count - lastIndex),
                                StartIndex = 0,
                                Material = GetMaterial(currentMaterial ?? "default")
                            };
                            currentGroups.Add(tg);
                        }
                        currentNode.Geometry = new Geometry();
                        currentNode.Geometry.Attributes = attributes;
                        currentNode.Geometry.Vertices = currentVertex.Vertices.ToArray();
                        currentNode.Geometry.Indices = Indices.FromBuffer(currentIndices.ToArray());
                        currentNode.Geometry.Groups = currentGroups.ToArray();
                        geometries.Add(currentNode.Geometry);
                        if (currentNode == rootNode)
                        {
                            currentNode = new ModelNode() {Name = objname};
                        }
                        else
                        {
                            rootNode.Children.Add(currentNode);
                            currentNode = new ModelNode();
                        }
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
                            BaseVertex = 0,
                            IndexCount = (currentIndices.Count - lastIndex),
                            StartIndex = lastIndex,
                            Material = GetMaterial(currentMaterial ?? "default")
                        };
                        currentGroups.Add(tg);
                        lastIndex = currentIndices.Count;
                    }
                }
                else if (StartsWithOrdinal(ln, "usemtl") && GetParam(ln, out param))
                {
                    currentMaterial = param;
                }
                else if (IsParam(ln, 's') ||
                         StartsWithOrdinal(ln, "mtllib"))
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
            currentNode.Geometry = new Geometry();
            currentNode.Geometry.Attributes = attributes;
            currentNode.Geometry.Vertices = currentVertex.Vertices.ToArray();
            currentNode.Geometry.Indices = Indices.FromBuffer(currentIndices.ToArray());
            currentNode.Geometry.Groups = currentGroups.ToArray();
            geometries.Add(currentNode.Geometry);
            if(currentNode != rootNode)
                rootNode.Children.Add(currentNode);
            model.Geometries = geometries.ToArray();
            return model;
        }

        static ObjVertex[] Triangulate(ObjVertex[] input)
        {
            var ln = new List<ObjVertex>();
            for (int i = 1; i < input.Length - 1; i++) {
                ln.Add(input[0]);
                ln.Add(input[i]);
                ln.Add(input[i + 1]);
            }
            return ln.ToArray();
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

        static ObjVertex ParseVertex(string x, int ln)
        {
            var s = x.Split('/');
            ObjVertex vtx = new ObjVertex();
            if(!int.TryParse(s[0].Trim(), out vtx.Position))
                throw new ModelLoadException($"Bad face element at line {ln}");
            if (s.Length > 1) {
                if (!string.IsNullOrWhiteSpace(s[1]))
                {
                    if (!int.TryParse(s[1], out vtx.TexCoord))
                        throw new ModelLoadException($"Bad face element at line {ln}");
                }
            }
            if (s.Length > 2 && !int.TryParse(s[2], out vtx.Normal))
            {
                throw new ModelLoadException($"Bad face element at line {ln}");
            }
            return vtx;
        }

        struct ObjVertex
        {
            public int Position;
            public int Normal;
            public int TexCoord;
        }

        static bool GetParam(string s, out string param)
        {
            var x = s.IndexOfAny(new[] { ' ', '\t' });
            if (x == -1) {
                param = null;
                return false;
            }
            param = s.Substring(x + 1);
            return true;
        }

        static bool StartsWithOrdinal(string s, string val) => s.StartsWith(val, StringComparison.Ordinal);

        static bool IsParam(string s, char c1, char c2)
        {
            if (s.Length < 3) return false;
            return s[0] == c1 && s[1] == c2 && char.IsWhiteSpace(s[2]);
        }
        static bool IsParam(string s, char c)
        {
            if (s.Length < 2) return false;
            return s[0] == c && char.IsWhiteSpace(s[1]);
        }
    }
}